module Frank.Validation.Tests.ValidationReflectionTests

open System.Net
open System.Net.Http
open System.Reflection
open System.Text
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open VDS.RDF.Shacl
open Frank.Builder
open Frank.Validation
open Frank.Validation.ValidationMiddleware

[<Tests>]
let validationReflectionTests =
    testList
        "ValidationReflection: useValidation zero-arg CE loads GeneratedValidation"
        [ test "executing assembly contains GeneratedValidation type" {
              let t = Assembly.GetExecutingAssembly().GetType("GeneratedValidation")
              Expect.isNotNull t "GeneratedValidation type should be found in executing assembly"
          }

          test "useValidation CE: registers non-empty ShapesGraph (AppDomain scan finds GeneratedValidation)" {
              let wh = WebHostBuilder([||])
              let configuredSpec = wh.UseValidation(WebHostSpec.Empty)
              let services = new ServiceCollection()
              configuredSpec.Services(services) |> ignore
              use provider = services.BuildServiceProvider()
              let shapesGraph = provider.GetRequiredService<ShapesGraph>()

              Expect.isTrue
                  (shapesGraph.Triples.Count > 0)
                  "useValidation CE should register the generated ShapesGraph with real SHACL triples — not the empty fallback"
          }

          testTask "useValidation CE: valid body passes (generated ShapesGraph, not empty fallback)" {
              let wh = WebHostBuilder([||])
              let configuredSpec = wh.UseValidation(WebHostSpec.Empty)

              let builder = WebApplication.CreateBuilder([||])
              builder.WebHost.UseTestServer() |> ignore
              builder.Services.AddRouting() |> ignore
              configuredSpec.Services(builder.Services) |> ignore
              let app = builder.Build()
              (app :> IApplicationBuilder).UseRouting() |> ignore
              configuredSpec.Middleware(app :> IApplicationBuilder) |> ignore

              app.MapPost(
                  "/orders",
                  RequestDelegate(fun ctx ->
                      ctx.Response.StatusCode <- 200
                      ctx.Response.WriteAsync("ok"))
              )
              |> ignore

              app.StartAsync() |> Async.AwaitTask |> Async.RunSynchronously
              let client = app.GetTestClient()

              let body =
                  """{"@type": "https://schema.org/Order", "https://schema.org/totalPaymentDue": 100}"""

              let content = new StringContent(body, Encoding.UTF8, "application/ld+json")
              let! (response: HttpResponseMessage) = client.PostAsync("/orders", content)

              Expect.equal
                  response.StatusCode
                  HttpStatusCode.OK
                  "valid Order body should pass — useValidation loaded real ShapesGraph"
          }

          testTask "useValidation CE: missing required field returns 422 (real SHACL constraints, not empty fallback)" {
              let wh = WebHostBuilder([||])
              let configuredSpec = wh.UseValidation(WebHostSpec.Empty)

              let builder = WebApplication.CreateBuilder([||])
              builder.WebHost.UseTestServer() |> ignore
              builder.Services.AddRouting() |> ignore
              configuredSpec.Services(builder.Services) |> ignore
              let app = builder.Build()
              (app :> IApplicationBuilder).UseRouting() |> ignore
              configuredSpec.Middleware(app :> IApplicationBuilder) |> ignore

              app.MapPost(
                  "/orders",
                  RequestDelegate(fun ctx ->
                      ctx.Response.StatusCode <- 200
                      ctx.Response.WriteAsync("ok"))
              )
              |> ignore

              app.StartAsync() |> Async.AwaitTask |> Async.RunSynchronously
              let client = app.GetTestClient()

              let body = """{"@type": "https://schema.org/Order"}"""
              let content = new StringContent(body, Encoding.UTF8, "application/ld+json")
              let! (response: HttpResponseMessage) = client.PostAsync("/orders", content)

              Expect.equal
                  response.StatusCode
                  HttpStatusCode.UnprocessableEntity
                  "missing totalPaymentDue returns 422 — useValidation loaded real minCount=1 constraint, not empty fallback"
          } ]
