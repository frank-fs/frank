---
work_package_id: WP05
title: Integration Tests & End-to-End Validation
lane: done
dependencies:
- WP03
subtasks: [T023, T024, T025, T026, T027, T028]
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-002, FR-003, FR-004, FR-005, FR-006, FR-007, FR-008, FR-009, FR-010, FR-011, FR-012, FR-013, FR-014, FR-015]
---

# Work Package Prompt: WP05 -- Integration Tests & End-to-End Validation

## Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP05 --base WP04
```

Depends on WP03 (ConditionalRequestMiddleware) and WP04 (StatechartETagProvider).

---

## Objectives & Success Criteria

- Validate the full conditional request pipeline end-to-end via ASP.NET Core TestHost
- Test all four user stories from spec.md with realistic HTTP request/response cycles
- Test with both statechart-backed resources (StatechartETagProvider) and custom providers
- Verify optimistic concurrency: concurrent client scenario with 412 on stale mutation
- Verify edge cases: resources without providers, DELETE responses, wildcard handling
- All acceptance scenarios from spec.md are covered by at least one integration test

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/008-conditional-request-etags/spec.md` -- All acceptance scenarios and edge cases
- `kitty-specs/008-conditional-request-etags/quickstart.md` -- Usage examples for test setup
- `test/Frank.Tests/` -- Existing test patterns (Expecto + TestHost)

**Key constraints**:
- Tests go in `test/Frank.Tests/ConditionalRequestIntegrationTests.fs` (separate from WP03 unit tests)
- Use ASP.NET Core TestHost (`Microsoft.AspNetCore.TestHost`) matching existing Frank.Tests patterns
- Use Expecto test framework (matching existing Frank.Tests patterns)
- Test fixture: simplified statechart resource with 2-3 states and POST to transition
- Tests must be deterministic -- no timing-dependent assertions
- All tests must be independent (no shared mutable state between tests)

---

## Subtasks & Detailed Guidance

### Subtask T023 -- Create test fixture: statechart resource with ETag middleware in TestHost

**Purpose**: Build a reusable test fixture with a stateful resource, ETag middleware, and TestHost for integration testing.

**Steps**:
1. Create `test/Frank.Tests/ConditionalRequestIntegrationTests.fs`
2. Add to `Frank.Tests.fsproj` before `Program.fs`
3. Define a simple statechart for testing:

```fsharp
module ConditionalRequestIntegrationTests

open System
open System.Net
open System.Net.Http
open Expecto
open Frank
open Frank.Statecharts
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection

// Simple 2-state machine for testing
type ItemState = Active | Completed
type ItemContext = { Name: string; UpdateCount: int }

let itemTransition state event context =
    match state, event with
    | Active, "complete" -> Transitioned(Completed, { context with UpdateCount = context.UpdateCount + 1 })
    | Active, "update" -> Transitioned(Active, { context with UpdateCount = context.UpdateCount + 1 })
    | _ -> TransitionResult.Invalid("Cannot transition from this state")

let contextSerializer (ctx: ItemContext) =
    System.Text.Encoding.UTF8.GetBytes(sprintf "%s|%d" ctx.Name ctx.UpdateCount)
```

4. Define a helper to create a TestHost with the full middleware pipeline:

```fsharp
let createTestHost () =
    // Build a WebHostBuilder with:
    // - ETagCache registered
    // - StatechartETagProvider registered
    // - ConditionalRequestMiddleware registered via plug
    // - A stateful resource at "/items/{itemId}"
    // Return TestServer and HttpClient
    ...
```

**Files**: `test/Frank.Tests/ConditionalRequestIntegrationTests.fs`, `test/Frank.Tests/Frank.Tests.fsproj` (modified)
**Notes**:
- The test fixture uses a minimal 2-state machine (`Active` -> `Completed`) with a context that tracks updates
- Each test should create its own TestHost to ensure isolation (no shared state)
- The `contextSerializer` must be deterministic for ETag stability
- Include both GET (read state) and POST (trigger transition) endpoints

### Subtask T024 -- Test User Story 1: ETag generation on GET responses

**Purpose**: Verify that GET responses include ETag headers and that ETags change on state transitions.

**Steps**:
1. Write tests covering acceptance scenarios from User Story 1:

**Scenario 1.1**: Given ETag middleware registered and resource in `Active` state, when GET arrives, then response includes `ETag` header derived from current state.

```fsharp
testAsync "GET response includes ETag header for statechart resource" {
    use server = createTestHost()
    let client = server.CreateClient()
    // Initialize resource instance (e.g., POST to create)
    // GET /items/1
    let! response = client.GetAsync("/items/1") |> Async.AwaitTask
    Expect.equal response.StatusCode HttpStatusCode.OK "200"
    let etag = response.Headers.ETag
    Expect.isNotNull etag "ETag header should be present"
    Expect.isTrue (etag.Tag.Length > 0) "ETag should have a value"
}
```

