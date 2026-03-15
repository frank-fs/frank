---
work_package_id: WP08
title: Integration Tests & End-to-End Validation
lane: "done"
dependencies:
- WP07
base_branch: 005-shacl-validation-from-fsharp-types-WP07
base_commit: 7bcf170ba79f5514022908e832ca9f472cc51ce1
created_at: '2026-03-15T19:42:55.103753+00:00'
subtasks: [T043, T044, T045, T046, T047, T048, T049]
shell_pid: "44454"
agent: "claude-opus"
reviewed_by: "Ryan Riley"
review_status: "approved"
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
- timestamp: '2026-03-14T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Amended per build-time SHACL unification design
amendment_ref: docs/superpowers/specs/2026-03-14-build-time-shacl-unification-design.md
requirement_refs: [FR-001, FR-002, FR-003, FR-004, FR-005, FR-006, FR-007, FR-008, FR-009, FR-010, FR-011, FR-012, FR-013, FR-014, FR-015, FR-016, FR-017, FR-018, FR-019]
---

# Work Package Prompt: WP08 -- Integration Tests & End-to-End Validation

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

## Amendment (2026-03-14): Build-Time SHACL Unification

> This WP is amended per the [build-time SHACL unification design](../../../docs/superpowers/specs/2026-03-14-build-time-shacl-unification-design.md). Scope expands to include:
>
> - **Build-time pipeline tests**: verify a sample project's embedded resource contains expected validation-grade shapes
> - **ShapeLoader tests**: verify deserialization from Turtle to ShaclShape domain values (covered in WP12 but integration-tested here)
> - **End-to-end with Frank.Cli.MSBuild**: build sample project with auto-generation, run it, send requests, verify validation behavior
> - **Missing resource test**: verify Frank.Validation fails fast with clear error when embedded resource is absent
> - **Test projects include Frank.Cli.MSBuild** to exercise the auto-generation pipeline

---

## Implementation Command

```bash
spec-kitty implement WP08 --base WP07
```

Depends on all prior WPs (WP01-WP07, WP09-WP12).

---

## Objectives & Success Criteria

- Full end-to-end integration tests using ASP.NET Core TestHost
- Valid requests pass through to handler and execute normally (SC-002)
- Invalid requests return 422 with structured violation reports (SC-002, SC-003)
- Content negotiation works: JSON-LD, Turtle, Problem Details for same violations (SC-005)
- Capability-dependent shapes produce different outcomes for different principals (SC-006)
- Custom constraints evaluated alongside auto-derived constraints (SC-007)
- Edge cases: recursive types, nested records, empty body, generic types, non-derivable types
- Existing Frank applications without Frank.Validation experience zero changes (SC-008)
- Handler counter confirms zero invocations for invalid requests (SC-002)

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/005-shacl-validation-from-fsharp-types/spec.md` -- All acceptance scenarios from User Stories 1-5, edge cases, success criteria
- `kitty-specs/005-shacl-validation-from-fsharp-types/quickstart.md` -- Example request/response pairs
- `kitty-specs/005-shacl-validation-from-fsharp-types/plan.md` -- Test project structure

**Key constraints**:
- Use ASP.NET Core TestHost (`Microsoft.AspNetCore.TestHost`) matching existing Frank test patterns
- Use Expecto test framework
- Test the full middleware pipeline: Auth -> Validation -> Handler
- All tests must pass on net10.0 (test project target)
- `dotnet build Frank.sln` and `dotnet test` must pass after this WP

---

## Subtasks & Detailed Guidance

### Subtask T043 -- Create `IntegrationTests.fs` with TestHost setup

**Purpose**: Set up the TestHost infrastructure and domain types used by all integration tests.

**Steps**:
1. Create `test/Frank.Validation.Tests/IntegrationTests.fs`
2. Define test domain types:

```fsharp
module IntegrationTests

open System
open Expecto
open Frank.Builder
open Frank.Validation

// Domain types for testing
type CreateCustomer =
    { Name: string
      Email: string
      Age: int
      Notes: string option }

type Address =
    { Street: string
      City: string
      ZipCode: string }

type CustomerWithAddress =
    { Name: string
      Address: Address }

