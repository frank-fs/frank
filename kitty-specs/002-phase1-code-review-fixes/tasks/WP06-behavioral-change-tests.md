---
work_package_id: "WP06"
subtasks:
  - "T031"
  - "T032"
  - "T033"
  - "T034"
  - "T035"
  - "T036"
title: "New Tests for Behavioral Changes"
phase: "Phase 3 - Verification"
lane: "planned"
dependencies:
  - "WP02"
  - "WP03"
  - "WP04"
  - "WP05"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
history:
  - timestamp: "2026-03-06T15:25:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
requirement_refs: [FR-001, FR-002, FR-004, FR-007, FR-008, FR-009, FR-010, FR-011]
---

# Work Package Prompt: WP06 – New Tests for Behavioral Changes

## ⚠️ IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

```bash
spec-kitty implement WP06 --base WP05
```

Depends on WP02, WP03, WP04, WP05 — tests validate behavioral changes from those WPs.

---

## Objectives & Success Criteria

- New tests added for every behavioral change: typed literal serialization, Accept header parsing, cache key uniqueness, null assembly handling, ExtractionState round-trip
- All 265 existing baseline tests continue to pass (SC-001)
- `dotnet test` succeeds across all test projects with zero failures

## Context & Constraints

- **Tracking Issue**: #81 — Acceptance criteria: "New tests added for any behavioral changes"
- **Constitution**: All principles enforced — tests are the verification gate
- **Test framework**: Expecto 10.x + ASP.NET Core TestHost
- **Test projects**:
  - `test/Frank.LinkedData.Tests/` — for LinkedData behavioral changes
  - `test/Frank.Cli.Core.Tests/` — for CLI behavioral changes
- **Success Criteria SC-001/SC-002**: All 265 existing tests pass + new tests for all behavioral changes

## Subtasks & Detailed Guidance

### Subtask T031 – Tests for Multi-Subject JSON-LD Typed Literal Serialization

- **Purpose**: Verify that the `@graph` code path in `JsonLdFormatter` preserves typed literal information (int, bool, double, decimal) — the bug fixed in WP02 T006.
- **Steps**:
  1. Open or create test file in `test/Frank.LinkedData.Tests/` (e.g., `JsonLdFormatterTests.fs`)
  2. Create a test that:
     - Constructs an RDF graph with multiple subjects (triggers `@graph` path)
     - Includes typed literals: integer (xsd:long), boolean (xsd:boolean), double (xsd:double), decimal (xsd:decimal)
     - Serializes to JSON-LD via `JsonLdFormatter`
     - Parses the JSON-LD output
     - Asserts each typed value has `@type` and `@value` annotations
  3. Create a second test verifying single-subject and multi-subject paths produce equivalent type annotations for the same literal
  4. Test edge case: untyped string literals should NOT have `@type` annotation
- **Files**: `test/Frank.LinkedData.Tests/JsonLdFormatterTests.fs` (new or existing)
- **Parallel?**: Yes — independent of T032-T035

### Subtask T032 – Tests for MediaTypeHeaderValue Accept Header Parsing

- **Purpose**: Verify that `negotiateRdfType` correctly parses Accept headers using `MediaTypeHeaderValue` — the fix from WP03 T012.
- **Steps**:
  1. Open or create test file in `test/Frank.LinkedData.Tests/` (e.g., `ContentNegotiationTests.fs`)
  2. Test cases:
     - `"application/ld+json"` → matches `application/ld+json`
     - `"text/turtle"` → matches `text/turtle`
     - `"application/rdf+xml"` → matches `application/rdf+xml`
     - `"text/html"` → returns `None` (no RDF match)
     - `"application/ld+json;q=0.5, text/turtle;q=0.9"` → returns `text/turtle` (higher quality)
     - `"*/*"` → returns highest-priority supported type (or `None` depending on implementation)
     - `""` → returns `None`
     - Malformed header (e.g., `"not a valid header;;;;"`) → returns `None` (no crash)
     - `"application/json"` → returns `None` (not `application/ld+json`)
     - `"x-application/ld+json"` → returns `None` (no false partial match)
  3. Each test should call `negotiateRdfType` directly and assert the result
- **Files**: `test/Frank.LinkedData.Tests/ContentNegotiationTests.fs` (new or existing)
- **Parallel?**: Yes — independent of T031, T033-T035

### Subtask T033 – Tests for Structural Hash Cache Key Uniqueness

