# Tasks: Fix Frank.Datastar.Basic Sample Tests

**Input**: Design documents from `/specs/006-fix-datastar-basic-tests/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md

**Tests**: Existing Playwright tests will validate the fix. No new tests needed.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing. However, the foundational change (single SSE channel) fixes all user stories simultaneously.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

## Path Conventions

- **Sample project**: `sample/Frank.Datastar.Basic/`
- **Test project**: `sample/Frank.Datastar.Tests/`

---

## Phase 1: Setup (Verification)

**Purpose**: Verify current state and establish baseline

- [X] T001 Build sample project in sample/Frank.Datastar.Basic/
- [X] T002 Build test project in sample/Frank.Datastar.Tests/
- [X] T003 Run tests to confirm current state (12 pass, 8 fail) with `DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/`

---

## Phase 2: Foundational (Core SSE Channel Refactor)

**Purpose**: Consolidate 5 SSE channels into 1 global channel - this is the core fix that enables all user stories

**⚠️ CRITICAL**: This phase fixes the root cause for ALL failing tests

### Channel Consolidation

- [X] T004 Replace 5 individual channel declarations with 1 global channel in sample/Frank.Datastar.Basic/Program.fs (delete `contactChannel`, `fruitsChannel`, `itemsChannel`, `usersChannel`, `registrationChannel`; add `globalChannel`)
- [X] T005 Create single SSE endpoint `GET /sse` that establishes the page-wide SSE connection in sample/Frank.Datastar.Basic/Program.fs

### Endpoint Updates - Contact Resource

- [X] T006 [P] Update `GET /contacts/{id}` to broadcast initial view through globalChannel instead of establishing SSE in sample/Frank.Datastar.Basic/Program.fs
- [X] T007 [P] Update `GET /contacts/{id}/edit` to broadcast edit form through globalChannel in sample/Frank.Datastar.Basic/Program.fs
- [X] T008 [P] Update `PUT /contacts/{id}` to broadcast updated view through globalChannel in sample/Frank.Datastar.Basic/Program.fs

### Endpoint Updates - Fruits Resource

- [X] T009 [P] Update `GET /fruits` to broadcast fruit list through globalChannel (both initial load and search results) in sample/Frank.Datastar.Basic/Program.fs

### Endpoint Updates - Items Resource

- [X] T010 [P] Update `GET /items` to broadcast items table through globalChannel in sample/Frank.Datastar.Basic/Program.fs
- [X] T011 [P] Update `DELETE /items/{id}` to broadcast removeElement through globalChannel in sample/Frank.Datastar.Basic/Program.fs

### Endpoint Updates - Users Resource

- [X] T012 [P] Update `GET /users` to broadcast users table through globalChannel in sample/Frank.Datastar.Basic/Program.fs
- [X] T013 [P] Update `PUT /users/bulk` to broadcast updated table through globalChannel in sample/Frank.Datastar.Basic/Program.fs

### Endpoint Updates - Registration Resource

- [X] T014 [P] Update `GET /registrations/form` to broadcast registration form through globalChannel in sample/Frank.Datastar.Basic/Program.fs
- [X] T015 [P] Update `POST /registrations/validate` to broadcast validation feedback through globalChannel in sample/Frank.Datastar.Basic/Program.fs
- [X] T016 [P] Update `POST /registrations` to broadcast result through globalChannel in sample/Frank.Datastar.Basic/Program.fs

### HTML Updates

- [X] T017 Update index.html to establish single SSE connection on page load in sample/Frank.Datastar.Basic/wwwroot/index.html
- [X] T018 Update "Load X" buttons in index.html to trigger fire-and-forget GET requests instead of establishing SSE connections in sample/Frank.Datastar.Basic/wwwroot/index.html

**Checkpoint**: Foundation ready - SSE channel consolidated, all endpoints use globalChannel

---

## Phase 3: User Story 1 - Bulk User Status Update (Priority: P1) 🎯 MVP

**Goal**: Bulk activate/deactivate updates user statuses and reflects changes via SSE

**Independent Test**: Run `DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/ --filter "FullyQualifiedName~BulkUpdateTests"`

### Verification for User Story 1

- [X] T019 [US1] Verify bulk update tests pass (4 tests: activate, deactivate, persist, empty selection, unselected unchanged) by running BulkUpdateTests

**Checkpoint**: User Story 1 complete - 4 previously failing bulk update tests now pass

---

## Phase 4: User Story 2 - Search Filter Updates (Priority: P2)

**Goal**: Search filtering updates fruits list via SSE in real-time

**Independent Test**: Run `DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/ --filter "FullyQualifiedName~SearchFilterTests"`

### Verification for User Story 2

- [X] T020 [US2] Verify search filter tests pass (2 failing + 2 passing = 4 tests) by running SearchFilterTests

**Checkpoint**: User Story 2 complete - 2 previously failing search tests now pass

---

## Phase 5: User Story 3 & 4 - Contact Edit Operations (Priority: P3)

**Goal**: Cancel returns to view mode; edits persist after refresh

**Independent Test**: Run `DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/ --filter "FullyQualifiedName~ClickToEditTests"`

### Verification for User Stories 3 & 4

- [X] T021 [US3] [US4] Verify click-to-edit tests pass (2 failing + 2 passing = 4 tests) by running ClickToEditTests

**Checkpoint**: User Stories 3 & 4 complete - 2 previously failing contact tests now pass

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and cleanup

- [X] T022 Run full test suite to verify all 20 tests pass with `DATASTAR_SAMPLE=Frank.Datastar.Basic dotnet test sample/Frank.Datastar.Tests/`
- [X] T023 Verify no regressions in StateIsolationTests (3 tests) and ConfigurationTests (6 tests)
- [X] T024 Clean up any dead code (unused channel declarations, commented code)
- [ ] T025 Verify sample runs correctly in browser with headed mode: `DATASTAR_SAMPLE=Frank.Datastar.Basic HEADED=1 dotnet test sample/Frank.Datastar.Tests/`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - verify current state
- **Foundational (Phase 2)**: Depends on Setup - implements the core fix
- **User Stories (Phases 3-5)**: Depend on Foundational - verify tests pass
- **Polish (Phase 6)**: Depends on all user story verifications passing

### Task Dependencies within Phase 2

```
T004 (create globalChannel)
  └─→ T005 (create SSE endpoint)
        └─→ T006-T016 (all endpoint updates - can run in parallel)
              └─→ T017-T018 (HTML updates)