type OrderStatus = Submitted | Processing | Shipped | Cancelled | Refunded

type UpdateOrder =
    { Status: string
      Notes: string option }

type TreeNode =
    { Value: string
      Children: TreeNode list }

type PagedResult<'T> =
    { Items: 'T list
      TotalCount: int
      Page: int }
```

3. Create helper functions for TestHost setup:

```fsharp
let createTestHost (configureResources: WebHostBuilder -> WebHostBuilder) =
    // Set up ASP.NET Core TestHost with Frank resources
    // Register useAuth and useValidation middleware
    // Return HttpClient for test requests
    ...

let mutable handlerCallCount = 0

let countingHandler (ctx: HttpContext) = task {
    System.Threading.Interlocked.Increment(&handlerCallCount) |> ignore
    ctx.Response.StatusCode <- 200
}

let resetCounter () =
    handlerCallCount <- 0
```

4. Add `IntegrationTests.fs` to the test project's `<Compile>` list.

**Files**: `test/Frank.Validation.Tests/IntegrationTests.fs`
**Notes**: Reference existing Frank test projects (e.g., `Frank.LinkedData.Tests`, `Frank.Auth.Tests`) for TestHost setup patterns. The `handlerCallCount` is used to verify handlers are NOT invoked for invalid requests.

### Subtask T044 -- Test valid request passes through to handler

**Purpose**: Verify that a valid request satisfying all SHACL constraints passes through validation and reaches the handler.

**Steps**:
1. Set up a validated resource for `CreateCustomer`:

```fsharp
let app = createTestHost (fun builder ->
    builder {
        useValidation
        resource "/customers" {
            validate typeof<CreateCustomer>
            post countingHandler
        }
    })
```

2. Write tests:

**a. Valid POST with all required fields**:
- Send POST with `{ "Name": "Alice", "Email": "a@b.com", "Age": 30 }`
- Verify response status is 200
- Verify `handlerCallCount = 1`

**b. Valid POST with optional field included**:
- Send POST with `{ "Name": "Alice", "Email": "a@b.com", "Age": 30, "Notes": "VIP" }`
- Verify response status is 200

**c. Valid POST with optional field omitted**:
- Same as (a) -- Notes is absent, which is valid for `option` field
- Verify response status is 200

**Files**: `test/Frank.Validation.Tests/IntegrationTests.fs`
**Notes**: Use `System.Net.Http.StringContent` with `application/json` content type for request bodies.

### Subtask T045 -- Test invalid request returns 422 with ValidationReport

**Purpose**: Verify that requests violating SHACL constraints are rejected with 422 before the handler executes.

**Steps**:
1. Write tests for each violation type:

**a. Missing required field**:
- Send POST missing `Name` field
- Verify 422 status
- Verify `handlerCallCount = 0` (handler never executed)
- Verify response body contains violation for `Name` with `sh:minCount`

**b. Wrong datatype**:
- Send POST with `"Age": "not-a-number"`
- Verify 422 with `sh:datatype` violation on `Age`

**c. Invalid DU value (if using sh:in)**:
- Define resource with DU-constrained field
- Send POST with invalid value
- Verify 422 with `sh:in` violation

**d. Multiple violations**:
- Send POST missing `Name` AND with wrong type for `Age`
- Verify 422 with exactly 2 violations in the report
- Verify each violation has correct resultPath and constraint

**e. Empty body on POST**:
- Send POST with no body (or empty body) to validated endpoint
- Verify 422 (not 400 or 500)
- Verify violations for all required fields

**f. Nested record violation**:
- Use `CustomerWithAddress` type
- Send POST with missing `Address.ZipCode`
- Verify 422 with violation path including nested field

**Files**: `test/Frank.Validation.Tests/IntegrationTests.fs`
**Notes**: The handler counter is critical -- it proves the handler was never invoked. Reset the counter before each test.

### Subtask T046 -- Test content negotiation for violation responses

**Purpose**: Verify the same validation failure produces correct responses for different Accept headers.

**Steps**:
1. Send the same invalid request (missing required field) with different Accept headers:

**a. Accept: application/json**:
- Verify Content-Type is `application/problem+json`
- Verify body is valid RFC 9457 Problem Details
- Verify `type` is `urn:frank:validation:shacl-violation`
- Verify `status` is 422
- Verify `errors` array has correct structure

**b. Accept: application/ld+json**:
- Verify Content-Type is `application/ld+json`
- Verify body contains `sh:ValidationReport` type
- Verify `sh:conforms` is false
- Verify `sh:result` entries match violations

**c. Accept: text/turtle** (if supported by Frank.LinkedData):
- Verify Content-Type is `text/turtle`
- Verify body contains SHACL validation report triples

**d. No Accept header**:
- Verify default response is Problem Details JSON

**e. Accept: */* (wildcard)**:
- Verify response is Problem Details JSON (default for non-semantic clients)

**Files**: `test/Frank.Validation.Tests/IntegrationTests.fs`
**Notes**: Parse JSON responses using `System.Text.Json.JsonDocument`. For JSON-LD, verify the `@context` includes SHACL namespace.

### Subtask T047 -- Test capability-dependent shape validation

**Purpose**: Verify that the same request body produces different validation outcomes based on the authenticated principal's capabilities.

**Steps**:
1. Set up a resource with `validateWithCapabilities`:

```fsharp
// Admin: no restriction on Status
// User: Status must be "Submitted" or "Cancelled"
let orders = createTestHost (fun builder ->
    builder {
        useAuth
        useValidation
        resource "/orders" {
            validateWithCapabilities typeof<UpdateOrder> [
                forClaim "role" ["admin"] (fun shape -> shape)
                forClaim "role" ["user"] (fun shape ->
                    // Restrict Status to Submitted/Cancelled
                    ...)
            ]
            put countingHandler
        }
    })
