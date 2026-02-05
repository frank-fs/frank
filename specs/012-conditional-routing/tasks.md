# Tasks: Conditional Before-Routing Middleware

**Input**: Design documents from `/specs/012-conditional-routing/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, quickstart.md

**Tests**: Included - spec.md explicitly requests unit tests for both new operations.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Library project**: `src/Frank/` for source, `test/Frank.Tests/` for tests
- Paths follow existing Frank repository structure

---

## Phase 1: Setup

**Purpose**: No project initialization needed - all changes are to existing files

- [x] T001 Verify existing test infrastructure compiles by running `dotnet build test/Frank.Tests/`

**Checkpoint**: Build passes, ready to implement

---

## Phase 2: Foundational (Core Implementation)

**Purpose**: Implement the two new custom operations that all user stories depend on

**⚠️ CRITICAL**: User story tests cannot pass until these operations exist

- [x] T002 Add `plugBeforeRoutingWhen` custom operation to WebHostBuilder in src/Frank/Builder.fs (insert after line 269, after `plugBeforeRouting`)
- [x] T003 Add `plugBeforeRoutingWhenNot` custom operation to WebHostBuilder in src/Frank/Builder.fs (insert after `plugBeforeRoutingWhen`)
- [x] T004 Verify implementation compiles by running `dotnet build src/Frank/`

**Implementation Reference** (from plan.md):

```fsharp
[<CustomOperation("plugBeforeRoutingWhen")>]
member __.PlugBeforeRoutingWhen(spec, cond, f) =
    { spec with
        BeforeRoutingMiddleware = fun app ->
            if cond app then
                f(spec.BeforeRoutingMiddleware(app))
            else spec.BeforeRoutingMiddleware(app) }

[<CustomOperation("plugBeforeRoutingWhenNot")>]
member __.PlugBeforeRoutingWhenNot(spec, cond, f) =
    __.PlugBeforeRoutingWhen(spec, not << cond, f)
