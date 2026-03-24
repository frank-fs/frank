module Frank.Provenance.Tests.GraphBuilderTests

open System
open System.Linq
open Expecto
open Frank.Provenance
open VDS.RDF

let private hasTriple (graph: IGraph) (s: string) (p: string) (o: string) =
    let subj = graph.CreateUriNode(UriFactory.Create(s))
    let pred = graph.CreateUriNode(UriFactory.Create(p))
    let obj = graph.CreateUriNode(UriFactory.Create(o))
    graph.ContainsTriple(Triple(subj, pred, obj))

let private hasLiteralTriple (graph: IGraph) (s: string) (p: string) (value: string) =
    let subj = graph.CreateUriNode(UriFactory.Create(s))
    let pred = graph.CreateUriNode(UriFactory.Create(p))
    let obj = graph.CreateLiteralNode(value)
    graph.ContainsTriple(Triple(subj, pred, obj))

let private hasTypedLiteralTriple (graph: IGraph) (s: string) (p: string) (value: string) (datatypeUri: string) =
    let subj = graph.CreateUriNode(UriFactory.Create(s))
    let pred = graph.CreateUriNode(UriFactory.Create(p))
    let obj = graph.CreateLiteralNode(value, UriFactory.Create(datatypeUri))
    graph.ContainsTriple(Triple(subj, pred, obj))

let private makeRecord (agent: ProvenanceAgent) =
    let now = DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero)

    let usedEntity =
        { ProvenanceEntity.Id = "urn:frank:entity:used-1"
          ResourceUri = "/orders/123"
          StateName = "Draft"
          CapturedAt = now.AddSeconds(-1.0) }

    let generatedEntity =
        { ProvenanceEntity.Id = "urn:frank:entity:gen-1"
          ResourceUri = "/orders/123"
          StateName = "Submitted"
          CapturedAt = now }

    let activity =
        { ProvenanceActivity.Id = "urn:frank:activity:act-1"
          HttpMethod = "POST"
          ResourceUri = "/orders/123"
          EventName = "Submit"
          PreviousState = "Draft"
          NewState = "Submitted"
          StartedAt = now.AddMilliseconds(-50.0)
          EndedAt = now }

    { ProvenanceRecord.Id = "urn:frank:record:rec-1"
      ResourceUri = "/orders/123"
      RecordedAt = now
      Activity = activity
      Agent = agent
      GeneratedEntity = generatedEntity
      UsedEntity = usedEntity
      ActingRoles = [] }

let private personAgent =
    { ProvenanceAgent.Id = "urn:frank:agent:alice"
      AgentType = AgentType.Person("Alice", "alice-001") }

let private softwareAgent =
    { ProvenanceAgent.Id = "urn:frank:agent:middleware"
      AgentType = AgentType.SoftwareAgent("frank-middleware") }

let private llmAgentWithModel =
    { ProvenanceAgent.Id = "urn:frank:agent:claude"
      AgentType = AgentType.LlmAgent("claude", Some "claude-opus-4-6") }

let private llmAgentWithoutModel =
    { ProvenanceAgent.Id = "urn:frank:agent:gpt"
      AgentType = AgentType.LlmAgent("gpt", None) }

