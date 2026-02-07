# Tasks: Frank.Datastar Native SSE

**Input**: Design documents from `/specs/014-datastar-native-sse/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Existing 18+ tests validate the replacement implementation. No new tests required unless TDD is explicitly requested.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `- [ ] [ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

## Path Conventions

- **Library source**: `src/Frank.Datastar/`
- **Tests**: `test/Frank.Datastar.Tests/`
- **Samples**: `samples/Frank.Datastar.Basic/`, `samples/Frank.Datastar.Hox/`, `samples/Frank.Datastar.Oxpecker/`

---

## Phase 1: Setup

**Purpose**: Update project configuration for multi-targeting and dependency removal

**Tasks**:

- [ ] T001 Update `src/Frank.Datastar/Frank.Datastar.fsproj` to change `<TargetFramework>net10.0</TargetFramework>` to `<TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>` and update `<VersionPrefix>8.0.0</VersionPrefix>` to `<VersionPrefix>7.1.0</VersionPrefix>` and update `<Description>` to remove ".NET 10 only" and emphasize native multi-target SSE implementation

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Verify existing internal SSE infrastructure is correct and uses only net8.0+ compatible APIs

**CRITICAL**: This infrastructure is already implemented but must be validated for multi-framework compatibility

**Tasks**:

- [ ] T002 [P] Verify `src/Frank.Datastar/Consts.fs` uses only APIs available across net8.0/net9.0/net10.0 (no .NET 10-specific features)
- [ ] T003 [P] Verify `src/Frank.Datastar/Types.fs` uses only APIs available across net8.0/net9.0/net10.0 (struct records, voption, TimeSpan are all net8.0+ compatible)
- [ ] T004 [P] Verify `src/Frank.Datastar/ServerSentEvent.fs` uses only APIs available across net8.0/net9.0/net10.0 (IBufferWriter<byte>, StringTokenizer from Microsoft.Extensions.Primitives, Encoding.UTF8 are all net8.0+ compatible)
- [ ] T005 [P] Verify `src/Frank.Datastar/ServerSentEventGenerator.fs` uses only APIs available across net8.0/net9.0/net10.0 (HttpResponse.BodyWriter, backgroundTask, all framework APIs are net8.0+ compatible)
- [ ] T006 [P] Verify `src/Frank.Datastar/Frank.Datastar.fs` uses only APIs available across net8.0/net9.0/net10.0 (Task, JsonSerializerOptions are net8.0+ compatible)

**Checkpoint**: Foundation verified — all implementation uses net8.0+ APIs only

---

## Phase 3: User Story 1 — Send Datastar SSE Events Without External Dependencies (Priority: P1) MVP

**Goal**: Core SSE event implementation already complete — this story validates the implementation conforms to ADR and works across all target frameworks

**Independent Test**: Send each event type through the implementation and verify SSE output conforms to the Datastar SDK ADR specification

**Acceptance Criteria**:
- [ ] US1-AC1: patchElements generates well-formed `event: datastar-patch-elements` SSE message with multi-line data
- [ ] US1-AC2: patchSignals generates well-formed `event: datastar-patch-signals` SSE message
- [ ] US1-AC3: removeElement generates SSE message with `mode: remove`
- [ ] US1-AC4: executeScript generates SSE message with script tag wrapped in datastar-patch-elements
- [ ] US1-AC5: tryReadSignals (GET) deserializes JSON from `datastar` query parameter
- [ ] US1-AC6: tryReadSignals (POST) deserializes JSON from request body

**Implementation Tasks** (verification only, code already exists):

- [ ] T007 [US1] Verify `ServerSentEventGenerator.PatchElementsAsync` in `src/Frank.Datastar/ServerSentEventGenerator.fs` writes correct SSE format per data-model.md (event type, optional id/retry, data lines for selector/mode/useViewTransition/namespace/elements, blank line terminator)
- [ ] T008 [US1] Verify `ServerSentEventGenerator.PatchSignalsAsync` in `src/Frank.Datastar/ServerSentEventGenerator.fs` writes correct SSE format per data-model.md (event type, optional id/retry, data line for onlyIfMissing, data lines for signals, blank line terminator)
- [ ] T009 [US1] Verify `ServerSentEventGenerator.RemoveElementAsync` in `src/Frank.Datastar/ServerSentEventGenerator.fs` writes correct SSE format per data-model.md (event type datastar-patch-elements, mode remove, selector, optional useViewTransition, blank line terminator)
- [ ] T010 [US1] Verify `ServerSentEventGenerator.ExecuteScriptAsync` in `src/Frank.Datastar/ServerSentEventGenerator.fs` writes correct SSE format per data-model.md (event type datastar-patch-elements, selector body, mode append, script tag with optional attributes and autoRemove, blank line terminator)
- [ ] T011 [US1] Verify `ServerSentEventGenerator.ReadSignalsAsync` in `src/Frank.Datastar/ServerSentEventGenerator.fs` reads from `datastar` query parameter on GET and request body on other methods, returns empty string on failure
- [ ] T012 [US1] Verify `ServerSentEventGenerator.ReadSignalsAsync<'T>` in `src/Frank.Datastar/ServerSentEventGenerator.fs` deserializes JSON correctly and returns `ValueNone` on parse failure

**Validation Tasks**:

- [ ] T013 [US1] Run existing tests in `test/Frank.Datastar.Tests/DatastarTests.fs` against the new implementation — all tests must pass (SC-001)
- [ ] T014 [US1] Build `src/Frank.Datastar/Frank.Datastar.fsproj` for all three target frameworks (net8.0, net9.0, net10.0) and verify no compilation errors
- [ ] T015 [US1] Verify `src/Frank.Datastar/Frank.Datastar.fsproj` has zero external NuGet dependencies (only framework references and Frank core project reference) (SC-003)

**Story 1 Complete**: ✅ Core SSE implementation validated and conforming to ADR specification

---

## Phase 4: User Story 2 — Preserve Existing Frank.Datastar Public API (Priority: P1)

**Goal**: Ensure complete API compatibility — no breaking changes to existing consumers

**Independent Test**: Compile existing Frank.Datastar sample projects and test suite against new implementation without any source code changes

**Acceptance Criteria**:
- [ ] US2-AC1: Frank.Datastar.Basic sample compiles without source changes
- [ ] US2-AC2: Frank.Datastar.Hox sample compiles without source changes
- [ ] US2-AC3: Frank.Datastar.Oxpecker sample compiles without source changes
- [ ] US2-AC4: Frank.Datastar.Tests test project runs all tests successfully without modification to test logic

**Implementation Tasks** (verification only, API is preserved):

- [ ] T016 [US2] Verify `ResourceBuilder.datastar` custom operation in `src/Frank.Datastar/Frank.Datastar.fs` matches existing signature: `spec:ResourceSpec * operation:(HttpContext -> Task<unit>) -> ResourceSpec`
- [ ] T017 [US2] Verify `ResourceBuilder.datastar` custom operation overload in `src/Frank.Datastar/Frank.Datastar.fs` matches existing signature: `spec:ResourceSpec * method:string * operation:(HttpContext -> Task<unit>) -> ResourceSpec`
- [ ] T018 [US2] Verify all `Datastar` module functions in `src/Frank.Datastar/Frank.Datastar.fs` match existing signatures per contracts/api-surface.md (patchElements, patchElementsWithOptions, patchSignals, patchSignalsWithOptions, tryReadSignals, tryReadSignalsWithOptions, removeElement, removeElementWithOptions, executeScript, executeScriptWithOptions)
- [ ] T019 [US2] Verify all option types in `src/Frank.Datastar/Types.fs` match existing field names and types per contracts/api-surface.md (PatchElementsOptions, PatchSignalsOptions, RemoveElementOptions, ExecuteScriptOptions with Attributes as string[])
- [ ] T020 [US2] Verify all enums in `src/Frank.Datastar/Consts.fs` match existing variants per contracts/api-surface.md (ElementPatchMode, PatchElementNamespace)

**Validation Tasks**:

- [ ] T021 [US2] Build `samples/Frank.Datastar.Basic/` without any source changes — compilation must succeed (SC-002)
- [ ] T022 [US2] Build `samples/Frank.Datastar.Hox/` without any source changes — compilation must succeed (SC-002)
- [ ] T023 [US2] Build `samples/Frank.Datastar.Oxpecker/` without any source changes — compilation must succeed (SC-002)
- [ ] T024 [US2] Run all tests in `test/Frank.Datastar.Tests/DatastarTests.fs` without modifying test logic (only namespace imports if needed) — all tests must pass (SC-001)

**Story 2 Complete**: ✅ API compatibility validated — seamless upgrade experience

---

## Phase 5: User Story 3 — Efficient Resource Usage via Direct Buffer Writing (Priority: P2)

**Goal**: Validate performance characteristics match or exceed StarFederation.Datastar.FSharp baseline

**Independent Test**: Benchmark the new implementation against the existing one using a standardized workload and compare allocation counts and throughput

**Acceptance Criteria**:
- [ ] US3-AC1: Pre-allocated byte arrays used for SSE prefixes (no runtime string-to-byte conversions)
- [ ] US3-AC2: Zero-allocation string segmentation for multi-line payloads (via StringTokenizer)
- [ ] US3-AC3: Direct buffer writing without intermediate string/byte copies (IBufferWriter<byte>)

**Implementation Tasks** (verification only, performance optimizations already implemented):

- [ ] T025 [US3] Verify `src/Frank.Datastar/Consts.fs` Bytes module pre-allocates all SSE field prefixes and enum values as byte arrays using `"..."B` syntax
- [ ] T026 [US3] Verify `src/Frank.Datastar/ServerSentEvent.fs` String.splitLinesToSegments uses StringTokenizer from Microsoft.Extensions.Primitives for zero-allocation line splitting
- [ ] T027 [US3] Verify all `ServerSentEventGenerator` methods in `src/Frank.Datastar/ServerSentEventGenerator.fs` write directly to `HttpResponse.BodyWriter` (IBufferWriter<byte>) without intermediate allocations
- [ ] T028 [US3] Verify all `Datastar` module functions in `src/Frank.Datastar/Frank.Datastar.fs` are marked `inline` to ensure zero-overhead wrapper calls

**Validation Tasks**:

- [ ] T029 [US3] Create a simple benchmark workload (10,000 patchElements events with multi-line HTML payload sent to a mock response stream) and measure allocation count
- [ ] T030 [US3] Compare allocation profile against baseline — new implementation must allocate no more memory per event than StarFederation.Datastar.FSharp (SC-005)

**Story 3 Complete**: ✅ Performance parity validated — zero-allocation buffer writing confirmed

---

## Phase 6: User Story 4 — Maintain Multi-Target Support for Frank.Datastar (Priority: P2)

**Goal**: Verify the library builds and passes all tests on net8.0, net9.0, and net10.0

**Independent Test**: Verify project file targets net8.0, net9.0, and net10.0, and that the library builds and passes all tests on each target framework

**Acceptance Criteria**:
- [ ] US4-AC1: Frank.Datastar.fsproj targets `net8.0;net9.0;net10.0`
- [ ] US4-AC2: Consumer project targeting .NET 8.0 can reference Frank.Datastar 7.1.0 and run successfully
- [ ] US4-AC3: Consumer project targeting .NET 9.0 can reference Frank.Datastar 7.1.0 and run successfully
- [ ] US4-AC4: Consumer project targeting .NET 10.0 can reference Frank.Datastar 7.1.0 and run successfully

**Implementation Tasks** (verification only, multi-targeting already configured in T001):

- [ ] T031 [US4] Verify `src/Frank.Datastar/Frank.Datastar.fsproj` contains `<TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>` (updated in T001)
- [ ] T032 [US4] Verify `src/Frank.Datastar/Frank.Datastar.fsproj` version is `7.1.0` (updated in T001)
- [ ] T033 [US4] Verify no conditional compilation directives (`#if`, `#else`, `#endif`) are needed — all code uses APIs available across net8.0/net9.0/net10.0

**Validation Tasks**:

- [ ] T034 [US4] Build `src/Frank.Datastar/Frank.Datastar.fsproj` specifically for net8.0 target and verify no compilation errors
- [ ] T035 [US4] Build `src/Frank.Datastar/Frank.Datastar.fsproj` specifically for net9.0 target and verify no compilation errors
- [ ] T036 [US4] Build `src/Frank.Datastar/Frank.Datastar.fsproj` specifically for net10.0 target and verify no compilation errors
- [ ] T037 [US4] Run all tests in `test/Frank.Datastar.Tests/` targeting net8.0 and verify all tests pass
- [ ] T038 [US4] Run all tests in `test/Frank.Datastar.Tests/` targeting net9.0 and verify all tests pass
- [ ] T039 [US4] Run all tests in `test/Frank.Datastar.Tests/` targeting net10.0 and verify all tests pass

**Story 4 Complete**: ✅ Multi-framework compatibility validated — broad framework support confirmed

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, edge case handling, and release preparation

**Tasks**:

- [ ] T040 Verify edge case handling in `ServerSentEventGenerator.StartServerEventStreamAsync` in `src/Frank.Datastar/ServerSentEventGenerator.fs`: SSE stream initialization occurs exactly once per request via `HttpContext.Items` flag check (FR-010, SC-006)
- [ ] T041 Verify edge case handling in all async methods in `src/Frank.Datastar/ServerSentEventGenerator.fs`: All methods accept and respect `CancellationToken` (HttpContext.RequestAborted) for graceful client disconnection (FR-011)
- [ ] T042 Verify edge case handling for empty strings in `ServerSentEventGenerator.PatchElementsAsync` and `PatchSignalsAsync`: Empty payloads generate valid SSE events with no data lines (matching current behavior per Edge Cases section)
- [ ] T043 Verify edge case handling for Windows line endings in `ServerSentEvent.fs` String.splitLinesToSegments: StringTokenizer handles both `\n` and `\r\n` correctly (per Edge Cases section)
- [ ] T044 Verify edge case handling in `ServerSentEventGenerator.ReadSignalsAsync<'T>`: Returns `ValueNone` for empty request body or missing query parameter without throwing (per Edge Cases section)
- [ ] T045 Run full test suite against all three target frameworks (net8.0, net9.0, net10.0) and verify all tests pass (SC-001)
- [ ] T046 Build all three sample projects (Basic, Hox, Oxpecker) and verify they compile and run correctly without source changes (SC-002)
- [ ] T047 Verify `src/Frank.Datastar/Frank.Datastar.fsproj` package description emphasizes native SSE implementation and multi-targeting support
- [ ] T048 Verify version 7.1.0 is reflected in all package metadata (VersionPrefix, Description mentions minor update) (SC-007)
- [ ] T049 Manual test: Run one sample project and verify browser Datastar client interaction works correctly (SSE events delivered, signals read, no console errors)
- [ ] T050 Manual test: Verify SSE output format matches Datastar SDK ADR specification byte-for-byte using a reference test vector (SC-004)
- [ ] T051 Verify per-event flushing in all event-writing methods in `src/Frank.Datastar/ServerSentEventGenerator.fs`: Each method (PatchElementsAsync, PatchSignalsAsync, RemoveElementAsync, ExecuteScriptAsync) calls `writer.FlushAsync(cancellationToken)` after writing the event to ensure immediate delivery per FR-013

---

## Phase 8: Release Documentation & CI Updates

**Purpose**: Update build infrastructure, CI pipeline, and release documentation to reflect multi-targeting and version changes

**Tasks**:

- [ ] T052 Update build scripts (e.g., `build.sh`, `build.ps1`, `build.fsx`) to build Frank.Datastar for all three target frameworks (net8.0, net9.0, net10.0) and remove any net10.0-only assumptions
- [ ] T053 Update CI pipeline configuration (e.g., `.github/workflows/`, `azure-pipelines.yml`) to build and test Frank.Datastar on all three target frameworks using build matrix strategy
- [ ] T054 Update `README.md` in repository root to document that Frank.Datastar 7.1.0 supports net8.0/net9.0/net10.0 and uses native SSE implementation (no external Datastar dependency)
- [ ] T055 Create or update `RELEASE_NOTES.md` (or similar) to document version 7.1.0 changes: (1) Native SSE implementation replaces StarFederation.Datastar.FSharp dependency, (2) Multi-targeting restored to net8.0/net9.0/net10.0, (3) Zero breaking API changes - seamless upgrade from 7.0.x, (4) Performance improvements via direct buffer writing
- [ ] T056 Verify all documentation files (README, RELEASE_NOTES, package description, project homepage) use consistent version number (7.1.0) and framework targets (net8.0;net9.0;net10.0)
- [ ] T057 Update any developer documentation (CONTRIBUTING.md, docs/) to reflect that Frank.Datastar now uses native implementation and document how to build/test across multiple frameworks
- [ ] T058 Update NuGet package metadata in `src/Frank.Datastar/Frank.Datastar.fsproj` (Description, PackageTags, PackageReleaseNotes) to emphasize: "Native SSE implementation, no external dependencies, supports .NET 8.0/9.0/10.0"

---

## Summary

### Task Count by Phase

| Phase | Task Count | Purpose |
|-------|------------|---------|
| Phase 1: Setup | 1 | Project configuration update (multi-targeting + version) |
| Phase 2: Foundational | 5 | Verify multi-framework API compatibility |
| Phase 3: User Story 1 (P1) | 9 | Core SSE implementation validation |
| Phase 4: User Story 2 (P1) | 9 | API compatibility verification |
| Phase 5: User Story 3 (P2) | 6 | Performance validation |
| Phase 6: User Story 4 (P2) | 9 | Multi-framework validation |
| Phase 7: Polish | 12 | Edge cases, final validation, release prep |
| Phase 8: Release Documentation | 7 | Build scripts, CI, README, RELEASE_NOTES updates |
| **TOTAL** | **58** | |

### User Story Independence

- **US1** (Core SSE Implementation): Foundation for all other stories — MUST complete first
- **US2** (API Compatibility): Can run in parallel with US3 and US4 validation tasks
- **US3** (Performance): Can run in parallel with US2 and US4 after US1 complete
- **US4** (Multi-Targeting): Can run in parallel with US2 and US3 after US1 complete

### Parallel Execution Opportunities

**After Phase 2 (Foundational) completes:**

- Parallel Group 1 (US1 verification): T007-T015 (validate core SSE implementation)
- Parallel Group 2 (US2 verification): T016-T024 (validate API compatibility)
- Parallel Group 3 (US3 verification): T025-T030 (validate performance)
- Parallel Group 4 (US4 verification): T031-T039 (validate multi-targeting)

**Phase 7 (Polish) runs sequentially after all user stories complete.**

### MVP Scope

**Minimum Viable Product**: Phase 1 + Phase 2 + Phase 3 (User Story 1)

This delivers:
- ✅ Multi-targeting configuration (net8.0/net9.0/net10.0)
- ✅ Dependency removal (StarFederation.Datastar.FSharp eliminated)
- ✅ Core SSE implementation (all event types working)
- ✅ Version 7.1.0 release ready

Remaining phases add validation confidence and release readiness:
- Phase 4 (US2): API compatibility validation (de-risks upgrade)
- Phase 5 (US3): Performance validation (confirms optimization goals)
- Phase 6 (US4): Multi-framework validation (ensures broad compatibility)
- Phase 7: Polish & edge cases (production hardening)
- Phase 8: Release documentation (build/CI/docs updates for publication)

### Implementation Strategy

1. **Start with T001** (Setup) — update project file for multi-targeting and version 7.1.0
2. **Complete Phase 2** (Foundational) — verify all code uses net8.0+ APIs only
3. **Execute User Story 1** (Phase 3) — validate core implementation works
4. **Parallelize US2/US3/US4 validation** — run compatibility, performance, and multi-framework tests concurrently
5. **Complete Phase 7** (Polish) — final edge case validation and per-event flush verification
6. **Finish with Phase 8** (Release Documentation) — update build scripts, CI pipeline, README, and RELEASE_NOTES

### Critical Success Criteria Mapping

| Success Criterion | Tasks | Phase |
|-------------------|-------|-------|
| SC-001: All tests pass | T013, T024, T045 | 3, 4, 7 |
| SC-002: Samples compile unchanged | T021-T023, T046 | 4, 7 |
| SC-003: Zero external dependencies | T015 | 3 |
| SC-004: SSE format matches ADR | T007-T012, T050 | 3, 7 |
| SC-005: Performance parity | T029-T030 | 5 |
| SC-006: Single stream initialization | T040 | 7 |
| SC-007: Version 7.1.0 | T001, T032, T048, T056 | 1, 6, 7, 8 |
| FR-013: Per-event flush | T051 | 7 |
| Documentation Updated | T052-T058 | 8 |

