module Frank.Provenance.ProvenanceEndpoint

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives

/// Write an RFC 9457 problem+json 404 — every not-found branch in this module uses this
/// instead of a bare status code, so a client dereferencing a provenance IRI that 404s
/// gets a machine-readable reason (Fielding review finding, gap 1).
let private notFound (ctx: HttpContext) (typeUri: string) (title: string) (detail: string) : Task =
    Frank.ProblemJson.write ctx 404 typeUri title detail

// Cheap content fingerprint over an already-scanned graph's triple string representations —
// sorted (ordinal, so no ICU/globalization dependency) for order-independence, then hashed
// via the same SHA-256 helper the rest of the codebase's ETag machinery uses. `tripleReprs`
// comes from ProvenanceGraph.scanTriples, the SAME single g.Triples walk serveJsonLd also
// uses (on the 200 path) to filter @context prefixes — so a cache miss pays one graph walk,
// not a fingerprint walk plus compactGraph's own separate one. Sorting is O(n log n)
// comparisons, not linear, but is negligible at these graph sizes (hundreds of triples).
// Fingerprint stability across identical requests only requires the SAME (config, records)
// to build the SAME triples, which every ProvenanceGraph builder already guarantees
// deterministically.
let private graphFingerprint (tripleReprs: string list) : string =
    tripleReprs
    |> List.sort
    |> String.concat "\n"
    |> System.Text.Encoding.UTF8.GetBytes
    |> Frank.ETagFormat.computeFromBytes

/// Single function backing every provenance JSON-LD 200 response (#424). Adds Vary: Accept
/// (gap 2) and a content-derived ETag + immutable Cache-Control (gap 3) — provenance nodes
/// represent a historical fact and never change once recorded, so the representation can be
/// cached indefinitely and round-tripped via If-None-Match.
///
/// Deliberately inline rather than routed through Frank.ConditionalRequestMiddleware +
/// IETagProviderFactory (src/Frank/ConditionalRequestMiddleware.fs), even though that
/// mechanism already short-circuits to 304 before the handler runs: IETagProvider.ComputeETag
/// only receives an opaque `instanceId: string` (from ETagMetadata.ResolveInstanceId, which
/// only sees HttpContext) — it has no access to the graph the handler is about to build. To
/// compute a matching ETag independently, a provider would have to re-derive the SAME
/// resource/nodeId decoding this module already does (tryParseStateEntityKey, the "entity-"
/// dispatch, origin resolution) and re-run the SAME store queries and SAME graph-build calls
/// as handle/handleStateEntity/handleActivityNode — i.e. duplicate this module's dispatch
/// logic into a second copy, with the attendant risk that the two copies drift and the
/// provider's ETag stops matching what the handler would actually serve. Computing the
/// fingerprint from the SAME `g: IGraph` the handler just built, in the SAME function that
/// serves it, makes that drift structurally impossible — the tradeoff standard
/// ConditionalRequestMiddleware makes (provider is authoritative, independent of the handler)
/// doesn't hold here without re-solving the whole dispatch problem a second time.
let private serveJsonLd (config: ProvenanceConfig) (g: VDS.RDF.IGraph) (ctx: HttpContext) : Task =
    // Single triple walk, shared by the fingerprint (always paid) and, only on cache-miss,
    // compactGraph's @context filtering — instead of each independently re-scanning g.Triples.
    let uris, tripleReprs = ProvenanceGraph.scanTriples g
    let etag = Frank.ETagFormat.quote (graphFingerprint tripleReprs)

    Frank.AcceptNegotiation.appendVaryAccept ctx.Response
    ctx.Response.Headers.ETag <- etag
    ctx.Response.Headers.CacheControl <- "max-age=31536000, immutable"

    let ifNoneMatch = ctx.Request.Headers.IfNoneMatch.ToString()

    if
        not (System.String.IsNullOrEmpty ifNoneMatch)
        && Frank.ETagComparison.anyMatch (Some etag) ifNoneMatch
    then
        // 304 short-circuit BEFORE compactGraph — the expensive step (JSON-LD expand→compact)
        // never runs when the client already has this representation. Only the shared triple
        // walk above was paid for.
        ctx.Response.StatusCode <- 304
        Task.CompletedTask
    else
        let body = ProvenanceGraph.compactGraphWithUris config.DeclaredPrefixes g uris
        ctx.Response.StatusCode <- 200
        ctx.Response.ContentType <- "application/ld+json"
        ctx.Response.WriteAsync(body)

let private handleStateEntity
    (store: IProvenanceStore)
    (config: ProvenanceConfig)
    (origin: string)
    (stateKey: string)
    (ctx: HttpContext)
    : Task =
    match ProvenanceGraph.tryParseStateEntityKey stateKey with
    | None ->
        notFound
            ctx
            "https://frankfs.dev/problems/unknown-state-entity"
            "Unknown state entity"
            (sprintf "'%s' is not a valid state entity key" stateKey)
    | Some(resourceUri, k) ->
        task {
            let! records = store.QueryByResource resourceUri

            if k < 0 || k > records.Length then
                do!
                    notFound
                        ctx
                        "https://frankfs.dev/problems/state-entity-index-out-of-range"
                        "State entity index out of range"
                        (sprintf
                            "state entity index %d is out of range for resource '%s' (valid range 0..%d)"
                            k
                            resourceUri
                            records.Length)
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
        | None ->
            do!
                notFound
                    ctx
                    "https://frankfs.dev/problems/unknown-activity"
                    "Unknown activity"
                    (sprintf "no provenance activity found for '%s'" nodeIri)
        | Some record ->
            let! allRecords = store.QueryByResource record.ResourceUri

            match allRecords |> List.tryFindIndex (fun r -> r.Id = record.Id) with
            | None ->
                do!
                    notFound
                        ctx
                        "https://frankfs.dev/problems/activity-not-in-lineage"
                        "Activity not found in resource lineage"
                        (sprintf
                            "activity '%s' was not found in the recorded lineage for resource '%s'"
                            nodeIri
                            record.ResourceUri)
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
        notFound
            ctx
            "https://frankfs.dev/problems/missing-node-id"
            "Missing node identifier"
            "the request path did not include a provenance node identifier"
    else
        match Frank.OriginValidation.tryValidateOrigin ctx.Request with
        | None ->
            ctx.Response.StatusCode <- 400
            Task.CompletedTask
        | Some origin -> dispatchNode store config origin nodeId ctx
