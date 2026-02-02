# Tasks: Update Oxpecker Sample

**Input**: Design documents from `/specs/007-update-oxpecker-sample/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests already exist in `sample/Frank.Datastar.Tests/`. This feature updates the Oxpecker sample to pass those tests.

**Organization**: Tasks are organized by implementation phase since all three user stories (US1: Pass Tests, US2: Behavioral Parity, US3: Use Oxpecker.ViewEngine) are achieved together through the implementation.

## Format: `[ID] [P?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- Include exact file paths in descriptions

## Path Conventions

- **Sample project**: `sample/Frank.Datastar.Oxpecker/`
- **Reference implementation**: `sample/Frank.Datastar.Basic/` (read-only)
- **Test suite**: `sample/Frank.Datastar.Tests/`

---

## Phase 1: Static Files

**Purpose**: Copy index.html from Basic sample to establish correct SSE connection pattern

- [x] T001 Copy index.html from `sample/Frank.Datastar.Basic/wwwroot/index.html` to `sample/Frank.Datastar.Oxpecker/wwwroot/index.html`

**Checkpoint**: Oxpecker sample has same static HTML as Basic sample

---

## Phase 2: Core Infrastructure (SSE Architecture)

**Purpose**: Replace MailboxProcessor channels with unified broadcast channel pattern

**⚠️ CRITICAL**: All resource handlers depend on this infrastructure

- [x] T002 Replace SseEvent type in `sample/Frank.Datastar.Oxpecker/Program.fs` - remove Close variant to match Basic (PatchElements, RemoveElement, PatchSignals only)
- [x] T003 Remove all MailboxProcessor channel code (contactChannel, fruitsChannel, itemsChannel, usersChannel, registrationChannel, createSseChannel, SseChannelMsg) in `sample/Frank.Datastar.Oxpecker/Program.fs`
- [x] T004 Add SseEvent module with subscribe/unsubscribe/broadcast/writeSseEvent functions in `sample/Frank.Datastar.Oxpecker/Program.fs` - copy from Basic sample

**Checkpoint**: SSE broadcast infrastructure ready for resource handlers

---

## Phase 3: Data Types and Stores

**Purpose**: Ensure data types and stores match Basic sample exactly

- [x] T005 [P] Verify/update Contact and ContactSignals types in `sample/Frank.Datastar.Oxpecker/Program.fs` match Basic sample
- [x] T006 [P] Verify/update User, UserStatus, and BulkUpdateSignals types in `sample/Frank.Datastar.Oxpecker/Program.fs` match Basic sample
- [x] T007 [P] Verify/update Item type in `sample/Frank.Datastar.Oxpecker/Program.fs` matches Basic sample
- [x] T008 [P] Verify/update Registration and RegistrationSignals types in `sample/Frank.Datastar.Oxpecker/Program.fs` match Basic sample
- [x] T009 Verify/update in-memory data stores (contacts, users, items, fruits, registrations, nextRegistrationId) in `sample/Frank.Datastar.Oxpecker/Program.fs` match Basic sample initialization

**Checkpoint**: All data types and stores identical to Basic sample

---

## Phase 4: Render Functions (Oxpecker.ViewEngine)

**Purpose**: Convert string template render functions to Oxpecker.ViewEngine while producing identical HTML

### Contact Pattern (Click-to-Edit)

- [x] T010 [P] Update renderContactView function in `sample/Frank.Datastar.Oxpecker/Program.fs` to produce identical HTML to Basic sample using Oxpecker.ViewEngine - include data-indicator:_fetching and data-attr:disabled on Edit button
- [x] T011 [P] Update renderContactEdit function in `sample/Frank.Datastar.Oxpecker/Program.fs` to produce identical HTML to Basic sample - use data-bind:first-name (kebab-case), include data-indicator:_fetching and data-attr:disabled on all inputs/buttons

### Fruits Pattern (Search)

- [x] T012 [P] Update renderFruitsList function in `sample/Frank.Datastar.Oxpecker/Program.fs` to produce identical HTML to Basic sample - include style="min-height: 1em;" on ul element

### Items Pattern (Delete)

- [x] T013 [P] Update renderItemsTable function in `sample/Frank.Datastar.Oxpecker/Program.fs` to produce identical HTML to Basic sample - use confirm('Are you sure?') in data-on:click, include data-indicator:_fetching and data-attr:disabled on Delete buttons

### Users Pattern (Bulk Update)

- [x] T014 [P] Update renderUsersTable function in `sample/Frank.Datastar.Oxpecker/Program.fs` to produce identical HTML to Basic sample - use data-signals__ifmissing, data-effect, data-bind:selections with array, and data-bind:_all for select-all checkbox

### Registration Pattern (Form Validation)

- [x] T015 [P] Update renderValidationFeedback function in `sample/Frank.Datastar.Oxpecker/Program.fs` to produce identical HTML to Basic sample
- [x] T016 [P] Update renderRegistrationSuccess function in `sample/Frank.Datastar.Oxpecker/Program.fs` to produce identical HTML to Basic sample
- [x] T017 [P] Update renderRegistrationForm function in `sample/Frank.Datastar.Oxpecker/Program.fs` to produce identical HTML to Basic sample - use data-on:keydown__debounce.500ms, include data-attr:disabled on all inputs/buttons

**Checkpoint**: All render functions produce HTML identical to Basic sample

---

## Phase 5: Resource Handlers

**Purpose**: Update resource handlers to use SseEvent.broadcast and match Basic sample behavior

### SSE Endpoint

- [x] T018 Add sseResource for `/sse` endpoint in `sample/Frank.Datastar.Oxpecker/Program.fs` - copy datastar handler from Basic sample

### Debug Endpoint

- [x] T019 [P] Add debugPingResource for `/debug/ping` endpoint in `sample/Frank.Datastar.Oxpecker/Program.fs` - copy from Basic sample

### Contact Handlers

- [x] T020 Update contactResource GET handler in `sample/Frank.Datastar.Oxpecker/Program.fs` - use SseEvent.broadcast instead of channel post, return 202/404
- [x] T021 Update contactResource PUT handler in `sample/Frank.Datastar.Oxpecker/Program.fs` - use SseEvent.broadcast, return 202/400/404
- [x] T022 Update contactEditResource GET handler in `sample/Frank.Datastar.Oxpecker/Program.fs` - use SseEvent.broadcast, return 202/404

### Fruits Handler

- [x] T023 Update fruitsResource GET handler in `sample/Frank.Datastar.Oxpecker/Program.fs` - use SseEvent.broadcast for both initial load and filtered search, return 202

### Items Handlers

- [x] T024 Rename itemsCollectionResource to itemsResource in `sample/Frank.Datastar.Oxpecker/Program.fs` - update GET handler to use SseEvent.broadcast, return 202
- [x] T025 Update itemResource DELETE handler in `sample/Frank.Datastar.Oxpecker/Program.fs` - use SseEvent.broadcast with RemoveElement, return 202/404

### Users Handlers

- [x] T026 Rename usersCollectionResource to usersResource in `sample/Frank.Datastar.Oxpecker/Program.fs` - update GET handler to use SseEvent.broadcast, return 202
- [x] T027 Update usersBulkResource PUT handler in `sample/Frank.Datastar.Oxpecker/Program.fs` - use SseEvent.broadcast, return 202/400

### Registration Handlers

- [x] T028 Update registrationFormResource GET handler in `sample/Frank.Datastar.Oxpecker/Program.fs` - use SseEvent.broadcast, return 202
- [x] T029 Update registrationValidateResource POST handler in `sample/Frank.Datastar.Oxpecker/Program.fs` - use SseEvent.broadcast, return 202/400
- [x] T030 Update registrationsResource POST handler in `sample/Frank.Datastar.Oxpecker/Program.fs` - use SseEvent.broadcast, return 201/400/409

**Checkpoint**: All resource handlers use broadcast pattern and return correct status codes

---

## Phase 6: Application Setup

**Purpose**: Register all resources in correct order

- [x] T031 Update webHost configuration in `sample/Frank.Datastar.Oxpecker/Program.fs` - add sseResource first, then register all resources in same order as Basic sample

**Checkpoint**: Application fully configured with all resources

---

## Phase 7: Validation

**Purpose**: Verify implementation against test suite

- [x] T032 Run full test suite: `dotnet run --project sample/Frank.Datastar.Oxpecker/ & sleep 3 && DATASTAR_SAMPLE=Frank.Datastar.Oxpecker dotnet test sample/Frank.Datastar.Tests/; pkill -f "Frank.Datastar.Oxpecker"`
- [x] T033 If tests fail, debug and fix issues - compare generated HTML with Basic sample using browser dev tools
- [x] T034 Run tests in headed mode to visually verify: `HEADED=1 DATASTAR_SAMPLE=Frank.Datastar.Oxpecker dotnet test sample/Frank.Datastar.Tests/`

**Checkpoint**: All tests pass

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1**: No dependencies - can start immediately
- **Phase 2**: Depends on Phase 1 (index.html must be in place)
- **Phase 3**: Can run in parallel with Phase 2
- **Phase 4**: Can run in parallel with Phase 2 and 3 (render functions don't depend on infrastructure)
- **Phase 5**: Depends on Phase 2 (needs SseEvent module) and Phase 4 (needs render functions)
- **Phase 6**: Depends on Phase 5 (needs all resources defined)
- **Phase 7**: Depends on Phase 6 (needs complete application)

### Parallel Opportunities

- T005-T009 (data types) can run in parallel
- T010-T017 (render functions) can run in parallel
- T019 (debug endpoint) can run in parallel with other Phase 5 tasks
- T020-T030 (resource handlers) are sequential within same resource but different resources can be parallelized

---

## Parallel Example: Render Functions

```bash
# Launch all render function tasks together:
Task: "Update renderContactView function"
Task: "Update renderContactEdit function"
Task: "Update renderFruitsList function"
Task: "Update renderItemsTable function"
Task: "Update renderUsersTable function"
Task: "Update renderValidationFeedback function"
Task: "Update renderRegistrationSuccess function"
Task: "Update renderRegistrationForm function"
```

---

## Implementation Strategy

### Recommended Approach

Since all tasks modify the same file (`sample/Frank.Datastar.Oxpecker/Program.fs`), execute sequentially within phases but verify after each phase:

1. **Phase 1**: Copy index.html → Verify file copied
2. **Phase 2**: SSE infrastructure → Verify compiles (types may break temporarily)
3. **Phase 3**: Data types → Verify compiles
4. **Phase 4**: Render functions → Verify compiles
5. **Phase 5**: Resource handlers → Verify compiles
6. **Phase 6**: App setup → Verify compiles and runs
7. **Phase 7**: Run tests → All should pass

### Alternative: Full Rewrite

Given the scope of changes (essentially rewriting Program.fs to match Basic sample structure with Oxpecker.ViewEngine syntax), an alternative approach:

1. Start with Basic sample Program.fs
2. Convert each string template render function to Oxpecker.ViewEngine
3. Keep everything else identical
4. This may be faster than incremental updates

---

## Notes

- All tasks modify `sample/Frank.Datastar.Oxpecker/Program.fs` (single file)
- Reference implementation: `sample/Frank.Datastar.Basic/Program.fs` (read-only)
- Tests validate behavior: `sample/Frank.Datastar.Tests/`
- Commit after each phase is complete and compiles
- Tests are the ultimate acceptance criteria
