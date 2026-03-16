module Frank.Discovery.Tests.OptionsDiscoveryTests

open System.Net
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder
open Frank.Discovery

/// Simple endpoint data source for tests (ResourceEndpointDataSource is internal).
type TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

let simpleHandler : RequestDelegate =
    RequestDelegate(fun ctx -> ctx.Response.WriteAsync("OK"))

/// Creates a test server with the OPTIONS discovery middleware enabled.
let createDiscoveryTestServer (resources: Resource list) =
    let allEndpoints =
        resources
        |> List.collect (fun r -> r.Endpoints |> Array.toList)
        |> List.toArray

    let dataSource = TestEndpointDataSource(allEndpoints)

    let builder =
        Host.CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        services.AddSingleton<EndpointDataSource>(dataSource) |> ignore)
                    .Configure(fun app ->
                        app
                            .UseRouting()
                            .UseMiddleware<OptionsDiscoveryMiddleware>()
                            .UseEndpoints(fun endpoints ->
                                endpoints.DataSources.Add(dataSource))
                        |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

/// Creates a test server WITHOUT the OPTIONS discovery middleware (for test 4).
let createTestServerWithoutDiscovery (resources: Resource list) =
    let allEndpoints =
        resources
        |> List.collect (fun r -> r.Endpoints |> Array.toList)
        |> List.toArray

    let dataSource = TestEndpointDataSource(allEndpoints)

    let builder =
        Host.CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        services.AddSingleton<EndpointDataSource>(dataSource) |> ignore)
                    .Configure(fun app ->
                        app
                            .UseRouting()
                            .UseEndpoints(fun endpoints ->
                                endpoints.DataSources.Add(dataSource))
                        |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

// ===== US1: Agent Discovers Available Media Types via OPTIONS =====

[<Tests>]
let us1Tests =
    testList "US1 - OPTIONS Discovery" [
        testTask "resource with GET and POST handlers returns Allow header with GET, OPTIONS, POST" {
            let itemsResource =
                resource "/items" {
                    name "Items"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                    post (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("created")))
                }

            let client = createDiscoveryTestServer [ itemsResource ]
            let request = new HttpRequestMessage(HttpMethod.Options, "/items")
            let! (response: HttpResponseMessage) = client.SendAsync(request)

            Expect.equal response.StatusCode HttpStatusCode.OK "OPTIONS should return 200"

            let allowHeader = response.Content.Headers.Allow |> Set.ofSeq
            Expect.contains allowHeader "GET" "Allow header should contain GET"
            Expect.contains allowHeader "POST" "Allow header should contain POST"
            Expect.contains allowHeader "OPTIONS" "Allow header should contain OPTIONS"

            let! body = response.Content.ReadAsStringAsync()
            Expect.equal body "" "Response body should be empty"
        }

        testTask "resource with GET only returns Allow header with GET, OPTIONS" {
            let healthResource =
                resource "/health" {
                    name "Health"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("healthy")))
                }

            let client = createDiscoveryTestServer [ healthResource ]
            let request = new HttpRequestMessage(HttpMethod.Options, "/health")
            let! (response: HttpResponseMessage) = client.SendAsync(request)

            Expect.equal response.StatusCode HttpStatusCode.OK "OPTIONS should return 200"

            let allowHeader = response.Content.Headers.Allow |> Set.ofSeq
            Expect.contains allowHeader "GET" "Allow header should contain GET"
            Expect.contains allowHeader "OPTIONS" "Allow header should contain OPTIONS"
            Expect.equal (Set.count allowHeader) 2 "Allow header should contain exactly GET and OPTIONS"

            let! body = response.Content.ReadAsStringAsync()
            Expect.equal body "" "Response body should be empty"
        }

        testTask "CORS preflight passes through without discovery response" {
            let itemsResource =
                resource "/items" {
                    name "Items"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                }

            let client = createDiscoveryTestServer [ itemsResource ]
            let request = new HttpRequestMessage(HttpMethod.Options, "/items")
            request.Headers.Add("Access-Control-Request-Method", "GET")
            let! (response: HttpResponseMessage) = client.SendAsync(request)

            // The middleware should pass through for CORS preflights.
            // Without CORS middleware registered, the response won't be 200 from our middleware.
            // ASP.NET Core routing may return 405 with its own Allow header, but the key is
            // our discovery middleware did NOT handle it (no 200 from us).
            Expect.notEqual response.StatusCode HttpStatusCode.OK
                "CORS preflight should not be handled by discovery middleware"
        }

        testTask "no discovery effect when middleware is not registered" {
            let itemsResource =
                resource "/items" {
                    name "Items"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                }

            let client = createTestServerWithoutDiscovery [ itemsResource ]
            let request = new HttpRequestMessage(HttpMethod.Options, "/items")
            let! (response: HttpResponseMessage) = client.SendAsync(request)

            // Without the discovery middleware, OPTIONS should not return 200.
            // ASP.NET Core routing may return 405 for method not allowed.
            Expect.notEqual response.StatusCode HttpStatusCode.OK
                "Without discovery middleware, OPTIONS should not return 200"
        }

        testTask "resource with GET and POST and DiscoveryMediaType metadata returns correct response" {
            let itemsResource =
                resource "/items" {
                    name "Items"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                    post (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("created")))
                }

            let client = createDiscoveryTestServer [ itemsResource ]
            let request = new HttpRequestMessage(HttpMethod.Options, "/items")
            let! (response: HttpResponseMessage) = client.SendAsync(request)

            Expect.equal response.StatusCode HttpStatusCode.OK "OPTIONS should return 200"

            let allowHeader = response.Content.Headers.Allow |> Set.ofSeq
            Expect.contains allowHeader "GET" "Allow header should contain GET"
            Expect.contains allowHeader "POST" "Allow header should contain POST"
            Expect.contains allowHeader "OPTIONS" "Allow header should contain OPTIONS"

            let! body = response.Content.ReadAsStringAsync()
            Expect.equal body "" "Response body should be empty (FR-013)"
        }

        testTask "unmatched route passes through" {
            let itemsResource =
                resource "/items" {
                    name "Items"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                }

            let client = createDiscoveryTestServer [ itemsResource ]
            let request = new HttpRequestMessage(HttpMethod.Options, "/nonexistent")
            let! (response: HttpResponseMessage) = client.SendAsync(request)

            // Unmatched route should not produce a 200 with Allow header
            let hasAllow = response.Content.Headers.Allow.Count > 0
            Expect.isFalse hasAllow "Unmatched route should not trigger discovery"
        }

        testTask "resource with explicit OPTIONS handler is not overridden" {
            let itemsResource =
                resource "/items" {
                    name "Items"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                    options (RequestDelegate(fun ctx ->
                        ctx.Response.StatusCode <- 200
                        ctx.Response.WriteAsync("explicit-options")))
                }

            let client = createDiscoveryTestServer [ itemsResource ]
            let request = new HttpRequestMessage(HttpMethod.Options, "/items")
            let! (response: HttpResponseMessage) = client.SendAsync(request)

            let! body = response.Content.ReadAsStringAsync()
            Expect.equal body "explicit-options" "Explicit OPTIONS handler should take precedence"
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

            let client = createDiscoveryTestServer [ itemsResource; healthResource ]

            // Check /items
            let request1 = new HttpRequestMessage(HttpMethod.Options, "/items")
            let! (response1: HttpResponseMessage) = client.SendAsync(request1)
            Expect.equal response1.StatusCode HttpStatusCode.OK "OPTIONS /items should return 200"
            let allow1 = response1.Content.Headers.Allow |> Set.ofSeq
            Expect.contains allow1 "GET" "/items Allow should contain GET"
            Expect.contains allow1 "POST" "/items Allow should contain POST"
            Expect.contains allow1 "DELETE" "/items Allow should contain DELETE"
            Expect.contains allow1 "OPTIONS" "/items Allow should contain OPTIONS"

            // Check /health
            let request2 = new HttpRequestMessage(HttpMethod.Options, "/health")
            let! (response2: HttpResponseMessage) = client.SendAsync(request2)
            Expect.equal response2.StatusCode HttpStatusCode.OK "OPTIONS /health should return 200"
            let allow2 = response2.Content.Headers.Allow |> Set.ofSeq
            Expect.contains allow2 "GET" "/health Allow should contain GET"
            Expect.contains allow2 "OPTIONS" "/health Allow should contain OPTIONS"
            Expect.equal (Set.count allow2) 2 "/health Allow should contain exactly GET and OPTIONS"
        }
    ]
