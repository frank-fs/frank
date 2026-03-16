# Data Model: OPTIONS and Link Header Discovery

**Date**: 2026-03-16
**Feature**: 019-options-link-discovery

## Entities

### DiscoveryMediaType (Frank core -- `src/Frank/Builder.fs`)

A value type representing a media type available for content negotiation on a resource endpoint. Extensions add instances of this to endpoint metadata during resource registration. The discovery middlewares read these entries at request time.

```fsharp
/// Media type metadata for HTTP discovery (OPTIONS + Link headers).
/// Extensions add instances to endpoint metadata to advertise supported content types.
[<Struct>]
type DiscoveryMediaType =
    { /// The content type string (e.g., "application/ld+json", "text/turtle").
      MediaType: string
      /// The link relation type for Link header generation (e.g., "describedby").
      Rel: string }
```

**Fields**:

| Field | Type | Description | Example |
|-------|------|-------------|---------|
| `MediaType` | `string` | IANA media type string | `"application/ld+json"` |
| `Rel` | `string` | RFC 8288 link relation type | `"describedby"` |

**Constraints**:
- `MediaType` must be a valid IANA media type string (not validated at registration time -- responsibility of the contributing extension)
- `Rel` must be a valid registered or extension relation type per RFC 8288
- Struct semantics -- no reference equality, compared by value

**Lifecycle**: Created at resource registration time (in `ResourceBuilder` custom operations). Stored in endpoint metadata (`EndpointBuilder.Metadata`). Read at request time by discovery middlewares. Immutable after endpoint build.

### OptionsDiscoveryMiddleware (Frank.Discovery -- `src/Frank.Discovery/OptionsDiscoveryMiddleware.fs`)

ASP.NET Core middleware that generates implicit OPTIONS responses. Not a persistent entity -- it is a request-scoped processing component.

**Constructor Dependencies**:

| Dependency | Type | Source |
|------------|------|--------|
| `next` | `RequestDelegate` | ASP.NET Core middleware pipeline |
| `dataSource` | `EndpointDataSource` | DI -- provides access to all registered endpoints |
| `logger` | `ILogger<OptionsDiscoveryMiddleware>` | DI -- logging |

**Request-time behavior**:
1. Check if request method is OPTIONS
2. If not OPTIONS, call `next` (pass through)
3. Check for CORS preflight (`Access-Control-Request-Method` header present) -- if so, call `next`
4. Get matched endpoint via `ctx.GetEndpoint()`
5. If null (no route match), call `next`
6. Check if matched endpoint has `HttpMethodMetadata` containing "OPTIONS" (explicit handler) -- if so, call `next`
7. Find all sibling endpoints with same `RoutePattern.RawText`
8. Collect HTTP methods from `HttpMethodMetadata` on all siblings, add "OPTIONS"
9. Collect and deduplicate `DiscoveryMediaType` entries from all siblings
10. Set `Allow` header, return 200 with empty body

### LinkHeaderMiddleware (Frank.Discovery -- `src/Frank.Discovery/LinkHeaderMiddleware.fs`)

ASP.NET Core middleware that appends RFC 8288 Link headers to successful GET/HEAD responses from endpoints with `DiscoveryMediaType` metadata.

**Constructor Dependencies**:

| Dependency | Type | Source |
|------------|------|--------|
| `next` | `RequestDelegate` | ASP.NET Core middleware pipeline |
| `logger` | `ILogger<LinkHeaderMiddleware>` | DI -- logging |

**Request-time behavior**:
1. Get matched endpoint via `ctx.GetEndpoint()`
2. If null, call `next` (pass through)
3. Collect `DiscoveryMediaType` entries from endpoint metadata
4. If none, call `next` (zero overhead for unmarked resources -- SC-007)
5. Call `next` to execute the handler
6. After handler execution, check response status code
7. If 2xx and method is GET or HEAD, append Link headers
8. Each `DiscoveryMediaType` produces: `Link: <{requestPath}>; rel="{Rel}"; type="{MediaType}"`
9. Deduplicate by `(MediaType, Rel)` tuple

## Relationships

```
Frank Core (Builder.fs)
├── DiscoveryMediaType [struct]        ← defined here
│
Frank.LinkedData (ResourceBuilderExtensions.fs)
├── linkedData custom operation
│   └── adds DiscoveryMediaType { MediaType = "application/ld+json"; Rel = "describedby" }
│   └── adds DiscoveryMediaType { MediaType = "text/turtle"; Rel = "describedby" }
│   └── adds DiscoveryMediaType { MediaType = "application/rdf+xml"; Rel = "describedby" }
│
Frank.Statecharts (ResourceBuilderExtensions.fs)
├── stateMachine custom operation
│   └── adds DiscoveryMediaType entries for statechart spec media types
│
Frank.Discovery
├── OptionsDiscoveryMiddleware         ← reads DiscoveryMediaType from endpoint metadata
├── LinkHeaderMiddleware               ← reads DiscoveryMediaType from endpoint metadata
└── WebHostBuilderExtensions
    ├── useOptionsDiscovery            ← registers OptionsDiscoveryMiddleware
    ├── useLinkHeaders                 ← registers LinkHeaderMiddleware
    └── useDiscovery                   ← registers both middlewares
```

## State Transitions

N/A -- this feature is stateless. All data is compile-time/startup-time metadata read at request time.

## Media Types Contributed by Extensions

### Frank.LinkedData

| MediaType | Rel | Condition |
|-----------|-----|-----------|
| `application/ld+json` | `describedby` | `linkedData` marker applied |
| `text/turtle` | `describedby` | `linkedData` marker applied |
| `application/rdf+xml` | `describedby` | `linkedData` marker applied |

### Frank.Statecharts

The specific media types contributed by Frank.Statecharts depend on which statechart spec formats are supported. These will be determined during implementation by inspecting the existing `StateMachineMetadata` type and WSD/SCXML support. Candidate types include:

| MediaType | Rel | Condition |
|-----------|-----|-----------|
| `application/scxml+xml` | `describedby` | `stateMachine` metadata applied |

Note: The exact set of Statecharts media types should be confirmed during implementation. The discovery mechanism is composable -- adding new types requires only adding `DiscoveryMediaType` metadata entries.
