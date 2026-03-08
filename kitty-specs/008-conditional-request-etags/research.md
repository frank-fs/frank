# Research: Conditional Request ETags

**Feature**: 008-conditional-request-etags
**Date**: 2026-03-07

## Decision 1: Hashing Strategy

### Options Considered

| Option | Deterministic | Speed | Collision Resistance | Cross-Version Stability |
|--------|--------------|-------|---------------------|------------------------|
| `HashCode.Combine` | No (runtime-seeded) | ~5ns | 32-bit (poor) | No -- changes per process restart |
| SHA-256 (truncated) | Yes | ~200ns (small inputs) | 128-bit birthday bound | Yes -- algorithm is standardized |
| `System.IO.Hashing.XxHash128` | Yes | ~30ns | 128-bit | Yes -- deterministic, .NET 8+ |
| Structural hashing via `%A` + SHA-256 | Yes | ~500ns (sprintf overhead) | 128-bit | Fragile -- depends on `%A` format |

### Decision: SHA-256 truncated to 128 bits (hex-encoded, 32 chars)

**Rationale**:

1. **`HashCode.Combine` is disqualified** -- it uses per-process randomized seeding (Marvin32 on .NET). ETags computed in one process would not match ETags from a restarted process for the same state. This violates FR-010 (ETags must change when state changes, but must NOT change when state is unchanged).

2. **SHA-256 is the safe default** -- deterministic, standardized, available on all .NET target frameworks (net8.0+) via `System.Security.Cryptography.SHA256`. Truncating to 128 bits (16 bytes, 32 hex chars) gives a birthday bound of ~2^64 operations before 50% collision probability -- astronomically unlikely for resource ETag spaces.

3. **XxHash128 is faster but newer** -- available in `System.IO.Hashing` (.NET 8+). It is deterministic and 128-bit. However, it is not a cryptographic hash, and adding a NuGet dependency (`System.IO.Hashing`) conflicts with the "ASP.NET Core built-ins only" constraint. For net8.0+ it is in the shared framework, so this could be reconsidered. SHA-256 is the conservative choice.

4. **Structural hashing via `%A`** -- Using F#'s `sprintf "%A"` to serialize state, then hashing the string, is fragile. The `%A` format is not guaranteed stable across F# compiler versions. Instead, the `IETagProvider` contract requires implementations to produce deterministic byte sequences from their state.

### Input Serialization for SHA-256

The `IETagProvider<'State, 'Context>` interface receives a `('State * 'Context)` pair. The provider is responsible for serializing this into a byte sequence for hashing. The default `StatechartETagProvider` uses:

1. Convert `'State` to its string representation via `string` (works for DU cases)
2. Convert `'Context` fields to bytes via a user-supplied `'Context -> byte[]` function (or BinaryFormatter-free serialization)
3. Concatenate and SHA-256 hash

This avoids dependence on `%A` formatting while keeping the interface simple. Custom providers can use any serialization strategy.

## Decision 2: ETag Format (RFC 9110 Compliance)

### RFC 9110 Section 8.8.3 -- Entity Tag

```
entity-tag = [ weak ] opaque-tag
weak       = %s"W/"
opaque-tag = DQUOTE *etagc DQUOTE
etagc      = %x21 / %x23-7E / obs-text
```

Key rules:
- Strong ETags are quoted strings: `"a1b2c3d4e5f6"`
- The quotes are part of the value (included in the `ETag` header)
- Strong comparison: two ETags match if and only if both are not weak and their opaque-tags are identical character-by-character
- `If-None-Match` uses weak comparison (but since we only produce strong ETags, this is equivalent to strong comparison)
- `If-Match` uses strong comparison

### Decision: 32-character hex-encoded SHA-256 prefix, double-quoted

Format: `"<32 hex chars>"` (e.g., `"a1b2c3d4e5f67890a1b2c3d4e5f67890"`)

**Rationale**:
- 32 hex characters = 128 bits of the SHA-256 output, providing ample collision resistance
- Hex encoding uses only `%x30-39` and `%x61-66`, all valid `etagc` characters
- Double quotes included in the stored/transmitted value per RFC 9110
- No weak prefix (`W/`) -- strong ETags only in initial implementation
- Deterministic: same state always produces the same ETag string

### ETagFormat Module

```fsharp
module ETagFormat =
    /// Wrap a raw hash string in double quotes for RFC 9110 strong ETag format
    let quote (raw: string) : string = "\"" + raw + "\""

    /// Remove surrounding double quotes from an ETag value
    let unquote (etag: string) : string =
        if etag.Length >= 2 && etag.[0] = '"' && etag.[etag.Length - 1] = '"' then
            etag.[1..etag.Length - 2]
        else
            etag

    /// Check if an ETag value is a weak ETag (W/"...")
    let isWeak (etag: string) : bool =
        etag.StartsWith("W/\"", StringComparison.Ordinal)
```

## Decision 3: Middleware Pipeline Position

### ASP.NET Core Middleware Ordering in Frank

Frank's `WebHostBuilder` provides two middleware insertion points:
- `plugBeforeRouting` -- runs before `UseRouting()` (e.g., CORS, static files)
- `plug` -- runs after `UseRouting()` (e.g., auth, content negotiation)

The conditional request middleware needs:
1. **Access to endpoint metadata** -- requires routing to have resolved the endpoint (after `UseRouting()`)
2. **Access to current resource state** -- requires state stores to be available via DI
3. **Ability to short-circuit** -- must run before the handler executes

### Decision: Register via `plug` (after routing, before handler execution)

Recommended ordering in the `webHost` CE:

