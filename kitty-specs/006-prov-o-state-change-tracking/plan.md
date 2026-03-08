# Implementation Plan: Frank.Provenance (PROV-O State Change Tracking)

**Branch**: `006-prov-o-state-change-tracking` | **Date**: 2026-03-07 | **Spec**: [spec.md](spec.md)
**Input**: Phase 2 of #80 (Semantic Metadata-Augmented Resources). Provenance layer consuming `onTransition` hooks from Frank.Statecharts.
**Research**: [research.md](research.md) | **Data Model**: [data-model.md](data-model.md)

## Summary

Implement `Frank.Provenance`, a library that records W3C PROV-O provenance for every successful state transition in Frank.Statecharts-managed resources. Subscribes to `onTransition` hooks, constructs provenance graphs (Agent, Activity, Entity) using dotNetRdf.Core, stores them in a MailboxProcessor-backed in-memory store (with pluggable `IProvenanceStore`), and serves provenance via content negotiation on resource URIs using custom `application/vnd.frank.provenance+*` media types through Frank.LinkedData. Agent type discrimination (Person, SoftwareAgent, LLM subclass) enriches audit trails. This unblocks the provenance layer for v7.3.0 milestone.

## Technical Context

**Prerequisite**: `TransitionEvent<'State, 'Event, 'Context>` in Frank.Statecharts must be extended with `InstanceId: string`, `ResourceUri: string`, and `HttpMethod: string` fields before this feature can be implemented. Current fields: PreviousState, PreviousContext, NewState, NewContext, Event, Timestamp, User.

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank core)
**Primary Dependencies**: Frank (project reference), Frank.LinkedData (project reference), Frank.Statecharts (project reference), dotNetRdf.Core (NuGet)
**Storage**: In-memory MailboxProcessor-backed store by default; `IProvenanceStore` interface for external stores
**Testing**: Expecto + ASP.NET Core TestHost (matching existing Frank test patterns)
**Target Platform**: .NET multi-target library (net8.0;net9.0;net10.0)
**Project Type**: Library extension for Frank
**Performance Goals**: Sub-millisecond append and query for up to 10,000 records; less than 1ms overhead per request in the state transition pipeline
**Constraints**: Configurable retention policy (default 10,000 records, oldest-first eviction); no external NuGet dependencies beyond dotNetRdf.Core
**Scale/Scope**: Framework-wide provenance for all stateful resources; per-resource opt-in deferred to future enhancement
**Subscription Model**: The `onTransition` CE operation is defined on `StatefulResourceBuilder` and applies per-resource. There is no global observable for all transitions. Provenance must hook into each resource's `onTransition` individually. The `ProvenanceSubscriptionManager` must iterate over all registered stateful resource endpoints and inject the observer via middleware or endpoint metadata, not subscribe to a single global stream.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. Resource-Oriented Design -- PASS

Provenance is served via content negotiation on the resource URI itself using custom `Accept` media types (`application/vnd.frank.provenance+turtle`, etc.). No separate `/provenance` sub-route is introduced. This preserves the resource as the primary abstraction -- provenance is an alternate representation of the resource, not a separate endpoint. The approach aligns with REST content negotiation semantics where the same resource can have multiple representations.

### II. Idiomatic F# -- PASS

