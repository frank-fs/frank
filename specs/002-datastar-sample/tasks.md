# Tasks: Frank.Datastar Sample Application

**Input**: Design documents from `/specs/002-datastar-sample/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api.md

**Tests**: NOT included - per spec assumptions, tests are for the Frank.Datastar library, not the sample application.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

All code changes are in the existing sample application:
- `sample/Frank.Datastar.Basic/Program.fs` - F# server code
- `sample/Frank.Datastar.Basic/wwwroot/index.html` - HTML page

---

## Phase 1: Setup

**Purpose**: Verify existing project structure and ensure prerequisites are met

- [ ] T001 Verify existing sample compiles with `dotnet build sample/Frank.Datastar.Basic`
- [ ] T002 Verify existing tests pass with `dotnet test test/Frank.Datastar.Tests`
- [ ] T003 Review existing examples in sample/Frank.Datastar.Basic/Program.fs to understand current patterns

**Checkpoint**: Existing sample verified working before modifications

---

## Phase 2: Foundational (Types & Data Structures)

**Purpose**: Add F# types and in-memory data that all user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T004 [P] Add Contact type and ContactSignals type in sample/Frank.Datastar.Basic/Program.fs
- [ ] T005 [P] Add User type, UserStatus DU, and BulkUpdateSignals type in sample/Frank.Datastar.Basic/Program.fs
- [ ] T006 [P] Add Item type in sample/Frank.Datastar.Basic/Program.fs
- [ ] T007 [P] Add Registration type and RegistrationSignals type in sample/Frank.Datastar.Basic/Program.fs
- [ ] T008 Add in-memory data stores (contacts dict, fruits list, items ResizeArray, users dict, registrations ResizeArray) in sample/Frank.Datastar.Basic/Program.fs
- [ ] T009 Verify project still compiles after adding types with `dotnet build sample/Frank.Datastar.Basic`

**Checkpoint**: Foundation ready - all types and data stores in place

---

## Phase 3: User Story 1 - Learn RESTful Datastar Patterns (Priority: P1) 🎯 MVP

**Goal**: Developer sees organized examples on home page demonstrating HTTP resource semantics

**Independent Test**: Run sample, navigate to home page, see organized list of patterns with descriptions

### Implementation for User Story 1

- [ ] T010 [US1] Add "RESTful Resource Patterns" section header comment in sample/Frank.Datastar.Basic/Program.fs after existing examples
- [ ] T011 [US1] Add RESTful Patterns navigation section to sample/Frank.Datastar.Basic/wwwroot/index.html with links to Click-to-Edit, Search, Delete, Bulk Update, and Validation demos
- [ ] T012 [US1] Add placeholder div elements for each new pattern section in sample/Frank.Datastar.Basic/wwwroot/index.html (contact-demo, fruits-demo, items-demo, users-demo, registration-demo)
- [ ] T013 [US1] Verify index.html loads without errors by running sample with `dotnet run --project sample/Frank.Datastar.Basic`

**Checkpoint**: Home page shows navigation to all patterns (placeholders for now)

---

## Phase 4: User Story 2 - Click-to-Edit Pattern (Priority: P2)

**Goal**: Developer sees contact resource with view mode (GET), edit mode (GET /edit), and save (PUT)

**Independent Test**: Click edit on contact, modify fields, save - URL stays `/contacts/1` while representation changes

### Implementation for User Story 2

- [ ] T014 [US2] Add renderContactView helper function for read-only contact HTML in sample/Frank.Datastar.Basic/Program.fs
- [ ] T015 [US2] Add renderContactEdit helper function for editable contact form HTML in sample/Frank.Datastar.Basic/Program.fs
- [ ] T016 [US2] Implement GET /contacts/{id} resource with datastar CE operation (establishes SSE, returns view) in sample/Frank.Datastar.Basic/Program.fs
- [ ] T017 [US2] Implement GET /contacts/{id}/edit resource with datastar CE operation (returns edit form) in sample/Frank.Datastar.Basic/Program.fs
- [ ] T018 [US2] Implement PUT /contacts/{id} resource with datastar CE operation (reads signals, updates contact, returns view) in sample/Frank.Datastar.Basic/Program.fs
- [ ] T019 [US2] Add 404 handling for non-existent contact IDs in all contact endpoints
- [ ] T020 [US2] Add contact demo section HTML with data-on-load to fetch initial contact in sample/Frank.Datastar.Basic/wwwroot/index.html
- [ ] T021 [US2] Register contact resources in webHost builder in sample/Frank.Datastar.Basic/Program.fs
- [ ] T022 [US2] Manually test click-to-edit flow: view → edit → save → view

**Checkpoint**: Contact click-to-edit pattern fully functional

---

## Phase 5: User Story 3 - Search with Filtering (Priority: P2)

**Goal**: Developer sees searchable fruits collection using GET with query parameters

**Independent Test**: Type in search box, results filter via debounced GET /fruits?q=term

### Implementation for User Story 3

- [ ] T023 [US3] Add renderFruitsList helper function for fruits HTML list in sample/Frank.Datastar.Basic/Program.fs
- [ ] T024 [US3] Implement GET /fruits resource with datastar CE operation (reads q param, filters fruits, returns list) in sample/Frank.Datastar.Basic/Program.fs
- [ ] T025 [US3] Add fruits demo section HTML with search input using data-bind and debounced data-on:input in sample/Frank.Datastar.Basic/wwwroot/index.html
- [ ] T026 [US3] Register fruits resource in webHost builder in sample/Frank.Datastar.Basic/Program.fs
- [ ] T027 [US3] Manually test search: type "ap" and see Apple, Apricot, Grape, Papaya filtered

**Checkpoint**: Fruits search pattern fully functional

---

## Phase 6: User Story 4 - Row Deletion (Priority: P3)

**Goal**: Developer sees item list with DELETE on specific resource URLs (fire-and-forget pattern)

**Independent Test**: Click delete on item, confirm, item removed from list via SSE channel

### Implementation for User Story 4

- [ ] T028 [US4] Add renderItemsTable helper function for items table HTML in sample/Frank.Datastar.Basic/Program.fs
- [ ] T029 [US4] Implement GET /items resource with datastar CE operation (establishes SSE, returns table, keeps connection open) in sample/Frank.Datastar.Basic/Program.fs
- [ ] T030 [US4] Implement DELETE /items/{id} resource with delete CE operation (fire-and-forget: removes item, returns 202, sends removeElement over SSE) in sample/Frank.Datastar.Basic/Program.fs
- [ ] T031 [US4] Add 404 handling for already-deleted items in DELETE endpoint
- [ ] T032 [US4] Add items demo section HTML with data-on-load to fetch initial items in sample/Frank.Datastar.Basic/wwwroot/index.html
- [ ] T033 [US4] Register items resources in webHost builder in sample/Frank.Datastar.Basic/Program.fs
- [ ] T034 [US4] Manually test deletion: delete item, confirm removal from list without page refresh

**Checkpoint**: Items deletion pattern fully functional

---

## Phase 7: User Story 5 - Bulk Operations (Priority: P3)

**Goal**: Developer sees user table with checkboxes and bulk status update via PUT on collection

**Independent Test**: Select multiple users, click Activate, all selected users show Active status

### Implementation for User Story 5

- [ ] T035 [US5] Add renderUsersTable helper function for users table HTML with checkboxes in sample/Frank.Datastar.Basic/Program.fs
- [ ] T036 [US5] Implement GET /users resource with datastar CE operation (establishes SSE, returns table) in sample/Frank.Datastar.Basic/Program.fs
- [ ] T037 [US5] Implement PUT /users/bulk resource with datastar CE operation (reads selections from signals, status from query param, updates users, returns updated table) in sample/Frank.Datastar.Basic/Program.fs
- [ ] T038 [US5] Add users demo section HTML with data-signals for selections array and activate/deactivate buttons in sample/Frank.Datastar.Basic/wwwroot/index.html
- [ ] T039 [US5] Register users resources in webHost builder in sample/Frank.Datastar.Basic/Program.fs
- [ ] T040 [US5] Manually test bulk update: select users, activate, verify status changes

**Checkpoint**: Users bulk update pattern fully functional

---

## Phase 8: User Story 6 - Form Validation Pattern (Priority: P3)

**Goal**: Developer sees registration form with real-time validation via separate validation endpoint

**Independent Test**: Type invalid email, see validation error; fix it, see success; submit form, see success message

### Implementation for User Story 6

- [ ] T041 [US6] Add validateRegistration helper function returning list of errors in sample/Frank.Datastar.Basic/Program.fs
- [ ] T042 [US6] Add renderValidationFeedback helper function for validation result HTML in sample/Frank.Datastar.Basic/Program.fs
- [ ] T043 [US6] Add renderRegistrationSuccess helper function for success message HTML in sample/Frank.Datastar.Basic/Program.fs
- [ ] T044 [US6] Implement POST /registrations/validate resource with datastar POST CE operation (validates, streams feedback) in sample/Frank.Datastar.Basic/Program.fs
- [ ] T045 [US6] Implement POST /registrations resource with datastar POST CE operation (validates, creates registration, streams success or errors) in sample/Frank.Datastar.Basic/Program.fs
- [ ] T045a [US6] Add 409 Conflict handling for duplicate email in POST /registrations in sample/Frank.Datastar.Basic/Program.fs
- [ ] T046 [US6] Add registration demo section HTML with form inputs using debounced validation in sample/Frank.Datastar.Basic/wwwroot/index.html
- [ ] T047 [US6] Register registration resources in webHost builder in sample/Frank.Datastar.Basic/Program.fs
- [ ] T048 [US6] Manually test validation flow: invalid input → errors, valid input → success

**Checkpoint**: Registration validation pattern fully functional

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Final verification and documentation

- [ ] T049 Add brief comments explaining resource model for each new example in sample/Frank.Datastar.Basic/Program.fs (FR-010)
- [ ] T050 Verify all existing 10 examples still work (FR-009) by testing displayDate, searchItems, counter
- [ ] T051 Verify zero RPC-style URLs exist (SC-004) - all URLs use nouns not verbs
- [ ] T052 Run full test suite with `dotnet test` to ensure no regressions
- [ ] T053 Verify sample compiles on .NET 8.0, 9.0, 10.0 (SC-006) with `dotnet build sample/Frank.Datastar.Basic`
- [ ] T054 Run quickstart.md validation - clone fresh, run sample, interact within 2 minutes (SC-001)
- [ ] T055 Add 405 Method Not Allowed handling for unsupported methods (e.g., DELETE on /fruits) in sample/Frank.Datastar.Basic/Program.fs
- [ ] T056 Manually test edge cases: 404 not found, 405 method not allowed, 409 conflict on duplicate registration

**Checkpoint**: All requirements verified, sample ready for use

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - verify existing state
- **Foundational (Phase 2)**: Depends on Setup - BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational - can start after Phase 2
- **User Stories 2-6 (Phases 4-8)**: Depend on Phase 3 (navigation structure) but can run in parallel
- **Polish (Phase 9)**: Depends on all user stories complete

### User Story Dependencies

```
Phase 1 (Setup)
    ↓
