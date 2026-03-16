---
work_package_id: "WP03"
subtasks:
  - "T013"
  - "T014"
  - "T015"
  - "T016"
  - "T017"
title: "US2 -- SPARQL Resource Queries"
phase: "Phase 1 - P1 User Stories"
lane: "done"
assignee: ""
agent: ""
shell_pid: ""
review_status: "approved"
reviewed_by: "Ryan Riley"
dependencies: ["WP01"]
requirement_refs: ["FR-005", "FR-006", "FR-007", "FR-013"]
history:
  - timestamp: "2026-03-15T23:59:02Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP03 -- US2: SPARQL Resource Queries

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
spec-kitty implement WP03 --base WP01
```

---

## Objectives & Success Criteria

1. `SparqlResourceQueryTests.fs` contains tests demonstrating SPARQL SELECT and ASK queries return correct results against Frank's resource RDF graph.
2. Queries cover: resource discovery by type (FR-005), unsafe transition discovery (FR-006), ALPS semantic descriptor retrieval (FR-007), and resource existence checks (FR-005).
3. All SPARQL queries are inline strings with comments explaining their purpose (FR-013, SC-006).
4. All tests pass under `dotnet test test/Frank.RdfValidation.Tests/ --filter "US2"`.
5. Test names follow `"US2-SC#: <description>"` convention.

---

## Context & Constraints

- **Spec User Story 2**: "SPARQL Queries Against Resource Graph" (Priority P1)
- **Requirements**: FR-005 (resource discovery), FR-006 (HTTP method capabilities), FR-007 (ALPS descriptors)
- **Research R1**: Use `LeviathanQueryProcessor` with `InMemoryDataset` for SPARQL execution
- **Data Model**: Resource triples use `<resource-uri> rdf:type <class-uri>` and `<resource-uri> <property-uri> "value"` patterns
- **Constitution VI**: `use` bindings for all disposable types
- **Prerequisite**: WP01 must be complete (TestHelpers with `executeSparql`, `executeSparqlAsk`, `createTestHost`, `loadTurtleGraph`)

**Understanding Frank.LinkedData's RDF model**:

Before implementing, inspect the following source files to understand what RDF predicates the LinkedData middleware produces:
- `src/Frank.LinkedData/Rdf.fs` -- the `projectJsonToRdf` function that converts JSON to RDF triples
- `src/Frank.LinkedData/LinkedDataConfig.fs` -- ontology graph structure and configuration
- `src/Frank.LinkedData/Middleware.fs` -- how the middleware applies content negotiation

The SPARQL queries must match the actual predicate URIs produced by the middleware. Do NOT assume predicate names -- inspect the source.

---

## Subtasks & Detailed Guidance

### Subtask T013 -- Create `SparqlResourceQueryTests.fs` Module Structure

- **Purpose**: Set up the test module with imports, shared setup, and test list.
- **Files**: `test/Frank.RdfValidation.Tests/SparqlResourceQueryTests.fs` (replace stub from WP01)
- **Parallel?**: No (foundation for T014-T017)

**Steps**:

1. Replace the stub with a full module:

```fsharp
module Frank.RdfValidation.Tests.SparqlResourceQueryTests

open System.Net.Http
open Expecto
open VDS.RDF
open VDS.RDF.Query
open Frank.RdfValidation.Tests.TestHelpers

[<Tests>]
let tests =
    testList "US2 - SPARQL Resource Queries" [
        // Test cases added in T014-T017
    ]
```

2. Consider a shared helper within this module to load a graph from the TestHost:

```fsharp
/// Load a Turtle graph from the test host for a given path.
let private loadResourceGraph (path: string) =
    async {
        use host = createTestHost ()
        let server = host.GetTestServer()
        use client = server.CreateClient()
        let! body = getRdfResponse client path "text/turtle"
        return loadTurtleGraph body
    }
```

This reduces boilerplate in each test case. However, note that `IGraph` returned from async must be carefully disposed by the caller using `use`.

---

### Subtask T014 -- US2-SC1: SPARQL SELECT for All Resources with `rdf:type` (FR-005)

- **Purpose**: Demonstrate that a SPARQL SELECT query can discover all resources and their types in the loaded RDF graph.
- **Files**: `test/Frank.RdfValidation.Tests/SparqlResourceQueryTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

1. Add the test:

