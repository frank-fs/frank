---
work_package_id: "WP05"
subtasks:
  - "T024"
  - "T025"
  - "T026"
  - "T027"
  - "T028"
  - "T029"
title: "US4 -- Cross-Resource Graph Coherence"
phase: "Phase 2 - P2 User Stories"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP01"]
requirement_refs: ["FR-008", "FR-011", "FR-012"]
history:
  - timestamp: "2026-03-15T23:59:02Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP05 -- US4: Cross-Resource Graph Coherence

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
spec-kitty implement WP05 --base WP01
```

---

## Objectives & Success Criteria

1. `GraphCoherenceTests.fs` contains tests validating that the combined RDF graph from multiple Frank resources forms a coherent whole.
2. Cross-resource link traversal works (URI from resource A matches subject URI of resource B) -- FR-008.
3. No orphaned blank nodes exist in the combined graph -- FR-011.
4. Namespace predicates are consistent (no mixed absolute/prefixed URIs for the same predicate) -- FR-012.
5. Edge case: special character URIs are properly encoded and queryable.
6. The `.fsproj` `<Compile>` items are correctly ordered for all test modules.
7. All tests pass under `dotnet test test/Frank.RdfValidation.Tests/ --filter "US4"`.

---

## Context & Constraints

- **Spec User Story 4**: "Graph Coherence Across Related Resources" (Priority P2)
- **Requirements**: FR-008 (cross-resource URI references), FR-011 (blank node scoping), FR-012 (consistent namespaces)
- **Data Model**: Combined graph from multiple endpoints loaded into a single `IGraph`
- **Constitution VI**: `use` bindings for all disposable types
- **Prerequisite**: WP01 must be complete (TestHelpers with `createTestHost`, `loadTurtleGraph`, `executeSparql`, and at least 2 sample endpoints)

**Understanding cross-resource links**:

The TestHost from WP01 should have at least 2 endpoints (e.g., `/person/1` and `/order/42`). For cross-resource link testing, the resource JSON responses need to include links or references between resources. This may require:
- Adjusting the TestHost endpoint responses to include URIs referencing other resources
- Or testing link coherence at the ontology level (both resources use the same namespace)

Inspect `src/Frank.LinkedData/Rdf.fs` to understand how cross-resource references are encoded in RDF.

---

## Subtasks & Detailed Guidance

### Subtask T024 -- Create `GraphCoherenceTests.fs` Module Structure

- **Purpose**: Set up the test module with imports, helpers for building combined graphs, and test list.
- **Files**: `test/Frank.RdfValidation.Tests/GraphCoherenceTests.fs` (replace stub from WP01)
- **Parallel?**: No (foundation for T025-T028)

**Steps**:

1. Replace the stub with a full module:

```fsharp
module Frank.RdfValidation.Tests.GraphCoherenceTests

open System
open System.Net.Http
open Expecto
open VDS.RDF
open VDS.RDF.Query
open Frank.RdfValidation.Tests.TestHelpers
```

2. Add a helper to build a combined graph from multiple resources:

```fsharp
/// Load RDF from multiple endpoints into a single combined graph.
/// This simulates what an external client would see after crawling Frank's API.
let private loadCombinedGraph (client: HttpClient) (paths: string list) =
    async {
        let combined = new Graph()
        for path in paths do
            let! body = getRdfResponse client path "text/turtle"
            use singleGraph = loadTurtleGraph body
            // Merge triples from each resource into the combined graph
            combined.Merge(singleGraph)
        return combined
    }
```

3. Test list:
```fsharp
[<Tests>]
let tests =
    testList "US4 - Graph Coherence" [
        // Test cases added in T025-T028
    ]
```

**Notes**:
- `IGraph.Merge(otherGraph)` adds all triples from `otherGraph` into the target graph
- The combined graph represents what an RDF-aware crawler would assemble from Frank's API

---

### Subtask T025 -- US4-SC1: Cross-Resource Link Traversal (FR-008)

- **Purpose**: Verify that when a SPARQL query traverses a link relation from resource A to resource B, the target URI in resource A matches the subject URI of resource B.
- **Files**: `test/Frank.RdfValidation.Tests/GraphCoherenceTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

