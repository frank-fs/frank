/// #467 (follow-up to #426's R10 ordering contract): proves LinkedDataMiddleware's
/// `describedby` Link header survives a 304 short-circuit produced by
/// ConditionalRequestMiddleware, when LinkedDataMiddleware is registered OUTER to
/// useConditionalRequests -- the same proof shape as
/// ConditionalRequestContextComputeTests.fs's generic R10 test, but exercising the real
/// production LinkedDataMiddleware (src/Frank.LinkedData/LinkedDataMiddleware.fs) instead of a
/// hand-rolled stand-in middleware.
module Frank.Tests.LinkedDataConditionalRequestSurvivalTests

open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Expecto
open Frank
open Frank.Builder
open Frank.LinkedData

let private buildFixtureGraph () =
    let graph = new VDS.RDF.Graph()

    let subject = graph.CreateUriNode(Uri "https://example.org/described/1")

    let predicate =
        graph.CreateUriNode(Uri "http://www.w3.org/2000/01/rdf-schema#seeAlso")

    let obj = graph.CreateUriNode(Uri "https://schema.org/Thing")
    graph.Assert(VDS.RDF.Triple(subject, predicate, obj)) |> ignore
    graph :> VDS.RDF.IGraph

/// Real LinkedDataMiddleware + real ConditionalRequestMiddleware, correctly ordered per R10:
/// LinkedDataMiddleware (outer) registered BEFORE useConditionalRequests (inner). The route
/// carries both LinkedDataConfig (so LinkedDataMiddleware emits describedby) and ETagMetadata
/// (so ConditionalRequestMiddleware can 304 it).
let private startServer () =
    let ldConfig =
        { LinkedDataConfig.Empty with
            Graph = buildFixtureGraph ()
            JsonLdContext = """{"@context":["https://schema.org"]}""" }

    let etagMetadata =
        ETagMetadata(
            (fun (ctx: HttpContext) -> ctx.Request.Path.Value),
            (fun (_: ETagContext) -> task { return Some "described-etag-1" })
        )

    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(ldConfig) |> ignore

    builder.Services.AddSingleton({ LinkedDataVocabularyConfig.VocabularyRoute = Some "/vocabulary" })
    |> ignore

    builder.Services.AddETagCache() |> ignore
    registerBoundedMemoryCaches builder.Services |> ignore
    let app = builder.Build()
    (app :> IApplicationBuilder).UseRouting() |> ignore
    // R10 (#426/#467): LinkedDataMiddleware registered OUTER to (before) useConditionalRequests.
    (app :> IApplicationBuilder).UseMiddleware<LinkedDataMiddleware>()
    |> useConditionalRequests
    |> ignore

    app
        .MapGet("/described", Func<HttpContext, Task>(fun ctx -> ctx.Response.WriteAsync "downstream"))
        .WithMetadata(ldConfig)
        .WithMetadata(etagMetadata)
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app

[<Tests>]
let linkedDataConditionalRequestSurvivalTests =
    testList
        "LinkedDataMiddleware describedby Link header survives a 304 short-circuit (R10, #467)"
        [ testTask "GET with matching If-None-Match returns 304, empty body, and describedby Link header intact" {
              use app = startServer ()
              let client = app.GetTestClient()

              // No Accept: application/ld+json here -- that would make LinkedDataMiddleware
              // serve the RDF representation itself and short-circuit BEFORE next.Invoke,
              // meaning ConditionalRequestMiddleware (registered after it) would never run.
              // The describedby Link header is appended unconditionally for every safe-method
              // response before negotiation (#420), then LinkedDataMiddleware passes through
              // to ConditionalRequestMiddleware/the endpoint for the actual ETag/304 decision.
              let! (firstResponse: HttpResponseMessage) = client.GetAsync("/described")
              Expect.equal firstResponse.StatusCode HttpStatusCode.OK "first GET must be 200"
              let etag = firstResponse.Headers.ETag.ToString()
              Expect.equal etag "\"described-etag-1\"" "ETag must be the quoted compute-closure value"

              let secondReq = new HttpRequestMessage(HttpMethod.Get, "/described")
              secondReq.Headers.TryAddWithoutValidation("If-None-Match", etag) |> ignore
              let! (secondResponse: HttpResponseMessage) = client.SendAsync(secondReq)

              Expect.equal
                  secondResponse.StatusCode
                  HttpStatusCode.NotModified
                  "matching If-None-Match must produce a 304"

              let! body = secondResponse.Content.ReadAsStringAsync()
              Expect.equal body "" "304 must have an empty body"

              Expect.isTrue
                  (secondResponse.Headers.Contains "Link")
                  "describedby Link header must survive the 304 short-circuit"

              let linkValues = secondResponse.Headers.GetValues "Link" |> Seq.toList

              Expect.contains
                  linkValues
                  "</vocabulary>; rel=\"describedby\"; type=\"application/ld+json\""
                  "Link header value must be the exact describedby relation LinkedDataMiddleware emits, not a loose substring match"
          } ]