- `useProvenance` is a computation expression custom operation on `WebHostBuilder`
- `ProvenanceRecord`, `ProvenanceAgent`, `ProvenanceActivity`, `ProvenanceEntity` are immutable F# records
- `AgentType` is a discriminated union (Person, SoftwareAgent, LlmAgent)
- `IProvenanceStore` uses `Async`/`Task` for async operations
- MailboxProcessor for serialized, lock-free concurrent access (idiomatic F# concurrency)
- Pipeline-friendly query functions (`queryByResource`, `queryByAgent`, `queryByTimeRange`)

### III. Library, Not Framework -- PASS

Frank.Provenance is an opt-in library extension. Resources without `useProvenance` enabled have zero overhead. Non-stateful resources (`resource` CE instead of `statefulResource`) do not participate. No opinions on view engines, authentication systems, or data access. Users can swap the store implementation via DI without changing any other code.

### IV. ASP.NET Core Native -- PASS

- Uses `IServiceCollection` for DI registration (`IProvenanceStore`)
- Middleware pattern follows Frank.LinkedData's established approach (endpoint metadata marker, `app.Use`)
- `ClaimsPrincipal` from `HttpContext.User` for agent identity
- Standard ASP.NET Core content negotiation via `Accept` header
- `ILogger` for all observability (Constitution VII compliance)

### V. Performance Parity -- PASS

- MailboxProcessor per-message overhead: ~1-5us (negligible vs network latency)
- Provenance recording is fire-and-forget from the transition hot path (MailboxProcessor `Post`, not `PostAndAsyncReply`)
- No allocations in the request path beyond the provenance record construction
- Read queries can proceed concurrently with writes (MailboxProcessor serializes writes only)
- Retention policy prevents unbounded memory growth

### VI. Resource Disposal Discipline -- PASS

- `MailboxProcessorProvenanceStore` implements `IDisposable` with proper drain of pending appends
- `IGraph` instances from dotNetRdf are created with `use` in middleware response serialization
- Subscription to `onTransition` returns `IDisposable` -- stored and disposed during host shutdown
- `ObjectDisposedException` handled gracefully in the provenance observer (spec edge case)

### VII. No Silent Exception Swallowing -- PASS

- Provenance middleware logs via `ILogger` on all error paths
- Store append failures are logged (not silently dropped)
- Content negotiation errors for provenance media types follow Frank.LinkedData's graduated exception handling (recoverable -> log + fallback, unrecoverable -> log + re-raise)
- No bare `with _ ->` handlers in request-serving code

### VIII. No Duplicated Logic -- PASS

- RDF URI construction reuses `Frank.LinkedData.Rdf.RdfUriHelpers` (no duplication)
- Content negotiation shared utilities from Frank.LinkedData are reused, not copied
- PROV-O namespace constants defined once in a shared `ProvVocabulary` module
- Graph serialization delegates to Frank.LinkedData's existing `writeRdf` function

## Project Structure

### Documentation (this feature)

```
kitty-specs/006-prov-o-state-change-tracking/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Research decisions and rationale
├── data-model.md        # Entity model with F# type signatures
└── quickstart.md        # Developer quickstart guide
```

### Source Code (repository root)

```
src/
├── Frank/                              # Existing core library
│   └── Builder.fs                      # WebHostBuilder, WebHostSpec
│
├── Frank.LinkedData/                   # Existing LinkedData library (dependency)
│   ├── Rdf/RdfUriHelpers.fs            # Reused for URI construction
│   └── WebHostBuilderExtensions.fs     # Pattern reference for middleware + negotiation
│
├── Frank.Statecharts/                  # Existing Statecharts library (dependency)
│   ├── Types.fs                        # TransitionEvent, onTransition hooks
│   └── Store.fs                        # IStateMachineStore (subscription source)
│
├── Frank.Provenance/                   # NEW: Provenance library
│   ├── Frank.Provenance.fsproj         # Multi-target: net8.0;net9.0;net10.0
│   ├── ProvVocabulary.fs               # PROV-O namespace URIs and term constants
│   ├── Types.fs                        # ProvenanceRecord, Agent, Activity, Entity, AgentType
│   ├── Store.fs                        # IProvenanceStore interface
│   ├── MailboxProcessorStore.fs        # Default in-memory store with retention
│   ├── GraphBuilder.fs                 # ProvenanceRecord -> dotNetRdf IGraph construction
│   ├── TransitionObserver.fs           # onTransition subscription -> ProvenanceRecord creation
│   ├── Middleware.fs                    # Content negotiation for provenance media types
│   └── WebHostBuilderExtensions.fs     # [<AutoOpen>] useProvenance custom operation

test/
└── Frank.Provenance.Tests/             # NEW: Test project
    ├── Frank.Provenance.Tests.fsproj   # Target: net10.0
    ├── VocabularyTests.fs              # PROV-O URI correctness
    ├── TypeTests.fs                    # ProvenanceRecord, AgentType construction
    ├── StoreTests.fs                   # MailboxProcessorStore append, query, retention
    ├── GraphBuilderTests.fs            # RDF graph construction validation
    ├── TransitionObserverTests.fs      # Hook subscription and record creation
    ├── MiddlewareTests.fs              # Content negotiation integration tests (TestHost)
    ├── CustomStoreTests.fs             # IProvenanceStore DI replacement
    └── Program.fs                      # Expecto entry point
```

**Structure Decision**: Single library project + single test project. Follows the same pattern as Frank.LinkedData, Frank.Auth, Frank.Statecharts, and Frank.OpenApi. The `ProvVocabulary` module is a shared constants module within the project, not a separate project, satisfying Constitution VIII without adding project complexity.

## Parallel Work Analysis

### Dependency Graph

```
WP01: Project Scaffold + ProvVocabulary + Types
  │
  ├──> WP02: IProvenanceStore + MailboxProcessorStore  (depends: WP01)
  │       │
  │       └──> WP04: Provenance Middleware              (depends: WP02, WP03)
  │               │
  │               └──> WP07: WebHostBuilderExtensions + Integration  (depends: WP04, WP06)
  │
  ├──> WP03: GraphBuilder (dotNetRdf)                   (depends: WP01)
  │       │
  │       ├──> WP04: Provenance Middleware
  │       │
  │       └──> WP06: Agent Type Discrimination          (depends: WP03)
  │               │
  │               └──> WP07: WebHostBuilderExtensions + Integration
  │
  └──> WP05: TransitionObserver                          (depends: WP01, WP02)
```

### Parallelism Opportunities

- **WP02 and WP03** can proceed in parallel after WP01; **WP05** depends on WP01 and WP02 (needs `IProvenanceStore` interface)
- **WP04** depends on both WP02 and WP03
- **WP06** depends on WP03
- **WP07** is the convergence point, depending on WP04 and WP06

### Critical Path

WP01 -> WP02 -> WP04 -> WP07

## Complexity Tracking

No constitution violations requiring justification. The design adds one new library project following established patterns. The only external NuGet dependency (dotNetRdf.Core) is shared with Frank.LinkedData, which already depends on it.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Frank.Statecharts `TransitionEvent` missing required fields | **High** | `TransitionEvent<'State, 'Event, 'Context>` currently lacks `InstanceId`, `ResourceUri`, and `HttpMethod`. These fields must be added to Frank.Statecharts before WP05 (TransitionObserver) can proceed. Coordinate with Statecharts maintainer. |
| Frank.Statecharts API surface changes | Low | The `onTransition` CE operation and `IStateMachineStore.Subscribe` are implemented and stable on master. Pin to current API surface. |
| dotNetRdf.Core version conflicts with Frank.LinkedData | Medium | Pin to same version as Frank.LinkedData; verify with multi-target build |
| MailboxProcessor store memory pressure under high write volume | Low | Retention policy (10,000 records, oldest-first eviction) bounds memory; configurable via `useProvenance` |
| Custom media type registration may conflict with Frank.LinkedData negotiation | Medium | Provenance media types use `vnd.frank.provenance+*` prefix, checked before LinkedData's standard types |
| Agent identity extraction from `ClaimsPrincipal` varies across auth providers | Low | Fall back to `prov:SoftwareAgent` when identity claims are absent or ambiguous |
