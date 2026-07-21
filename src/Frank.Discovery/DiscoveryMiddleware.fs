module Frank.Discovery.DiscoveryMiddleware

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Template
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Primitives

/// Filter an EndpointDataSource down to its RouteEndpoints — the one-time cast/filter
/// step shared by every endpoint-metadata scan in this file.
let private scanRouteEndpoints (dataSource: EndpointDataSource) : RouteEndpoint seq =
    dataSource.Endpoints
    |> Seq.choose (fun ep ->
        match ep with
        | :? RouteEndpoint as re -> Some re
        | _ -> None)

/// HTTP methods declared on an endpoint via HttpMethodMetadata, or None when absent — the
/// GetMetadata<HttpMethodMetadata>() → null-check → .HttpMethods idiom shared by every
/// endpoint-metadata scan in this file (#398 /simplify item 2).
let private httpMethodsOf (re: RouteEndpoint) : string list option =
    match re.Metadata.GetMetadata<HttpMethodMetadata>() with
    | null -> None
    | meta -> Some(meta.HttpMethods |> Seq.toList)

/// Declared relation IRI on an endpoint via ResourceRelationMetadata, or None when absent —
/// the GetMetadata<ResourceRelationMetadata>() |> box → null-check → unbox → .Relation idiom
/// shared by every endpoint-metadata scan in this file (#398 /simplify item 2).
/// ResourceRelationMetadata is an F# record — it doesn't support the `null` pattern
/// directly, so box it first.
let private relationOf (re: RouteEndpoint) : string option =
    match re.Metadata.GetMetadata<ResourceRelationMetadata>() |> box with
    | null -> None
    | relBox -> Some((unbox<ResourceRelationMetadata> relBox).Relation)

/// Accepted request CLR type full name declared on an endpoint via IAcceptsMetadata,
/// normalized via Frank.ClrTypeName so a module-nested/generic request type correlates
/// against codegen's FCS-derived RequestClrTypeName — or None when absent/untyped.
let private acceptedRequestTypeOf (re: RouteEndpoint) : string option =
    match re.Metadata.GetMetadata<IAcceptsMetadata>() with
    | null -> None
    | meta ->
        match meta.RequestType with
        | null -> None
        | t -> Some(Frank.ClrTypeName.normalizeFullName t.FullName)

/// Top-level class descriptors' ClassIri → Href, e.g. "https://tictactoe.invalid/ex#Game"
/// → "/ex#Game" — DiscoveryEmitter already computed this host-relative-for-declared-only
/// href (EmitterShared.hrefFor) once at codegen time. Only descriptors carrying BOTH a
/// ClassIri and an Href contribute (field/case children never carry ClassIri).
let internal classIriHrefMap (descriptors: AlpsDescriptor list) : Map<string, string> =
    descriptors
    |> List.choose (fun d ->
        match d.ClassIri, d.Href with
        | Some c, Some h -> Some(c, h)
        | _ -> None)
    |> Map.ofList

