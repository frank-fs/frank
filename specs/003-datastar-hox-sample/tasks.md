# Tasks: Frank.Datastar.Hox Sample Application

**Input**: Design documents from `/specs/003-datastar-hox-sample/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api.md

**Tests**: NOT included - per spec, testing is via manual test.sh script, not automated tests.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. However, since this sample replaces an existing implementation with identical functionality, all stories share the same codebase and are tested together via test.sh.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

All code changes are in the existing sample application:
- `sample/Frank.Datastar.Hox/Program.fs` - F# server code
- `sample/Frank.Datastar.Hox/wwwroot/index.html` - HTML page (copied from Basic sample)

---

## Phase 1: Setup

**Purpose**: Verify existing project structure and prepare for implementation

- [ ] T001 Verify existing sample compiles with `dotnet build sample/Frank.Datastar.Hox`
- [ ] T002 Copy wwwroot/index.html from sample/Frank.Datastar.Basic to sample/Frank.Datastar.Hox/wwwroot/
- [ ] T003 Copy test.sh from sample/Frank.Datastar.Basic to sample/Frank.Datastar.Hox/
- [ ] T004 Verify Hox package reference exists in sample/Frank.Datastar.Hox/Frank.Datastar.Hox.fsproj (Version="3.*")

**Checkpoint**: Setup verified - ready to replace implementation

---

## Phase 2: Foundational (Types & Infrastructure)

**Purpose**: Add F# types and SSE infrastructure that all patterns depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T005 [P] Add Hox imports (open Hox, open Hox.Core, open Hox.Rendering) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T006 [P] Add Contact type and ContactSignals type in sample/Frank.Datastar.Hox/Program.fs
- [ ] T007 [P] Add User type, UserStatus DU, and BulkUpdateSignals type in sample/Frank.Datastar.Hox/Program.fs
- [ ] T008 [P] Add Item type in sample/Frank.Datastar.Hox/Program.fs
- [ ] T009 [P] Add Registration type and RegistrationSignals type in sample/Frank.Datastar.Hox/Program.fs
- [ ] T010 Add SseEvent and SseChannelMsg types in sample/Frank.Datastar.Hox/Program.fs
- [ ] T011 Add createSseChannel function (MailboxProcessor factory) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T012 Add SSE channel instances (contactChannel, fruitsChannel, itemsChannel, usersChannel, registrationChannel) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T013 Add in-memory data stores (contacts dict, fruits list, items ResizeArray, users dict, registrations ResizeArray) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T014 Add writeSseEvent helper function in sample/Frank.Datastar.Hox/Program.fs
- [ ] T015 Verify project still compiles after adding types with `dotnet build sample/Frank.Datastar.Hox`

**Checkpoint**: Foundation ready - all types, data stores, and SSE infrastructure in place

---

## Phase 3: User Story 2 - Click-to-Edit Pattern (Priority: P2)

**Goal**: Developer sees contact resource with view mode (GET), edit mode (GET /edit), and save (PUT)

**Independent Test**: Click edit on contact, modify fields, save - URL stays `/contacts/1` while representation changes

### Hox Render Functions for Contact

- [ ] T016 [US2] Add renderContactView helper function using Hox in sample/Frank.Datastar.Hox/Program.fs
- [ ] T017 [US2] Add renderContactEdit helper function using Hox in sample/Frank.Datastar.Hox/Program.fs

### Contact Resources

- [ ] T018 [US2] Implement GET /contacts/{id} resource with SSE channel pattern in sample/Frank.Datastar.Hox/Program.fs
- [ ] T019 [US2] Implement GET /contacts/{id}/edit resource as fire-and-forget in sample/Frank.Datastar.Hox/Program.fs
- [ ] T020 [US2] Implement PUT /contacts/{id} resource as fire-and-forget in sample/Frank.Datastar.Hox/Program.fs
- [ ] T021 [US2] Add 404 handling for non-existent contact IDs in all contact endpoints
- [ ] T022 [US2] Register contact resources in webHost builder in sample/Frank.Datastar.Hox/Program.fs

**Checkpoint**: Contact click-to-edit pattern fully functional

---

## Phase 4: User Story 3 - Search with Filtering (Priority: P2)

**Goal**: Developer sees searchable fruits collection using GET with query parameters

**Independent Test**: Type in search box, results filter via debounced GET /fruits?q=term

### Hox Render Functions for Fruits

- [ ] T023 [US3] Add renderFruitsList helper function using Hox in sample/Frank.Datastar.Hox/Program.fs

### Fruits Resource

- [ ] T024 [US3] Implement GET /fruits resource with SSE channel pattern (no query = establish SSE, with query = fire-and-forget) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T025 [US3] Register fruits resource in webHost builder in sample/Frank.Datastar.Hox/Program.fs

**Checkpoint**: Fruits search pattern fully functional

---

## Phase 5: User Story 4 - Row Deletion (Priority: P3)

**Goal**: Developer sees item list with DELETE on specific resource URLs (fire-and-forget pattern)

**Independent Test**: Click delete on item, confirm, item removed from list via SSE channel

### Hox Render Functions for Items

- [ ] T026 [US4] Add renderItemsTable helper function using Hox in sample/Frank.Datastar.Hox/Program.fs

### Items Resources

- [ ] T027 [US4] Implement GET /items resource with SSE channel pattern in sample/Frank.Datastar.Hox/Program.fs
- [ ] T028 [US4] Implement DELETE /items/{id} resource as fire-and-forget (posts removeElement to channel) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T029 [US4] Add 404 handling for already-deleted items in DELETE endpoint
- [ ] T030 [US4] Register items resources in webHost builder in sample/Frank.Datastar.Hox/Program.fs

**Checkpoint**: Items deletion pattern fully functional

---

## Phase 6: User Story 5 - Bulk Update (Priority: P3)

**Goal**: Developer sees user table with checkboxes and bulk status update via PUT on collection

**Independent Test**: Select multiple users, click Activate, all selected users show Active status

### Hox Render Functions for Users

- [ ] T031 [US5] Add renderUsersTable helper function using Hox in sample/Frank.Datastar.Hox/Program.fs

### Users Resources

- [ ] T032 [US5] Implement GET /users resource with SSE channel pattern in sample/Frank.Datastar.Hox/Program.fs
- [ ] T033 [US5] Implement PUT /users/bulk resource as fire-and-forget (posts updated table to channel) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T034 [US5] Register users resources in webHost builder in sample/Frank.Datastar.Hox/Program.fs

**Checkpoint**: Users bulk update pattern fully functional

---

## Phase 7: User Story 6 - Form Validation Pattern (Priority: P3)

**Goal**: Developer sees registration form with real-time validation via separate validation endpoint

**Independent Test**: Type invalid email, see validation error; fix it, see success; submit form, see success message

### Hox Render Functions for Registration

- [ ] T035 [US6] Add validateRegistration helper function returning list of errors in sample/Frank.Datastar.Hox/Program.fs
- [ ] T036 [US6] Add renderValidationFeedback helper function using Hox in sample/Frank.Datastar.Hox/Program.fs
- [ ] T037 [US6] Add renderRegistrationSuccess helper function using Hox in sample/Frank.Datastar.Hox/Program.fs
- [ ] T038 [US6] Add renderRegistrationForm helper function using Hox in sample/Frank.Datastar.Hox/Program.fs

### Registration Resources

- [ ] T039 [US6] Implement GET /registrations/form resource with SSE channel pattern in sample/Frank.Datastar.Hox/Program.fs
- [ ] T040 [US6] Implement POST /registrations/validate resource as fire-and-forget in sample/Frank.Datastar.Hox/Program.fs
- [ ] T041 [US6] Implement POST /registrations resource as fire-and-forget (with 409 Conflict for duplicates) in sample/Frank.Datastar.Hox/Program.fs
- [ ] T042 [US6] Register registration resources in webHost builder in sample/Frank.Datastar.Hox/Program.fs

**Checkpoint**: Registration validation pattern fully functional

---

## Phase 8: User Story 1 - Test Validation & Polish (Priority: P1)

**Goal**: All tests pass, sample is complete and documented

**Independent Test**: Run `./test.sh` against sample running on localhost:5000 - all 18 tests pass

### Validation

- [ ] T043 [US1] Verify sample compiles with `dotnet build sample/Frank.Datastar.Hox`
- [ ] T044 [US1] Run sample with `dotnet run --project sample/Frank.Datastar.Hox`
- [ ] T045 [US1] Execute test.sh and verify all 18 tests pass (tests 11-28)
- [ ] T046 [US1] Verify 405 Method Not Allowed for unsupported methods (DELETE /fruits, POST /contacts/1, PUT /fruits)

### Polish

- [ ] T047 Add module header comment explaining Hox + Datastar integration pattern in sample/Frank.Datastar.Hox/Program.fs
- [ ] T048 Add section comments for each resource group in sample/Frank.Datastar.Hox/Program.fs
- [ ] T049 Verify all HTML element IDs match Basic sample (contact-view, fruits-list, items-table, users-table-container, registration-form, validation-feedback, registration-result)

**Checkpoint**: All requirements verified, sample ready for use

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - verify existing state
- **Foundational (Phase 2)**: Depends on Setup - BLOCKS all user stories
- **User Stories 2-6 (Phases 3-7)**: Depend on Foundational phase completion
- **User Story 1 Validation (Phase 8)**: Depends on ALL user stories being implemented

### User Story Dependencies

```text
Phase 1 (Setup)
    ↓
