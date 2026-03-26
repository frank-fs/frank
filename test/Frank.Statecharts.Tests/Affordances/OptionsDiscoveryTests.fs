module Frank.Statecharts.Tests.Affordances.OptionsDiscoveryTests

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Primitives
open Expecto
open Frank.Builder
open Frank.Discovery
open Frank.Affordances
open Frank.Affordances.Tests.AffordanceTestHelpers
open Frank.Resources.Model
open Frank.Statecharts

/// Simple endpoint data source for tests (ResourceEndpointDataSource is internal).
type TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

/// Test extension to add DiscoveryMediaType metadata to a resource via the builder.
[<AutoOpen>]
module TestResourceBuilderExtensions =
    type ResourceBuilder with
        [<CustomOperation("discoveryMediaType")>]
        member _.DiscoveryMediaType(spec: ResourceSpec, mediaType: string, rel: string) : ResourceSpec =
            ResourceBuilder.AddMetadata(
                spec,
                fun b -> b.Metadata.Add({ MediaType = mediaType; Rel = rel }: DiscoveryMediaType)
            )

let simpleHandler: RequestDelegate =
    RequestDelegate(fun ctx -> ctx.Response.WriteAsync("OK"))

/// Run a test against a Host-based test server with configurable middleware, ensuring proper disposal.
let withTestHost (configureApp: IApplicationBuilder -> unit) (resources: Resource list) (f: HttpClient -> Task) =
    task {
        let allEndpoints =
            resources |> List.collect (fun r -> r.Endpoints |> Array.toList) |> List.toArray

        let dataSource = TestEndpointDataSource(allEndpoints)

        let host =
            Host
                .CreateDefaultBuilder([||])
                .ConfigureWebHost(fun webBuilder ->
                    webBuilder
                        .UseTestServer()
                        .ConfigureServices(fun services ->
                            services.AddRouting() |> ignore
                            services.AddSingleton<EndpointDataSource>(dataSource) |> ignore
                            services.AddSingleton<Dictionary<string, PreComputedAffordance>>(
                                Dictionary<string, PreComputedAffordance>(StringComparer.Ordinal)) |> ignore)
                        .Configure(fun app ->
                            app.UseRouting() |> ignore
                            configureApp app

                            app.UseEndpoints(fun endpoints -> endpoints.DataSources.Add(dataSource))
                            |> ignore)
                    |> ignore)
                .Build()

        host.Start()

        try
            let client = host.GetTestClient()

            try
                do! f client
            finally
                client.Dispose()
        finally
            (host :> System.IDisposable).Dispose()
    }
    :> Task

/// Runs a test against a server with the OPTIONS discovery middleware enabled.
let withDiscoveryServer resources f =
    withTestHost (fun app -> app.UseMiddleware<OptionsDiscoveryMiddleware>() |> ignore) resources f

/// Runs a test against a server WITHOUT the OPTIONS discovery middleware.
let withServerWithoutDiscovery resources f = withTestHost ignore resources f

// ===== US1: Agent Discovers Available Media Types via OPTIONS =====

