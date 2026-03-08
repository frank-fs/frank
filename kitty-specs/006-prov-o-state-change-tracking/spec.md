# Feature Specification: PROV-O State Change Tracking

**Feature Branch**: `006-prov-o-state-change-tracking`
**Created**: 2026-03-07
**Status**: Draft
**GitHub Issue**: #77
**Dependencies**: Frank.Statecharts (#87, complete), Frank.LinkedData (#75, complete)
**Input**: Phase 2 of #80 (Semantic Metadata-Augmented Resources). Provenance layer consuming `onTransition` hooks from Frank.Statecharts.

## Clarifications

### Session 2026-03-07

- Q: How is the provenance graph exposed to clients? → A: Via content negotiation on the resource URI itself using a custom `Accept` media type (e.g., `application/vnd.frank.provenance+json`, `application/vnd.frank.provenance+ld+json`). No separate `/provenance` sub-route. Aggregate cross-resource queries are deferred to Frank.Sparql (#78).

## Overview

Frank.Provenance records the provenance of resource state changes using the W3C PROV-O vocabulary. Every mutation to a stateful resource produces a complete provenance record capturing who acted (Agent), what happened (Activity), and what was produced (Entity). Provenance is framework-wide by default -- all stateful resources automatically participate. The provenance graph is queryable and served through Frank.LinkedData content negotiation, enabling clients to request provenance in JSON-LD, Turtle, or RDF/XML.

## User Scenarios & Testing

### User Story 1 - Automatic Provenance Recording for State Changes (Priority: P1)

A Frank developer deploys a stateful resource with `useProvenance` enabled. Every successful state transition automatically produces a provenance record without any per-resource configuration. The developer can later query who made each change and when.

**Why this priority**: This is the core value proposition -- zero-configuration provenance for all resource mutations. Without this, there is no provenance system.

**Independent Test**: Enable provenance on a stateful resource, trigger a state transition via POST, query the provenance store, verify a complete PROV-O record exists with agent, activity, and entity information.

**Acceptance Scenarios**:

1. **Given** a `webHost` with `useProvenance` and a `statefulResource`, **When** a POST triggers a state transition, **Then** the provenance store contains a record with `prov:Agent`, `prov:Activity`, and `prov:Entity`.
2. **Given** a successful state transition by an authenticated user, **When** the provenance record is created, **Then** the `prov:Agent` references the user's identity from `ClaimsPrincipal`.
3. **Given** a successful state transition, **When** the provenance record is created, **Then** it captures both the pre-transition entity (via `prov:used`) and the post-transition entity (via `prov:wasGeneratedBy`).
4. **Given** a guard-blocked request (no state transition), **When** the provenance store is queried, **Then** no provenance record was created for that request.

---

### User Story 2 - Content-Negotiated Provenance Responses (Priority: P2)

A client requests provenance for a resource by sending a GET to the same resource URI with a custom `Accept` media type (e.g., `application/vnd.frank.provenance+json`). The server returns the provenance graph for that resource in the requested serialization, leveraging Frank.LinkedData's content negotiation infrastructure. No separate provenance route is needed.

**Why this priority**: Provenance is only useful if it can be consumed. Using content negotiation on the resource URI itself keeps the API surface clean and discoverable.

**Independent Test**: Trigger several state transitions, then GET the resource URI with `Accept: application/vnd.frank.provenance+turtle`, verify a valid Turtle document containing PROV-O triples is returned. Repeat with `Accept: application/vnd.frank.provenance+ld+json`.

**Acceptance Scenarios**:

1. **Given** a resource with provenance records, **When** a GET request with `Accept: application/vnd.frank.provenance+turtle` arrives at the resource URI, **Then** the response contains valid Turtle with `prov:Activity`, `prov:Agent`, and `prov:Entity` triples.
2. **Given** the same resource, **When** a GET request with `Accept: application/vnd.frank.provenance+ld+json` arrives, **Then** the response contains valid JSON-LD with the `@context` including the PROV-O namespace.
3. **Given** a resource with no provenance records, **When** the provenance media type is requested, **Then** an empty graph is returned (200 with empty collection, not 404).
4. **Given** a standard `Accept: application/json` request, **When** it arrives at the resource URI, **Then** the normal resource representation is returned (not provenance).

---

### User Story 3 - Agent Type Discrimination (Priority: P3)

A Frank developer's provenance records distinguish between human users, system processes, and LLM-originated changes. Each agent type is represented with appropriate PROV-O subclasses so that provenance consumers can filter and audit by agent origin.

**Why this priority**: Agent discrimination is important for audit trails and trust assessment, but the system functions without it (all agents are `prov:Agent`). This enriches the provenance model.

**Independent Test**: Trigger transitions from an authenticated user, an unauthenticated request (system agent), and a request with an `X-Agent-Type: llm` header. Query provenance and verify each record has the correct agent type classification.

**Acceptance Scenarios**:

1. **Given** an authenticated user triggers a transition, **When** the provenance record is created, **Then** the agent is typed as `prov:Person` with identity from `ClaimsPrincipal`.
2. **Given** an unauthenticated request triggers a transition, **When** the provenance record is created, **Then** the agent is typed as `prov:SoftwareAgent` with a system identifier.
3. **Given** a request with agent-type metadata indicating LLM origin, **When** the provenance record is created, **Then** the agent is typed as `prov:SoftwareAgent` with an LLM-specific subclass annotation.

---

### User Story 4 - External Provenance Store (Priority: P4)

A Frank developer swaps the default in-memory provenance store for an external triple store or database by implementing `IProvenanceStore` and registering it in DI. The provenance system continues to function identically.

**Why this priority**: Production systems need durable provenance. The interface exists from day one, but external implementations are out of scope for this feature.

**Independent Test**: Implement a mock `IProvenanceStore`, register it in DI, trigger transitions, verify the mock received all provenance records.

**Acceptance Scenarios**:

1. **Given** a custom `IProvenanceStore` registered in DI, **When** state transitions occur, **Then** the custom store receives all provenance records instead of the default in-memory store.
2. **Given** the default in-memory store, **When** provenance records are appended, **Then** they are retrievable by resource URI, agent, and time range.

---

### Edge Cases

- **Unauthenticated requests**: System agent (`prov:SoftwareAgent`) is used when no `ClaimsPrincipal` is available or the principal has no identity claims.
- **Failed state transitions**: Provenance records are NOT created for failed transitions (guard-blocked, invalid transition). Provenance tracks what happened, not what was attempted.
- **Concurrent mutations**: The MailboxProcessor-backed store serializes writes, ensuring provenance records have consistent ordering even under concurrent access.
- **Memory pressure**: The default in-memory store must support a configurable retention policy (max record count or time window) to prevent unbounded growth. Default: 10,000 records with oldest-first eviction.
- **Provenance of provenance (meta-provenance)**: Out of scope for this feature. The provenance system does not record provenance of its own operations.
- **Resource without statecharts**: Non-stateful resources do not participate in provenance recording. Provenance is scoped to `statefulResource` endpoints with `onTransition` hooks.
- **Store disposal during requests**: The provenance observer must handle `ObjectDisposedException` gracefully if the store is disposed while transitions are in-flight.

## Requirements

### Functional Requirements

- **FR-001**: System MUST subscribe to `onTransition` hooks from Frank.Statecharts to capture state change events as the provenance unit.
- **FR-002**: System MUST produce a `ProvenanceRecord` for every successful state transition containing `prov:Agent`, `prov:Activity`, and `prov:Entity` assertions.
- **FR-003**: System MUST extract agent identity from `ClaimsPrincipal` when available, falling back to a system agent (`prov:SoftwareAgent`) for unauthenticated requests.
- **FR-004**: System MUST capture pre-transition state as `prov:Entity` referenced via `prov:used` and post-transition state as `prov:Entity` referenced via `prov:wasGeneratedBy`.
- **FR-005**: System MUST record `prov:Activity` with `prov:startedAtTime` and `prov:endedAtTime` timestamps derived from the transition event.
- **FR-006**: System MUST associate activities with their triggering HTTP method and resource URI via `prov:wasAssociatedWith` (agent) and `prov:used` (input entity).
- **FR-007**: System MUST provide `IProvenanceStore` interface with `Append`, `QueryByResource`, `QueryByAgent`, and `QueryByTimeRange` operations.
- **FR-008**: System MUST provide a `MailboxProcessor`-backed default `IProvenanceStore` implementation with serialized append and concurrent-safe reads.
- **FR-009**: System MUST support a configurable retention policy on the default in-memory store (max record count, default 10,000) with oldest-first eviction.
- **FR-010**: System MUST serve provenance via content negotiation on the resource URI itself using custom `application/vnd.frank.provenance+*` media types, integrated with Frank.LinkedData.
- **FR-011**: System MUST provide `useProvenance` custom operation on `WebHostBuilder` for DI registration and `onTransition` subscription setup.
- **FR-012**: System MUST classify agents as `prov:Person` (authenticated human), `prov:SoftwareAgent` (system/unauthenticated), or LLM-specific subclass based on available identity metadata.
- **FR-013**: System MUST NOT record provenance for failed transitions (guard-blocked or invalid).
- **FR-014**: System MUST use W3C PROV-O namespace (`http://www.w3.org/ns/prov#`) for all provenance vocabulary terms.
- **FR-015**: System MUST implement `IDisposable` on the default provenance store with proper cleanup (drain pending appends, release memory).

### Key Entities

- **ProvenanceRecord**: Immutable record containing agent, activity, and entity references forming a complete PROV-O assertion. One record per successful state transition.
- **ProvenanceAgent**: The actor responsible for a state change. Wraps identity from `ClaimsPrincipal` or system identifier. Typed as `prov:Person`, `prov:SoftwareAgent`, or LLM subclass.
- **ProvenanceActivity**: The state transition itself. Captures HTTP method, resource URI, triggering event, start/end timestamps, and references to the agent and entities.
- **ProvenanceEntity**: A snapshot of resource state at a point in time. Pre-transition entity is `prov:used` by the activity; post-transition entity is `prov:wasGeneratedBy` the activity.
- **ProvenanceGraph**: A queryable collection of `ProvenanceRecord` instances for a given resource or scope. Serializable to RDF via Frank.LinkedData formatters.
- **IProvenanceStore**: Persistence abstraction with `Append`, `QueryByResource`, `QueryByAgent`, `QueryByTimeRange`, and `Dispose`. Pluggable via DI.
- **MailboxProcessorProvenanceStore**: Default in-memory `IProvenanceStore` implementation using `MailboxProcessor` for serialized writes and configurable retention.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every successful state transition on a `statefulResource` with provenance enabled produces exactly one `ProvenanceRecord` containing agent, activity, and entity data.
- **SC-002**: Provenance records are queryable by resource URI, agent identity, and time range, each returning correct subsets.
- **SC-003**: The default in-memory store handles 10,000+ records with sub-millisecond append and query times.
- **SC-004**: Provenance tracking adds less than 1ms overhead per request to the state transition pipeline.
- **SC-005**: Provenance responses are content-negotiated via Frank.LinkedData, producing valid JSON-LD, Turtle, and RDF/XML with correct PROV-O vocabulary.
- **SC-006**: The retention policy correctly evicts oldest records when the configured maximum is exceeded.
- **SC-007**: All public API types compile under multi-target (net8.0/net9.0/net10.0) with `dotnet build`.
- **SC-008**: A custom `IProvenanceStore` implementation registered in DI receives all provenance records instead of the default store.