Phase 2 (Foundational - Types & SSE)
    ↓
┌───┴───┬───────┬───────┬───────┐
↓       ↓       ↓       ↓       ↓
US2     US3     US4     US5     US6
(P2)    (P2)    (P3)    (P3)    (P3)
Contact Fruits  Items   Users   Registration
    └───────┴───────┴───────┴───────┘
                    ↓
              Phase 8 (US1 - Validation)
```

**Note**: User Story 1 (P1) is actually the final validation - it cannot be tested until all other stories are implemented because test.sh tests all endpoints.

### Within Each User Story

- Render helper functions before resource implementations
- Resources before registration in webHost
- Core implementation before 404/error handling

### Parallel Opportunities

- T005, T006, T007, T008, T009 (all types and imports) can run in parallel
- After Phase 2, all user stories (US2-US6) can proceed in parallel
- Within each story, render helpers can be written in parallel

---

## Parallel Example: Phase 2 (Foundational)

```bash
# Launch all type definitions together:
Task: "Add Contact type and ContactSignals type in sample/Frank.Datastar.Hox/Program.fs"
Task: "Add User type, UserStatus DU, and BulkUpdateSignals type in sample/Frank.Datastar.Hox/Program.fs"
Task: "Add Item type in sample/Frank.Datastar.Hox/Program.fs"
Task: "Add Registration type and RegistrationSignals type in sample/Frank.Datastar.Hox/Program.fs"
```

## Parallel Example: User Stories (after Phase 2)

```bash
# With multiple developers, all can work simultaneously:
Developer A: Phase 3 (US2 - Contact click-to-edit)
Developer B: Phase 4 (US3 - Fruits search)
Developer C: Phases 5-7 (US4-6 - Delete, Bulk, Validation)
```

---

## Implementation Strategy

### Single Developer Strategy

Work through phases sequentially:
1. Setup → Foundational (required)
2. US2 (Contact) → verify manually
3. US3 (Fruits) → verify manually
4. US4 (Items) → verify manually
5. US5 (Users) → verify manually
6. US6 (Registration) → verify manually
7. US1 (Run test.sh) → all 18 tests pass

### MVP Definition

For this feature, MVP = complete implementation. Since test.sh tests all endpoints, partial implementation cannot pass the primary acceptance criterion. However, each user story can be manually verified in isolation during development.

---

## Notes

- All HTML uses Hox's CSS-selector DSL: `h("tag#id.class [attr=value]", children)`
- Use `Render.asString` to convert Hox nodes to HTML strings
- Use `fragment` for lists of elements without wrapper
- The wwwroot/index.html is identical to Basic sample (Datastar client code unchanged)
- 405 Method Not Allowed is handled automatically by Frank/ASP.NET Core routing
- Commit after each task or logical group