```

2. Write tests:

**a. Admin POSTs unrestricted value: passes**:
- Authenticate as admin (role=admin)
- Send PUT with `{ "Status": "Refunded" }`
- Verify 200 (validation passes)

**b. Regular user POSTs restricted value: fails with 422**:
- Authenticate as user (role=user)
- Send PUT with `{ "Status": "Refunded" }`
- Verify 422 (NOT 403 -- this is a validation failure, not authorization)
- Verify violation is `sh:in` constraint

**c. Regular user POSTs allowed value: passes**:
- Authenticate as user
- Send PUT with `{ "Status": "Submitted" }`
- Verify 200

**d. Anonymous principal: base shape applied**:
- No authentication
- Send PUT with `{ "Status": "Refunded" }`
- Verify 422 (base shape is most restrictive)

**Files**: `test/Frank.Validation.Tests/IntegrationTests.fs`
**Notes**: Use ASP.NET Core test authentication (`AddAuthentication("Test").AddScheme<TestAuthHandler>("Test", ...)`) to set up mock principals. Verify 422 (not 403) is critical -- capability-dependent validation failures are semantic constraint violations, not authorization failures.

### Subtask T048 -- Test custom constraints

**Purpose**: Verify custom constraints (pattern, minInclusive, cross-field SPARQL) are evaluated alongside auto-derived constraints.

**Steps**:
1. Set up a resource with custom constraints:

```fsharp
let customers = createTestHost (fun builder ->
    builder {
        useValidation
        resource "/customers" {
            validate typeof<CreateCustomer>
            customConstraint "Email" (PatternConstraint @"^[^@]+@[^@]+\.[^@]+$")
            customConstraint "Age" (MinInclusiveConstraint 0)
            customConstraint "Age" (MaxInclusiveConstraint 150)
            post countingHandler
        }
    })
