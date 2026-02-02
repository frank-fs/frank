# Tasks: Browser Automation Test Suite for Frank.Datastar Samples

**Input**: Design documents from `/specs/005-fix-sample-tests/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, quickstart.md

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

This is a single test project located at `sample/Frank.Datastar.Tests/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and F# Playwright test project structure

- [x] T001 Create F# test project directory at `sample/Frank.Datastar.Tests/`
- [x] T002 Create `sample/Frank.Datastar.Tests/Frank.Datastar.Tests.fsproj` with NUnit and Playwright dependencies (Microsoft.Playwright.NUnit, NUnit, NUnit3TestAdapter, Microsoft.NET.Test.Sdk)
- [x] T003 [P] Create `sample/Frank.Datastar.Tests/test.runsettings` with DATASTAR_SAMPLE, DATASTAR_BASE_URL, DATASTAR_TIMEOUT_MS environment variables

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story tests can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T004 Create `sample/Frank.Datastar.Tests/TestConfiguration.fs` with:
  - SampleName, BaseUrl, TimeoutMs configuration from environment variables
  - discoverSamples function to find Frank.Datastar.* folders
  - Validation logic for sample name (must start with "Frank.Datastar.", must exist in discovered samples)
  - Help message generation listing available samples
- [x] T005 Create `sample/Frank.Datastar.Tests/TestHelpers.fs` with:
  - waitForText: Wait for element text content to match expected value
  - waitForTextContains: Wait for element to contain substring
  - waitForVisible: Wait for element to appear via SSE
  - waitForHidden: Wait for element to disappear
  - All helpers using WaitForFunctionAsync with configurable timeout
- [x] T006 Create `sample/Frank.Datastar.Tests/TestBase.fs` with:
  - Base test class with Playwright, Browser, Context, Page lifecycle management
  - OneTimeSetUp for browser launch
  - SetUp for fresh page per test with navigation to BaseUrl
  - TearDown for page/context cleanup
  - OneTimeTearDown for browser disposal
- [x] T006a Add connection error handling to `sample/Frank.Datastar.Tests/TestBase.fs`:
  - In SetUp, wrap page.GotoAsync in try/catch
  - On connection failure, throw with message: "Cannot connect to {BaseUrl}. Ensure the sample server is running: dotnet run --project sample/{SampleName}/"
- [x] T007 Build project and install Playwright browsers: `dotnet build && pwsh bin/Debug/net10.0/playwright.ps1 install`

**Checkpoint**: Foundation ready - user story implementation can now begin

---

## Phase 3: User Story 5 - Target Sample via Parameter (Priority: P1) 🎯 MVP

**Goal**: Enable tests to be run against any Frank.Datastar.* sample via environment variable

**Independent Test**: Run `dotnet test` without DATASTAR_SAMPLE and verify help message; run with invalid sample and verify error; run with valid sample and verify tests discover correct target

**Note**: This is implemented first because all other user stories depend on configuration being in place.

### Implementation for User Story 5

- [x] T008 [US5] Update `sample/Frank.Datastar.Tests/TestConfiguration.fs` to fail fast with helpful message when DATASTAR_SAMPLE is missing, listing all discovered samples
- [x] T009 [US5] Update `sample/Frank.Datastar.Tests/TestConfiguration.fs` to validate DATASTAR_SAMPLE starts with "Frank.Datastar." and show error with pattern requirement if not
- [x] T010 [US5] Update `sample/Frank.Datastar.Tests/TestConfiguration.fs` to validate DATASTAR_SAMPLE exists in discovered samples and show available samples if not found
- [x] T011 [US5] Create `sample/Frank.Datastar.Tests/ConfigurationTests.fs` with tests:
  - Test that configuration loads when valid DATASTAR_SAMPLE is set
  - Test that configuration reports sample name in test output
  - Verify discovered samples exclude Frank.Datastar.Tests itself

**Checkpoint**: Tests can now target any valid sample via DATASTAR_SAMPLE environment variable

---

## Phase 4: User Story 1 - Click-to-Edit SSE Updates (Priority: P1)

**Goal**: Verify click-to-edit displays current values in edit form (via SSE) and persists updated values

**Independent Test**: Run tests with `DATASTAR_SAMPLE=Frank.Datastar.Basic`, click edit on contact, verify form shows "Joe", change to "Updated", save, verify display shows "Updated"

### Implementation for User Story 1