**Scenario 1.2**: Given state transition to `Completed`, when GET arrives, then ETag value differs from previous.

```fsharp
testAsync "ETag changes after state transition" {
    use server = createTestHost()
    let client = server.CreateClient()
    // GET /items/1 -> ETag A
    let! resp1 = client.GetAsync("/items/1") |> Async.AwaitTask
    let etagA = resp1.Headers.ETag.Tag
    // POST /items/1 (trigger "complete" transition)
    let! _ = client.PostAsync("/items/1", ...) |> Async.AwaitTask
    // GET /items/1 -> ETag B
    let! resp2 = client.GetAsync("/items/1") |> Async.AwaitTask
    let etagB = resp2.Headers.ETag.Tag
    Expect.notEqual etagA etagB "ETag should change after transition"
}
```

**Scenario 1.3**: First GET to new instance includes ETag from initial state.

**Scenario 1.4**: Resource without `IETagProvider` has no ETag header (tested in T028).

**Files**: `test/Frank.Tests/ConditionalRequestIntegrationTests.fs`

### Subtask T025 -- Test User Story 2: Conditional GET with 304 Not Modified

**Purpose**: Verify If-None-Match handling for GET requests.

**Steps**:
1. Write tests covering acceptance scenarios from User Story 2:

**Scenario 2.1**: GET with `If-None-Match` matching current ETag returns 304 with no body.

```fsharp
testAsync "Conditional GET with matching If-None-Match returns 304" {
    use server = createTestHost()
    let client = server.CreateClient()
    // GET /items/1 -> 200 with ETag
    let! resp1 = client.GetAsync("/items/1") |> Async.AwaitTask
    let etag = resp1.Headers.ETag.Tag
    // GET /items/1 with If-None-Match: <etag> -> 304
    let request = new HttpRequestMessage(HttpMethod.Get, "/items/1")
    request.Headers.IfNoneMatch.ParseAdd(etag)
    let! resp2 = client.SendAsync(request) |> Async.AwaitTask
    Expect.equal resp2.StatusCode HttpStatusCode.NotModified "304"
    let! body = resp2.Content.ReadAsStringAsync() |> Async.AwaitTask
    Expect.equal body "" "no body on 304"
}
```

**Scenario 2.2**: GET with old `If-None-Match` after state change returns 200 with new ETag.

**Scenario 2.3**: GET with `If-None-Match: *` on existing resource returns 304.

**Scenario 2.4**: GET with multiple ETags in `If-None-Match` -- any match triggers 304.

```fsharp
testAsync "Conditional GET with multiple ETags in If-None-Match" {
    // Send If-None-Match with "old1", "old2", "<current>"
    // Should return 304 because <current> matches
}
```

**Scenario 2.5**: Resource without provider ignores `If-None-Match` (tested in T028).

**Files**: `test/Frank.Tests/ConditionalRequestIntegrationTests.fs`

### Subtask T026 -- Test User Story 3: Optimistic concurrency via If-Match

**Purpose**: Verify If-Match handling for mutation requests and the optimistic concurrency pattern.

**Steps**:
1. Write tests covering acceptance scenarios from User Story 3:

**Scenario 3.1**: POST with `If-Match` matching current ETag proceeds normally.

```fsharp
testAsync "POST with matching If-Match proceeds normally" {
    use server = createTestHost()
    let client = server.CreateClient()
    // GET /items/1 -> ETag
    let! resp1 = client.GetAsync("/items/1") |> Async.AwaitTask
    let etag = resp1.Headers.ETag.Tag
    // POST /items/1 with If-Match: <etag> -> 200
    let request = new HttpRequestMessage(HttpMethod.Post, "/items/1")
    request.Headers.IfMatch.ParseAdd(etag)
    request.Content <- new StringContent("{\"event\":\"update\"}")
    let! resp2 = client.SendAsync(request) |> Async.AwaitTask
    Expect.equal resp2.StatusCode HttpStatusCode.OK "200"
}
```

**Scenario 3.2**: POST with stale `If-Match` returns 412 (the core optimistic concurrency test).

