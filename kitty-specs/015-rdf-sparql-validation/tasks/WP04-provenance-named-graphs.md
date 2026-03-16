---
work_package_id: "WP04"
subtasks:
  - "T018"
  - "T019"
  - "T020"
  - "T021"
  - "T022"
  - "T023"
title: "US3 -- Provenance Named Graph Isolation"
phase: "Phase 2 - P2 User Stories"
lane: "done"
assignee: ""
agent: ""
shell_pid: ""
review_status: "approved"
reviewed_by: "Ryan Riley"
dependencies: ["WP01"]
requirement_refs: ["FR-009", "FR-010"]
history:
  - timestamp: "2026-03-15T23:59:02Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP04 -- US3: Provenance Named Graph Isolation

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
Use language identifiers in code blocks: ````fsharp`, ````sparql`, ````bash`

---

## Implementation Command

Depends on WP01 -- branch from WP01's branch:
```bash
spec-kitty implement WP04 --base WP01
```

---

## Objectives & Success Criteria

1. `ProvenanceGraphTests.fs` contains tests validating named graph isolation for PROV-O provenance data.
2. Resource model triples and provenance triples are loaded into separate named graphs in a `TripleStore`.
3. SPARQL queries using `GRAPH` clause correctly scope to provenance-only data (FR-009, FR-010).
4. Per-resource provenance scoping is verified (no cross-resource leakage).
5. Edge case: empty provenance graph returns empty results (not errors).
6. All tests pass under `dotnet test test/Frank.RdfValidation.Tests/ --filter "US3"`.

---

## Context & Constraints

- **Spec User Story 3**: "Provenance Graph Coherence and Named Graph Isolation" (Priority P2)
- **Requirements**: FR-009 (named graph loading), FR-010 (GRAPH clause SPARQL)
- **Research R1**: `LeviathanQueryProcessor` with `InMemoryDataset` wrapping `TripleStore` for named graph support
- **Research R4**: Provenance named graphs use resource-scoped URIs; `GraphBuilder.toGraph` creates an `IGraph` from `ProvenanceRecord` values
- **Data Model**: Provenance triples follow PROV-O patterns (`prov:Activity`, `prov:wasAssociatedWith`, `prov:used`, etc.)
- **Data Model Namespace Prefixes**: `prov:` -> `http://www.w3.org/ns/prov#`, `frank:` -> `https://frank-web.dev/ns/provenance#`
- **Constitution VI**: `use` bindings for `IGraph`, `TripleStore`, `HttpClient`
- **Prerequisite**: WP01 must be complete (TestHelpers with `executeSparqlOnDataset`, `createTestHost`, provenance store seeding)

**Understanding Frank.Provenance's RDF model**:

Before implementing, inspect:
- `src/Frank.Provenance/GraphBuilder.fs` -- the `toGraph` function that builds provenance RDF
- `src/Frank.Provenance/Types.fs` -- `ProvenanceRecord`, `ProvenanceActivity`, `ProvenanceAgent`, `AgentType`
- `src/Frank.Provenance/Vocabulary.fs` -- PROV-O predicate URIs used
- `test/Frank.Provenance.Tests/IntegrationTests.fs` -- patterns for seeding provenance records and creating TestHost with provenance middleware

---

## Subtasks & Detailed Guidance

### Subtask T018 -- Create `ProvenanceGraphTests.fs` Module Structure

- **Purpose**: Set up the test module with imports, provenance-specific helpers, and test list.
- **Files**: `test/Frank.RdfValidation.Tests/ProvenanceGraphTests.fs` (replace stub from WP01)
- **Parallel?**: No (foundation for T019-T023)

**Steps**:

1. Replace the stub with a full module:

```fsharp
module Frank.RdfValidation.Tests.ProvenanceGraphTests

open System
open System.Net.Http
open Expecto
open VDS.RDF
open VDS.RDF.Query
open Frank.Provenance
open Frank.RdfValidation.Tests.TestHelpers
```

2. Add module-level helpers for provenance test setup:

```fsharp
/// Create a TripleStore with resource graph in default graph and provenance in a named graph.
let private createProvenanceDataset
    (resourceGraph: IGraph)
    (provenanceGraph: IGraph)
    (namedGraphUri: Uri)
    : TripleStore =
    let store = new TripleStore()
    store.Add(resourceGraph)  // default graph
    // Add provenance as named graph
    provenanceGraph.BaseUri <- namedGraphUri
    store.Add(provenanceGraph)
    store
```

3. Add a helper to build provenance records for testing:

