module Frank.Discovery.DiscoveryMiddleware

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Template
open Microsoft.Extensions.Logging

/// Build JSON Home resource entries from live endpoints.
/// Endpoints carrying ResourceRelationMetadata contribute to one merged entry per
/// (Relation, Href) pair — a resource with both GET and POST produces a single entry
/// with allow ⊇ {GET, HEAD, OPTIONS, POST}. HEAD is added when GET is present;
/// OPTIONS is always added (RFC 7231 §7.4.1).
/// Returns a list that may contain multiple entries with the same Relation when two
/// distinct hrefs share a relation IRI; caller is responsible for deduplication.
/// resourceHrefVars maps each relation IRI to its template-variable meaning IRIs.
val homeResourcesFromEndpoints:
    resourceHrefVars: Map<string, Map<string, string>> -> dataSource: EndpointDataSource -> JsonHomeResource list

/// Real HTTP methods per relation IRI, from live endpoints' ResourceRelationMetadata.
/// Coarse correlation key: one `resource { relation X; ... }` block stamps the SAME
/// relation on every verb it registers, so a route serving both GET and POST under one
/// relation (#390) yields a multi-method set here. Sourced directly from Frank's own
/// composed Endpoint[] (typically the narrow ResourceEndpointDataSource) — no
/// ApiExplorer/reflection walk, no Microsoft.AspNetCore.OpenApi dependency (#411).
val internal methodsByRelation: dataSource: EndpointDataSource -> Map<string, Set<string>>

/// Real HTTP methods per accepted request CLR type full name, from live endpoints'
/// IAcceptsMetadata. Precise correlation key: Frank.OpenApi's `accepts` operation is
/// stamped only on the endpoint whose own HttpMethodMetadata matches, so this
/// disambiguates an action's real method even when its route also serves other verbs
/// (#397/#411).
val internal methodsByRequestType: dataSource: EndpointDataSource -> Map<string, Set<string>>

/// ALPS §2.2 transition semantics from a resource's real registered HTTP method(s).
/// GET present (however else the route is used) is safe; exactly {PUT} or {DELETE} is
/// idempotent; exactly {POST} is unsafe. Anything else (no live match, or an otherwise
/// ambiguous multi-write verb combination) returns None — the codegen-emitted Type is
/// left as the fallback, never guessed (#397).
val internal alpsTypeForMethods: methods: Set<string> -> string option

/// Resolve a served descriptor Href against a live request origin (#398). A relative
/// value becomes absolute against origin; an already-absolute value (external vocab)
/// passes through unchanged — RFC 3986 §5.3 reference resolution. Public: shared with
/// app code that must resolve the SAME codegen-emitted href against a live request
/// origin outside the middleware — never reimplemented.
val resolveHref: origin: string -> href: string -> string

/// Reconcile codegen-emitted ALPS Type against real registered HTTP methods (#397).
/// Tries the precise per-verb signal first (RequestClrTypeName via IAcceptsMetadata),
/// then falls back to the coarser per-route signal (ClassIri via
/// ResourceRelationMetadata). A descriptor with neither signal resolvable keeps its
/// codegen default untouched.
val internal reconcileAlpsTypes:
    methodsByRel: Map<string, Set<string>> ->
    methodsByType: Map<string, Set<string>> ->
    descriptors: AlpsDescriptor list ->
        AlpsDescriptor list

/// Real HTTP methods registered for the given request path, from every endpoint whose
/// (already cache-parsed, #421) route template matches — the OPTIONS Allow header's
/// source of truth. Takes the RouteTemplate cache built once per middleware instance,
/// never a raw EndpointDataSource re-parsed per call.
val internal methodsForPath: routeTemplates: (RouteEndpoint * RouteTemplate) list -> requestPath: string -> string list

/// Declared relation IRI(s) for the given request path, from ResourceRelationMetadata on
/// every endpoint whose (already cache-parsed, #421) route template matches. Used to scope
/// rel="type" Link headers to only the resource actually matched (#398). Shares the same
/// RouteTemplate cache as methodsForPath — a request needing both never parses twice.
val internal relationsForPath:
    routeTemplates: (RouteEndpoint * RouteTemplate) list -> requestPath: string -> string list

/// Static discovery for the application:
///  - OPTIONS → `Allow` (methods from matching endpoints + HEAD + OPTIONS) + `Link rel="profile"`
///  - GET ProfileUri → ALPS profile (application/alps+json)
///  - GET HomeRoute with `Accept: application/json-home` → JSON Home directory
/// Anything else falls through. Runs after UseRouting, before endpoint execution.
type DiscoveryMiddleware =
    new:
        next: RequestDelegate *
        config: DiscoveryConfig *
        endpointDataSource: EndpointDataSource *
        resourceEndpointDataSource: Frank.Builder.ResourceEndpointDataSource *
        logger: ILogger<DiscoveryMiddleware> ->
            DiscoveryMiddleware

    /// Test-only visibility (internal + InternalsVisibleTo, #392 pattern): number of times
    /// the resolved ALPS descriptor tree was actually (re)built — proves build-once-per-
    /// distinct-origin, not once per request (#398 /simplify item 6).
    member internal ResolvedAlpsBuildCount: int

    /// Test-only visibility (internal + InternalsVisibleTo, #392 pattern): number of times
    /// the resolved JSON Home resources list was actually (re)built — proves build-once-
    /// per-distinct-origin, not once per request, mirroring ResolvedAlpsBuildCount above.
    member internal ResolvedHomeBuildCount: int

    /// Test-only visibility (internal + InternalsVisibleTo, #392 pattern): number of times
    /// TemplateParser.Parse was actually invoked — proves parse-once-per-endpoint at
    /// cache-build time, not once per OPTIONS request (#421).
    member internal RouteTemplateParseCount: int

    member Invoke: ctx: HttpContext -> Task
