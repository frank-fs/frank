module Frank.Discovery.Tests.JsonHomeMiddlewareTests

open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Threading.Tasks
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

/// Simple endpoint data source for tests (ResourceEndpointDataSource is internal).
type TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

let private withServer (f: HttpClient -> Task) =
    task {
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

[<Tests>]
let tests =
    testList "JsonHomeMiddleware" [
        testTask "GET / with Accept: application/json-home returns home document" {
            do! withServer (fun client -> task {
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
            })
        }

        testTask "GET / with Accept: text/html passes through" {
            do! withServer (fun client -> task {
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("text/html"))
                let! (resp: HttpResponseMessage) = client.SendAsync(req)
                let! (body: string) = resp.Content.ReadAsStringAsync()
                Expect.equal body "Hello World" "should pass through to user handler"
            })
        }

        testTask "GET / with Accept: */* passes through" {
            do! withServer (fun client -> task {
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("*/*"))
                let! (resp: HttpResponseMessage) = client.SendAsync(req)
                let! (body: string) = resp.Content.ReadAsStringAsync()
                Expect.equal body "Hello World" "should pass through"
            })
        }

        testTask "GET / with Accept: application/json passes through" {
            do! withServer (fun client -> task {
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json"))
                let! (resp: HttpResponseMessage) = client.SendAsync(req)
                let! (body: string) = resp.Content.ReadAsStringAsync()
                Expect.equal body "Hello World" "should pass through"
            })
        }

        testTask "POST / passes through" {
            do! withServer (fun client -> task {
                let req = new HttpRequestMessage(HttpMethod.Post, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
                let! (resp: HttpResponseMessage) = client.SendAsync(req)
                Expect.notEqual resp.StatusCode HttpStatusCode.OK "should not serve home doc on POST"
            })
        }

        testTask "GET /other passes through" {
            do! withServer (fun client -> task {
                let req = new HttpRequestMessage(HttpMethod.Get, "/other")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
                let! (resp: HttpResponseMessage) = client.SendAsync(req)
                Expect.equal resp.StatusCode HttpStatusCode.NotFound "non-root path passes through"
            })
        }

        testTask "Accept with parameters still matches" {
            do! withServer (fun client -> task {
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.TryAddWithoutValidation("Accept", "application/json-home; charset=utf-8") |> ignore
                let! (resp: HttpResponseMessage) = client.SendAsync(req)
                Expect.equal resp.StatusCode HttpStatusCode.OK "should match ignoring parameters"
            })
        }

        testTask "Accept with multiple types and quality values selects json-home" {
            do! withServer (fun client -> task {
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.TryAddWithoutValidation("Accept", "application/json-home, text/html;q=0.9") |> ignore
                let! (resp: HttpResponseMessage) = client.SendAsync(req)
                Expect.equal resp.StatusCode HttpStatusCode.OK "should match json-home in multi-value Accept"
                Expect.equal (resp.Content.Headers.ContentType.MediaType) "application/json-home" "content type"
            })
        }

        testTask "HEAD / with Accept: application/json-home returns headers with no body" {
            do! withServer (fun client -> task {
                let req = new HttpRequestMessage(HttpMethod.Head, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
                let! (resp: HttpResponseMessage) = client.SendAsync(req)
                Expect.equal resp.StatusCode HttpStatusCode.OK "should be 200"
                Expect.equal (resp.Content.Headers.ContentType.MediaType) "application/json-home" "content type"
                Expect.isTrue (resp.Headers.Contains("Vary")) "should have Vary header"
                Expect.isTrue (resp.Headers.Contains("Cache-Control")) "should have Cache-Control"
                let! (body: string) = resp.Content.ReadAsStringAsync()
                Expect.equal body "" "HEAD should have empty body"
            })
        }
    ]

/// Build a test server from a WebHostSpec, replicating the CE Run pipeline with TestServer.
let withCeServer (spec: WebHostSpec) (f: HttpClient -> Task) =
    task {
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

        let client = app.GetTestClient()

        try
            do! f client
        finally
            client.Dispose()
            (app :> System.IDisposable).Dispose()
    }
    :> Task

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

            do! withCeServer spec (fun client -> task {
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
                let! (resp: HttpResponseMessage) = client.SendAsync(req)
                Expect.equal resp.StatusCode HttpStatusCode.OK "should serve home document"
                let! (body: string) = resp.Content.ReadAsStringAsync()
                Expect.isTrue (body.Contains("/items")) "should contain items resource"
                Expect.isTrue (body.Contains("urn:frank:")) "should have URN relation"
            })
        }

        // #198: useJsonHome registers JsonHomeMetadata in DI
        testTask "useJsonHome registers default JsonHomeMetadata in DI" {
            let testResource =
                resource "/items" {
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                }

            let ceBuilder = WebHostBuilder([||])
            let spec =
                ceBuilder.Yield()
                |> fun s -> ceBuilder.UseJsonHome(s)
                |> fun s -> ceBuilder.Resource(s, testResource)

            do! withCeServer spec (fun client -> task {
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
                let! (resp: HttpResponseMessage) = client.SendAsync(req)
                Expect.equal resp.StatusCode HttpStatusCode.OK "should serve home document with auto-registered metadata"
                let! (body: string) = resp.Content.ReadAsStringAsync()
                Expect.isTrue (body.Contains("/items")) "should contain items resource"
            })
        }

        // #198: TryAddSingleton does NOT override pre-registered metadata
        testTask "useJsonHome does not override explicitly registered JsonHomeMetadata" {
            let testResource =
                resource "/games/{gameId}" {
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("game")))
                }

            let customMetadata: JsonHomeMetadata =
                { Title = Some "Custom Title"
                  DocsUrl = None
                  AlpsBaseUri = Some "http://example.com/alps/games"
                  AlpsDescriptors = Some (Map.ofList [ "games", Map.ofList [ "gameId", "http://example.com/alps/games#gameId" ] ]) }

            let ceBuilder = WebHostBuilder([||])
            let spec =
                ceBuilder.Yield()
                |> fun s -> ceBuilder.UseJsonHome(s)
                |> fun s -> ceBuilder.Resource(s, testResource)

            // Wrap Services to pre-register custom metadata BEFORE useJsonHome's TryAddSingleton
            let innerServices = spec.Services
            let specWithCustom =
                { spec with
                    Services = fun services ->
                        services.AddSingleton<JsonHomeMetadata>(customMetadata) |> ignore
                        innerServices services }

            do! withCeServer specWithCustom (fun client -> task {
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
                let! (resp: HttpResponseMessage) = client.SendAsync(req)
                Expect.equal resp.StatusCode HttpStatusCode.OK "should serve home document"
                let! (body: string) = resp.Content.ReadAsStringAsync()
                Expect.isTrue (body.Contains("Custom Title")) "should use pre-registered metadata title, not Empty"
                Expect.isTrue (body.Contains("http://example.com/alps/games#games~gameId")) "should use ALPS relation from pre-registered metadata"
            })
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

            do! withCeServer spec (fun client -> task {
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("text/html"))
                let! (resp: HttpResponseMessage) = client.SendAsync(req)
                // Should pass through — Accept is text/html, not application/json-home
                let contentType =
                    if isNull resp.Content.Headers.ContentType then "" else resp.Content.Headers.ContentType.MediaType
                Expect.notEqual contentType "application/json-home" "should not be json-home"
            })
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

            try
                let client = host.GetTestClient()

                try
                    let req = new HttpRequestMessage(HttpMethod.Get, "/")
                    req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
                    let! (resp: HttpResponseMessage) = client.SendAsync(req)
                    let! (body: string) = resp.Content.ReadAsStringAsync()
                    Expect.isTrue (body.Contains("Game API")) "should use metadata title"
                    Expect.isTrue (body.Contains("http://example.com/alps/games#games~gameId")) "should have ALPS relation"
                    Expect.isTrue (body.Contains("http://example.com/alps/games#gameId")) "should have ALPS hrefVar"
                    Expect.isTrue (body.Contains("/scalar/v1")) "should have docs URL"
                finally
                    client.Dispose()
            finally
                (host :> System.IDisposable).Dispose()
        }
    ]
