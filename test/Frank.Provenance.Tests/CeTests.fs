module Frank.Provenance.Tests.CeTests

open System
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Expecto
open Frank.Builder
open Frank.Provenance
open Frank.Tests.Shared.TestEndpointDataSource

let private startCeServer (config: ProvenanceConfig) =
    let ceBuilder = WebHostBuilder([||])
    let spec = ceBuilder.Yield(()) |> fun s -> ceBuilder.UseProvenanceWith(s, config)
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    spec.Services(builder.Services) |> ignore
    let app = builder.Build()

    (app :> IApplicationBuilder)
    |> fun a -> a.UseRouting()
    |> spec.Middleware
    |> ignore

    (app :> IEndpointRouteBuilder).DataSources.Add(TestEndpointDataSource(spec.Endpoints))

    app
        .MapPost(
            "/orders",
            Func<HttpContext, System.Threading.Tasks.Task>(fun ctx ->
                ctx.Response.StatusCode <- 201
                ctx.Response.WriteAsync("{}"))
        )
        .WithMetadata(
            Microsoft.AspNetCore.Http.ProducesResponseTypeMetadata(
                201,
                typeof<MiddlewareTestHelpers.OrderPlaced>,
                [| "application/json" |]
            )
        )
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app

[<Tests>]
let tests =
    testList
        "ProvenanceCE"
        [ testCaseAsync
              "useProvenanceWith: POST with prov Accept profile returns PROV-O ld+json (middleware wired by CE)"
          <| async {
              use app = startCeServer (MiddlewareTestHelpers.orderProvConfig ())
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Post, "/orders")

              req.Headers.TryAddWithoutValidation(
                  "Accept",
                  "application/ld+json; profile=\"http://www.w3.org/ns/prov\""
              )
              |> ignore

              let! (resp: HttpResponseMessage) = client.SendAsync(req) |> Async.AwaitTask
              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.stringContains body "prov:Activity" "Activity present as CURIE"
              Expect.stringContains body "prov:Agent" "Agent present as CURIE"
          }

          testCaseAsync
              "useProvenanceWith: GET /provenance?resource=<path> returns 200 ld+json lineage (endpoint mapped by CE)"
          <| async {
              use app = startCeServer (MiddlewareTestHelpers.orderProvConfig ())
              use client = app.GetTestClient()

              let! _ = client.PostAsync("/orders", new StringContent("{}")) |> Async.AwaitTask

              let! (resp: HttpResponseMessage) = client.GetAsync("/provenance?resource=/orders") |> Async.AwaitTask

              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "200 from provenance endpoint"

              Expect.isTrue
                  (resp.Content.Headers.ContentType.MediaType.StartsWith("application/ld+json"))
                  "content-type is ld+json"

              Expect.stringContains body "prov:Activity" "at least one Activity as CURIE"
          } ]
