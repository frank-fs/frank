---
work_package_id: "WP02"
subtasks:
  - "T007"
  - "T008"
  - "T009"
  - "T010"
  - "T011"
  - "T012"
title: "US1 -- RDF Parsing and Cross-Format Isomorphism"
phase: "Phase 1 - P1 User Stories"
lane: "done"
assignee: ""
agent: "claude-opus"
shell_pid: "60814"
review_status: "approved"
reviewed_by: "Ryan Riley"
dependencies: ["WP01"]
requirement_refs: ["FR-001", "FR-002", "FR-003", "FR-004", "FR-012"]
history:
  - timestamp: "2026-03-15T23:59:02Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 -- US1: RDF Parsing and Cross-Format Isomorphism

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback, update `review_status: acknowledged`.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````bash`

---

## Implementation Command

Depends on WP01 -- branch from WP01's branch:
```bash
spec-kitty implement WP02 --base WP01
```

---

## Objectives & Success Criteria

1. `RdfParsingTests.fs` contains tests validating all three RDF serialization formats (JSON-LD, Turtle, RDF/XML) parse without errors.
2. Cross-format isomorphism is verified using dotNetRdf's `GraphDiff` (graphs from all three formats contain equivalent triples).
3. Namespace prefix resolution is validated (all prefixed namespaces resolve to valid URIs).
4. Edge case: empty resource definitions produce parseable graphs.
5. All tests pass under `dotnet test test/Frank.RdfValidation.Tests/`.
6. Test names follow the pattern `"US1-SC#: <description>"` with inline comments documenting the SPARQL/RDF pattern being tested (FR-013, SC-006).

---

## Context & Constraints

- **Spec User Story 1**: "RDF Output Parses Successfully in All Three Formats" (Priority P1)
- **Requirements**: FR-001 (JSON-LD parsing), FR-002 (Turtle parsing), FR-003 (RDF/XML parsing), FR-004 (isomorphism), FR-012 (namespace prefix resolution)
- **Research R2**: Use `graph1.Difference(graph2)` for isomorphism checks -- `diff.AreEqual` is true when graphs are isomorphic (handles blank node renaming)
- **Research R3**: TestHost pattern for content negotiation -- request with Accept header, parse response body
- **Data Model**: Resource graph triples follow pattern `<resource-uri> <ontology-property-uri> "literal-value"` and `<resource-uri> rdf:type <ontology-class-uri>`
- **Constitution VI**: All `IGraph`, `HttpClient`, `StringReader` must use `use` bindings
- **Prerequisite**: WP01 must be complete (TestHelpers.fs with `createTestHost`, `loadTurtleGraph`, `loadJsonLdGraph`, `loadRdfXmlGraph`, `getRdfResponse`)

---

## Subtasks & Detailed Guidance

### Subtask T007 -- Create `RdfParsingTests.fs` Module Structure

- **Purpose**: Set up the test module with imports, test list, and shared test fixtures.
- **Files**: `test/Frank.RdfValidation.Tests/RdfParsingTests.fs` (replace stub from WP01)
- **Parallel?**: No (foundation for T008-T012)

**Steps**:

1. Replace the stub file with a full module:

```fsharp
module Frank.RdfValidation.Tests.RdfParsingTests

open System.Net.Http
open Expecto
open VDS.RDF
open Frank.RdfValidation.Tests.TestHelpers

[<Tests>]
let tests =
    testList "US1 - RDF Parsing" [
        // Test cases added in T008-T012
    ]
```

2. The test list will contain `testAsync` entries for each acceptance scenario.

3. Each test should:
   - Create a TestHost using the shared helper
   - Send HTTP requests with appropriate Accept headers
   - Parse responses into dotNetRdf graphs
   - Assert expected outcomes

---

### Subtask T008 -- US1-SC1: JSON-LD Parsing Test (FR-001)

- **Purpose**: Verify that Frank's JSON-LD output (`application/ld+json`) loads into a dotNetRdf graph without parse errors and contains expected triples.
- **Files**: `test/Frank.RdfValidation.Tests/RdfParsingTests.fs` (add test case)
- **Parallel?**: Yes [P] (independent test)

**Steps**:

1. Add a `testAsync` to the test list:

```fsharp
testAsync "US1-SC1: JSON-LD representation parses into graph with expected triples" {
    // Arrange: Create TestHost with LinkedData middleware
    use host = createTestHost ()
    let server = host.GetTestServer()
    use client = server.CreateClient()

    // Act: Request JSON-LD format
    let! body = getRdfResponse client "/person/1" "application/ld+json"

    // Parse into dotNetRdf graph
    use graph = loadJsonLdGraph body

    // Assert: Graph contains triples (no parse errors would have thrown)
    Expect.isGreaterThan graph.Triples.Count 0
        "JSON-LD graph should contain at least one triple"

    // Verify expected subject URI exists
    let subjects =
        graph.Triples
        |> Seq.map (fun t -> t.Subject)
        |> Seq.distinct
        |> Seq.toList

    Expect.isGreaterThan subjects.Length 0
        "Graph should have at least one distinct subject"
}
```

2. Validate that the triple count is reasonable for the test resource (a person with Name and Age should produce at least 2-3 triples: the property values + `rdf:type`).

**Notes**:
- If JSON-LD parsing fails, dotNetRdf throws an `RdfParseException` which Expecto will surface as a test failure -- no need for explicit try/catch
- The exact triple count depends on how LinkedData middleware serializes the JSON response; assert `> 0` as a minimum and optionally check specific counts if predictable

---

### Subtask T009 -- US1-SC2: Turtle Parsing and Isomorphism with JSON-LD (FR-002, FR-004)

- **Purpose**: Verify Turtle format parses correctly and produces a graph isomorphic to the JSON-LD graph.
- **Files**: `test/Frank.RdfValidation.Tests/RdfParsingTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