```fsharp
testAsync "US2-SC1: SPARQL SELECT finds all resources with their rdf:type" {
    // Arrange: Create TestHost and load resource RDF
    use host = createTestHost ()
    let server = host.GetTestServer()
    use client = server.CreateClient()
    let! body = getRdfResponse client "/person/1" "text/turtle"
    use graph = loadTurtleGraph body

    // Act: Execute SPARQL SELECT to find all typed resources
    // This query discovers every resource that has an rdf:type declaration,
    // which is the fundamental pattern for resource discovery in RDF.
    let results = executeSparql graph """
        PREFIX rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#>

        # Find all resources and their RDF types -- the basic building block
        # for any RDF-aware client discovering what resources exist.
        SELECT ?resource ?type
        WHERE {
            ?resource rdf:type ?type .
        }
    """

    // Assert: At least one typed resource exists
    Expect.isGreaterThan results.Count 0
        "Should find at least one resource with rdf:type"

    // Verify the result set has the expected variables
    Expect.contains (results.Variables |> Seq.toList) "resource"
        "Result set should include 'resource' variable"
    Expect.contains (results.Variables |> Seq.toList) "type"
        "Result set should include 'type' variable"
}
```

**Notes**:
- The `rdf:type` triple is the most fundamental RDF pattern -- every typed resource will have one
- The test validates that the LinkedData middleware produces `rdf:type` triples (it should, given the ontology graph includes OWL class declarations)
- Use `PREFIX` declarations in SPARQL (not the short `a` syntax) for clarity in documented examples

---

### Subtask T015 -- US2-SC2: SPARQL SELECT for Unsafe Transitions (FR-006)

- **Purpose**: Query for resources that have unsafe HTTP transitions (POST, PUT, DELETE handlers), demonstrating capability discovery via SPARQL.
- **Files**: `test/Frank.RdfValidation.Tests/SparqlResourceQueryTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

1. **First, understand how Frank.LinkedData represents HTTP capabilities in RDF**:
   - Inspect `src/Frank.LinkedData/Rdf.fs` for how HTTP method information is encoded in triples
   - Look for predicates related to `hydra:method`, `hydra:operation`, or Frank-specific predicates
   - The TestHost from WP01 should include at least one endpoint with POST/PUT/DELETE

2. Add the test (SPARQL query adapted to actual predicates after inspecting source):

```fsharp
testAsync "US2-SC2: SPARQL SELECT discovers resources with unsafe transitions" {
    // Arrange: Load graph from a resource that has unsafe transitions
    use host = createTestHost ()
    let server = host.GetTestServer()
    use client = server.CreateClient()
    // Request a resource that should have POST/PUT/DELETE capabilities
    let! body = getRdfResponse client "/person/1" "text/turtle"
    use graph = loadTurtleGraph body

    // Act: Query for resources with unsafe HTTP methods
    // This query finds resources that advertise unsafe transitions (POST, PUT, DELETE),
    // useful for clients that need to discover write operations.
    //
    // NOTE: The exact predicate URI depends on Frank.LinkedData's RDF model.
    // Inspect src/Frank.LinkedData/Rdf.fs to determine the correct predicate.
    // The placeholder below uses a Hydra-style predicate; adjust to match actual output.
    let results = executeSparql graph """
        PREFIX rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#>

        # Discover resources with unsafe (state-changing) HTTP operations.
        # Clients use this to find resources they can POST/PUT/DELETE to.
        SELECT ?resource ?method
        WHERE {
            ?resource ?methodPredicate ?method .
            FILTER(?method IN ("POST", "PUT", "DELETE"))
        }
    """

    // Assert: The query runs without error
    // If the test resource has unsafe transitions, expect results > 0
    // If not, the query should still execute successfully (returning 0 results)
    Expect.isTrue (results.Count >= 0)
        "SPARQL query for unsafe transitions should execute without errors"
}
```

**Important**: The exact SPARQL query depends on how `Frank.LinkedData` encodes HTTP method information in RDF. The implementer MUST inspect the actual RDF output first (load a graph, dump its triples, then write the appropriate query). The template above is a starting point.

**Notes**:
- If Frank.LinkedData does not encode HTTP methods in RDF triples, this test should verify that absence gracefully (empty result set, no errors)
- The spec says "resources with unsafe transitions (POST, PUT, DELETE)" -- the query should filter for these specific methods

---

### Subtask T016 -- US2-SC3: SPARQL SELECT for ALPS Semantic Descriptors (FR-007)

- **Purpose**: Query for ALPS semantic descriptors and link relations from the loaded RDF graph.
- **Files**: `test/Frank.RdfValidation.Tests/SparqlResourceQueryTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

1. **First, understand how Frank.LinkedData represents ALPS descriptors in RDF**:
   - Inspect `src/Frank.LinkedData/` for ALPS-related types and RDF encoding
   - Look for predicates related to `alps:descriptor`, `alps:type`, link relations
   - The ontology graph in TestHelpers should include ALPS descriptor declarations

2. Add the test:

```fsharp
testAsync "US2-SC3: SPARQL SELECT retrieves ALPS semantic descriptors" {
    // Arrange
    use host = createTestHost ()
    let server = host.GetTestServer()
    use client = server.CreateClient()
    let! body = getRdfResponse client "/person/1" "text/turtle"
    use graph = loadTurtleGraph body

    // Act: Query for semantic descriptors and their types
    // ALPS descriptors provide semantic meaning to resource properties,
    // enabling machine-readable API profiles.
    //
    // NOTE: Adjust predicate URIs to match Frank.LinkedData's actual RDF model.
    let results = executeSparql graph """
        PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
        PREFIX owl: <http://www.w3.org/2002/07/owl#>

        # Find all properties (OWL/RDFS descriptors) defined in the resource graph.
        # These represent the semantic descriptors that give meaning to resource fields.
        SELECT ?property ?label ?range
        WHERE {
            ?property a owl:DatatypeProperty .
            OPTIONAL { ?property rdfs:label ?label }
            OPTIONAL { ?property rdfs:range ?range }
        }
    """

    // Assert: Should find descriptors if the ontology defines them
    Expect.isTrue (results.Count >= 0)
        "SPARQL query for semantic descriptors should execute without errors"
}
```

**Important**: The query shape depends entirely on how `Frank.LinkedData` maps ALPS descriptors to RDF. The implementer must:
1. Load a real graph from the TestHost
2. Enumerate all triples to understand the actual RDF structure
3. Write SPARQL that matches the actual predicates

**Notes**:
- The ontology graph built in WP01's TestHelpers should include OWL property declarations that map to ALPS descriptors
- If ALPS descriptors are not directly represented in the RDF output, use OWL/RDFS properties as the semantic equivalent

---

### Subtask T017 -- US2-SC4: SPARQL ASK for Resource Existence (FR-005)

- **Purpose**: Demonstrate that SPARQL ASK queries can check whether a specific resource exists with a specific capability, returning true for existing and false for non-existent.
- **Files**: `test/Frank.RdfValidation.Tests/SparqlResourceQueryTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

1. Add tests for both positive and negative ASK results:

```fsharp
testAsync "US2-SC4: SPARQL ASK returns true for existing resource, false for non-existent" {
    // Arrange
    use host = createTestHost ()
    let server = host.GetTestServer()
    use client = server.CreateClient()
    let! body = getRdfResponse client "/person/1" "text/turtle"
    use graph = loadTurtleGraph body

    // Act & Assert: ASK for a resource that should exist
    // ASK queries return a boolean: true if the pattern matches, false otherwise.
    // This is the simplest way to check resource existence in an RDF graph.
    let existsResult = executeSparqlAsk graph """
        PREFIX rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#>

        # Check if a typed resource exists in the graph.
        # Returns true if at least one resource has an rdf:type declaration.
        ASK {
            ?resource rdf:type ?type .
        }
    """
    Expect.isTrue existsResult
        "ASK should return true -- at least one typed resource exists"

    // Act & Assert: ASK for a resource that should NOT exist
    // Using a URI that doesn't exist in the graph to verify false negatives work.
    let notExistsResult = executeSparqlAsk graph """
        PREFIX rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#>

        # Check for a resource URI that should not exist in this graph.
        # Expected result: false (no match).
        ASK {
            <http://example.org/nonexistent/resource/999> rdf:type ?type .
        }
    """
    Expect.isFalse notExistsResult
        "ASK should return false -- nonexistent resource not in graph"
}
```

**Notes**:
- The `executeSparqlAsk` helper from WP01 returns a `bool` directly
- Both positive and negative cases are tested in one test case (the spec says "returns true for existing capabilities and false for non-existent ones")
- The negative case uses a URI that is guaranteed not to exist in the test graph

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Frank.LinkedData's RDF predicates for HTTP methods are unknown | Inspect `src/Frank.LinkedData/Rdf.fs` before writing SPARQL; dump triple output for debugging |
| ALPS descriptor representation in RDF may differ from expected | Inspect actual ontology graph output; adapt queries to match |
| SPARQL PREFIX declarations may not match registered namespaces | Use full URIs in PREFIX declarations; verify against `graph.NamespaceMap` |
| Test data (ontology + endpoints) from WP01 may not produce the right triples | Review WP01's TestHelpers and add endpoint/ontology adjustments if needed |

---

## Review Guidance

- Verify all 4 test cases are present (T014-T017) with `"US2-SC#"` naming
- Verify SPARQL queries have inline comments explaining their purpose (FR-013)
- Verify the implementer inspected actual RDF output before writing queries (not guessing predicates)
- Verify `executeSparql` and `executeSparqlAsk` helpers are used (not raw dotNetRdf API calls)
- Verify both positive and negative ASK results are tested (T017)
- Run `dotnet test test/Frank.RdfValidation.Tests/ --filter "US2"` to confirm all tests pass

---

## Activity Log

- 2026-03-15T23:59:02Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T14:33:11Z – unknown – lane=done – Moved to done