```fsharp
/// Create test provenance records for a resource.
/// Reference: test/Frank.Provenance.Tests/IntegrationTests.fs for ProvenanceRecord construction.
let private makeTestProvenanceRecords (resourceUri: string) : ProvenanceRecord list =
    // Build 2-3 provenance records representing state transitions
    // Inspect Frank.Provenance.Types for exact constructor signatures
    // ...
```

4. Test list:
```fsharp
[<Tests>]
let tests =
    testList "US3 - Provenance Named Graph Isolation" [
        // Test cases added in T019-T023
    ]
```

---

### Subtask T019 -- US3-SC1: Named Graph Isolation Verification (FR-009, FR-010)

- **Purpose**: Load both resource and provenance RDF into named graphs, verify SPARQL against the provenance named graph returns only PROV-O triples (not resource triples).
- **Files**: `test/Frank.RdfValidation.Tests/ProvenanceGraphTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

1. This test needs to:
   - Get resource RDF from the TestHost (Turtle format)
   - Build provenance RDF using `GraphBuilder.toGraph` with test provenance records
   - Load both into a `TripleStore` with resource as default graph and provenance as named graph
   - Execute SPARQL with `GRAPH` clause targeting the provenance named graph
   - Verify results contain only PROV-O predicates, not resource predicates

```fsharp
testAsync "US3-SC1: Provenance named graph contains only PROV-O triples, not resource triples" {
    // Arrange: Get resource graph from TestHost
    use host = createTestHost ()
    let server = host.GetTestServer()
    use client = server.CreateClient()
    let! body = getRdfResponse client "/person/1" "text/turtle"
    use resourceGraph = loadTurtleGraph body

    // Build provenance graph from test records
    // Use GraphBuilder.toGraph with seeded ProvenanceRecord values
    let provenanceRecords = makeTestProvenanceRecords "http://example.org/api/person/1"
    use provenanceGraph = GraphBuilder.toGraph provenanceRecords

    // Load into dataset with named graphs
    let provUri = Uri("http://example.org/api/person/1/provenance")
    use store = createProvenanceDataset resourceGraph provenanceGraph provUri

    // Act: Query provenance named graph for all predicates
    // The GRAPH clause scopes the query to only the provenance named graph.
    // This proves isolation: only PROV-O predicates should appear.
    let results = executeSparqlOnDataset store $"""
        PREFIX prov: <http://www.w3.org/ns/prov#>

        # List all predicates in the provenance named graph.
        # If isolation is correct, only prov: predicates should appear.
        SELECT DISTINCT ?predicate
        WHERE {{
            GRAPH <{provUri}> {{
                ?s ?predicate ?o .
            }}
        }}
    """

    // Assert: All predicates should be PROV-O or related (rdf:type, prov:*)
    Expect.isGreaterThan results.Count 0
        "Provenance named graph should contain triples"

    for result in results do
        let pred = result.["predicate"].ToString()
        let isProvOrRdf =
            pred.StartsWith("http://www.w3.org/ns/prov#")
            || pred.StartsWith("http://www.w3.org/1999/02/22-rdf-syntax-ns#")
            || pred.StartsWith("https://frank-web.dev/ns/provenance#")
        Expect.isTrue isProvOrRdf
            $"Predicate in provenance graph should be PROV-O or rdf:type, but found: {pred}"
}
```

**Notes**:
- The exact method to build provenance graphs depends on `GraphBuilder.toGraph` signature -- inspect the source
- Named graph URI convention should match what Frank.Provenance uses (e.g., `{resourceUri}/provenance`)
- The predicate whitelist may need adjustment based on actual PROV-O vocabulary used

---

### Subtask T020 -- US3-SC2: SPARQL for `prov:Activity` with Agents and Timestamps (FR-010)

- **Purpose**: Query the provenance named graph for all `prov:Activity` instances with their associated agents and timestamps, verify they match recorded state transitions.
- **Files**: `test/Frank.RdfValidation.Tests/ProvenanceGraphTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

```fsharp
testAsync "US3-SC2: SPARQL finds prov:Activity instances with agents and timestamps" {
    // Arrange: Build provenance graph with known records
    let records = makeTestProvenanceRecords "http://example.org/api/person/1"
    use provenanceGraph = GraphBuilder.toGraph records
    let provUri = Uri("http://example.org/api/person/1/provenance")

    use store = new TripleStore()
    provenanceGraph.BaseUri <- provUri
    store.Add(provenanceGraph)

    // Act: Query for activities, agents, and timestamps
    // This SPARQL pattern extracts the core provenance audit trail:
    // who did what, and when.
    let results = executeSparqlOnDataset store $"""
        PREFIX prov: <http://www.w3.org/ns/prov#>
        PREFIX rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#>
        PREFIX xsd: <http://www.w3.org/2001/XMLSchema#>

        # Find all provenance activities with their agents and start times.
        # This is the fundamental provenance query for audit trail reconstruction.
        SELECT ?activity ?agent ?startTime
        WHERE {{
            GRAPH <{provUri}> {{
                ?activity rdf:type prov:Activity .
                ?activity prov:wasAssociatedWith ?agent .
                ?activity prov:startedAtTime ?startTime .
            }}
        }}
    """

    // Assert: Should find activities matching the seeded records
    Expect.isGreaterThan results.Count 0
        "Should find at least one prov:Activity with agent and timestamp"

    // Verify the activity count matches seeded records
    Expect.equal results.Count (List.length records)
        $"Activity count should match seeded record count ({List.length records})"
}
```