/// Build JSON Home resource entries from live endpoints.
/// Endpoints carrying ResourceRelationMetadata contribute to one merged entry per
/// (Relation, Href) pair — a resource with both GET and POST produces a single entry
/// with allow ⊇ {GET, HEAD, OPTIONS, POST}. HEAD is added when GET is present;
/// OPTIONS is always added (RFC 7231 §7.4.1).
/// Returns a list that may contain multiple entries with the same Relation when two
/// distinct hrefs share a relation IRI; caller is responsible for deduplication.
/// resourceHrefVars maps each relation IRI to its template-variable meaning IRIs.
/// classIriToHref (#415) resolves the SERVED resource key: a relation whose class is a
/// declared-only/owned prefix (EmitterShared.declaredOnlyBases, #396) is served as its
/// own AlpsDescriptor's host-relative Href — never the un-relativized identity key
/// (which stays the correlation-key contract's absolute form, unchanged, #397/#398/#411)
/// — so a placeholder domain nobody serves never leaks onto the wire as a JSON Home
/// resource key. A relation with no matching descriptor (e.g. a route whose class was
/// never itself emitted as a top-level ALPS descriptor) falls back to the raw relation,
/// preserving prior behavior.
let homeResourcesFromEndpoints
    (resourceHrefVars: Map<string, Map<string, string>>)
    (classIriToHref: Map<string, string>)
    (dataSource: EndpointDataSource)
    : JsonHomeResource list =
    let addHead (methods: string list) =
        if List.contains "GET" methods && not (List.contains "HEAD" methods) then
            "HEAD" :: methods
        else
            methods

    let addOptions (methods: string list) =
        if not (List.contains "OPTIONS" methods) then
            "OPTIONS" :: methods
        else
            methods

    scanRouteEndpoints dataSource
    |> Seq.choose (fun re ->
        match relationOf re, httpMethodsOf re with
        | Some relation, Some methods -> Some(relation, re.RoutePattern.RawText, methods)
        | _ -> None)
    |> Seq.groupBy (fun (relation, href, _) -> (relation, href))
    |> Seq.map (fun ((relation, href), entries) ->
        let allMethods =
            entries
            |> Seq.collect (fun (_, _, methods) -> methods)
            |> Seq.distinct
            |> Seq.toList
            |> addHead
            |> addOptions
            |> List.sort

        let varMeanings =
            resourceHrefVars |> Map.tryFind relation |> Option.defaultValue Map.empty

        let servedRelation =
            classIriToHref |> Map.tryFind relation |> Option.defaultValue relation

        { Relation = servedRelation
          Href = href
          Allow = allMethods
          HrefVars = varMeanings })
    |> Seq.toList

// ── #411: ALPS Type reconciliation against real registered HTTP methods ──────
// Codegen (DiscoveryEmitter) bakes a Type fallback from lock-file Rt presence — it
// cannot see the app's actual `resource { get/post/... }` registrations. Here, where
// the ALPS profile is served, the served Type is reconciled against ground truth,
// never left to a lock-file guess (#397).
//
// The ground truth is read directly from Frank's own composed Endpoint[] — the SAME
// RouteEndpoint instances ResourceEndpointDataSource wraps and WebHostBuilder.Run
// registers as a narrowly-typed DI singleton at Run()-time, after the whole webHost CE
// block has finished composing (#411). No ApiExplorer/reflection walk, no
// Microsoft.AspNetCore.OpenApi dependency, and no risk of ASP.NET Core's internal
// ApiDescription machinery silently excluding an endpoint (e.g. one lacking a
// MethodInfo) — Endpoint.Metadata is read the same way handleOptions/
// homeResourcesFromEndpoints already read it above, just for a different metadata pair
// (IAcceptsMetadata/ResourceRelationMetadata → HTTP method).
//
// The narrow ResourceEndpointDataSource (Frank-only endpoints, injected into
// DiscoveryMiddleware separately from the generic EndpointDataSource used for Allow) and
// the generic EndpointDataSource used for Allow/OPTIONS above are deliberately different
// sources by design, not a drift risk to detect: the generic source may also carry
// non-Frank endpoints (any app-registered route sharing a path), which Allow legitimately
// wants and ALPS Type correlation does not.

/// One fold over every live RouteEndpoint, reading each's metadata once and accumulating
/// BOTH correlation keys simultaneously — relation IRI (from ResourceRelationMetadata)
/// and accepted request CLR type full name (from IAcceptsMetadata, normalized via
/// Frank.ClrTypeName). Grouping into the two final maps happens once, at the end, from
/// the accumulated pairs — used both by methodsByRelation/methodsByRequestType below
/// (each still a single walk when called alone, for isolated testability) and by
/// cachedAlpsDescriptors's lazy, the only call site that needs both maps together.
let private correlateMethodsByRelationAndRequestType
    (dataSource: EndpointDataSource)
    : Map<string, Set<string>> * Map<string, Set<string>> =
    // Flattened to (endpoint, method) pairs FIRST — keeps the accumulation loop below to a
    // single nesting level instead of a per-endpoint inner loop over its methods (Holzmann 9).
    let endpointMethodPairs =
        scanRouteEndpoints dataSource
        |> Seq.collect (fun re -> httpMethodsOf re |> Option.defaultValue [] |> List.map (fun m -> re, m))

    let byRelation = ResizeArray<string * string>()
    let byRequestType = ResizeArray<string * string>()

    for re, m in endpointMethodPairs do
        relationOf re |> Option.iter (fun r -> byRelation.Add(r, m))
        acceptedRequestTypeOf re |> Option.iter (fun t -> byRequestType.Add(t, m))

    let toMethodsMap (pairs: ResizeArray<string * string>) =
        pairs
        |> Seq.groupBy fst
        |> Seq.map (fun (key, entries) -> key, entries |> Seq.map snd |> Set.ofSeq)
        |> Map.ofSeq

    toMethodsMap byRelation, toMethodsMap byRequestType