[<Tests>]
let us1Tests =
    testList
        "US1 - OPTIONS Discovery"
        [ testTask "resource with GET and POST handlers returns Allow header with GET, OPTIONS, POST" {
              let itemsResource =
                  resource "/items" {
                      name "Items"
                      get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                      post (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("created")))
                  }

              do!
                  withDiscoveryServer [ itemsResource ] (fun client ->
                      task {
                          let request = new HttpRequestMessage(HttpMethod.Options, "/items")
                          let! (response: HttpResponseMessage) = client.SendAsync(request)

                          Expect.equal response.StatusCode HttpStatusCode.NoContent "OPTIONS should return 204"

                          let allowHeader = response.Content.Headers.Allow |> Set.ofSeq
                          Expect.contains allowHeader "GET" "Allow header should contain GET"
                          Expect.contains allowHeader "POST" "Allow header should contain POST"
                          Expect.contains allowHeader "OPTIONS" "Allow header should contain OPTIONS"

                          let! body = response.Content.ReadAsStringAsync()
                          Expect.equal body "" "Response body should be empty"
                      })
          }

          testTask "resource with GET only returns Allow header with GET, OPTIONS" {
              let healthResource =
                  resource "/health" {
                      name "Health"
                      get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("healthy")))
                  }

              do!
                  withDiscoveryServer [ healthResource ] (fun client ->
                      task {
                          let request = new HttpRequestMessage(HttpMethod.Options, "/health")
                          let! (response: HttpResponseMessage) = client.SendAsync(request)

                          Expect.equal response.StatusCode HttpStatusCode.NoContent "OPTIONS should return 204"

                          let allowHeader = response.Content.Headers.Allow |> Set.ofSeq
                          Expect.contains allowHeader "GET" "Allow header should contain GET"
                          Expect.contains allowHeader "OPTIONS" "Allow header should contain OPTIONS"
                          Expect.equal (Set.count allowHeader) 2 "Allow header should contain exactly GET and OPTIONS"

                          let! body = response.Content.ReadAsStringAsync()
                          Expect.equal body "" "Response body should be empty"
                      })
          }

          testTask "CORS preflight passes through without discovery response" {
              let itemsResource =
                  resource "/items" {
                      name "Items"
                      get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                  }

              do!
                  withDiscoveryServer [ itemsResource ] (fun client ->
                      task {
                          let request = new HttpRequestMessage(HttpMethod.Options, "/items")
                          request.Headers.Add("Access-Control-Request-Method", "GET")
                          let! (response: HttpResponseMessage) = client.SendAsync(request)

                          // The middleware should pass through for CORS preflights.
                          // Without CORS middleware registered, the response won't be 200 from our middleware.
                          // ASP.NET Core routing may return 405 with its own Allow header, but the key is
                          // our discovery middleware did NOT handle it (no 200 from us).
                          Expect.notEqual
                              response.StatusCode
                              HttpStatusCode.NoContent
                              "CORS preflight should not be handled by discovery middleware"
                      })
          }

          testTask "no discovery effect when middleware is not registered" {
              let itemsResource =
                  resource "/items" {
                      name "Items"
                      get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                  }

              do!
                  withServerWithoutDiscovery [ itemsResource ] (fun client ->
                      task {
                          let request = new HttpRequestMessage(HttpMethod.Options, "/items")
                          let! (response: HttpResponseMessage) = client.SendAsync(request)

                          // Without the discovery middleware, OPTIONS should not return 204.
                          // ASP.NET Core routing may return 405 for method not allowed.
                          Expect.notEqual
                              response.StatusCode
                              HttpStatusCode.NoContent
                              "Without discovery middleware, OPTIONS should not return 204"
                      })
          }

          testTask "resource with DiscoveryMediaType metadata returns Link headers" {
              let itemsResource =
                  resource "/items" {
                      name "Items"
                      get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                      post (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("created")))
                      discoveryMediaType "application/ld+json" "describedby"
                  }

              do!
                  withDiscoveryServer [ itemsResource ] (fun client ->
                      task {
                          let request = new HttpRequestMessage(HttpMethod.Options, "/items")
                          let! (response: HttpResponseMessage) = client.SendAsync(request)

                          Expect.equal response.StatusCode HttpStatusCode.NoContent "OPTIONS should return 204"

                          let allowHeader = response.Content.Headers.Allow |> Set.ofSeq
                          Expect.contains allowHeader "GET" "Allow header should contain GET"
                          Expect.contains allowHeader "POST" "Allow header should contain POST"
                          Expect.contains allowHeader "OPTIONS" "Allow header should contain OPTIONS"

                          // Verify Link header is emitted for the DiscoveryMediaType
                          let linkHeaders = response.Headers.GetValues("Link") |> Seq.toList
                          Expect.isNonEmpty linkHeaders "Link headers should be present"
                          let linkValue = linkHeaders |> String.concat ", "
                          Expect.stringContains linkValue "</items>" "Link header should contain the resource path"

                          Expect.stringContains
                              linkValue
                              "rel=\"describedby\""
                              "Link header should contain rel=describedby"

                          Expect.stringContains
                              linkValue
                              "type=\"application/ld+json\""
                              "Link header should contain media type"

                          let! body = response.Content.ReadAsStringAsync()
                          Expect.equal body "" "Response body should be empty (FR-013)"
                      })
          }

          testTask "unmatched route passes through" {
              let itemsResource =
                  resource "/items" {
                      name "Items"
                      get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                  }

              do!
                  withDiscoveryServer [ itemsResource ] (fun client ->
                      task {
                          let request = new HttpRequestMessage(HttpMethod.Options, "/nonexistent")
                          let! (response: HttpResponseMessage) = client.SendAsync(request)

                          // Unmatched route should not produce a 200 with Allow header
                          let hasAllow = response.Content.Headers.Allow.Count > 0
                          Expect.isFalse hasAllow "Unmatched route should not trigger discovery"
                      })
          }

          testTask "resource with explicit OPTIONS handler is not overridden" {
              let itemsResource =
                  resource "/items" {
                      name "Items"
                      get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))

                      options (
                          RequestDelegate(fun ctx ->
                              ctx.Response.StatusCode <- 200
                              ctx.Response.WriteAsync("explicit-options"))
                      )
                  }

              do!
                  withDiscoveryServer [ itemsResource ] (fun client ->
                      task {
                          let request = new HttpRequestMessage(HttpMethod.Options, "/items")
                          let! (response: HttpResponseMessage) = client.SendAsync(request)

                          let! body = response.Content.ReadAsStringAsync()
                          Expect.equal body "explicit-options" "Explicit OPTIONS handler should take precedence"
                      })
          }

          testTask "multiple resources at different routes each get correct Allow headers" {
              let itemsResource =
                  resource "/items" {
                      name "Items"
                      get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                      post (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("created")))
                      delete (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("deleted")))
                  }

              let healthResource =
                  resource "/health" {
                      name "Health"
                      get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("healthy")))
                  }

              do!
                  withDiscoveryServer [ itemsResource; healthResource ] (fun client ->
                      task {
                          // Check /items
                          let request1 = new HttpRequestMessage(HttpMethod.Options, "/items")
                          let! (response1: HttpResponseMessage) = client.SendAsync(request1)
                          Expect.equal response1.StatusCode HttpStatusCode.NoContent "OPTIONS /items should return 204"
                          let allow1 = response1.Content.Headers.Allow |> Set.ofSeq
                          Expect.contains allow1 "GET" "/items Allow should contain GET"
                          Expect.contains allow1 "POST" "/items Allow should contain POST"
                          Expect.contains allow1 "DELETE" "/items Allow should contain DELETE"
                          Expect.contains allow1 "OPTIONS" "/items Allow should contain OPTIONS"

                          // Check /health
                          let request2 = new HttpRequestMessage(HttpMethod.Options, "/health")
                          let! (response2: HttpResponseMessage) = client.SendAsync(request2)
                          Expect.equal response2.StatusCode HttpStatusCode.NoContent "OPTIONS /health should return 204"
                          let allow2 = response2.Content.Headers.Allow |> Set.ofSeq
                          Expect.contains allow2 "GET" "/health Allow should contain GET"
                          Expect.contains allow2 "OPTIONS" "/health Allow should contain OPTIONS"
                          Expect.equal (Set.count allow2) 2 "/health Allow should contain exactly GET and OPTIONS"
                      })
          } ]