1. Add a test that fetches both JSON-LD and Turtle, then compares:

```fsharp
testAsync "US1-SC2: Turtle graph is isomorphic to JSON-LD graph" {
    // Arrange
    use host = createTestHost ()
    let server = host.GetTestServer()
    use client = server.CreateClient()

    // Act: Get both formats for the same resource
    let! jsonldBody = getRdfResponse client "/person/1" "application/ld+json"
    let! turtleBody = getRdfResponse client "/person/1" "text/turtle"

    // Parse into graphs
    use jsonldGraph = loadJsonLdGraph jsonldBody
    use turtleGraph = loadTurtleGraph turtleBody

    // Assert: Graphs are isomorphic (same triples, modulo blank node identity)
    // GraphDiff handles blank node renaming across serialization formats
    let diff = jsonldGraph.Difference(turtleGraph)
    Expect.isTrue diff.AreEqual
        "Turtle graph should be isomorphic to JSON-LD graph (same triples modulo blank nodes)"
}
```

2. If `GraphDiff.AreEqual` fails, use `diff.AddedTriples` and `diff.RemovedTriples` for diagnostic output.

**Notes**:
- Blank node identifiers are format-specific (edge case from spec). `GraphDiff` handles this by comparing graph structure rather than node identifiers.
- If the graphs differ only in blank node naming, `AreEqual` should still return true.

---

### Subtask T010 -- US1-SC3: RDF/XML Parsing and Isomorphism (FR-003, FR-004)

- **Purpose**: Verify RDF/XML format parses correctly and is isomorphic to both JSON-LD and Turtle graphs.
- **Files**: `test/Frank.RdfValidation.Tests/RdfParsingTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

1. Add a test that fetches all three formats and compares:

```fsharp
testAsync "US1-SC3: RDF/XML graph is isomorphic to JSON-LD and Turtle graphs" {
    // Arrange
    use host = createTestHost ()
    let server = host.GetTestServer()
    use client = server.CreateClient()

    // Act: Get all three formats
    let! jsonldBody = getRdfResponse client "/person/1" "application/ld+json"
    let! turtleBody = getRdfResponse client "/person/1" "text/turtle"
    let! rdfxmlBody = getRdfResponse client "/person/1" "application/rdf+xml"

    // Parse into graphs
    use jsonldGraph = loadJsonLdGraph jsonldBody
    use turtleGraph = loadTurtleGraph turtleBody
    use rdfxmlGraph = loadRdfXmlGraph rdfxmlBody

    // Assert: All three are isomorphic
    let diffJT = jsonldGraph.Difference(turtleGraph)
    let diffJR = jsonldGraph.Difference(rdfxmlGraph)
    let diffTR = turtleGraph.Difference(rdfxmlGraph)

    Expect.isTrue diffJT.AreEqual "JSON-LD and Turtle should be isomorphic"
    Expect.isTrue diffJR.AreEqual "JSON-LD and RDF/XML should be isomorphic"
    Expect.isTrue diffTR.AreEqual "Turtle and RDF/XML should be isomorphic"
}
```

**Notes**:
- Comparing all three pairs is technically redundant (if A=B and A=C then B=C), but it provides better diagnostic output when a specific pair fails.
- The RDF/XML parser may handle namespace prefixes differently than Turtle -- this is expected and `GraphDiff` should handle it.

---

### Subtask T011 -- US1-SC4: Namespace Prefix Resolution (FR-012)

- **Purpose**: Verify that all namespace prefixes in the RDF output resolve to valid URIs with no undefined-prefix errors.
- **Files**: `test/Frank.RdfValidation.Tests/RdfParsingTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

1. Add a test that checks namespace prefixes:

```fsharp
testAsync "US1-SC4: All namespace prefixes resolve to valid URIs" {
    // Arrange
    use host = createTestHost ()
    let server = host.GetTestServer()
    use client = server.CreateClient()

    // Act: Get Turtle format (most explicit about prefix declarations)
    let! turtleBody = getRdfResponse client "/person/1" "text/turtle"
    use graph = loadTurtleGraph turtleBody

    // Assert: All registered namespace prefixes have valid URIs
    // dotNetRdf's NamespaceMap tracks prefix->URI mappings
    let namespaces = graph.NamespaceMap.Prefixes |> Seq.toList

    Expect.isGreaterThan namespaces.Length 0
        "Graph should have at least one namespace prefix registered"

    for prefix in namespaces do
        let uri = graph.NamespaceMap.GetNamespaceUri(prefix)
        Expect.isNotNull uri
            $"Namespace prefix '{prefix}' should resolve to a valid URI"
        Expect.isTrue (uri.IsAbsoluteUri)
            $"Namespace URI for prefix '{prefix}' should be an absolute URI: {uri}"
}
```

**Notes**:
- Turtle format is best for this test because it explicitly declares `@prefix` directives
- The `NamespaceMap` on `IGraph` tracks all registered prefixes after parsing
- Expected prefixes include: `rdf:`, `rdfs:`, `owl:`, `xsd:`, and the application-specific prefix based on `LinkedDataConfig.BaseUri`

---

### Subtask T012 -- Edge Case: Empty Resource Parsing

- **Purpose**: Verify that a resource with no handlers (empty resource definition) still produces parseable RDF output (empty or minimal graph) without errors.
- **Files**: `test/Frank.RdfValidation.Tests/RdfParsingTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

1. This test may require a special TestHost configuration or a separate endpoint that returns minimal/empty JSON. Options:
   - Add an endpoint to the TestHost that returns `{}` (empty JSON object) with `LinkedDataMarker`
   - Or test parsing an empty Turtle document

2. Approach A (empty endpoint):
```fsharp
testAsync "US1-Edge: Empty resource definition produces parseable graph" {
    // Arrange: Use a TestHost with an endpoint returning minimal JSON
    // (The TestHost helper may need an overload or the empty endpoint
    // may need to be added to the standard test setup)
    use host = createTestHost ()
    let server = host.GetTestServer()
    use client = server.CreateClient()

    // Act: Request an empty or minimal resource
    // If the standard test host has an empty endpoint:
    let! turtleBody = getRdfResponse client "/empty" "text/turtle"
    use graph = loadTurtleGraph turtleBody

    // Assert: Graph parses without error (may be empty or have just rdf:type)
    // The key assertion is that no exception was thrown during parsing
    Expect.isTrue (graph.Triples.Count >= 0)
        "Empty resource should produce a parseable graph (even if empty)"
}
```

3. If an empty endpoint is not practical, test that parsing an empty but valid Turtle document works:
```fsharp
testAsync "US1-Edge: Minimal RDF content parses without errors" {
    // Parse an empty Turtle document (just prefix declarations, no triples)
    use graph = loadTurtleGraph "@prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> ."

    Expect.equal graph.Triples.Count 0
        "Empty Turtle document should parse to a graph with zero triples"
}
```

**Notes**:
- The spec edge case says "empty or minimal graph" -- either approach validates the requirement
- The preferred approach depends on whether the TestHost can easily serve an empty resource; if not, direct parsing validation is sufficient
- Add an empty endpoint `/empty` to the TestHost helper in WP01 if needed (coordinate with TestHelpers.fs)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| LinkedData middleware may not produce all three RDF formats | Check that content negotiation for `application/rdf+xml` is supported; if not, the test will surface this as a failure (which is the point) |
| `GraphDiff` may have issues with certain blank node patterns | If isomorphism fails, inspect `diff.AddedTriples` / `diff.RemovedTriples` for diagnostics; consider manual triple count comparison as fallback |
| JSON-LD parsing may require `@context` that middleware doesn't produce | Verify LinkedData middleware output includes proper JSON-LD `@context` -- if not, this test surfaces the gap |

---

## Review Guidance

- Verify all 6 test cases are present and follow the naming convention `"US1-SC#: <description>"`
- Verify isomorphism tests use `GraphDiff` (not manual triple comparison)
- Verify `use` bindings for all `IGraph` instances and `HttpClient`/`TestServer`
- Verify inline comments in test bodies explain what RDF/SPARQL pattern is being validated (FR-013)
- Run `dotnet test test/Frank.RdfValidation.Tests/ --filter "US1"` to confirm all tests pass
- Check that namespace prefix test validates both existence and absolute URI validity

---

## Activity Log

- 2026-03-15T23:59:02Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T11:46:50Z – unknown – lane=for_review – Ready for review: All 6 US1 test cases implemented and passing
- 2026-03-16T11:48:00Z – claude-opus – shell_pid=60814 – lane=doing – Started review via workflow command
- 2026-03-16T11:50:40Z – claude-opus – shell_pid=60814 – lane=done – Review passed: All 6 US1 test cases present and passing. FR-001/002/003/004/012 covered. GraphDiff used for isomorphism. Proper use bindings throughout. Test naming convention followed. Clean build with 0 warnings.