```
plugBeforeRouting (CORS, static files)
UseRouting()            -- implicit, inserted by WebHostBuilder
plug useConditionalRequests   -- ETag middleware (this feature)
plug useAuth                  -- Auth middleware (if used)
plug useStatecharts           -- Statecharts middleware (if used)
```

**Rationale**:
- The ETag middleware must run after routing so it can read `ETagMetadata` from the matched endpoint
- It should run before auth/statecharts so that 304 responses skip unnecessary auth evaluation for safe GET requests (performance optimization)
- For mutations (POST/PUT/DELETE with `If-Match`), the ETag middleware checks preconditions before the handler runs -- a 412 response avoids executing the mutation entirely
- If auth rejects the request (401/403), the ETag middleware never sees the conditional headers because it ran first -- but this is acceptable because unauthorized clients should not receive 304 responses that confirm resource existence

**Alternative considered**: Running after auth would prevent 304 responses from leaking resource existence to unauthorized clients. However, ETags are already public information (sent in prior GET responses), and the client must have previously authenticated to obtain the ETag. The performance benefit of skipping auth on 304 responses outweighs this concern.

## Decision 4: MailboxProcessor Cache Design

### Message Types

```fsharp
type ETagCacheMessage =
    | GetETag of resourceKey: string * AsyncReplyChannel<string option>
    | SetETag of resourceKey: string * etag: string
    | InvalidateETag of resourceKey: string
    | InvalidateAll
    | GetStats of AsyncReplyChannel<CacheStats>
```

### Cache Design

The `ETagCache` wraps a `MailboxProcessor<ETagCacheMessage>` managing a `Dictionary<string, CacheEntry>`:

```fsharp
type CacheEntry =
    { ETag: string
      LastAccessed: DateTimeOffset
      ComputedAt: DateTimeOffset }
```

**Eviction strategy**: LRU eviction when the cache exceeds a configurable maximum size (default: 10,000 entries). On each `SetETag`, if the cache is at capacity, the least-recently-accessed entry is removed. The `LastAccessed` timestamp is updated on both `GetETag` and `SetETag`.

**Why MailboxProcessor over ConcurrentDictionary**:
1. **Atomic read-compute-write**: When a cache miss occurs, the middleware must compute the ETag (via `IETagProvider`) and store it. With `ConcurrentDictionary`, two concurrent requests for the same resource could both compute the ETag simultaneously (wasted work). The MailboxProcessor serializes these operations.
2. **Consistent with Frank.Statecharts**: The `MailboxProcessorStore` pattern is already established. Using the same concurrency primitive keeps the codebase consistent.
3. **Cache invalidation atomicity**: When a state transition occurs, the ETag must be invalidated atomically. The MailboxProcessor guarantees this without additional locking.

**Cache key format**: The resource key is derived from the request path and route values. For parameterized routes (e.g., `/games/{gameId}`), the resolved path (e.g., `/games/42`) is the key. This ensures each resource instance has its own ETag.

### Lifecycle

- The `ETagCache` is registered as a singleton in DI via `IServiceCollection`
- It implements `IDisposable`, disposing the inner `MailboxProcessor` on application shutdown
- The `MailboxProcessor.Error` event is wired to `ILogger.LogError` per Constitution principle VII

## Decision 5: StatechartETagProvider Integration

### Integration with IStateMachineStore

The `StatechartETagProvider<'State, 'Context>` retrieves state from `IStateMachineStore<'State, 'Context>` and hashes it:

```fsharp
type StatechartETagProvider<'State, 'Context
    when 'State : equality>(store: IStateMachineStore<'State, 'Context>,
                            contextSerializer: 'Context -> byte[]) =
    interface IETagProvider with
        member _.ComputeETag(instanceId: string) : Task<string option> = task {
            match! store.GetState(instanceId) with
            | Some (state, context) ->
                let stateBytes = System.Text.Encoding.UTF8.GetBytes(string state)
                let contextBytes = contextSerializer context
                let combined = Array.append stateBytes contextBytes
                use sha256 = System.Security.Cryptography.SHA256.Create()
                let hash = sha256.ComputeHash(combined)
                let hex = hash |> Array.take 16 |> Array.map (fun b -> b.ToString("x2")) |> String.concat ""
                return Some (ETagFormat.quote hex)
            | None -> return None
        }
```

### Cache Invalidation on State Transitions

The `StatechartETagProvider` subscribes to state transition events (via `IStateMachineStore.Subscribe`) and sends `InvalidateETag` messages to the `ETagCache` when transitions occur. This ensures that:

1. The next GET request after a state transition computes a fresh ETag
2. The cache never serves a stale ETag for a resource that has transitioned

The subscription is established when the middleware starts and uses `IDisposable` (Constitution principle VI) for cleanup.

### Resources Without Statecharts

For plain resources using a custom `IETagProvider`, the provider is responsible for knowing when its state changes. The cache invalidation is triggered by the provider itself (or by the middleware detecting a mutation response, as a fallback). The middleware invalidates the cache entry for a resource after any successful mutation (POST, PUT, DELETE) response, forcing a recomputation on the next GET.

## Open Questions (Deferred)

1. **Weak ETags**: Supporting `W/"..."` for semantic equivalence (e.g., same logical state but different serialization). Deferred per spec.
2. **Per-resource opt-out**: Allowing individual resources to disable ETag generation when the middleware is registered framework-wide. Planned as future enhancement per spec.
3. **Distributed caching**: The MailboxProcessor cache is single-node. Multi-node ETag consistency would require a distributed cache (Redis, etc.). Out of scope.
4. **If-Unmodified-Since / If-Modified-Since**: Date-based conditional headers are a separate concern from ETags. Not in scope.