- [x] T012 [US1] Create `sample/Frank.Datastar.Tests/ClickToEditTests.fs` with test fixture inheriting from TestBase
- [x] T013 [US1] Implement test `EditFormShowsCurrentValues`: Navigate to contact, click edit button, wait for edit form via SSE, assert firstName input contains "Joe"
- [x] T014 [US1] Implement test `SavedEditsAppearInDisplay`: Fill edit form with "Updated", click save, wait for view mode via SSE, assert display shows "Updated"
- [x] T015 [US1] Implement test `SavedEditsPersistedAfterRefresh`: After save, refresh page, wait for initial load, assert display shows "Updated" (not reverting to "Joe")
- [x] T016 [US1] Add test cleanup in ClickToEditTests to restore contact to original values after each test (PUT to reset "Joe", "Smith", "joe@smith.org")

**Checkpoint**: Click-to-edit validation complete - can detect bugs where edit form shows empty or updates don't persist

---

## Phase 5: User Story 2 - Search Filtering with SSE (Priority: P1)

**Goal**: Verify search filtering updates list via SSE with matching results

**Independent Test**: Run tests, type "ap" in search, verify list shows Apple and Apricot but not Banana

### Implementation for User Story 2

- [x] T017 [US2] Create `sample/Frank.Datastar.Tests/SearchFilterTests.fs` with test fixture inheriting from TestBase
- [x] T018 [US2] Implement test `SearchFiltersToMatchingItems`: Navigate to fruits, type "ap" in search, wait for list update via SSE, assert Apple and Apricot visible, Banana not visible
- [x] T019 [US2] Implement test `ClearSearchRestoresFullList`: After filtering, clear search input, wait for list update via SSE, assert all fruits (Apple, Apricot, Banana) visible
- [x] T020 [US2] Implement test `NoMatchesShowsEmptyOrMessage`: Type query with no matches (e.g., "xyz"), wait for response, assert either empty list or "no results" indicator (no error)

**Checkpoint**: Search filtering validation complete - can detect bugs where search clears list entirely

---

## Phase 6: User Story 3 - Bulk Update Operations (Priority: P1)

**Goal**: Verify bulk status changes update selected users via SSE

**Independent Test**: Run tests, select two inactive users, click Activate, verify their statuses change to Active

### Implementation for User Story 3

- [x] T021 [US3] Create `sample/Frank.Datastar.Tests/BulkUpdateTests.fs` with test fixture inheriting from TestBase
- [x] T022 [US3] Implement test `BulkActivateChangesSelectedUserStatuses`: Navigate to users, select two inactive users, click "Activate Selected", wait for updates via SSE, assert selected users show Active
- [x] T023 [US3] Implement test `BulkActivateDoesNotAffectUnselectedUsers`: After bulk activate, verify unselected users' statuses unchanged
- [x] T024 [US3] Implement test `BulkChangesPersistedAfterRefresh`: After bulk activate, refresh page, verify activated users still show Active
- [x] T025 [US3] Implement test `EmptySelectionDoesNothing`: Click bulk action with no users selected, verify no errors and no status changes
- [x] T026 [US3] Add test cleanup in BulkUpdateTests to restore user statuses to initial state after each test

**Checkpoint**: Bulk update validation complete - can detect bugs where bulk operations don't modify data

---

## Phase 7: User Story 4 - State Isolation (Priority: P2)

**Goal**: Verify that registration form data does not leak into contact click-to-edit

**Independent Test**: Run tests, enter data in registration form, verify contact display is unaffected

### Implementation for User Story 4

- [x] T027 [US4] Create `sample/Frank.Datastar.Tests/StateIsolationTests.fs` with test fixture inheriting from TestBase
- [x] T028 [US4] Implement test `RegistrationDataDoesNotAffectContactDisplay`: Enter "test@isolation.com" in registration email, verify contact display does not show that email
- [x] T029 [US4] Implement test `ContactDataPreservedAfterRegistrationInteraction`: Verify contact shows "joe@smith.org", interact with registration form using different values, verify contact still shows "joe@smith.org"

**Checkpoint**: State isolation validation complete - can detect state leakage between features

---

## Phase 8: User Story 6 - Clear Test Results and Failure Reporting (Priority: P2)

**Goal**: Ensure test output includes diagnostic information for debugging

**Independent Test**: Run tests with intentional failures, verify output explains what was expected vs observed

**Note**: NUnit provides most of this automatically, but we ensure assertions are descriptive.

### Implementation for User Story 6

- [x] T030 [US6] Review all test assertions across all test files and ensure they include descriptive failure messages (e.g., `Assert.That(content, Is.EqualTo("Joe"), "Edit form should display current firstName value")`)
- [x] T031 [US6] Add sample name output at test fixture level using `TestContext.WriteLine` to print which sample is being tested
- [x] T032 [US6] Add timeout context to SSE wait failures - wrap WaitForFunctionAsync calls to include selector and expected content in timeout error messages