```fsharp
testAsync "Optimistic concurrency: stale If-Match returns 412" {
    use server = createTestHost()
    let client = server.CreateClient()
    // Both clients GET /items/1 -> same ETag "abc"
    let! resp1 = client.GetAsync("/items/1") |> Async.AwaitTask
    let etag = resp1.Headers.ETag.Tag
    // Client A: POST /items/1 with If-Match: "abc" -> 200 (state changes, new ETag)
    let reqA = new HttpRequestMessage(HttpMethod.Post, "/items/1")
    reqA.Headers.IfMatch.ParseAdd(etag)
    reqA.Content <- new StringContent("{\"event\":\"update\"}")
    let! respA = client.SendAsync(reqA) |> Async.AwaitTask
    Expect.equal respA.StatusCode HttpStatusCode.OK "Client A succeeds"
    // Client B: POST /items/1 with If-Match: "abc" (stale!) -> 412
    let reqB = new HttpRequestMessage(HttpMethod.Post, "/items/1")
    reqB.Headers.IfMatch.ParseAdd(etag)
    reqB.Content <- new StringContent("{\"event\":\"update\"}")
    let! respB = client.SendAsync(reqB) |> Async.AwaitTask
    Expect.equal respB.StatusCode HttpStatusCode.PreconditionFailed "Client B rejected"
}
```

**Scenario 3.3**: PUT with `If-Match: *` on existing resource proceeds.

**Scenario 3.4**: POST without `If-Match` proceeds normally.

**Files**: `test/Frank.Tests/ConditionalRequestIntegrationTests.fs`

### Subtask T027 -- Test User Story 4: Custom IETagProvider on plain resource

**Purpose**: Verify that custom `IETagProvider` implementations work identically to the statechart provider.

**Steps**:
1. Define a mock `IETagProvider` with configurable behavior:

```fsharp
type VersionedETagProvider() =
    let mutable versions = Map.empty<string, int>

    member _.SetVersion(instanceId, version) =
        versions <- Map.add instanceId version

    interface IETagProvider with
        member _.ComputeETag(instanceId) = task {
            match Map.tryFind instanceId versions with
            | Some version ->
                let data = System.Text.Encoding.UTF8.GetBytes(sprintf "%s:%d" instanceId version)
                return Some (ETagFormat.computeFromBytes data)
            | None -> return None
        }
```

2. Create a TestHost with a plain resource (not statefulResource) using this custom provider.

3. Write tests:
- GET includes ETag from custom provider
- Conditional GET with matching ETag returns 304
- Provider state change -> new ETag on subsequent GET
- Resource with no provider has no ETag (already covered in T028)

**Files**: `test/Frank.Tests/ConditionalRequestIntegrationTests.fs`
**Notes**:
- This tests the `IETagProvider` abstraction works independently of Frank.Statecharts
- The custom provider uses `ETagFormat.computeFromBytes` directly
- The test validates that the middleware is provider-agnostic

### Subtask T028 -- Test edge cases

**Purpose**: Cover edge cases from the spec's edge cases section.

**Steps**:
1. Write tests for the following edge cases:

**a. Resource without IETagProvider**:
- GET to a resource with no ETagMetadata: 200 with no ETag header, no conditional processing
- GET with If-None-Match to non-ETag resource: header ignored, 200 returned

**b. DELETE responses**:
- After successful DELETE, verify behavior (no ETag on response if resource no longer exists)

**c. ETag format validation**:
- Verify ETag is always a quoted string matching `"[0-9a-f]{32}"`
- Verify ETag is exactly 34 characters (32 hex + 2 quotes)

**d. 304 response headers**:
- Verify 304 includes ETag header
- Verify 304 has no Content-Type header and no body

**e. First request to new instance**:
- GET to a resource instance that has never been accessed: ETag derived from initial state

**f. Multiple ETags in If-None-Match**:
- Send 3+ ETags, only last one matches: returns 304
- Send 3+ ETags, none match: returns 200

**Files**: `test/Frank.Tests/ConditionalRequestIntegrationTests.fs`

---

## Test Strategy

- Run `dotnet build Frank.sln` to verify everything compiles
- Run `dotnet test test/Frank.Tests/` to verify all integration tests pass
- Verify that all acceptance scenarios from spec.md User Stories 1-4 have corresponding tests
- Verify that edge cases from spec.md are covered

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| TestHost setup complexity for statechart resources | Reference existing Frank.Statecharts test patterns; start with simple 2-state machine |
| Test isolation: shared MailboxProcessor state between tests | Each test creates its own TestHost (new DI container, new cache, new store) |
| Async timing: fire-and-forget cache operations may not complete before assertion | Use GetETag (blocking) after SetETag to verify; or small Async.Sleep |
| HttpRequestMessage disposal | Use `use` bindings for all request/response objects |
| TestHost disposes middleware on test completion | Ensure middleware IDisposable is clean |

---

## Review Guidance

- Verify every acceptance scenario from spec.md has at least one corresponding test
- Verify optimistic concurrency scenario tests the full flow: two clients, one succeeds, one gets 412
- Verify custom provider test does NOT depend on Frank.Statecharts (validates provider abstraction)
- Verify edge case tests cover: no provider, DELETE, format validation, first request, multiple ETags
- Verify test isolation: each test creates its own TestHost
- Verify all tests are deterministic (no timing-dependent assertions)
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
