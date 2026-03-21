# Work Packages: Role Definition Schema

**Inputs**: Design documents from `kitty-specs/033-role-definition-schema/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Included as WP04 — integration tests validating full role lifecycle.

**Organization**: 24 subtasks (`T001`–`T024`) across 4 work packages (`WP01`–`WP04`). Linear dependency chain: WP01 → WP02 → WP03 → WP04.

---

## Work Package WP01: Foundation Types & Compilation Fixes (Priority: P0)

**Goal**: Add all new types and fields across 3 assemblies. Update all construction sites with default values so the project compiles cleanly after this WP.
**Independent Test**: `dotnet build Frank.sln` succeeds. All existing tests pass unchanged.
**Prompt**: `tasks/WP01-foundation-types.md`
**Estimated Size**: ~350 lines

### Included Subtasks
- [ ] T001 [P] Add `RoleInfo` type to `src/Frank.Resources.Model/ResourceTypes.fs`
- [ ] T002 Add `Roles: RoleInfo list` field to `ExtractedStatechart` in `src/Frank.Resources.Model/ResourceTypes.fs`
- [ ] T003 Update `StatechartExtractor.toExtractedStatechart` factory to accept `roles` parameter
- [ ] T004 [P] Update call sites in `UnifiedExtractor.fs` and `StatechartSourceExtractor.fs` to pass `Roles = []`
- [ ] T005 [P] Add `RoleDefinition` type to `src/Frank.Statecharts/Types.fs`
- [ ] T006 Add `Roles: Set<string>` field + `HasRole` member to `AccessControlContext` in `Types.fs`; update `evaluateGuards` closure construction with `Roles = Set.empty`
- [ ] T007 Add `Roles: Set<string>` field + `HasRole` member to `EventValidationContext` in `Types.fs`; update `evaluateEventGuards` closure construction with `Roles = Set.empty`
- [ ] T008 [P] Add `IRoleFeature` interface + `SetRoles` extension method to `src/Frank.Statecharts/StatechartFeature.fs`

### Implementation Notes
- All new record fields initialize with empty defaults (`[]`, `Set.empty`) at existing construction sites
- The project MUST compile after this WP — no broken construction sites
- `RoleInfo` in `Frank.Resources.Model` has zero dependencies (same assembly as `StateInfo`)
- `RoleDefinition` in `Frank.Statecharts` depends on `System.Security.Claims.ClaimsPrincipal`
- `IRoleFeature` follows the same pattern as `IStatechartFeature` but is non-generic (no `'S`, `'C` type parameters)
- `HasRole` is a member method on the record, not an extension — preserves structural equality

### Parallel Opportunities
- T001 (RoleInfo), T005 (RoleDefinition), T008 (IRoleFeature) touch different files and can proceed in parallel
- T004 (CLI call sites) is independent of T006/T007 (guard context updates)
- T002 must precede T003/T004 (ExtractedStatechart field → factory → call sites)
- T006/T007 must update the closures in `StatefulResourceBuilder.fs` that construct guard contexts

### Dependencies
- None (starting package).

### Risks & Mitigations
- **F# compile order**: New types in `Types.fs` must be defined BEFORE they're referenced. `RoleDefinition` needs `ClaimsPrincipal` open — verify import order.
- **Record breaking changes**: Adding fields to F# records breaks all construction sites. Mitigation: WP01 explicitly updates every construction site with defaults.
- **Factory function signature**: `StatechartExtractor.toExtractedStatechart` gains a parameter. Both call sites (UnifiedExtractor, StatechartSourceExtractor) must be updated in the same WP.

---

## Work Package WP02: CE Builder & Metadata Construction (Priority: P1) 🎯 MVP

**Goal**: Wire up the `role` custom operation on `StatefulResourceBuilder`, validate role names at startup, and produce `StateMachineMetadata` with role data and a `ResolveRoles` closure.
**Independent Test**: A `statefulResource` with `role` declarations compiles, starts without error, and roles appear in endpoint metadata.
**Prompt**: `tasks/WP02-ce-builder-and-metadata.md`
**Estimated Size**: ~400 lines

### Included Subtasks
- [ ] T009 Add `Roles: RoleDefinition list` to `StatefulResourceSpec<'S,'E,'C>` + update `Yield()` with `Roles = []`
- [ ] T010 Add `role` custom operation to `StatefulResourceBuilder` (name + predicate → append to `spec.Roles`)
- [ ] T011 Add duplicate role name validation in `Run()` — fail-fast with descriptive error if duplicate names detected
- [ ] T012 Add `Roles: RoleDefinition list` + `ResolveRoles: HttpContext -> Set<string>` to `StateMachineMetadata`; update metadata construction with defaults
- [ ] T013 Create `ResolveRoles` closure in `Run()` — evaluate predicates against `ctx.User`, catch exceptions per-role (log via `ILogger`), return `Set<string>` of matching role names
- [ ] T014 Extract role names as `RoleInfo list` from `spec.Roles` for `StateMachineMetadata.Roles` and wire into `ExtractedStatechart`-compatible metadata

