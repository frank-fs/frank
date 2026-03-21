module Frank.Affordances.Tests.AutoLoadTests

open System.Net
open System.Net.Http
open System.Reflection
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder
open Frank.Affordances
open MiddlewareTests

[<Tests>]
let autoLoadTests =
    testList "useAffordances auto-load" [
        testCase "loadAffordanceMapFromAssembly returns None for assembly without model.bin" <| fun _ ->
            // The test assembly has Frank.Descriptors.bin, not model.bin
            let assembly = Assembly.GetExecutingAssembly()
            let result = StartupProjection.loadAffordanceMapFromAssembly assembly
            Expect.isNone result "should return None when no model.bin is present"

        testTask "useAffordances no-arg creates working server without error" {
            let testResource =
                resource "/items" {
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                }

            let ceBuilder = WebHostBuilder([||])
            let spec =
                ceBuilder.Yield()
                |> fun s -> ceBuilder.UseAffordances(s)
                |> fun s -> ceBuilder.Resource(s, testResource)

            // Build a minimal test server from the spec (replicates CE Run pipeline)
            let builder = WebApplication.CreateBuilder([||])
            builder.WebHost.UseTestServer() |> ignore
            spec.Services(builder.Services) |> ignore
            let app = builder.Build()
            (app :> IApplicationBuilder)
            |> spec.BeforeRoutingMiddleware
            |> fun app -> app.UseRouting()
            |> spec.Middleware
            |> ignore
            (app :> Microsoft.AspNetCore.Routing.IEndpointRouteBuilder).DataSources.Add(
                TestEndpointDataSource(spec.Endpoints))
            app.Start()
            let client = app.GetTestClient()

            let! (resp: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/items"))
            Expect.equal resp.StatusCode HttpStatusCode.OK "GET should return 200"
            let! (body: string) = resp.Content.ReadAsStringAsync()
            Expect.equal body "items" "should get handler response"
        }
    ]
