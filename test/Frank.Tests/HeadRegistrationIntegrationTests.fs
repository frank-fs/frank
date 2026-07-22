module Frank.Tests.HeadRegistrationIntegrationTests

/// #431: `get`-registered endpoints must also answer HEAD (RFC 7231 §7.4.1 — HEAD is GET
/// without a body) with the SAME headers GET would return, not a 405. Exercises the REAL
/// `resource`/`get`/`webHost` CE composition over TestServer — never a hand-built
/// RouteEndpoint — because the gap this fixes (ResourceSpec.Build registering GET-only
/// HttpMethodMetadata) only reproduces through that real registration path.
open System.Net
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open Frank
open Frank.Builder

/// Spins a TestServer composing the given resources via the real `resource`/`get`/`webHost`
/// CE path, mirroring WebHostBuilder.Run's own wiring sequence (ResourceEndpointDataSource
/// built from the fully composed spec.Endpoints, registered before Build(), added to
/// IEndpointRouteBuilder.DataSources after) with non-blocking Start() substituted for Run().
let private createHeadTestServer (resources: Resource list) =
    let spec =
        { WebHostSpec.Empty with
            Endpoints = resources |> List.collect (fun r -> List.ofArray r.Endpoints) |> Array.ofList }

    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    let dataSource = ResourceEndpointDataSource(spec.Endpoints)
    builder.Services.AddSingleton<ResourceEndpointDataSource>(dataSource) |> ignore
    spec.Services builder.Services |> ignore
    let app = builder.Build()

    (app :> IApplicationBuilder)
    |> fun a -> a.UseRouting() |> spec.Middleware |> ignore

    (app :> IEndpointRouteBuilder).DataSources.Add(dataSource)
    app.Start()
    app

let private gameHandler (ctx: HttpContext) =
    task {
        ctx.Response.Headers.ETag <- Microsoft.Extensions.Primitives.StringValues "\"v1\""
        ctx.Response.ContentType <- "application/json"
        do! ctx.Response.WriteAsync "{}"
    }

let private rootHandler (ctx: HttpContext) =
    task { do! ctx.Response.WriteAsync "root" }

let private buildApp () =
    let gameResource = resource "/games/{id}" { get gameHandler }
    let rootResource = resource "/" { get rootHandler }
    createHeadTestServer [ gameResource; rootResource ]

[<Tests>]
let headRegistrationIntegrationTests =
    testList
        "get CE registers HEAD alongside GET (#431)"
        [ testTask "HEAD /games/{id} returns 200 with empty body and GET's header set" {
              use app = buildApp ()
              let client = app.GetTestClient()

              let! (getResp: HttpResponseMessage) = client.GetAsync("/games/demo1")
              let headReq = new HttpRequestMessage(HttpMethod.Head, "/games/demo1")
              let! (headResp: HttpResponseMessage) = client.SendAsync(headReq)

              Expect.equal headResp.StatusCode HttpStatusCode.OK "HEAD should return 200"

              let! headBody = headResp.Content.ReadAsStringAsync()
              Expect.equal headBody "" "HEAD response body must be empty"

              let headerName (h: System.Net.Http.Headers.HttpResponseHeaders) =
                  h |> Seq.map (fun kvp -> kvp.Key) |> Set.ofSeq

              let getHeaders = headerName getResp.Headers
              let headHeaders = headerName headResp.Headers

              Expect.equal headHeaders getHeaders "HEAD header-set should equal GET header-set"
              Expect.equal (headResp.Headers.ETag.ToString()) (getResp.Headers.ETag.ToString()) "ETag should match GET"

              Expect.equal
                  (headResp.Content.Headers.ContentType.ToString())
                  (getResp.Content.Headers.ContentType.ToString())
                  "Content-Type should match GET"
          }

          testTask "HEAD / returns 200" {
              use app = buildApp ()
              let client = app.GetTestClient()
              let headReq = new HttpRequestMessage(HttpMethod.Head, "/")
              let! (headResp: HttpResponseMessage) = client.SendAsync(headReq)
              Expect.equal headResp.StatusCode HttpStatusCode.OK "HEAD / should return 200"
          }

          testTask "explicit head handler is respected — GET endpoint is not folded" {
              let explicitHeadCalled = System.Collections.Generic.List<bool>()

              let explicitHeadHandler (ctx: HttpContext) =
                  task {
                      explicitHeadCalled.Add true
                      ctx.Response.Headers.Append("X-Explicit-Head", "yes")
                  }

              let explicitResource =
                  resource "/explicit" {
                      get gameHandler
                      head explicitHeadHandler
                  }

              use app = createHeadTestServer [ explicitResource ]
              let client = app.GetTestClient()
              let headReq = new HttpRequestMessage(HttpMethod.Head, "/explicit")
              let! (headResp: HttpResponseMessage) = client.SendAsync(headReq)

              Expect.equal headResp.StatusCode HttpStatusCode.OK "explicit HEAD handler should still return 200"
              Expect.isTrue (headResp.Headers.Contains("X-Explicit-Head")) "explicit HEAD handler must run, not GET's"
          } ]
