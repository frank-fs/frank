module Frank.Provenance.Tests.StoreTests

open System
open System.Threading.Tasks
open Expecto
open Frank.Provenance
open Microsoft.Extensions.Logging

let private createLogger () =
    let factory: ILoggerFactory =
        LoggerFactory.Create(fun (builder: ILoggingBuilder) ->
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug) |> ignore)

    factory.CreateLogger<MailboxProcessorProvenanceStore>()

let private makeRecord id resourceUri agentId (recordedAt: DateTimeOffset) =
    let agent =
        { ProvenanceAgent.Id = agentId
          AgentType = AgentType.SoftwareAgent("test-agent") }

    let usedEntity =
        { ProvenanceEntity.Id = $"used-{id}"
          ResourceUri = resourceUri
          StateName = "Before"
          CapturedAt = recordedAt.AddSeconds(-1.0) }

    let generatedEntity =
        { ProvenanceEntity.Id = $"generated-{id}"
          ResourceUri = resourceUri
          StateName = "After"
          CapturedAt = recordedAt }

    let activity =
        { ProvenanceActivity.Id = $"activity-{id}"
          HttpMethod = "POST"
          ResourceUri = resourceUri
          EventName = "Transition"
          PreviousState = "Before"
          NewState = "After"
          StartedAt = recordedAt.AddMilliseconds(-50.0)
          EndedAt = recordedAt }

    { ProvenanceRecord.Id = id
      ResourceUri = resourceUri
      RecordedAt = recordedAt
      Activity = activity
      Agent = agent
      GeneratedEntity = generatedEntity
      UsedEntity = usedEntity
      ActingRoles = [] }

let private defaultConfig = ProvenanceStoreConfig.defaults

let private createStore config =
    new MailboxProcessorProvenanceStore(config, createLogger ())