// ===== Parameterized Route Matching =====

[<Tests>]
let parameterizedRouteTests =
    testList
        "Parameterized Route Matching"
        [ testTask "OPTIONS /games/abc123 matches parameterized route /games/{gameId}" {
              let gamesResource =
                  resource "/games/{gameId}" {
                      name "Games"
                      get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("game")))
                  }

              do!
                  withDiscoveryServer [ gamesResource ] (fun client ->
                      task {
                          let request = new HttpRequestMessage(HttpMethod.Options, "/games/abc123")
                          let! (response: HttpResponseMessage) = client.SendAsync(request)

                          Expect.equal response.StatusCode HttpStatusCode.NoContent "OPTIONS should return 204"

                          let allowHeader = response.Content.Headers.Allow |> Set.ofSeq
                          Expect.contains allowHeader "GET" "Allow header should contain GET"
                          Expect.contains allowHeader "OPTIONS" "Allow header should contain OPTIONS"
                      })
          }

          testTask "OPTIONS /games/abc123/moves matches nested parameterized route" {
              let movesResource =
                  resource "/games/{gameId}/moves" {
                      name "Moves"
                      get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("moves")))
                      post (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("move created")))
                  }

              do!
                  withDiscoveryServer [ movesResource ] (fun client ->
                      task {
                          let request = new HttpRequestMessage(HttpMethod.Options, "/games/abc123/moves")
                          let! (response: HttpResponseMessage) = client.SendAsync(request)

                          Expect.equal response.StatusCode HttpStatusCode.NoContent "OPTIONS should return 204"

                          let allowHeader = response.Content.Headers.Allow |> Set.ofSeq
                          Expect.contains allowHeader "GET" "Allow header should contain GET"
                          Expect.contains allowHeader "POST" "Allow header should contain POST"
                          Expect.contains allowHeader "OPTIONS" "Allow header should contain OPTIONS"
                      })
          }

          testTask "Literal routes still match after parameterized fix" {
              let healthResource =
                  resource "/health" {
                      name "Health"
                      get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("healthy")))
                  }

              do!
                  withDiscoveryServer [ healthResource ] (fun client ->
                      task {
                          let request = new HttpRequestMessage(HttpMethod.Options, "/health")
                          let! (response: HttpResponseMessage) = client.SendAsync(request)

                          Expect.equal response.StatusCode HttpStatusCode.NoContent "OPTIONS should return 204"

                          let allowHeader = response.Content.Headers.Allow |> Set.ofSeq
                          Expect.contains allowHeader "GET" "Allow header should contain GET"
                          Expect.contains allowHeader "OPTIONS" "Allow header should contain OPTIONS"
                          Expect.equal (Set.count allowHeader) 2 "Allow header should contain exactly GET and OPTIONS"
                      })
          } ]

