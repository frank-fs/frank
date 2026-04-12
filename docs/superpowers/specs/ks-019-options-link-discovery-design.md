---
source: kitty-specs/019-options-link-discovery
status: complete
type: spec
---

# Feature Specification: OPTIONS and Link Header Discovery

**Feature Branch**: `019-options-link-discovery`
**Created**: 2026-03-15
**Status**: Done
**GitHub Issue**: #102
**Dependencies**: Frank core (`src/Frank/`) for `ResourceBuilder`/`ResourceSpec`/endpoint metadata; Frank.LinkedData (#75) and Frank.Statecharts (#87) as contributing extensions
**Location**: New extension package `Frank.Discovery` (shared OPTIONS/Link mechanism) with integration points in Frank.LinkedData and Frank.Statecharts
**Input**: GitHub issue #102 -- OPTIONS + Link header discovery for semantic media types

## Clarifications

### Session 2026-03-15

- Q: Should implicit OPTIONS support live in Frank core or as an extension? -> A: Extension, not core. CORS also uses OPTIONS, so extension isn't wrong. Core Frank should provide CEs on top of ASP.NET Core; extensions progressively enhance.
- Q: Which package should house the feature? -> A: Split across packages. A shared discovery mechanism handles OPTIONS/Link aggregation. Frank.LinkedData adds its RDF/ALPS media types when registered. Frank.Statecharts adds SCXML/XState/smcat/WSD types when registered. Each extension contributes its own OPTIONS content.
- Q: How should extensions register their media types for aggregation? -> A: Endpoint metadata aggregation. Each extension adds its media types as endpoint metadata (like `LinkedDataMarker`). The OPTIONS handler inspects metadata on the matched endpoint at request time to build the response dynamically. Per-resource, not global.

## User Scenarios & Testing

### User Story 1 - Agent Discovers Available Media Types via OPTIONS (Priority: P1)

An automated agent (or developer tool) sends an `OPTIONS` request to a Frank resource endpoint. The response includes an `Allow` header listing the HTTP methods the resource supports and an `Accept` header (or response body) listing the media types available for content negotiation on that resource. The agent uses this information to make an informed follow-up request with the correct `Accept` header.

**Why this priority**: Without OPTIONS discovery, agents must be pre-configured with knowledge of Frank's supported media types. This is the foundational capability that makes all other discovery possible -- an agent's first interaction with an unknown Frank application.

**Independent Test**: Register a resource with `get` and `post` handlers plus the `linkedData` marker. Enable the discovery middleware. Send `OPTIONS` to that resource's route. Verify the response includes `Allow: GET, POST, OPTIONS` and lists the LinkedData media types (`application/ld+json`, `text/turtle`, `application/rdf+xml`). Verify a resource *without* the `linkedData` marker returns only its registered HTTP methods with no extra media types.

**Acceptance Scenarios**:

1. **Given** a resource registered with `get` and `post` handlers and the `linkedData` marker, and the discovery middleware is enabled, **When** an `OPTIONS` request is sent to that resource's route, **Then** the response includes `Allow: GET, POST, OPTIONS` and lists the LinkedData media types.
2. **Given** a resource registered with `get` only and no semantic markers, **When** an `OPTIONS` request is sent, **Then** the response includes `Allow: GET, OPTIONS` with no semantic media types listed.
3. **Given** a resource registered with both `linkedData` and statechart spec media types, **When** an `OPTIONS` request is sent, **Then** the response lists media types from *both* extensions (RDF types + statechart spec types).
4. **Given** the discovery middleware is *not* registered, **When** an `OPTIONS` request is sent, **Then** no implicit OPTIONS response is generated (existing behavior unchanged).

---

### User Story 2 - Agent Follows Link Header to Semantic Profile (Priority: P2)

An agent sends a GET request to a Frank resource and receives the normal response (e.g., HTML or JSON). The response also includes a `Link` header pointing to the resource's semantic profile (e.g., `Link: </>;rel="describedby";type="application/alps+json"`). The agent follows this link with the indicated `Accept` header to retrieve the semantic description and learn the full set of capabilities.

**Why this priority**: Link headers provide passive discovery on every response -- agents don't need to know to send OPTIONS first. This complements OPTIONS by embedding discovery hints into normal interactions. However, it depends on the OPTIONS/discovery infrastructure being in place first.

**Independent Test**: Register a resource with the `linkedData` marker and enable Link header discovery (either per-resource or globally). Send a GET request. Verify the response includes a `Link` header with `rel="describedby"` and the correct media type. Follow the link with the specified Accept header and verify a semantic representation is returned.

**Acceptance Scenarios**:

1. **Given** a resource with `linkedData` enabled and Link header discovery enabled globally, **When** a GET request is sent, **Then** the response includes a `Link` header with `rel="describedby"` pointing to the resource's own URI with the ALPS media type.
2. **Given** a resource with both `linkedData` and statechart spec types, **When** a GET request is sent, **Then** the response includes Link headers for the primary semantic profiles from each registered extension.
3. **Given** Link header discovery is enabled but a resource has no semantic markers, **When** a GET request is sent, **Then** no `Link` header for semantic profiles is included.
4. **Given** Link header discovery is *not* enabled (opt-in not activated), **When** a GET request is sent, **Then** no `Link` header is included regardless of semantic markers.

---

### User Story 3 - Developer Controls Discovery Per-Resource (Priority: P2)

A Frank developer wants discovery on some resources but not others. Per-resource control is achieved implicitly by choosing which resources receive semantic markers (e.g., `linkedData`, statechart spec types). Resources with `DiscoveryMediaType` metadata entries get Link headers; resources without them do not. No dedicated `discoverable` operation is needed.

**Why this priority**: Per-resource granularity gives developers control over which resources advertise themselves. Some resources may be internal or not meaningful for agent discovery.

**Independent Test**: Register two resources: one with a semantic marker (e.g., `linkedData`) that adds `DiscoveryMediaType` metadata, one without any semantic markers. Enable Link header discovery globally. Send GET requests to both. Verify only the semantically-marked resource includes Link headers.

**Acceptance Scenarios**:

1. **Given** a resource with semantic markers (e.g., `linkedData`) that add `DiscoveryMediaType` metadata, and Link header discovery is enabled, **When** a GET request is sent, **Then** the response includes appropriate `Link` headers.
2. **Given** a resource *without* any semantic markers (no `DiscoveryMediaType` metadata), **When** a GET request is sent, **Then** no `Link` headers are emitted, even if Link header discovery is enabled globally.
3. **Given** global Link header discovery is enabled, **When** a GET is sent to any resource with `DiscoveryMediaType` metadata from any extension, **Then** Link headers are included automatically.

---

### User Story 4 - Developer Enables Discovery Globally (Priority: P3)

A Frank developer wants all semantically-marked resources to automatically include Link headers without opting in each resource individually. They use a `WebHostBuilder` operation (e.g., `useDiscovery`) to enable Link headers globally for all resources that have semantic metadata.

**Why this priority**: For applications where all resources are agent-facing, global opt-in reduces boilerplate. This is a convenience layer over per-resource control.

**Independent Test**: Enable global discovery via `WebHostBuilder`. Register multiple resources with varying semantic markers. Verify all semantically-marked resources include Link headers and unmarked resources do not.

**Acceptance Scenarios**:

1. **Given** global discovery is enabled via `WebHostBuilder`, **When** a GET is sent to a resource with `linkedData`, **Then** Link headers are included.
2. **Given** global discovery is enabled, **When** a GET is sent to a resource with *no* semantic markers, **Then** no Link headers are included.
3. **Given** global discovery is enabled and a resource has both LinkedData and statechart markers, **When** a GET is sent, **Then** Link headers reflect all registered semantic profiles.

---

### Edge Cases

- **CORS preflight interaction**: ASP.NET Core's CORS middleware also handles OPTIONS. The discovery OPTIONS handler must coexist with CORS -- if CORS middleware is registered, it should handle CORS preflights, and the discovery middleware handles non-CORS OPTIONS (or augments CORS OPTIONS with discovery information). The discovery middleware should not interfere with CORS `Access-Control-*` headers.
- **Resource with explicit OPTIONS handler**: If a developer defines an explicit `options` handler on a resource, the implicit discovery OPTIONS handler must not conflict. The explicit handler should take precedence (the developer's intent overrides automatic behavior).
- **Empty handler list**: A resource with no handlers registered (edge case in the CE) should still respond to OPTIONS if the discovery middleware is active, but with only `Allow: OPTIONS` and no media types.
- **Multiple Link headers**: RFC 8288 allows multiple `Link` headers or comma-separated values in a single header. When multiple extensions contribute profiles, they should each add their own `Link` value.
- **Link header on error responses**: Link headers should only be included on successful responses (2xx status codes). Error responses (4xx, 5xx) should not include discovery Link headers.
- **OPTIONS response body**: The OPTIONS response body format needs to be defined. An empty body with headers only is the minimal approach; a structured body (e.g., JSON listing media types with descriptions) is a possible enhancement.
- **HEAD requests**: Link headers should also be included in HEAD responses (HEAD is GET without the body, per HTTP semantics).
- **Media type deduplication**: If two extensions contribute the same media type, it should appear only once in the OPTIONS response and Link headers.

## Requirements

### Functional Requirements

- **FR-001**: System MUST provide an implicit OPTIONS response for any resource when the discovery middleware is registered, returning an `Allow` header listing all HTTP methods that resource has handlers for, plus `OPTIONS` itself
- **FR-002**: System MUST aggregate media type information from endpoint metadata contributed by registered extensions (e.g., Frank.LinkedData, Frank.Statecharts) and communicate them on the OPTIONS response via `Link` headers with `rel="describedby"` (the same Link header format used for GET/HEAD responses per FR-004). The OPTIONS response therefore returns: an `Allow` header (listing HTTP methods), `Link` headers (listing available media types), and an empty body
- **FR-003**: System MUST define a standard endpoint metadata type for media type discovery that any extension can use to register its supported media types on a per-resource basis
- **FR-004**: System MUST include `Link` headers with `rel="describedby"` on responses from semantically-marked resources when Link header discovery is enabled, following RFC 8288 (Web Linking)
- **FR-005**: System MUST support opt-in for Link header discovery both per-resource (implicitly, by whether an extension adds `DiscoveryMediaType` metadata to a resource -- e.g., via the `linkedData` custom operation) and globally (via `useLinkHeaders`/`useDiscovery` WebHostBuilder operations that register the `LinkHeaderMiddleware`)
- **FR-006**: System MUST NOT alter behavior of resources without semantic metadata markers -- no Link headers added, no media types listed beyond the resource's registered HTTP methods in the Allow header
- **FR-007**: System MUST NOT interfere with existing explicit `options` handlers defined by the developer on a resource -- explicit handlers take precedence over the implicit discovery handler
- **FR-008**: System MUST NOT interfere with CORS middleware -- CORS preflight handling and `Access-Control-*` headers remain unaffected
- **FR-009**: System MUST deduplicate media types when multiple extensions contribute the same type to a resource's metadata
- **FR-010**: System MUST include Link headers on successful responses only (2xx status codes) and on HEAD responses in addition to GET
- **FR-011**: Frank.LinkedData MUST contribute its supported media types (`application/ld+json`, `text/turtle`, `application/rdf+xml`, and ALPS if registered) as endpoint metadata when the `linkedData` marker is applied to a resource
- **FR-012**: Frank.Statecharts MUST contribute its spec media types (SCXML, XState, smcat, etc.) as endpoint metadata when statechart spec support is applied to a resource
- **FR-013**: The OPTIONS response MUST return a 200 status code with an empty body (headers only) for the initial implementation
- **FR-014**: System MUST support multiple `Link` header values per response when multiple semantic profiles are available, per RFC 8288

### Key Entities

- **DiscoveryMediaType**: Endpoint metadata type that represents a media type available for content negotiation on a resource. Extensions add instances of this to endpoint metadata during resource registration. Contains at minimum the media type string (e.g., `application/ld+json`) and optionally a `rel` value for Link header generation.
- **DiscoveryMiddleware**: Middleware that handles implicit OPTIONS responses by inspecting endpoint metadata for registered HTTP methods and `DiscoveryMediaType` entries. Also responsible for appending Link headers to non-OPTIONS responses when Link discovery is enabled. Per-resource Link header emission is triggered implicitly by the presence of any `DiscoveryMediaType` entries in an endpoint's metadata (no separate marker needed).

## Success Criteria

### Measurable Outcomes

- **SC-001**: An agent sending `OPTIONS` to any resource with the discovery middleware enabled receives a correct `Allow` header and media type listing within the same response time envelope as a normal request (no measurable latency regression)
- **SC-002**: Resources with semantic markers include all contributed media types in OPTIONS responses; resources without markers list only their registered HTTP methods
- **SC-003**: Link headers on GET/HEAD responses from semantically-marked resources contain valid RFC 8288 values that, when followed with the indicated Accept header, return a valid semantic representation
- **SC-004**: Explicit `options` handlers defined by developers are never overridden or duplicated by the discovery middleware
- **SC-005**: CORS preflight requests continue to work correctly when both CORS and discovery middleware are registered
- **SC-006**: The discovery mechanism is composable -- adding a new extension that contributes media types requires only adding endpoint metadata, with no changes to the discovery middleware itself
- **SC-007**: Enabling discovery (OPTIONS + Link headers) adds zero overhead to resources without semantic metadata markers


## Research

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
