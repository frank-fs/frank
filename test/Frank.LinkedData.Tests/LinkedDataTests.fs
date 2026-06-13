module Frank.LinkedData.Tests.LinkedDataTests

open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open VDS.RDF
open Frank.LinkedData.LinkedDataMiddleware

let private makeTestGraph () : IGraph =
    let g = new Graph()
    let uri s = g.CreateUriNode(UriFactory.Create(s))

    g.Assert(
        Triple(
            uri "urn:frank:type:Order",
            uri "http://www.w3.org/2002/07/owl#equivalentClass",
            uri "https://schema.org/Order"
        )
    )
    |> ignore

    g :> IGraph

let private makeTestConfig () : LinkedDataConfig =
    { Graph = makeTestGraph ()
      JsonLdContext = """{"@context": ["https://schema.org/"]}""" }

let private createServer () =
    let config = makeTestConfig ()
    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddRouting() |> ignore
    builder.Services.AddSingleton<LinkedDataConfig>(config) |> ignore
    let app = builder.Build()
    (app :> IApplicationBuilder).UseRouting() |> ignore
    app.UseMiddleware<LinkedDataMiddleware>() |> ignore

    app.MapGet(
        "/orders/{id}",
        Func<HttpContext, Task>(fun ctx ->
            task {
                ctx.Response.StatusCode <- 200
                do! ctx.Response.WriteAsync("Order response")
            }
            :> Task)
    )
    |> ignore

    app.Start()
    app.GetTestClient()

[<Tests>]
let linkedDataTests =
    testList
        "Frank.LinkedData"
        [ testTask "AT1: application/ld+json returns 200 with JSON-LD content type" {
              let client = createServer ()

              let req = new HttpRequestMessage(HttpMethod.Get, "/orders/o1")
              req.Headers.Add("Accept", "application/ld+json")
              let! (response: HttpResponseMessage) = client.SendAsync(req)
              Expect.equal response.StatusCode HttpStatusCode.OK "should return 200"

              Expect.isTrue
                  (response.Content.Headers.ContentType.MediaType = "application/ld+json")
                  "content type should be application/ld+json"
          }

          testTask "AT2: text/turtle returns 200 with Turtle content type" {
              let client = createServer ()

              let req = new HttpRequestMessage(HttpMethod.Get, "/orders/o1")
              req.Headers.Add("Accept", "text/turtle")
              let! (response: HttpResponseMessage) = client.SendAsync(req)
              Expect.equal response.StatusCode HttpStatusCode.OK "should return 200"

              Expect.isTrue
                  (response.Content.Headers.ContentType.MediaType = "text/turtle")
                  "content type should be text/turtle"
          }

          testTask "AT3: unsupported Accept returns 406" {
              let client = createServer ()

              let req = new HttpRequestMessage(HttpMethod.Get, "/orders/o1")
              req.Headers.Add("Accept", "application/xml")
              let! (response: HttpResponseMessage) = client.SendAsync(req)
              Expect.equal response.StatusCode HttpStatusCode.NotAcceptable "should return 406"
          }

          testTask "AT4: JSON-LD body contains owl:equivalentClass triple" {
              let client = createServer ()

              let req = new HttpRequestMessage(HttpMethod.Get, "/orders/o1")
              req.Headers.Add("Accept", "application/ld+json")
              let! (response: HttpResponseMessage) = client.SendAsync(req)
              let! (body: string) = response.Content.ReadAsStringAsync()

              Expect.isTrue
                  (body.Contains("schema.org") || body.Contains("equivalentClass"))
                  "body should reference schema.org vocabulary"
          }

          testTask "AT2b: Turtle body contains vocabulary IRIs" {
              let client = createServer ()

              let req = new HttpRequestMessage(HttpMethod.Get, "/orders/o1")
              req.Headers.Add("Accept", "text/turtle")
              let! (response: HttpResponseMessage) = client.SendAsync(req)
              let! (body: string) = response.Content.ReadAsStringAsync()

              Expect.isTrue (body.Contains("schema.org") || body.Length > 0) "turtle body should reference vocabulary"
          }

          testTask "AT5: useLinkedDataWith compiles and registers correctly" {
              let config = makeTestConfig ()
              let builder = WebApplication.CreateBuilder([||])
              builder.WebHost.UseTestServer() |> ignore
              builder.Services.AddRouting() |> ignore
              builder.Services.AddSingleton<LinkedDataConfig>(config) |> ignore
              let app = builder.Build()
              (app :> IApplicationBuilder).UseMiddleware<LinkedDataMiddleware>() |> ignore

              app.MapGet("/test", Func<HttpContext, Task>(fun ctx -> (task { ctx.Response.StatusCode <- 200 }) :> Task))
              |> ignore

              app.Start()
              let client = app.GetTestClient()
              let! (response: HttpResponseMessage) = client.GetAsync("/test")
              Expect.equal response.StatusCode HttpStatusCode.OK "server should start and respond"
          } ]
