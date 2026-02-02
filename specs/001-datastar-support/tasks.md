# Tasks: Datastar SSE Streaming Support

**Input**: Design documents from `/specs/001-datastar-support/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Status**: ✅ Core library complete. FR-005 compliant. Tests passing.

**Scope**: Library and tests only. Sample applications will be added as separate feature specs.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Fix FR-005 Violations (Remove One-Off Operations) ✅ COMPLETE

**Purpose**: Remove custom operations that violate FR-005 (no one-off convenience operations)

**⚠️ CRITICAL**: The spec explicitly forbids one-off operations. Only the `datastar` streaming handler should be a ResourceBuilder custom operation.

- [x] T001 Remove `patchElements` custom operations (3 overloads) from ResourceBuilder in src/Frank.Datastar/Frank.Datastar.fs
- [x] T002 Remove `removeElement` custom operation from ResourceBuilder in src/Frank.Datastar/Frank.Datastar.fs
- [x] T003 Remove `patchSignals` custom operations (2 overloads) from ResourceBuilder in src/Frank.Datastar/Frank.Datastar.fs
- [x] T004 Remove `executeScript` custom operation from ResourceBuilder in src/Frank.Datastar/Frank.Datastar.fs
- [x] T005 Remove `readSignals` custom operation from ResourceBuilder in src/Frank.Datastar/Frank.Datastar.fs
- [x] T006 Remove `transformSignals` custom operation from ResourceBuilder in src/Frank.Datastar/Frank.Datastar.fs
- [x] T007 Verify only `datastar` custom operation remains on ResourceBuilder

**After Phase 1, ResourceBuilder should have ONLY:**
```fsharp
type ResourceBuilder with
    [<CustomOperation("datastar")>]
    member _.Datastar(spec: ResourceSpec, operation: HttpContext -> Task<unit>) : ResourceSpec
```

---

## Phase 2: Verify/Enhance Datastar Helper Module ✅ COMPLETE

**Purpose**: Ensure the `Datastar` module provides clean aliases for use inside the `datastar` handler

- [x] T008 Verify `Datastar.patchElements` helper exists in src/Frank.Datastar/Frank.Datastar.fs
- [x] T009 Verify `Datastar.patchSignals` helper exists in src/Frank.Datastar/Frank.Datastar.fs
- [x] T010 Verify `Datastar.removeElement` helper exists in src/Frank.Datastar/Frank.Datastar.fs
- [x] T011 Verify `Datastar.executeScript` helper exists in src/Frank.Datastar/Frank.Datastar.fs
- [x] T012 Verify `Datastar.tryReadSignals` helper exists in src/Frank.Datastar/Frank.Datastar.fs
- [x] T013 Remove `Datastar.stream` if it exists - unnecessary since users can use ServerSentEventGenerator directly

**Datastar module should provide (for use inside `datastar` handler OR standalone with manual stream init):**
```fsharp
module Datastar =
    let inline patchElements (html: string) (ctx: HttpContext) = ...
    let inline patchSignals (signals: string) (ctx: HttpContext) = ...
    let inline removeElement (selector: string) (ctx: HttpContext) = ...
    let inline executeScript (script: string) (ctx: HttpContext) = ...
    let inline tryReadSignals<'T> (ctx: HttpContext) = ...
