module Frank.Discovery.Tests.JsonHomeMiddlewareTests

open System.Net
open System.Net.Http
open System.Net.Http.Headers
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Patterns
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder
open Frank.Discovery
open Frank.Discovery.Tests.OptionsDiscoveryTests

let private buildTestServer () =
    let itemsRes = resource "/items" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items list"))) }
    let dataSource = TestEndpointDataSource(itemsRes.Endpoints)

    let host =
        Host.CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        services.AddSingleton<EndpointDataSource>(dataSource) |> ignore)
                    .Configure(fun app ->
                        app.UseMiddleware<JsonHomeMiddleware>() |> ignore
                        app.UseRouting() |> ignore
                        app.UseEndpoints(fun endpoints ->
                            endpoints.DataSources.Add(dataSource)
                            endpoints.MapGet("/", fun ctx ->
                                ctx.Response.WriteAsync("Hello World")) |> ignore) |> ignore)
                |> ignore)
            .Build()

    host.Start()
    host.GetTestClient()

[<Tests>]
let tests =
    testList "JsonHomeMiddleware" [
        testTask "GET / with Accept: application/json-home returns home document" {
            let client = buildTestServer ()
            let req = new HttpRequestMessage(HttpMethod.Get, "/")
            req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
            let! (resp: HttpResponseMessage) = client.SendAsync(req)
            Expect.equal resp.StatusCode HttpStatusCode.OK "should be 200"
            Expect.equal (resp.Content.Headers.ContentType.MediaType) "application/json-home" "content type"
            Expect.isTrue (resp.Headers.Contains("Vary")) "should have Vary header"
            Expect.isTrue (resp.Headers.Contains("Cache-Control")) "should have Cache-Control"
            let! (body: string) = resp.Content.ReadAsStringAsync()
            Expect.isTrue (body.Contains("\"resources\"")) "should have resources"
            Expect.isTrue (body.Contains("/items")) "should contain items resource"
        }

        testTask "GET / with Accept: text/html passes through" {
            let client = buildTestServer ()
            let req = new HttpRequestMessage(HttpMethod.Get, "/")
            req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("text/html"))
            let! (resp: HttpResponseMessage) = client.SendAsync(req)
            let! (body: string) = resp.Content.ReadAsStringAsync()
            Expect.equal body "Hello World" "should pass through to user handler"
        }

        testTask "GET / with Accept: */* passes through" {
            let client = buildTestServer ()
            let req = new HttpRequestMessage(HttpMethod.Get, "/")
            req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("*/*"))
            let! (resp: HttpResponseMessage) = client.SendAsync(req)
            let! (body: string) = resp.Content.ReadAsStringAsync()
            Expect.equal body "Hello World" "should pass through"
        }

        testTask "GET / with Accept: application/json passes through" {
            let client = buildTestServer ()
            let req = new HttpRequestMessage(HttpMethod.Get, "/")
            req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json"))
            let! (resp: HttpResponseMessage) = client.SendAsync(req)
            let! (body: string) = resp.Content.ReadAsStringAsync()
            Expect.equal body "Hello World" "should pass through"
        }

        testTask "POST / passes through" {
            let client = buildTestServer ()
            let req = new HttpRequestMessage(HttpMethod.Post, "/")
            req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
            let! (resp: HttpResponseMessage) = client.SendAsync(req)
            Expect.notEqual resp.StatusCode HttpStatusCode.OK "should not serve home doc on POST"
        }

        testTask "GET /other passes through" {
            let client = buildTestServer ()
            let req = new HttpRequestMessage(HttpMethod.Get, "/other")
            req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
            let! (resp: HttpResponseMessage) = client.SendAsync(req)
            Expect.equal resp.StatusCode HttpStatusCode.NotFound "non-root path passes through"
        }

        testTask "Accept with parameters still matches" {
            let client = buildTestServer ()
            let req = new HttpRequestMessage(HttpMethod.Get, "/")
            req.Headers.TryAddWithoutValidation("Accept", "application/json-home; charset=utf-8") |> ignore
            let! (resp: HttpResponseMessage) = client.SendAsync(req)
            Expect.equal resp.StatusCode HttpStatusCode.OK "should match ignoring parameters"
        }

        testTask "Accept with multiple types and quality values selects json-home" {
            let client = buildTestServer ()
            let req = new HttpRequestMessage(HttpMethod.Get, "/")
            req.Headers.TryAddWithoutValidation("Accept", "application/json-home, text/html;q=0.9") |> ignore
            let! (resp: HttpResponseMessage) = client.SendAsync(req)
            Expect.equal resp.StatusCode HttpStatusCode.OK "should match json-home in multi-value Accept"
            Expect.equal (resp.Content.Headers.ContentType.MediaType) "application/json-home" "content type"
        }

        testTask "HEAD / with Accept: application/json-home returns headers with no body" {
            let client = buildTestServer ()
            let req = new HttpRequestMessage(HttpMethod.Head, "/")
            req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
            let! (resp: HttpResponseMessage) = client.SendAsync(req)
            Expect.equal resp.StatusCode HttpStatusCode.OK "should be 200"
            Expect.equal (resp.Content.Headers.ContentType.MediaType) "application/json-home" "content type"
            Expect.isTrue (resp.Headers.Contains("Vary")) "should have Vary header"
            Expect.isTrue (resp.Headers.Contains("Cache-Control")) "should have Cache-Control"
            let! (body: string) = resp.Content.ReadAsStringAsync()
            Expect.equal body "" "HEAD should have empty body"
        }
    ]

