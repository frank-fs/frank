# Tasks: Middleware Before Endpoints

**Input**: Design documents from `/specs/011-middleware-before-endpoints/`
**Prerequisites**: plan.md (required), spec.md (required), research.md

**Tests**: Authentication middleware test case requested.

**Organization**: This feature includes a bug fix, new API addition (`plugBeforeRouting`), documentation, version bump, and a test case.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Verify current state before making changes

- [x] T001 Verify build passes before changes: `dotnet build`
- [x] T002 Run existing Playwright tests to establish baseline: `dotnet test sample/Frank.Datastar.Tests/` (requires running sample + DATASTAR_SAMPLE env)

---

## Phase 2: Core Implementation

**Purpose**: Fix middleware ordering and add `plugBeforeRouting` - addresses US1, US2, US3, US4

### WebHostSpec Record Changes

- [x] T003 [US4] Add `BeforeRoutingMiddleware` field to `WebHostSpec` record in src/Frank/Builder.fs (line ~219)

**Change Required**:
```fsharp
type WebHostSpec =
    { Host: (IWebHostBuilder -> IWebHostBuilder)
      BeforeRoutingMiddleware: (IApplicationBuilder -> IApplicationBuilder)  // ADD
      Middleware: (IApplicationBuilder -> IApplicationBuilder)
      Endpoints: Endpoint[]
      Services: (IServiceCollection -> IServiceCollection)
      UseDefaults: bool }
```

- [x] T004 [US4] Update `WebHostSpec.Empty` to include `BeforeRoutingMiddleware=id` in src/Frank/Builder.fs (line ~225)

### WebHostBuilder.Run Fix

- [x] T005 [US1] [US2] [US3] [US4] Fix middleware pipeline ordering in `WebHostBuilder.Run` method in src/Frank/Builder.fs (lines 241-247)

**Change Required**:
```fsharp
.Configure(fun app ->
    app
    |> spec.BeforeRoutingMiddleware  // NEW: Pre-routing middleware
    |> fun app -> app.UseRouting()
    |> spec.Middleware               // FIX: Post-routing, pre-endpoint
    |> fun app ->
        app.UseEndpoints(fun endpoints ->
            let dataSource = ResourceEndpointDataSource(spec.Endpoints)
            endpoints.DataSources.Add(dataSource))
    |> ignore)
```

### New plugBeforeRouting Operation

- [x] T006 [US4] Add `plugBeforeRouting` custom operation to `WebHostBuilder` in src/Frank/Builder.fs (after `Plug` method, ~line 264)

**Change Required**:
```fsharp
[<CustomOperation("plugBeforeRouting")>]
member __.PlugBeforeRouting(spec, f) =
    { spec with BeforeRoutingMiddleware = spec.BeforeRoutingMiddleware >> f }
```

**Checkpoint**: Core implementation complete. Pipeline is now: plugBeforeRouting → UseRouting → plug → UseEndpoints.

---

## Phase 3: Test Case

**Purpose**: Add authentication middleware test to verify `plug` works correctly

- [x] T007 [P] Create simple authentication test sample in test/Frank.Tests/ or sample/Frank.Auth.Test/

**Test should verify**:
1. Create a Frank app with basic authentication middleware via `plug`
2. Unauthenticated requests to protected endpoint return 401
3. Authenticated requests reach the endpoint handler

---

## Phase 4: Documentation & Version

**Purpose**: Update README and version

- [x] T008 [P] Update README.md with `plugBeforeRouting` documentation

**Add section explaining**:
- When to use `plugBeforeRouting` (HttpsRedirection, StaticFiles, compression)
- When to use `plug` (Authentication, Authorization, CORS)
- Pipeline order diagram

- [x] T009 [P] Increment minor version and reset patch in src/Directory.Build.props

**Change**: If current version is `6.x.y`, change to `6.(x+1).0`

---

## Phase 5: Verification

**Purpose**: Validate all changes work correctly

- [x] T010 Build all projects to verify compilation: `dotnet build`
- [ ] T011 Run Playwright tests for Datastar samples: `dotnet test sample/Frank.Datastar.Tests/` (requires DATASTAR_SAMPLE env)
- [x] T012 Run new authentication test: `dotnet test test/Frank.Tests/`
- [ ] T013 [P] Manually verify Sample app (ResponseCaching, StaticFiles): `dotnet run --project sample/Sample/`
- [ ] T014 [P] Manually verify Frank.Datastar.Basic app (DefaultFiles, StaticFiles): `dotnet run --project sample/Frank.Datastar.Basic/`

**Checkpoint**: All verification tasks should pass.

---

## Phase 6: Polish

**Purpose**: Final cleanup

- [x] T015 Update RELEASE_NOTES.md with feature entry
- [x] T016 Verify no code style issues introduced

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - establishes baseline
- **Core Implementation (Phase 2)**: Depends on Setup
  - T003 → T004 → T005 → T006 (sequential within WebHostSpec/Builder changes)
- **Test Case (Phase 3)**: Can run in parallel with Phase 4 after Phase 2
- **Documentation (Phase 4)**: Can run in parallel with Phase 3 after Phase 2
- **Verification (Phase 5)**: Depends on Phases 2, 3, 4
- **Polish (Phase 6)**: Depends on Verification passing

### Task Flow

```
T001 ──┐
       ├──► T003 → T004 → T005 → T006 ──┬──► T010 → T011 → T012 ──► T015
T002 ──┘                                 │                          T016
                                         ├──► T007 (test) ──────────┘
                                         ├──► T008 (readme) [P]
                                         └──► T009 (version) [P]
                                               T013 (sample verify) [P]
                                               T014 (datastar verify) [P]
```

### Parallel Opportunities

- T001 and T002 can run in parallel (pre-change verification)
- T007, T008, T009 can run in parallel after Phase 2 (independent work)
- T013 and T014 can run in parallel (independent sample apps)
- T015 and T016 can run in parallel (independent polish tasks)

---

## Implementation Strategy

### MVP (Minimum Viable Fix)

1. T001 → T002 (baseline)
2. T003 → T004 → T005 → T006 (core implementation)
3. T010 → T011 (automated verification)
4. **STOP**: Fix is validated, can ship

### Full Completion

1. Complete MVP
2. T007 (authentication test)
3. T008 (README), T009 (version) in parallel
4. T012, T013, T014 (full verification)
5. T015, T016 (polish)

---

## User Story Coverage

| User Story | Tasks | Description |
|------------|-------|-------------|
| US1 - Middleware Before Endpoints | T005 | Core pipeline fix |
| US2 - Existing Middleware Works | T005, T011-T014 | Fix + verification |
| US3 - Conditional Middleware Works | T005 | Same fix (composition) |
| US4 - plugBeforeRouting | T003, T004, T005, T006, T008 | New API + docs |

---

## Notes

- Files to modify: `src/Frank/Builder.fs`, `src/Directory.Build.props`, `README.md`
- New file: Authentication test (location TBD)
- All three user stories (US1, US2, US3) addressed by single pipeline fix
- US4 adds new `plugBeforeRouting` operation
- Commit message: "feat: add plugBeforeRouting and fix middleware ordering"
