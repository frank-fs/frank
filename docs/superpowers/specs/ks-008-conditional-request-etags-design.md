---
source: kitty-specs/008-conditional-request-etags
status: complete
type: spec
---

# Feature Specification: Conditional Request ETags

**Feature Branch**: `008-conditional-request-etags`
**Created**: 2026-03-07
**Status**: Done
**GitHub Issue**: #93
**Dependencies**: Frank.Statecharts (#87, `statefulResource` CE) for default provider
**Location**: Frank core (`src/Frank/`) — conditional requests are fundamental HTTP semantics
**Input**: Automatic ETag generation from resource state for conditional request handling (304 Not Modified, 412 Precondition Failed)

## Clarifications

### Session 2026-03-07

- Q: Where should conditional request support live? → A: Frank core (`src/Frank/`) — ETags are fundamental HTTP semantics, not specific to statecharts.

## Design Decision: Framework-Wide Opt-In

Conditional request support is enabled **framework-wide** when the developer registers the ETag middleware. Once enabled, all stateful resources with an available `IETagProvider` automatically participate in conditional request handling. Per-resource granularity (enabling/disabling ETag generation on individual resources) is planned as a future enhancement. This approach is consistent with Frank.Provenance's framework-wide-by-default pattern and the MailboxProcessor-backed in-memory default.

## User Scenarios & Testing

### User Story 1 - Automatic ETag Generation on GET Responses (Priority: P1)

A Frank developer enables conditional request support framework-wide by registering the ETag middleware. All stateful resources with an available `IETagProvider` automatically include `ETag` headers in GET responses reflecting the current domain state.

**Why this priority**: ETags in responses are the foundation for all conditional request behavior. Without them, clients have nothing to send back in `If-None-Match` or `If-Match` headers.

**Independent Test**: Register the ETag middleware, define a stateful resource, issue GET requests and verify `ETag` headers are present. Trigger a state transition, issue another GET, verify the ETag value has changed.

**Acceptance Scenarios**:

1. **Given** the ETag middleware is registered and a stateful resource is in state `XTurn`, **When** a GET request arrives, **Then** the response includes an `ETag` header with a value derived from the current `('State * 'Context)` pair.
2. **Given** the same resource after a state transition to `OTurn`, **When** a GET request arrives, **Then** the `ETag` value differs from the previous response.
3. **Given** a resource with no prior state (first access), **When** a GET arrives, **Then** the response includes an ETag derived from the machine's initial state.
4. **Given** a resource without an `IETagProvider` (e.g., a plain resource with no state source), **When** a GET arrives, **Then** no `ETag` header is set and no conditional request processing occurs (see FR-013).

---

### User Story 2 - Conditional GET with 304 Not Modified (Priority: P2)

A client that previously received an ETag sends a subsequent GET with `If-None-Match` containing that ETag. If the resource state has not changed, the server responds with 304 Not Modified and an empty body, saving bandwidth and processing.

**Why this priority**: This is the primary bandwidth optimization use case. Clients that cache responses can skip re-downloading unchanged resources.

**Independent Test**: GET a resource to obtain its ETag, issue a second GET with `If-None-Match` set to that ETag, verify 304 response with no body. Trigger a state transition, repeat the conditional GET, verify 200 with new body and new ETag.

**Acceptance Scenarios**:

1. **Given** an ETag-enabled resource whose state has not changed, **When** a GET arrives with `If-None-Match` matching the current ETag, **Then** the server returns 304 Not Modified with no body.
2. **Given** an ETag-enabled resource whose state has changed, **When** a GET arrives with `If-None-Match` containing the old ETag, **Then** the server returns 200 with the full response body and an updated ETag.
3. **Given** a GET with `If-None-Match: *`, **When** the ETag-enabled resource exists (has any state), **Then** the server returns 304 Not Modified.
4. **Given** a GET with `If-None-Match` containing multiple ETags (comma-separated), **When** any one matches the current ETag, **Then** the server returns 304.
5. **Given** a resource without an `IETagProvider`, **When** a GET arrives with `If-None-Match`, **Then** the header is ignored and the server returns 200 with the full response.

---

### User Story 3 - Optimistic Concurrency via If-Match on Mutations (Priority: P2)

A client sends a mutation request (POST, PUT, DELETE) with `If-Match` containing the ETag from its last read. If the resource state has changed since that read (another client mutated it), the server rejects the request with 412 Precondition Failed instead of silently applying a stale update.

**Why this priority**: Optimistic concurrency is the key safety mechanism for multi-client scenarios. Without it, concurrent mutations cause lost updates.

**Independent Test**: Two clients GET the same resource (both receive ETag "abc"). Client A POSTs (succeeds, state transitions, new ETag "def"). Client B POSTs with `If-Match: "abc"` -- receives 412 because the ETag no longer matches.

**Acceptance Scenarios**:

1. **Given** an ETag-enabled resource with current ETag "abc", **When** a POST arrives with `If-Match: "abc"`, **Then** the request proceeds normally.
2. **Given** an ETag-enabled resource with current ETag "def" (changed since client's last read), **When** a POST arrives with `If-Match: "abc"`, **Then** the server returns 412 Precondition Failed.
3. **Given** a PUT with `If-Match: *`, **When** the ETag-enabled resource exists, **Then** the request proceeds (wildcard matches any existing state).
4. **Given** a POST with no `If-Match` header, **When** the request arrives, **Then** it proceeds normally (conditional headers are optional).

---

### User Story 4 - Custom ETag Computation for Non-Statechart Resources (Priority: P3)

A Frank developer uses plain `resource` (not `statefulResource`) but wants conditional request support. They provide a custom ETag computation function that derives an ETag from their own domain state, and the conditional request middleware honors it.

**Why this priority**: Conditional requests are generally useful beyond statecharts. Supporting arbitrary ETag sources makes this a capability available to any Frank resource, not just statechart-backed ones.

**Independent Test**: Define a plain resource with a custom `IETagProvider` that computes ETags from an external state source. Verify that GET responses include ETags and conditional requests work identically to statechart-backed resources.

**Acceptance Scenarios**:

1. **Given** a plain resource registered with a custom `IETagProvider`, **When** a GET arrives, **Then** the response includes an ETag computed by that provider.
2. **Given** the custom provider's state changes, **When** a conditional GET arrives with the old ETag, **Then** the server returns 200 with the new ETag.
3. **Given** a plain resource with no `IETagProvider`, **When** a GET arrives, **Then** no ETag header is set and no conditional request processing occurs (default behavior unchanged) (see FR-013).

---

### Edge Cases

- **Weak vs strong ETags**: Initial implementation uses strong ETags (byte-for-byte equivalence semantics per RFC 9110). Weak ETags (`W/"..."`) as a future enhancement.
- **Multiple ETags in If-None-Match**: The `If-None-Match` header can contain a comma-separated list; any match triggers 304.
- **Wildcard `*` in conditional headers**: `If-None-Match: *` matches any existing ETag (resource exists). `If-Match: *` matches any existing resource.
- **First request to a new resource instance**: ETag is computed from the machine's initial state -- there is always a state, even before explicit interaction.
- **Hash collisions**: SHA-256 truncation makes collisions astronomically unlikely (birthday bound ~2^128). Not mitigated beyond hash quality.
- **Race condition between ETag check and handler execution**: A state transition could occur between the conditional header check and the handler running. The MailboxProcessor serializes cache access within a single resource instance, so a GET between a state transition and cache update will either see the old ETag (resulting in a correct 200 with new state on the next request) or the new ETag. Cross-instance races are inherent to optimistic concurrency and result in a 412 on the next attempt.
- **ETag for DELETE responses**: After a successful DELETE, the resource may no longer have state. No ETag is set on the response.
- **Cache invalidation on state transition**: When state transitions occur, the ETag cache entry for that resource instance must be invalidated or updated atomically with the state change.
- **Opted-in resource with conditional headers from a different resource**: `If-Match`/`If-None-Match` ETags are scoped to the resource URI. The middleware only compares against the ETag for the resource being requested, not across resources.

## Requirements

### Functional Requirements

- **FR-001**: System MUST provide a framework-wide middleware registration that enables ETag generation for all resources with an available `IETagProvider`
- **FR-002**: System MUST compute ETags from resource state using stable, deterministic hashing of the `('State * 'Context)` pair for statechart-backed resources
- **FR-003**: System MUST set the `ETag` response header on successful GET responses for all resources with a registered `IETagProvider`
- **FR-004**: System MUST return 304 Not Modified when a GET or HEAD request to an ETag-enabled resource includes `If-None-Match` matching the current ETag
- **FR-005**: System MUST return 412 Precondition Failed when a mutation request (POST, PUT, DELETE) to an ETag-enabled resource includes `If-Match` that does not match the current ETag
- **FR-006**: System MUST process requests normally when no conditional headers are present, regardless of ETag middleware registration
- **FR-007**: System MUST expose a non-generic `IETagProvider` interface (accepts a resource instance key string, returns an ETag) for custom ETag computation from arbitrary state sources. Generic type parameters exist only on concrete implementations (e.g., `StatechartETagProvider<'State, 'Context>`), not on the interface itself. System MUST also expose an `IETagProviderFactory` abstraction that resolves the appropriate `IETagProvider` for a given resource endpoint, registered in DI and consumed by the `ConditionalRequestMiddleware`
- **FR-008**: System MUST provide a default `StatechartETagProvider<'State, 'Context>` that implements the non-generic `IETagProvider` interface, hashing the current `('State * 'Context)` pair using stable structural hashing
- **FR-009**: System MUST use a MailboxProcessor-backed cache for resource-instance-to-ETag mapping with serialized access (consistent with Frank.Statecharts pattern) with configurable maximum capacity (default: 10,000 entries) and LRU eviction policy
- **FR-010**: ETag value MUST change when resource state transitions occur
- **FR-011**: System MUST support multiple ETags in `If-None-Match` headers (comma-separated per RFC 9110)
- **FR-012**: System MUST support wildcard `*` in both `If-None-Match` and `If-Match` headers per RFC 9110 semantics
- **FR-013**: System MUST NOT alter behavior of resources without an available `IETagProvider` -- no headers added, no conditional processing, no overhead
- **FR-014**: System MUST produce strong ETags (not weak) using quoted-string format per RFC 9110 Section 8.8.3, using SHA-256 truncated to 128 bits (32 hex characters) for the ETag value
- **FR-015**: System MUST invalidate or update cached ETags atomically with state transitions
- **FR-016**: System MUST include the ETag header in 304 Not Modified responses per RFC 9110 Section 15.4.5

### Key Entities

- **IETagProvider**: Non-generic abstraction for computing an ETag string from a resource instance key. Implementations may hash statechart state, database version stamps, or any domain state. Generic type parameters exist only on concrete implementations (e.g., `StatechartETagProvider<'State, 'Context>`), not on this interface.
- **IETagProviderFactory**: Factory abstraction that resolves the appropriate `IETagProvider` for a given resource endpoint. Registered in DI and consumed by the `ConditionalRequestMiddleware` to obtain per-resource ETag providers.
- **ConditionalRequestMiddleware**: ASP.NET Core middleware that intercepts requests to ETag-enabled resources, evaluates conditional headers (`If-Match`, `If-None-Match`) against current ETags, and short-circuits with 304/412 when appropriate. Passes through non-ETag-enabled resources untouched.
- **ETagCache**: MailboxProcessor-backed concurrent cache mapping resource instance identifiers to their current ETag values. Serializes access to prevent races.
- **StatechartETagProvider<'State, 'Context>**: Default `IETagProvider` implementation for statechart-backed resources that hashes the `('State * 'Context)` pair using stable structural hashing.
- **ETagMetadata**: Endpoint metadata marker indicating a resource has an available `IETagProvider` for conditional request processing. Absence of this metadata means the resource does not participate.

## Success Criteria

### Measurable Outcomes

- **SC-001**: GET responses to ETag-enabled resources include correct `ETag` headers reflecting current domain state
- **SC-002**: GET responses to non-ETag-enabled resources contain no `ETag` header and experience zero overhead from the middleware
- **SC-003**: Conditional GET with matching `If-None-Match` returns 304 Not Modified with no body, reducing bandwidth for unchanged resources
- **SC-004**: Mutation requests with stale `If-Match` return 412 Precondition Failed, detecting concurrent modifications via optimistic concurrency
- **SC-005**: ETag computation adds < 1ms overhead for state pairs serialized under 1 KB, measured as p95 latency delta via BenchmarkDotNet
- **SC-006**: Custom `IETagProvider` implementations work identically to the built-in statechart provider for conditional request handling
- **SC-007**: All ETags conform to RFC 9110 strong ETag format (quoted strings, deterministic, change on state transition)
- **SC-008**: The ETag cache handles concurrent access without deadlocks or data races under load


## Research

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
