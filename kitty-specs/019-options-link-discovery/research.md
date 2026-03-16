# Research: OPTIONS and Link Header Discovery

**Date**: 2026-03-16
**Feature**: 019-options-link-discovery

## R-01: Sibling Endpoint Enumeration for OPTIONS

**Decision**: Use `EndpointDataSource` from DI to enumerate all endpoints and match by `RoutePattern.RawText`.

**Rationale**: Frank creates one `Endpoint` per HTTP method per resource via `RouteEndpointBuilder`. When an OPTIONS request arrives, the middleware needs to discover all HTTP methods registered for that route. ASP.NET Core routing selects a single endpoint per request, so the middleware must enumerate siblings.

All Frank endpoints are `RouteEndpoint` instances with a `RoutePattern` property. Two endpoints for the same resource share the same `RoutePattern.RawText` (the route template string). The middleware can:

1. Get the matched endpoint via `ctx.GetEndpoint()` and cast to `RouteEndpoint`
2. Extract its `RoutePattern.RawText` (e.g., `"/items/{id}"`)
3. Inject `EndpointDataSource` (via DI or constructor) and enumerate all endpoints
4. Filter to `RouteEndpoint` instances with matching `RoutePattern.RawText`
5. Collect `HttpMethodMetadata` from each sibling to build the `Allow` header
6. Collect all `DiscoveryMediaType` metadata from siblings for media type aggregation

**Alternatives considered**:
- Using `IEndpointRouteBuilder.DataSources` directly -- not available in middleware, only during configuration
- Storing a route-to-methods map at startup -- would work but adds startup cost and stale data risk; runtime enumeration is simpler and always correct
- Injecting `IEnumerable<EndpointDataSource>` -- possible but `EndpointDataSource` singleton is sufficient since Frank uses one data source

**Performance note**: Endpoint enumeration happens only on OPTIONS requests, which are infrequent (agent discovery, not every request). The endpoint collection is small (bounded by route count). No allocation concern.

## R-02: RFC 8288 Link Header Format

**Decision**: Format Link headers as `<URI>; rel="describedby"; type="media/type"` per RFC 8288 Section 3.

**Rationale**: RFC 8288 (Web Linking) defines the Link header field syntax:

```
Link: <URI>; rel="relation-type"; type="media-type"
```

Key rules:
- Target URI is enclosed in angle brackets `< >`
- Parameters follow as semicolon-separated key-value pairs
- `rel` parameter specifies the link relation type (e.g., `describedby`)
- `type` parameter specifies the media type hint for the target
- Multiple link values can be comma-separated in a single header or sent as separate headers
- Both formats are equivalent per HTTP/1.1 header folding rules

For discovery, each `DiscoveryMediaType` entry generates a Link value:
```
Link: </>; rel="describedby"; type="application/ld+json"
Link: </>; rel="describedby"; type="text/turtle"
```

The `rel="describedby"` relation type (IANA registered) indicates that the linked resource provides a description of the context resource. The target URI points to the resource's own URI (the client can request it with the specified `Accept` header to get that representation).

**Alternatives considered**:
- Using `rel="profile"` -- less standard for discovery; `describedby` is the IANA-registered relation for semantic descriptions
- JSON body in OPTIONS response listing media types -- the spec explicitly calls for empty body with headers only (FR-013); JSON body is a possible future enhancement
- Single Link header with comma-separated values -- valid per RFC 8288 but separate headers are simpler to construct and equally valid

## R-03: CORS Preflight Detection

**Decision**: Detect CORS preflight by checking for `Access-Control-Request-Method` header on OPTIONS requests.

**Rationale**: A CORS preflight request is an OPTIONS request with two specific headers:
1. `Origin` header (present on all cross-origin requests)
2. `Access-Control-Request-Method` header (only on preflight)

The `Access-Control-Request-Method` header is the definitive signal that distinguishes a CORS preflight from a regular OPTIONS request. If this header is present, the `OptionsDiscoveryMiddleware` should pass through to let CORS middleware handle it.

