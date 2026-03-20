module Frank.Provenance.Tests.CustomStoreTests

open System
open System.Threading.Tasks
open Expecto
open Frank.Provenance
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

// ---------------------------------------------------------------------------
// Test custom store
// ---------------------------------------------------------------------------

/// A simple in-memory IProvenanceStore that tracks all appended records and
/// whether it was ever disposed.  Used to verify DI replacement behaviour.
type TestCustomStore() =
    let records = ResizeArray<ProvenanceRecord>()
    let mutable disposed = false

    member _.AppendedRecords = records |> Seq.toList
    member _.RecordCount = records.Count
    member _.WasDisposed = disposed

    interface IProvenanceStore with
        member _.Append(record) = records.Add(record)

        member _.QueryByResource(uri) =
            records
            |> Seq.filter (fun r -> r.ResourceUri = uri)
            |> Seq.toList
            |> Task.FromResult

        member _.QueryByAgent(agentId) =
            records
            |> Seq.filter (fun r -> r.Agent.Id = agentId)
            |> Seq.toList
            |> Task.FromResult

        member _.QueryByTimeRange(startTime, endTime) =
            records
            |> Seq.filter (fun r -> r.RecordedAt >= startTime && r.RecordedAt <= endTime)
            |> Seq.toList
            |> Task.FromResult

    interface IDisposable with
        member _.Dispose() = disposed <- true

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private makeRecord (id: string) (resourceUri: string) (agentId: string) (recordedAt: DateTimeOffset) =
    let agent =
        { ProvenanceAgent.Id = agentId
          AgentType = AgentType.SoftwareAgent("test-agent") }

    let activity =
        { ProvenanceActivity.Id = $"activity-{id}"
          HttpMethod = "POST"
          ResourceUri = resourceUri
          EventName = "Transition"
          PreviousState = "Before"
          NewState = "After"
          StartedAt = recordedAt.AddMilliseconds(-50.0)
          EndedAt = recordedAt }

    let usedEntity =
        { ProvenanceEntity.Id = $"used-{id}"
          ResourceUri = resourceUri
          StateName = "Before"
          CapturedAt = recordedAt.AddMilliseconds(-50.0) }

    let generatedEntity =
        { ProvenanceEntity.Id = $"generated-{id}"
          ResourceUri = resourceUri
          StateName = "After"
          CapturedAt = recordedAt }

    { ProvenanceRecord.Id = id
      ResourceUri = resourceUri
      RecordedAt = recordedAt
      Activity = activity
      Agent = agent
      GeneratedEntity = generatedEntity
      UsedEntity = usedEntity }

