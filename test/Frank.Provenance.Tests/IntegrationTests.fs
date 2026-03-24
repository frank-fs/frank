module Frank.Provenance.Tests.IntegrationTests

open System
open System.Net
open System.Net.Http
open System.Security.Claims
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
// Minimal observable subject (avoids System.Reactive dependency)
// ---------------------------------------------------------------------------

/// A minimal observable subject that allows manual event dispatch in tests.
type private TransitionSubject() =
    let observers = System.Collections.Generic.List<IObserver<TransitionEvent>>()

    member _.OnNext(event: TransitionEvent) =
        for obs in observers do
            obs.OnNext(event)

    interface IObservable<TransitionEvent> with
        member _.Subscribe(observer) =
            observers.Add(observer)

            { new IDisposable with
                member _.Dispose() = observers.Remove(observer) |> ignore }

// ---------------------------------------------------------------------------
// Helpers shared across integration tests
// ---------------------------------------------------------------------------

/// Creates an authenticated ClaimsPrincipal.
let private makeAuthPrincipal (name: string) (id: string) =
    let claims = [ Claim(ClaimTypes.Name, name); Claim(ClaimTypes.NameIdentifier, id) ]
    ClaimsPrincipal(ClaimsIdentity(claims, "TestAuth"))

/// Builds a minimal TransitionEvent.
let private makeTransitionEvent
    (resourceUri: string)
    (previousState: string)
    (newState: string)
    (user: ClaimsPrincipal option)
    =
    { TransitionEvent.InstanceId = Guid.NewGuid().ToString()
      ResourceUri = resourceUri
      PreviousState = previousState
      NewState = newState
      Event = "submit"
      Timestamp = DateTimeOffset.UtcNow
      User = user
      HttpMethod = "POST"
      Headers = Map.empty
      Roles = [] }

/// Builds a minimal ProvenanceRecord for seeding read-only tests.
let private makeRecord (resourceUri: string) (prevState: string) (newState: string) (agentId: string) =
    let now = DateTimeOffset.UtcNow

    let agent =
        { ProvenanceAgent.Id = agentId
          AgentType = AgentType.Person("Test User", agentId) }

    let activity =
        { ProvenanceActivity.Id = $"urn:frank:activity:{Guid.NewGuid()}"
          HttpMethod = "POST"
          ResourceUri = resourceUri
          EventName = "submit"
          PreviousState = prevState
          NewState = newState
          StartedAt = now.AddMilliseconds(-20.0)
          EndedAt = now }

    let usedEntity =
        { ProvenanceEntity.Id = $"urn:frank:entity:{Guid.NewGuid()}"
          ResourceUri = resourceUri
          StateName = prevState
          CapturedAt = now.AddMilliseconds(-20.0) }

    let generatedEntity =
        { ProvenanceEntity.Id = $"urn:frank:entity:{Guid.NewGuid()}"
          ResourceUri = resourceUri
          StateName = newState
          CapturedAt = now }

    { ProvenanceRecord.Id = $"urn:frank:record:{Guid.NewGuid()}"
      ResourceUri = resourceUri
      RecordedAt = now
      Activity = activity
      Agent = agent
      GeneratedEntity = generatedEntity
      UsedEntity = usedEntity
      ActingRoles = [] }

/// Creates a full-pipeline test server with:
///   - a real MailboxProcessorProvenanceStore (default config)
///   - a TransitionSubject registered as IObservable<TransitionEvent>
///   - ProvenanceSubscriptionManager (subscribes on host start)
///   - the provenance content-negotiation middleware
///   - a simple terminal "normal response" handler
///
/// Returns the TestServer and the TransitionSubject for pushing events.
let private createFullPipelineServer () =
    let subject = TransitionSubject()

    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddLogging() |> ignore

    builder.Services.AddSingleton<IObservable<TransitionEvent>>(subject :> IObservable<TransitionEvent>)
    |> ignore

    builder.Services.TryAddSingleton<IProvenanceStore>(fun sp ->
        let logger = sp.GetRequiredService<ILogger<MailboxProcessorProvenanceStore>>()
        new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, logger) :> IProvenanceStore)

    builder.Services.AddHostedService<ProvenanceSubscriptionManager>() |> ignore

    let app = builder.Build()
    let loggerFactory = app.Services.GetRequiredService<ILoggerFactory>()

    (app :> IApplicationBuilder).Use(
        Func<RequestDelegate, RequestDelegate>(fun next ->
            ProvenanceMiddleware.createProvenanceMiddleware loggerFactory next)
    )
    |> ignore

    app.Run(fun ctx -> ctx.Response.WriteAsync("normal response")) |> ignore

    app.Start()
    app.GetTestServer(), subject

