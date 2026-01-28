# Tasks: Frank.Datastar.Oxpecker Sample

**Input**: Design documents from `/specs/004-datastar-oxpecker-sample/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api.md

**Tests**: Uses existing test.sh script (bash/curl HTTP tests). No additional tests required.

**Organization**: Tasks organized by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Includes exact file paths in descriptions

## Path Conventions

Single-file sample project:
```text
sample/Frank.Datastar.Oxpecker/
├── Frank.Datastar.Oxpecker.fsproj
├── Program.fs
└── test.sh
```

---

## Phase 1: Setup

**Purpose**: Create project directory and configuration

- [ ] T001 Create project directory at sample/Frank.Datastar.Oxpecker/
- [ ] T002 Create Frank.Datastar.Oxpecker.fsproj with net10.0, Frank 6.x, Oxpecker.ViewEngine 2.x, and Frank.Datastar ProjectReference
- [ ] T003 Copy test.sh from sample/Frank.Datastar.Basic/test.sh to sample/Frank.Datastar.Oxpecker/test.sh

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure in Program.fs that MUST be complete before implementing resources

**File**: sample/Frank.Datastar.Oxpecker/Program.fs

- [ ] T004 Create Program.fs with module declaration, open statements for System, Microsoft.AspNetCore, Frank, Frank.Datastar, Oxpecker.ViewEngine, Oxpecker.ViewEngine.Render
- [ ] T005 Define all entity types (Contact, ContactSignals, UserStatus, User, BulkUpdateSignals, Item, Registration, RegistrationSignals) per data-model.md
- [ ] T006 Define SSE event types (SseEvent, SseChannelMsg) and createSseChannel MailboxProcessor factory
- [ ] T007 Create all SSE channels (contactChannel, fruitsChannel, itemsChannel, usersChannel, registrationChannel)
- [ ] T008 Create in-memory data stores (contacts Dictionary, fruits list, items ResizeArray, users Dictionary, registrations ResizeArray)
- [ ] T009 Implement writeSseEvent helper function for SSE response handling

**Checkpoint**: Foundation ready - all types and infrastructure defined

---

## Phase 3: User Story 1 - F# Developer Evaluates Oxpecker.ViewEngine (Priority: P1) MVP

**Goal**: Complete sample with all 5 RESTful patterns using Oxpecker.ViewEngine syntax

**Independent Test**: Run `dotnet build` and `./test.sh` - all 18 tests pass

**File**: sample/Frank.Datastar.Oxpecker/Program.fs (continued)

### Contact Resources (Click-to-Edit Pattern)

- [ ] T010 [US1] Implement renderContactView function using Oxpecker.ViewEngine computation expressions with toString
- [ ] T011 [US1] Implement renderContactEdit function with data-signals and data-bind attributes via .attr()
- [ ] T012 [US1] Implement contactResource (GET /contacts/{id} SSE, PUT /contacts/{id} update)
- [ ] T013 [US1] Implement contactEditResource (GET /contacts/{id}/edit fire-and-forget)

### Fruits Resource (Search Pattern)

- [ ] T014 [US1] Implement renderFruitsList function using Oxpecker.ViewEngine for ul/li list
- [ ] T015 [US1] Implement fruitsResource (GET /fruits SSE with optional ?q= search)

### Items Resources (Delete Pattern)

- [ ] T016 [US1] Implement renderItemsTable function using Oxpecker.ViewEngine for table with delete buttons
- [ ] T017 [US1] Implement itemsCollectionResource (GET /items SSE)
- [ ] T018 [US1] Implement itemResource (DELETE /items/{id} fire-and-forget)

### Users Resources (Bulk Update Pattern)

- [ ] T019 [US1] Implement renderUsersTable function using Oxpecker.ViewEngine with checkbox bindings
- [ ] T020 [US1] Implement usersCollectionResource (GET /users SSE)
- [ ] T021 [US1] Implement usersBulkResource (PUT /users/bulk?status= fire-and-forget)

### Registration Resources (Form Validation Pattern)

- [ ] T022 [US1] Implement validateRegistration function
- [ ] T023 [US1] Implement renderValidationFeedback function using Oxpecker.ViewEngine
- [ ] T024 [US1] Implement renderRegistrationSuccess function using Oxpecker.ViewEngine
- [ ] T025 [US1] Implement renderRegistrationForm function using Oxpecker.ViewEngine with data-on:input attributes
- [ ] T026 [US1] Implement registrationFormResource (GET /registrations/form SSE)
- [ ] T027 [US1] Implement registrationValidateResource (POST /registrations/validate fire-and-forget)
- [ ] T028 [US1] Implement registrationsResource (POST /registrations create)

### Application Entry Point

- [ ] T029 [US1] Implement main function with webHost builder registering all resources
- [ ] T030 [US1] Build and verify compilation with `dotnet build`
- [ ] T031 [US1] Run test.sh and verify all 18 tests pass (tests 11-28)

**Checkpoint**: User Story 1 complete - sample fully functional and tested

---

## Phase 4: User Story 2 - Developer Compares View Engine Approaches (Priority: P2)

**Goal**: Ensure code structure enables easy comparison with other samples

**Independent Test**: Compare Program.fs files - same structure, only render functions differ

- [ ] T032 [US2] Review Program.fs structure matches Frank.Datastar.Basic and Frank.Datastar.Hox organization
- [ ] T033 [US2] Verify all render functions use Oxpecker.ViewEngine patterns from VIEW_ENGINE_COMPARISON.md
- [ ] T034 [US2] Add header comment explaining Oxpecker.ViewEngine syntax patterns

**Checkpoint**: User Story 2 complete - code is comparison-ready

---

## Phase 5: User Story 3 - Sample Passes Automated Testing (Priority: P3)

**Goal**: Verify complete test compatibility with other samples

**Independent Test**: Run test.sh - all tests 11-28 show PASS

- [ ] T035 [US3] Verify test 11 (GET /contacts/1 SSE view) returns HTML with "First Name"
- [ ] T036 [US3] Verify test 12-14 (contact edit/update/404) return correct HTTP status codes
- [ ] T037 [US3] Verify test 15-16 (fruits SSE/search) work correctly
- [ ] T038 [US3] Verify test 17-18 (items table/delete 404) work correctly
- [ ] T039 [US3] Verify test 19-20 (users table/bulk update) work correctly
- [ ] T040 [US3] Verify test 21-25 (registration form/validate/create/duplicate) work correctly
- [ ] T041 [US3] Verify test 26-28 (405 method not allowed) work correctly

**Checkpoint**: User Story 3 complete - all automated tests pass

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and cleanup

- [ ] T042 Ensure no Hox imports or raw HTML string templates remain
- [ ] T043 Verify Oxpecker.ViewEngine computation expression syntax is consistent throughout
- [ ] T044 Run final `dotnet build` - no warnings
- [ ] T045 Run final `./test.sh` - all 18 tests pass
- [ ] T046 Validate quickstart.md instructions work end-to-end

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational - implements all functionality
- **User Story 2 (Phase 4)**: Depends on User Story 1 - code review/comparison
- **User Story 3 (Phase 5)**: Depends on User Story 1 - verification testing
- **Polish (Phase 6)**: Depends on all user stories complete

### User Story Dependencies

- **User Story 1 (P1)**: Main implementation - no dependencies on other stories
- **User Story 2 (P2)**: Code review - depends on US1 completion
- **User Story 3 (P3)**: Test verification - depends on US1 completion

### Within User Story 1

All implementation tasks are in a single file (Program.fs), so they must be sequential:
1. Render functions for each pattern
2. Resource definitions for each pattern
3. Main entry point with all resources registered
4. Build and test verification

### Parallel Opportunities

**Phase 1** (Setup):
```text
T002 and T003 can run in parallel (different files)
```

**Phase 2** (Foundational):
```text
All tasks modify same file (Program.fs) - must be sequential
```

**Phase 3-5** (User Stories):
```text
US2 and US3 can run in parallel after US1 completes
(US2 = code review, US3 = test verification - independent activities)
```

---

## Parallel Example: Setup Phase

```bash
# Can launch in parallel:
Task: "Create Frank.Datastar.Oxpecker.fsproj..."
Task: "Copy test.sh..."
```

## Parallel Example: After User Story 1

```bash
# Once US1 is complete, launch in parallel:
Task: "[US2] Review Program.fs structure..."
Task: "[US3] Verify test 11..."
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (3 tasks)
2. Complete Phase 2: Foundational (6 tasks)
3. Complete Phase 3: User Story 1 (22 tasks)
4. **STOP and VALIDATE**: `dotnet build` and `./test.sh`
5. MVP is deployable/demonstrable

### Incremental Delivery

1. Setup + Foundational → Project skeleton ready
2. Add US1 → Test with test.sh → MVP complete!
3. Add US2 → Code review/comparison ready
4. Add US3 → Full test verification documented
5. Polish → Production-ready sample

### Single Developer Strategy

Since all main implementation is in one file (Program.fs):
1. Complete Setup and Foundational sequentially
2. Implement all render functions (T010-T011, T014, T016, T019, T022-T025)
3. Implement all resources (T012-T013, T015, T017-T018, T020-T021, T026-T028)
4. Add main entry point (T029)
5. Build and test (T030-T031)
6. Quick verification of US2 and US3 (code review + test spot checks)

---

## Notes

- All implementation is in single Program.fs file (~700 lines)
- Existing test.sh script is copied, not modified
- Oxpecker.ViewEngine patterns use `.attr()` for Datastar attributes
- `Render.toString` converts HtmlElement to string synchronously
- Commit after each logical group (Setup, Foundational, each resource pattern)