/// Real HTTP methods per relation IRI, from live endpoints' ResourceRelationMetadata.
/// Coarse correlation key: one `resource { relation X; ... }` block stamps the SAME
/// relation on every verb it registers, so a route serving both GET and POST under one
/// relation (#390) yields a multi-method set here.
let internal methodsByRelation (dataSource: EndpointDataSource) : Map<string, Set<string>> =
    correlateMethodsByRelationAndRequestType dataSource |> fst

/// Real HTTP methods per accepted request CLR type full name, from live endpoints'
/// IAcceptsMetadata. Precise correlation key: Frank.OpenApi's `accepts` operation is
/// stamped only on the endpoint whose own HttpMethodMetadata matches
/// (ResourceBuilderExtensions.addHandlerDefinition), so this disambiguates an action's
/// real method even when its route also serves other verbs (e.g. POST /games/{id}
/// accepting MoveRequest on a route that also serves GET for Game). The map key is
/// normalized via Frank.ClrTypeName.normalizeFullName so a module-nested/generic request
/// type correlates against codegen's FCS-derived RequestClrTypeName.
let internal methodsByRequestType (dataSource: EndpointDataSource) : Map<string, Set<string>> =
    correlateMethodsByRelationAndRequestType dataSource |> snd

/// ALPS §2.2 transition semantics from a resource's real registered HTTP method(s).
/// GET present (however else the route is used) is safe; exactly {PUT} or {DELETE} is
/// idempotent; exactly {POST} is unsafe. Anything else (no live match, or an otherwise
/// ambiguous multi-write verb combination) returns None — the codegen-emitted Type is
/// left as the fallback, never guessed.
let internal alpsTypeForMethods (methods: Set<string>) : string option =
    if Set.contains "GET" methods then
        Some "safe"
    elif methods = Set.singleton "PUT" || methods = Set.singleton "DELETE" then
        Some "idempotent"
    elif methods = Set.singleton "POST" then
        Some "unsafe"
    else
        None

/// Resolve a served descriptor Href/Rt string against a pre-parsed live request origin
/// Uri (#398). The absolute-vs-relative rule itself is Frank.UriResolution.resolveAgainst —
/// the ONE place both this module and Frank.LinkedData.Ontology.resolveAbsolute apply it
/// (#398 /simplify item 1); this function only handles the string↔Uri conversion at its
/// own boundary. Internal: callers resolving many descriptors in one request should parse
/// `origin` once (see resolveDescriptorHrefsAgainst / DiscoveryMiddleware.handleAlpsProfile)
/// rather than re-parse it at every leaf (#398 /simplify items 4-5).
let private resolveHrefAgainst (baseUri: Uri) (href: string) : string =
    (Frank.UriResolution.resolveAgainst baseUri (Uri(href, UriKind.RelativeOrAbsolute))).AbsoluteUri

/// Resolve a served descriptor Href against a live request origin (#398). A relative
/// value (e.g. "/tictactoe#square", emitted for the app's own declared-only vocabulary —
/// see EmitterShared.hrefFor) becomes absolute against origin; an already-absolute value
/// (external vocab, e.g. https://schema.org/Game) passes through unchanged — RFC 3986
/// §5.3 reference resolution (Frank.UriResolution.resolveAgainst), the same rule
/// LinkedDataMiddleware's per-request term resolution already applies (#396).
/// Public: shared with app code that must resolve the SAME codegen-emitted href against
/// a live request origin outside the middleware (e.g. a POST handler decoding a JSON-LD
/// body whose keys are the client-observed, already-resolved IRIs) — never reimplemented.
let resolveHref (origin: string) (href: string) : string = resolveHrefAgainst (Uri origin) href

