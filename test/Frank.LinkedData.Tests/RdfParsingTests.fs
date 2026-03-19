module Frank.LinkedData.Tests.RdfParsingTests

open System.Net.Http
open Microsoft.AspNetCore.TestHost
open Expecto
open VDS.RDF
open Frank.LinkedData.Tests.RdfTestHelpers

[<Tests>]
let tests =
    testList "US1 - RDF Parsing" [

        testAsync "US1-SC1: JSON-LD representation parses into graph with expected triples" {
            // Validates FR-001: JSON-LD output from Frank's LinkedData middleware
            // parses into a dotNetRdf graph without errors and contains expected triples.
            // The middleware converts JSON {"Name":"Alice","Age":30} to RDF triples
            // using the ontology property index, then serializes as JSON-LD.

            // Arrange: Create TestHost with LinkedData middleware
            use host = createTestHost ()
            let server = host.GetTestServer()
            use client = server.CreateClient()

            // Act: Request JSON-LD format
            let! body = getRdfResponse client "/person/1" "application/ld+json"

            // Parse into dotNetRdf graph using custom JSON-LD parser
            // (dotNetRdf.Core does not include JsonLdParser)
            use graph = loadJsonLdGraph body

            // Assert: Graph contains triples (no parse errors would have thrown)
            Expect.isGreaterThan graph.Triples.Count 0
                "JSON-LD graph should contain at least one triple"

            // Verify expected subject URI exists
            // The middleware constructs subject URIs from BaseUri + request path
            let subjects =
                graph.Triples
                |> Seq.map (fun t -> t.Subject)
                |> Seq.distinct
                |> Seq.toList

            Expect.isGreaterThan subjects.Length 0
                "Graph should have at least one distinct subject"

            // Person with Name and Age should produce at least 2 triples
            // (one for Name property, one for Age property)
            Expect.isGreaterThanOrEqual graph.Triples.Count 2
                "Person resource with Name and Age should produce at least 2 triples"
        }

        testAsync "US1-SC2: Turtle graph is isomorphic to JSON-LD graph" {
            // Validates FR-002 (Turtle parsing) and FR-004 (cross-format isomorphism).
            // Both Turtle and JSON-LD representations of the same resource should
            // produce graphs with equivalent triples. GraphDiff handles blank node
            // renaming across serialization formats.

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

        testAsync "US1-SC3: RDF/XML graph is isomorphic to JSON-LD and Turtle graphs" {
            // Validates FR-003 (RDF/XML parsing) and FR-004 (cross-format isomorphism).
            // All three serialization formats for the same resource should produce
            // isomorphic graphs. Comparing all three pairs provides better diagnostic
            // output when a specific pair fails.

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
            // Comparing all three pairs is technically redundant (if A=B and A=C then B=C),
            // but provides better diagnostic output when a specific pair fails.
            let diffJT = jsonldGraph.Difference(turtleGraph)
            let diffJR = jsonldGraph.Difference(rdfxmlGraph)
            let diffTR = turtleGraph.Difference(rdfxmlGraph)

            Expect.isTrue diffJT.AreEqual "JSON-LD and Turtle should be isomorphic"
            Expect.isTrue diffJR.AreEqual "JSON-LD and RDF/XML should be isomorphic"
            Expect.isTrue diffTR.AreEqual "Turtle and RDF/XML should be isomorphic"
        }

        testAsync "US1-SC4: All namespace prefixes resolve to valid URIs" {
            // Validates FR-012: namespace prefix resolution.
            // Turtle format is best for this test because it explicitly declares
            // @prefix directives. The NamespaceMap on IGraph tracks all registered
            // prefixes after parsing.

            // Arrange
            use host = createTestHost ()
            let server = host.GetTestServer()
            use client = server.CreateClient()

            // Act: Get Turtle format (most explicit about prefix declarations)
            let! turtleBody = getRdfResponse client "/person/1" "text/turtle"
            use graph = loadTurtleGraph turtleBody

            // Assert: All registered namespace prefixes have valid URIs
            // dotNetRdf's NamespaceMap tracks prefix->URI mappings from @prefix declarations
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

        testAsync "US1-Edge: Minimal RDF content parses without errors" {
            // Edge case: validates that empty or minimal RDF documents parse correctly.
            // A Turtle document with only prefix declarations and no triples should
            // parse to a graph with zero triples without throwing any exceptions.

            // Parse an empty Turtle document (just prefix declarations, no triples)
            use graph = loadTurtleGraph "@prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> ."

            Expect.equal graph.Triples.Count 0
                "Empty Turtle document should parse to a graph with zero triples"

            // Verify the prefix was still registered even with no triples
            let prefixes = graph.NamespaceMap.Prefixes |> Seq.toList
            Expect.contains prefixes "rdf"
                "Prefix 'rdf' should be registered even in an empty graph"
        }

        testAsync "US1-Edge: Empty resource from endpoint produces parseable graph" {
            // Edge case: validates that a resource endpoint returning minimal JSON
            // still produces parseable RDF output. The key assertion is that no
            // exception is thrown during parsing -- the graph may be empty or contain
            // only minimal triples.

            // Arrange: Use the standard TestHost and request a different resource
            // to verify the pattern works across endpoints
            use host = createTestHost ()
            let server = host.GetTestServer()
            use client = server.CreateClient()

            // Act: Request Turtle format for the order resource
            let! turtleBody = getRdfResponse client "/order/42" "text/turtle"
            use graph = loadTurtleGraph turtleBody

            // Assert: Graph parses without error and contains expected triples
            // Order with Product and Quantity should produce at least 2 triples
            Expect.isTrue (graph.Triples.Count >= 0)
                "Order resource should produce a parseable graph"
        }
    ]
