# Tasks: Add WithOptions Variants for Datastar Helpers

**Input**: Design documents from `/specs/010-datastar-patch-mode/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md

**Tests**: Tests are included as this is a library feature where correctness is verified via unit tests.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Include exact file paths in descriptions

## Path Conventions

- **Source**: `src/Frank.Datastar/Frank.Datastar.fs`
- **Tests**: `test/Frank.Datastar.Tests/DatastarTests.fs`

---

## Phase 1: Setup (Verification)

**Purpose**: Verify build environment and existing tests pass before changes

- [ ] T001 Verify project builds: `dotnet build src/Frank.Datastar/Frank.Datastar.fsproj`
- [ ] T002 Verify existing tests pass: `dotnet test test/Frank.Datastar.Tests/`

**Checkpoint**: Existing codebase verified working

---

## Phase 2: User Story 1 - Use Custom Options with Any Datastar Helper (Priority: P1) 🎯 MVP

**Goal**: Add 5 `WithOptions` functions enabling developers to specify full options for any Datastar helper

**Independent Test**: Call each `WithOptions` variant with non-default options and verify SSE output reflects specified options

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T003 [P] [US1] Add test `patchElementsWithOptions sends custom mode` in test/Frank.Datastar.Tests/DatastarTests.fs
- [ ] T004 [P] [US1] Add test `patchSignalsWithOptions sends onlyIfMissing option` in test/Frank.Datastar.Tests/DatastarTests.fs
- [ ] T005 [P] [US1] Add test `removeElementWithOptions sends useViewTransition option` in test/Frank.Datastar.Tests/DatastarTests.fs
- [ ] T006 [P] [US1] Add test `executeScriptWithOptions respects autoRemove false` in test/Frank.Datastar.Tests/DatastarTests.fs
- [ ] T007 [P] [US1] Add test `tryReadSignalsWithOptions uses custom JsonSerializerOptions` in test/Frank.Datastar.Tests/DatastarTests.fs

### Implementation for User Story 1

- [ ] T008 [US1] Add `System.Text.Json` open statement if needed in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T009 [P] [US1] Implement `patchElementsWithOptions` inline function in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T010 [P] [US1] Implement `patchSignalsWithOptions` inline function in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T011 [P] [US1] Implement `removeElementWithOptions` inline function in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T012 [P] [US1] Implement `executeScriptWithOptions` inline function in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T013 [P] [US1] Implement `tryReadSignalsWithOptions<'T>` inline function in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T014 [US1] Verify all US1 tests pass: `dotnet test test/Frank.Datastar.Tests/`

**Checkpoint**: All 5 `WithOptions` functions implemented and tested with non-default options

---

## Phase 3: User Story 2 - Maintain Existing API Compatibility (Priority: P2)

**Goal**: Verify existing simple helpers remain unchanged and backward compatible

**Independent Test**: Run all existing tests without modification; verify they still pass

### Tests for User Story 2

- [ ] T015 [P] [US2] Add test `patchElementsWithOptions with Defaults equals patchElements output` in test/Frank.Datastar.Tests/DatastarTests.fs
- [ ] T016 [P] [US2] Add test `patchSignalsWithOptions with Defaults equals patchSignals output` in test/Frank.Datastar.Tests/DatastarTests.fs
- [ ] T017 [P] [US2] Add test `removeElementWithOptions with Defaults equals removeElement output` in test/Frank.Datastar.Tests/DatastarTests.fs
- [ ] T018 [P] [US2] Add test `executeScriptWithOptions with Defaults equals executeScript output` in test/Frank.Datastar.Tests/DatastarTests.fs

### Verification for User Story 2

- [ ] T019 [US2] Run full test suite including existing tests: `dotnet test test/Frank.Datastar.Tests/`
- [ ] T020 [US2] Verify existing simple helpers are unchanged (no modifications to existing function bodies)

**Checkpoint**: Backward compatibility verified; existing API unchanged

---

## Phase 4: Polish & Cross-Cutting Concerns

**Purpose**: Multi-target verification and documentation

- [ ] T021 [P] Verify build succeeds for net8.0: `dotnet build src/Frank.Datastar/Frank.Datastar.fsproj -f net8.0`
- [ ] T022 [P] Verify build succeeds for net9.0: `dotnet build src/Frank.Datastar/Frank.Datastar.fsproj -f net9.0`
- [ ] T023 [P] Verify build succeeds for net10.0: `dotnet build src/Frank.Datastar/Frank.Datastar.fsproj -f net10.0`
- [ ] T024 Add XML doc comments to all 5 new functions in src/Frank.Datastar/Frank.Datastar.fs
- [ ] T025 Final test run: `dotnet test test/Frank.Datastar.Tests/`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - verify environment first
- **User Story 1 (Phase 2)**: Depends on Setup - implements core feature
- **User Story 2 (Phase 3)**: Depends on User Story 1 - verifies compatibility
- **Polish (Phase 4)**: Depends on all user stories complete

### Within User Story 1

- T003-T007 (Tests): Write first, verify they FAIL
- T008: Import statement (if needed) - do first
- T009-T013 (Implementation): All can run in parallel (different functions, same file but non-overlapping)
- T014: Verify tests pass after implementation

### Parallel Opportunities

**Phase 1**: T001 and T002 sequential (T002 depends on T001)

**Phase 2 - Tests (T003-T007)**: All can run in parallel
```bash
# Launch all test tasks together:
T003: patchElementsWithOptions test
T004: patchSignalsWithOptions test
T005: removeElementWithOptions test
T006: executeScriptWithOptions test
T007: tryReadSignalsWithOptions test
```

**Phase 2 - Implementation (T009-T013)**: All can run in parallel
```bash
# Launch all implementation tasks together:
T009: patchElementsWithOptions
T010: patchSignalsWithOptions
T011: removeElementWithOptions
T012: executeScriptWithOptions
T013: tryReadSignalsWithOptions
```

**Phase 3 - Tests (T015-T018)**: All can run in parallel

**Phase 4 - Build verification (T021-T023)**: All can run in parallel

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup verification
2. Complete Phase 2: User Story 1 (5 WithOptions functions + tests)
3. **STOP and VALIDATE**: Test User Story 1 independently
4. This delivers the core feature value

### Full Delivery

1. Complete Setup → Verified environment
2. Complete User Story 1 → Core feature working (MVP!)
3. Complete User Story 2 → Backward compatibility verified
4. Complete Polish → Multi-target verified, documented

---

## Notes

- All 5 `WithOptions` functions go in the same file but are independent implementations
- Tests are written first (TDD) and must fail before implementation
- Each function is ~3 lines (inline wrapper calling ServerSentEventGenerator)
- Total implementation is ~30 lines of F# code
- Commit after each logical group (tests, then implementations)
