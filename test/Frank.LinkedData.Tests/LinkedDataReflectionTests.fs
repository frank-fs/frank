module Frank.LinkedData.Tests.LinkedDataReflectionTests

open System
open System.Net
open System.Net.Http
open System.Reflection
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Expecto
open VDS.RDF
open Frank.LinkedData.LinkedDataMiddleware

[<Tests>]
let linkedDataReflectionTests =
    testList
        "LinkedDataReflection: useLinkedData zero-arg reflection path"
        [ test "executing assembly contains GeneratedLinkedData type" {
              let asm = Assembly.GetExecutingAssembly()
              Expect.isNotNull asm "executing assembly should not be null"
              let t = asm.GetType("GeneratedLinkedData")
              Expect.isNotNull t "GeneratedLinkedData type should be found in entry assembly"
          }

          test "GeneratedLinkedData.graph has triples (not empty fallback)" {
              let asm = Assembly.GetExecutingAssembly()
              let t = asm.GetType("GeneratedLinkedData")
              Expect.isNotNull t "GeneratedLinkedData type should be found"
              let prop = t.GetProperty("graph")
              Expect.isNotNull prop "graph property should exist on GeneratedLinkedData"
              let graph = prop.GetValue(null) :?> IGraph
              Expect.isNotNull graph "graph should not be null"

              Expect.isTrue
                  (graph.Triples.Count > 0)
                  "graph should have owl:equivalentClass triples — not empty fallback"
          }

          test "GeneratedLinkedData.jsonLdContext references schema.org" {
              let asm = Assembly.GetExecutingAssembly()
              let t = asm.GetType("GeneratedLinkedData")
              let prop = t.GetProperty("jsonLdContext")
              Expect.isNotNull prop "jsonLdContext property should exist on GeneratedLinkedData"
              let ctx = prop.GetValue(null) :?> string
              Expect.stringContains ctx "schema.org" "jsonLdContext should reference schema.org vocabulary"
          }

          testTask
              "useLinkedData zero-arg: GET ld+json returns 200 with schema.org reference (loaded graph, not empty fallback)" {
              let asm = Assembly.GetExecutingAssembly()
              let t = asm.GetType("GeneratedLinkedData")
              let graph = t.GetProperty("graph").GetValue(null) :?> IGraph
              let jsonLdContext = t.GetProperty("jsonLdContext").GetValue(null) :?> string

              let builder = WebApplication.CreateBuilder([||])
              builder.WebHost.UseTestServer() |> ignore
              builder.Services.AddRouting() |> ignore

              builder.Services.AddSingleton<LinkedDataConfig>(
                  { Graph = graph
                    JsonLdContext = jsonLdContext }
              )
              |> ignore

              let app = builder.Build()
              (app :> IApplicationBuilder).UseRouting() |> ignore
              app.UseMiddleware<LinkedDataMiddleware>() |> ignore

              app.MapGet(
                  "/orders/{id}",
                  Func<HttpContext, Task>(fun ctx ->
                      task {
                          ctx.Response.StatusCode <- 200
                          do! ctx.Response.WriteAsync("Order")
                      }
                      :> Task)
              )
              |> ignore

              app.StartAsync() |> Async.AwaitTask |> Async.RunSynchronously
              let client = app.GetTestClient()

              let req = new HttpRequestMessage(HttpMethod.Get, "/orders/o1")
              req.Headers.Add("Accept", "application/ld+json")
              let! (response: HttpResponseMessage) = client.SendAsync(req)
              Expect.equal response.StatusCode HttpStatusCode.OK "GET ld+json should return 200"
              let! (body: string) = response.Content.ReadAsStringAsync()

              Expect.stringContains
                  body
                  "schema.org"
                  "body should reference schema.org from loaded graph — proves graph has real equivalentClass triples, not empty fallback"
          } ]
