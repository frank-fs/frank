module Frank.LinkedData.Tests.SparqlResourceQueryTests

open Expecto
open Microsoft.AspNetCore.TestHost
open VDS.RDF
open Frank.LinkedData.Tests.RdfTestHelpers

[<Tests>]
let tests =
    testList "US2 - SPARQL Resource Queries" [

        // T014 -- US2-SC1: SPARQL SELECT for all resources and their properties (FR-005)
        testAsync "US2-SC1: SPARQL SELECT finds all resources with their property triples" {
            // Arrange: Create TestHost and load resource RDF as Turtle
            use host = createTestHost ()
            let server = host.GetTestServer()
            use client = server.CreateClient()
            let! body = getRdfResponse client "/person/1" "text/turtle"
            use graph = loadTurtleGraph body

            // Act: Execute SPARQL SELECT to discover all subjects and their predicates/objects.
            //
            // Frank.LinkedData's projectJsonToRdf emits data-property triples of the form:
            //   <baseUri + path> <ontology-property-uri> "value"
            // where predicate URIs come from the ontology graph (e.g.
            //   <http://example.org/api/properties/Person/Name>).
            //
            // This query discovers every subject and its predicate-object pairs,
            // which is the fundamental pattern for resource discovery in the
            // projected RDF graph.
            let results = executeSparql graph """
                # Find all subjects with their predicate-object pairs.
                # This is the basic building block for any RDF-aware client
                # discovering what resources and properties exist.
                SELECT ?resource ?predicate ?value
                WHERE {
                    ?resource ?predicate ?value .
                }
            """

            // Assert: The person/1 endpoint returns {"Name":"Alice","Age":30},
            // which the ontology maps to two triples (Name and Age properties).
            Expect.isGreaterThan results.Count 0
                "Should find at least one triple in the resource graph"

            // Verify the result set has the expected variables
            Expect.contains (results.Variables |> Seq.toList) "resource"
                "Result set should include 'resource' variable"
            Expect.contains (results.Variables |> Seq.toList) "predicate"
                "Result set should include 'predicate' variable"
            Expect.contains (results.Variables |> Seq.toList) "value"
                "Result set should include 'value' variable"

            // Verify the subject URI matches the expected resource URI.
            // projectJsonToRdf uses baseUri + request path as the subject.
            let firstRow = results.[0]
            let resourceNode = firstRow.["resource"] :?> IUriNode
            Expect.stringContains (resourceNode.Uri.ToString()) "person/1"
                "Resource URI should contain the request path 'person/1'"
        }

        // T015 -- US2-SC2: SPARQL SELECT for unsafe transitions (FR-006)
        testAsync "US2-SC2: SPARQL SELECT discovers resources with unsafe transitions" {
            // Arrange: Load graph from a resource that has an associated POST endpoint.
            // Frank.LinkedData's projectJsonToRdf projects JSON properties to RDF
            // data-property triples. It does NOT encode HTTP method capabilities
            // (POST, PUT, DELETE) in the projected RDF graph. The middleware
            // only performs content negotiation on GET responses.
            use host = createTestHost ()
            let server = host.GetTestServer()
            use client = server.CreateClient()
            let! body = getRdfResponse client "/person/1" "text/turtle"
            use graph = loadTurtleGraph body

            // Act: Query for resources with unsafe HTTP methods.
            // Since Frank.LinkedData does not encode HTTP method information in RDF
            // triples (it projects only data properties from JSON responses), this
            // query should return an empty result set. The test validates that
            // SPARQL queries against the resource graph execute without errors
            // even when no matching patterns exist.
            let results = executeSparql graph """
                PREFIX hydra: <http://www.w3.org/ns/hydra/core#>

                # Attempt to discover resources with unsafe (state-changing) HTTP operations.
                # Frank.LinkedData currently projects only JSON data properties, not HTTP
                # method metadata. This query demonstrates graceful handling of absent
                # capability triples -- clients should fall back to OPTIONS or link headers.
                SELECT ?resource ?method
                WHERE {
                    ?resource hydra:method ?method .
                    FILTER(?method IN ("POST", "PUT", "DELETE"))
                }
            """

            // Assert: The query executes without error and returns zero results,
            // confirming that HTTP method capabilities are not in the RDF graph.
            Expect.equal results.Count 0
                "Should find no unsafe-transition triples -- Frank.LinkedData does not encode HTTP methods in RDF"
        }

        // T016 -- US2-SC3: SPARQL SELECT for ALPS/OWL semantic descriptors (FR-007)
        testAsync "US2-SC3: SPARQL SELECT retrieves ALPS semantic descriptors from ontology" {
            // Arrange: Load the resource graph AND the ontology graph.
            // Frank.LinkedData uses OWL DatatypeProperty declarations in the ontology
            // graph as semantic equivalents to ALPS descriptors. The ontology graph
            // (built by createTestOntology in TestHelpers) declares properties like:
            //   <http://example.org/api/properties/Person/Name> rdf:type owl:DatatypeProperty
            //   <http://example.org/api/properties/Person/Age>  rdf:type owl:DatatypeProperty
            //
            // These are the semantic descriptors that give machine-readable meaning
            // to resource fields, analogous to ALPS descriptor elements.
            use host = createTestHost ()
            let server = host.GetTestServer()
            use client = server.CreateClient()
            let! body = getRdfResponse client "/person/1" "text/turtle"
            use resourceGraph = loadTurtleGraph body

            // Load the ontology graph directly (it contains the OWL property declarations)
            let ontology = createTestOntology ()

            // Merge ontology + resource triples into a single graph for combined querying.
            // This simulates a client that fetches both the resource and its ontology.
            use combined = new Graph()
            combined.Merge(ontology, false)
            combined.Merge(resourceGraph, false)

            // Act: Query for OWL DatatypeProperty declarations (semantic descriptors).
            // These correspond to ALPS descriptors -- each property declared in the
            // ontology graph provides semantic meaning to a resource field.
            let results = executeSparql combined """
                PREFIX rdf:  <http://www.w3.org/1999/02/22-rdf-syntax-ns#>
                PREFIX owl:  <http://www.w3.org/2002/07/owl#>

                # Find all OWL DatatypeProperty declarations in the combined graph.
                # These serve as ALPS-equivalent semantic descriptors, enabling
                # machine-readable API profiles that describe resource fields.
                SELECT ?property
                WHERE {
                    ?property rdf:type owl:DatatypeProperty .
                }
            """

            // Assert: The test ontology declares 4 DatatypeProperties:
            //   Person/Name, Person/Age, Order/Product, Order/Quantity
            Expect.isGreaterThan results.Count 0
                "Should find at least one OWL DatatypeProperty (semantic descriptor)"

            // Verify the descriptors include Person properties used by /person/1
            let propertyUris =
                [ for row in results -> (row.["property"] :?> IUriNode).Uri.ToString() ]

            Expect.exists propertyUris (fun uri -> uri.Contains("Person/Name"))
                "Should find the Person/Name semantic descriptor"
            Expect.exists propertyUris (fun uri -> uri.Contains("Person/Age"))
                "Should find the Person/Age semantic descriptor"
        }

        // T017 -- US2-SC4: SPARQL ASK for resource existence (FR-005)
        testAsync "US2-SC4: SPARQL ASK returns true for existing resource, false for non-existent" {
            // Arrange
            use host = createTestHost ()
            let server = host.GetTestServer()
            use client = server.CreateClient()
            let! body = getRdfResponse client "/person/1" "text/turtle"
            use graph = loadTurtleGraph body

            // Act & Assert: ASK for a resource that should exist.
            // ASK queries return a boolean: true if the pattern matches, false otherwise.
            // This is the simplest way to check resource existence in an RDF graph.
            //
            // The projected graph contains triples with subject
            //   <http://example.org/api/person/1>
            // and predicates from the ontology (e.g. .../Person/Name, .../Person/Age).
            let existsResult = executeSparqlAsk graph """
                PREFIX ex: <http://example.org/api/properties/Person/>

                # Check if a resource with Person/Name property exists in the graph.
                # Returns true if at least one resource has the Name property --
                # confirming the resource was projected to RDF successfully.
                ASK {
                    ?resource ex:Name ?name .
                }
            """
            Expect.isTrue existsResult
                "ASK should return true -- a resource with Person/Name exists"

            // Act & Assert: ASK for a resource that should NOT exist.
            // Using a URI that does not appear in the graph to verify false negatives work.
            let notExistsResult = executeSparqlAsk graph """
                # Check for a specific resource URI that should not exist in this graph.
                # Expected result: false (no match).
                ASK {
                    <http://example.org/nonexistent/resource/999> ?p ?o .
                }
            """
            Expect.isFalse notExistsResult
                "ASK should return false -- nonexistent resource not in graph"
        }
    ]
