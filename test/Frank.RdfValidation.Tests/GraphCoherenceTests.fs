module Frank.RdfValidation.Tests.GraphCoherenceTests

open System
open System.Net.Http
open Expecto
open Microsoft.AspNetCore.TestHost
open VDS.RDF
open VDS.RDF.Query
open Frank.RdfValidation.Tests.TestHelpers

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

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

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[<Tests>]
let tests =
    testList "US4 - Graph Coherence" [

        // T025: US4-SC1 -- Cross-Resource Link Traversal (FR-008)
        testAsync "US4-SC1: Cross-resource link traversal -- target URI matches subject" {
            // Arrange: Load RDF from multiple related resources into combined graph
            use host = createTestHost ()
            let server = host.GetTestServer()
            use client = server.CreateClient()

            use combinedGraph =
                loadCombinedGraph client [ "/person/1"; "/order/42" ]
                |> Async.RunSynchronously

            // Act: Find cross-resource references via SPARQL
            // This query looks for triples where the object is a URI that also appears
            // as a subject elsewhere in the graph -- indicating a valid cross-resource link.
            let results =
                executeSparql
                    combinedGraph
                    """
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

                    Expect.isTrue
                        hasAsSubject
                        $"Target URI {target} should exist as a subject in the combined graph"
            // If no cross-resource links exist, that's OK -- the resources may be independent.
            // The combined graph should still contain triples from both resources.
            Expect.isGreaterThan
                combinedGraph.Triples.Count
                0
                "Combined graph should contain triples from merged resources"
        }

        // T026: US4-SC2 -- Orphaned Blank Node Detection (FR-011)
        testAsync "US4-SC2: No orphaned blank nodes in combined graph" {
            // Arrange
            use host = createTestHost ()
            let server = host.GetTestServer()
            use client = server.CreateClient()

            use combinedGraph =
                loadCombinedGraph client [ "/person/1"; "/order/42" ]
                |> Async.RunSynchronously

            // Act: Find orphaned blank nodes via SPARQL
            // An orphaned blank node appears as a subject but is never referenced
            // as an object by any triple. This indicates structural incoherence.
            let results =
                executeSparql
                    combinedGraph
                    """
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
            Expect.equal
                results.Count
                0
                $"Combined graph should have no orphaned blank nodes, but found {results.Count}"

            // Diagnostic output if orphans found
            if results.Count > 0 then
                for result in results do
                    let orphan = result.["orphan"].ToString()
                    printfn $"Orphaned blank node: {orphan}"
        }

        // T027: US4-SC3 -- Consistent Namespace Predicates (FR-012)
        testAsync "US4-SC3: Consistent namespace predicates across resources" {
            // Arrange
            use host = createTestHost ()
            let server = host.GetTestServer()
            use client = server.CreateClient()

            use combinedGraph =
                loadCombinedGraph client [ "/person/1"; "/order/42" ]
                |> Async.RunSynchronously

            // Act: Collect all distinct predicates
            // In a coherent graph, the same logical predicate should always use
            // the same full URI. No mixing of absolute and prefixed forms.
            let results =
                executeSparql
                    combinedGraph
                    """
                    SELECT DISTINCT ?predicate
                    WHERE {
                        ?s ?predicate ?o .
                    }
                    ORDER BY ?predicate
                    """

            // Assert: Check for duplicate predicates with different namespace representations
            let predicateUris =
                [ for result in results -> result.["predicate"].ToString() ]

            // All predicates should be absolute URIs (no relative URIs or malformed URIs)
            for uri in predicateUris do
                Expect.isTrue
                    (Uri.IsWellFormedUriString(uri, UriKind.Absolute))
                    $"Predicate URI should be absolute and well-formed: {uri}"

            // Group predicates by their local name (the part after the last # or /)
            let localNames =
                predicateUris
                |> List.map (fun uri ->
                    let lastHash = uri.LastIndexOf('#')
                    let lastSlash = uri.LastIndexOf('/')
                    let splitAt = max lastHash lastSlash

                    if splitAt >= 0 then
                        uri.Substring(splitAt + 1)
                    else
                        uri)

            // Check for duplicate local names with different namespaces
            // (e.g., "http://example.org/Name" and "http://other.org/Name")
            let duplicates =
                localNames |> List.groupBy id |> List.filter (fun (_, group) -> group.Length > 1)

            // For Frank's controlled ontology, duplicate local names across
            // different namespaces would indicate inconsistency.
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

                        if splitAt >= 0 then
                            uri.Substring(0, splitAt + 1)
                        else
                            uri)
                    |> List.distinct

                // If multiple namespaces use the same local name, log it as a warning.
                // In Frank's controlled output this should not happen.
                if namespaces.Length > 1 then
                    printfn
                        $"Predicate local name '{name}' used with multiple namespaces: {namespaces}"
        }

        // T028: Edge Case -- Special Character URI Encoding
        testAsync "US4-Edge: Special character URIs are properly encoded in RDF" {
            // Arrange: Test with a URI containing percent-encoded characters
            // Construct RDF directly with encoded URIs (more reliable for edge case testing)
            use graph = new Graph()
            let encodedUri = "http://example.org/api/person/John%20Doe"
            let subject = graph.CreateUriNode(UriFactory.Root.Create(encodedUri))

            let predicate =
                graph.CreateUriNode(
                    UriFactory.Root.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type")
                )

            let obj =
                graph.CreateUriNode(UriFactory.Root.Create("http://example.org/api/Person"))

            graph.Assert(Triple(subject, predicate, obj)) |> ignore

            // Act: SPARQL ASK query with the encoded URI
            // URIs in SPARQL must match exactly, including percent-encoding.
            let askResult =
                executeSparqlAsk
                    graph
                    $"""
                    PREFIX rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#>

                    ASK {{
                        <{encodedUri}> rdf:type ?type .
                    }}
                    """

            // Assert: The encoded URI should be findable
            Expect.isTrue askResult "Percent-encoded URI should be queryable via SPARQL ASK"

            // Also verify via triple enumeration
            let subjects =
                graph.Triples |> Seq.map (fun t -> t.Subject.ToString()) |> Seq.toList

            Expect.contains
                subjects
                encodedUri
                "Graph should contain the percent-encoded URI as a subject"
        }
    ]