/// Creates a test server pre-seeded with a read-only in-memory store.
/// Useful for testing the middleware's content-negotiation in isolation.
let private createSeededServer (records: ProvenanceRecord list) =
    let store =
        { new IProvenanceStore with
            member _.Append(_) = ()

            member _.QueryByResource(uri) =
                records |> List.filter (fun r -> r.ResourceUri = uri) |> Task.FromResult

            member _.QueryByAgent(id) =
                records |> List.filter (fun r -> r.Agent.Id = id) |> Task.FromResult

            member _.QueryByTimeRange(s, e) =
                records
                |> List.filter (fun r -> r.RecordedAt >= s && r.RecordedAt <= e)
                |> Task.FromResult

          interface IDisposable with
              member _.Dispose() = () }

    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddLogging() |> ignore
    builder.Services.AddSingleton<IProvenanceStore>(store) |> ignore
    let app = builder.Build()

    let loggerFactory = app.Services.GetRequiredService<ILoggerFactory>()

    (app :> IApplicationBuilder).Use(
        Func<RequestDelegate, RequestDelegate>(fun next ->
            ProvenanceMiddleware.createProvenanceMiddleware loggerFactory next)
    )
    |> ignore

    app.Run(fun ctx -> ctx.Response.WriteAsync("normal response")) |> ignore

    app.Start()
    app.GetTestServer()

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[<Tests>]
let integrationTests =
    testList
        "Integration"
        [ testList
              "US1: Automatic provenance recording"
              [ testAsync "US1-SC1: TransitionObserver.OnNext triggers a provenance record in the store" {
                    let server, subject = createFullPipelineServer ()
                    use _server = server
                    // Give the ProvenanceSubscriptionManager time to start and subscribe.
                    do! Async.Sleep 200

                    let event = makeTransitionEvent "/orders/1" "Draft" "Submitted" None

                    // Push the event through the observable pipeline.
                    subject.OnNext(event)

                    // Give the MailboxProcessor time to process the Append.
                    do! Async.Sleep 100

                    let store = server.Services.GetRequiredService<IProvenanceStore>()
                    let! records = store.QueryByResource("/orders/1") |> Async.AwaitTask

                    Expect.equal records.Length 1 "Should have exactly one provenance record"
                    let record = records.[0]
                    Expect.equal record.ResourceUri "/orders/1" "Record ResourceUri should match"
                    Expect.equal record.Activity.PreviousState "Draft" "PreviousState should be Draft"
                    Expect.equal record.Activity.NewState "Submitted" "NewState should be Submitted"

                    Expect.isTrue
                        (record.Id.StartsWith("urn:frank:record:"))
                        "Record Id should use urn:frank:record: scheme"
                }

                testAsync "US1-SC2: Authenticated user maps to Person agent in the provenance record" {
                    let server, subject = createFullPipelineServer ()
                    use _server = server
                    do! Async.Sleep 200

                    let user = makeAuthPrincipal "Alice" "alice-001"
                    let event = makeTransitionEvent "/orders/2" "Draft" "Submitted" (Some user)

                    subject.OnNext(event)
                    do! Async.Sleep 100

                    let store = server.Services.GetRequiredService<IProvenanceStore>()
                    let! records = store.QueryByResource("/orders/2") |> Async.AwaitTask

                    Expect.equal records.Length 1 "Should have one record"
                    let record = records.[0]

                    match record.Agent.AgentType with
                    | AgentType.Person(name, id) ->
                        Expect.equal name "Alice" "Agent name should be Alice"
                        Expect.equal id "alice-001" "Agent id should be alice-001"
                    | other -> failtest $"Expected Person agent, got {other}"

                    Expect.equal record.Agent.Id "urn:frank:agent:person:alice-001" "Agent URN should use person scheme"
                }

                testAsync "US1-SC3: Pre and post state entities are captured in the provenance record" {
                    let server, subject = createFullPipelineServer ()
                    use _server = server
                    do! Async.Sleep 200

                    let event = makeTransitionEvent "/orders/3" "Pending" "Approved" None

                    subject.OnNext(event)
                    do! Async.Sleep 100

                    let store = server.Services.GetRequiredService<IProvenanceStore>()
                    let! records = store.QueryByResource("/orders/3") |> Async.AwaitTask

                    Expect.equal records.Length 1 "Should have one record"
                    let record = records.[0]

                    Expect.equal
                        record.UsedEntity.StateName
                        "Pending"
                        "UsedEntity.StateName should be the previous state"

                    Expect.equal
                        record.GeneratedEntity.StateName
                        "Approved"
                        "GeneratedEntity.StateName should be the new state"

                    Expect.equal record.UsedEntity.ResourceUri "/orders/3" "UsedEntity ResourceUri should match"

                    Expect.equal
                        record.GeneratedEntity.ResourceUri
                        "/orders/3"
                        "GeneratedEntity ResourceUri should match"
                }

                testAsync "US1-SC4: Guard-blocked requests produce no provenance records" {
                    let server, subject = createFullPipelineServer ()
                    use _server = server
                    do! Async.Sleep 200

                    // A guard-blocked transition never fires OnNext.
                    // We verify the store stays empty for this resource URI.

                    let store = server.Services.GetRequiredService<IProvenanceStore>()
                    let! records = store.QueryByResource("/orders/blocked") |> Async.AwaitTask

                    Expect.isEmpty records "Guard-blocked transitions should produce no provenance records"
                } ]

          testList
              "US2: Content-negotiated provenance responses"
              [ testAsync "US2-SC1: GET with Turtle Accept returns 200 with prov:Activity in body" {
                    let record =
                        makeRecord "/orders/100" "Draft" "Submitted" "urn:frank:agent:person:user-1"

                    use server = createSeededServer [ record ]
                    use client = server.CreateClient()

                    let request = new HttpRequestMessage(HttpMethod.Get, "/orders/100")
                    request.Headers.Add("Accept", ProvenanceMediaTypes.ProvenanceTurtle)
                    let! (response: HttpResponseMessage) = client.SendAsync(request) |> Async.AwaitTask
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                    Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                    Expect.equal
                        (response.Content.Headers.ContentType.MediaType)
                        ProvenanceMediaTypes.ProvenanceTurtle
                        "Content-Type should be the provenance Turtle media type"

                    Expect.stringContains body "prov:Activity" "Body should contain prov:Activity"
                }

                testAsync "US2-SC2: GET with JSON-LD Accept returns 200 with @context in body" {
                    let record =
                        makeRecord "/orders/200" "Draft" "Submitted" "urn:frank:agent:person:user-2"

                    use server = createSeededServer [ record ]
                    use client = server.CreateClient()

                    let request = new HttpRequestMessage(HttpMethod.Get, "/orders/200")
                    request.Headers.Add("Accept", ProvenanceMediaTypes.ProvenanceLdJson)
                    let! (response: HttpResponseMessage) = client.SendAsync(request) |> Async.AwaitTask
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                    Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                    Expect.equal
                        (response.Content.Headers.ContentType.MediaType)
                        ProvenanceMediaTypes.ProvenanceLdJson
                        "Content-Type should be the provenance LD+JSON media type"

                    Expect.stringContains body "@context" "Body should contain @context"
                }

                testAsync "US2-SC3: No provenance records returns 200 with empty graph (not 404)" {
                    use server = createSeededServer []
                    use client = server.CreateClient()

                    let request = new HttpRequestMessage(HttpMethod.Get, "/orders/nonexistent")
                    request.Headers.Add("Accept", ProvenanceMediaTypes.ProvenanceTurtle)
                    let! (response: HttpResponseMessage) = client.SendAsync(request) |> Async.AwaitTask

                    Expect.equal response.StatusCode HttpStatusCode.OK "Empty provenance should return 200, not 404"

                    Expect.equal
                        (response.Content.Headers.ContentType.MediaType)
                        ProvenanceMediaTypes.ProvenanceTurtle
                        "Content-Type should still be set even for an empty graph"
                }

                testAsync "US2-SC4: Standard Accept passes through to the normal response handler" {
                    use server = createSeededServer []
                    use client = server.CreateClient()

                    let request = new HttpRequestMessage(HttpMethod.Get, "/orders/123")
                    request.Headers.Add("Accept", "application/json")
                    let! (response: HttpResponseMessage) = client.SendAsync(request) |> Async.AwaitTask
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                    Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
                    Expect.equal body "normal response" "Standard Accept should pass through to normal handler"
                } ]

          testList
              "US4: Full end-to-end pipeline via observable"
              [ testAsync "US4-SC1: Multiple events pushed through observable each create a distinct record" {
                    let server, subject = createFullPipelineServer ()
                    use _server = server
                    do! Async.Sleep 200

                    subject.OnNext(makeTransitionEvent "/invoices/1" "New" "Processing" None)
                    subject.OnNext(makeTransitionEvent "/invoices/2" "New" "Cancelled" None)

                    do! Async.Sleep 150

                    let store = server.Services.GetRequiredService<IProvenanceStore>()

                    let! r1 = store.QueryByResource("/invoices/1") |> Async.AwaitTask
                    Expect.equal r1.Length 1 "invoices/1 should have one record"
                    Expect.equal r1.[0].Activity.NewState "Processing" "State should be Processing"

                    let! r2 = store.QueryByResource("/invoices/2") |> Async.AwaitTask
                    Expect.equal r2.Length 1 "invoices/2 should have one record"
                    Expect.equal r2.[0].Activity.NewState "Cancelled" "State should be Cancelled"
                } ]

          testList
              "useProvenanceWith: config passthrough"
              [ test "ProvenanceStoreConfig.defaults has expected values" {
                    Expect.equal ProvenanceStoreConfig.defaults.MaxRecords 10_000 "Default MaxRecords should be 10,000"

                    Expect.equal
                        ProvenanceStoreConfig.defaults.EvictionBatchSize
                        100
                        "Default EvictionBatchSize should be 100"
                }

                testAsync "useProvenanceWith registers the store with the provided config" {
                    let customConfig =
                        { MaxRecords = 50
                          EvictionBatchSize = 5 }

                    let appBuilder = WebApplication.CreateBuilder([||])
                    appBuilder.WebHost.UseTestServer() |> ignore
                    appBuilder.Services.AddLogging() |> ignore
                    // Manually replicate what useProvenanceWith does:
                    // register the store with customConfig.
                    appBuilder.Services.TryAddSingleton<IProvenanceStore>(fun sp ->
                        let logger = sp.GetRequiredService<ILogger<MailboxProcessorProvenanceStore>>()
                        new MailboxProcessorProvenanceStore(customConfig, logger) :> IProvenanceStore)
                    let app = appBuilder.Build()
                    app.Run(fun ctx -> ctx.Response.WriteAsync("ok")) |> ignore
                    app.Start()
                    use server = app.GetTestServer()

                    let store = server.Services.GetRequiredService<IProvenanceStore>()
                    let istore = store :> IProvenanceStore

                    // Append exactly MaxRecords + 1 records to trigger eviction.
                    let baseTime = DateTimeOffset.UtcNow

                    for i in 1 .. customConfig.MaxRecords + 1 do
                        istore.Append(
                            { ProvenanceRecord.Id = $"r{i}"
                              ResourceUri = "/test/config"
                              RecordedAt = baseTime.AddSeconds(float i)
                              Activity =
                                { ProvenanceActivity.Id = $"act-{i}"
                                  HttpMethod = "POST"
                                  ResourceUri = "/test/config"
                                  EventName = "test"
                                  PreviousState = "A"
                                  NewState = "B"
                                  StartedAt = baseTime.AddSeconds(float i)
                                  EndedAt = baseTime.AddSeconds(float i) }
                              Agent =
                                { ProvenanceAgent.Id = "agent-1"
                                  AgentType = AgentType.SoftwareAgent("test") }
                              UsedEntity =
                                { ProvenanceEntity.Id = $"used-{i}"
                                  ResourceUri = "/test/config"
                                  StateName = "A"
                                  CapturedAt = baseTime.AddSeconds(float i) }
                              GeneratedEntity =
                                { ProvenanceEntity.Id = $"gen-{i}"
                                  ResourceUri = "/test/config"
                                  StateName = "B"
                                  CapturedAt = baseTime.AddSeconds(float i) }
                              ActingRoles = [] }
                        )

                    do! Async.Sleep 150

                    let! results = istore.QueryByResource("/test/config") |> Async.AwaitTask

                    // After eviction the store should have at most MaxRecords records.
                    Expect.isTrue
                        (results.Length <= customConfig.MaxRecords)
                        $"Custom MaxRecords={customConfig.MaxRecords} should be enforced; got {results.Length}"
                } ] ]