```

**Checkpoint**: Core operations implemented - user story testing can begin

---

## Phase 3: User Story 1 - Conditional HTTPS Redirection (Priority: P1) 🎯 MVP

**Goal**: Enable conditional middleware execution before routing when condition is true

**Independent Test**: Test that middleware executes only when condition returns true, and is skipped when false

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before Phase 2 implementation**

- [x] T005 [P] [US1] Add test "plugBeforeRoutingWhen executes middleware when condition is true" in test/Frank.Tests/MiddlewareOrderingTests.fs
- [x] T006 [P] [US1] Add test "plugBeforeRoutingWhen skips middleware when condition is false" in test/Frank.Tests/MiddlewareOrderingTests.fs

### Verification for User Story 1

- [x] T007 [US1] Run tests with `dotnet test test/Frank.Tests/` and verify US1 tests pass

**Checkpoint**: `plugBeforeRoutingWhen` fully tested - core conditional functionality verified

---

## Phase 4: User Story 2 - Conditional Static File Serving (Priority: P2)

**Goal**: Enable conditional middleware execution before routing using negated condition

**Independent Test**: Test that `plugBeforeRoutingWhenNot` correctly inverts the condition logic

### Tests for User Story 2

- [x] T008 [P] [US2] Add test "plugBeforeRoutingWhenNot executes middleware when condition is false" in test/Frank.Tests/MiddlewareOrderingTests.fs
- [x] T009 [P] [US2] Add test "plugBeforeRoutingWhenNot skips middleware when condition is true" in test/Frank.Tests/MiddlewareOrderingTests.fs

### Verification for User Story 2

- [x] T010 [US2] Run tests with `dotnet test test/Frank.Tests/` and verify US2 tests pass

**Checkpoint**: `plugBeforeRoutingWhenNot` fully tested - negated condition functionality verified

---

## Phase 5: User Story 3 - Conditional Security Headers (Priority: P2)

**Goal**: Verify composition of multiple conditional before-routing middleware and integration with existing operations

**Independent Test**: Test that multiple conditional middleware compose correctly and work with existing `plugBeforeRouting`

### Tests for User Story 3

- [x] T011 [P] [US3] Add test "Multiple conditional before-routing middleware compose correctly" in test/Frank.Tests/MiddlewareOrderingTests.fs
- [x] T012 [P] [US3] Add test "Conditional before-routing middleware works with regular plugBeforeRouting" in test/Frank.Tests/MiddlewareOrderingTests.fs

### Verification for User Story 3

- [x] T013 [US3] Run tests with `dotnet test test/Frank.Tests/` and verify US3 tests pass

**Checkpoint**: All composition and integration scenarios verified

---

## Phase 6: Polish & Documentation

**Purpose**: Documentation updates and final validation

- [x] T014 [P] Update README.md middleware section to document `plugBeforeRoutingWhen` operation
- [x] T015 [P] Update README.md middleware section to document `plugBeforeRoutingWhenNot` operation
- [x] T016 [P] Add usage example to README.md showing conditional HTTPS redirection
- [x] T017 Run full test suite with `dotnet test` to verify no regressions
- [x] T018 Update RELEASE_NOTES.md with new operations for next release

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - verify build first
- **Foundational (Phase 2)**: Depends on Setup - implements core operations
- **User Stories (Phase 3-5)**: All depend on Foundational phase completion
  - Tests can be written before Phase 2, but will fail until implementation exists
  - User stories can proceed in parallel after Phase 2
- **Polish (Phase 6)**: Depends on all user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Core `plugBeforeRoutingWhen` - no other story dependencies
- **User Story 2 (P2)**: Core `plugBeforeRoutingWhenNot` - no other story dependencies (implementation reuses US1)
- **User Story 3 (P2)**: Composition tests - can run after Phase 2, integrates behaviors from US1 and US2

### Within Each User Story

- Tests written first (TDD approach per spec requirements)
- Tests should fail until Phase 2 implementation
- Tests should pass after Phase 2 implementation

### Parallel Opportunities

Within Phase 3-5:
- All test tasks marked [P] can be written in parallel
- T005, T006, T008, T009, T011, T012 are all independent test additions

Within Phase 6:
- T014, T015, T016 can all be done in parallel (different README sections)

---

## Parallel Example: All Tests

```bash
# Launch all test writing tasks together (all [P] marked):
Task: "Add test 'plugBeforeRoutingWhen executes middleware when condition is true'"
Task: "Add test 'plugBeforeRoutingWhen skips middleware when condition is false'"
Task: "Add test 'plugBeforeRoutingWhenNot executes middleware when condition is false'"
Task: "Add test 'plugBeforeRoutingWhenNot skips middleware when condition is true'"
Task: "Add test 'Multiple conditional before-routing middleware compose correctly'"
Task: "Add test 'Conditional before-routing middleware works with regular plugBeforeRouting'"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (verify build)
2. Write Phase 3 tests first (T005, T006) - will fail
3. Complete Phase 2: Foundational (implement both operations)
4. Run Phase 3 verification (T007) - tests should pass
5. **STOP and VALIDATE**: Core functionality works

### Incremental Delivery

1. Setup + Foundational + US1 tests → MVP: `plugBeforeRoutingWhen` works
2. Add US2 tests → `plugBeforeRoutingWhenNot` verified
3. Add US3 tests → Composition verified
4. Add documentation → Feature complete

### TDD Sequence (Recommended)

1. T001 - Verify build
2. T005, T006 - Write US1 tests (will fail - operations don't exist)
3. T002, T003 - Implement operations
4. T004 - Verify implementation compiles
5. T007 - Run tests (should pass now)
6. T008, T009 - Write US2 tests
7. T010 - Run tests (should pass)
8. T011, T012 - Write US3 tests
9. T013 - Run tests (should pass)
10. T014-T018 - Documentation and final validation

---

## Notes

- All test tasks add to existing file: `test/Frank.Tests/MiddlewareOrderingTests.fs`
- All implementation tasks modify existing file: `src/Frank/Builder.fs`
- Implementation is ~10 lines of code total
- Tests reuse existing `createTestServer` helper from MiddlewareOrderingTests.fs
- Each test should follow the pattern of existing tests in that file