let private createLogger () =
    let factory: ILoggerFactory =
        LoggerFactory.Create(fun (builder: ILoggingBuilder) ->
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug) |> ignore)

    factory.CreateLogger<MailboxProcessorProvenanceStore>()

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[<Tests>]
let customStoreTests =
    testList
        "CustomStore"
        [ testList
              "US4-SC1: Custom store receives records instead of default"
              [ testAsync "Custom IProvenanceStore registered before useProvenance receives all Append calls" {
                    let customStore = TestCustomStore()

                    let appBuilder = WebApplication.CreateBuilder([||])
                    appBuilder.WebHost.UseTestServer() |> ignore
                    appBuilder.Services.AddLogging() |> ignore
                    // Register the custom store BEFORE TryAddSingleton from useProvenance.
                    // AddSingleton takes precedence over TryAddSingleton.
                    appBuilder.Services.AddSingleton<IProvenanceStore>(customStore :> IProvenanceStore)
                    |> ignore
                    // Simulate what useProvenance does — TryAddSingleton should be a no-op.
                    appBuilder.Services.TryAddSingleton<IProvenanceStore>(fun sp ->
                        let logger = sp.GetRequiredService<ILogger<MailboxProcessorProvenanceStore>>()

                        new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, logger)
                        :> IProvenanceStore)
                    let app = appBuilder.Build()
                    app.Run(fun ctx -> ctx.Response.WriteAsync("ok")) |> ignore
                    app.Start()
                    use server = app.GetTestServer()

                    let store = server.Services.GetRequiredService<IProvenanceStore>()
                    let baseTime = DateTimeOffset(2025, 10, 1, 12, 0, 0, TimeSpan.Zero)
                    store.Append(makeRecord "r1" "/orders/1" "agent-a" baseTime)
                    store.Append(makeRecord "r2" "/orders/2" "agent-b" (baseTime.AddSeconds(1.0)))

                    Expect.equal customStore.RecordCount 2 "Custom store should have received both records"

                    Expect.isTrue
                        (customStore.AppendedRecords |> List.exists (fun r -> r.Id = "r1"))
                        "Custom store should contain r1"

                    Expect.isTrue
                        (customStore.AppendedRecords |> List.exists (fun r -> r.Id = "r2"))
                        "Custom store should contain r2"
                }

                test "TryAddSingleton does not replace an already-registered IProvenanceStore" {
                    use customStore = new TestCustomStore()
                    let services = ServiceCollection()
                    services.AddLogging() |> ignore

                    // Register custom store first.
                    services.AddSingleton<IProvenanceStore>(customStore :> IProvenanceStore)
                    |> ignore

                    // useProvenance uses TryAddSingleton — this should be a no-op.
                    services.TryAddSingleton<IProvenanceStore>(fun sp ->
                        let logger = sp.GetRequiredService<ILogger<MailboxProcessorProvenanceStore>>()
                        new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, logger) :> IProvenanceStore)

                    use sp = services.BuildServiceProvider()
                    let resolved = sp.GetRequiredService<IProvenanceStore>()

                    // The resolved instance must be the custom store, not a MailboxProcessorProvenanceStore.
                    Expect.isTrue
                        (Object.ReferenceEquals(resolved, customStore))
                        "TryAddSingleton must not replace the already-registered custom store"
                }

                test "Default MailboxProcessorProvenanceStore is NOT created when custom store is registered" {
                    use customStore = new TestCustomStore()
                    let services = ServiceCollection()
                    services.AddLogging() |> ignore

                    let mutable defaultStoreCreated = false

                    // Register custom store first.
                    services.AddSingleton<IProvenanceStore>(customStore :> IProvenanceStore)
                    |> ignore

                    // Register a sentinel factory that records whether it was called.
                    services.TryAddSingleton<IProvenanceStore>(fun sp ->
                        defaultStoreCreated <- true
                        let logger = sp.GetRequiredService<ILogger<MailboxProcessorProvenanceStore>>()
                        new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, logger) :> IProvenanceStore)

                    use sp = services.BuildServiceProvider()
                    // Resolve to trigger any lazy factory.
                    sp.GetRequiredService<IProvenanceStore>() |> ignore

                    Expect.isFalse
                        defaultStoreCreated
                        "MailboxProcessorProvenanceStore factory must NOT be invoked when custom store is registered"
                } ]

          testList
              "US4-SC2: Default store is queryable"
              [ testAsync "Default MailboxProcessorProvenanceStore is queryable by resource URI" {
                    let logger = createLogger ()

                    use store =
                        new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, logger)

                    let istore = store :> IProvenanceStore
                    let baseTime = DateTimeOffset(2025, 11, 1, 9, 0, 0, TimeSpan.Zero)

                    istore.Append(makeRecord "rec-1" "/resources/a" "agent-x" baseTime)
                    istore.Append(makeRecord "rec-2" "/resources/a" "agent-y" (baseTime.AddSeconds(1.0)))
                    istore.Append(makeRecord "rec-3" "/resources/b" "agent-x" (baseTime.AddSeconds(2.0)))

                    do! Async.Sleep 100

                    let! resultsA = istore.QueryByResource("/resources/a") |> Async.AwaitTask
                    Expect.equal resultsA.Length 2 "Should return 2 records for /resources/a"

                    Expect.isTrue
                        (resultsA |> List.forall (fun r -> r.ResourceUri = "/resources/a"))
                        "All results should belong to /resources/a"

                    let! resultsB = istore.QueryByResource("/resources/b") |> Async.AwaitTask
                    Expect.equal resultsB.Length 1 "Should return 1 record for /resources/b"
                }

                testAsync "Default MailboxProcessorProvenanceStore is queryable by agent ID" {
                    let logger = createLogger ()

                    use store =
                        new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, logger)

                    let istore = store :> IProvenanceStore
                    let baseTime = DateTimeOffset(2025, 11, 1, 9, 0, 0, TimeSpan.Zero)

                    istore.Append(makeRecord "rec-1" "/resources/a" "agent-x" baseTime)
                    istore.Append(makeRecord "rec-2" "/resources/b" "agent-x" (baseTime.AddSeconds(1.0)))
                    istore.Append(makeRecord "rec-3" "/resources/c" "agent-y" (baseTime.AddSeconds(2.0)))

                    do! Async.Sleep 100

                    let! byAgentX = istore.QueryByAgent("agent-x") |> Async.AwaitTask
                    Expect.equal byAgentX.Length 2 "agent-x should have 2 records"

                    Expect.isTrue
                        (byAgentX |> List.forall (fun r -> r.Agent.Id = "agent-x"))
                        "All results should belong to agent-x"

                    let! byAgentY = istore.QueryByAgent("agent-y") |> Async.AwaitTask
                    Expect.equal byAgentY.Length 1 "agent-y should have 1 record"
                }

                testAsync "Default MailboxProcessorProvenanceStore is queryable by time range" {
                    let logger = createLogger ()

                    use store =
                        new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, logger)

                    let istore = store :> IProvenanceStore
                    let baseTime = DateTimeOffset(2025, 11, 1, 9, 0, 0, TimeSpan.Zero)

                    istore.Append(makeRecord "rec-1" "/resources/a" "agent-x" baseTime)
                    istore.Append(makeRecord "rec-2" "/resources/b" "agent-x" (baseTime.AddHours(1.0)))
                    istore.Append(makeRecord "rec-3" "/resources/c" "agent-x" (baseTime.AddHours(3.0)))

                    do! Async.Sleep 100

                    let rangeStart = baseTime.AddMinutes(30.0)
                    let rangeEnd = baseTime.AddHours(2.0)

                    let! results = istore.QueryByTimeRange(rangeStart, rangeEnd) |> Async.AwaitTask
                    Expect.equal results.Length 1 "Should return 1 record within the time range"

                    Expect.equal results.[0].Id "rec-2" "Only rec-2 falls within the time range"
                } ] ]
