module Frank.Provenance.ProvenanceEndpoint

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives

let private serveJsonLd (config: ProvenanceConfig) (g: VDS.RDF.IGraph) (ctx: HttpContext) : Task =
    let extraCtx = ProvenanceGraph.usedPrefixContext config.DeclaredPrefixes g
    ctx.Response.StatusCode <- 200
    ctx.Response.ContentType <- "application/ld+json"
    ctx.Response.WriteAsync(ProvenanceGraph.compactGraph extraCtx g)

let private handleStateEntity
    (store: IProvenanceStore)
    (config: ProvenanceConfig)
    (origin: string)
    (stateKey: string)
    (ctx: HttpContext)
    : Task =
    match ProvenanceGraph.tryParseStateEntityKey stateKey with
    | None ->
        ctx.Response.StatusCode <- 404
        Task.CompletedTask
    | Some(resourceUri, k) ->
        task {
            let! records = store.QueryByResource resourceUri

            if k < 0 || k > records.Length then
                ctx.Response.StatusCode <- 404
            else
                let g = ProvenanceGraph.buildStateEntityNodeGraph origin resourceUri records k
                do! serveJsonLd config g ctx
        }

let private handleActivityNode
    (store: IProvenanceStore)
    (config: ProvenanceConfig)
    (origin: string)
    (nodeIri: string)
    (ctx: HttpContext)
    : Task =
    task {
        let! recordOpt = store.QueryByActivityId nodeIri

        match recordOpt with
        | None -> ctx.Response.StatusCode <- 404
        | Some record ->
            let! allRecords = store.QueryByResource record.ResourceUri

            match allRecords |> List.tryFindIndex (fun r -> r.Id = record.Id) with
            | None -> ctx.Response.StatusCode <- 404
            | Some posIdx ->
                let g = ProvenanceGraph.buildActivityNodeGraph origin record posIdx
                do! serveJsonLd config g ctx
    }

/// GET /provenance?resource=<uri> — return the full lineage batch document.
let handle (store: IProvenanceStore) (config: ProvenanceConfig) (ctx: HttpContext) : Task =
    if isNull (box store) then
        invalidArg (nameof store) "store must not be null"

    if isNull ctx then
        invalidArg (nameof ctx) "HttpContext must not be null"

    let resource = ctx.Request.Query.["resource"]

    if StringValues.IsNullOrEmpty resource then
        Frank.ProblemJson.write
            ctx
            400
            "https://frankfs.dev/problems/missing-parameter"
            "Missing required query parameter"
            "provenance query requires a 'resource' parameter"
    else
        match Frank.OriginValidation.tryValidateOrigin ctx.Request with
        | None ->
            ctx.Response.StatusCode <- 400
            Task.CompletedTask
        | Some origin ->
            task {
                let rawResource = resource.ToString()

                let resolvedResource =
                    if rawResource.StartsWith("/") then
                        origin + rawResource
                    else
                        rawResource

                let! records = store.QueryByResource(resolvedResource)
                let g = ProvenanceGraph.buildLineageGraph origin resolvedResource records
                do! serveJsonLd config g ctx
            }

// Route an already-validated origin + nodeId to the correct per-node handler.
// Extracted to keep handleNode ≤ 2 nesting levels (Rule 9).
let private dispatchNode
    (store: IProvenanceStore)
    (config: ProvenanceConfig)
    (origin: string)
    (nodeId: string)
    (ctx: HttpContext)
    : Task =
    if nodeId.StartsWith "entity-" then
        handleStateEntity store config origin (nodeId.Substring 7) ctx
    else
        handleActivityNode store config origin (sprintf "%s/provenance/%s" origin nodeId) ctx

/// GET /provenance/{nodeId} — return a focused graph for a single activity or state entity.
/// nodeId starting with "entity-" is a state entity (base64url-encoded resourceUri|k).
/// Any other nodeId is treated as an activity IRI suffix.
let handleNode (store: IProvenanceStore) (config: ProvenanceConfig) (ctx: HttpContext) : Task =
    if isNull (box store) then
        invalidArg (nameof store) "store must not be null"

    if isNull ctx then
        invalidArg (nameof ctx) "HttpContext must not be null"

    let nodeId =
        match ctx.Request.RouteValues.TryGetValue "nodeId" with
        | true, v when not (isNull v) -> v :?> string
        | _ -> ""

    if System.String.IsNullOrEmpty nodeId then
        ctx.Response.StatusCode <- 404
        Task.CompletedTask
    else
        match Frank.OriginValidation.tryValidateOrigin ctx.Request with
        | None ->
            ctx.Response.StatusCode <- 400
            Task.CompletedTask
        | Some origin -> dispatchNode store config origin nodeId ctx
