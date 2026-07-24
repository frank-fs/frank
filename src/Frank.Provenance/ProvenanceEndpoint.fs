module Frank.Provenance.ProvenanceEndpoint

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives
open VDS.RDF

/// Write an RFC 9457 problem+json 404 — every not-found branch in this module uses this
/// instead of a bare status code, so a client dereferencing a provenance IRI that 404s
/// gets a machine-readable reason (Fielding review finding, gap 1).
let private notFound (ctx: HttpContext) (typeUri: string) (title: string) (detail: string) : Task =
    Frank.ProblemJson.write ctx 404 typeUri title detail

// Cheap content fingerprint over an already-scanned graph's triple string representations —
// sorted (ordinal, so no ICU/globalization dependency) for order-independence, then hashed
// via the same SHA-256 helper the rest of the codebase's ETag machinery uses. Used ONLY by
// the ETagMetadata compute closures below (#426) — never by the HTTP response path itself,
// which is now owned entirely by Frank.ConditionalRequestMiddleware.
let private graphFingerprint (tripleReprs: string list) : string =
    tripleReprs
    |> List.sort
    |> String.concat "\n"
    |> System.Text.Encoding.UTF8.GetBytes
    |> Frank.ETagFormat.computeFromBytes

/// Private ctx.Items marker keys (#426 follow-up, Fix 1): computeNodeETag/computeLineageETag
/// run BEFORE handleNode/handle on the SAME HttpContext (ConditionalRequestMiddleware calls
/// the compute closure, then next.Invoke on a cache miss) -- stashing the already-resolved
/// graph here lets handleNode/handle reuse it instead of re-running the identical store query
/// + graph build a second time. Object identity, not strings, so this can never collide with
/// ctx.Items keys set by other middleware. Only the success (graph-found) case is stashed --
/// a not-found node outcome is cheap to re-derive, so handleNode's 404 path always falls
/// through to a fresh resolveNodeGraph rather than threading NodeOutcome through ctx.Items too.
module private CtxItemKeys =
    let nodeGraph = obj ()
    let lineageGraph = obj ()

/// Single function backing every provenance JSON-LD 200 response (#424). Vary: Accept and
/// immutable Cache-Control (provenance nodes represent a historical fact and never change
/// once recorded, so the representation can be cached indefinitely) are set by
/// ProvenanceCacheHeadersMiddleware, registered OUTER to ConditionalRequestMiddleware
/// (Frank.Provenance.fs) so they are present on a 304 short-circuit too, which never
/// reaches this function (#426 fix) — this function only sets headers that DO depend on
/// the handler running. ETag computation and If-None-Match/304 short-circuiting are owned
/// entirely by Frank.ConditionalRequestMiddleware via the ETagMetadata attached to these
/// routes (see Frank.Provenance.fs) — this function is now a plain 200-body writer (#426).
let private serveJsonLd (config: ProvenanceConfig) (g: IGraph) (ctx: HttpContext) : Task =
    let body = ProvenanceGraph.compactGraph config.DeclaredPrefixes g
    ctx.Response.StatusCode <- 200
    ctx.Response.ContentType <- "application/ld+json"
    ctx.Response.WriteAsync(body)

/// Outcome of resolving a per-node graph — either the graph to serve, or the RFC 9457
/// (typeUri, title, detail) triple to 404 with. Shared by handleNode's 200 path AND
/// computeNodeETag (#426), so the two can never derive different answers for the same
/// request — there is exactly one dispatch (tryParseStateEntityKey / entity- branch /
/// index bounds / activity lookup), not two copies that can drift.
type private NodeOutcome =
    | NodeGraph of IGraph
    | NodeNotFound of typeUri: string * title: string * detail: string

let private resolveStateEntityGraph (store: IProvenanceStore) (origin: string) (stateKey: string) : Task<NodeOutcome> =
    match ProvenanceGraph.tryParseStateEntityKey stateKey with
    | None ->
        Task.FromResult(
            NodeNotFound(
                "https://frankfs.dev/problems/unknown-state-entity",
                "Unknown state entity",
                sprintf "'%s' is not a valid state entity key" stateKey
            )
        )
    | Some(resourceUri, k) ->
        task {
            let! records = store.QueryByResource resourceUri

            if k < 0 || k > records.Length then
                return
                    NodeNotFound(
                        "https://frankfs.dev/problems/state-entity-index-out-of-range",
                        "State entity index out of range",
                        sprintf
                            "state entity index %d is out of range for resource '%s' (valid range 0..%d)"
                            k
                            resourceUri
                            records.Length
                    )
            else
                return NodeGraph(ProvenanceGraph.buildStateEntityNodeGraph origin resourceUri records k)
        }