```

### Parallel Opportunities

**Phase 2 Endpoint Updates (T006-T016)**: All can run in parallel since they modify different resource handlers in the same file. However, since they're all in Program.fs, coordination is needed.

**Recommended approach**: Execute T006-T016 as a single logical change to Program.fs, updating all endpoints in one pass.

---

## Parallel Example: Phase 2 Endpoint Updates

Since all endpoint updates are in the same file (Program.fs), they should be done together:

```bash
# Execute as single coherent change:
# 1. Delete 5 channel declarations
# 2. Add globalChannel
# 3. Add GET /sse endpoint
# 4. Update all resource endpoints to use globalChannel
# 5. Update HTML to connect on load
```

---

## Implementation Strategy

### MVP First (Complete Phase 2)

1. Complete Phase 1: Verify current failing state
2. Complete Phase 2: Core SSE channel refactor
3. Complete Phase 3: Verify US1 (bulk update) tests pass
4. **STOP and VALIDATE**: If bulk update works, the core fix is correct
5. Proceed to verify remaining user stories

### Key Insight

The foundational change (single SSE channel) fixes ALL 8 failing tests simultaneously. The user story phases are primarily verification phases to confirm the fix works for each test category.

### Incremental Validation

1. Setup → Confirm 12 pass, 8 fail
2. Foundational → Core channel refactor
3. US1 Verify → Confirm bulk update tests pass (4 tests)
4. US2 Verify → Confirm search filter tests pass (2 tests)
5. US3/US4 Verify → Confirm contact tests pass (2 tests)
6. Polish → Confirm all 20 tests pass, no regressions

---

## Notes

- [P] tasks = different resources, can conceptually run in parallel
- [Story] label maps verification task to specific user story
- Phase 2 is the core implementation; Phases 3-5 are verification
- The fix is architectural (channel consolidation), not per-feature fixes
- All 8 failing tests share the same root cause and fix
- Existing tests are the acceptance criteria - no new tests needed