/// Build a test server from a WebHostSpec, replicating the CE Run pipeline with TestServer.
let buildCeTestServer (spec: WebHostSpec) =
    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    spec.Services(builder.Services) |> ignore
    let app = builder.Build()
    let dataSource = TestEndpointDataSource(spec.Endpoints)
    (app :> IApplicationBuilder)
    |> spec.BeforeRoutingMiddleware
    |> fun app -> app.UseRouting()
    |> spec.Middleware
    |> ignore
    (app :> IEndpointRouteBuilder).DataSources.Add(dataSource)
    app.Start()
    app.GetTestClient()

[<Tests>]
let ceTests =
    testList "useJsonHome CE operation" [
        testTask "useJsonHome with webHost CE serves home document for registered resources" {
            let testResource =
                resource "/items" {
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                }

            // Build the spec the same way the CE does, but without calling Run
            let ceBuilder = WebHostBuilder([||])
            let spec =
                ceBuilder.Yield()
                |> fun s -> ceBuilder.UseJsonHome(s)
                |> fun s -> ceBuilder.Resource(s, testResource)

            let client = buildCeTestServer spec

            let req = new HttpRequestMessage(HttpMethod.Get, "/")
            req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
            let! (resp: HttpResponseMessage) = client.SendAsync(req)
            Expect.equal resp.StatusCode HttpStatusCode.OK "should serve home document"
            let! (body: string) = resp.Content.ReadAsStringAsync()
            Expect.isTrue (body.Contains("/items")) "should contain items resource"
            Expect.isTrue (body.Contains("urn:frank:")) "should have URN relation"
        }

        testTask "useJsonHome passes through non-json-home Accept at root" {
            let testResource =
                resource "/items" {
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                }

            let ceBuilder = WebHostBuilder([||])
            let spec =
                ceBuilder.Yield()
                |> fun s -> ceBuilder.UseJsonHome(s)
                |> fun s -> ceBuilder.Resource(s, testResource)

            let client = buildCeTestServer spec

            let req = new HttpRequestMessage(HttpMethod.Get, "/")
            req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("text/html"))
            let! (resp: HttpResponseMessage) = client.SendAsync(req)
            // Should pass through — Accept is text/html, not application/json-home
            let contentType =
                if isNull resp.Content.Headers.ContentType then "" else resp.Content.Headers.ContentType.MediaType
            Expect.notEqual contentType "application/json-home" "should not be json-home"
        }
    ]

