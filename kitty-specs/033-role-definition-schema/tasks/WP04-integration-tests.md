---
work_package_id: WP04
title: Integration Tests
lane: "doing"
dependencies: [WP03]
base_branch: 033-role-definition-schema-WP03
base_commit: 9f3cefd11303950a30be8c0fec5b4b323e805bde
created_at: '2026-03-22T01:43:35.015332+00:00'
subtasks:
- T019
- T020
- T021
- T022
- T023
- T024
phase: Phase 3 - Validation
assignee: ''
agent: "claude-opus-wp04"
shell_pid: "11112"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-21T18:59:09Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-002, FR-003, FR-004, FR-005, FR-009, FR-010]
---

# Work Package Prompt: WP04 – Integration Tests

## ⚠️ IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

```bash
spec-kitty implement WP04 --base WP03
```

Depends on WP03 (needs full middleware pipeline for integration tests).

---

## Objectives & Success Criteria

- Comprehensive test coverage for the full role lifecycle: declaration → resolution → guard integration → backward compatibility → error handling
- `dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"` passes with all new tests green
- Tests follow existing Expecto + TestHost patterns in `test/Frank.Statecharts.Tests/`
- Each test is independently runnable and creates its own isolated test host

## Context & Constraints

- **Constitution**: All public APIs MUST have tests (Technical Standards)
- **Testing patterns**: Expecto `testTask` for async, `testCase` for pure assertions
- **Key file**: `test/Frank.Statecharts.Tests/StatefulResourceTests.fs` — existing TicTacToe-style tests to follow
- **Type annotations**: F# needs explicit types on `let!` bindings in task CEs: `let! (resp: HttpResponseMessage) = client.SendAsync(req)`
- **`use` in task CEs**: Requires `IAsyncDisposable`. `IHost`/`IDisposable` types need `let` not `use` in `task { }`
- **`ResourceEndpointDataSource` is internal**: Tests create their own `EndpointDataSource` subclass
- **Quickstart**: [quickstart.md](../quickstart.md) — target API patterns for tests

## Subtasks & Detailed Guidance

### Subtask T019 – Test role declaration via CE

- **Purpose**: Verify that `role` declarations in the CE produce correct metadata on endpoints.
- **Steps**:
  1. In `test/Frank.Statecharts.Tests/`, add a new test (in existing test file or new module — follow existing organization)
  2. Define a minimal stateful resource with roles:
     ```fsharp
     let resource = statefulResource "/test/{id}" {
         machine testMachine
         resolveInstanceId (fun ctx -> ctx.Request.RouteValues["id"] :?> string)
         role "RoleA" (fun claims -> claims.HasClaim("role", "a"))
         role "RoleB" (fun claims -> claims.HasClaim("role", "b"))
         inState (forState Initial [
             get (RequestDelegate(fun ctx -> Task.CompletedTask))
         ])
     }
     ```
  3. Register the resource and extract `StateMachineMetadata` from endpoint metadata
  4. Assert:
     - `metadata.Roles |> List.length` = 2
     - `metadata.Roles |> List.map (fun r -> r.Name)` contains "RoleA" and "RoleB"
     - Role predicates are callable: `metadata.Roles[0].ClaimsPredicate(someClaimsPrincipal)` returns expected result
- **Files**: `test/Frank.Statecharts.Tests/` (specific file depends on existing organization)
- **Parallel?**: Yes — independent test function
- **Notes**: You need a minimal `StateMachine<'S,'E,'C>` for the test. Look at existing tests for test machine definitions. The state machine can have a single state with no transitions — the test focuses on role metadata, not statechart behavior.

### Subtask T020 – Test duplicate role name rejection

- **Purpose**: Verify startup fails with a descriptive error for duplicate role names (FR-005).
- **Steps**:
  1. Define a resource with duplicate role names:
     ```fsharp
     let duplicateResource () =
         statefulResource "/test/{id}" {
             machine testMachine
             resolveInstanceId (fun ctx -> "1")
             role "SameName" (fun _ -> true)
             role "SameName" (fun _ -> false)
             inState (forState Initial [
                 get (RequestDelegate(fun ctx -> Task.CompletedTask))
             ])
         }
     ```
  2. Use `testCase` (not `testTask` — this is synchronous)
  3. Assert that evaluating the CE throws an exception containing "Duplicate role names" and the route template
  4. Use `Expect.throws` or `Expect.throwsT<exn>` (check Expecto API)
- **Files**: `test/Frank.Statecharts.Tests/`
- **Parallel?**: Yes — independent test function
- **Notes**: The exception is thrown during CE evaluation (`Run()` method), not during HTTP request processing. No TestHost needed for this test. Also test empty role name rejection if implemented in WP02.

### Subtask T021 – Test role resolution with different identities

- **Purpose**: Verify correct role sets are resolved for various user identities.
- **Steps**:
  1. Create a stateful resource with 3 roles:
     - "RoleA": matches claim `("role", "a")`
     - "RoleB": matches claim `("role", "b")`
     - "Observer": matches all (`fun _ -> true`)
  2. Set up TestHost with the resource
  3. Test scenarios:

     **Scenario A: User matches two roles**
     - Create request with claims `[Claim("role", "a")]`
     - Send GET request
     - Handler reads `IRoleFeature` from `ctx.Features` and writes role names to response body
     - Assert response contains "RoleA" and "Observer" (not "RoleB")

     **Scenario B: User matches no specific roles**
     - Create request with no claims (anonymous or empty identity)
     - Assert response contains only "Observer"

     **Scenario C: User matches all roles**
     - Create request with claims `[Claim("role", "a"); Claim("role", "b")]`
     - Assert response contains "RoleA", "RoleB", and "Observer"
  4. For claims setup, create `ClaimsPrincipal(ClaimsIdentity([Claim("role","a")], "test"))` — the auth type string "test" marks the identity as authenticated
