# Tasks: Enhanced Sample Test Validation

**Input**: Design documents from `/specs/005-fix-sample-tests/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, quickstart.md

**Tests**: Not applicable - this feature IS the test enhancement. No separate test tasks needed.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Files modified:
- `sample/Frank.Datastar.Basic/test.sh`
- `sample/Frank.Datastar.Hox/test.sh`
- `sample/Frank.Datastar.Oxpecker/test.sh`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create reusable test helpers and establish test script structure

- [ ] T001 Create test helper functions (assert, check_server, counters) in sample/Frank.Datastar.Basic/test.sh
- [ ] T002 Add structured output format with PASS/FAIL markers and summary in sample/Frank.Datastar.Basic/test.sh
- [ ] T003 Add non-zero exit status on test failures in sample/Frank.Datastar.Basic/test.sh

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Server availability and seed data verification that MUST pass before any feature tests

**Note**: Foundational phase tasks are implemented in Basic first, then copied to other samples in Phase 9.

- [ ] T004 Add server availability check with clear error message in sample/Frank.Datastar.Basic/test.sh
- [ ] T005 Add seed data verification (contact ID 1 exists, fruits list populated, users 1-4 exist) in sample/Frank.Datastar.Basic/test.sh

**Checkpoint**: Basic test infrastructure ready - user story implementation can now begin

---

## Phase 3: User Story 1 - Click-to-Edit Validation (Priority: P1) MVP

**Goal**: Verify click-to-edit displays current values and persists updates

**Independent Test**: Run test.sh and verify click-to-edit section shows PASS for all scenarios

### Implementation for User Story 1

- [ ] T006 [US1] Add test: GET /contacts/1 returns view with seed data values (Joe, Smith, joe@smith.org) in sample/Frank.Datastar.Basic/test.sh
- [ ] T007 [US1] Add test: GET /contacts/1/edit returns form with current values in data-signals attribute in sample/Frank.Datastar.Basic/test.sh
- [ ] T008 [US1] Add test: PUT /contacts/1 with new values, then GET /contacts/1 shows updated values in sample/Frank.Datastar.Basic/test.sh
- [ ] T009 [US1] Add test: After edit, subsequent view shows updated firstName/lastName/email in sample/Frank.Datastar.Basic/test.sh

**Checkpoint**: Click-to-edit validation complete and independently testable

---

## Phase 4: User Story 2 - Search Filtering Validation (Priority: P1)

**Goal**: Verify search returns matching results (not empty list)

**Independent Test**: Run test.sh and verify search section shows PASS for filter and clear scenarios

### Implementation for User Story 2

- [ ] T010 [US2] Add test: GET /fruits returns full list with all seed fruits (Apple, Banana, etc.) in sample/Frank.Datastar.Basic/test.sh
- [ ] T011 [US2] Add test: GET /fruits?q=ap returns Apple and Apricot (verify content, not just status) in sample/Frank.Datastar.Basic/test.sh
- [ ] T012 [US2] Add test: GET /fruits?q=ap does NOT return Banana in sample/Frank.Datastar.Basic/test.sh
- [ ] T013 [US2] Add test: GET /fruits?q=xyz returns empty results (not error) in sample/Frank.Datastar.Basic/test.sh

**Checkpoint**: Search validation complete and independently testable

---

## Phase 5: User Story 3 - Bulk Update Validation (Priority: P1)

**Goal**: Verify bulk operations actually modify selected users' status

**Independent Test**: Run test.sh and verify bulk update section shows PASS for status changes

### Implementation for User Story 3

- [ ] T014 [US3] Add test: GET /users returns table with initial statuses (User 1 Active, User 2 Inactive, etc.) in sample/Frank.Datastar.Basic/test.sh
- [ ] T015 [US3] Add test: PUT /users/bulk?status=active with selections [false,true,false,true] then verify User 2 and 4 show Active in sample/Frank.Datastar.Basic/test.sh
- [ ] T016 [US3] Add test: PUT /users/bulk?status=inactive with selections [true,false,true,false] then verify User 1 and 3 show Inactive in sample/Frank.Datastar.Basic/test.sh
- [ ] T017 [US3] Add test: Verify non-selected users retain their previous status after bulk update in sample/Frank.Datastar.Basic/test.sh

**Checkpoint**: Bulk update validation complete and independently testable

---

## Phase 6: User Story 4 - State Isolation Validation (Priority: P2)

**Goal**: Verify registration form does not affect contact data

**Independent Test**: Run test.sh and verify state isolation section shows PASS

### Implementation for User Story 4

- [ ] T018 [US4] Add test: POST /registrations/validate with test values, then GET /contacts/1 still shows original contact data in sample/Frank.Datastar.Basic/test.sh
- [ ] T019 [US4] Add test: POST /registrations with new email, then GET /contacts/1/edit shows original contact email (not registration email) in sample/Frank.Datastar.Basic/test.sh

**Checkpoint**: State isolation validation complete and independently testable

---

## Phase 7: User Story 5 - Sample-Specific Reporting (Priority: P2)

**Goal**: Clear per-sample identification in test output

**Independent Test**: Run test.sh for any sample and verify output clearly shows which sample was tested

### Implementation for User Story 5

- [ ] T020 [US5] Add sample name identification banner at start of test output (e.g., "Frank.Datastar.Basic Tests") in sample/Frank.Datastar.Basic/test.sh
- [ ] T021 [US5] Add final summary with pass/fail counts at end of test output in sample/Frank.Datastar.Basic/test.sh

**Checkpoint**: Per-sample reporting complete

---

## Phase 8: User Story 6 - Async Timing Handling (Priority: P3)

**Goal**: Tests wait appropriately for SSE content

**Independent Test**: Run test.sh 5 times consecutively and verify consistent results

### Implementation for User Story 6

- [ ] T022 [US6] Add appropriate curl timeout (-m 2) for all SSE endpoint tests in sample/Frank.Datastar.Basic/test.sh
- [ ] T023 [US6] Add brief sleep (0.5s) before verification reads after fire-and-forget requests in sample/Frank.Datastar.Basic/test.sh

**Checkpoint**: Timing handling complete - tests should be consistent

---

## Phase 9: Copy to Other Samples

**Purpose**: Apply the same test structure to Hox and Oxpecker samples

- [ ] T024 [P] Copy enhanced test.sh from Basic to sample/Frank.Datastar.Hox/test.sh with sample name updated
- [ ] T025 [P] Copy enhanced test.sh from Basic to sample/Frank.Datastar.Oxpecker/test.sh with sample name updated
- [ ] T026 Verify sample/Frank.Datastar.Hox/test.sh runs against Hox server (may show failures - that's expected)
- [ ] T027 Verify sample/Frank.Datastar.Oxpecker/test.sh runs against Oxpecker server (may show failures - that's expected)

**Checkpoint**: All three samples have enhanced test scripts

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and documentation

- [ ] T028 Run sample/Frank.Datastar.Basic/test.sh against running Basic server and document results
- [ ] T029 Run sample/Frank.Datastar.Hox/test.sh against running Hox server and document results
- [ ] T030 Run sample/Frank.Datastar.Oxpecker/test.sh against running Oxpecker server and document results
- [ ] T031 Verify test exit codes: 0 if all pass, non-zero if any fail

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion
- **User Stories (Phases 3-8)**: All depend on Foundational phase completion
  - User stories are sequential (tests build on state from previous tests)
- **Copy to Other Samples (Phase 9)**: Depends on all user story phases complete for Basic
- **Polish (Phase 10)**: Depends on all samples having test scripts

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational - No dependencies on other stories
- **User Story 2 (P1)**: Can start after Foundational - Independent of US1
- **User Story 3 (P1)**: Can start after Foundational - Independent of US1/US2
- **User Story 4 (P2)**: Can start after Foundational - Independent
- **User Story 5 (P2)**: Can start after Foundational - Framework for output
- **User Story 6 (P3)**: Should be applied throughout, but formalized last

### Within Each User Story

- Add test cases for acceptance scenarios
- Tests are self-validating (PASS/FAIL output)
- Story complete when all acceptance scenarios have tests

### Parallel Opportunities

- T024 and T025 can run in parallel (copy to Hox and Oxpecker)
- Within Basic, user story tests can technically be written in any order
- However, execution order matters (tests are sequential/stateful)

---

## Parallel Example: Phase 9

```bash
# Launch copies to other samples together:
Task: "Copy enhanced test.sh from Basic to sample/Frank.Datastar.Hox/test.sh"
Task: "Copy enhanced test.sh from Basic to sample/Frank.Datastar.Oxpecker/test.sh"
```

---

## Implementation Strategy

### MVP First (User Stories 1-3 Only)

1. Complete Phase 1: Setup (helper functions)
2. Complete Phase 2: Foundational (server check, seed data)
3. Complete Phase 3: User Story 1 (click-to-edit) - Core bug detection
4. Complete Phase 4: User Story 2 (search) - Core bug detection
5. Complete Phase 5: User Story 3 (bulk update) - Core bug detection
6. **STOP and VALIDATE**: Run test.sh against Basic - should detect known bugs
7. Deploy/demo if ready

### Incremental Delivery

1. Complete Setup + Foundational → Basic infrastructure ready
2. Add User Story 1 → Click-to-edit bugs detected
3. Add User Story 2 → Search bugs detected
4. Add User Story 3 → Bulk update bugs detected
5. Add User Story 4-6 → State isolation, reporting, timing
6. Copy to Hox/Oxpecker → All samples testable
7. Each story adds detection capability

### Single Developer Strategy

Execute phases sequentially:
1. Complete all tasks in sample/Frank.Datastar.Basic/test.sh first
2. Copy to Hox and Oxpecker
3. Run all three and document which tests fail on which sample

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Tests are sequential within a sample (state builds up)
- Each user story adds specific bug detection capability
- Commit after each phase or logical group
- Expected: Tests will FAIL on samples with bugs (that's the point!)
- Avoid: Breaking test sequentiality, modifying sample application code