Phase 2 (Foundational - Types)
    ↓
Phase 3 (US1 - Home Page/Navigation) ← MVP
    ↓
┌───┴───┬───────┬───────┬───────┐
↓       ↓       ↓       ↓       ↓
US2     US3     US4     US5     US6
(P2)    (P2)    (P3)    (P3)    (P3)
Contact Fruits  Items   Users   Registration
    └───────┴───────┴───────┴───────┘
                    ↓
              Phase 9 (Polish)
```

### Within Each User Story

- Render helpers before resource implementations
- Resources before registration in webHost
- Manual testing after implementation complete

### Parallel Opportunities

- T004, T005, T006, T007 (all type definitions) can run in parallel
- After Phase 3, all user stories (US2-US6) can proceed in parallel
- Within each story, [P] marked tasks can run in parallel

---

## Parallel Example: Phase 2 (Foundational)

```bash
# Launch all type definitions together:
Task: "Add Contact type and ContactSignals type in sample/Frank.Datastar.Basic/Program.fs"
Task: "Add User type, UserStatus DU, and BulkUpdateSignals type in sample/Frank.Datastar.Basic/Program.fs"
Task: "Add Item type in sample/Frank.Datastar.Basic/Program.fs"
Task: "Add Registration type and RegistrationSignals type in sample/Frank.Datastar.Basic/Program.fs"
```

## Parallel Example: User Stories 2-6 (after Phase 3)

```bash
# With multiple developers, all can work simultaneously:
Developer A: Phase 4 (US2 - Contact click-to-edit)
Developer B: Phase 5 (US3 - Fruits search)
Developer C: Phases 6-8 (US4-6 - Delete, Bulk, Validation)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup verification
2. Complete Phase 2: Types and data stores
3. Complete Phase 3: Home page with navigation
4. **STOP and VALIDATE**: Sample runs, shows organized examples
5. Deploy/demo if ready

### Incremental Delivery

1. Phase 1 + 2 → Foundation ready
2. Add Phase 3 (US1) → Test → Demo (MVP with navigation)
3. Add Phase 4 (US2) → Test → Demo (Click-to-Edit works)
4. Add Phase 5 (US3) → Test → Demo (Search works)
5. Add Phases 6-8 (US4-6) → Test → Demo (All patterns complete)
6. Phase 9 → Final verification

### Single Developer Strategy

Work through phases sequentially in priority order:
1. Setup → Foundational → US1 (MVP)
2. US2 (P2) → US3 (P2) - both high value
3. US4 → US5 → US6 (all P3)
4. Polish

---

## Notes

- All HTML uses F# string templates ($"..." and $"""...""") per FR-008
- Existing 10 examples MUST remain functional per FR-009
- No new tests - per spec assumptions, tests are for library not sample
- SSE architecture: GET/POST establish connections, DELETE is fire-and-forget
- Each user story is independently testable by running sample and interacting
- Commit after each task or logical group