// ===== State-Aware OPTIONS =====

/// Build a minimal StateMachineMetadata for test purposes.
/// The stateResolver function returns the current state key given an instance ID.
let testStateMachineMetadata (stateResolver: string -> string) : StateMachineMetadata =
    { Machine = obj ()
      StateHandlerMap = Map.empty
      ResolveInstanceId = fun ctx -> ctx.Request.RouteValues.["gameId"] :?> string
      TransitionObservers = []
      InitialStateKey = "XTurn"
      GuardNames = []
      StateMetadataMap = Map.empty
      GetCurrentStateKey = fun _sp _ctx instanceId -> Task.FromResult(stateResolver instanceId)
      EvaluateGuards = fun _ -> Allowed
      EvaluateEventGuards = fun _ -> Allowed
      ExecuteTransition = fun _sp _ctx _instanceId -> Task.FromResult(TransitionAttemptResult.NoEvent)
      Roles = []
      ResolveRoles = fun _ -> Set.empty }

/// Run a test against a WebApplication-based server with OPTIONS discovery middleware
/// and an affordance lookup, using MapGet/MapPost endpoints with StateMachineMetadata.
let withStatefulOptionsServer
    (lookup: Dictionary<string, PreComputedAffordance>)
    (stateMeta: StateMachineMetadata option)
    (configureEndpoints: IEndpointRouteBuilder -> unit)
    (f: HttpClient -> Task)
    =
    task {
        let builder = WebApplication.CreateBuilder([||])
        builder.WebHost.UseTestServer() |> ignore
        builder.Services.AddRouting() |> ignore

        builder.Services.AddSingleton<Dictionary<string, PreComputedAffordance>>(lookup)
        |> ignore

        let app = builder.Build()

        app.UseRouting() |> ignore

        (app :> IApplicationBuilder).UseMiddleware<OptionsDiscoveryMiddleware>()
        |> ignore

        app.UseEndpoints(fun endpoints ->
            configureEndpoints endpoints

            // If StateMachineMetadata provided, add it to endpoints after they're built
            match stateMeta with
            | Some _ -> ()
            | None -> ())
        |> ignore

        app.Start()
        let server = app.GetTestServer()
        let client = server.CreateClient()

        try
            do! f client
        finally
            client.Dispose()
            server.Dispose()
            (app :> IDisposable).Dispose()
    }
    :> Task

/// Configure endpoints with StateMachineMetadata on the games route.
let statefulEndpoints (meta: StateMachineMetadata) (endpoints: IEndpointRouteBuilder) =
    endpoints.MapGet("/games/{gameId}", RequestDelegate(fun ctx -> ctx.Response.WriteAsync("OK"))).WithMetadata(meta)
    |> ignore

    endpoints.MapPost("/games/{gameId}", RequestDelegate(fun ctx -> ctx.Response.WriteAsync("OK"))).WithMetadata(meta)
    |> ignore