let private resolveActivityNodeGraph (store: IProvenanceStore) (origin: string) (nodeIri: string) : Task<NodeOutcome> =
    task {
        let! recordOpt = store.QueryByActivityId nodeIri

        match recordOpt with
        | None ->
            return
                NodeNotFound(
                    "https://frankfs.dev/problems/unknown-activity",
                    "Unknown activity",
                    sprintf "no provenance activity found for '%s'" nodeIri
                )
        | Some record ->
            let! allRecords = store.QueryByResource record.ResourceUri

            match allRecords |> List.tryFindIndex (fun r -> r.Id = record.Id) with
            | None ->
                return
                    NodeNotFound(
                        "https://frankfs.dev/problems/activity-not-in-lineage",
                        "Activity not found in resource lineage",
                        sprintf
                            "activity '%s' was not found in the recorded lineage for resource '%s'"
                            nodeIri
                            record.ResourceUri
                    )
            | Some posIdx -> return NodeGraph(ProvenanceGraph.buildActivityNodeGraph origin record posIdx)
    }

// Route an already-validated origin + nodeId to the correct per-node graph resolution.
// Extracted to keep handleNode/computeNodeETag ≤ 2 nesting levels (Rule 9).
let private resolveNodeGraph (store: IProvenanceStore) (origin: string) (nodeId: string) : Task<NodeOutcome> =
    if nodeId.StartsWith "entity-" then
        resolveStateEntityGraph store origin (nodeId.Substring 7)
    else
        resolveActivityNodeGraph store origin (sprintf "%s/provenance/%s" origin nodeId)

/// Resolves the node outcome handleNode's 200/404 path serves -- reusing the graph
/// computeNodeETag already stashed on ctx.Items for THIS request (Fix 1, #426 follow-up)
/// when present, falling back to a fresh resolveNodeGraph otherwise (e.g. a direct handleNode
/// call that never went through ConditionalRequestMiddleware, or a 404 that was never stashed).
let private resolveNodeGraphForRequest
    (store: IProvenanceStore)
    (ctx: HttpContext)
    (origin: string)
    (nodeId: string)
    : Task<NodeOutcome> =
    match ctx.Items.TryGetValue CtxItemKeys.nodeGraph with
    | true, (:? IGraph as g) -> Task.FromResult(NodeGraph g)
    | _ -> resolveNodeGraph store origin nodeId

/// Extracts the nodeId route value the same way handleNode does — used by the ETagMetadata
/// attached to the per-node route (Frank.Provenance.fs) so the instance id it resolves can
/// never diverge from what handleNode itself resolves (#426).
let resolveNodeId (ctx: HttpContext) : string =
    match ctx.Request.RouteValues.TryGetValue "nodeId" with
    | true, v when not (isNull v) -> v :?> string
    | _ -> ""

/// Computes an ETag for a provenance node by re-running the SAME node-resolution
/// (resolveNodeGraph) that handleNode's 200 path uses — attached as ETagMetadata.Compute
/// on the per-node route (Frank.Provenance.fs) so ConditionalRequestMiddleware's 304
/// short-circuit can never drift from what the handler would actually serve (#426).
/// Returns the raw (unquoted) ETag value.
let computeNodeETag (store: IProvenanceStore) (etagContext: Frank.ETagContext) : Task<string option> =
    let nodeId = etagContext.InstanceId

    if System.String.IsNullOrEmpty nodeId then
        Task.FromResult None
    else
        match Frank.OriginValidation.tryValidateOrigin etagContext.HttpContext.Request with
        | None -> Task.FromResult None
        | Some origin ->
            task {
                let! outcome = resolveNodeGraph store origin nodeId

                match outcome with
                | NodeNotFound _ -> return None
                | NodeGraph g ->
                    etagContext.HttpContext.Items.[CtxItemKeys.nodeGraph] <- box g
                    let _, tripleReprs = ProvenanceGraph.scanTriples g
                    return Some(graphFingerprint tripleReprs)
            }

