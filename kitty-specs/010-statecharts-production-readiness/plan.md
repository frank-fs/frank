# Implementation Plan: Statecharts Production Readiness

**Branch**: `010-statecharts-production-readiness` | **Date**: 2026-03-15 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/kitty-specs/010-statecharts-production-readiness/spec.md`
**GitHub Issue**: #95

## Summary

Address four production-readiness gaps in Frank.Statecharts identified during spec 004 integration testing:

1. **State key extraction** (P1): Replace fragile `state.ToString()` key derivation with `FSharpValue.PreComputeUnionTagReader` + `FSharpType.GetUnionCases` so parameterized DU cases (e.g., `Won "X"`, `Won "O"`) map to a single handler key (`"Won"`).

2. **Actor-serialized concurrency** (P1): Validate and document that the existing `IStateMachineStore` contract assumes actor-serialized access. The in-memory `MailboxProcessorStore` already satisfies this. No interface changes needed.

3. **Two-phase guard evaluation** (P2): Split guard evaluation into access-control guards (pre-handler, existing behavior) and event-validation guards (post-handler, with actual event context). Add `EventGuards` field to `StateMachine` record.

4. **SQLite durable store** (P2): Create `Frank.Statecharts.Sqlite` package with `SqliteStateMachineStore` -- an actor-wrapped SQLite implementation of `IStateMachineStore` supporting durable persistence, lazy rehydration, and the same observable subscription semantics as the in-memory store.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank core)
**Primary Dependencies**: Frank 6.x (project reference), Microsoft.AspNetCore.App (framework reference), Microsoft.Data.Sqlite (for SQLite project only), FSharp.Reflection (in FSharp.Core)
**Storage**: SQLite via Microsoft.Data.Sqlite (new `Frank.Statecharts.Sqlite` project); in-memory MailboxProcessor (existing, unchanged)
**Testing**: Expecto + TestHost pattern (existing `Frank.Statecharts.Tests`); new `Frank.Statecharts.Sqlite.Tests` project
**Target Platform**: .NET 8.0, 9.0, 10.0 (multi-target for core changes); all three for SQLite package
**Project Type**: Multi-project library (core library + separate SQLite package + tests)
**Performance Goals**: State key extraction O(1) via precomputed tag reader (no per-request reflection). SQLite operations synchronous inside actor (Microsoft.Data.Sqlite async is synchronous under the hood).
**Constraints**: No breaking changes to `IStateMachineStore` interface. Source-breaking change to `StateMachine` record (adding `EventGuards` field) is acceptable for pre-1.0 library. ETag computation (`StatechartETagProvider`) must NOT use case-name keys -- it correctly uses full `string state` for parameter-sensitive hashing.
**Scale/Scope**: 4 source files modified in core (`Types.fs`, `StatefulResourceBuilder.fs`, `Middleware.fs`, `Store.fs` docs only), 1 new project (`Frank.Statecharts.Sqlite`), 2 test projects updated/created

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | PASS | Statecharts extend resource semantics with state-aware routing. No route-centric patterns introduced. |
| II. Idiomatic F# | PASS | DU-based state types, computation expressions for configuration, `FSharpValue.PreComputeUnionTagReader` is idiomatic F# reflection. Two-phase guards use separate list fields (not OOP inheritance). |
| III. Library, Not Framework | PASS | SQLite store is a separate optional package. Core has no new external dependencies. Users opt in to persistence. |
| IV. ASP.NET Core Native | PASS | DI registration via `IServiceCollection` extension. Middleware pattern unchanged. `HttpContext` exposed directly. |
| V. Performance Parity | PASS | State key extraction uses precomputed delegate (zero per-request reflection). SQLite store uses single connection with WAL mode. Actor serialization adds no overhead vs. current design. |
| VI. Resource Disposal Discipline | PASS | `SqliteStateMachineStore` implements `IDisposable`. `SqliteConnection` bound with `use` semantics inside the actor. SHA256 in ETag provider already uses `use`. |
| VII. No Silent Exception Swallowing | PASS | SQLite store logs via `ILogger`. Actor subscriber notification errors logged with context (matching existing `MailboxProcessorStore` pattern). SQLite busy timeout surfaces clear error. |
| VIII. No Duplicated Logic | PASS | State key extraction is a single `stateKey` function captured in closures. SQLite store reuses the same `StoreMessage` DU pattern as in-memory store. Guard evaluation extracted into two closures sharing structure. |

**Post-Phase 1 Re-check**: Verified. The `SqliteStateMachineStore` mirrors `MailboxProcessorStore` structurally but does not duplicate code -- each has its own actor loop with different persistence backing. The `StoreMessage` DU can potentially be shared (both stores use identical message types), but since they live in separate projects and the DU is private, keeping them separate avoids a cross-project internal dependency. This is acceptable per Principle VIII because the DU is a 5-line type definition, not duplicated business logic.

## Project Structure

### Documentation (this feature)

```
kitty-specs/010-statecharts-production-readiness/
├── plan.md              # This file
├── research.md          # Phase 0 output (complete, revision 2)
├── data-model.md        # Phase 1 output (complete, revision 2)
├── checklists/
│   └── requirements.md  # Spec quality checklist (complete)
└── tasks.md             # Phase 2 output (NOT created by /spec-kitty.plan)
```

### Source Code (repository root)

```
src/
├── Frank.Statecharts/                    # Existing core library (MODIFIED)
│   ├── Types.fs                          # Add EventGuards field to StateMachine record
│   ├── Store.fs                          # Documentation-only: document actor-serialization contract
│   ├── StatefulResourceBuilder.fs        # State key extraction + two-phase guard closures
│   ├── Middleware.fs                     # Insert event-validation guard step between handler and transition
│   ├── StatechartETagProvider.fs         # UNCHANGED (correctly uses full state string for ETags)
│   ├── ResourceBuilderExtensions.fs      # UNCHANGED
│   └── WebHostBuilderExtensions.fs       # UNCHANGED
│
├── Frank.Statecharts.Sqlite/             # NEW separate project
│   ├── Frank.Statecharts.Sqlite.fsproj   # Multi-target net8.0;net9.0;net10.0
│   └── SqliteStateMachineStore.fs        # Actor-wrapped SQLite IStateMachineStore implementation
│
test/
├── Frank.Statecharts.Tests/              # Existing test project (MODIFIED)
│   ├── TypeTests.fs                      # Tests for EventGuards field
│   ├── StoreTests.fs                     # Concurrency serialization tests
│   ├── StatefulResourceTests.fs          # State key extraction + parameterized DU tests
│   ├── MiddlewareTests.fs                # Two-phase guard evaluation tests
│   └── StatechartETagProviderTests.fs    # Verify ETags unaffected by key extraction change
│
├── Frank.Statecharts.Sqlite.Tests/       # NEW test project
│   ├── Frank.Statecharts.Sqlite.Tests.fsproj
│   ├── SqliteStoreTests.fs               # CRUD, rehydration, subscription tests
│   └── Program.fs                        # Expecto entry point
```

**Structure Decision**: Two source projects (core + SQLite) with two test projects. The SQLite store lives in a separate project per spec assumption ("Frank.Statecharts.Sqlite or similar") to avoid adding a SQLite dependency to the core library (Constitution Principle III -- Library, Not Framework). This matches the Frank project's existing pattern of optional extension packages.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| New project `Frank.Statecharts.Sqlite` | SQLite dependency must not pollute core package | A single project would force all users to take a `Microsoft.Data.Sqlite` dependency even if they only use in-memory stores. Separate project per Constitution III. |

## Parallel Work Analysis

### Dependency Graph

```
Foundation (WP01-WP02, sequential)
  WP01: State key extraction (Types.fs, StatefulResourceBuilder.fs) -- no external deps
  WP02: Two-phase guards (Types.fs, StatefulResourceBuilder.fs, Middleware.fs) -- depends on WP01 for shared Types.fs changes

