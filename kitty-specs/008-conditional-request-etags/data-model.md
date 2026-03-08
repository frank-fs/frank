# Data Model: Conditional Request ETags

**Feature**: 008-conditional-request-etags
**Date**: 2026-03-07

## Entity Relationship Overview

```
IETagProvider<'State, 'Context>
       │
       │ implemented by
       ├──────────────────────> StatechartETagProvider<'S,'C>
       │                              │
       │                              │ reads state from
       │                              v
       │                       IStateMachineStore<'S,'C>
       │
       │ computes
       v
   ETag (string, RFC 9110)
       │
       │ cached in
       v
   ETagCache (MailboxProcessor)
       │
       │ queried by
       v
ConditionalRequestMiddleware
       │
       │ discovers via
       v
   ETagMetadata (endpoint metadata)
       │
       │ evaluates
       ├──> If-None-Match ──> 304 Not Modified (GET/HEAD)
       └──> If-Match ──────> 412 Precondition Failed (POST/PUT/DELETE)
```

## Core Entities

### IETagProvider

Abstraction for computing an ETag string from a resource instance. Implementations may hash statechart state, database version stamps, or any domain state. The interface is intentionally non-generic at the consumption site (middleware uses `IETagProvider`) to avoid generic type leakage into endpoint metadata.

```fsharp
/// Non-generic interface consumed by the middleware.
/// The middleware does not know the state/context types --
/// it only needs an ETag string for a given resource instance.
type IETagProvider =
    /// Compute the current ETag for a resource instance.
    /// Returns None if the resource has no state (e.g., deleted or not yet created).
    abstract ComputeETag: instanceId: string -> Task<string option>
```

| Method | Signature | Description |
|--------|-----------|-------------|
| ComputeETag | `string -> Task<string option>` | Compute ETag for a resource instance by ID. Returns `None` if no state exists. |

**Identity**: One provider per resource type (registered via DI or endpoint metadata).
**Lifecycle**: Created at application startup, typically singleton.
**Key constraint**: Must be deterministic -- same state must always produce the same ETag string.

### IETagProviderFactory

Factory abstraction that creates `IETagProvider` instances for specific resource endpoints. This enables the middleware to resolve the correct provider for each request without generic type parameters in the middleware itself.

```fsharp
type IETagProviderFactory =
    /// Create a provider for the given endpoint, or None if the endpoint
    /// does not participate in conditional request handling.
    abstract CreateProvider: endpoint: Endpoint -> IETagProvider option
```

| Method | Signature | Description |
|--------|-----------|-------------|
| CreateProvider | `Endpoint -> IETagProvider option` | Resolve a provider for the matched endpoint |

### ETagMetadata

Endpoint metadata marker indicating a resource participates in conditional request handling. Attached to endpoints during resource building. The middleware checks for this marker to avoid processing non-participating resources.

```fsharp
[<Sealed>]
type ETagMetadata(providerKey: string, instanceIdResolver: HttpContext -> string) =
    /// Key used to look up the IETagProvider in DI or a provider registry
    member _.ProviderKey: string = providerKey

    /// Extracts the resource instance ID from the request context
    /// (typically from route values, e.g., ctx.GetRouteValue("gameId") |> string)
    member _.ResolveInstanceId: HttpContext -> string = instanceIdResolver
```

| Attribute | Type | Description |
|-----------|------|-------------|
| ProviderKey | `string` | Identifies which `IETagProvider` handles this resource |
| ResolveInstanceId | `HttpContext -> string` | Extracts resource instance ID from the request |

**Identity**: One per resource endpoint (attached to endpoint metadata during build).
**Lifecycle**: Created at application startup, immutable thereafter.
**Key relationship**: The middleware reads `ETagMetadata` from `Endpoint.Metadata` to determine if conditional request processing applies (same pattern as `StateMachineMetadata` in Frank.Statecharts).

### ETagCache

MailboxProcessor-backed concurrent cache mapping resource instance keys to their current ETag values. Serializes all access to prevent races between concurrent requests and state transitions.