[<Tests>]
let storeTests =
    testList
        "Store"
        [ testList
              "IProvenanceStore interface"
              [ test "has Append method" {
                    use store = createStore defaultConfig
                    let istore = store :> IProvenanceStore

                    let record =
                        makeRecord "r1" "/orders/1" "agent-1" (DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero))

                    istore.Append(record)
                } ]

          testList
              "QueryByResource"
              [ testAsync "returns records matching resource URI" {
                    use store = createStore defaultConfig
                    let istore = store :> IProvenanceStore
                    let baseTime = DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero)

                    // 3 records for resource A
                    istore.Append(makeRecord "r1" "/orders/1" "agent-1" baseTime)
                    istore.Append(makeRecord "r2" "/orders/1" "agent-2" (baseTime.AddSeconds(1.0)))
                    istore.Append(makeRecord "r3" "/orders/1" "agent-1" (baseTime.AddSeconds(2.0)))

                    // 2 records for resource B
                    istore.Append(makeRecord "r4" "/orders/2" "agent-1" (baseTime.AddSeconds(3.0)))
                    istore.Append(makeRecord "r5" "/orders/2" "agent-2" (baseTime.AddSeconds(4.0)))

                    do! Async.Sleep 100

                    let! resultsA = istore.QueryByResource("/orders/1") |> Async.AwaitTask
                    Expect.equal resultsA.Length 3 "Should have 3 records for /orders/1"

                    let! resultsB = istore.QueryByResource("/orders/2") |> Async.AwaitTask
                    Expect.equal resultsB.Length 2 "Should have 2 records for /orders/2"
                } ]

          testList
              "QueryByAgent"
              [ testAsync "returns records matching agent ID" {
                    use store = createStore defaultConfig
                    let istore = store :> IProvenanceStore
                    let baseTime = DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero)

                    istore.Append(makeRecord "r1" "/orders/1" "agent-1" baseTime)
                    istore.Append(makeRecord "r2" "/orders/2" "agent-2" (baseTime.AddSeconds(1.0)))
                    istore.Append(makeRecord "r3" "/orders/3" "agent-1" (baseTime.AddSeconds(2.0)))

                    do! Async.Sleep 100

                    let! results = istore.QueryByAgent("agent-1") |> Async.AwaitTask
                    Expect.equal results.Length 2 "Should have 2 records for agent-1"

                    Expect.isTrue
                        (results |> List.forall (fun r -> r.Agent.Id = "agent-1"))
                        "All results should be for agent-1"
                } ]

          testList
              "QueryByTimeRange"
              [ testAsync "returns records within the specified time range" {
                    use store = createStore defaultConfig
                    let istore = store :> IProvenanceStore
                    let baseTime = DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero)

                    istore.Append(makeRecord "r1" "/orders/1" "agent-1" baseTime)

                    istore.Append(makeRecord "r2" "/orders/2" "agent-1" (baseTime.AddHours(1.0)))

                    istore.Append(makeRecord "r3" "/orders/3" "agent-1" (baseTime.AddHours(2.0)))

                    istore.Append(makeRecord "r4" "/orders/4" "agent-1" (baseTime.AddHours(3.0)))

                    istore.Append(makeRecord "r5" "/orders/5" "agent-1" (baseTime.AddHours(4.0)))

                    do! Async.Sleep 100

                    let rangeStart = baseTime.AddMinutes(30.0)
                    let rangeEnd = baseTime.AddHours(2.5)

                    let! results = istore.QueryByTimeRange(rangeStart, rangeEnd) |> Async.AwaitTask

                    Expect.equal results.Length 2 "Should have 2 records in the time range"

                    Expect.isTrue
                        (results
                         |> List.forall (fun r -> r.RecordedAt >= rangeStart && r.RecordedAt <= rangeEnd))
                        "All results should be within the time range"
                } ]

          testList
              "Retention policy"
              [ testAsync "evicts oldest records when max is exceeded" {
                    let config =
                        { MaxRecords = 10
                          EvictionBatchSize = 5 }

                    use store = createStore config
                    let istore = store :> IProvenanceStore
                    let baseTime = DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero)

                    for i in 1..15 do
                        istore.Append(makeRecord $"r{i}" $"/orders/{i}" "agent-1" (baseTime.AddSeconds(float i)))

                    do! Async.Sleep 100

                    let! allByAgent = istore.QueryByAgent("agent-1") |> Async.AwaitTask
                    Expect.equal allByAgent.Length 10 "Should retain 10 records after eviction"

                    // The oldest 5 should have been evicted
                    let ids = allByAgent |> List.map (fun r -> r.Id)
                    Expect.isFalse (ids |> List.contains "r1") "r1 should have been evicted"
                    Expect.isFalse (ids |> List.contains "r5") "r5 should have been evicted"
                    Expect.isTrue (ids |> List.contains "r6") "r6 should be retained"
                    Expect.isTrue (ids |> List.contains "r15") "r15 should be retained"
                } ]

          testList
              "Disposal"
              [ testAsync "queries throw ObjectDisposedException after dispose" {
                    let store = createStore defaultConfig
                    let istore = store :> IProvenanceStore
                    let baseTime = DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero)

                    istore.Append(makeRecord "r1" "/orders/1" "agent-1" baseTime)
                    do! Async.Sleep 100

                    (store :> IDisposable).Dispose()

                    Expect.throws
                        (fun () -> istore.QueryByResource("/orders/1") |> ignore)
                        "Should throw ObjectDisposedException after dispose"
                }

                testAsync "append after dispose throws ObjectDisposedException" {
                    let store = createStore defaultConfig
                    let istore = store :> IProvenanceStore
                    (store :> IDisposable).Dispose()

                    let record =
                        makeRecord "r1" "/orders/1" "agent-1" (DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero))

                    Expect.throws (fun () -> istore.Append(record)) "Should throw ObjectDisposedException after dispose"
                } ]

          testList
              "Concurrent appends"
              [ testAsync "handles 1000 concurrent appends" {
                    use store = createStore defaultConfig
                    let istore = store :> IProvenanceStore
                    let baseTime = DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero)

                    let tasks =
                        [| for i in 1..1000 do
                               Task.Run(fun () ->
                                   istore.Append(
                                       makeRecord $"r{i}" "/orders/concurrent" "agent-1" (baseTime.AddTicks(int64 i))
                                   )) |]

                    do! Task.WhenAll(tasks) |> Async.AwaitTask
                    do! Async.Sleep 100

                    let! results = istore.QueryByResource("/orders/concurrent") |> Async.AwaitTask
                    Expect.equal results.Length 1000 "All 1000 records should be stored"
                } ]

          testList
              "Empty store"
              [ testAsync "QueryByResource returns empty for unknown URI" {
                    use store = createStore defaultConfig
                    let istore = store :> IProvenanceStore
                    let! results = istore.QueryByResource("/nonexistent") |> Async.AwaitTask
                    Expect.isEmpty results "Should return empty for unknown resource"
                }

                testAsync "QueryByAgent returns empty for unknown agent" {
                    use store = createStore defaultConfig
                    let istore = store :> IProvenanceStore
                    let! results = istore.QueryByAgent("unknown-agent") |> Async.AwaitTask
                    Expect.isEmpty results "Should return empty for unknown agent"
                }

                testAsync "QueryByTimeRange returns empty for empty store" {
                    use store = createStore defaultConfig
                    let istore = store :> IProvenanceStore
                    let baseTime = DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero)

                    let! results = istore.QueryByTimeRange(baseTime, baseTime.AddHours(1.0)) |> Async.AwaitTask

                    Expect.isEmpty results "Should return empty for empty store"
                } ] ]