### Implementation Notes
- `role` custom operation follows the same pattern as `onTransition`: takes spec, appends to list, returns updated spec
- `Run()` validation should check for duplicates BEFORE constructing closures — fail-fast
- `ResolveRoles` closure pattern: iterate `roles`, evaluate each `role.ClaimsPredicate(ctx.User)` inside try/catch, collect matching names into `Set<string>`
- Exception handling: catch per-role, log role name + exception via `ILogger`, do not resolve that role, continue with remaining roles (constitution VII, spec edge case: predicate exceptions)
- `RoleInfo` extraction: `spec.Roles |> List.map (fun r -> { Name = r.Name; Description = None })` — descriptions can be added later

### Parallel Opportunities
- T009/T010 (CE accumulator + operation) and T012 (metadata type) can start in parallel since they modify different parts of `StatefulResourceBuilder.fs`
- T011, T013, T014 depend on T009 and T012

### Dependencies
- Depends on WP01 (needs `RoleDefinition`, `RoleInfo`, `IRoleFeature` types).

### Risks & Mitigations
- **CE custom operation overload**: The `role` operation takes `(string * (ClaimsPrincipal -> bool))`. If F# has trouble resolving the overload, may need explicit type annotation on the `CustomOperation` method.
- **ILogger access in closure**: The `ResolveRoles` closure needs `ILogger`. Pattern: resolve `ILoggerFactory` from `IServiceProvider` inside the closure (same as existing `GetCurrentStateKey` pattern), or capture logger in the closure via `Run()`.

---

## Work Package WP03: Middleware Role Resolution (Priority: P2)

**Goal**: Wire role resolution into the stateful resource middleware pipeline. After this WP, roles are resolved per-request, cached on `IRoleFeature`, and available to guards via `ctx.Roles` / `ctx.HasRole`.
**Independent Test**: Send request to a stateful resource with roles declared. Verify guards can access `ctx.HasRole` and it returns correct values.
**Prompt**: `tasks/WP03-middleware-role-resolution.md`
**Estimated Size**: ~300 lines

### Included Subtasks
- [ ] T015 Add role resolution step in `Middleware.fs` — call `meta.ResolveRoles(ctx)` between state resolution (step 1) and guard evaluation (step 3); cache result via `ctx.SetRoles(resolvedRoles)`
- [ ] T016 Update `evaluateGuards` closure to read `IRoleFeature` from `ctx.Features` and populate `AccessControlContext.Roles` with resolved role set
- [ ] T017 Update `evaluateEventGuards` closure to read `IRoleFeature` from `ctx.Features` and populate `EventValidationContext.Roles` with resolved role set
- [ ] T018 Handle edge cases: skip role resolution when `meta.Roles` is empty (backward compatibility); handle `IRoleFeature` being null when no roles declared (guards get `Set.empty`)

### Implementation Notes
- Role resolution placement: AFTER state resolution (step 1), BEFORE method filtering (step 2). This ensures roles are available to all subsequent steps.
- `SetRoles` extension method (from WP01) registers `IRoleFeature` on `HttpContext.Features`
- For backward compatibility: if `meta.Roles` is empty, skip `ResolveRoles` call entirely. Guards should receive `Roles = Set.empty` (not null).
- The `evaluateGuards` closure already reads `IStatechartFeature<'S,'C>` from features. Add a second feature read for `IRoleFeature` — pattern: `let roleFeature = ctx.Features.Get<IRoleFeature>()`; handle null case with `Set.empty`.

### Parallel Opportunities
- T016 and T017 modify different closures in the same file but can be done sequentially within the same session
- T015 and T018 are tightly coupled (resolution + edge cases)

### Dependencies
- Depends on WP02 (needs `ResolveRoles` closure on `StateMachineMetadata`).

### Risks & Mitigations
- **Middleware pipeline ordering**: Role resolution MUST happen after authentication middleware has populated `ctx.User`. This is the application's responsibility (standard ASP.NET Core ordering), not Frank's. Document this assumption.
- **Null `IRoleFeature`**: When no roles are declared, `ctx.Features.Get<IRoleFeature>()` returns null. Guards must handle this gracefully — default to `Set.empty`.

---

## Work Package WP04: Integration Tests (Priority: P3)

**Goal**: Comprehensive test coverage for the full role lifecycle: declaration → resolution → guard integration → backward compatibility → error handling.
**Independent Test**: `dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"` passes with all new tests green.
**Prompt**: `tasks/WP04-integration-tests.md`
**Estimated Size**: ~400 lines

