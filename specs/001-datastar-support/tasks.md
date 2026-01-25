# Tasks: Datastar SSE Streaming Support

**Input**: Design documents from `/specs/001-datastar-support/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Status**: Core library partially implemented with FR-005 violations. Tasks focus on removing one-off operations, fixing samples, and completing validation.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Fix FR-005 Violations (Remove One-Off Operations)

**Purpose**: Remove custom operations that violate FR-005 (no one-off convenience operations)

**⚠️ CRITICAL**: The spec explicitly forbids one-off operations. Only the `datastar` streaming handler should be a ResourceBuilder custom operation.

- [ ] T001 Remove `patchElements` custom operations (3 overloads) from ResourceBuilder in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T002 Remove `removeElement` custom operation from ResourceBuilder in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T003 Remove `patchSignals` custom operations (2 overloads) from ResourceBuilder in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T004 Remove `executeScript` custom operation from ResourceBuilder in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T005 Remove `readSignals` custom operation from ResourceBuilder in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T006 Remove `transformSignals` custom operation from ResourceBuilder in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T007 Verify only `datastar` custom operation remains on ResourceBuilder

**After Phase 1, ResourceBuilder should have ONLY:**
```fsharp
type ResourceBuilder with
    [<CustomOperation("datastar")>]
    member _.Datastar(spec: ResourceSpec, operation: HttpContext -> Task<unit>) : ResourceSpec
```

---

## Phase 2: Verify/Enhance Datastar Helper Module

**Purpose**: Ensure the `Datastar` module provides clean aliases for use inside the `datastar` handler

- [ ] T008 Verify `Datastar.patchElements` helper exists in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T009 Verify `Datastar.patchSignals` helper exists in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T010 Verify `Datastar.removeElement` helper exists in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T011 Verify `Datastar.executeScript` helper exists in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T012 Verify `Datastar.tryReadSignals` helper exists in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T013 Remove `Datastar.stream` if it exists - unnecessary since users can use ServerSentEventGenerator directly

**Datastar module should provide:**
```fsharp
module Datastar =
    let patchElements (html: string) (ctx: HttpContext) = ...
    let patchSignals (signals: string) (ctx: HttpContext) = ...
    let removeElement (selector: string) (ctx: HttpContext) = ...
    let executeScript (script: string) (ctx: HttpContext) = ...
    let tryReadSignals<'T> (ctx: HttpContext) = ...
