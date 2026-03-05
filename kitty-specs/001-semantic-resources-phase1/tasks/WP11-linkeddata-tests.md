---
work_package_id: WP11
title: LinkedData Tests
lane: "doing"
dependencies: [WP08]
base_branch: 001-semantic-resources-phase1-WP08
base_commit: 1d04de09b9012674f85325676b509713e21ebe74
created_at: '2026-03-05T23:39:07.857259+00:00'
subtasks:
- T055
- T056
- T057
- T058
phase: Phase 2 - Testing
assignee: ''
agent: ''
shell_pid: "11043"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-04T22:10:13Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-015, FR-016, FR-017, FR-018, FR-019, FR-020, FR-021]
---

> **Review Feedback Status**: No review feedback yet.

## Review Feedback

_No feedback recorded._

> **Markdown Formatting Note**: All prose in this file uses plain Markdown. Code samples use fenced code blocks with language tags. Lists use `-` bullets.

## Objectives & Success Criteria

Provide comprehensive unit and integration tests for the `Frank.LinkedData` library.

**Success criteria**:
- `InstanceProjector` unit tests verify correct triple production for all supported F# type shapes
- Formatter tests verify round-trip correctness: write a known graph → parse back → graphs are isomorphic
- Integration tests using ASP.NET Core `TestHost` verify content negotiation behavior end-to-end
- Startup validation tests verify that missing embedded resources produce clear, actionable error messages
- All tests pass as part of `dotnet test`; non-LinkedData resources are completely unaffected by the library

## Context & Constraints

- Test project: `test/Frank.LinkedData.Tests/` (created in WP01)
- Use Expecto + TestHost pattern from existing `test/Frank.Tests/` and `test/Frank.OpenApi.Tests/`
- Formatter round-trip tests must not compare serialized strings; compare parsed graphs for isomorphism
- Integration tests must verify that `Accept: application/json` and `text/html` continue to use standard Frank content negotiation (FR-019: zero behavioral change for non-RDF requests)
- Depends on WP08 (Frank.LinkedData library implementation) being complete

## Subtasks & Detailed Guidance

### T055 — InstanceProjector tests

**File**: `test/Frank.LinkedData.Tests/InstanceProjectorTests.fs`

Test that `InstanceProjector` correctly converts F# record and DU instances to RDF triples.

Key test cases:

- **Simple record**: `{ Name: string; Age: int }` with values `{ Name = "Alice"; Age = 30 }` → 2 triples: one with an `xsd:string` literal "Alice", one with an `xsd:integer` literal 30
- **Option field present**: `{ Value: string option }` with `{ Value = Some "hello" }` → triple for `Value` exists in the graph
- **Option field absent**: `{ Value: string option }` with `{ Value = None }` → no triple for `Value` in the graph
- **Nested record**: `{ Inner: { X: int } }` → the outer subject links to a blank node, which in turn carries the `X` triple
- **String list**: `{ Tags: string list }` with `{ Tags = ["a"; "b"; "c"] }` → 3 triples with the same predicate and different literal objects
- **DU value**: A DU instance → a triple that encodes the case information (verify against `data-model.md` for the exact encoding)
- **Literal datatypes**: Verify that `string` → `xsd:string`, `int` → `xsd:integer`, `bool` → `xsd:boolean`, `decimal` → `xsd:decimal`

---

### T056 — Formatter tests

**File**: `test/Frank.LinkedData.Tests/FormatterTests.fs`

Test all three RDF formatters for correct output and round-trip correctness.

For each formatter — JSON-LD, Turtle, RDF/XML:

1. Construct a known `IGraph` with a fixed set of triples (use a small but non-trivial graph: at least 3 subjects, 6 triples total)
2. Serialize using the formatter → capture output as a byte array or string
3. Parse the output back using the corresponding dotNetRdf parser into a fresh `IGraph`
4. Assert graph isomorphism: the parsed graph contains exactly the same triples as the input

Additional cases:
- **Empty graph**: Each formatter must produce valid but empty output (no crashes, no invalid syntax)
- **Unicode characters**: A graph containing a literal with non-ASCII characters (e.g., "Ångström", "日本語") must serialize and round-trip correctly
- **Content-Type header**: Verify each formatter sets the correct `Content-Type` response header:
  - JSON-LD: `application/ld+json`
  - Turtle: `text/turtle`
  - RDF/XML: `application/rdf+xml`

Graph isomorphism check using dotNetRdf:

```fsharp
let isIsomorphic (g1: IGraph) (g2: IGraph) =
    g1.IsIsomorphicWith(g2)

Expect.isTrue (isIsomorphic original parsed) "Round-tripped graph must be isomorphic to original"
```

---

### T057 — Content negotiation integration tests

**File**: `test/Frank.LinkedData.Tests/ContentNegotiationTests.fs`

Test end-to-end content negotiation using ASP.NET Core `TestHost`.

Set up a test Frank app:

```fsharp
let app = webHost [||] {
    useLinkedData
    resource "/test" {
        linkedData
        get (fun ctx -> ctx.Negotiate(200, { Name = "Test"; Value = 42 }))
    }
    resource "/plain" {
        get (fun ctx -> ctx.Negotiate(200, { Name = "Plain"; Value = 0 }))
    }
}
```

Test cases for the `/test` resource (LinkedData-enabled):

- **`Accept: application/ld+json`**: Response is 200, body is valid JSON-LD, `Content-Type` header is `application/ld+json`
- **`Accept: text/turtle`**: Response is 200, body is valid Turtle, `Content-Type` header is `text/turtle`
- **`Accept: application/rdf+xml`**: Response is 200, body is valid RDF/XML, `Content-Type` header is `application/rdf+xml`
- **`Accept: application/json`**: Response uses standard Frank JSON negotiation (NOT LinkedData), `Content-Type` header is `application/json`
- **`Accept: text/html`**: Response uses standard Frank negotiation, not LinkedData

Test cases for the `/plain` resource (no `linkedData` CE operation):

- **`Accept: application/ld+json`**: Must NOT return RDF — return standard Frank response or 406 Not Acceptable, same behavior as before this feature was added
- **`Accept: text/turtle`**: Same as above

This directly verifies FR-019: the feature must not alter behavior of resources that have not opted in.

---

### T058 — Startup validation tests

**File**: `test/Frank.LinkedData.Tests/StartupValidationTests.fs`

Test that the startup validation in `Frank.LinkedData` gives clear errors when the required embedded resources are missing.

Key test cases:

- **Valid configuration**: App with `useLinkedData`, at least one resource with `linkedData`, and embedded resources (`Frank.Semantic.ontology.owl.xml` etc.) present in the assembly → app starts successfully, `TestHost.CreateClient()` does not throw
- **Missing embedded resources**: App with `useLinkedData` + resource with `linkedData`, but the embedded resources are NOT present (simulated by loading a test assembly without the resources) → startup throws a descriptive exception
  - The exception message must include the assembly name
  - The exception message must list the expected embedded resource names (`Frank.Semantic.ontology.owl.xml`, etc.)
  - The exception message must suggest running `frank-cli compile`
- **No `useLinkedData`**: App without `useLinkedData` but with embedded resources → no validation runs, starts normally (no false positives)

For the "missing embedded resources" case, the simplest approach is to not embed the resources in the test project itself and verify that startup validation catches it. The test assembly for `test/Frank.LinkedData.Tests/` should NOT include the embedded resources by default, making it a natural test harness for the failure case.

## Risks & Mitigations

- **TestHost output formatter registration**: ASP.NET Core's `TestHost` may require explicit output formatter registration for RDF media types. Reference `test/Frank.OpenApi.Tests/` for how formatters are registered in the test server setup.
- **Graph isomorphism edge cases**: dotNetRdf's `IsIsomorphicWith` handles blank node renaming, but may be sensitive to graph size. Keep test graphs small.
- **Startup validation test isolation**: The test for missing embedded resources depends on the test assembly not having embedded resources. Ensure the `Frank.Cli.MSBuild` package is NOT referenced by `test/Frank.LinkedData.Tests/` (only `Frank.LinkedData` is referenced directly).

## Review Guidance

- Ensure integration tests cover both positive (RDF served correctly) and negative (non-opted-in resources unaffected) scenarios
- Verify formatter tests do not compare serialized strings — only graph isomorphism
- Confirm that the startup validation error message is human-readable and actionable (include the assembly name, list expected resource names, suggest the CLI command)
- Run `dotnet test test/Frank.LinkedData.Tests/` and verify all tests pass
- Manually verify that removing the `linkedData` CE operation from a resource makes that resource invisible to LinkedData content negotiation

## Activity Log

| Timestamp | Agent | Action |
|-----------|-------|--------|
| 2026-03-04T22:10:13Z | system | Prompt generated via /spec-kitty.tasks |
