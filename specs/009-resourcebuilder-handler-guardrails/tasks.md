# Tasks: ResourceBuilder Handler Guardrails

**Input**: Design documents from `/specs/009-resourcebuilder-handler-guardrails/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md

**Tests**: This feature uses CLI-based test fixtures rather than traditional unit tests. Test fixtures are created alongside implementation.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [ ] T001 Create src/Frank.Analyzers/ directory structure
- [ ] T002 Create src/Frank.Analyzers/Frank.Analyzers.fsproj with FSharp.Analyzers.SDK 0.35.* reference and multi-target net8.0;net9.0;net10.0
- [ ] T003 Create test/Frank.Analyzers.Tests/ directory structure with fixtures/ subdirectory
- [ ] T004 Create test/Frank.Analyzers.Tests/Frank.Analyzers.Tests.fsproj as minimal project to compile fixtures (reference Frank package)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core analyzer infrastructure that MUST be complete before user story implementation

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T005 Implement HttpMethod type in src/Frank.Analyzers/DuplicateHandlerAnalyzer.fs (DU with GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS, CONNECT, TRACE)
- [ ] T006 Implement httpMethodOperations set containing all 9 method identifier names in src/Frank.Analyzers/DuplicateHandlerAnalyzer.fs
- [ ] T007 Implement tryGetHttpMethodName function to extract method name from SynExpr.Ident in src/Frank.Analyzers/DuplicateHandlerAnalyzer.fs
- [ ] T008 Implement DuplicateHandlerWalker type inheriting SyntaxCollectorBase in src/Frank.Analyzers/DuplicateHandlerAnalyzer.fs
- [ ] T009 Implement WalkExpr override for SynExpr.ComputationExpr (context push/pop) in src/Frank.Analyzers/DuplicateHandlerAnalyzer.fs
- [ ] T010 Implement WalkExpr override for SynExpr.App (HTTP method detection) in src/Frank.Analyzers/DuplicateHandlerAnalyzer.fs
- [ ] T011 Implement createDuplicateDiagnostic function with FRANK001 code and warning severity in src/Frank.Analyzers/DuplicateHandlerAnalyzer.fs
- [ ] T012 Implement analyzeFile function that creates walker and runs ASTCollecting.walkAst in src/Frank.Analyzers/DuplicateHandlerAnalyzer.fs
- [ ] T013 [P] Add [<EditorAnalyzer>] attributed function wrapping analyzeFile in src/Frank.Analyzers/DuplicateHandlerAnalyzer.fs
- [ ] T014 [P] Add [<CliAnalyzer>] attributed function wrapping analyzeFile in src/Frank.Analyzers/DuplicateHandlerAnalyzer.fs
- [ ] T015 Build and verify src/Frank.Analyzers compiles without errors

**Checkpoint**: Analyzer infrastructure ready - user story fixtures can now be created

---

## Phase 3: User Story 1 - Compile-Time Duplicate Handler Detection (Priority: P1) 🎯 MVP

**Goal**: Detect duplicate HTTP method handlers (e.g., two `get` calls) within a single resource CE

**Independent Test**: Run CLI analyzer against DuplicateGet.fs fixture, verify FRANK001 warning produced

### Test Fixtures for User Story 1

- [ ] T016 [P] [US1] Create test/Frank.Analyzers.Tests/fixtures/DuplicateGet.fs with resource containing two get handlers
- [ ] T017 [P] [US1] Create test/Frank.Analyzers.Tests/fixtures/ValidSingleHandlers.fs with resource containing one get and one post handler (no warnings expected)
- [ ] T018 [P] [US1] Create test/Frank.Analyzers.Tests/fixtures/MultipleResources.fs with two separate resources each with one get handler (no warnings expected)

### Test Script for User Story 1

- [ ] T019 [US1] Create test/Frank.Analyzers.Tests/run-analyzer-tests.sh with build and test execution logic
- [ ] T020 [US1] Add test case in run-analyzer-tests.sh for DuplicateGet.fs expecting FRANK001 warning with GET
- [ ] T021 [US1] Add test case in run-analyzer-tests.sh for ValidSingleHandlers.fs expecting no warnings
- [ ] T022 [US1] Add test case in run-analyzer-tests.sh for MultipleResources.fs expecting no warnings
- [ ] T023 [US1] Run run-analyzer-tests.sh and verify all User Story 1 tests pass

**Checkpoint**: Core duplicate detection works for GET method - MVP functional

---

## Phase 4: User Story 2 - Clear Diagnostic Messages (Priority: P2)

**Goal**: Diagnostic messages identify the HTTP method and line number of first occurrence

**Independent Test**: Verify FRANK001 message format includes method name (GET) and line number

### Verification for User Story 2

- [ ] T024 [US2] Update test/Frank.Analyzers.Tests/run-analyzer-tests.sh to verify diagnostic message format includes method name
- [ ] T025 [US2] Update test/Frank.Analyzers.Tests/run-analyzer-tests.sh to verify diagnostic message includes line number reference
- [ ] T026 [US2] Run run-analyzer-tests.sh and verify message format tests pass

**Checkpoint**: Diagnostic messages are clear and actionable

---

## Phase 5: User Story 3 - All HTTP Methods Covered (Priority: P2)

**Goal**: Analyzer detects duplicates for all 9 HTTP methods (GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS, CONNECT, TRACE)

**Independent Test**: Run analyzer against fixtures with duplicates for each method, verify all detected

### Test Fixtures for User Story 3

- [ ] T027 [P] [US3] Create test/Frank.Analyzers.Tests/fixtures/DuplicatePost.fs with resource containing two post handlers
- [ ] T028 [P] [US3] Create test/Frank.Analyzers.Tests/fixtures/DuplicatePut.fs with resource containing two put handlers
- [ ] T029 [P] [US3] Create test/Frank.Analyzers.Tests/fixtures/DuplicateDelete.fs with resource containing two delete handlers
- [ ] T030 [P] [US3] Create test/Frank.Analyzers.Tests/fixtures/DuplicatePatch.fs with resource containing two patch handlers
- [ ] T031 [P] [US3] Create test/Frank.Analyzers.Tests/fixtures/DuplicateHead.fs with resource containing two head handlers
- [ ] T032 [P] [US3] Create test/Frank.Analyzers.Tests/fixtures/DuplicateOptions.fs with resource containing two options handlers
- [ ] T033 [P] [US3] Create test/Frank.Analyzers.Tests/fixtures/DuplicateConnect.fs with resource containing two connect handlers
- [ ] T034 [P] [US3] Create test/Frank.Analyzers.Tests/fixtures/DuplicateTrace.fs with resource containing two trace handlers
- [ ] T035 [P] [US3] Create test/Frank.Analyzers.Tests/fixtures/AllMethodsOnce.fs with resource containing one of each of the 9 methods (no warnings expected)

### Test Script Updates for User Story 3

- [ ] T036 [US3] Add test cases in run-analyzer-tests.sh for all DuplicateXxx.fs fixtures expecting FRANK001 with respective method
- [ ] T037 [US3] Add test case in run-analyzer-tests.sh for AllMethodsOnce.fs expecting no warnings
- [ ] T038 [US3] Run run-analyzer-tests.sh and verify all User Story 3 tests pass

**Checkpoint**: All 9 HTTP methods are covered with 100% consistency

---

## Phase 6: User Story 4 - Datastar Extension Compatibility (Priority: P3)

**Goal**: Detect conflicts between datastar operation and explicit HTTP method handlers

**Independent Test**: Run analyzer against fixture with datastar + get, verify duplicate detected

### Implementation for User Story 4

- [ ] T039 [US4] Add "datastar" to operation detection in tryGetHttpMethodName in src/Frank.Analyzers/DuplicateHandlerAnalyzer.fs
- [ ] T040 [US4] Implement datastar HTTP method resolution (default GET, or explicit method from first argument) in src/Frank.Analyzers/DuplicateHandlerAnalyzer.fs

### Test Fixtures for User Story 4

- [ ] T041 [P] [US4] Create test/Frank.Analyzers.Tests/fixtures/DatastarConflict.fs with resource containing datastar and get handler (should warn)
- [ ] T042 [P] [US4] Create test/Frank.Analyzers.Tests/fixtures/DatastarWithPost.fs with resource containing datastar HttpMethods.Post and post handler (should warn)
- [ ] T043 [P] [US4] Create test/Frank.Analyzers.Tests/fixtures/DatastarNoConflict.fs with resource containing datastar and post handler (no warning - different methods)

### Test Script Updates for User Story 4

- [ ] T044 [US4] Add test case in run-analyzer-tests.sh for DatastarConflict.fs expecting FRANK001 with GET
- [ ] T045 [US4] Add test case in run-analyzer-tests.sh for DatastarWithPost.fs expecting FRANK001 with POST
- [ ] T046 [US4] Add test case in run-analyzer-tests.sh for DatastarNoConflict.fs expecting no warnings
- [ ] T047 [US4] Run run-analyzer-tests.sh and verify all User Story 4 tests pass

**Checkpoint**: Datastar extension compatibility complete

---

## Phase 7: User Story 5 - CLI and CI/CD Support (Priority: P2)

**Goal**: Analyzer works via fsharp-analyzers CLI tool for CI/CD integration

**Independent Test**: Run fsharp-analyzers CLI against project and verify warnings output correctly

### Implementation for User Story 5

- [ ] T048 [US5] Verify [<CliAnalyzer>] registration works with fsharp-analyzers tool by running against test fixtures
- [ ] T049 [US5] Document CLI usage in specs/009-resourcebuilder-handler-guardrails/quickstart.md (verify existing content is accurate)
- [ ] T050 [US5] Verify exit codes from fsharp-analyzers tool (non-zero when warnings present, if supported)

### CI Integration for User Story 5

- [ ] T051 [US5] Ensure run-analyzer-tests.sh returns non-zero exit code on test failure
- [ ] T052 [US5] Run full test suite via run-analyzer-tests.sh to validate CI readiness

**Checkpoint**: CLI and CI/CD support verified and documented

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Final cleanup and validation

- [ ] T053 [P] Add XML documentation comments to public analyzer functions in src/Frank.Analyzers/DuplicateHandlerAnalyzer.fs
- [ ] T054 [P] Update specs/009-resourcebuilder-handler-guardrails/quickstart.md with final usage examples
- [ ] T055 Run complete test suite and verify all fixtures pass
- [ ] T056 Build analyzer in Release mode and verify NuGet package metadata
- [ ] T057 Manual verification: Load analyzer in IDE (Ionide/VS/Rider) and confirm warnings display correctly

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3-7)**: All depend on Foundational phase completion
  - US1 (P1): Core detection - complete first as MVP
  - US2 (P2): Message format - can parallel with US3
  - US3 (P2): All methods - can parallel with US2
  - US4 (P3): Datastar - after core detection (US1)
  - US5 (P2): CLI/CI - can parallel with US3/US4
- **Polish (Phase 8)**: Depends on all user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (P2)**: Can start after US1 (verifies message format from US1 implementation)
- **User Story 3 (P2)**: Can start after US1 (extends same detection logic)
- **User Story 4 (P3)**: Can start after US1 (extends detection to include datastar)
- **User Story 5 (P2)**: Can start after US1 (tests CLI output which requires working analyzer)

### Within Each User Story

- Fixtures can be created in parallel [P]
- Test script updates after fixtures
- Run tests to verify

### Parallel Opportunities

- All Setup tasks can run sequentially (small number)
- Foundational tasks T005-T012 are sequential (building on each other)
- T013 and T014 (editor/CLI analyzer registration) can run in parallel
- All fixtures within a story marked [P] can run in parallel
- US2, US3, US5 can run in parallel after US1 completes

---

## Parallel Example: User Story 3

```bash
# Launch all US3 fixtures together:
Task: "Create test/Frank.Analyzers.Tests/fixtures/DuplicatePost.fs"
Task: "Create test/Frank.Analyzers.Tests/fixtures/DuplicatePut.fs"
Task: "Create test/Frank.Analyzers.Tests/fixtures/DuplicateDelete.fs"
Task: "Create test/Frank.Analyzers.Tests/fixtures/DuplicatePatch.fs"
Task: "Create test/Frank.Analyzers.Tests/fixtures/DuplicateHead.fs"
Task: "Create test/Frank.Analyzers.Tests/fixtures/DuplicateOptions.fs"
Task: "Create test/Frank.Analyzers.Tests/fixtures/DuplicateConnect.fs"
Task: "Create test/Frank.Analyzers.Tests/fixtures/DuplicateTrace.fs"
Task: "Create test/Frank.Analyzers.Tests/fixtures/AllMethodsOnce.fs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - builds core analyzer)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Run test script, verify GET duplicate detection works
5. This delivers the core value proposition

### Incremental Delivery

1. Complete Setup + Foundational → Analyzer compiles
2. Add User Story 1 → Test independently → Core detection works (MVP!)
3. Add User Story 2 → Test independently → Message format verified
4. Add User Story 3 → Test independently → All 9 methods covered
5. Add User Story 4 → Test independently → Datastar compatibility
6. Add User Story 5 → Test independently → CI/CD ready
7. Each story adds value without breaking previous stories

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Test fixtures are created alongside implementation (CLI-based testing approach)
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- The analyzer has no runtime dependencies - it's a development-time tool only
