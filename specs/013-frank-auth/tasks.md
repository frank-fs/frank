# Tasks: Frank.Auth Resource-Level Authorization

**Input**: Design documents from `/specs/013-frank-auth/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-surface.md

**Tests**: Included — spec explicitly requires "unit and integration tests for all authorization patterns".

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US5)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the Frank.Auth project, test project, and register them in the solution

- [ ] T001 Create Frank.Auth project file at src/Frank.Auth/Frank.Auth.fsproj targeting net8.0;net9.0;net10.0 with project reference to src/Frank/Frank.fsproj (mirror Frank.Datastar.fsproj structure, add PackageTags and Description)
- [ ] T002 Create Frank.Auth.Tests project file at test/Frank.Auth.Tests/Frank.Auth.Tests.fsproj targeting net10.0 with Expecto, YoloDev.Expecto.TestSdk, Microsoft.AspNetCore.TestHost, and project reference to src/Frank.Auth/Frank.Auth.fsproj (mirror Frank.Tests.fsproj structure)
- [ ] T003 Create test entry point at test/Frank.Auth.Tests/Program.fs (copy from test/Frank.Tests/Program.fs)
- [ ] T004 Add Frank.Auth and Frank.Auth.Tests projects to Frank.sln under appropriate solution folders (src/ and test/)
- [ ] T005 Verify solution builds with `dotnet build Frank.sln`

---

## Phase 2: Foundational (Core Extensibility Point)

**Purpose**: Add generic `Metadata` field to `ResourceSpec` in Frank core — MUST be complete before any Frank.Auth work can begin

**CRITICAL**: No user story work can begin until this phase is complete

- [ ] T006 Add `Metadata : (EndpointBuilder -> unit) list` field to `ResourceSpec` record in src/Frank/Builder.fs, update `ResourceSpec.Empty` to include `Metadata = []`, and add `static member AddMetadata(spec, convention: EndpointBuilder -> unit)` to `ResourceBuilder`. Bump VersionPrefix in src/Directory.Build.props from 6.5.0 to 7.0.0 (binary-breaking change due to record field addition).
- [ ] T007 Update `ResourceSpec.Build()` in src/Frank/Builder.fs to use `RouteEndpointBuilder` instead of constructing `RouteEndpoint` directly. For each handler: create `RouteEndpointBuilder(handler, routePattern, 0)`, set `DisplayName`, add `HttpMethodMetadata`, apply all metadata convention functions from `spec.Metadata`, then call `Build()`. This is an internal change — return type and endpoint behavior are unchanged.
- [ ] T008 Add MetadataTests.fs to test/Frank.Tests/ — test that: (a) `ResourceSpec.Empty` has empty metadata list, (b) `AddMetadata` appends convention functions, (c) `Build()` applies convention functions — metadata objects added via `(fun b -> b.Metadata.Add(obj))` appear in endpoint's `EndpointMetadataCollection`, (d) existing resources without metadata produce functionally identical endpoints to current behavior (same HTTP method metadata, display name, route pattern). Add MetadataTests.fs to Frank.Tests.fsproj Compile items
- [ ] T009 Verify all existing tests still pass with `dotnet test test/Frank.Tests/`
- [ ] T010 Verify all existing sample applications still build with `dotnet build Frank.sln`

**Checkpoint**: Core extensibility point is in place. Frank.Auth implementation can now begin.

---

## Phase 3: User Story 5 - Application-Level Auth Configuration (Priority: P1)

**Goal**: Provide `useAuthentication`, `useAuthorization`, and `authorizationPolicy` operations on `WebHostBuilder` so that authentication and authorization services can be registered using Frank's builder syntax.

**Independent Test**: Configure authentication and authorization services via the builder, define a protected resource using `requireAuth`, and verify that authorization is evaluated.

**Why US5 first**: This is the foundational wiring — without `useAuthentication` and `useAuthorization`, no resource-level auth operations can be tested end-to-end.

### Implementation for User Story 5

- [ ] T011 [P] [US5] Create AuthRequirement discriminated union in src/Frank.Auth/AuthRequirement.fs per contracts/api-surface.md (Authenticated, Claim, Role, Policy variants with `[<RequireQualifiedAccess>]`)
- [ ] T012 [P] [US5] Create AuthConfig record and module in src/Frank.Auth/AuthConfig.fs per contracts/api-surface.md (empty, addRequirement, isEmpty functions)
- [ ] T013 [P] [US5] Create EndpointAuth module in src/Frank.Auth/EndpointAuth.fs — implement `applyAuth` function that translates AuthConfig to `(EndpointBuilder -> unit)` convention functions (adding AuthorizeAttribute, AuthorizationPolicy to `EndpointBuilder.Metadata`) and appends them to ResourceSpec.Metadata per research.md R2 translation table
- [ ] T014 [US5] Create WebHostBuilder extensions in src/Frank.Auth/WebHostBuilderExtensions.fs — implement `useAuthentication` (adds authentication services + middleware), `useAuthorization` (adds authorization services + middleware), and `authorizationPolicy` (registers named policy via AuthorizationOptions) per contracts/api-surface.md
- [ ] T015 [US5] Create stub ResourceBuilderExtensions.fs in src/Frank.Auth/ResourceBuilderExtensions.fs with just `requireAuth` custom operation (translates Authenticated requirement via EndpointAuth.applyAuth into an `(EndpointBuilder -> unit)` convention function). Other operations will be added in later user stories
- [ ] T016 [US5] Update Frank.Auth.fsproj Compile items to include all .fs files in correct dependency order: AuthRequirement.fs, AuthConfig.fs, EndpointAuth.fs, ResourceBuilderExtensions.fs, WebHostBuilderExtensions.fs
- [ ] T017 [US5] Write integration tests in test/Frank.Auth.Tests/AuthorizationTests.fs — test that: (a) `useAuthentication` + `useAuthorization` wiring enables the authorization pipeline, (b) a resource with `requireAuth` returns 401 for unauthenticated and 200 for authenticated requests when auth services are configured. Use TestHost pattern from Frank.Tests/MiddlewareOrderingTests.fs
- [ ] T018 [US5] Verify Frank.Auth.Tests pass with `dotnet test test/Frank.Auth.Tests/`

**Checkpoint**: Application-level auth wiring works. `requireAuth` is available as a minimal end-to-end proof.

---

## Phase 4: User Story 1 - Restrict Resource to Authenticated Users (Priority: P1)

**Goal**: `requireAuth` operation on the resource builder rejects unauthenticated requests with 401 and allows authenticated requests through.

**Independent Test**: Define a resource with `requireAuth`, make unauthenticated and authenticated requests, verify 401 and 200 respectively. Also verify a resource without auth is publicly accessible.

**Note**: The `requireAuth` implementation was started in Phase 3 (T015). This phase adds comprehensive tests covering all acceptance scenarios.

### Tests for User Story 1

- [ ] T019 [US1] Add tests to test/Frank.Auth.Tests/AuthorizationTests.fs covering all US1 acceptance scenarios: (a) requireAuth + unauthenticated → 401, (b) requireAuth + authenticated → 200 with handler executed, (c) no auth operations → publicly accessible regardless of auth status. Use ClaimsIdentity/ClaimsPrincipal to simulate authenticated users via test middleware

**Checkpoint**: `requireAuth` is fully tested and working independently.

---

## Phase 5: User Story 2 - Restrict Resource by Claim (Priority: P1)

**Goal**: `requireClaim` operation supports single-value and multi-value claim requirements with correct OR/AND semantics.

**Independent Test**: Define resources with single-value and multi-value claim requirements, verify access is granted/denied based on claim presence.

### Implementation for User Story 2

- [ ] T020 [US2] Add `requireClaim` custom operations (single-value and multi-value overloads) to src/Frank.Auth/ResourceBuilderExtensions.fs — translate Claim requirement via EndpointAuth into convention functions that add AuthorizeAttribute + built AuthorizationPolicy to EndpointBuilder.Metadata
- [ ] T021 [US2] Add tests to test/Frank.Auth.Tests/AuthorizationTests.fs covering all US2 acceptance scenarios: (a) single-value claim match → 200, (b) single-value claim mismatch → 403, (c) multi-value claim with any match → 200, (d) multi-value claim with no match → 403, (e) two separate claim requirements with only one satisfied → 403, (f) two separate claim requirements both satisfied → 200
- [ ] T022 [US2] Verify all tests pass with `dotnet test test/Frank.Auth.Tests/`

**Checkpoint**: Claim-based authorization works with single values, multiple values (OR), and multiple operations (AND).

---

## Phase 6: User Story 3 - Restrict Resource by Role (Priority: P2)

**Goal**: `requireRole` operation requires membership in a named role.

**Independent Test**: Define a resource with `requireRole`, verify users with the role are granted access and users without are rejected.

### Implementation for User Story 3

- [ ] T023 [US3] Add `requireRole` custom operation to src/Frank.Auth/ResourceBuilderExtensions.fs — translate Role requirement via EndpointAuth into convention functions that add AuthorizeAttribute + built AuthorizationPolicy to EndpointBuilder.Metadata
- [ ] T024 [US3] Add tests to test/Frank.Auth.Tests/AuthorizationTests.fs covering US3 acceptance scenarios: (a) user in role → 200, (b) user not in role → 403
- [ ] T025 [US3] Verify all tests pass with `dotnet test test/Frank.Auth.Tests/`

**Checkpoint**: Role-based authorization works independently.

---

## Phase 7: User Story 4 - Restrict Resource by Named Policy (Priority: P2)

**Goal**: `requirePolicy` operation delegates to a named authorization policy registered at the application level.

**Independent Test**: Register a named policy via `authorizationPolicy`, reference it on a resource with `requirePolicy`, verify enforcement.

### Implementation for User Story 4

- [ ] T026 [US4] Add `requirePolicy` custom operation to src/Frank.Auth/ResourceBuilderExtensions.fs — translate Policy requirement into convention function that adds AuthorizeAttribute(name) to EndpointBuilder.Metadata
- [ ] T027 [US4] Add tests to test/Frank.Auth.Tests/AuthorizationTests.fs covering US4 acceptance scenarios: (a) user satisfying named policy → 200, (b) user not satisfying named policy → 403. Register policy via `authorizationPolicy` WebHostBuilder operation
- [ ] T028 [US4] Verify all tests pass with `dotnet test test/Frank.Auth.Tests/`

**Checkpoint**: Named policy authorization works independently.

---

## Phase 8: User Story 6 - Compose Multiple Authorization Requirements (Priority: P2)

**Goal**: Multiple `require*` operations on a single resource use AND semantics — all must pass.

**Independent Test**: Combine `requireAuth`, `requireClaim`, and `requireRole` on one resource, verify all must be satisfied.

### Implementation for User Story 6

- [ ] T029 [US6] Add tests to test/Frank.Auth.Tests/AuthorizationTests.fs covering US6 acceptance scenarios: (a) resource with requireAuth + requireClaim + requireRole, user satisfies all three → 200, (b) same resource, user satisfies only two of three → 403
- [ ] T030 [US6] Verify all tests pass with `dotnet test test/Frank.Auth.Tests/`

**Checkpoint**: Composition of authorization requirements works. All authorization patterns are complete.

---

## Phase 9: Edge Cases & Negative Tests

**Purpose**: Cover edge cases identified in the spec

- [ ] T031 Add edge case tests to test/Frank.Auth.Tests/AuthorizationTests.fs: (a) unauthenticated user accessing a claim-required resource → 401 (not 403), (b) empty claim values list → requires claim type exists with any value, (c) multiple claim operations with same claim type but different values → AND semantics (each evaluated independently), (d) resource referencing an unregistered policy name → returns 500 (ASP.NET Core's InvalidOperationException when policy cannot be resolved)
- [ ] T032 Verify all tests pass with `dotnet test test/Frank.Auth.Tests/`

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and final validation

- [ ] T033 Update README.md with Frank.Auth documentation: overview, installation, usage examples (from quickstart.md), and authorization pattern reference
- [ ] T034 Run full solution build and test suite: `dotnet build Frank.sln && dotnet test Frank.sln`
- [ ] T035 Validate quickstart.md examples compile and work correctly against the implementation

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — can start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories
- **Phase 3 (US5 — App Config)**: Depends on Phase 2 — BLOCKS US1-US4 and US6 (provides the auth pipeline wiring)
- **Phases 4-5 (US1, US2)**: Depend on Phase 3 — P1 stories, implement sequentially
- **Phases 6-8 (US3, US4, US6)**: Depend on Phase 3 — P2 stories, can run in parallel after US1/US2
- **Phase 9 (Edge Cases)**: Depends on Phases 4-8
- **Phase 10 (Polish)**: Depends on all prior phases

### User Story Dependencies

- **US5 (App Config, P1)**: Depends only on Foundational — must be first
- **US1 (requireAuth, P1)**: Depends on US5 (needs auth pipeline wiring + requireAuth from T015)
- **US2 (requireClaim, P1)**: Depends on US5 (needs auth pipeline wiring)
- **US3 (requireRole, P2)**: Depends on US5 — can run in parallel with US1/US2
- **US4 (requirePolicy, P2)**: Depends on US5 — can run in parallel with US1/US2/US3
- **US6 (Composition, P2)**: Depends on US1, US2, US3 (needs all requirement types available)

### Within Each User Story

- Types/models before service logic
- Service logic (EndpointAuth) before builder extensions
- Builder extensions before tests
- Tests validate the full integration

### Parallel Opportunities

- T011, T012, T013 can run in parallel (independent .fs files)
- US3 and US4 can run in parallel after US5 is complete
- T023 and T026 can run in parallel (independent operations in different code paths)

---

## Parallel Example: Phase 3 (US5 Foundation)

```bash
# Launch independent type definitions in parallel:
Task: "T011 [P] [US5] Create AuthRequirement DU in src/Frank.Auth/AuthRequirement.fs"
Task: "T012 [P] [US5] Create AuthConfig record in src/Frank.Auth/AuthConfig.fs"
Task: "T013 [P] [US5] Create EndpointAuth module in src/Frank.Auth/EndpointAuth.fs"

