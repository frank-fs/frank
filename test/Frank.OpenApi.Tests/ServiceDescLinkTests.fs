module Frank.OpenApi.Tests.ServiceDescLinkTests

open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder
open Frank.OpenApi
open Frank.OpenApi.Tests.OpenApiDocumentTests

/// Creates a test server by calling the real WebHostBuilder.UseOpenApi member and
/// applying its Services/Middleware onto a TestServer-based host, so the behavior
/// under test is the actual production code path -- not a hand-copied duplicate of
/// its wiring. (Frank.WebHostBuilder.Run calls the blocking .Build().Run(), which
/// cannot be wired to a TestServer, hence not going through the `webHost { }` CE's
/// Run member directly.)
let createRealUseOpenApiTestServer (resources: Resource list) =
    let allEndpoints = resources |> List.collect (fun r -> r.Endpoints |> Array.toList) |> List.toArray
    let spec = (webHost [||]).UseOpenApi(WebHostSpec.Empty)
    let builder =
        Host.CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        spec.Services services |> ignore)
                    .Configure(fun app ->
                        spec.BeforeRoutingMiddleware app |> ignore
                        app.UseRouting() |> ignore
                        spec.Middleware app |> ignore
                        app.UseEndpoints(fun endpoints ->
                            endpoints.DataSources.Add(TestEndpointDataSource(allEndpoints)))
                        |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

/// Same as above but exercises the `configure: OpenApiOptions -> unit` overload.
let createRealUseOpenApiWithConfigureTestServer (resources: Resource list) =
    let allEndpoints = resources |> List.collect (fun r -> r.Endpoints |> Array.toList) |> List.toArray
    let spec = (webHost [||]).UseOpenApi(WebHostSpec.Empty, fun _options -> ())
    let builder =
        Host.CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        spec.Services services |> ignore)
                    .Configure(fun app ->
                        spec.BeforeRoutingMiddleware app |> ignore
                        app.UseRouting() |> ignore
                        spec.Middleware app |> ignore
                        app.UseEndpoints(fun endpoints ->
                            endpoints.DataSources.Add(TestEndpointDataSource(allEndpoints)))
                        |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

let private expectedLinkValue =
    "<" + openApiRoutePattern + ">; rel=\"service-desc\"; type=\"application/json\""

let private expectLinkHeader (response: HttpResponseMessage) (context: string) =
    Expect.isTrue (response.Headers.Contains("Link")) (context + ": response should carry a Link header")
    let values = response.Headers.GetValues("Link") |> List.ofSeq
    Expect.contains values expectedLinkValue (context + ": Link header should advertise the OpenAPI document as service-desc")

[<Tests>]
let tests =
    testList "Frank.OpenApi service-desc Link header" [
        testTask "response from an arbitrary resource carries the service-desc Link header" {
            let products =
                resource "/products" {
                    name "Products"
                    get simpleHandler
                }
            let client = createRealUseOpenApiTestServer [ products ]
            let! (response: HttpResponseMessage) = client.GetAsync("/products")
            expectLinkHeader response "GET /products"
        }

        testTask "response from the OpenAPI document's own route also carries the header" {
            let products =
                resource "/products" {
                    name "Products"
                    get simpleHandler
                }
            let client = createRealUseOpenApiTestServer [ products ]
            let! (response: HttpResponseMessage) = client.GetAsync(openApiRoutePattern)
            expectLinkHeader response (sprintf "GET %s" openApiRoutePattern)
        }

        testTask "the header is present with the configure-taking UseOpenApi overload too" {
            let products =
                resource "/products" {
                    name "Products"
                    get simpleHandler
                }
            let client = createRealUseOpenApiWithConfigureTestServer [ products ]
            let! (response: HttpResponseMessage) = client.GetAsync("/products")
            expectLinkHeader response "GET /products (configure overload)"
        }
    ]