**Notes**:
- The exact PROV-O predicates used depend on `Frank.Provenance.Vocabulary` -- inspect `src/Frank.Provenance/Vocabulary.fs`
- The seeded records should have known agent IDs and timestamps for assertion
- If `GraphBuilder.toGraph` takes a different argument shape, adapt accordingly

---

### Subtask T021 -- US3-SC3: Per-Resource Provenance Scoping (FR-009)

- **Purpose**: Verify that when multiple resources have provenance, each resource's provenance is scoped to its own named graph with no cross-resource leakage.
- **Files**: `test/Frank.RdfValidation.Tests/ProvenanceGraphTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

```fsharp
testAsync "US3-SC3: Per-resource provenance scoping -- no cross-resource leakage" {
    // Arrange: Create provenance for TWO different resources
    let personRecords = makeTestProvenanceRecords "http://example.org/api/person/1"
    let orderRecords = makeTestProvenanceRecords "http://example.org/api/order/42"

    use personProvGraph = GraphBuilder.toGraph personRecords
    use orderProvGraph = GraphBuilder.toGraph orderRecords

    let personProvUri = Uri("http://example.org/api/person/1/provenance")
    let orderProvUri = Uri("http://example.org/api/order/42/provenance")

    use store = new TripleStore()
    personProvGraph.BaseUri <- personProvUri
    orderProvGraph.BaseUri <- orderProvUri
    store.Add(personProvGraph)
    store.Add(orderProvGraph)

    // Act: Query ONLY the person's provenance graph
    // This verifies that querying one resource's provenance graph
    // does not leak triples from another resource's provenance.
    let personResults = executeSparqlOnDataset store $"""
        PREFIX prov: <http://www.w3.org/ns/prov#>

        # Query only the person's provenance named graph.
        # Should NOT contain any order-related provenance.
        SELECT ?activity ?entity
        WHERE {{
            GRAPH <{personProvUri}> {{
                ?activity prov:used ?entity .
            }}
        }}
    """

    let orderResults = executeSparqlOnDataset store $"""
        PREFIX prov: <http://www.w3.org/ns/prov#>

        # Query only the order's provenance named graph.
        SELECT ?activity ?entity
        WHERE {{
            GRAPH <{orderProvUri}> {{
                ?activity prov:used ?entity .
            }}
        }}
    """

    // Assert: Each graph's results should only reference its own resource
    // Person provenance should not mention order URIs and vice versa
    for result in personResults do
        let entityStr = result.["entity"].ToString()
        Expect.isFalse (entityStr.Contains("order"))
            $"Person provenance should not contain order references: {entityStr}"

    for result in orderResults do
        let entityStr = result.["entity"].ToString()
        Expect.isFalse (entityStr.Contains("person"))
            $"Order provenance should not contain person references: {entityStr}"
}
```

**Notes**:
- The entity URI check is a heuristic -- the actual validation depends on how `GraphBuilder.toGraph` encodes resource URIs
- A stronger check would compare entity URIs against expected resource URI prefixes

---

### Subtask T022 -- US3-SC4: GRAPH Clause Targeting (FR-010)

- **Purpose**: Verify that a SPARQL query using the `GRAPH` clause to target a specific provenance named graph returns only triples from that graph when executed against the full dataset.
- **Files**: `test/Frank.RdfValidation.Tests/ProvenanceGraphTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