ASP.NET Core's CORS middleware (`UseCors`) should be registered before the discovery middleware in the pipeline. The CORS middleware handles preflight requests and short-circuits. If the request reaches the discovery middleware, it's not a CORS preflight. However, as a safety net, the middleware should still check for `Access-Control-Request-Method` to avoid interfering with unusual middleware ordering.

**Alternatives considered**:
- Only relying on middleware ordering -- fragile if user registers middlewares in wrong order
- Checking `Origin` header -- insufficient; `Origin` is present on all cross-origin requests including normal GETs
- Letting CORS middleware run first and always passing through if CORS headers are present -- CORS middleware doesn't always short-circuit (it may add headers and continue)

## R-04: Explicit OPTIONS Handler Detection

**Decision**: Check if the matched endpoint's `HttpMethodMetadata` contains "OPTIONS". If yes, defer to the explicit handler.

**Rationale**: When a developer defines an explicit `options` handler on a resource via the `ResourceBuilder`:

```fsharp
resource "/items" {
    get getHandler
    options myOptionsHandler
}
```

Frank creates an endpoint with `HttpMethodMetadata(["OPTIONS"])` for that handler. ASP.NET Core routing matches this endpoint for OPTIONS requests. The `OptionsDiscoveryMiddleware` can detect this by inspecting the matched endpoint's `HttpMethodMetadata`:

```fsharp
let endpoint = ctx.GetEndpoint()
let httpMethods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()
if httpMethods <> null && httpMethods.HttpMethods |> Seq.contains "OPTIONS" then
    // Explicit handler -- pass through
    next.Invoke(ctx)
```

This ensures FR-007: explicit handlers take precedence over implicit discovery.

**Alternatives considered**:
- Custom metadata marker for explicit vs implicit handlers -- over-engineering; the `HttpMethodMetadata` already distinguishes them
- Always running discovery and merging with explicit handler output -- violates FR-007's "take precedence" requirement

## R-05: DiscoveryMediaType Struct Design

**Decision**: `[<Struct>] type DiscoveryMediaType = { MediaType: string; Rel: string }` in `src/Frank/Builder.fs`.

**Rationale**: Minimal struct with two fields:
- `MediaType`: The content type string (e.g., `"application/ld+json"`)
- `Rel`: The link relation type (e.g., `"describedby"`)

Design choices:
- **Struct**: Zero allocation when stored in endpoint metadata (the metadata list itself is allocated, but the struct values are not individually heap-allocated). Matches Constitution Principle V.
- **No Option types**: `Rel` always has a value (defaults to `"describedby"` at the call site). This avoids null checks in the middleware.
- **No ProfileUri field**: The target URI in Link headers points to the resource's own URI (the resource at the same path, requested with a different Accept header). No separate profile URL needed.
- **Placed in Builder.fs**: Alongside `ResourceSpec` and other core types. Extensions already depend on `Frank.Builder`.

**Alternatives considered**:
- Class type with factory methods -- unnecessary complexity for a two-field value type
- Record with `Rel: string option` -- would require `Option.defaultValue` everywhere; always providing `Rel` is cleaner
- Separate file in Frank core -- only 3 lines; not worth a separate file

## R-06: Middleware Registration Pattern

**Decision**: Three custom operations on `WebHostBuilder`: `useOptionsDiscovery`, `useLinkHeaders`, and `useDiscovery` (convenience for both).

**Rationale**: Follows the established pattern from Frank.LinkedData (`useLinkedData`), Frank.Statecharts (`useStatecharts`), and Frank.Auth (`useAuthentication`/`useAuthorization`). Each operation adds its middleware to `spec.Middleware`.

`useDiscovery` is defined as:
```fsharp
member _.UseDiscovery(spec) =
    spec |> _.UseOptionsDiscovery |> _.UseLinkHeaders
```

This gives developers full control while providing a convenience shortcut for the common case.

**Alternatives considered**:
- Only individual operations (no combined shortcut) -- less convenient for the common case where both are wanted
- Single `useDiscovery` with boolean parameters -- not idiomatic F#; imperative configuration style
