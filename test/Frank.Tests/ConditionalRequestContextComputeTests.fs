/// #426: ETagMetadata folds ETag computation into a `compute: ETagContext -> Task<string
/// option>` closure attached at route-registration time, instead of an opaque
/// `instanceId`-keyed IETagProvider resolved from DI. These tests prove the two concrete
/// falsifiable claims that motivated the redesign:
///
/// AC1: a compute closure can read anything off the HttpContext it is handed (not just the
/// pre-resolved instanceId) to build its ETag -- the whole point of folding ETag computation
/// into endpoint metadata instead of an opaque-instanceId provider interface.
///
/// R10: a Link header appended by a middleware registered OUTER to
/// useConditionalRequests survives a 304 short-circuit produced by
/// ConditionalRequestMiddleware -- the documented ordering contract on
/// useConditionalRequests (see src/Frank/ConditionalRequestMiddleware.fsi).
module Frank.Tests.ConditionalRequestContextComputeTests

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
open Microsoft.Extensions.Logging.Abstractions
open Expecto
open Frank

let private linkEmittingMiddleware (next: RequestDelegate) (ctx: HttpContext) =
    task {
        ctx.Response.Headers.Append("Link", "<https://example.org/describedby>; rel=\"describedby\"")
        do! next.Invoke(ctx)
    }
    :> Task

[<Tests>]
let conditionalRequestContextComputeTests =
    testList
        "ConditionalRequestMiddleware context-aware compute (#426)"
        [
          // -- AC1: compute closure derives ETag from HttpContext beyond instanceId --
          testTask "compute closure derives ETag from a route value AND a query string value, not instanceId alone" {
              // instanceIdResolver only resolves the route value; the compute closure below
              // additionally reads the query string directly off ETagContext.HttpContext --
              // proving the closure is not limited to the opaque instanceId string.
              let etagMetadata =
                  ETagMetadata(
                      (fun ctx -> ctx.Request.RouteValues.["id"] :?> string),
                      (fun (etagContext: ETagContext) ->
                          task {
                              let version = etagContext.HttpContext.Request.Query.["version"].ToString()
                              return Some(sprintf "%s-v%s" etagContext.InstanceId version)
                          })
                  )

              let cache = new ETagCache(100, NullLogger<ETagCache>.Instance)
              let builder = WebApplication.CreateBuilder([||])
              builder.WebHost.UseTestServer() |> ignore
              builder.Services.AddRouting() |> ignore
              builder.Services.AddSingleton<ETagCache>(cache) |> ignore
              builder.Services.AddLogging() |> ignore
              let app = builder.Build()

              (app :> IApplicationBuilder).UseRouting() |> ignore

              (app :> IApplicationBuilder).UseMiddleware<ConditionalRequestMiddleware>()
              |> ignore

              app
                  .MapGet(
                      "/versioned/{id}",
                      Func<HttpContext, Task>(fun ctx -> task { do! ctx.Response.WriteAsync("OK") } :> Task)
                  )
                  .WithMetadata(etagMetadata)
              |> ignore

              app.Start()
              let client = app.GetTestClient()
              let! (response: HttpResponseMessage) = client.GetAsync("/versioned/42?version=7")
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
              let etag = response.Headers.ETag.ToString()

              Expect.equal
                  etag
                  "\"42-v7\""
                  "ETag must reflect both the route value AND the query string -- proof the compute closure is context-aware, not instanceId-only"
          }

          // -- R10: Link header from an outer middleware survives a 304 short-circuit --
          testTask "Link header appended by a middleware registered outer to useConditionalRequests survives a 304" {
              let etagMetadata =
                  ETagMetadata(
                      (fun ctx -> ctx.Request.RouteValues.["id"] :?> string),
                      (fun (_: ETagContext) -> task { return Some "abc123" })
                  )

              let cache = new ETagCache(100, NullLogger<ETagCache>.Instance)
              let builder = WebApplication.CreateBuilder([||])
              builder.WebHost.UseTestServer() |> ignore
              builder.Services.AddRouting() |> ignore
              builder.Services.AddSingleton<ETagCache>(cache) |> ignore
              builder.Services.AddLogging() |> ignore
              let app = builder.Build()

              (app :> IApplicationBuilder).UseRouting() |> ignore

              // Registered OUTER to (before) ConditionalRequestMiddleware -- the ordering
              // contract useConditionalRequests documents.
              (app :> IApplicationBuilder)
                  .Use(Func<HttpContext, RequestDelegate, Task>(fun ctx next -> linkEmittingMiddleware next ctx))
              |> ignore

              (app :> IApplicationBuilder).UseMiddleware<ConditionalRequestMiddleware>()
              |> ignore

              app
                  .MapGet(
                      "/linked/{id}",
                      Func<HttpContext, Task>(fun ctx -> task { do! ctx.Response.WriteAsync("OK") } :> Task)
                  )
                  .WithMetadata(etagMetadata)
              |> ignore

              app.Start()
              let client = app.GetTestClient()
              let request = new HttpRequestMessage(HttpMethod.Get, "/linked/42")
              request.Headers.TryAddWithoutValidation("If-None-Match", "\"abc123\"") |> ignore
              let! (response: HttpResponseMessage) = client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.NotModified "Should return 304"
              Expect.isTrue (response.Headers.Contains("Link")) "Link header must survive the 304 short-circuit"
              let linkValue = response.Headers.GetValues("Link") |> Seq.head
              Expect.stringContains linkValue "describedby" "Link header value must be the describedby relation"
          } ]
