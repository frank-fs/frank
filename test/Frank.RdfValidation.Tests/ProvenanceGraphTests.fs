module Frank.RdfValidation.Tests.ProvenanceGraphTests

open System
open Expecto
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.Hosting
open VDS.RDF
open Frank.Provenance
open Frank.RdfValidation.Tests.TestHelpers

// ---------------------------------------------------------------------------
// Module-level helpers for provenance test setup (T018)
// ---------------------------------------------------------------------------

/// Copy all triples from a source graph into a named graph within a TripleStore.
/// In dotNetRdf 3.x, Graph.Name (IRefNode) determines graph identity in a store,
/// so we create a new Graph with a UriNode name and merge the source triples.
let private addAsNamedGraph (store: TripleStore) (sourceGraph: IGraph) (graphUri: Uri) =
    let namedGraph = new Graph(new UriNode(graphUri))
    namedGraph.Merge(sourceGraph)
    store.Add(namedGraph) |> ignore

/// Create a TripleStore with resource graph in default graph and provenance in a named graph.
let private createProvenanceDataset
    (resourceGraph: IGraph)
    (provenanceGraph: IGraph)
    (namedGraphUri: Uri)
    : TripleStore =
    let store = new TripleStore()
    store.Add(resourceGraph) |> ignore
    // Add provenance as a named graph (using Graph.Name, not BaseUri)
    addAsNamedGraph store provenanceGraph namedGraphUri
    store

/// Create test provenance records for a resource.
/// Builds 2 provenance records representing state transitions.
let private makeTestProvenanceRecords (resourceUri: string) : ProvenanceRecord list =
    let now = DateTimeOffset.UtcNow

    let agent =
        { ProvenanceAgent.Id = "urn:frank:agent:person:test-user-1"
          AgentType = AgentType.Person("Test User", "test-user-1") }

    let record1 =
        let activity =
            { ProvenanceActivity.Id = $"urn:frank:activity:{Guid.NewGuid()}"
              HttpMethod = "POST"
              ResourceUri = resourceUri
              EventName = "submit"
              PreviousState = "Draft"
              NewState = "Submitted"
              StartedAt = now.AddSeconds(-60.0)
              EndedAt = now.AddSeconds(-59.0) }

        let usedEntity =
            { ProvenanceEntity.Id = $"urn:frank:entity:{Guid.NewGuid()}"
              ResourceUri = resourceUri
              StateName = "Draft"
              CapturedAt = now.AddSeconds(-60.0) }

        let generatedEntity =
            { ProvenanceEntity.Id = $"urn:frank:entity:{Guid.NewGuid()}"
              ResourceUri = resourceUri
              StateName = "Submitted"
              CapturedAt = now.AddSeconds(-59.0) }

        { ProvenanceRecord.Id = $"urn:frank:record:{Guid.NewGuid()}"
          ResourceUri = resourceUri
          RecordedAt = now.AddSeconds(-59.0)
          Activity = activity
          Agent = agent
          GeneratedEntity = generatedEntity
          UsedEntity = usedEntity }

    let record2 =
        let activity =
            { ProvenanceActivity.Id = $"urn:frank:activity:{Guid.NewGuid()}"
              HttpMethod = "POST"
              ResourceUri = resourceUri
              EventName = "approve"
              PreviousState = "Submitted"
              NewState = "Approved"
              StartedAt = now.AddSeconds(-30.0)
              EndedAt = now.AddSeconds(-29.0) }

        let usedEntity =
            { ProvenanceEntity.Id = $"urn:frank:entity:{Guid.NewGuid()}"
              ResourceUri = resourceUri
              StateName = "Submitted"
              CapturedAt = now.AddSeconds(-30.0) }

        let generatedEntity =
            { ProvenanceEntity.Id = $"urn:frank:entity:{Guid.NewGuid()}"
              ResourceUri = resourceUri
              StateName = "Approved"
              CapturedAt = now.AddSeconds(-29.0) }

        { ProvenanceRecord.Id = $"urn:frank:record:{Guid.NewGuid()}"
          ResourceUri = resourceUri
          RecordedAt = now.AddSeconds(-29.0)
          Activity = activity
          Agent = agent
          GeneratedEntity = generatedEntity
          UsedEntity = usedEntity }

    [ record1; record2 ]

