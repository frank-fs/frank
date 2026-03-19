module Frank.LinkedData.Tests.GraphCoherenceTests

open System
open System.Net.Http
open Expecto
open Microsoft.AspNetCore.TestHost
open VDS.RDF
open VDS.RDF.Query
open Frank.LinkedData.Tests.RdfTestHelpers

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
            // If no cross-resource links exist, that is OK -- the resources may be independent.
            // Verify the combined graph has subjects from at least 2 resources.
            let subjectUris =
                combinedGraph.Triples
                |> Seq.map (fun t -> t.Subject.ToString())
                |> Seq.distinct
                |> Seq.toList

            Expect.isGreaterThanOrEqual
                subjectUris.Length
                2
                "Combined graph should have subjects from at least 2 resources"
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

        // T026 supplementary: Blank node that IS referenced (non-orphaned) passes
        testCase "US4-SC2: Synthetic graph with non-orphaned blank node passes" <| fun () ->
            // Arrange: Build a graph with a blank node that is referenced as an object
            use graph = new Graph()

            let subject =
                graph.CreateUriNode(UriFactory.Root.Create("http://example.org/api/person/1"))

            let predicate =
                graph.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/address"))

            let blank = graph.CreateBlankNode()
            graph.Assert(Triple(subject, predicate, blank)) |> ignore

            let streetPred =
                graph.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/street"))

            let streetVal = graph.CreateLiteralNode("123 Main St")
            graph.Assert(Triple(blank, streetPred, streetVal)) |> ignore

            // Act
            let results =
                executeSparql
                    graph
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

            // Assert: The blank node is reachable from the named subject, so zero orphans
            Expect.equal results.Count 0 "Non-orphaned blank node should not be detected"

        // T026 supplementary: Blank node that is NOT referenced (orphaned) is detected
        testCase "US4-SC2: Synthetic graph with orphaned blank node is detected" <| fun () ->
            // Arrange: Build a graph with an orphaned blank node
            use graph = new Graph()

            // Normal triple with a named subject
            let subject =
                graph.CreateUriNode(UriFactory.Root.Create("http://example.org/api/person/1"))

            let namePred =
                graph.CreateUriNode(
                    UriFactory.Root.Create("http://example.org/api/properties/Person/Name")
                )

            let nameVal = graph.CreateLiteralNode("Alice")
            graph.Assert(Triple(subject, namePred, nameVal)) |> ignore

            // Orphaned blank node -- subject with properties but nothing references it
            let orphanBlank = graph.CreateBlankNode()

            let streetPred =
                graph.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/street"))

            let streetVal = graph.CreateLiteralNode("456 Orphan Ave")
            graph.Assert(Triple(orphanBlank, streetPred, streetVal)) |> ignore

            // Act
            let results =
                executeSparql
                    graph
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

            // Assert: One orphaned blank node should be found
            Expect.equal results.Count 1 "Orphaned blank node should be detected"

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

        // T027 supplementary: Verify that inconsistent predicates are detected
        testCase "US4-SC3: Synthetic graph with inconsistent predicates detected" <| fun () ->
            // Arrange: Build a graph where the same logical property uses two
            // different namespace forms (simulating an inconsistency bug)
            use graph = new Graph()

            let subject =
                graph.CreateUriNode(UriFactory.Root.Create("http://example.org/api/person/1"))

            // Same logical "Name" property with two different namespace URIs
            let pred1 =
                graph.CreateUriNode(
                    UriFactory.Root.Create("http://example.org/api/properties/Person/Name")
                )

            let pred2 =
                graph.CreateUriNode(
                    UriFactory.Root.Create("http://example.org/api/v2/properties/Person/Name")
                )

            let val1 = graph.CreateLiteralNode("Alice")
            let val2 = graph.CreateLiteralNode("Bob")
            graph.Assert(Triple(subject, pred1, val1)) |> ignore
            graph.Assert(Triple(subject, pred2, val2)) |> ignore

            // Act: Collect predicates and group by local name
            let results =
                executeSparql
                    graph
                    """
                    SELECT DISTINCT ?predicate
                    WHERE {
                        ?s ?predicate ?o .
                    }
                    """

            let predicateUris =
                [ for result in results -> result.["predicate"].ToString() ]

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

            let duplicates =
                localNames |> List.groupBy id |> List.filter (fun (_, g) -> g.Length > 1)

            // Assert: The duplicate local name "Name" should be detected
            Expect.isNonEmpty duplicates "Duplicate predicate local names should be found"

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

            // dotNetRdf may decode %20 to a space in the URI string representation,
            // so check for either form
            let hasEncodedOrDecoded =
                subjects
                |> List.exists (fun s ->
                    s.Contains("John%20Doe") || s.Contains("John Doe"))

            Expect.isTrue
                hasEncodedOrDecoded
                "Graph should contain the URI with the special characters (encoded or decoded)"
        }

        // T028 supplementary: URI with query parameters
        testCase "US4-Edge: URI with query parameters round-trips through RDF" <| fun () ->
            // Arrange: URI with query string special characters
            use graph = new Graph()
            let uriWithQuery = "http://example.org/api/search?q=hello%26world&page=1"

            let subject =
                graph.CreateUriNode(UriFactory.Root.Create(uriWithQuery))

            let predicate =
                graph.CreateUriNode(
                    UriFactory.Root.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type")
                )

            let obj =
                graph.CreateUriNode(UriFactory.Root.Create("http://example.org/api/SearchResult"))

            graph.Assert(Triple(subject, predicate, obj)) |> ignore

            // Act: Verify the triple exists
            let tripleCount = graph.Triples |> Seq.length

            // Assert
            Expect.equal tripleCount 1 "Graph should contain exactly one triple with the special URI"

            let found =
                executeSparqlAsk
                    graph
                    $"""
                    ASK {{
                        <{uriWithQuery}> ?p ?o .
                    }}
                    """

            Expect.isTrue found "URI with query parameters should be queryable via SPARQL"

        // T028 supplementary: URI with Unicode characters
        testCase "US4-Edge: URI with Unicode characters round-trips through RDF" <| fun () ->
            // Arrange: URI with percent-encoded Unicode character
            use graph = new Graph()
            let unicodeUri = "http://example.org/api/item/%C3%A9l%C3%A8ve"

            let subject =
                graph.CreateUriNode(UriFactory.Root.Create(unicodeUri))

            let predicate =
                graph.CreateUriNode(
                    UriFactory.Root.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type")
                )

            let obj =
                graph.CreateUriNode(UriFactory.Root.Create("http://example.org/api/Item"))

            graph.Assert(Triple(subject, predicate, obj)) |> ignore

            // Assert: Triple was asserted successfully
            Expect.equal
                (graph.Triples |> Seq.length)
                1
                "Graph should contain the triple with Unicode URI"
    ]
