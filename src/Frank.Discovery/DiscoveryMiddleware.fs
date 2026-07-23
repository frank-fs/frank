module Frank.Discovery.DiscoveryMiddleware

open System
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

/// Frank.OpenApi's `produces` operation stamps ProducesResponseTypeMetadata with
/// typeof<Void> as its sentinel for "no declared response body type" (HandlerDefinition.fs)
/// — never null. Mirrors Frank.Provenance.ProvenanceMiddleware's own sentinel-filtering
/// convention (isSentinel) for the same metadata type.
let private isVoidResponseType (t: Type) : bool =
    t = typeof<Void> || t = typeof<unit> || t = typeof<obj>

/// Declared response CLR type full names on an endpoint via IProducesResponseTypeMetadata
/// (Frank.OpenApi's `produces`), normalized via Frank.ClrTypeName — the SAME correlation-key
/// convention as acceptedRequestTypeOf, but for the response side (#418): a class-mapped
/// resource can be legitimately reachable as an action's DECLARED RESPONSE type (e.g.
/// `produces typeof<MoveResult> 200`) without ever being accepted as a request body or
/// backing its own route by relation — methodsByRequestType (IAcceptsMetadata only) can't
/// see this on its own.
let internal producedResponseTypesOf (re: RouteEndpoint) : string list =
    re.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
    |> Seq.choose (fun m ->
        match m.Type with
        | null -> None
        | t when isVoidResponseType t -> None
        | t -> Some(Frank.ClrTypeName.normalizeFullName t.FullName))
    |> Seq.toList

/// #422 Finding C: the pluggable list of "live correlation key" signals that make an ALPS
/// descriptor reachable (isLiveDescriptor) — relation IRI (ResourceRelationMetadata),
/// accepted request CLR type (IAcceptsMetadata), declared response CLR type
/// (IProducesResponseTypeMetadata, #418's third signal). Before this, each signal needed
/// its own extraction function, its own map/set, and its own threaded parameter through
/// filterReachableDescriptors/cachedAlpsDescriptors — #418 already had to grow a 3rd
/// signal mid-implementation to fix a self-discovered regression (MoveResult). Adding a
/// future signal is now exactly ONE function appended to this list;
/// isLiveDescriptor/filterReachableDescriptors below never change.
let internal correlationExtractors: (RouteEndpoint -> string list) list =
    [ relationOf >> Option.toList
      acceptedRequestTypeOf >> Option.toList
      producedResponseTypesOf ]

/// Union of every correlation key any of `extractors` contributes across every live
/// endpoint in `dataSource` — the single set isLiveDescriptor/filterReachableDescriptors
/// check ClassIri/RequestClrTypeName membership against. Parameterized over `extractors`
/// (rather than hard-coding correlationExtractors) so the fold is independently testable —
/// removing one extractor from the list demonstrably shrinks the resulting set with zero
/// changes to isLiveDescriptor/filterReachableDescriptors.
let internal liveCorrelationKeysWith
    (extractors: (RouteEndpoint -> string list) list)
    (dataSource: EndpointDataSource)
    : Set<string> =
    scanRouteEndpoints dataSource
    |> Seq.collect (fun re -> extractors |> List.collect (fun extract -> extract re))
    |> Set.ofSeq

/// liveCorrelationKeysWith applied to the full, real correlationExtractors list — what
/// production code actually calls.
let internal liveCorrelationKeys (dataSource: EndpointDataSource) : Set<string> =
    liveCorrelationKeysWith correlationExtractors dataSource

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