### Included Subtasks
- [ ] T019 Test role declaration via CE — define `statefulResource` with roles, verify roles appear in `StateMachineMetadata` on endpoint
- [ ] T020 Test duplicate role name rejection — define resource with duplicate role names, verify startup fails with descriptive error
- [ ] T021 Test role resolution — send requests with different `ClaimsPrincipal` identities; verify correct role sets resolved (multi-role, no-role, anonymous)
- [ ] T022 Test `HasRole` in guards — create guard using `ctx.HasRole "RoleName"`, verify allows/blocks based on resolved roles
- [ ] T023 Test backward compatibility — define `statefulResource` with NO role declarations, verify existing behavior unchanged and `HasRole` returns `false`
- [ ] T024 Test predicate exception handling — create role with throwing predicate, verify role is not resolved (graceful degradation) and request proceeds

### Implementation Notes
- Follow existing test patterns in `test/Frank.Statecharts.Tests/StatefulResourceTests.fs`
- Use Expecto `testTask` for async tests with `TestHost`
- Create test claims via `ClaimsPrincipal(ClaimsIdentity([Claim("player","X")], "test"))` — matches TicTacToe pattern
- For T020 (startup failure), catch the exception from resource registration — don't use TestHost for this
- For T024 (exception handling), define a role with `fun _ -> failwith "boom"` predicate
- Type annotations needed: `let! (resp: HttpResponseMessage) = client.SendAsync(req)` in task CEs

### Parallel Opportunities
- T019/T020 (declaration tests) are independent of T021/T022 (runtime tests) — different test functions
- T023/T024 (edge case tests) can be written in parallel with other tests

### Dependencies
- Depends on WP03 (needs full middleware pipeline for integration tests).

### Risks & Mitigations
- **Test isolation**: Each test should create its own `TestHost` and `statefulResource` — no shared mutable state
- **`ResourceEndpointDataSource` is internal**: Tests need their own `EndpointDataSource` subclass (existing pattern in test project)
- **`IHost` is `IDisposable`**: Use `let` not `use` in `task { }` blocks (existing pattern — `IHost` is not `IAsyncDisposable`)

---

## Dependency & Execution Summary

- **Sequence**: WP01 → WP02 → WP03 → WP04 (linear chain)
- **Parallelization**: Within each WP, subtasks marked `[P]` can proceed in parallel. No cross-WP parallelization since each depends on the previous.
- **MVP Scope**: WP01 + WP02 constitute the minimal release — types defined, CE operation works, metadata populated. WP03 adds runtime resolution. WP04 adds test coverage.

---

## Subtask Index (Reference)

| Subtask ID | Summary | Work Package | Priority | Parallel? |
|------------|---------|--------------|----------|-----------|
| T001 | Add RoleInfo type | WP01 | P0 | Yes |
| T002 | Add Roles to ExtractedStatechart | WP01 | P0 | No |
| T003 | Update StatechartExtractor factory | WP01 | P0 | No |
| T004 | Update CLI extraction call sites | WP01 | P0 | Yes |
| T005 | Add RoleDefinition type | WP01 | P0 | Yes |
| T006 | Update AccessControlContext + evaluateGuards | WP01 | P0 | No |
| T007 | Update EventValidationContext + evaluateEventGuards | WP01 | P0 | No |
| T008 | Add IRoleFeature + SetRoles extension | WP01 | P0 | Yes |
| T009 | Add Roles to StatefulResourceSpec + Yield | WP02 | P1 | No |
| T010 | Add role CE custom operation | WP02 | P1 | No |
| T011 | Duplicate role name validation | WP02 | P1 | No |
| T012 | Add Roles + ResolveRoles to StateMachineMetadata | WP02 | P1 | Yes |
| T013 | Create ResolveRoles closure | WP02 | P1 | No |
| T014 | Extract RoleInfo list for metadata | WP02 | P1 | No |
| T015 | Add role resolution step in middleware | WP03 | P2 | No |
| T016 | Update evaluateGuards to populate Roles | WP03 | P2 | No |
| T017 | Update evaluateEventGuards to populate Roles | WP03 | P2 | No |
| T018 | Handle edge cases (empty roles, null feature) | WP03 | P2 | No |
| T019 | Test role declaration via CE | WP04 | P3 | Yes |
| T020 | Test duplicate role name rejection | WP04 | P3 | Yes |
| T021 | Test role resolution | WP04 | P3 | Yes |
| T022 | Test HasRole in guards | WP04 | P3 | Yes |
| T023 | Test backward compatibility | WP04 | P3 | Yes |
| T024 | Test predicate exception handling | WP04 | P3 | Yes |