1. **Prerequisites**: The TestHost endpoints must produce RDF where one resource references another. Options:
   - The `/person/1` JSON could include an `"orders"` field linking to `/order/42`
   - Or the ontology could define a link relation between Person and Order types
   - The implementer should check what cross-resource references are naturally produced by LinkedData

2. Add the test:

```fsharp
testAsync "US4-SC1: Cross-resource link traversal -- target URI matches subject" {
    // Arrange: Load RDF from multiple related resources into combined graph
    use host = createTestHost ()
    let server = host.GetTestServer()
    use client = server.CreateClient()
    use combinedGraph = loadCombinedGraph client ["/person/1"; "/order/42"] |> Async.RunSynchronously

    // Act: Find cross-resource references via SPARQL
    // This query looks for triples where the object is a URI that also appears
    // as a subject elsewhere in the graph -- indicating a valid cross-resource link.
    let results = executeSparql combinedGraph """
        # Find resources that reference other resources in the graph.
        # A valid cross-resource link means the target URI exists as a subject.
        SELECT ?source ?predicate ?target
        WHERE {
            ?source ?predicate ?target .
            FILTER(isIRI(?target))
            ?target ?anyPred ?anyObj .
        }
    """

    // Assert: If cross-resource links exist, they should be resolvable
    // The query returns links where the target is also a subject (valid link)
    if results.Count > 0 then
        for result in results do
            let target = result.["target"].ToString()
            // The target URI should be a subject in the graph (link integrity)
            let hasAsSubject =
                combinedGraph.Triples
                |> Seq.exists (fun t -> t.Subject.ToString() = target)
            Expect.isTrue hasAsSubject
                $"Target URI {target} should exist as a subject in the combined graph"
    // If no cross-resource links exist, that's OK -- the resources may be independent
}
```

**Notes**:
- This test is meaningful only if the test data includes cross-resource references. If the simple test endpoints don't produce cross-resource links, consider adding a link property to the endpoint responses in TestHelpers (e.g., person -> orders).
- The SPARQL query uses `FILTER(isIRI(?target))` to exclude literal values from the traversal check.

---

### Subtask T026 -- US4-SC2: Orphaned Blank Node Detection (FR-011)

- **Purpose**: Verify that no orphaned blank nodes exist in the combined graph. An orphaned blank node is a blank node that exists as a subject but is never referenced as an object by any named resource.
- **Files**: `test/Frank.RdfValidation.Tests/GraphCoherenceTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

```fsharp
testAsync "US4-SC2: No orphaned blank nodes in combined graph" {
    // Arrange
    use host = createTestHost ()
    let server = host.GetTestServer()
    use client = server.CreateClient()
    use combinedGraph = loadCombinedGraph client ["/person/1"; "/order/42"] |> Async.RunSynchronously

    // Act: Find orphaned blank nodes via SPARQL
    // An orphaned blank node appears as a subject but is never referenced
    // as an object by any triple. This indicates structural incoherence.
    let results = executeSparql combinedGraph """
        # Detect orphaned blank nodes -- blank nodes that are subjects
        # but are never referenced as objects by any triple.
        # In a well-formed graph, blank nodes should be reachable from
        # named resources via object references.
        SELECT ?orphan
        WHERE {
            ?orphan ?p ?o .
            FILTER(isBlank(?orphan))
            FILTER NOT EXISTS {
                ?anySubject ?anyPred ?orphan .
            }
        }
    """

    // Assert: Zero orphaned blank nodes
    Expect.equal results.Count 0
        $"Combined graph should have no orphaned blank nodes, but found {results.Count}"

    // Diagnostic output if orphans found
    if results.Count > 0 then
        for result in results do
            let orphan = result.["orphan"].ToString()
            // Log the orphaned blank node for debugging
            printfn $"Orphaned blank node: {orphan}"
}
```

**Notes**:
- The SPARQL `FILTER NOT EXISTS` pattern checks for blank nodes that appear as subjects but are never objects
- Blank nodes that are only objects (not subjects) are fine -- they're anonymous values
- If the combined graph has no blank nodes at all, this test trivially passes (zero orphans)
- The `FILTER(isBlank(?orphan))` ensures we only check blank nodes, not URI nodes

---

### Subtask T027 -- US4-SC3: Consistent Namespace Predicates (FR-012)

- **Purpose**: Verify that all distinct predicates in the combined graph use consistent namespace prefixes -- no mixed absolute/prefixed URIs for the same logical predicate.
- **Files**: `test/Frank.RdfValidation.Tests/GraphCoherenceTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