```fsharp
type CacheEntry =
    { ETag: string
      LastAccessed: DateTimeOffset
      ComputedAt: DateTimeOffset }

type ETagCacheMessage =
    | GetETag of resourceKey: string * AsyncReplyChannel<string option>
    | SetETag of resourceKey: string * etag: string
    | InvalidateETag of resourceKey: string
    | InvalidateAll
    | GetStats of AsyncReplyChannel<CacheStats>

type CacheStats =
    { EntryCount: int
      HitCount: int64
      MissCount: int64 }

type ETagCache(maxEntries: int, logger: ILogger<ETagCache>) =
    let agent = MailboxProcessor<ETagCacheMessage>.Start(fun inbox -> ...)

    member _.GetETag(resourceKey: string) : Async<string option>
    member _.SetETag(resourceKey: string, etag: string) : unit
    member _.Invalidate(resourceKey: string) : unit
    member _.InvalidateAll() : unit
    member _.GetStats() : Async<CacheStats>

    interface IDisposable with
        member _.Dispose() = (agent :> IDisposable).Dispose()
```

| Method | Signature | Description |
|--------|-----------|-------------|
| GetETag | `string -> Async<string option>` | Retrieve cached ETag (reply channel, blocking) |
| SetETag | `string * string -> unit` | Store/update ETag (fire-and-forget) |
| Invalidate | `string -> unit` | Remove a specific cache entry (fire-and-forget) |
| InvalidateAll | `unit -> unit` | Clear entire cache |
| GetStats | `unit -> Async<CacheStats>` | Diagnostic: cache hit/miss statistics |

**Identity**: Singleton per application (registered in DI).
**Lifecycle**: Created on middleware registration, disposed on application shutdown.
**Eviction**: LRU when `maxEntries` exceeded (default: 10,000).

### ConditionalRequestMiddleware

ASP.NET Core middleware that intercepts requests to ETag-enabled resources, evaluates conditional headers, and short-circuits with 304/412 when appropriate.

```fsharp
type ConditionalRequestMiddleware(next: RequestDelegate,
                                   cache: ETagCache,
                                   providerFactory: IETagProviderFactory,
                                   logger: ILogger<ConditionalRequestMiddleware>) =

    member _.Invoke(ctx: HttpContext) : Task
```

**Request processing flow**:

1. Read matched endpoint from `HttpContext.GetEndpoint()`
2. Check for `ETagMetadata` in endpoint metadata -- if absent, call `next` and return
3. Resolve resource instance ID via `ETagMetadata.ResolveInstanceId(ctx)`
4. Look up current ETag from cache (or compute via `IETagProvider` on cache miss)
5. Evaluate conditional headers:
   - **GET/HEAD with `If-None-Match`**: If any ETag matches (or `*` and resource exists), return 304
   - **POST/PUT/DELETE with `If-Match`**: If no ETag matches (and not `*`), return 412
6. If not short-circuited, call `next` and set `ETag` header on successful response
7. After successful mutations, invalidate the cache entry for this resource instance

### ETagComparison

Pure functions for RFC 9110 ETag comparison semantics.

```fsharp
module ETagComparison =
    /// Strong comparison (RFC 9110 Section 8.8.3.2):
    /// Two ETags match if both are strong and their opaque-tags are identical.
    let strongMatch (etag1: string) (etag2: string) : bool

    /// Parse an If-None-Match header value into individual ETag values.
    /// Handles comma-separated lists and the wildcard "*".
    let parseIfNoneMatch (headerValue: string) : string list

    /// Parse an If-Match header value into individual ETag values.
    let parseIfMatch (headerValue: string) : string list

    /// Check if any ETag in the list matches the current ETag.
    /// Handles wildcard "*" (matches any existing resource).
    let anyMatch (currentETag: string option) (headerETags: string list) : bool
```

| Function | Signature | Description |
|----------|-----------|-------------|
| strongMatch | `string -> string -> bool` | RFC 9110 strong comparison of two ETags |
| parseIfNoneMatch | `string -> string list` | Parse `If-None-Match` header (comma-separated, wildcard) |
| parseIfMatch | `string -> string list` | Parse `If-Match` header (comma-separated, wildcard) |
| anyMatch | `string option -> string list -> bool` | Check if any header ETag matches current |

