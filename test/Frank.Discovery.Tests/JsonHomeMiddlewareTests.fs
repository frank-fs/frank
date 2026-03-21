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

/// Simple endpoint data source for middleware tests.
type MiddlewareTestDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

let private buildTestServer () =
    let itemsRes = resource "/items" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items list"))) }
    let dataSource = MiddlewareTestDataSource(itemsRes.Endpoints)

    let builder =
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

    builder.Start()
    builder.GetTestClient()

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
    ]