**Checkpoint**: Test output provides enough context for debugging without re-running manually

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and documentation

- [x] T033 Run all tests against Frank.Datastar.Basic: `DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/`
  - Result: 12 passed, 8 failed - tests correctly detect bugs in sample application (expected behavior)
- [x] T034 Run all tests against Frank.Datastar.Hox: `DATASTAR_SAMPLE=Frank.Datastar.Hox dotnet test sample/Frank.Datastar.Tests/`
  - Result: 6 passed, 14 failed - tests correctly detect bugs in sample application (expected behavior)
- [x] T035 Run all tests against Frank.Datastar.Oxpecker: `DATASTAR_SAMPLE=Frank.Datastar.Oxpecker dotnet test sample/Frank.Datastar.Tests/`
  - Result: 12 passed, 8 failed - tests correctly detect bugs in sample application (expected behavior)
- [x] T036 Run consistency check: Execute test suite 5 times consecutively against one sample, verify consistent results (no flaky failures)
  - Result: 5 consecutive runs against Oxpecker all showed 8 failed, 11 passed, 52s each - consistent, no flaky tests
- [x] T037 Verify `dotnet test` without DATASTAR_SAMPLE shows help message with available samples
- [x] T038 [P] Update `specs/005-fix-sample-tests/quickstart.md` with any corrections based on actual implementation
  - Added note about test failures being expected since samples have known bugs
- [x] T039 Verify timing: Run `time DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/` and confirm total execution time is under 60 seconds (SC-004)
  - Result: 39-52 seconds - well under 60 second limit

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Story 5 (Phase 3)**: Depends on Foundational - provides configuration for all other stories
- **User Stories 1-4 (Phases 4-7)**: All depend on User Story 5 completion (configuration must work)
- **User Story 6 (Phase 8)**: Depends on User Stories 1-4 (needs test assertions to enhance)
- **Polish (Phase 9)**: Depends on all user stories being complete

### User Story Dependencies

```
Setup (P1) → Foundational (P2) → US5 Configuration (P3)
                                        ↓
                    ┌───────────────────┼───────────────────┐
                    ↓                   ↓                   ↓
            US1 Click-to-Edit    US2 Search Filter    US3 Bulk Update
                    │                   │                   │
                    └───────────────────┼───────────────────┘
                                        ↓
                                US4 State Isolation
                                        ↓
                                US6 Reporting
                                        ↓
                                    Polish
```

### Within Each User Story

- Test file creation before test methods
- Core tests before edge case tests
- Cleanup logic after main tests

### Parallel Opportunities

- T003 (runsettings) can run in parallel with T002 (fsproj)
- US1, US2, US3 can all run in parallel after US5 completes (different test files)
- T033, T034, T035 can run in parallel (different sample targets)
- T038 (documentation) can run in parallel with validation tasks

---

## Parallel Example: User Stories 1, 2, 3

After US5 (Configuration) completes:

```bash
# These can all be worked on simultaneously:
Task: "Create sample/Frank.Datastar.Tests/ClickToEditTests.fs" [US1]
Task: "Create sample/Frank.Datastar.Tests/SearchFilterTests.fs" [US2]
Task: "Create sample/Frank.Datastar.Tests/BulkUpdateTests.fs" [US3]
```

---

## Implementation Strategy

### MVP First (Configuration + Click-to-Edit)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational
3. Complete Phase 3: User Story 5 (Configuration)
4. Complete Phase 4: User Story 1 (Click-to-Edit)
5. **STOP and VALIDATE**: Run `DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test`
6. This delivers core SSE validation capability

### Incremental Delivery

1. Setup + Foundational + US5 → Can run tests against any sample
2. Add US1 (Click-to-Edit) → Test independently → Validates core Datastar pattern
3. Add US2 (Search) → Test independently → Validates list filtering
4. Add US3 (Bulk Update) → Test independently → Validates batch operations
5. Add US4 (Isolation) → Test independently → Validates state separation
6. Add US6 (Reporting) → Enhances debugging experience
7. Polish → Cross-sample validation

### Parallel Team Strategy

With multiple developers after Foundational:

- Developer A: User Story 1 (ClickToEditTests.fs)
- Developer B: User Story 2 (SearchFilterTests.fs)
- Developer C: User Story 3 (BulkUpdateTests.fs)

All stories complete independently, then integrate for US4 and US6.

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- This IS a test project, so the "tests" are the feature itself (not separate unit tests)
- Tests will likely fail on buggy samples - that's the expected behavior (detecting bugs)