- **Purpose**: Verify that the structural hash cache key in `InstanceProjector` produces correct keys — the fix from WP03 T014.
- **Steps**:
  1. Open or create test file in `test/Frank.LinkedData.Tests/` (e.g., `InstanceProjectorTests.fs`)
  2. Test cases:
     - Two F# record instances with the same field values → same cache key
     - Two F# record instances with different field values → different cache keys
     - Same object instance called twice → same cache key (stability)
     - Two distinct object instances with identical structural content → same cache key (content-addressable)
  3. Use simple test record types:
     ```fsharp
     type TestResource = { Id: int; Name: string; Active: bool }
     ```
  4. Call the cache key generation function directly (may need to expose it as `internal` for testing)
- **Files**: `test/Frank.LinkedData.Tests/InstanceProjectorTests.fs` (new or existing)
- **Parallel?**: Yes — independent of T031-T032, T034-T035

### Subtask T034 – Tests for Null Assembly.GetEntryAssembly() Handling

- **Purpose**: Verify that the middleware handles `Assembly.GetEntryAssembly()` returning null gracefully — the fix from WP03 T013.
- **Steps**:
  1. Open or create test file in `test/Frank.LinkedData.Tests/`
  2. This is tricky to test directly since `Assembly.GetEntryAssembly()` is a static method
  3. Options:
     - If the code was refactored to accept an assembly parameter (preferred), test with null
     - If using a fallback pattern, test the fallback behavior
     - Test via TestHost (which naturally returns null for `GetEntryAssembly()`)
  4. Test that:
     - When entry assembly is null, a clear error message is produced (if fail-fast)
     - OR the fallback assembly is used correctly (if fallback pattern)
     - No `NullReferenceException` is thrown
- **Files**: `test/Frank.LinkedData.Tests/` (appropriate test file)
- **Parallel?**: Yes — independent of T031-T033, T035

### Subtask T035 – Tests for ExtractionState Map Serialization Round-Trip

- **Purpose**: Verify that `ExtractionState` with `Map<string, SourceLocation>` serializes and deserializes correctly, including backward-compatible loading of old `Dictionary<Uri, SourceLocation>` format — the fix from WP04 T021.
- **Steps**:
  1. Open or create test file in `test/Frank.Cli.Core.Tests/` (e.g., `ExtractionStateTests.fs`)
  2. Test cases:
     - Create an `ExtractionState` with `SourceMap` entries → save → load → verify identical entries
     - Create a test fixture JSON file with old Uri-keyed format → load → verify entries converted to string keys
     - Empty `SourceMap` → save → load → verify empty Map
     - `SourceMap` with special characters in keys → round-trip correctly
  3. Create a fixture file for the old format:
     ```json
     {
       "sourceMap": {
         "http://example.com/types/Person": { "file": "Person.fs", "line": 10 },
         "http://example.com/types/Address": { "file": "Address.fs", "line": 25 }
       }
     }
     ```
  4. Place fixture in `test/Frank.Cli.Core.Tests/TestData/` (or equivalent)
- **Files**:
  - `test/Frank.Cli.Core.Tests/ExtractionStateTests.fs` (new or existing)
  - `test/Frank.Cli.Core.Tests/TestData/old-state-format.json` (fixture)
- **Parallel?**: Yes — independent of T031-T034

### Subtask T036 – Full Test Suite Verification

- **Purpose**: Run the complete test suite to verify all 265 baseline tests pass and no regressions were introduced.
- **Steps**:
  1. From the repository root, run all tests:
     ```bash
     dotnet test Frank.sln
     ```
     Or if tests are not in the solution:
     ```bash
     dotnet test test/Frank.LinkedData.Tests/
     dotnet test test/Frank.Cli.Core.Tests/
     ```
  2. Verify total test count is at least 265 (baseline) plus new tests
  3. Verify zero failures
  4. If any baseline test fails, investigate whether it was caused by a behavioral change in WP02-WP05 and fix the root cause (not the test)
  5. Document the final test count in the activity log
- **Files**: All test projects
- **Parallel?**: No — must run after all other subtasks in this WP
- **Notes**: If a baseline test fails due to an intentional behavioral change (e.g., JSON-LD now includes type annotations), that test must be updated to match the new correct behavior — not reverted.

## Risks & Mitigations

- **Null assembly testing**: May need creative test setup since `Assembly.GetEntryAssembly()` is hard to mock. TestHost naturally provides this scenario.
- **Backward-compatible loading**: Need a fixture file representing the old state format. Create one manually based on the old schema.
- **Test count verification**: If the baseline changed during implementation (tests added/removed by other WPs), adjust the expected count accordingly.

## Review Guidance

- Verify each behavioral change (T006, T007, T012, T013, T014, T021) has at least one corresponding test
- Verify edge cases are covered (malformed Accept, empty map, null assembly)
- Verify full test suite passes with `dotnet test`
- Check test count: should be 265+ baseline plus new tests

## Activity Log

- 2026-03-06T15:25:00Z – system – lane=planned – Prompt created.
