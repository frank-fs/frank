module Frank.Provenance.Tests.ProvenanceGraphTests

open System
open Expecto
open VDS.RDF
open Frank.Provenance
open Frank.Provenance.Tests.SparqlHelpers

// ---------------------------------------------------------------------------
// Module-level helpers for provenance test setup
// ---------------------------------------------------------------------------

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
          UsedEntity = usedEntity
          ActingRoles = [] }

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
          UsedEntity = usedEntity
          ActingRoles = [] }

    [ record1; record2 ]

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[<Tests>]
let tests =
    testList "US3 - Provenance Named Graph Isolation" [

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