[<Tests>]
let graphBuilderTests =
    testList
        "GraphBuilder"
        [ testList
              "Activity triples"
              [ test "activity has rdf:type prov:Activity" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasTriple graph record.Activity.Id ProvVocabulary.Rdf.Type ProvVocabulary.Class.Activity)
                        "Activity should have rdf:type prov:Activity"
                }

                test "activity has prov:startedAtTime" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasTypedLiteralTriple
                            graph
                            record.Activity.Id
                            ProvVocabulary.Property.StartedAtTime
                            (record.Activity.StartedAt.ToString("o"))
                            ProvVocabulary.Xsd.DateTime)
                        "Activity should have prov:startedAtTime"
                }

                test "activity has prov:endedAtTime" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasTypedLiteralTriple
                            graph
                            record.Activity.Id
                            ProvVocabulary.Property.EndedAtTime
                            (record.Activity.EndedAt.ToString("o"))
                            ProvVocabulary.Xsd.DateTime)
                        "Activity should have prov:endedAtTime"
                }

                test "activity has prov:wasAssociatedWith agent" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasTriple graph record.Activity.Id ProvVocabulary.Property.WasAssociatedWith record.Agent.Id)
                        "Activity should be associated with agent"
                }

                test "activity has prov:used entity" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasTriple graph record.Activity.Id ProvVocabulary.Property.Used record.UsedEntity.Id)
                        "Activity should have prov:used"
                }

                test "activity has frank:httpMethod" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasLiteralTriple graph record.Activity.Id ProvVocabulary.Frank.httpMethod "POST")
                        "Activity should have frank:httpMethod"
                }

                test "activity has frank:eventName" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasLiteralTriple graph record.Activity.Id ProvVocabulary.Frank.eventName "Submit")
                        "Activity should have frank:eventName"
                }

                test "activity has frank:actingRole triples for each role" {
                    let record =
                        { makeRecord personAgent with
                            ActingRoles = [ "PlayerX"; "Spectator" ] }

                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasLiteralTriple graph record.Activity.Id ProvVocabulary.Frank.actingRole "PlayerX")
                        "Activity should have frank:actingRole PlayerX"

                    Expect.isTrue
                        (hasLiteralTriple graph record.Activity.Id ProvVocabulary.Frank.actingRole "Spectator")
                        "Activity should have frank:actingRole Spectator"
                }

                test "activity with no roles emits no frank:actingRole triples" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    let activityNode = graph.CreateUriNode(UriFactory.Create(record.Activity.Id))

                    let rolePred =
                        graph.CreateUriNode(UriFactory.Create(ProvVocabulary.Frank.actingRole))

                    let triples = graph.GetTriplesWithSubjectPredicate(activityNode, rolePred).Count()
                    Expect.equal triples 0 "Activity with no roles should have no frank:actingRole triples"
                } ]

          testList
              "Agent triples - Person"
              [ test "Person has rdf:type prov:Person" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasTriple graph personAgent.Id ProvVocabulary.Rdf.Type ProvVocabulary.Class.Person)
                        "Person agent should have rdf:type prov:Person"
                }

                test "Person has rdfs:label with name" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasLiteralTriple graph personAgent.Id ProvVocabulary.Rdfs.label "Alice")
                        "Person agent should have rdfs:label"
                } ]

          testList
              "Agent triples - SoftwareAgent"
              [ test "SoftwareAgent has rdf:type prov:SoftwareAgent" {
                    let record = makeRecord softwareAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasTriple graph softwareAgent.Id ProvVocabulary.Rdf.Type ProvVocabulary.Class.SoftwareAgent)
                        "SoftwareAgent should have rdf:type prov:SoftwareAgent"
                }

                test "SoftwareAgent has rdfs:label with identifier" {
                    let record = makeRecord softwareAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasLiteralTriple graph softwareAgent.Id ProvVocabulary.Rdfs.label "frank-middleware")
                        "SoftwareAgent should have rdfs:label"
                } ]

          testList
              "Agent triples - LlmAgent"
              [ test "LlmAgent has dual rdf:type prov:SoftwareAgent and frank:LlmAgent" {
                    let record = makeRecord llmAgentWithModel
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasTriple graph llmAgentWithModel.Id ProvVocabulary.Rdf.Type ProvVocabulary.Class.SoftwareAgent)
                        "LlmAgent should have rdf:type prov:SoftwareAgent"

                    Expect.isTrue
                        (hasTriple graph llmAgentWithModel.Id ProvVocabulary.Rdf.Type ProvVocabulary.Frank.LlmAgent)
                        "LlmAgent should have rdf:type frank:LlmAgent"
                }

                test "LlmAgent has rdfs:label with identifier" {
                    let record = makeRecord llmAgentWithModel
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasLiteralTriple graph llmAgentWithModel.Id ProvVocabulary.Rdfs.label "claude")
                        "LlmAgent should have rdfs:label"
                }

                test "LlmAgent with model has frank:agentModel" {
                    let record = makeRecord llmAgentWithModel
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasLiteralTriple graph llmAgentWithModel.Id ProvVocabulary.Frank.agentModel "claude-opus-4-6")
                        "LlmAgent should have frank:agentModel"
                }

                test "LlmAgent without model has no frank:agentModel" {
                    let record = makeRecord llmAgentWithoutModel
                    let graph = GraphBuilder.toGraph [ record ]

                    let agentNode = graph.CreateUriNode(UriFactory.Create(llmAgentWithoutModel.Id))

                    let modelPred =
                        graph.CreateUriNode(UriFactory.Create(ProvVocabulary.Frank.agentModel))

                    let triples = graph.GetTriplesWithSubjectPredicate(agentNode, modelPred).Count()

                    Expect.equal triples 0 "LlmAgent without model should have no frank:agentModel triple"
                } ]

          testList
              "Entity triples"
              [ test "UsedEntity has rdf:type prov:Entity" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasTriple graph record.UsedEntity.Id ProvVocabulary.Rdf.Type ProvVocabulary.Class.Entity)
                        "UsedEntity should have rdf:type prov:Entity"
                }

                test "UsedEntity has prov:wasAttributedTo agent" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasTriple graph record.UsedEntity.Id ProvVocabulary.Property.WasAttributedTo record.Agent.Id)
                        "UsedEntity should be attributed to agent"
                }

                test "UsedEntity has frank:stateName" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasLiteralTriple graph record.UsedEntity.Id ProvVocabulary.Frank.stateName "Draft")
                        "UsedEntity should have frank:stateName"
                }

                test "GeneratedEntity has rdf:type prov:Entity" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasTriple graph record.GeneratedEntity.Id ProvVocabulary.Rdf.Type ProvVocabulary.Class.Entity)
                        "GeneratedEntity should have rdf:type prov:Entity"
                }

                test "GeneratedEntity has prov:wasGeneratedBy activity" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasTriple
                            graph
                            record.GeneratedEntity.Id
                            ProvVocabulary.Property.WasGeneratedBy
                            record.Activity.Id)
                        "GeneratedEntity should be generated by activity"
                }

                test "GeneratedEntity has prov:wasAttributedTo agent" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasTriple
                            graph
                            record.GeneratedEntity.Id
                            ProvVocabulary.Property.WasAttributedTo
                            record.Agent.Id)
                        "GeneratedEntity should be attributed to agent"
                }

                test "GeneratedEntity has prov:wasDerivedFrom usedEntity" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasTriple
                            graph
                            record.GeneratedEntity.Id
                            ProvVocabulary.Property.WasDerivedFrom
                            record.UsedEntity.Id)
                        "GeneratedEntity should be derived from usedEntity"
                }

                test "GeneratedEntity has frank:stateName" {
                    let record = makeRecord personAgent
                    let graph = GraphBuilder.toGraph [ record ]

                    Expect.isTrue
                        (hasLiteralTriple graph record.GeneratedEntity.Id ProvVocabulary.Frank.stateName "Submitted")
                        "GeneratedEntity should have frank:stateName"
                } ]

          testList
              "Edge cases"
              [ test "empty input produces empty graph" {
                    let graph = GraphBuilder.toGraph []
                    Expect.equal (graph.Triples.Count) 0 "Empty input should produce graph with no triples"
                }

                test "multiple records produce correct triple count" {
                    let record1 = makeRecord personAgent
                    let record2 = makeRecord softwareAgent
                    let graph = GraphBuilder.toGraph [ record1; record2 ]
                    Expect.isGreaterThan (graph.Triples.Count) 0 "Multiple records should produce triples"

                    // Both activities should exist
                    Expect.isTrue
                        (hasTriple graph record1.Activity.Id ProvVocabulary.Rdf.Type ProvVocabulary.Class.Activity)
                        "First activity should exist"

                    Expect.isTrue
                        (hasTriple graph record2.Activity.Id ProvVocabulary.Rdf.Type ProvVocabulary.Class.Activity)
                        "Second activity should exist"
                } ] ]