- **Files**: `test/Frank.Statecharts.Tests/`
- **Parallel?**: Yes — independent test function
- **Notes**:
  - The handler needs to access `ctx.Features.Get<IRoleFeature>()` and write the resolved roles to the response. This proves end-to-end resolution.
  - For anonymous users (no auth type on `ClaimsIdentity`), use `ClaimsPrincipal(ClaimsIdentity())` — `IsAuthenticated` will be false, but `ctx.User` is still non-null.
  - Use `testTask` for these tests (async HTTP operations).

### Subtask T022 – Test `HasRole` in guards

- **Purpose**: Verify guards can use `ctx.HasRole` and it correctly allows/blocks requests.
- **Steps**:
  1. Create a stateful resource with:
     - Role "Allowed" matching claim `("access", "yes")`
     - A guard that checks `ctx.HasRole "Allowed"` and returns `Blocked` if role not held
  2. Set up TestHost
  3. Test scenarios:

     **Scenario A: User holds required role**
     - Send request with claim `("access", "yes")`
     - Assert 200 OK (guard allows)

     **Scenario B: User does not hold required role**
     - Send request without the claim
     - Assert 403 Forbidden (or whatever status the guard's `BlockReason` maps to)

     **Scenario C: Guard checks role that was never declared**
     - Guard checks `ctx.HasRole "NonexistentRole"`
     - Assert `HasRole` returns false (guard blocks or allows based on its logic)
  4. The guard definition:
     ```fsharp
     let accessGuard: Guard<TestState, TestEvent, TestContext> =
         AccessControl(
             "AccessGuard",
             fun ctx ->
                 if ctx.HasRole "Allowed" then Allowed
                 else Blocked (Custom "Access denied"))
     ```
- **Files**: `test/Frank.Statecharts.Tests/`
- **Parallel?**: Yes — independent test function
- **Notes**: Check existing guard tests for the pattern of defining guards and verifying HTTP status codes. The `BlockReason` → HTTP status mapping is in the middleware — follow existing conventions.

### Subtask T023 – Test backward compatibility

- **Purpose**: Verify existing `statefulResource` definitions without roles continue to work (FR-010).
- **Steps**:
  1. Use an existing test that defines a `statefulResource` WITHOUT any `role` declarations
  2. Verify:
     - Resource starts successfully
     - GET/POST requests work as before
     - `ctx.Features.Get<IRoleFeature>()` returns null (feature not registered)
     - Guards receive `Roles = Set.empty`
     - `HasRole` returns `false` for any role name
  3. If an existing test already covers this, annotate it with a comment and add one additional assertion for `IRoleFeature` being null
- **Files**: `test/Frank.Statecharts.Tests/`
- **Parallel?**: Yes — independent test function
- **Notes**: This test may be as simple as verifying that existing tests still pass (they should from WP01-WP03). The additional value is explicitly testing `IRoleFeature` absence and `HasRole` returning false.

### Subtask T024 – Test predicate exception handling

- **Purpose**: Verify graceful degradation when a role predicate throws.
- **Steps**:
  1. Create a stateful resource with:
     - "GoodRole" matching `fun _ -> true`
     - "BadRole" with `fun _ -> failwith "predicate error"`
  2. Set up TestHost
  3. Send a request
  4. Assert:
     - Request succeeds (not a 500)
     - `IRoleFeature.Roles` contains "GoodRole" but NOT "BadRole"
     - The exception was logged (check test logger if available, or verify the request completes successfully)
  5. Handler reads roles from `IRoleFeature` and writes to response for assertion
- **Files**: `test/Frank.Statecharts.Tests/`
- **Parallel?**: Yes — independent test function
- **Notes**: The per-role exception handling in `ResolveRoles` (WP02) catches the exception, logs it, and skips the role. This test proves that behavior end-to-end. If the test framework doesn't support capturing log output, the minimum assertion is: request succeeds and the failing role is not in the resolved set.

## Risks & Mitigations

- **Test isolation**: Each test creates its own `TestHost` — no shared mutable state. This prevents test ordering dependencies.
- **TestHost cleanup**: `IHost` is `IDisposable` but NOT `IAsyncDisposable`. Use `let` binding (not `use`) in `task { }` blocks. Call `host.StopAsync()` explicitly if needed.
- **Claims setup**: Use `ClaimsIdentity([claims], "test")` — the auth type string "test" is required for `IsAuthenticated` to return true. Without it, claim checks may behave differently.
- **Guard status code mapping**: Verify the `BlockReason` → HTTP status code mapping in existing tests before writing T022. The mapping may vary (403 for access denied, 409 for conflict, etc.).

## Review Guidance

- Verify each test is independently runnable (no shared state)
- Verify tests cover all 5 edge cases from the spec (duplicates, exceptions, zero roles, anonymous, multi-role)
- Verify type annotations on `let!` bindings in task CEs
- Verify `let` (not `use`) for `IHost` in task CEs
- Verify test claims use auth type string (second arg to `ClaimsIdentity`)
- Run `dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"` — all tests (existing + new) must pass

## Activity Log

- 2026-03-21T18:59:09Z – system – lane=planned – Prompt created.
- 2026-03-22T01:43:35Z – claude-opus-wp04 – shell_pid=11112 – lane=doing – Assigned agent via workflow command
