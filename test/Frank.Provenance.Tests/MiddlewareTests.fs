module Frank.Provenance.Tests.MiddlewareTests

open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Expecto
open Frank.Provenance
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection

// -- Test helpers --

let private makeRecord resourceUri =
    let now = DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero)

    let usedEntity =
        { ProvenanceEntity.Id = "urn:frank:entity:used-1"
          ResourceUri = resourceUri
          StateName = "Draft"
          CapturedAt = now.AddSeconds(-1.0) }

    let generatedEntity =
        { ProvenanceEntity.Id = "urn:frank:entity:gen-1"
          ResourceUri = resourceUri
          StateName = "Submitted"
          CapturedAt = now }

    let activity =
        { ProvenanceActivity.Id = "urn:frank:activity:act-1"
          HttpMethod = "POST"
          ResourceUri = resourceUri
          EventName = "Submit"
          PreviousState = "Draft"
          NewState = "Submitted"
          StartedAt = now.AddMilliseconds(-50.0)
          EndedAt = now }

    let agent =
        { ProvenanceAgent.Id = "urn:frank:agent:alice"
          AgentType = AgentType.Person("Alice", "alice-001") }

    { ProvenanceRecord.Id = "urn:frank:record:rec-1"
      ResourceUri = resourceUri
      RecordedAt = now
      Activity = activity
      Agent = agent
      GeneratedEntity = generatedEntity
      UsedEntity = usedEntity }

/// A simple in-memory IProvenanceStore for testing.
type private TestProvenanceStore(records: ProvenanceRecord list) =
    interface IProvenanceStore with
        member _.Append(_) = ()

        member _.QueryByResource(resourceUri) =
            records |> List.filter (fun r -> r.ResourceUri = resourceUri) |> Task.FromResult

        member _.QueryByAgent(agentId) =
            records |> List.filter (fun r -> r.Agent.Id = agentId) |> Task.FromResult

        member _.QueryByTimeRange(startTime, endTime) =
            records
            |> List.filter (fun r -> r.RecordedAt >= startTime && r.RecordedAt <= endTime)
            |> Task.FromResult

    interface IDisposable with
        member _.Dispose() = ()

let private createTestServer (store: IProvenanceStore) =
    let builder =
        WebHostBuilder()
            .ConfigureServices(fun services ->
                services.AddSingleton<IProvenanceStore>(store) |> ignore
                services.AddLogging() |> ignore)
            .Configure(fun app ->
                app.Use(
                    Func<HttpContext, RequestDelegate, Task>(fun ctx next ->
                        ProvenanceMiddleware.provenanceMiddleware next ctx)
                )
                |> ignore

                app.Run(fun ctx -> ctx.Response.WriteAsync("normal response")) |> ignore)

    new TestServer(builder)

