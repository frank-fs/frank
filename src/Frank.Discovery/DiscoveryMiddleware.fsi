module Frank.Discovery.DiscoveryMiddleware

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
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

/// Static discovery for the application:
///  - OPTIONS → `Allow` (methods from matching endpoints + HEAD + OPTIONS) + `Link rel="describedby"`
///  - GET ProfileUri → ALPS profile (application/alps+json)
///  - GET HomeRoute with `Accept: application/json-home` → JSON Home directory
/// Anything else falls through. Runs after UseRouting, before endpoint execution.
type DiscoveryMiddleware =
    new:
        next: RequestDelegate *
        config: DiscoveryConfig *
        endpointDataSource: EndpointDataSource *
        logger: ILogger<DiscoveryMiddleware> ->
            DiscoveryMiddleware

    member Invoke: ctx: HttpContext -> Task