[<Tests>]
let useDiscoveryTests =
    testList "useDiscovery CE operation" [
        testTask "useDiscovery serves JSON Home at root" {
            let testResource =
                resource "/items" {
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                }

            let ceBuilder = WebHostBuilder([||])
            let spec =
                ceBuilder.Yield()
                |> fun s -> ceBuilder.UseDiscovery(s)
                |> fun s -> ceBuilder.Resource(s, testResource)

            let client = buildCeTestServer spec

            let req = new HttpRequestMessage(HttpMethod.Get, "/")
            req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
            let! (resp: HttpResponseMessage) = client.SendAsync(req)
            Expect.equal resp.StatusCode HttpStatusCode.OK "should serve home document"
            Expect.isTrue (resp.Headers.Contains("Vary")) "should have Vary header"
            let! (body: string) = resp.Content.ReadAsStringAsync()
            Expect.isTrue (body.Contains("/items")) "should contain items resource"
        }

        testTask "useDiscovery returns Allow header on OPTIONS" {
            let testResource =
                resource "/items" {
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                    discoveryMediaType "application/json" "self"
                }

            let ceBuilder = WebHostBuilder([||])
            let spec =
                ceBuilder.Yield()
                |> fun s -> ceBuilder.UseDiscovery(s)
                |> fun s -> ceBuilder.Resource(s, testResource)

            let client = buildCeTestServer spec

            let! (resp: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/items"))
            Expect.equal resp.StatusCode HttpStatusCode.OK "OPTIONS should return 200"
            Expect.isGreaterThan (resp.Content.Headers.Allow.Count) 0 "should have Allow header"
        }

        testTask "useDiscovery returns Link headers on GET" {
            let testResource =
                resource "/items" {
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                    discoveryMediaType "application/json" "self"
                }

            let ceBuilder = WebHostBuilder([||])
            let spec =
                ceBuilder.Yield()
                |> fun s -> ceBuilder.UseDiscovery(s)
                |> fun s -> ceBuilder.Resource(s, testResource)

            let client = buildCeTestServer spec

            let! (resp: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/items"))
            Expect.equal resp.StatusCode HttpStatusCode.OK "GET should return 200"
            Expect.isTrue (resp.Headers.Contains("Link")) "should have Link header"
        }
    ]

[<Tests>]
let useDiscoveryHeadersTests =
    testList "useDiscoveryHeaders CE operation" [
        testTask "useDiscoveryHeaders does not serve JSON Home" {
            let testResource =
                resource "/items" {
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                }

            let ceBuilder = WebHostBuilder([||])
            let spec =
                ceBuilder.Yield()
                |> fun s -> ceBuilder.UseDiscoveryHeaders(s)
                |> fun s -> ceBuilder.Resource(s, testResource)

            let client = buildCeTestServer spec

            let req = new HttpRequestMessage(HttpMethod.Get, "/")
            req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
            let! (resp: HttpResponseMessage) = client.SendAsync(req)
            // Should NOT serve JSON Home — useDiscoveryHeaders only provides OPTIONS + Link
            Expect.notEqual resp.StatusCode HttpStatusCode.OK "should not serve home document"
        }
    ]

[<Tests>]
let metadataTests =
    testList "JsonHomeMiddleware with metadata" [
        testTask "resolves JsonHomeMetadata from DI for ALPS enrichment" {
            let gamesRes = resource "/games/{gameId}" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("game"))) }
            let dataSource = TestEndpointDataSource(gamesRes.Endpoints)

            let metadata: JsonHomeMetadata =
                { Title = Some "Game API"
                  DocsUrl = Some "/scalar/v1"
                  AlpsBaseUri = Some "http://example.com/alps/games"
                  AlpsDescriptors = Some (Map.ofList [ "games", Map.ofList [ "gameId", "http://example.com/alps/games#gameId" ] ]) }

            let host =
                Host.CreateDefaultBuilder([||])
                    .ConfigureWebHost(fun webBuilder ->
                        webBuilder
                            .UseTestServer()
                            .ConfigureServices(fun services ->
                                services.AddRouting() |> ignore
                                services.AddSingleton<EndpointDataSource>(dataSource) |> ignore
                                services.AddSingleton<JsonHomeMetadata>(metadata) |> ignore)
                            .Configure(fun app ->
                                app.UseMiddleware<JsonHomeMiddleware>() |> ignore
                                app.UseRouting() |> ignore
                                app.UseEndpoints(fun endpoints ->
                                    endpoints.DataSources.Add(dataSource)) |> ignore)
                        |> ignore)
                    .Build()
            host.Start()
            let client = host.GetTestClient()

            let req = new HttpRequestMessage(HttpMethod.Get, "/")
            req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
            let! (resp: HttpResponseMessage) = client.SendAsync(req)
            let! (body: string) = resp.Content.ReadAsStringAsync()
            Expect.isTrue (body.Contains("Game API")) "should use metadata title"
            Expect.isTrue (body.Contains("http://example.com/alps/games#games")) "should have ALPS relation"
            Expect.isTrue (body.Contains("http://example.com/alps/games#gameId")) "should have ALPS hrefVar"
            Expect.isTrue (body.Contains("/scalar/v1")) "should have docs URL"
        }
    ]