/// Origin-keyed build-once-per-distinct-origin memoization, shared by
/// resolvedAlpsCache/cachedResolvedAlps and resolvedHomeResourcesCache/
/// cachedResolvedHomeResources below (#398 /simplify item 6 was duplicated verbatim
/// between the two; this is the single extraction). `cache`/`onBuild` are owned by the
/// calling DiscoveryMiddleware instance so this function stays free of module-level
/// mutable state (Rule 13) — the caller supplies its own dictionary and build-count
/// callback. The `Lazy` value under each key guarantees `build` runs at most once per
/// origin, even if two requests race on a brand-new origin simultaneously. Mirrors
/// ValidationMiddleware's getOrBuildShapesGraph shape (src/Frank.Validation/
/// ValidationMiddleware.fs).
let private getOrBuildByOrigin
    (cache: ConcurrentDictionary<string, Lazy<'T>>)
    (onBuild: unit -> unit)
    (origin: string)
    (build: unit -> 'T)
    : 'T =
    cache
        .GetOrAdd(
            origin,
            (fun _ ->
                Lazy<'T>(fun () ->
                    onBuild ()
                    build ()))
        )
        .Value

/// Resolve every Href/Rt in a descriptor tree (top-level and nested children) against a
/// pre-parsed live request origin Uri (#398) — parsed ONCE by the caller and threaded
/// through the whole recursive walk, not re-parsed at every leaf (#398 /simplify items
/// 4-5). Rt is resolved alongside Href so an internal `rt` reference to another
/// descriptor's (now-absolute) href stays self-consistent within the served document.
let rec private resolveDescriptorHrefsAgainst (baseUri: Uri) (d: AlpsDescriptor) : AlpsDescriptor =
    { d with
        Href = d.Href |> Option.map (resolveHrefAgainst baseUri)
        Rt = d.Rt |> Option.map (resolveHrefAgainst baseUri)
        Descriptors = d.Descriptors |> List.map (resolveDescriptorHrefsAgainst baseUri) }

/// Resolve a served JSON Home resource's Relation (RFC 8288 §2.1 link-relation-type IRI,
/// the `resources` object's key) and HrefVars VALUES (json-home draft §4.2 "meaning"
/// IRIs) against a pre-parsed live request origin Uri — the same rule
/// resolveDescriptorHrefsAgainst already applies to ALPS Href/Rt. HrefVars KEYS are
/// template variable names (e.g. "id"), never IRIs, so only the map's values are resolved.
/// Href/Allow are deliberately left untouched: Href is a route template/resource location,
/// legitimately relative per the json-home spec — resolved by the consuming client against
/// the document's own URL, same as any relative link on a web page — not the identity key
/// this closes.
let private resolveJsonHomeResourceAgainst (baseUri: Uri) (r: JsonHomeResource) : JsonHomeResource =
    { r with
        Relation = resolveHrefAgainst baseUri r.Relation
        HrefVars = r.HrefVars |> Map.map (fun _ meaning -> resolveHrefAgainst baseUri meaning) }

/// Reconcile codegen-emitted ALPS Type against real registered HTTP methods (#397).
/// Tries the precise per-verb signal first (RequestClrTypeName via IAcceptsMetadata —
/// disambiguates an action sharing a route with other verbs), then falls back to the
/// coarser per-route signal (ClassIri via ResourceRelationMetadata). A descriptor with
/// neither signal resolvable (e.g. a pure embedded/outcome type never itself routed)
/// keeps its codegen default untouched.
let internal reconcileAlpsTypes
    (methodsByRel: Map<string, Set<string>>)
    (methodsByType: Map<string, Set<string>>)
    (descriptors: AlpsDescriptor list)
    : AlpsDescriptor list =
    let rec reconcile (d: AlpsDescriptor) =
        let byType =
            d.RequestClrTypeName
            |> Option.bind (fun t -> Map.tryFind t methodsByType)
            |> Option.bind alpsTypeForMethods

        let byRelation =
            d.ClassIri
            |> Option.bind (fun c -> Map.tryFind c methodsByRel)
            |> Option.bind alpsTypeForMethods

        let resolved = byType |> Option.orElse byRelation

        { d with
            Type = resolved |> Option.defaultValue d.Type
            Descriptors = d.Descriptors |> List.map reconcile }

    descriptors |> List.map reconcile

// ── #398: per-request path matching shared by Allow and rel="type" scoping ───

/// RouteEndpoints whose route template matches the given raw request path, regardless
/// of the HTTP method each declares — the one-time template-match step shared by
/// methodsForPath and relationsForPath. TemplateMatcher is not thread-safe — a fresh
/// instance is constructed per candidate route, mirroring the prior inline usage.
let private endpointsForPath (dataSource: EndpointDataSource) (requestPath: string) : RouteEndpoint list =
    let pathString = PathString(requestPath)

    scanRouteEndpoints dataSource
    |> Seq.filter (fun re ->
        let raw = re.RoutePattern.RawText
        let pattern = if raw.StartsWith('/') then raw.TrimStart('/') else raw
        let matcher = TemplateMatcher(TemplateParser.Parse(pattern), RouteValueDictionary())
        matcher.TryMatch(pathString, RouteValueDictionary()))
    |> Seq.toList

/// Real HTTP methods registered for the given request path, from every endpoint whose
/// route template matches — the OPTIONS Allow header's source of truth.
let internal methodsForPath (dataSource: EndpointDataSource) (requestPath: string) : string list =
    endpointsForPath dataSource requestPath
    |> List.choose httpMethodsOf
    |> List.collect id
    |> List.distinct

/// Declared relation IRI(s) for the given request path, from ResourceRelationMetadata on
/// every endpoint whose route template matches. Used to scope rel="type" Link headers to
/// only the resource actually matched (#398) — a route carrying no relation (e.g. "/",
/// "/tictactoe") yields an empty list, and every codegen-emitted DescribedByLink for
/// OTHER resources is withheld, not broadcast.
let internal relationsForPath (dataSource: EndpointDataSource) (requestPath: string) : string list =
    endpointsForPath dataSource requestPath
    |> List.choose relationOf
    |> List.distinct

/// Static discovery for the application:
///  - OPTIONS → `Allow` (methods from matching endpoints + HEAD + OPTIONS) + `Link rel="describedby"`
///  - GET ProfileUri → ALPS profile (application/alps+json)
///  - GET HomeRoute with `Accept: application/json-home` → JSON Home directory
/// Anything else falls through. Runs after UseRouting, before endpoint execution.
type DiscoveryMiddleware
    (
        next: RequestDelegate,
        config: DiscoveryConfig,
        endpointDataSource: EndpointDataSource,
        resourceEndpointDataSource: Frank.Builder.ResourceEndpointDataSource,
        logger: ILogger<DiscoveryMiddleware>
    ) =

    // Dedup by relation at the middleware boundary where the logger lives (Holzmann 14:
    // surface side-effects at the call site). JSON Home 'resources' is keyed by relation
    // IRI — one entry per relation per spec. Two distinct hrefs sharing a relation IRI is
    // a configuration error; first-registered href wins with a LogWarning.
    // Computed once via Lazy<_> (F3: endpoint set is fixed after startup).
    let buildHomeResources () =
        let all =
            homeResourcesFromEndpoints
                config.ResourceHrefVars
                (classIriHrefMap config.AlpsDescriptors)
                endpointDataSource

        all
        |> List.groupBy (fun r -> r.Relation)
        |> List.map (fun (_, rs) ->
            if rs.Length > 1 then
                let kept = rs.Head

                for dropped in rs.Tail do
                    logger.LogWarning(
                        "DiscoveryMiddleware: relation '{Relation}' registered with multiple hrefs — keeping '{KeptHref}', dropping '{DroppedHref}'. Register each resource with a unique relation IRI.",
                        kept.Relation,
                        kept.Href,
                        dropped.Href
                    )

                kept
            else
                rs.Head)

    let cachedHomeResources = lazy (buildHomeResources ())

    // #397/#411: reconciled once, same lifetime/rationale as cachedHomeResources — the
    // endpoint set is fixed after startup. Sourced from the narrow, Frank-only
    // ResourceEndpointDataSource (#411) — never the generic endpointDataSource above,
    // which may also carry non-Frank endpoints Allow legitimately wants but ALPS Type
    // correlation should not see.
    let cachedAlpsDescriptors =
        lazy
            (let methodsByRel, methodsByReq =
                correlateMethodsByRelationAndRequestType (resourceEndpointDataSource :> EndpointDataSource)

             reconcileAlpsTypes methodsByRel methodsByReq config.AlpsDescriptors)

    // #398: DescribedByLinks keyed by class IRI, so a matched route's own declared
    // relation looks up only its own rel="type" link(s) — never every app resource's.
    // Computed once via Lazy<_>, same lifetime/rationale as cachedHomeResources.
    let describedByLinksByRelation =
        lazy
            (config.DescribedByLinks
             |> List.groupBy (fun l -> l.ClassIri)
             |> List.map (fun (classIri, links) -> classIri, links |> List.map (fun l -> l.Link))
             |> Map.ofList)

    // #398 /simplify item 6: resolved-descriptor-tree cache, origin-keyed — mirrors
    // LinkedDataMiddleware.cachedStaticBody's origin-keyed Lazy memoization (#382),
    // applied here to handleAlpsProfile's per-request href/rt resolution, which used to
    // re-walk the whole descriptor tree on every request regardless of origin repetition.
    // DiscoveryConfig (unlike LinkedDataConfig) is a single constructor-injected value —
    // one per middleware instance, never looked up per-endpoint per-request — so a plain
    // instance-level dictionary keyed by origin alone is the right-sized mirror of the
    // same idea (LinkedDataMiddleware additionally keys by config via ConditionalWeakTable
    // because ONE of its middleware instances serves MANY distinct LinkedDataConfig values,
    // one per endpoint; DiscoveryMiddleware never does).
    let resolvedAlpsCache = ConcurrentDictionary<string, Lazy<AlpsDescriptor list>>()
    let mutable resolvedAlpsBuildCount = 0

    let cachedResolvedAlps (origin: string) : AlpsDescriptor list =
        getOrBuildByOrigin
            resolvedAlpsCache
            (fun () -> System.Threading.Interlocked.Increment(&resolvedAlpsBuildCount) |> ignore)
            origin
            (fun () ->
                cachedAlpsDescriptors.Value
                |> List.map (resolveDescriptorHrefsAgainst (Uri origin)))

    // Mirrors resolvedAlpsCache/cachedResolvedAlps exactly, applied to JSON Home's
    // resources instead of the ALPS descriptor tree — same instance-level-dictionary
    // rationale (one DiscoveryConfig per middleware instance, never per-endpoint) and same
    // build-once-per-distinct-origin discipline.
    let resolvedHomeResourcesCache =
        ConcurrentDictionary<string, Lazy<JsonHomeResource list>>()

    let mutable resolvedHomeBuildCount = 0

    let cachedResolvedHomeResources (origin: string) : JsonHomeResource list =
        getOrBuildByOrigin
            resolvedHomeResourcesCache
            (fun () -> System.Threading.Interlocked.Increment(&resolvedHomeBuildCount) |> ignore)
            origin
            (fun () ->
                cachedHomeResources.Value
                |> List.map (resolveJsonHomeResourceAgainst (Uri origin)))

    let handleOptions (ctx: HttpContext) : Task =
        let requestPath = ctx.Request.Path.Value
        let methods = methodsForPath endpointDataSource requestPath

        let methods =
            if List.contains "GET" methods && not (List.contains "HEAD" methods) then
                "HEAD" :: methods
            else
                methods

        // RFC 7231 §7.4.1: OPTIONS is always handled by the server, so always advertise it.
        let methods =
            if not (List.contains "OPTIONS" methods) then
                "OPTIONS" :: methods
            else
                methods

        if not methods.IsEmpty then
            // Single comma-joined value — one wire-level header line, matching ASP.NET
            // Core's own built-in Allow serialization (HttpMethodMatcherPolicy) instead of
            // one line per method (#398).
            ctx.Response.Headers.["Allow"] <- StringValues(methods |> List.sort |> String.concat ", ")

        let profileLink = sprintf "<%s>; rel=\"describedby\"" config.ProfileUri
        ctx.Response.Headers.Append("Link", profileLink)

        // #398: scope rel="type" links to the matched route's own declared relation(s) —
        // a route with no declared relation gets zero rel="type" links.
        let scopedLinks =
            relationsForPath endpointDataSource requestPath
            |> List.collect (fun relation ->
                describedByLinksByRelation.Value
                |> Map.tryFind relation
                |> Option.defaultValue [])
            |> List.distinct

        for link in scopedLinks do
            ctx.Response.Headers.Append("Link", link)

        ctx.Response.StatusCode <- 200
        Task.CompletedTask

    let acceptsJsonHome (ctx: HttpContext) =
        match ctx.Request.Headers.TryGetValue "Accept" with
        | true, v -> v.ToString().Contains "application/json-home"
        | _ -> false

    // #398: ALPS href values are resolved against the LIVE request origin, not served
    // schemeless-relative — mirrors LinkedDataMiddleware's per-request term resolution
    // (#396). A malformed Host header cannot mint resolvable hrefs, so this fails the
    // same way LinkedDataMiddleware does: logged and 400, never a garbage-but-valid URI.
    let handleAlpsProfile (ctx: HttpContext) : Task =
        match Frank.OriginValidation.tryValidateOrigin ctx.Request with
        | None ->
            logger.LogWarning(
                "DiscoveryMiddleware: malformed Host header '{Host}' — cannot mint resolvable ALPS hrefs, rejecting with 400",
                ctx.Request.Host.Value
            )

            ctx.Response.StatusCode <- 400
            Task.CompletedTask
        | Some origin ->
            let resolved = cachedResolvedAlps origin
            ctx.Response.ContentType <- "application/alps+json"
            ctx.Response.WriteAsync(AlpsSerializer.serialize resolved)

    // JSON Home resource keys (Relation, an RFC 8288 §2.1 link-relation-type IRI) and
    // href-vars meaning IRIs are resolved against the LIVE request origin, not served
    // schemeless-relative — mirrors handleAlpsProfile above (#398) and LinkedDataMiddleware's
    // per-request term resolution (#396). A malformed Host header cannot mint resolvable
    // IRIs, so this fails the same way: logged and 400, never a garbage-but-valid URI.
    let handleJsonHome (ctx: HttpContext) : Task =
        match Frank.OriginValidation.tryValidateOrigin ctx.Request with
        | None ->
            logger.LogWarning(
                "DiscoveryMiddleware: malformed Host header '{Host}' — cannot mint resolvable JSON Home IRIs, rejecting with 400",
                ctx.Request.Host.Value
            )

            ctx.Response.StatusCode <- 400
            Task.CompletedTask
        | Some origin ->
            ctx.Response.Headers.Append("Vary", "Accept")
            ctx.Response.ContentType <- "application/json-home"
            ctx.Response.WriteAsync(JsonHomeSerializer.serialize (cachedResolvedHomeResources origin))

    /// Test-only visibility (internal + InternalsVisibleTo, #392 pattern): number of times
    /// the resolved ALPS descriptor tree was actually (re)built — proves build-once-per-
    /// distinct-origin, not once per request (#398 /simplify item 6).
    member internal _.ResolvedAlpsBuildCount = resolvedAlpsBuildCount

    /// Test-only visibility (internal + InternalsVisibleTo, #392 pattern): number of times
    /// the resolved JSON Home resources list was actually (re)built — proves build-once-
    /// per-distinct-origin, not once per request, mirroring ResolvedAlpsBuildCount above.
    member internal _.ResolvedHomeBuildCount = resolvedHomeBuildCount

    member _.Invoke(ctx: HttpContext) : Task =
        let path = ctx.Request.Path.Value
        let isGet = HttpMethods.IsGet ctx.Request.Method

        if HttpMethods.IsOptions ctx.Request.Method then
            handleOptions ctx
        elif isGet && path = config.ProfileUri then
            handleAlpsProfile ctx
        elif isGet && path = config.HomeRoute && acceptsJsonHome ctx then
            handleJsonHome ctx
        else
            next.Invoke ctx