// ---------------------------------------------------------------------------
// Tests (T018-T023)
// ---------------------------------------------------------------------------

[<Tests>]
let tests =
    testList "US3 - Provenance Named Graph Isolation" [
        // T019: US3-SC1 -- Named Graph Isolation Verification (FR-009, FR-010)
        testAsync "US3-SC1: Provenance named graph contains only PROV-O triples, not resource triples" {
            // Arrange: Get resource graph from TestHost
            use host = createTestHost ()
            let server = host.GetTestServer()
            use client = server.CreateClient()
            let! body = getRdfResponse client "/person/1" "text/turtle"
            use resourceGraph = loadTurtleGraph body

            // Build provenance graph from test records
            let provenanceRecords = makeTestProvenanceRecords "http://example.org/api/person/1"
            use provenanceGraph = GraphBuilder.toGraph provenanceRecords

            // Load into dataset with named graphs
            let provUri = Uri("http://example.org/api/person/1/provenance")
            use store = createProvenanceDataset resourceGraph provenanceGraph provUri

            // Act: Query provenance named graph for all predicates.
            // The GRAPH clause scopes the query to only the provenance named graph.
            // This proves isolation: only PROV-O predicates should appear.
            let results = executeSparqlOnDataset store $"""
                PREFIX prov: <http://www.w3.org/ns/prov#>

                SELECT DISTINCT ?predicate
                WHERE {{
                    GRAPH <{provUri}> {{
                        ?s ?predicate ?o .
                    }}
                }}
            """

            // Assert: All predicates should be PROV-O or related (rdf:type, prov:*, frank:*, rdfs:label)
            Expect.isGreaterThan results.Count 0
                "Provenance named graph should contain triples"

            for result in results do
                let pred = result.["predicate"].ToString()
                let isProvOrRdfOrFrank =
                    pred.StartsWith("http://www.w3.org/ns/prov#")
                    || pred.StartsWith("http://www.w3.org/1999/02/22-rdf-syntax-ns#")
                    || pred.StartsWith("https://frank-web.dev/ns/provenance#")
                    || pred.StartsWith("http://www.w3.org/2000/01/rdf-schema#")
                Expect.isTrue isProvOrRdfOrFrank
                    $"Predicate in provenance graph should be PROV-O, rdf:, rdfs:, or frank:, but found: {pred}"
        }

        // T020: US3-SC2 -- SPARQL for prov:Activity with Agents and Timestamps (FR-010)
        testAsync "US3-SC2: SPARQL finds prov:Activity instances with agents and timestamps" {
            // Arrange: Build provenance graph with known records
            let records = makeTestProvenanceRecords "http://example.org/api/person/1"
            use provenanceGraph = GraphBuilder.toGraph records
            let provUri = Uri("http://example.org/api/person/1/provenance")

            use store = new TripleStore()
            addAsNamedGraph store provenanceGraph provUri

            // Act: Query for activities, agents, and timestamps.
            // This SPARQL pattern extracts the core provenance audit trail:
            // who did what, and when.
            let results = executeSparqlOnDataset store $"""
                PREFIX prov: <http://www.w3.org/ns/prov#>
                PREFIX rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#>
                PREFIX xsd: <http://www.w3.org/2001/XMLSchema#>

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

        // T021: US3-SC3 -- Per-Resource Provenance Scoping (FR-009)
        testAsync "US3-SC3: Per-resource provenance scoping -- no cross-resource leakage" {
            // Arrange: Create provenance for TWO different resources
            let personRecords = makeTestProvenanceRecords "http://example.org/api/person/1"
            let orderRecords = makeTestProvenanceRecords "http://example.org/api/order/42"

            use personProvGraph = GraphBuilder.toGraph personRecords
            use orderProvGraph = GraphBuilder.toGraph orderRecords

            let personProvUri = Uri("http://example.org/api/person/1/provenance")
            let orderProvUri = Uri("http://example.org/api/order/42/provenance")

            use store = new TripleStore()
            addAsNamedGraph store personProvGraph personProvUri
            addAsNamedGraph store orderProvGraph orderProvUri

            // Act: Query ONLY the person's provenance graph.
            // This verifies that querying one resource's provenance graph
            // does not leak triples from another resource's provenance.
            let personResults = executeSparqlOnDataset store $"""
                PREFIX prov: <http://www.w3.org/ns/prov#>

                SELECT ?activity ?entity
                WHERE {{
                    GRAPH <{personProvUri}> {{
                        ?activity prov:used ?entity .
                    }}
                }}
            """

            let orderResults = executeSparqlOnDataset store $"""
                PREFIX prov: <http://www.w3.org/ns/prov#>

                SELECT ?activity ?entity
                WHERE {{
                    GRAPH <{orderProvUri}> {{
                        ?activity prov:used ?entity .
                    }}
                }}
            """

            // Assert: Each graph's results should only reference its own resource.
            // Person provenance should not mention order URIs and vice versa.
            Expect.isGreaterThan personResults.Count 0
                "Person provenance graph should have results"

            Expect.isGreaterThan orderResults.Count 0
                "Order provenance graph should have results"

            for result in personResults do
                let entityStr = result.["entity"].ToString()
                Expect.isFalse (entityStr.Contains("order"))
                    $"Person provenance should not contain order references: {entityStr}"

            for result in orderResults do
                let entityStr = result.["entity"].ToString()
                Expect.isFalse (entityStr.Contains("person"))
                    $"Order provenance should not contain person references: {entityStr}"
        }

        // T022: US3-SC4 -- GRAPH Clause Targeting (FR-010)
        testAsync "US3-SC4: GRAPH clause targets specific provenance graph in full dataset" {
            // Arrange: Build a dataset with default graph + provenance named graph
            use host = createTestHost ()
            let server = host.GetTestServer()
            use client = server.CreateClient()
            let! body = getRdfResponse client "/person/1" "text/turtle"
            use resourceGraph = loadTurtleGraph body

            let records = makeTestProvenanceRecords "http://example.org/api/person/1"
            use provGraph = GraphBuilder.toGraph records
            let provUri = Uri("http://example.org/api/person/1/provenance")

            use store = createProvenanceDataset resourceGraph provGraph provUri

            // Act: Count triples in default graph vs named graph.
            // The GRAPH clause should isolate queries to the named graph only.
            let defaultGraphTriples = executeSparqlOnDataset store """
                SELECT (COUNT(*) AS ?count)
                WHERE {
                    ?s ?p ?o .
                }
            """

            let namedGraphTriples = executeSparqlOnDataset store $"""
                SELECT (COUNT(*) AS ?count)
                WHERE {{
                    GRAPH <{provUri}> {{
                        ?s ?p ?o .
                    }}
                }}
            """

            // Assert: Both counts should be > 0 and different.
            // COUNT(*) returns a typed literal node; extract the numeric value
            // via AsValuedNode().AsInteger() to avoid parsing the datatype suffix.
            let defaultCount =
                (defaultGraphTriples.[0].["count"] :?> ILiteralNode).Value |> int
            let namedCount =
                (namedGraphTriples.[0].["count"] :?> ILiteralNode).Value |> int

            Expect.isGreaterThan defaultCount 0
                "Default graph should have resource triples"
            Expect.isGreaterThan namedCount 0
                "Named provenance graph should have provenance triples"

            // The counts being different proves isolation
            Expect.notEqual defaultCount namedCount
                "Default and named graph triple counts should differ (different data)"
        }

        // T023: Edge Case -- Empty Provenance Graph
        testAsync "US3-Edge: Empty provenance graph returns empty results, not errors" {
            // Arrange: Create an empty provenance graph (no records)
            use emptyProvGraph = GraphBuilder.toGraph []
            let provUri = Uri("http://example.org/api/person/1/provenance")

            use store = new TripleStore()
            addAsNamedGraph store emptyProvGraph provUri

            // Act: Query for activities in the empty provenance graph.
            // When no state transitions have occurred, the graph exists but is empty.
            // SPARQL should return an empty result set, NOT throw an error.
            let results = executeSparqlOnDataset store $"""
                PREFIX prov: <http://www.w3.org/ns/prov#>

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
    ]
