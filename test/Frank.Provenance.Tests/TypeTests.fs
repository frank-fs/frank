module Frank.Provenance.Tests.TypeTests

open System
open Expecto
open Frank.Provenance

[<Tests>]
let typeTests =
    testList
        "Types"
        [ testList
              "AgentType"
              [ test "Person carries name and identifier" {
                    let agent = AgentType.Person("Alice", "alice-001")

                    match agent with
                    | AgentType.Person(name, id) ->
                        Expect.equal name "Alice" "name"
                        Expect.equal id "alice-001" "identifier"
                    | _ -> failtest "Expected Person"
                }

                test "SoftwareAgent carries identifier" {
                    let agent = AgentType.SoftwareAgent("frank-middleware")

                    match agent with
                    | AgentType.SoftwareAgent id -> Expect.equal id "frank-middleware" "identifier"
                    | _ -> failtest "Expected SoftwareAgent"
                }

                test "LlmAgent carries identifier and optional model" {
                    let withModel = AgentType.LlmAgent("claude", Some "claude-opus-4-6")
                    let withoutModel = AgentType.LlmAgent("gpt", None)

                    match withModel with
                    | AgentType.LlmAgent(id, model) ->
                        Expect.equal id "claude" "identifier"
                        Expect.equal model (Some "claude-opus-4-6") "model"
                    | _ -> failtest "Expected LlmAgent"

                    match withoutModel with
                    | AgentType.LlmAgent(_, model) -> Expect.isNone model "model should be None"
                    | _ -> failtest "Expected LlmAgent"
                } ]

          testList
              "ProvenanceAgent"
              [ test "can be constructed" {
                    let agent =
                        { ProvenanceAgent.Id = "agent-1"
                          AgentType = AgentType.Person("Bob", "bob-42") }

                    Expect.equal agent.Id "agent-1" "Id"
                } ]

          testList
              "ProvenanceEntity"
              [ test "can be constructed" {
                    let now = DateTimeOffset.UtcNow

                    let entity =
                        { ProvenanceEntity.Id = "entity-1"
                          ResourceUri = "/orders/123"
                          StateName = "Submitted"
                          CapturedAt = now }

                    Expect.equal entity.Id "entity-1" "Id"
                    Expect.equal entity.ResourceUri "/orders/123" "ResourceUri"
                    Expect.equal entity.StateName "Submitted" "StateName"
                    Expect.equal entity.CapturedAt now "CapturedAt"
                } ]

          testList
              "ProvenanceActivity"
              [ test "can be constructed" {
                    let start = DateTimeOffset.UtcNow
                    let finish = start.AddMilliseconds(50.0)

                    let activity =
                        { ProvenanceActivity.Id = "activity-1"
                          HttpMethod = "POST"
                          ResourceUri = "/orders/123"
                          EventName = "Submit"
                          PreviousState = "Draft"
                          NewState = "Submitted"
                          StartedAt = start
                          EndedAt = finish }

                    Expect.equal activity.HttpMethod "POST" "HttpMethod"
                    Expect.equal activity.EventName "Submit" "EventName"
                    Expect.equal activity.PreviousState "Draft" "PreviousState"
                    Expect.equal activity.NewState "Submitted" "NewState"
                } ]

          testList
              "ProvenanceRecord"
              [ test "can be constructed with all fields" {
                    let now = DateTimeOffset.UtcNow

                    let agent =
                        { ProvenanceAgent.Id = "agent-1"
                          AgentType = AgentType.SoftwareAgent("test-harness") }

                    let usedEntity =
                        { ProvenanceEntity.Id = "entity-before"
                          ResourceUri = "/orders/123"
                          StateName = "Draft"
                          CapturedAt = now.AddSeconds(-1.0) }

                    let generatedEntity =
                        { ProvenanceEntity.Id = "entity-after"
                          ResourceUri = "/orders/123"
                          StateName = "Submitted"
                          CapturedAt = now }

                    let activity =
                        { ProvenanceActivity.Id = "activity-1"
                          HttpMethod = "POST"
                          ResourceUri = "/orders/123"
                          EventName = "Submit"
                          PreviousState = "Draft"
                          NewState = "Submitted"
                          StartedAt = now.AddMilliseconds(-50.0)
                          EndedAt = now }

                    let record =
                        { ProvenanceRecord.Id = "record-1"
                          ResourceUri = "/orders/123"
                          RecordedAt = now
                          Activity = activity
                          Agent = agent
                          GeneratedEntity = generatedEntity
                          UsedEntity = usedEntity
                          ActingRoles = [] }

                    Expect.equal record.Id "record-1" "Id"
                    Expect.equal record.ResourceUri "/orders/123" "ResourceUri"
                    Expect.equal record.Agent.Id "agent-1" "Agent.Id"
                    Expect.equal record.GeneratedEntity.StateName "Submitted" "GeneratedEntity.StateName"
                    Expect.equal record.UsedEntity.StateName "Draft" "UsedEntity.StateName"
                } ]

          testList
              "ProvenanceGraph"
              [ test "can be constructed with empty records" {
                    let graph =
                        { ProvenanceGraph.ResourceUri = "/orders/123"
                          Records = [] }

                    Expect.equal graph.ResourceUri "/orders/123" "ResourceUri"
                    Expect.isEmpty graph.Records "Records should be empty"
                } ]

          testList
              "ProvenanceStoreConfig"
              [ test "defaults has expected values" {
                    let config = ProvenanceStoreConfig.defaults
                    Expect.equal config.MaxRecords 10_000 "MaxRecords"
                    Expect.equal config.EvictionBatchSize 100 "EvictionBatchSize"
                } ] ]
