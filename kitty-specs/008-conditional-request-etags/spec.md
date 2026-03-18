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