```

---

## Phase 3: API Improvements

**Purpose**: Add HTTP method flexibility to the `datastar` custom operation

- [ ] T014 Add HTTP method parameter support to `datastar` custom operation in src/Frank.Datastar/Frank.Datastar.fs - currently hardcodes GET only
- [ ] T015 Verify solution builds with `dotnet build Frank.Datastar.sln --configuration Release`

**Checkpoint**: Library API is FR-005 compliant and supports any HTTP method

---

## Phase 4: Fix Sample Compilation Issues

**Purpose**: Resolve blocking compilation errors so samples build

- [ ] T016 [P] Fix F# format string issues in sample/Frank.Datastar.Basic/Program.fs - replace interpolation format specifiers with explicit .ToString() calls
- [ ] T017 [P] Fix Hox API in sample/Frank.Datastar.Hox/Program.fs - change `Render.AsStringAsync` to `Render.asString`, change `Attr("name", "value")` to `.extension` syntax per Hox docs

---

## Phase 5: User Story 1 - Stream Multiple Updates to Client (Priority: P1) 🎯 MVP

**Goal**: Verify developers can create streaming resources that send multiple progressive updates

**Independent Test**: Create a resource streaming 10 updates with delays, verify each arrives progressively

### Validation for User Story 1

- [ ] T018 [US1] Add unit test for multi-event streaming (10 progressive updates with ~100ms delays) verifying progressive arrival in test/Frank.Datastar.Tests/DatastarTests.fs
- [ ] T019 [US1] Add unit test for single stream start per request (SC-002) in test/Frank.Datastar.Tests/DatastarTests.fs
- [ ] T020 [US1] Add unit test for client disconnection detection via cancellation token in test/Frank.Datastar.Tests/DatastarTests.fs

### Implementation for User Story 1

- [ ] T021 [US1] Refactor Basic sample to use `webHost` builder pattern with `datastar` operation and `Datastar.*` helpers in sample/Frank.Datastar.Basic/Program.fs
- [ ] T022 [US1] Test Basic sample compiles and runs with `dotnet run --project sample/Frank.Datastar.Basic`
- [ ] T023 [US1] Manually test Basic sample SSE streaming works in browser with Datastar client

**Checkpoint**: User Story 1 complete - streaming multiple updates is validated end-to-end

---

## Phase 6: User Story 2 - Read Client Signals During Stream (Priority: P2)

**Goal**: Verify developers can read signals from client request body within streaming handlers

**Independent Test**: Send request with JSON signals, verify handler deserializes them correctly

### Validation for User Story 2

- [ ] T024 [US2] Verify existing test covers valid JSON deserialization via `Datastar.tryReadSignals` in test/Frank.Datastar.Tests/DatastarTests.fs
- [ ] T025 [US2] Verify existing test covers malformed JSON returning ValueNone in test/Frank.Datastar.Tests/DatastarTests.fs
- [ ] T026 [US2] Update Basic sample to demonstrate signal reading using `Datastar.tryReadSignals` within `datastar` handler

### Implementation for User Story 2

- [ ] T027 [US2] Test signal reading with curl or similar tool sending JSON body

**Checkpoint**: User Story 2 complete - signal reading validated with hypermedia-first responses

---

## Phase 7: User Story 3 - Standalone Helper Functions (Priority: P3)

**Goal**: Verify `Datastar.*` helper functions work correctly inside the `datastar` handler

**Independent Test**: Unit test each helper function in isolation

### Validation for User Story 3

- [ ] T028 [US3] Add unit test for `Datastar.patchElements` helper function in test/Frank.Datastar.Tests/DatastarTests.fs
- [ ] T029 [US3] Add unit test for `Datastar.patchSignals` helper function in test/Frank.Datastar.Tests/DatastarTests.fs
- [ ] T030 [US3] Add unit test for `Datastar.removeElement` helper function in test/Frank.Datastar.Tests/DatastarTests.fs
- [ ] T031 [US3] Add unit test for `Datastar.executeScript` helper function in test/Frank.Datastar.Tests/DatastarTests.fs

**Checkpoint**: User Story 3 complete - helper functions validated

---

## Phase 8: Hox Sample Validation

**Goal**: Complete Hox sample compilation and demonstrate type-safe HTML rendering with Datastar

- [ ] T032 [P] Fix all Hox API calls to use `Render.asString` throughout sample/Frank.Datastar.Hox/Program.fs
- [ ] T033 [P] Fix Hox attributes - change `Attr("name", "value")` to `.extension` syntax throughout sample/Frank.Datastar.Hox/Program.fs
- [ ] T034 Refactor Hox sample to use `webHost` builder with `datastar` operation in sample/Frank.Datastar.Hox/Program.fs
- [ ] T035 Test Hox sample compiles and runs with `dotnet run --project sample/Frank.Datastar.Hox`
- [ ] T036 Manually test Hox sample SSE streaming works in browser

**Checkpoint**: Both samples compile without warnings (SC-007)

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and quality improvements

- [ ] T037 Run all unit tests with `dotnet test test/Frank.Datastar.Tests`
- [ ] T038 [P] Update README.md in src/Frank.Datastar/ to reflect correct API (only `datastar` operation + helper module)
- [ ] T039 [P] Verify library uses latest StarFederation.Datastar.FSharp package version (SC-004)
- [ ] T040 Update quickstart.md to show correct usage pattern
- [ ] T041 Update contracts/frank-datastar-api.md to reflect correct API
- [ ] T042 Validate success criteria checklist:
  - SC-001: 10 progressive updates arrive progressively
  - SC-002: Single stream start per request
  - SC-003: 100% F# idioms
  - SC-004: Latest StarFederation.Datastar package
  - SC-005: Malformed JSON returns ValueNone
  - SC-006: Natural integration with resource builder
  - SC-007: Both samples compile without warnings

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (FR-005 Fix)**: No dependencies - MUST complete first
- **Phase 2 (Helper Module)**: Can parallel with Phase 1
- **Phase 3 (API Improvements)**: Depends on Phase 1
- **Phase 4 (Sample Fixes)**: Can start after Phase 3
- **Phases 5-8 (User Stories + Hox)**: Depend on Phases 1-4
- **Phase 9 (Polish)**: Depends on all previous phases

### Parallel Opportunities

**Phases 1-2**:
- T001-T007 sequential (same section of code)
- T008-T013 can verify in parallel

**Phase 4**:
- T016 and T017 can run in parallel (different files)

**Phases 5-8** (after Phase 4):
- All user story phases can run in parallel if team capacity allows

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Remove FR-005 violations (T001-T007)
2. Complete Phase 2: Verify helper module (T008-T013)
3. Complete Phase 3: Add HTTP method support (T014-T015)
4. Complete Phase 4: Fix sample compilation (T016-T017)
5. Complete Phase 5: User Story 1 (T018-T023)
6. **STOP and VALIDATE**: Test streaming works end-to-end

### Correct Usage Pattern

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
- Samples MUST use Frank's `webHost` builder pattern
- Hox attributes: use `.extension` syntax (not `Attr("name", "value")`)
- HTTP method flexibility needed to match StarFederation.Datastar.FSharp
