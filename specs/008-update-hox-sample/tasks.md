# Tasks: Update Frank.Datastar.Hox Sample

**Input**: Design documents from `/specs/008-update-hox-sample/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api.md

**Tests**: Tests are NOT generated (existing Playwright tests in Frank.Datastar.Tests will be used for validation)

**Organization**: Tasks organized to first establish foundational infrastructure (SSE architecture), then implement each pattern (Click-to-Edit, Search, Delete, Bulk Update, Form Validation) with its Hox render functions and resources.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different code sections, no dependencies)
- **[Story]**: Which user story this task belongs to (US3-US7)
- Foundational tasks (US1, US2) have no story label as they're prerequisites

## Path Conventions

- **Single file**: `sample/Frank.Datastar.Hox/Program.fs`
- **Static assets**: `sample/Frank.Datastar.Hox/wwwroot/index.html`
- **Reference**: `sample/Frank.Datastar.Basic/Program.fs` (read-only)

---

## Phase 1: Setup

**Purpose**: Prepare project structure and copy static assets

- [ ] T001 Copy wwwroot/index.html from Frank.Datastar.Basic to Frank.Datastar.Hox in sample/Frank.Datastar.Hox/wwwroot/index.html
- [ ] T002 Verify Frank.Datastar.Hox.fsproj has correct dependencies (Frank, Hox, Frank.Datastar project reference) in sample/Frank.Datastar.Hox/Frank.Datastar.Hox.fsproj

**Checkpoint**: Project structure ready, static assets in place

---

## Phase 2: Foundational (US1 + US2 - Blocking Prerequisites)

**Purpose**: Implement the core SSE broadcast infrastructure that ALL patterns depend on

**⚠️ CRITICAL**: No pattern implementation (US3-US7) can begin until this phase is complete

**Goal**: Replace MailboxProcessor-based SSE with single-channel broadcast pattern from Basic

**Independent Test**: Application builds and serves index.html; SSE endpoint accepts connections

### Module Header and Imports

- [ ] T003 Write module header with imports (System, Threading, Channels, ASP.NET Core, Frank, Hox) in sample/Frank.Datastar.Hox/Program.fs

### Type Definitions (from data-model.md)

- [ ] T004 [P] Define Contact record type and ContactSignals CLIMutable type in sample/Frank.Datastar.Hox/Program.fs
- [ ] T005 [P] Define UserStatus DU, User record type, and BulkUpdateSignals CLIMutable type in sample/Frank.Datastar.Hox/Program.fs
- [ ] T006 [P] Define Item record type in sample/Frank.Datastar.Hox/Program.fs
- [ ] T007 [P] Define Registration record type and RegistrationSignals CLIMutable type in sample/Frank.Datastar.Hox/Program.fs

### SSE Broadcast Infrastructure (matches Basic exactly)

- [ ] T008 Define SseEvent discriminated union (PatchElements, RemoveElement, PatchSignals) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T009 Implement SseEvent module with subscribe, unsubscribe, broadcast, writeSseEvent functions using Channel<SseEvent> in sample/Frank.Datastar.Hox/Program.fs

### In-Memory Data Stores (matches Basic exactly)

- [ ] T010 [P] Initialize contacts Dictionary with default contact (Joe Smith) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T011 [P] Initialize fruits static list (22 fruits) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T012 [P] Initialize items ResizeArray with 4 default items in sample/Frank.Datastar.Hox/Program.fs
- [ ] T013 [P] Initialize users Dictionary with 4 default users (mixed statuses) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T014 [P] Initialize registrations ResizeArray (empty) and nextRegistrationId mutable in sample/Frank.Datastar.Hox/Program.fs

### SSE Resource and Application Entry Point

- [ ] T015 Implement sseResource with datastar builder for /sse endpoint in sample/Frank.Datastar.Hox/Program.fs
- [ ] T016 Implement main entry point with webHost builder (UseDefaults, UseDefaultFiles, UseStaticFiles) in sample/Frank.Datastar.Hox/Program.fs

**Checkpoint**: Application builds, runs, serves index.html, and accepts SSE connections at /sse

---

## Phase 3: User Story 3 - Click-to-Edit Pattern (Priority: P2)

**Goal**: Implement contact view/edit with Hox-rendered HTML

**Independent Test**: Load contact, click Edit, modify fields, Save/Cancel - verify state transitions

### Hox Render Functions

- [ ] T017 [P] [US3] Implement renderContactView function using Hox h() returning ValueTask<string> in sample/Frank.Datastar.Hox/Program.fs
- [ ] T018 [P] [US3] Implement renderContactEdit function using Hox h() with data-signals and data-bind attributes in sample/Frank.Datastar.Hox/Program.fs

### Resources

- [ ] T019 [US3] Implement contactResource for /contacts/{id} with GET and PUT handlers in sample/Frank.Datastar.Hox/Program.fs
- [ ] T020 [US3] Implement contactEditResource for /contacts/{id}/edit with GET handler in sample/Frank.Datastar.Hox/Program.fs
- [ ] T021 [US3] Register contactResource and contactEditResource in webHost builder in sample/Frank.Datastar.Hox/Program.fs

**Checkpoint**: Click-to-Edit pattern works - Edit/Save/Cancel all function correctly

---

## Phase 4: User Story 4 - Search with Filtering Pattern (Priority: P2)

**Goal**: Implement searchable fruits list with Hox-rendered HTML

**Independent Test**: Load fruits, type search term, verify filtered results appear

### Hox Render Function

- [ ] T022 [P] [US4] Implement renderFruitsList function using Hox h() and fragment for list items in sample/Frank.Datastar.Hox/Program.fs

### Resource

- [ ] T023 [US4] Implement fruitsResource for /fruits with GET handler (query param filtering) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T024 [US4] Register fruitsResource in webHost builder in sample/Frank.Datastar.Hox/Program.fs

**Checkpoint**: Search pattern works - typing filters results, clearing restores full list

---

## Phase 5: User Story 5 - Row Deletion Pattern (Priority: P3)

**Goal**: Implement deletable items table with Hox-rendered HTML

**Independent Test**: Load items, delete one, verify it disappears

### Hox Render Function

- [ ] T025 [P] [US5] Implement renderItemsTable function using Hox h() with delete button and confirm dialog in sample/Frank.Datastar.Hox/Program.fs

### Resources

- [ ] T026 [US5] Implement itemsResource for /items with GET handler in sample/Frank.Datastar.Hox/Program.fs
- [ ] T027 [US5] Implement itemResource for /items/{id} with DELETE handler (broadcasts RemoveElement) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T028 [US5] Register itemsResource and itemResource in webHost builder in sample/Frank.Datastar.Hox/Program.fs

**Checkpoint**: Delete pattern works - deleted items disappear without page refresh

---

## Phase 6: User Story 6 - Bulk Update Pattern (Priority: P3)

**Goal**: Implement bulk status updates with Hox-rendered HTML

**Independent Test**: Load users, select checkboxes, Activate/Deactivate, verify status changes

### Hox Render Function

- [ ] T029 [P] [US6] Implement renderUsersTable function using Hox h() with checkbox bindings and data-signals__ifmissing in sample/Frank.Datastar.Hox/Program.fs

### Resources

- [ ] T030 [US6] Implement usersResource for /users with GET handler in sample/Frank.Datastar.Hox/Program.fs
- [ ] T031 [US6] Implement usersBulkResource for /users/bulk with PUT handler (reads selections array) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T032 [US6] Register usersResource and usersBulkResource in webHost builder in sample/Frank.Datastar.Hox/Program.fs

**Checkpoint**: Bulk update pattern works - checkbox selections affect correct users

---

## Phase 7: User Story 7 - Form Validation Pattern (Priority: P3)

**Goal**: Implement registration form with real-time validation using Hox-rendered HTML

**Independent Test**: Load form, enter invalid data, see errors, fix and submit

### Hox Render Functions

- [ ] T033 [P] [US7] Implement validateRegistration function (returns error list) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T034 [P] [US7] Implement renderValidationFeedback function using Hox h() in sample/Frank.Datastar.Hox/Program.fs
- [ ] T035 [P] [US7] Implement renderRegistrationSuccess function using Hox h() in sample/Frank.Datastar.Hox/Program.fs
- [ ] T036 [P] [US7] Implement renderRegistrationForm function using Hox h() with debounced validation in sample/Frank.Datastar.Hox/Program.fs

### Resources

- [ ] T037 [US7] Implement registrationFormResource for /registrations/form with GET handler in sample/Frank.Datastar.Hox/Program.fs
- [ ] T038 [US7] Implement registrationValidateResource for /registrations/validate with POST handler in sample/Frank.Datastar.Hox/Program.fs
- [ ] T039 [US7] Implement registrationsResource for /registrations with POST handler (creates registration, checks duplicates) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T040 [US7] Register registrationFormResource, registrationValidateResource, registrationsResource in webHost builder in sample/Frank.Datastar.Hox/Program.fs

**Checkpoint**: Form validation works - real-time feedback, successful submission, duplicate detection

---

## Phase 8: Polish & Validation

**Purpose**: Final verification and cleanup

- [ ] T041 [P] Implement debugPingResource for /debug/ping with GET handler in sample/Frank.Datastar.Hox/Program.fs
- [ ] T042 Build sample and verify no compilation errors with `dotnet build sample/Frank.Datastar.Hox/`
- [ ] T043 Run Playwright tests with `DATASTAR_SAMPLE=Frank.Datastar.Hox dotnet test sample/Frank.Datastar.Tests/`
- [ ] T044 Verify all tests pass (Click-to-Edit, Search, Bulk Update, State Isolation)

**Checkpoint**: All Playwright tests pass - implementation complete

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup - BLOCKS all pattern implementations
- **US3-US7 (Phases 3-7)**: All depend on Foundational phase completion
  - Can proceed sequentially in priority order (P2 → P3)
  - Or in parallel if desired (each pattern is independent)
- **Polish (Phase 8)**: Depends on all patterns being complete

### User Story Dependencies

| Story | Phase | Priority | Depends On | Independent Test |
|-------|-------|----------|------------|------------------|
| US1+US2 | 2 | P1 | Setup | App builds, serves index.html, SSE works |
| US3 | 3 | P2 | Foundational | Click-to-Edit works |
| US4 | 4 | P2 | Foundational | Search works |
| US5 | 5 | P3 | Foundational | Delete works |
| US6 | 6 | P3 | Foundational | Bulk update works |
| US7 | 7 | P3 | Foundational | Form validation works |

### Within Each User Story

1. Render functions (can be parallel within story)
2. Resources (depend on render functions)
3. Register resources in webHost builder

### Parallel Opportunities

**Phase 2 (Foundational)**:
- T004-T007: Type definitions (different code sections)
- T010-T014: Data store initializations (different code sections)

**Each Pattern Phase**:
- Render functions within a story can be parallel
- Different stories can run in parallel after Foundational

---

## Parallel Example: Phase 2 Foundational

```text
# Launch type definitions in parallel:
Task: "T004 Define Contact record type and ContactSignals"
Task: "T005 Define UserStatus DU, User record type, BulkUpdateSignals"
Task: "T006 Define Item record type"
Task: "T007 Define Registration record type and RegistrationSignals"

