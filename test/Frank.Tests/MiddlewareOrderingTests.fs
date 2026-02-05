module Frank.Tests.MiddlewareOrderingTests

open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder

/// Simple authentication middleware that checks for an "X-Auth-Token" header
let simpleAuthMiddleware (next: RequestDelegate) (ctx: HttpContext) =
    task {
        let hasToken = ctx.Request.Headers.ContainsKey("X-Auth-Token")
        if hasToken then
            do! next.Invoke(ctx)
        else
            ctx.Response.StatusCode <- 401
    } :> Task

/// Middleware that adds a marker header to track execution order
let markerMiddleware (name: string) (next: RequestDelegate) (ctx: HttpContext) =
    task {
        let existingMarkers =
            if ctx.Items.ContainsKey("markers") then
                ctx.Items["markers"] :?> string
            else
                ""
        ctx.Items["markers"] <- existingMarkers + name + ","
        do! next.Invoke(ctx)
    } :> Task

/// Creates a test server with the given webHost configuration
let createTestServer (configureSpec: WebHostSpec -> WebHostSpec) =
    let builder =
        Host.CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore)
                    .Configure(fun app ->
                        let spec = WebHostSpec.Empty |> configureSpec

                        app
                        |> spec.BeforeRoutingMiddleware
                        |> fun app -> app.UseRouting()
                        |> spec.Middleware
                        |> fun app ->
                            app.UseEndpoints(fun endpoints ->
                                endpoints.MapGet("/test", Func<HttpContext, Task>(fun ctx ->
                                    task {
                                        let markers =
                                            if ctx.Items.ContainsKey("markers") then
                                                ctx.Items["markers"] :?> string
                                            else
                                                ""
                                        ctx.Response.Headers.Append("X-Markers", markers)
                                        do! ctx.Response.WriteAsync("OK")
                                    } :> Task)) |> ignore)
                        |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

[<Tests>]
let middlewareOrderingTests =
    testList "Middleware Ordering" [
        testTask "plug middleware executes after UseRouting" {
            let client = createTestServer (fun spec ->
                { spec with
                    Middleware = fun app ->
                        app.Use(Func<HttpContext, RequestDelegate, Task>(fun ctx next ->
                            markerMiddleware "plug" next ctx)) })

            let! (response : HttpResponseMessage) = client.GetAsync("/test")
            Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200 OK"
        }

        testTask "plugBeforeRouting middleware executes before UseRouting" {
            let client = createTestServer (fun spec ->
                { spec with
                    BeforeRoutingMiddleware = fun app ->
                        app.Use(Func<HttpContext, RequestDelegate, Task>(fun ctx next ->
                            markerMiddleware "beforeRouting" next ctx)) })

            let! (response : HttpResponseMessage) = client.GetAsync("/test")
            Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200 OK"
        }

        testTask "middleware execution order is plugBeforeRouting then plug" {
            let client = createTestServer (fun spec ->
                { spec with
                    BeforeRoutingMiddleware = fun app ->
                        app.Use(Func<HttpContext, RequestDelegate, Task>(fun ctx next ->
                            markerMiddleware "before" next ctx))
                    Middleware = fun app ->
                        app.Use(Func<HttpContext, RequestDelegate, Task>(fun ctx next ->
                            markerMiddleware "after" next ctx)) })

            let! (response : HttpResponseMessage) = client.GetAsync("/test")
            let markers =
                if response.Headers.Contains("X-Markers") then
                    response.Headers.GetValues("X-Markers") |> Seq.head
                else
                    ""

            Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200 OK"
            Expect.isTrue (markers.StartsWith("before,")) "before marker should come first"
            Expect.isTrue (markers.Contains("after,")) "after marker should be present"
        }

        testTask "authentication middleware via plug can reject unauthenticated requests" {
            let client = createTestServer (fun spec ->
                { spec with
                    Middleware = fun app ->
                        app.Use(Func<HttpContext, RequestDelegate, Task>(fun ctx next ->
                            simpleAuthMiddleware next ctx)) })

            let! (response : HttpResponseMessage) = client.GetAsync("/test")
            Expect.equal response.StatusCode HttpStatusCode.Unauthorized "Should return 401 for unauthenticated"
        }

        testTask "authentication middleware via plug allows authenticated requests" {
            let client = createTestServer (fun spec ->
                { spec with
                    Middleware = fun app ->
                        app.Use(Func<HttpContext, RequestDelegate, Task>(fun ctx next ->
                            simpleAuthMiddleware next ctx)) })

            let request = new HttpRequestMessage(HttpMethod.Get, "/test")
            request.Headers.Add("X-Auth-Token", "valid-token")
            let! (response : HttpResponseMessage) = client.SendAsync(request)

            Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200 OK for authenticated"
        }

        testTask "no middleware registered still works" {
            let client = createTestServer id

            let! (response : HttpResponseMessage) = client.GetAsync("/test")
            Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200 OK with no middleware"
        }
    ]