### ETagFormat

Pure functions for RFC 9110 ETag formatting.

```fsharp
module ETagFormat =
    /// Wrap a raw hash hex string in double quotes for strong ETag format.
    /// "a1b2c3" -> "\"a1b2c3\""
    let quote (raw: string) : string

    /// Remove surrounding double quotes.
    /// "\"a1b2c3\"" -> "a1b2c3"
    let unquote (etag: string) : string

    /// Check if an ETag uses the weak prefix W/"..."
    let isWeak (etag: string) : bool

    /// Compute a strong ETag from raw bytes using SHA-256, truncated to 128 bits.
    let computeFromBytes (data: byte[]) : string
```

### StatechartETagProvider

Default `IETagProvider` implementation for statechart-backed resources. Reads the current `('State * 'Context)` pair from `IStateMachineStore` and hashes it with SHA-256.

```fsharp
type StatechartETagProvider<'State, 'Context when 'State : equality>
    (store: IStateMachineStore<'State, 'Context>,
     contextSerializer: 'Context -> byte[]) =

    interface IETagProvider with
        member _.ComputeETag(instanceId: string) : Task<string option>
```

| Attribute | Type | Description |
|-----------|------|-------------|
| store | `IStateMachineStore<'State, 'Context>` | State retrieval |
| contextSerializer | `'Context -> byte[]` | Converts context to bytes for hashing |

**Identity**: One per stateful resource type.
**Lifecycle**: Created at middleware registration, singleton.
**Key relationship**: Reads from `IStateMachineStore` (Frank.Statecharts dependency). Subscribes to state transitions for cache invalidation.

## Relationships

1. **ETagMetadata -> Endpoint**: One `ETagMetadata` marker per ETag-enabled endpoint (attached via `EndpointBuilder.Metadata.Add`). The middleware discovers this during request processing. Endpoints without `ETagMetadata` are not processed.

2. **IETagProvider -> ETagCache**: The middleware uses the provider to compute ETags on cache misses, then stores the result in the cache. The cache is the intermediary between the provider (state source) and the middleware (consumer).

3. **StatechartETagProvider -> IStateMachineStore**: The default provider delegates state retrieval to the existing statecharts store. This is a read-only dependency -- the provider never writes state.

4. **StatechartETagProvider -> ETagCache (invalidation)**: On state transitions, the provider invalidates the corresponding cache entry. This is a write-only dependency (fire-and-forget `Invalidate` message).

5. **ConditionalRequestMiddleware -> ETagComparison**: The middleware delegates all ETag comparison logic to the pure `ETagComparison` module. This separation enables unit testing of comparison logic without HTTP infrastructure.

6. **ResourceBuilder -> ETagMetadata**: When building a resource with an `IETagProvider`, the builder attaches `ETagMetadata` to endpoint metadata (same pattern as `StateMachineMetadata` in Frank.Statecharts).

## State Transitions

The following diagram shows how an ETag flows through the system for a typical request lifecycle:

```
First GET:
  Client ──GET──> Middleware ──cache miss──> IETagProvider ──hash──> ETag
                  Middleware ──cache set──> ETagCache
                  Middleware ──set header──> Response (200, ETag: "abc")

Conditional GET (unchanged):
  Client ──GET, If-None-Match: "abc"──> Middleware ──cache hit──> "abc"
                                         Middleware ──compare──> match!
                                         Middleware ──304──> Client (no body)

Mutation:
  Client ──POST, If-Match: "abc"──> Middleware ──cache hit──> "abc"
                                     Middleware ──compare──> match!
                                     Middleware ──invoke handler──> state transition
                                     Middleware ──invalidate cache──> ETagCache
                                     Middleware ──200──> Client (new ETag: "def")

Mutation with stale ETag:
  Client ──POST, If-Match: "old"──> Middleware ──cache hit──> "abc"
                                     Middleware ──compare──> no match!
                                     Middleware ──412──> Client (Precondition Failed)
```
