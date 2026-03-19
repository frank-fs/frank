module Frank.LinkedData.Tests.ProvenanceGraphTests

open System
open Expecto
open Microsoft.AspNetCore.TestHost
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Query
open VDS.RDF.Query.Datasets
open Frank.Provenance
open Frank.LinkedData.Tests.RdfTestHelpers

// ---------------------------------------------------------------------------
// Module-level helpers for provenance test setup
// ---------------------------------------------------------------------------

/// Copy all triples from a source graph into a named graph within a TripleStore.
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
    addAsNamedGraph store provenanceGraph namedGraphUri
    store

/// Execute a SPARQL query against a TripleStore with named graphs.
let private executeSparqlOnDataset (store: TripleStore) (sparql: string) : SparqlResultSet =
    let dataset = new InMemoryDataset(store)
    let processor = new LeviathanQueryProcessor(dataset :> ISparqlDataset)
    let parser = new SparqlQueryParser()
    let query = parser.ParseFromString(sparql)

    match processor.ProcessQuery(query) with
    | :? SparqlResultSet as results -> results
    | _ -> failwith "Expected SparqlResultSet from SELECT/ASK query"

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
// Tests -- LinkedData + Provenance integration (require TestHost)
// ---------------------------------------------------------------------------

[<Tests>]
let tests =
    testList "US3 - Provenance + LinkedData Integration" [
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
    ]