```

2. Write tests:

**a. Pattern violation**:
- Send POST with `"Email": "not-an-email"`
- Verify 422 with pattern violation on Email

**b. Pattern passes**:
- Send POST with `"Email": "alice@example.com"`
- Verify validation passes (if other fields valid)

**c. MinInclusive violation**:
- Send POST with `"Age": -1`
- Verify 422 with minInclusive violation on Age

**d. MaxInclusive violation**:
- Send POST with `"Age": 200`
- Verify 422 with maxInclusive violation on Age

**e. Both auto-derived and custom violations**:
- Send POST missing `Name` (auto-derived minCount) AND with `"Email": "bad"` (custom pattern)
- Verify 422 with BOTH violations in the report

**f. Cross-field SPARQL constraint** (if implemented):
- Define a type with `StartDate` and `EndDate`
- Add SPARQL constraint: endDate > startDate
- Send POST with endDate before startDate
- Verify 422 with cross-field violation

**Files**: `test/Frank.Validation.Tests/IntegrationTests.fs`

### Subtask T049 -- Test edge cases

**Purpose**: Verify correct behavior for edge cases identified in the spec.

**Steps**:
1. Write tests for each edge case:

**a. Recursive type (TreeNode)**:
- Derive shape for `TreeNode = { Value: string; Children: TreeNode list }`
- Verify derivation completes without infinite loop
- Verify shape has `sh:node` reference back to TreeNode

**b. Nested record validation**:
- `CustomerWithAddress` with nested `Address` record
- Send POST with valid outer record but invalid inner record field
- Verify violation path includes nested field

**c. Empty body on validated endpoint**:
- Send POST with no Content-Type and empty body
- Verify 422 with minCount violations for all required fields

**d. Generic type (PagedResult<CreateCustomer>)**:
- Derive shape for `PagedResult<CreateCustomer>`
- Verify concrete shape has Customer-specific Items property

**e. Non-derivable handler type**:
- Resource handler accepting `HttpContext` directly with `validate` enabled
- Verify startup warning logged (FR-017)
- Verify validation is skipped at runtime

**f. Non-validated endpoint**:
- Resource without `validate`
- Send invalid data
- Verify handler still executes (no validation)
- Verify zero overhead from validation middleware

**g. Option types nested in collections**:
- Type with `Tags: string option list`
- Verify shape handles collection-level and element-level constraints

**Files**: `test/Frank.Validation.Tests/IntegrationTests.fs`
**Validation**: `dotnet test test/Frank.Validation.Tests/` passes with all tests green.

---

## Test Strategy

- Run `dotnet build Frank.sln` to verify full solution builds
- Run `dotnet test` for all integration tests
- Verify ALL acceptance scenarios from User Stories 1-5 in spec.md
- Verify ALL success criteria SC-001 through SC-008
- Verify ALL edge cases from spec.md

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| TestHost setup complexity | Reference existing Frank test projects (Frank.Auth.Tests, Frank.LinkedData.Tests) for patterns |
| Test authentication setup | Use standard ASP.NET Core test authentication schemes |
| SPARQL constraint testing requires dotNetRdf SPARQL engine | Verify dotNetRdf.Core includes SPARQL engine (it does, as a transitive dependency) |
| flaky tests due to async middleware | Use proper async/await patterns; avoid timing-dependent assertions |

---

## Review Guidance

- Verify ALL acceptance scenarios from spec.md User Stories 1-5 are covered
- Verify handler counter tests prove zero invocations for invalid requests
- Verify 422 status code (not 400 or 500) for all validation failures
- Verify 422 (not 403) for capability-dependent validation failures
- Verify content negotiation produces correct Content-Type headers
- Verify edge cases: recursive types, empty body, generic types, non-derivable types
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green
- Run existing Frank tests to verify zero behavioral changes (SC-008)

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-15T19:42:55Z – claude-opus – shell_pid=41173 – lane=doing – Assigned agent via workflow command
- 2026-03-15T19:49:36Z – claude-opus – shell_pid=41173 – lane=for_review – Ready for review: 25 integration tests covering valid/invalid flows, content negotiation, capabilities, custom constraints, edge cases
- 2026-03-15T19:49:41Z – claude-opus – shell_pid=44454 – lane=doing – Started review via workflow command
- 2026-03-15T19:55:22Z – claude-opus – shell_pid=44454 – lane=done – Review passed: 25 integration tests covering T043-T049 all verified. 0 build errors, 169 Validation tests pass, 77 existing Frank tests pass (SC-008). Handler counter correctly proves zero invocations for all invalid requests. 422 status consistently used for validation failures. Content negotiation, capability-dependent shapes, custom constraints, and edge cases all properly exercised.