/// Configure endpoints without StateMachineMetadata (plain resource).
let plainEndpoints (endpoints: IEndpointRouteBuilder) =
    endpoints.MapGet("/health", RequestDelegate(fun ctx -> ctx.Response.WriteAsync("healthy")))
    |> ignore

    endpoints.MapGet("/games/{gameId}", RequestDelegate(fun ctx -> ctx.Response.WriteAsync("OK")))
    |> ignore

    endpoints.MapPost("/games/{gameId}", RequestDelegate(fun ctx -> ctx.Response.WriteAsync("OK")))
    |> ignore

[<Tests>]
let stateAwareOptionsTests =
    testList
        "State-Aware OPTIONS"
        [ testTask "OPTIONS returns state-aware Allow when affordance data available" {
              let xTurnAffordance =
                  { AllowHeaderValue = StringValues("GET, OPTIONS, POST")
                    LinkHeaderValues =
                      StringValues(
                          [| "<https://example.com/alps/games>; rel=\"profile\""
                             "</games/{gameId}/move>; rel=\"makeMove\"" |]
                      )
                    HasTemplateLinks = true }

              let lookup = buildAffordanceLookup [ "/games/{gameId}|XTurn", xTurnAffordance ]

              let meta = testStateMachineMetadata (fun _ -> "XTurn")

              do!
                  withStatefulOptionsServer lookup (Some meta) (statefulEndpoints meta) (fun client ->
                      task {
                          let request = new HttpRequestMessage(HttpMethod.Options, "/games/abc123")
                          let! (response: HttpResponseMessage) = client.SendAsync(request)

                          Expect.equal response.StatusCode HttpStatusCode.NoContent "OPTIONS should return 204"

                          let allowHeader = response.Content.Headers.Allow |> Set.ofSeq
                          Expect.contains allowHeader "GET" "Allow header should contain GET"
                          Expect.contains allowHeader "POST" "Allow header should contain POST"
                          Expect.contains allowHeader "OPTIONS" "Allow header should contain OPTIONS"
                      })
          }

          testTask "OPTIONS returns restricted Allow for terminal state" {
              let wonAffordance =
                  { AllowHeaderValue = StringValues("GET, OPTIONS")
                    LinkHeaderValues = StringValues([| "<https://example.com/alps/games>; rel=\"profile\"" |])
                    HasTemplateLinks = false }

              let lookup = buildAffordanceLookup [ "/games/{gameId}|Won", wonAffordance ]

              let meta = testStateMachineMetadata (fun _ -> "Won")

              do!
                  withStatefulOptionsServer lookup (Some meta) (statefulEndpoints meta) (fun client ->
                      task {
                          let request = new HttpRequestMessage(HttpMethod.Options, "/games/abc123")
                          let! (response: HttpResponseMessage) = client.SendAsync(request)

                          Expect.equal response.StatusCode HttpStatusCode.NoContent "OPTIONS should return 204"

                          let allowHeader = response.Content.Headers.Allow |> Set.ofSeq
                          Expect.contains allowHeader "GET" "Allow header should contain GET"
                          Expect.contains allowHeader "OPTIONS" "Allow header should contain OPTIONS"

                          Expect.isFalse
                              (Set.contains "POST" allowHeader)
                              "Allow should NOT contain POST for Won state"
                      })
          }

          // Step 2 gap: verify Link headers in state-aware OPTIONS path
          testTask "OPTIONS returns state-aware Link headers with profile and transitions" {
              let xTurnAffordance =
                  { AllowHeaderValue = StringValues("GET, OPTIONS, POST")
                    LinkHeaderValues =
                      StringValues(
                          [| "<https://example.com/alps/games>; rel=\"profile\""
                             "</games/{gameId}/move>; rel=\"makeMove\"" |]
                      )
                    HasTemplateLinks = true }

              let lookup = buildAffordanceLookup [ "/games/{gameId}|XTurn", xTurnAffordance ]

              let meta = testStateMachineMetadata (fun _ -> "XTurn")

              do!
                  withStatefulOptionsServer lookup (Some meta) (statefulEndpoints meta) (fun client ->
                      task {
                          let request = new HttpRequestMessage(HttpMethod.Options, "/games/abc123")
                          let! (response: HttpResponseMessage) = client.SendAsync(request)

                          Expect.equal response.StatusCode HttpStatusCode.NoContent "OPTIONS should return 204"

                          // Verify Link headers are present in state-aware path
                          Expect.isTrue (response.Headers.Contains("Link")) "State-aware OPTIONS should include Link headers"
                          let linkHeaders = response.Headers.GetValues("Link") |> Seq.toList
                          let allLinks = linkHeaders |> String.concat " "
                          Expect.isTrue (allLinks.Contains("rel=\"profile\"")) "Link should contain profile"
                          Expect.isTrue (allLinks.Contains("rel=\"makeMove\"")) "Link should contain makeMove transition"
                      })
          }

          // Step 2 gap: terminal state Link headers (profile only, no transitions)
          testTask "OPTIONS returns Link headers with only profile for terminal state" {
              let wonAffordance =
                  { AllowHeaderValue = StringValues("GET, OPTIONS")
                    LinkHeaderValues = StringValues([| "<https://example.com/alps/games>; rel=\"profile\"" |])
                    HasTemplateLinks = false }

              let lookup = buildAffordanceLookup [ "/games/{gameId}|Won", wonAffordance ]

              let meta = testStateMachineMetadata (fun _ -> "Won")

              do!
                  withStatefulOptionsServer lookup (Some meta) (statefulEndpoints meta) (fun client ->
                      task {
                          let request = new HttpRequestMessage(HttpMethod.Options, "/games/abc123")
                          let! (response: HttpResponseMessage) = client.SendAsync(request)

                          Expect.equal response.StatusCode HttpStatusCode.NoContent "OPTIONS should return 204"

                          Expect.isTrue (response.Headers.Contains("Link")) "Terminal state OPTIONS should include Link headers"
                          let linkHeaders = response.Headers.GetValues("Link") |> Seq.toList
                          let allLinks = linkHeaders |> String.concat " "
                          Expect.isTrue (allLinks.Contains("rel=\"profile\"")) "Link should contain profile"
                          Expect.isFalse (allLinks.Contains("makeMove")) "Link should NOT contain transitions for terminal state"
                      })
          }

          testTask "OPTIONS falls back to route-level when no affordance data" {
              let lookup = buildAffordanceLookup []

              do!
                  withStatefulOptionsServer lookup None plainEndpoints (fun client ->
                      task {
                          let request = new HttpRequestMessage(HttpMethod.Options, "/games/abc123")
                          let! (response: HttpResponseMessage) = client.SendAsync(request)

                          Expect.equal response.StatusCode HttpStatusCode.NoContent "OPTIONS should return 204"

                          // Falls back to route-level: collects all HttpMethodMetadata methods
                          let allowHeader = response.Content.Headers.Allow |> Set.ofSeq
                          Expect.contains allowHeader "GET" "Allow header should contain GET"
                          Expect.contains allowHeader "POST" "Allow header should contain POST"
                          Expect.contains allowHeader "OPTIONS" "Allow header should contain OPTIONS"
                      })
          }

          // F-4: OPTIONS returns 404 when statechart state cannot be resolved
          testTask "OPTIONS returns 404 when statechart state cannot be resolved" {
              // Lookup with only "XTurn" state
              let xTurnAffordance =
                  { AllowHeaderValue = StringValues("GET, OPTIONS, POST")
                    LinkHeaderValues = StringValues([| "</games/{gameId}>; rel=\"profile\"" |])
                    HasTemplateLinks = true }

              let lookup = buildAffordanceLookup [ "/games/{gameId}|XTurn", xTurnAffordance ]

              // stateMeta resolves instanceId but GetCurrentStateKey returns a state not in lookup
              let meta = testStateMachineMetadata (fun _ -> "NonExistentState")

              do!
                  withStatefulOptionsServer lookup (Some meta) (statefulEndpoints meta) (fun client ->
                      task {
                          let request = new HttpRequestMessage(HttpMethod.Options, "/games/unknown-id")
                          let! (response: HttpResponseMessage) = client.SendAsync(request)

                          Expect.equal response.StatusCode HttpStatusCode.NotFound "OPTIONS should return 404 for unresolvable state"
                      })
          } ]