/// The advertised HTTP method set for a route, derived from its real registered methods:
/// HEAD is added when GET is present (RFC 7231 §7.4.1 — HEAD is GET without a body),
/// OPTIONS is always added (every server handles OPTIONS), and the result is sorted for a
/// stable wire order. The ONE place this computation lives — handleOptions (Allow header)
/// and homeResourcesFromEndpoints (JSON Home Allow field) both call it instead of each
/// keeping its own copy, so the advertised set can never independently drift between the
/// two (#432, Constitution rule 8).
let internal advertisedMethods (methods: string list) : string list =
    let withHead =
        if List.contains "GET" methods && not (List.contains "HEAD" methods) then
            "HEAD" :: methods
        else
            methods

    let withOptions =
        if not (List.contains "OPTIONS" withHead) then
            "OPTIONS" :: withHead
        else
            withHead

    withOptions |> List.sort

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
            |> advertisedMethods

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
/// mutable state (Rule 13) — the caller supplies its own cache and build-count callback.
/// `cache` is a Frank.BoundedCache (#405: bounds retained memory to a hard ceiling
/// independent of how many distinct Host header values a client sends — an unauthenticated
/// client varying Host could otherwise mint unbounded permanent entries) — build still
/// runs at most once per origin even if two requests race on a brand-new one. Mirrors
/// ValidationMiddleware's getOrBuildShapesGraph shape (src/Frank.Validation/
/// ValidationMiddleware.fs).
let private getOrBuildByOrigin
    (cache: Frank.BoundedCache<string, 'T>)
    (onBuild: unit -> unit)
    (origin: string)
    (build: unit -> 'T)
    : 'T =
    cache.GetOrAdd(
        origin,
        (fun () ->
            onBuild ()
            build ())
    )

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

/// True iff a top-level descriptor is "live": its ClassIri OR its RequestClrTypeName
/// appears in `liveKeys` — the union of every correlation signal any
/// correlationExtractors entry contributes across live endpoints (#422 Finding C: relation
/// IRI, accepted request type, and declared response type — #418's three signals — are no
/// longer three separately-named maps/sets each requiring their own check here, just ONE
/// set membership test).
let private isLiveDescriptor (liveKeys: Set<string>) (d: AlpsDescriptor) : bool =
    (d.ClassIri |> Option.exists (fun c -> Set.contains c liveKeys))
    || (d.RequestClrTypeName |> Option.exists (fun t -> Set.contains t liveKeys))

/// Bounded fixed-point closure over the `rt` chain (#422 expert-review finding 1): a
/// one-hop check keeps a live descriptor's direct `rt` target but drops that target's OWN
/// `rt` target, even though the now-served target descriptor publishes a real link to it —
/// a client following live -> target -> target's-rt would hit a dead reference the server
/// itself just served. Repeatedly unions in newly-rt-reachable descriptors (same dual-match
/// join as before: a candidate is rt-reachable if its OWN Href OR Id appears in the raw Rt
/// STRING VALUES collected from currently-reachable descriptors) until no new descriptor is
/// added. Capped at `List.length descriptors` iterations (Holzmann rule 10): each productive
/// iteration adds at least one previously-unreached descriptor Id, and there are only that
/// many descriptors total, so the loop provably converges within the cap — hitting the cap
/// with `newlyReachable` still non-empty is structurally impossible, not a truncation risk.
let rec private closeOverRtChain
    (descriptors: AlpsDescriptor list)
    (reachableIds: Set<string>)
    (remaining: int)
    : Set<string> =
    if remaining <= 0 then
        reachableIds
    else
        let rtValues =
            descriptors
            |> List.filter (fun d -> Set.contains d.Id reachableIds)
            |> List.choose (fun d -> d.Rt)
            |> Set.ofList

        let isRtReachable (d: AlpsDescriptor) =
            (d.Href |> Option.exists (fun h -> Set.contains h rtValues))
            || Set.contains d.Id rtValues

        let newlyReachable =
            descriptors
            |> List.filter (fun d -> not (Set.contains d.Id reachableIds) && isRtReachable d)
            |> List.map (fun d -> d.Id)
            |> Set.ofList

        if Set.isEmpty newlyReachable then
            reachableIds
        else
            closeOverRtChain descriptors (Set.union reachableIds newlyReachable) (remaining - 1)

/// #418: drop any top-level ALPS descriptor that (a) IS a class-mapped resource (ClassIri
/// Some — DiscoveryEmitter.collectDescriptors only ever emits a top-level descriptor when
/// the source resource carries a ClassIri, so this is never a real-world exclusion, only a
/// guard against non-class-mapped top-level fixtures/descriptors this filter was never
/// meant to touch) and (b) is neither live (isLiveDescriptor) nor `rt`-reachable, via a
/// bounded fixed-point closure (closeOverRtChain, #422 expert-review finding 1), from a
/// descriptor that IS live — an `rt` chain of ANY depth, not just one hop. Codegen
/// (DiscoveryEmitter, MSBuild time) cannot see which types end up routed/embedded in
/// Program.fs — that information only exists at runtime — so the running app is responsible
/// for filtering its candidate descriptor set down to what a client can actually reach. A
/// descriptor satisfying neither is a phantom affordance (e.g. a class-mapped type that
/// exists only to exercise an `equivalentClass` declaration, never itself routed or
/// embedded, #418) — a client following it would find nothing.
let internal filterReachableDescriptors
    (liveKeys: Set<string>)
    (descriptors: AlpsDescriptor list)
    : AlpsDescriptor list =
    let liveIds =
        descriptors
        |> List.filter (isLiveDescriptor liveKeys)
        |> List.map (fun d -> d.Id)
        |> Set.ofList

    let reachableIds = closeOverRtChain descriptors liveIds (List.length descriptors)

    descriptors
    |> List.filter (fun d -> d.ClassIri.IsNone || Set.contains d.Id reachableIds)

// ── #398/#421: per-request path matching shared by Allow and rel="type" scoping ───

/// One endpoint paired with its pre-parsed, immutable RouteTemplate (src/CLAUDE.md:
/// "cache immutable RouteTemplate objects via TemplateParser.Parse; create TemplateMatcher
/// per-request" — TemplateMatcher itself is NOT thread-safe and must never be cached, but
/// the RouteTemplate it matches against is safe to share and must be, #421).
type private EndpointTemplate = RouteEndpoint * RouteTemplate

/// Parse every registered endpoint's route template exactly once — the cache-build step
/// #421 introduces so methodsForPath/relationsForPath below stop re-parsing the SAME
/// templates from raw strings on every OPTIONS request. `onParse` is called once per
/// endpoint parsed, so callers (the DiscoveryMiddleware instance) can count real
/// TemplateParser.Parse invocations without this pure function owning any counter state
/// itself (Holzmann 14: side effects surfaced at the call site).
let private buildRouteTemplates (onParse: unit -> unit) (dataSource: EndpointDataSource) : EndpointTemplate list =
    scanRouteEndpoints dataSource
    |> Seq.map (fun re ->
        let raw = re.RoutePattern.RawText
        let pattern = if raw.StartsWith('/') then raw.TrimStart('/') else raw
        onParse ()
        re, TemplateParser.Parse(pattern))
    |> Seq.toList

/// RouteEndpoints whose (already-parsed, cached) route template matches the given raw
/// request path, regardless of the HTTP method each declares — the one-time template-match
/// step shared by methodsForPath and relationsForPath. TemplateMatcher is not thread-safe —
/// a fresh instance is still constructed per candidate route on every call (#421 only caches
/// the RouteTemplate parse, never the matcher itself, per the project's own rule).
let private endpointsForPath (routeTemplates: EndpointTemplate list) (requestPath: string) : RouteEndpoint list =
    let pathString = PathString(requestPath)

    routeTemplates
    |> List.filter (fun (_, template) ->
        let matcher = TemplateMatcher(template, RouteValueDictionary())
        matcher.TryMatch(pathString, RouteValueDictionary()))
    |> List.map fst

/// Real HTTP methods registered for the given request path, from every endpoint whose
/// route template matches — the OPTIONS Allow header's source of truth. Takes the
/// already-built RouteTemplate cache (#421), never a raw EndpointDataSource — the caller
/// (handleOptions) builds/reuses that cache once per middleware instance lifetime.
let internal methodsForPath (routeTemplates: EndpointTemplate list) (requestPath: string) : string list =
    endpointsForPath routeTemplates requestPath
    |> List.choose httpMethodsOf
    |> List.collect id
    |> List.distinct

/// Declared relation IRI(s) for the given request path, from ResourceRelationMetadata on
/// every endpoint whose route template matches. Used to scope rel="type" Link headers to
/// only the resource actually matched (#398) — a route carrying no relation (e.g. "/",
/// "/tictactoe") yields an empty list, and every codegen-emitted DescribedByLink for
/// OTHER resources is withheld, not broadcast. Takes the already-built RouteTemplate cache
/// (#421), shared with methodsForPath — a request needing both never parses twice.
let internal relationsForPath (routeTemplates: EndpointTemplate list) (requestPath: string) : string list =
    endpointsForPath routeTemplates requestPath
    |> List.choose relationOf
    |> List.distinct

/// Static discovery for the application:
///  - OPTIONS → `Allow` (methods from matching endpoints + HEAD + OPTIONS) + `Link rel="profile"`
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

    // #432 review fix 3/6: the ALPS profile is an Application-Level Profile, not the
    // resource's own representation format — RFC 6906 `rel="profile"`, never
    // `rel="describedby"` (which LinkedDataMiddleware's vocabulary document is now the
    // sole occupant of, on GET/HEAD). config.ProfileUri is fixed per middleware instance,
    // so the link string is built ONCE here and reused by both handleOptions and
    // EmitDescribedByOnStarting below, instead of re-`sprintf`-ing it per request.
    let profileLink =
        sprintf "<%s>; rel=\"profile\"; type=\"application/alps+json\"" config.ProfileUri

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
            (let narrowSource = resourceEndpointDataSource :> EndpointDataSource

             let methodsByRel, methodsByReq =
                 correlateMethodsByRelationAndRequestType narrowSource

             // #422 Finding C: liveKeys unions ALL correlationExtractors signals (relation,
             // accepted-request-type, produced-response-type) in one fold — methodsByRel/
             // methodsByReq above are still computed separately because reconcileAlpsTypes
             // below needs the actual per-key HTTP METHOD sets, not just liveness.
             let liveKeys = liveCorrelationKeys narrowSource

             config.AlpsDescriptors
             // #418: drop phantom top-level descriptors BEFORE Type reconciliation — a
             // dropped descriptor never needs its Type reconciled.
             |> filterReachableDescriptors liveKeys
             |> reconcileAlpsTypes methodsByRel methodsByReq)

    // #398: DescribedByLinks keyed by class IRI, so a matched route's own declared
    // relation looks up only its own rel="type" link(s) — never every app resource's.
    // Computed once via Lazy<_>, same lifetime/rationale as cachedHomeResources.
    let describedByLinksByRelation =
        lazy
            (config.DescribedByLinks
             |> List.groupBy (fun l -> l.ClassIri)
             |> List.map (fun (classIri, links) -> classIri, links |> List.map (fun l -> l.Link))
             |> Map.ofList)

    // #421: every registered endpoint's RouteTemplate, parsed once — src/CLAUDE.md's own
    // documented rule ("cache immutable RouteTemplate objects via TemplateParser.Parse;
    // create TemplateMatcher per-request") was honored for the matcher half but not the
    // parse half; methodsForPath/relationsForPath (the Allow header and rel="type" scoping
    // sources of truth) used to re-parse every endpoint's raw route string from scratch on
    // every single OPTIONS request. Same "endpoint set is fixed after startup" rationale as
    // cachedHomeResources/cachedAlpsDescriptors above — and here that invariant isn't just
    // assumed: Frank's own ResourceEndpointDataSource.GetChangeToken() returns
    // NullChangeToken.Singleton (src/Frank/Builder.fs), i.e. it declares itself as never
    // changing after construction. A third-party EndpointDataSource composed into the same
    // app that DOES fire change tokens (e.g. hot-reloaded Razor Pages) would need this cache
    // to move to change-token-driven invalidation — this project has no such case anywhere
    // yet (every other Lazy<_> cache in this file makes the identical assumption), so a
    // plain build-once Lazy is the right-sized fix, not speculative invalidation plumbing.
    let mutable routeTemplateParseCount = 0

    let cachedRouteTemplates =
        lazy
            (buildRouteTemplates
                (fun () -> System.Threading.Interlocked.Increment(&routeTemplateParseCount) |> ignore)
                endpointDataSource)

    // #398 /simplify item 6: resolved-descriptor-tree cache, origin-keyed — mirrors
    // LinkedDataMiddleware.cachedStaticBody's origin-keyed Lazy memoization (#382),
    // applied here to handleAlpsProfile's per-request href/rt resolution, which used to
    // re-walk the whole descriptor tree on every request regardless of origin repetition.
    // DiscoveryConfig (unlike LinkedDataConfig) is a single constructor-injected value —
    // one per middleware instance, never looked up per-endpoint per-request — so a plain
    // instance-level cache keyed by origin alone is the right-sized mirror of the same idea
    // (LinkedDataMiddleware additionally keys by config via ConditionalWeakTable because ONE
    // of its middleware instances serves MANY distinct LinkedDataConfig values, one per
    // endpoint; DiscoveryMiddleware never does). #405: Frank.BoundedCache bounds retained
    // memory to a hard ceiling regardless of how many distinct Host header values a client
    // sends — the origin string is only ever validated for SYNTACTIC well-formedness
    // (Frank.OriginValidation.tryValidateOrigin), never checked against a configured
    // allowlist, so an unbounded cache here would let an unauthenticated client mint
    // unlimited permanent entries.
    let resolvedAlpsCache =
        Frank.BoundedCache<string, AlpsDescriptor list>(Frank.BoundedCache.DefaultCapacity)

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
    // resources instead of the ALPS descriptor tree — same instance-level-cache rationale
    // (one DiscoveryConfig per middleware instance, never per-endpoint), same
    // build-once-per-distinct-origin discipline, and the SAME bounded-cache fix (#405).
    let resolvedHomeResourcesCache =
        Frank.BoundedCache<string, JsonHomeResource list>(Frank.BoundedCache.DefaultCapacity)

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

        let methods =
            methodsForPath cachedRouteTemplates.Value requestPath |> advertisedMethods

        if not methods.IsEmpty then
            // Single comma-joined value — one wire-level header line, matching ASP.NET
            // Core's own built-in Allow serialization (HttpMethodMatcherPolicy) instead of
            // one line per method (#398).
            ctx.Response.Headers.["Allow"] <- StringValues(methods |> String.concat ", ")

        ctx.Response.Headers.Append("Link", profileLink)

        // #398: scope rel="type" links to the matched route's own declared relation(s) —
        // a route with no declared relation gets zero rel="type" links.
        let scopedLinks =
            relationsForPath cachedRouteTemplates.Value requestPath
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

    /// Test-only visibility (internal + InternalsVisibleTo, #392 pattern): number of times
    /// TemplateParser.Parse was actually invoked — proves parse-once-per-endpoint at
    /// cache-build time, not once per OPTIONS request (#421).
    member internal _.RouteTemplateParseCount = routeTemplateParseCount

    /// Test-only visibility (internal + InternalsVisibleTo, #392 pattern): number of
    /// distinct origins currently retained in the resolved-ALPS cache — proves the
    /// Host-header-flood cache-DoS fix (#405): bounded at Frank.BoundedCache.DefaultCapacity
    /// regardless of how many distinct Host header values a client sends.
    member internal _.ResolvedAlpsCacheSize = resolvedAlpsCache.Count

    /// Test-only visibility (internal + InternalsVisibleTo, #392 pattern): mirrors
    /// ResolvedAlpsCacheSize above, for the resolved-JSON-Home cache (#405).
    member internal _.ResolvedHomeCacheSize = resolvedHomeResourcesCache.Count

    /// #432: emit the ALPS `rel="profile"` link (RFC 6906, #432 review fix 3) on a GET or
    /// HEAD whose path matches a registered resource route (RFC 7231 §4.3.2 — HEAD MUST
    /// return the same headers GET would). The route match itself is the endpoint
    /// `UseRouting` already resolved (Invoke below reads `ctx.GetEndpoint()`, #432 review
    /// fix 4) — never a second/parallel matcher. The endpoint writes its own body, so the
    /// header cannot be set after next.Invoke returns; OnStarting is registered BEFORE
    /// calling next.Invoke and fires just before headers are sent, whether or not the
    /// handler writes a body (e.g. a 304 short-circuit from the HTTP-caching layer). Gated
    /// on 200/304 so an error response from the matched route (4xx/5xx) is not mislabeled
    /// as describing a resource it never served.
    member private _.EmitDescribedByOnStarting(ctx: HttpContext) : unit =
        ctx.Response.OnStarting(fun () ->
            let status = ctx.Response.StatusCode

            if status = 200 || status = 304 then
                ctx.Response.Headers.Append("Link", profileLink)

            Task.CompletedTask)

    member this.Invoke(ctx: HttpContext) : Task =
        let path = ctx.Request.Path.Value
        let isGet = HttpMethods.IsGet ctx.Request.Method
        let isHead = HttpMethods.IsHead ctx.Request.Method

        if HttpMethods.IsOptions ctx.Request.Method then
            handleOptions ctx
        elif isGet && path = config.ProfileUri then
            handleAlpsProfile ctx
        elif isGet && path = config.HomeRoute && acceptsJsonHome ctx then
            handleJsonHome ctx
        // #432 review fix 2/4: HEAD must carry the SAME rel="profile" link GET does
        // (RFC 7231 §4.3.2) — gated on the endpoint UseRouting already matched
        // (ctx.GetEndpoint()), never a re-run of Frank's own template matcher on every
        // request (that duplicated work methodsForPath still does for the Allow header,
        // which OPTIONS needs regardless of what UseRouting resolved for THIS request).
        elif (isGet || isHead) && not (isNull (ctx.GetEndpoint())) then
            this.EmitDescribedByOnStarting ctx
            next.Invoke ctx
        else
            next.Invoke ctx