/// Resolves (origin, resolvedResource) for the lineage batch document from the request's
/// `resource` query parameter, or None if the parameter is missing / the Host header is
/// malformed. Shared by `handle` and `computeLineageETag` (#426).
let private resolveLineageQuery (ctx: HttpContext) : (string * string) option =
    let resource = ctx.Request.Query.["resource"]

    if StringValues.IsNullOrEmpty resource then
        None
    else
        Frank.OriginValidation.tryValidateOrigin ctx.Request
        |> Option.map (fun origin ->
            let rawResource = resource.ToString()

            let resolvedResource =
                if rawResource.StartsWith("/") then
                    origin + rawResource
                else
                    rawResource

            origin, resolvedResource)

/// Builds the lineage graph for a resolved resource URI — the SAME store query and graph
/// builder call `handle`'s 200 path and `computeLineageETag` both use (#426).
let private resolveLineageGraph (store: IProvenanceStore) (origin: string) (resolvedResource: string) : Task<IGraph> =
    task {
        let! records = store.QueryByResource resolvedResource
        return ProvenanceGraph.buildLineageGraph origin resolvedResource records
    }

/// Resolves the lineage graph `handle`'s 200 path serves -- reusing the graph
/// computeLineageETag already stashed on ctx.Items for THIS request (Fix 1, #426 follow-up)
/// when present, falling back to a fresh resolveLineageQuery/resolveLineageGraph otherwise
/// (e.g. a direct `handle` call that never went through ConditionalRequestMiddleware). Returns
/// None only when resolveLineageQuery itself fails (malformed Host) -- `handle` has already
/// confirmed the 'resource' parameter is present before calling this.
let private resolveLineageGraphForRequest (store: IProvenanceStore) (ctx: HttpContext) : Task<IGraph option> =
    match ctx.Items.TryGetValue CtxItemKeys.lineageGraph with
    | true, (:? IGraph as g) -> Task.FromResult(Some g)
    | _ ->
        match resolveLineageQuery ctx with
        | None -> Task.FromResult None
        | Some(origin, resolvedResource) ->
            task {
                let! g = resolveLineageGraph store origin resolvedResource
                return Some g
            }

/// Computes an ETag for the lineage batch document by re-running the SAME resolution
/// (resolveLineageQuery/resolveLineageGraph) that `handle`'s 200 path uses — attached as
/// ETagMetadata.Compute on the batch route (Frank.Provenance.fs) (#426). Returns the raw
/// (unquoted) ETag value.
let computeLineageETag (store: IProvenanceStore) (etagContext: Frank.ETagContext) : Task<string option> =
    match resolveLineageQuery etagContext.HttpContext with
    | None -> Task.FromResult None
    | Some(origin, resolvedResource) ->
        task {
            let! g = resolveLineageGraph store origin resolvedResource
            etagContext.HttpContext.Items.[CtxItemKeys.lineageGraph] <- box g
            let _, tripleReprs = ProvenanceGraph.scanTriples g
            return Some(graphFingerprint tripleReprs)
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
        task {
            let! graphOpt = resolveLineageGraphForRequest store ctx

            match graphOpt with
            | None -> ctx.Response.StatusCode <- 400
            | Some g -> do! serveJsonLd config g ctx
        }

/// GET /provenance/{nodeId} — return a focused graph for a single activity or state entity.
/// nodeId starting with "entity-" is a state entity (base64url-encoded resourceUri|k).
/// Any other nodeId is treated as an activity IRI suffix.
let handleNode (store: IProvenanceStore) (config: ProvenanceConfig) (ctx: HttpContext) : Task =
    if isNull (box store) then
        invalidArg (nameof store) "store must not be null"

    if isNull ctx then
        invalidArg (nameof ctx) "HttpContext must not be null"

    let nodeId = resolveNodeId ctx

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
        | Some origin ->
            task {
                let! outcome = resolveNodeGraphForRequest store ctx origin nodeId

                match outcome with
                | NodeNotFound(typeUri, title, detail) -> do! notFound ctx typeUri title detail
                | NodeGraph g -> do! serveJsonLd config g ctx
            }