Wave 1 (WP03-WP04, parallelizable after Foundation)
  WP03: Actor-serialization validation + concurrency tests (Store.fs docs, new tests) -- independent
  WP04: SQLite store (new project, new tests) -- depends on WP01/WP02 for final Types.fs

Integration (WP05, after all)
  WP05: Integration testing + backward compatibility validation
```

### Work Distribution

- **Sequential work (Foundation)**: WP01 (state key extraction) must land first because WP02 modifies the same `StateMachine` record. WP02 (two-phase guards) modifies `Types.fs`, `StatefulResourceBuilder.fs`, and `Middleware.fs`.
- **Parallel streams (Wave 1)**: WP03 (concurrency validation) and WP04 (SQLite store) can proceed in parallel -- WP03 tests existing `MailboxProcessorStore`, WP04 creates a new project.
- **Integration (WP05)**: Final validation that all 4 gaps work together, backward compatibility with spec 004 tests, and cross-cutting edge cases.

### Coordination Points

- **After WP01**: `StateMachine` record in `Types.fs` gains stable shape; `StatefulResourceBuilder.fs` has `stateKey` function available
- **After WP02**: `StateMachineMetadata` has `EvaluateEventGuards` field; middleware flow is finalized
- **After WP03+WP04**: All store implementations validated; SQLite project buildable
- **WP05**: Run full `dotnet build` and `dotnet test` across all target frameworks

## Design Decisions Summary

Decisions are fully documented in [research.md](research.md) (revision 2). Key highlights:

| # | Decision | Rationale |
|---|----------|-----------|
| D-001 | `FSharpValue.PreComputeUnionTagReader` for state keys | O(1) precomputed delegate, immune to `ToString()` overrides, existing precedent in `src/Frank.LinkedData/Rdf/InstanceProjector.fs` |
| D-002 | Actor-serialized concurrency (no interface changes) | `IStateMachineStore` unchanged; `MailboxProcessorStore` already correct; no version tokens needed |
| D-003 | Two separate guard lists (access + event) | Backward compatible; existing guards default to access-control phase; event guards opt-in |
| D-004 | SQLite store as actor-wrapped persistence | Same `MailboxProcessor` pattern as in-memory; lazy rehydration; single connection; WAL mode |
| D-005 | MailboxProcessor backpressure: document as known limitation | Kestrel provides implicit backpressure; V1 scope does not include bounded mailbox |
| D-006 | Accept `JsonSerializerOptions` parameter, soft dependency on `FSharp.SystemTextJson` | Users configure their own serializer; no hard dependency on `FSharp.SystemTextJson` in the SQLite package |

## Open Questions (Resolved)

Per research.md, these open questions have been resolved with the following positions:

1. **Non-DU state types**: Runtime check with fallback to `ToString()` for non-DU types. `FSharpType.IsUnion` guard before attempting tag reader precomputation.
2. **ETag interaction**: `StatechartETagProvider` correctly uses `string state` (full representation including parameters). NOT changed to use case-name keys.
3. **Cross-process SQLite**: Documented as unsupported. Single-process actor model. Use PostgreSQL etc. for multi-process deployments.
4. **Guard evaluation when response started**: Log warning, consistent with existing `TransitionAttemptResult.Blocked` handling in `Middleware.fs` lines 97-107.
5. **Multi-targeting for SQLite project**: Match core package targets (`net8.0;net9.0;net10.0`).
6. **Actor cache eviction**: Document as known limitation. LRU eviction deferred to future version.
7. **SQLite busy timeout**: `PRAGMA busy_timeout=5000` configured at connection open. Store catches `SqliteException` and surfaces clear error via `ILogger`.