# Then sequentially (depends on above):
Task: "T014 [US5] Create WebHostBuilderExtensions.fs"
Task: "T015 [US5] Create ResourceBuilderExtensions.fs (requireAuth only)"
```

## Parallel Example: P2 Stories After US5

```bash
# After Phase 3 (US5) is complete, these can run in parallel:
Task: "T023 [US3] Add requireRole to ResourceBuilderExtensions.fs"
Task: "T026 [US4] Add requirePolicy to ResourceBuilderExtensions.fs"
```

---

## Implementation Strategy

### MVP First (US5 + US1 Only)

1. Complete Phase 1: Setup (project scaffolding)
2. Complete Phase 2: Foundational (core Metadata extensibility)
3. Complete Phase 3: US5 (auth pipeline wiring + requireAuth stub)
4. Complete Phase 4: US1 (requireAuth comprehensive tests)
5. **STOP and VALIDATE**: Test `requireAuth` end-to-end independently
6. At this point you have a working authorization library with authentication gating

### Incremental Delivery

1. Setup + Foundational + US5 → Auth pipeline is wired
2. Add US1 (requireAuth) → Test independently → First auth pattern working
3. Add US2 (requireClaim) → Test independently → Most versatile auth pattern
4. Add US3 (requireRole) → Test independently → Role-based access
5. Add US4 (requirePolicy) → Test independently → Named policy delegation
6. Add US6 (Composition) → Test independently → All patterns composable
7. Edge cases + Polish → Full coverage and documentation

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- The spec explicitly requests tests — all acceptance scenarios are covered
- US5 is ordered first despite being listed as Story 5 in the spec, because it is the foundational wiring all other stories depend on
- US6 (composition) has no new implementation — it is purely a test phase verifying AND semantics across existing requirement types
- Edge case tests (Phase 9) are separated to avoid bloating individual user story phases