[<Tests>]
let middlewareTests =
    testList
        "ProvenanceMiddleware"
        [ testList
              "Accept header matching"
              [ test "tryMatchProvenanceAccept returns None for null" {
                    let result = ProvenanceMiddleware.tryMatchProvenanceAccept null
                    Expect.isNone result "null Accept header should return None"
                }

                test "tryMatchProvenanceAccept returns None for standard Accept" {
                    let result = ProvenanceMiddleware.tryMatchProvenanceAccept "application/json"
                    Expect.isNone result "Standard Accept should return None"
                }

                test "tryMatchProvenanceAccept matches +turtle" {
                    let result =
                        ProvenanceMiddleware.tryMatchProvenanceAccept ProvenanceMediaTypes.ProvenanceTurtle

                    Expect.equal result (Some ProvenanceMediaTypes.ProvenanceTurtle) "Should match Turtle"
                }

                test "tryMatchProvenanceAccept matches +ld+json" {
                    let result =
                        ProvenanceMiddleware.tryMatchProvenanceAccept ProvenanceMediaTypes.ProvenanceLdJson

                    Expect.equal result (Some ProvenanceMediaTypes.ProvenanceLdJson) "Should match LD+JSON"
                }

                test "tryMatchProvenanceAccept matches +json" {
                    let result =
                        ProvenanceMiddleware.tryMatchProvenanceAccept ProvenanceMediaTypes.ProvenanceJson

                    Expect.equal result (Some ProvenanceMediaTypes.ProvenanceJson) "Should match JSON"
                }

                test "tryMatchProvenanceAccept matches +rdf+xml" {
                    let result =
                        ProvenanceMiddleware.tryMatchProvenanceAccept ProvenanceMediaTypes.ProvenanceRdfXml

                    Expect.equal result (Some ProvenanceMediaTypes.ProvenanceRdfXml) "Should match RDF+XML"
                }

                test "tryMatchProvenanceAccept prefers +ld+json over +json in mixed header" {
                    let header =
                        sprintf
                            "text/html, %s, %s"
                            ProvenanceMediaTypes.ProvenanceLdJson
                            ProvenanceMediaTypes.ProvenanceJson

                    let result = ProvenanceMiddleware.tryMatchProvenanceAccept header
                    Expect.equal result (Some ProvenanceMediaTypes.ProvenanceLdJson) "Should prefer +ld+json"
                }

                test "tryMatchProvenanceAccept finds provenance type in mixed Accept header" {
                    let header = sprintf "text/html, %s" ProvenanceMediaTypes.ProvenanceTurtle
                    let result = ProvenanceMiddleware.tryMatchProvenanceAccept header

                    Expect.equal
                        result
                        (Some ProvenanceMediaTypes.ProvenanceTurtle)
                        "Should find turtle in mixed header"
                } ]

          testList
              "Middleware integration"
              [ testAsync "Turtle provenance response returns 200 with correct content type" {
                    let record = makeRecord "/orders/123"
                    let store = new TestProvenanceStore([ record ])
                    use server = createTestServer store
                    use client = server.CreateClient()

                    let request = new HttpRequestMessage(HttpMethod.Get, "/orders/123")

                    request.Headers.Add("Accept", ProvenanceMediaTypes.ProvenanceTurtle)
                    let! (response: HttpResponseMessage) = client.SendAsync(request) |> Async.AwaitTask
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                    Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                    Expect.equal
                        (response.Content.Headers.ContentType.MediaType)
                        ProvenanceMediaTypes.ProvenanceTurtle
                        "Content-Type should be the provenance media type"

                    Expect.stringContains body "prov:Activity" "Body should contain prov:Activity"
                }

                testAsync "JSON-LD provenance response returns 200 with JSON content" {
                    let record = makeRecord "/orders/456"
                    let store = new TestProvenanceStore([ record ])
                    use server = createTestServer store
                    use client = server.CreateClient()

                    let request = new HttpRequestMessage(HttpMethod.Get, "/orders/456")

                    request.Headers.Add("Accept", ProvenanceMediaTypes.ProvenanceLdJson)
                    let! (response: HttpResponseMessage) = client.SendAsync(request) |> Async.AwaitTask
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                    Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                    Expect.equal
                        (response.Content.Headers.ContentType.MediaType)
                        ProvenanceMediaTypes.ProvenanceLdJson
                        "Content-Type should be the provenance media type"

                    Expect.stringContains body "@context" "Body should contain @context for JSON-LD"
                }

                testAsync "RDF/XML provenance response returns 200" {
                    let record = makeRecord "/orders/789"
                    let store = new TestProvenanceStore([ record ])
                    use server = createTestServer store
                    use client = server.CreateClient()

                    let request = new HttpRequestMessage(HttpMethod.Get, "/orders/789")

                    request.Headers.Add("Accept", ProvenanceMediaTypes.ProvenanceRdfXml)
                    let! (response: HttpResponseMessage) = client.SendAsync(request) |> Async.AwaitTask
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                    Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                    Expect.equal
                        (response.Content.Headers.ContentType.MediaType)
                        ProvenanceMediaTypes.ProvenanceRdfXml
                        "Content-Type should be the provenance media type"

                    Expect.stringContains body "rdf:RDF" "Body should contain rdf:RDF element"
                }

                testAsync "Standard Accept header passes through to normal handler" {
                    let store = new TestProvenanceStore([])
                    use server = createTestServer store
                    use client = server.CreateClient()

                    let request = new HttpRequestMessage(HttpMethod.Get, "/orders/123")
                    request.Headers.Add("Accept", "application/json")
                    let! (response: HttpResponseMessage) = client.SendAsync(request) |> Async.AwaitTask
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                    Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
                    Expect.equal body "normal response" "Should pass through to normal handler"
                }

                testAsync "Non-GET request with provenance Accept passes through" {
                    let store = new TestProvenanceStore([])
                    use server = createTestServer store
                    use client = server.CreateClient()

                    let request = new HttpRequestMessage(HttpMethod.Post, "/orders/123")
                    request.Headers.Add("Accept", ProvenanceMediaTypes.ProvenanceTurtle)
                    request.Content <- new StringContent("{}")
                    let! (response: HttpResponseMessage) = client.SendAsync(request) |> Async.AwaitTask
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                    Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
                    Expect.equal body "normal response" "POST should pass through even with provenance Accept"
                }

                testAsync "Empty provenance returns 200 with valid graph (not 404)" {
                    let store = new TestProvenanceStore([])
                    use server = createTestServer store
                    use client = server.CreateClient()

                    let request = new HttpRequestMessage(HttpMethod.Get, "/orders/unknown")
                    request.Headers.Add("Accept", ProvenanceMediaTypes.ProvenanceTurtle)
                    let! (response: HttpResponseMessage) = client.SendAsync(request) |> Async.AwaitTask
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                    Expect.equal response.StatusCode HttpStatusCode.OK "Empty provenance should return 200, not 404"

                    Expect.equal
                        (response.Content.Headers.ContentType.MediaType)
                        ProvenanceMediaTypes.ProvenanceTurtle
                        "Content-Type should still be set"

                    // Body should be valid (may be empty or just prefix declarations)
                    Expect.isNotNull body "Body should not be null"
                }

                testAsync "No Accept header passes through to normal handler" {
                    let store = new TestProvenanceStore([])
                    use server = createTestServer store
                    use client = server.CreateClient()

                    let request = new HttpRequestMessage(HttpMethod.Get, "/orders/123")
                    // Explicitly clear Accept header
                    request.Headers.Accept.Clear()
                    let! (response: HttpResponseMessage) = client.SendAsync(request) |> Async.AwaitTask
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                    Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
                    Expect.equal body "normal response" "No Accept should pass through"
                }

                testAsync "Mixed Accept header with provenance type serves provenance" {
                    let record = makeRecord "/orders/123"
                    let store = new TestProvenanceStore([ record ])
                    use server = createTestServer store
                    use client = server.CreateClient()

                    let request = new HttpRequestMessage(HttpMethod.Get, "/orders/123")

                    request.Headers.TryAddWithoutValidation(
                        "Accept",
                        sprintf "text/html, %s" ProvenanceMediaTypes.ProvenanceTurtle
                    )
                    |> ignore

                    let! (response: HttpResponseMessage) = client.SendAsync(request) |> Async.AwaitTask
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                    Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
                    Expect.stringContains body "prov:Activity" "Mixed Accept with provenance should serve provenance"
                } ] ]