```fsharp
testAsync "US3-SC4: GRAPH clause targets specific provenance graph in full dataset" {
    // Arrange: Build a dataset with default graph + 2 provenance named graphs
    use host = createTestHost ()
    let server = host.GetTestServer()
    use client = server.CreateClient()
    let! body = getRdfResponse client "/person/1" "text/turtle"
    use resourceGraph = loadTurtleGraph body

    let records = makeTestProvenanceRecords "http://example.org/api/person/1"
    use provGraph = GraphBuilder.toGraph records
    let provUri = Uri("http://example.org/api/person/1/provenance")

    use store = createProvenanceDataset resourceGraph provGraph provUri

    // Act: Count triples in default graph vs named graph
    // The GRAPH clause should isolate queries to the named graph only.
    let defaultGraphTriples = executeSparqlOnDataset store """
        # Count triples in the default (resource) graph.
        SELECT (COUNT(*) AS ?count)
        WHERE {
            ?s ?p ?o .
        }
    """

    let namedGraphTriples = executeSparqlOnDataset store $"""
        # Count triples ONLY in the provenance named graph.
        # This should be different from the default graph count,
        # proving that GRAPH clause isolation works.
        SELECT (COUNT(*) AS ?count)
        WHERE {{
            GRAPH <{provUri}> {{
                ?s ?p ?o .
            }}
        }}
    """

    // Assert: Both counts should be > 0 and different
    let defaultCount =
        defaultGraphTriples.[0].["count"].ToString() |> int
    let namedCount =
        namedGraphTriples.[0].["count"].ToString() |> int

    Expect.isGreaterThan defaultCount 0
        "Default graph should have resource triples"
    Expect.isGreaterThan namedCount 0
        "Named provenance graph should have provenance triples"

    // The counts being different proves isolation
    Expect.notEqual defaultCount namedCount
        "Default and named graph triple counts should differ (different data)"
}
```

**Notes**:
- `COUNT(*)` in SPARQL returns a single result row with the count
- The assertion that counts differ is a proxy for isolation; the T019 test provides stronger predicate-level verification

---

### Subtask T023 -- Edge Case: Empty Provenance Graph

- **Purpose**: Verify that when provenance is enabled but no state transitions have occurred, the provenance named graph exists but is empty, and SPARQL queries return empty results (not errors).
- **Files**: `test/Frank.RdfValidation.Tests/ProvenanceGraphTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

```fsharp
testAsync "US3-Edge: Empty provenance graph returns empty results, not errors" {
    // Arrange: Create an empty provenance graph (no records)
    use emptyProvGraph = GraphBuilder.toGraph []
    let provUri = Uri("http://example.org/api/person/1/provenance")

    use store = new TripleStore()
    emptyProvGraph.BaseUri <- provUri
    store.Add(emptyProvGraph)

    // Act: Query for activities in the empty provenance graph
    // When no state transitions have occurred, the graph exists but is empty.
    // SPARQL should return an empty result set, NOT throw an error.
    let results = executeSparqlOnDataset store $"""
        PREFIX prov: <http://www.w3.org/ns/prov#>

        # Query for activities in an empty provenance graph.
        # Expected: empty result set (zero rows), no errors.
        SELECT ?activity
        WHERE {{
            GRAPH <{provUri}> {{
                ?activity a prov:Activity .
            }}
        }}
    """

    // Assert: Empty result set, not error
    Expect.equal results.Count 0
        "Empty provenance graph should return zero results, not throw"
}
```

**Notes**:
- `GraphBuilder.toGraph []` may or may not produce a graph with zero triples -- it might still have namespace prefix declarations. That's fine; the query looks for `prov:Activity` instances, of which there should be none.
- If `GraphBuilder.toGraph` does not accept an empty list, create a `new Graph()` directly.

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| `GraphBuilder.toGraph` API may have changed or take different arguments | Inspect `src/Frank.Provenance/GraphBuilder.fs` for exact signature; adapt accordingly |
| Named graph URI convention unknown | Check `GraphBuilder` source and Provenance middleware for naming convention; default to `{resourceUri}/provenance` |
| `TripleStore.Add` named graph API may differ in dotNetRdf 3.5.1 | Test that `BaseUri` assignment + `store.Add(graph)` creates a named graph; alternative: use `store.Add(graph, true)` or explicit named graph API |
| SPARQL string interpolation with URIs may break with special characters | Use `$"""..."""` F# interpolated strings; ensure URIs are properly formatted |

---

## Review Guidance

- Verify all 6 test cases are present (T018-T023) with `"US3-SC#"` naming
- Verify named graph isolation is properly tested (predicates in provenance graph are PROV-O only)
- Verify cross-resource leakage test uses 2+ independent resources
- Verify GRAPH clause is used in all provenance-scoped queries
- Verify edge case handles empty graph gracefully (returns empty results, not errors)
- Verify `use` bindings for all `IGraph`, `TripleStore` instances
- Run `dotnet test test/Frank.RdfValidation.Tests/ --filter "US3"` to confirm all tests pass

---

## Activity Log

- 2026-03-15T23:59:02Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T14:33:11Z – unknown – lane=done – Moved to done
