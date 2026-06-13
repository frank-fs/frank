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
open Microsoft.Extensions.Hosting
open Expecto
open VDS.RDF
open Frank.Builder
open Frank.LinkedData
open Frank.LinkedData.LinkedDataMiddleware

[<Tests>]
let linkedDataReflectionTests =
    testList
        "LinkedDataReflection: useLinkedData zero-arg CE loads GeneratedLinkedData"
        [ test "executing assembly contains GeneratedLinkedData type" {
              let t = Assembly.GetExecutingAssembly().GetType("GeneratedLinkedData")
              Expect.isNotNull t "GeneratedLinkedData type should be found in executing assembly"
          }

          test "useLinkedData CE: registers non-empty graph (AppDomain scan finds GeneratedLinkedData)" {
              let wh = WebHostBuilder([||])
              let configuredSpec = wh.UseLinkedData(WebHostSpec.Empty)
              let services = new ServiceCollection()
              configuredSpec.Services(services) |> ignore
              use provider = services.BuildServiceProvider()
              let config = provider.GetRequiredService<LinkedDataConfig>()

              Expect.isTrue
                  (config.Graph.Triples.Count > 0)
                  "useLinkedData CE should register a graph with owl:equivalentClass triples — not the empty fallback"

              Expect.stringContains
                  config.JsonLdContext
                  "schema.org"
                  "useLinkedData CE should register a jsonLdContext referencing schema.org — not the empty fallback"
          }

          testTask
              "useLinkedData CE: GET ld+json returns 200 with schema.org reference (loaded graph, not empty fallback)" {
              let wh = WebHostBuilder([||])
              let configuredSpec = wh.UseLinkedData(WebHostSpec.Empty)

              let builder = WebApplication.CreateBuilder([||])
              builder.WebHost.UseTestServer() |> ignore
              builder.Services.AddRouting() |> ignore
              configuredSpec.Services(builder.Services) |> ignore
              let app = builder.Build()
              (app :> IApplicationBuilder).UseRouting() |> ignore
              configuredSpec.Middleware(app :> IApplicationBuilder) |> ignore

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
                  "body must reference schema.org from loaded graph — useLinkedData found GeneratedLinkedData, not empty fallback"
          } ]
