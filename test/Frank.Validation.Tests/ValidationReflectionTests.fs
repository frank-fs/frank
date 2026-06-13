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
open Frank.Validation.ValidationMiddleware

[<Tests>]
let validationReflectionTests =
    testList
        "ValidationReflection: useValidation zero-arg reflection path"
        [ test "executing assembly contains GeneratedValidation type" {
              let asm = Assembly.GetExecutingAssembly()
              Expect.isNotNull asm "executing assembly should not be null"
              let t = asm.GetType("GeneratedValidation")
              Expect.isNotNull t "GeneratedValidation type should be found in entry assembly"
          }

          test "GeneratedValidation.shapesGraph property loads a non-empty ShapesGraph" {
              let asm = Assembly.GetExecutingAssembly()
              let t = asm.GetType("GeneratedValidation")
              Expect.isNotNull t "GeneratedValidation type should be found"
              let prop = t.GetProperty("shapesGraph")
              Expect.isNotNull prop "shapesGraph property should exist on GeneratedValidation"
              let shapesGraph = prop.GetValue(null) :?> ShapesGraph
              Expect.isNotNull shapesGraph "shapesGraph should not be null"

              Expect.isTrue
                  (shapesGraph.Triples.Count > 0)
                  "shapesGraph should have SHACL triples — not the empty fallback"
          }

          testTask "useValidation zero-arg: valid body passes (generated ShapesGraph, not empty fallback)" {
              let asm = Assembly.GetExecutingAssembly()
              let t = asm.GetType("GeneratedValidation")
              let shapesGraph = t.GetProperty("shapesGraph").GetValue(null) :?> ShapesGraph

              let builder = WebApplication.CreateBuilder([||])
              builder.WebHost.UseTestServer() |> ignore
              builder.Services.AddRouting() |> ignore
              builder.Services.AddSingleton<ShapesGraph>(shapesGraph) |> ignore
              let app = builder.Build()
              (app :> IApplicationBuilder).UseRouting() |> ignore
              app.UseMiddleware<ValidationMiddleware>() |> ignore

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

              let content =
                  new StringContent(body, System.Text.Encoding.UTF8, "application/ld+json")

              let! (response: HttpResponseMessage) = client.PostAsync("/orders", content)

              Expect.equal
                  response.StatusCode
                  HttpStatusCode.OK
                  "valid Order body should pass with generated ShapesGraph"
          }

          testTask "useValidation zero-arg: missing required field returns 422 (ShapesGraph has real SHACL constraints)" {
              let asm = Assembly.GetExecutingAssembly()
              let t = asm.GetType("GeneratedValidation")
              let shapesGraph = t.GetProperty("shapesGraph").GetValue(null) :?> ShapesGraph

              let builder = WebApplication.CreateBuilder([||])
              builder.WebHost.UseTestServer() |> ignore
              builder.Services.AddRouting() |> ignore
              builder.Services.AddSingleton<ShapesGraph>(shapesGraph) |> ignore
              let app = builder.Build()
              (app :> IApplicationBuilder).UseRouting() |> ignore
              app.UseMiddleware<ValidationMiddleware>() |> ignore

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

              let content =
                  new StringContent(body, System.Text.Encoding.UTF8, "application/ld+json")

              let! (response: HttpResponseMessage) = client.PostAsync("/orders", content)

              Expect.equal
                  response.StatusCode
                  HttpStatusCode.UnprocessableEntity
                  "missing totalPaymentDue must return 422 — proves loaded ShapesGraph has real minCount constraint, not empty fallback"
          } ]