```
**Note**: All functions are `inline` to ensure zero-overhead wrapper calls (Constitution Principle V).

---

## Phase 3: API Improvements ✅ COMPLETE

**Purpose**: Add HTTP method flexibility to the `datastar` custom operation

- [x] T014 Add HTTP method parameter support to `datastar` custom operation in src/Frank.Datastar/Frank.Datastar.fs - currently hardcodes GET only
- [x] T014a [US1] Add unit test verifying `datastar` operation works with POST method (not just GET) in test/Frank.Datastar.Tests/DatastarTests.fs
- [x] T015 Verify solution builds with `dotnet build Frank.Datastar.sln --configuration Release`
- [x] T016 Mark all `Datastar` module wrapper functions with `inline` in src/Frank.Datastar/Frank.Datastar.fs - ensures compiled code calls StarFederation.Datastar.FSharp methods directly (Constitution Principle V)

**Checkpoint**: Library API is FR-005 compliant, supports any HTTP method, and has zero-overhead wrappers ✅

---

## Phase 4: User Story 1 - Stream Multiple Updates to Client (Priority: P1) 🎯 MVP ✅ COMPLETE

**Goal**: Verify developers can create streaming resources that send multiple progressive updates

**Independent Test**: Create a resource streaming 10 updates with delays, verify each arrives progressively

### Validation for User Story 1

- [x] T018 [US1] Add unit test for multi-event streaming (10 progressive updates with ~100ms delays) verifying progressive arrival in test/Frank.Datastar.Tests/DatastarTests.fs
- [x] T019 [US1] Add unit test for single stream start per request (SC-002) in test/Frank.Datastar.Tests/DatastarTests.fs
- [x] T020 [US1] Add unit test for client disconnection detection via cancellation token in test/Frank.Datastar.Tests/DatastarTests.fs

**Checkpoint**: User Story 1 complete - streaming multiple updates validated via unit tests ✅

---

## Phase 4a: Edge Case Validation ✅ COMPLETE

**Purpose**: Validate documented edge case behaviors from spec.md

- [x] T021 [EC] Add unit test for empty stream - handler returns immediately without sending events, verify HTTP 200 with empty SSE response in test/Frank.Datastar.Tests/DatastarTests.fs
- [x] T022 [EC] Add unit test for unused signals - client sends signals in request body but handler ignores them, verify no error occurs in test/Frank.Datastar.Tests/DatastarTests.fs

**Checkpoint**: Edge cases #1 and #4 validated via unit tests ✅

---

## Phase 5: User Story 2 - Read Client Signals During Stream (Priority: P2) ✅ COMPLETE

**Goal**: Verify developers can read signals from client request body within streaming handlers

**Independent Test**: Send request with JSON signals, verify handler deserializes them correctly

### Validation for User Story 2

- [x] T024 [US2] Verify existing test covers valid JSON deserialization via `Datastar.tryReadSignals` in test/Frank.Datastar.Tests/DatastarTests.fs
- [x] T025 [US2] Verify existing test covers malformed JSON returning ValueNone in test/Frank.Datastar.Tests/DatastarTests.fs

**Checkpoint**: User Story 2 complete - signal reading validated via unit tests ✅

---

## Phase 6: User Story 3 - Standalone Helper Functions (Priority: P3) ✅ COMPLETE

**Goal**: Verify `Datastar.*` helper functions work correctly inside the `datastar` handler

**Independent Test**: Unit test each helper function in isolation

### Validation for User Story 3

- [x] T028 [US3] Add unit test for `Datastar.patchElements` helper function in test/Frank.Datastar.Tests/DatastarTests.fs
- [x] T029 [US3] Add unit test for `Datastar.patchSignals` helper function in test/Frank.Datastar.Tests/DatastarTests.fs
- [x] T030 [US3] Add unit test for `Datastar.removeElement` helper function in test/Frank.Datastar.Tests/DatastarTests.fs
- [x] T031 [US3] Add unit test for `Datastar.executeScript` helper function in test/Frank.Datastar.Tests/DatastarTests.fs

**Checkpoint**: User Story 3 complete - helper functions validated ✅

---

## Phase 7: Final Validation ✅ COMPLETE

**Purpose**: Final validation and quality improvements

- [x] T037 Run all unit tests with `dotnet test test/Frank.Datastar.Tests` - 14 tests passing
- [x] T038 [P] Update README.md in src/Frank.Datastar/ to reflect correct API (only `datastar` operation + helper module)
- [x] T039 [P] Verify library uses latest StarFederation.Datastar.FSharp package version (SC-004) - uses `Version="*"` for latest
- [x] T040 Validate success criteria checklist:
  - SC-001: ✅ 10 progressive updates arrive progressively (tested)
  - SC-002: ✅ Single stream start per request (tested)
  - SC-003: ✅ 100% F# idioms (computation expressions, voption, etc.)
  - SC-004: ✅ Latest StarFederation.Datastar package (Version="*")
  - SC-005: ✅ Malformed JSON returns ValueNone (tested)
  - SC-006: ✅ Natural integration with resource builder (`datastar` custom operation)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (FR-005 Fix)**: No dependencies - MUST complete first
- **Phase 2 (Helper Module)**: Can parallel with Phase 1
- **Phase 3 (API Improvements)**: Depends on Phase 1
- **Phases 4-6 (User Stories)**: Depend on Phases 1-3
- **Phase 7 (Final Validation)**: Depends on all previous phases

### Parallel Opportunities

**Phases 1-2**:
- T001-T007 sequential (same section of code)
- T008-T013 can verify in parallel

**Phases 4-6** (after Phase 3):
- All user story phases can run in parallel if team capacity allows

---

## Correct Usage Pattern

```fsharp
// The ONLY custom operation on ResourceBuilder
let updates =
    resource "/updates" {
        name "Updates"
        datastar (fun ctx -> task {
            // Use Datastar.* helpers inside the handler
            do! Datastar.patchElements "<div id='a'>First</div>" ctx
            do! Task.Delay(100)
            do! Datastar.patchElements "<div id='b'>Second</div>" ctx

            // Read signals if needed
            let! signals = Datastar.tryReadSignals<MySignals> ctx
            match signals with
            | ValueSome s -> do! Datastar.patchSignals $"{{\"received\": true}}" ctx
            | ValueNone -> ()
        })
    }
```

---

## Notes

- **FR-005 is critical**: No one-off convenience operations on ResourceBuilder
- Only `datastar` custom operation should exist on ResourceBuilder
- `Datastar` module helpers are for use INSIDE the streaming handler
- No `Datastar.stream` needed - users not using Frank can use `ServerSentEventGenerator` directly
- HTTP method flexibility needed to match StarFederation.Datastar.FSharp
- **Samples deferred**: Sample applications will be added as separate feature specs