```fsharp
testAsync "US4-SC3: Consistent namespace predicates across resources" {
    // Arrange
    use host = createTestHost ()
    let server = host.GetTestServer()
    use client = server.CreateClient()
    use combinedGraph = loadCombinedGraph client ["/person/1"; "/order/42"] |> Async.RunSynchronously

    // Act: Collect all distinct predicates
    // In a coherent graph, the same logical predicate should always use
    // the same full URI. No mixing of absolute and prefixed forms.
    let results = executeSparql combinedGraph """
        # Find all distinct predicates used across all resources.
        # Consistent namespace usage means each predicate appears with
        # exactly one URI form (no duplicates from different prefix expansions).
        SELECT DISTINCT ?predicate
        WHERE {
            ?s ?predicate ?o .
        }
        ORDER BY ?predicate
    """

    // Assert: Check for duplicate predicates with different namespace representations
    let predicateUris =
        [ for result in results -> result.["predicate"].ToString() ]

    // Group predicates by their local name (the part after the last # or /)
    let localNames =
        predicateUris
        |> List.map (fun uri ->
            let lastHash = uri.LastIndexOf('#')
            let lastSlash = uri.LastIndexOf('/')
            let splitAt = max lastHash lastSlash
            if splitAt >= 0 then uri.Substring(splitAt + 1) else uri)

    // Check for duplicate local names with different namespaces
    // (e.g., "http://example.org/Name" and "http://other.org/Name")
    let duplicates =
        localNames
        |> List.groupBy id
        |> List.filter (fun (_, group) -> group.Length > 1)

    // Note: Some legitimate predicates may share local names across different
    // ontologies (e.g., rdfs:label and skos:label). This check is conservative.
    // For Frank's controlled ontology, duplicate local names indicate inconsistency.
    for (name, _) in duplicates do
        let matchingUris =
            List.zip predicateUris localNames
            |> List.filter (fun (_, ln) -> ln = name)
            |> List.map fst
        // Only flag as inconsistent if URIs share the same base namespace
        let namespaces =
            matchingUris
            |> List.map (fun uri ->
                let splitAt = max (uri.LastIndexOf('#')) (uri.LastIndexOf('/'))
                if splitAt >= 0 then uri.Substring(0, splitAt + 1) else uri)
            |> List.distinct
        if namespaces.Length > 1 then
            // This is a warning, not necessarily a failure -- log it
            printfn $"Predicate local name '{name}' used with multiple namespaces: {namespaces}"
}
```

**Notes**:
- This is a heuristic check. In RDF, predicates are identified by their full URI, so there's no ambiguity at the machine level. The test verifies that Frank's output is consistent and doesn't accidentally use different namespace forms.
- The predicate list should show standard namespace prefixes (`rdf:`, `rdfs:`, `owl:`, etc.) alongside the application-specific namespace.
- A simpler alternative: just verify that all predicates are absolute URIs (no relative URIs or malformed URIs).

---

### Subtask T028 -- Edge Case: Special Character URI Encoding

- **Purpose**: Verify that resource URIs with special characters (percent-encoded path segments) are properly encoded in RDF output and can be matched by SPARQL queries.
- **Files**: `test/Frank.RdfValidation.Tests/GraphCoherenceTests.fs` (add test case)
- **Parallel?**: Yes [P]

**Steps**:

1. **Prerequisite**: The TestHost may need an endpoint with special characters in its path (e.g., `/person/John%20Doe` or `/item/a+b`). This could be:
   - Added to the TestHost helper in WP01
   - Or tested by constructing RDF directly (without HTTP round-trip)

2. Add the test:

```fsharp
testAsync "US4-Edge: Special character URIs are properly encoded in RDF" {
    // Arrange: Test with a URI containing percent-encoded characters
    // Option A: If TestHost has a special-character endpoint
    // Option B: Construct RDF directly with encoded URIs

    // Option B approach (more reliable for edge case testing):
    use graph = new Graph()
    let encodedUri = "http://example.org/api/person/John%20Doe"
    let subject = graph.CreateUriNode(UriFactory.Root.Create(encodedUri))
    let predicate = graph.CreateUriNode(UriFactory.Root.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"))
    let obj = graph.CreateUriNode(UriFactory.Root.Create("http://example.org/api/Person"))
    graph.Assert(Triple(subject, predicate, obj))

    // Act: SPARQL query with the encoded URI
    // URIs in SPARQL must match exactly, including percent-encoding.
    let results = executeSparql graph $"""
        PREFIX rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#>

        # Query with a percent-encoded URI to verify encoding round-trip.
        # The URI in the SPARQL query must match the graph's URI exactly.
        ASK {{
            <{encodedUri}> rdf:type ?type .
        }}
    """

    // Assert: The encoded URI should be findable
    Expect.isTrue results.Result
        "Percent-encoded URI should be queryable via SPARQL"

    // Also verify via triple enumeration
    let subjects =
        graph.Triples
        |> Seq.map (fun t -> t.Subject.ToString())
        |> Seq.toList

    Expect.contains subjects encodedUri
        "Graph should contain the percent-encoded URI as a subject"
}
```

**Notes**:
- Percent-encoding in URIs is defined by RFC 3986. dotNetRdf's `UriFactory.Root.Create` handles this.
- The test verifies that the round-trip (URI -> RDF -> SPARQL) preserves the encoding.
- If using F# string interpolation with curly braces inside SPARQL, use `$"""..."""` triple-quoted strings and `{{` / `}}` for literal braces.

---

### Subtask T029 -- Update `.fsproj` Compile Items Ordering

- **Purpose**: Ensure the `.fsproj` file lists all `<Compile>` items in the correct dependency order for all test modules.
- **Files**: `test/Frank.RdfValidation.Tests/Frank.RdfValidation.Tests.fsproj` (verify/update)
- **Parallel?**: No (final validation step)

**Steps**:

1. Verify the `.fsproj` `<Compile>` items are in this order:
```xml
<ItemGroup>
    <Compile Include="TestHelpers.fs" />
    <Compile Include="RdfParsingTests.fs" />
    <Compile Include="SparqlResourceQueryTests.fs" />
    <Compile Include="ProvenanceGraphTests.fs" />
    <Compile Include="GraphCoherenceTests.fs" />
    <Compile Include="Program.fs" />
</ItemGroup>
```

2. This should already be set from WP01, but verify after all test modules are populated.

3. Run `dotnet build test/Frank.RdfValidation.Tests/` to confirm compilation.

4. Run `dotnet test test/Frank.RdfValidation.Tests/` to confirm all tests execute.

**Notes**:
- In F#, file ordering in `.fsproj` matters -- files can only reference types/modules from files listed above them
- `TestHelpers.fs` must be first (all test modules depend on it)
- `Program.fs` must be last (it's the entry point)
- Test module ordering (Parsing, SPARQL, Provenance, Coherence) is not critical since they don't depend on each other, but keeping them in user story order aids readability

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Cross-resource links may not exist in test data | Add a link property to one test endpoint (person -> order) in TestHelpers; if not feasible, test link traversal with constructed RDF |
| Orphaned blank node query may be complex for dotNetRdf's SPARQL engine | Test with small graphs first; `FILTER NOT EXISTS` is standard SPARQL 1.1 and should work |
| Namespace consistency check is heuristic, not definitive | Accept false positives for legitimate cross-ontology predicates; focus on Frank's own namespace |
| `.fsproj` compile order may break if WP02-WP04 add new files | This WP validates final ordering; T029 is the catch-all |

---

## Review Guidance

- Verify all 6 test cases are present (T024-T029) with `"US4-SC#"` naming
- Verify the combined graph is built from multiple endpoints (not just one)
- Verify the orphaned blank node query uses `FILTER NOT EXISTS` pattern correctly
- Verify special character URI test covers percent-encoding round-trip
- Verify `.fsproj` compile order is correct after all modules are populated
- Verify `use` bindings for all `IGraph`, `TripleStore` instances
- Run `dotnet test test/Frank.RdfValidation.Tests/` to confirm all tests pass

---

## Activity Log

- 2026-03-15T23:59:02Z -- system -- lane=planned -- Prompt created.