# Launch data store initializations in parallel:
Task: "T010 Initialize contacts Dictionary"
Task: "T011 Initialize fruits static list"
Task: "T012 Initialize items ResizeArray"
Task: "T013 Initialize users Dictionary"
Task: "T014 Initialize registrations ResizeArray"
```

## Parallel Example: Phase 7 (US7)

```text
# Launch render functions in parallel:
Task: "T033 Implement validateRegistration function"
Task: "T034 Implement renderValidationFeedback function"
Task: "T035 Implement renderRegistrationSuccess function"
Task: "T036 Implement renderRegistrationForm function"
```

---

## Implementation Strategy

### MVP First (Foundational + US3 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (SSE infrastructure)
3. Complete Phase 3: User Story 3 (Click-to-Edit)
4. **STOP and VALIDATE**: Run ClickToEditTests
5. If passing, continue to remaining stories

### Incremental Delivery

1. Setup + Foundational → SSE works, app runs
2. Add US3 → Click-to-Edit tests pass
3. Add US4 → Search tests pass
4. Add US5 → Delete functionality works
5. Add US6 → Bulk Update tests pass
6. Add US7 → Form Validation works
7. Polish → All tests pass

### Recommended Order

Since all patterns are independent after Foundational, implement in priority order:
1. **P2**: US3 (Click-to-Edit), US4 (Search) - Most visible patterns
2. **P3**: US5 (Delete), US6 (Bulk Update), US7 (Form Validation)

---

## Notes

- All code goes in single file: `sample/Frank.Datastar.Hox/Program.fs`
- Reference `sample/Frank.Datastar.Basic/Program.fs` for exact behavior
- Use `research.md` for Hox syntax patterns
- Every render function returns `ValueTask<string>` (await before broadcast)
- HTML IDs must match Basic sample exactly (tests depend on selectors)
- Commit after each phase or logical group
