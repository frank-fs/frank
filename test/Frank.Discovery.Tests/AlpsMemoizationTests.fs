module Frank.Discovery.Tests.AlpsMemoizationTests

open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Primitives
open Expecto
open Frank.Discovery
open Frank.Tests.Shared.TestEndpointDataSource
open Frank.Discovery.Tests.TestHelpers

/// #398 /simplify item 6: handleAlpsProfile used to re-walk the whole resolved-descriptor
/// tree (href/rt resolution against the live origin) on EVERY request, regardless of origin
/// repetition — pure per-request waste for a deterministic-per-origin computation, the same
/// class of gap #382 already closed for LinkedDataMiddleware's static-graph bodies. These
/// tests drive the middleware directly (no TestServer/Kestrel), so the internal
/// ResolvedAlpsBuildCount counter — incremented at the exact point the resolved descriptor
/// tree is actually (re)built — gives a deterministic, non-flaky proof of
/// build-once-per-distinct-origin.

let private emptyEndpoints =
    TestEndpointDataSource([||]) :> Microsoft.AspNetCore.Routing.EndpointDataSource

let private emptyApiDescriptionProvider = apiDescriptionProviderFor emptyEndpoints

/// Unlike sampleConfig (both Hrefs already absolute, schema.org), this descriptor carries a
/// RELATIVE, app-owned href — the case whose resolved value actually differs per origin, so
/// the "distinct origin ⇒ distinct resolved body" test below can observe a real difference.
let private relativeHrefConfig =
    { ProfileUri = "/alps/test"
      HomeRoute = "/"
      AlpsDescriptors =
        [ { Id = "square"
            Type = "semantic"
            Doc = None
            Href = Some "/tictactoe#square"
            Descriptors = []
            Rt = None
            ClassIri = None
            RequestClrTypeName = None } ]
      DescribedByLinks = []
      ResourceHrefVars = Map.empty }

let private makeContext (scheme: string) (host: string) (path: string) : HttpContext =
    let ctx = new DefaultHttpContext()
    ctx.Request.Method <- "GET"
    ctx.Request.Scheme <- scheme
    ctx.Request.Host <- HostString host
    ctx.Request.Path <- PathString path
    ctx.Response.Body <- new MemoryStream()
    ctx :> HttpContext

let private invoke (middleware: DiscoveryMiddleware.DiscoveryMiddleware) (ctx: HttpContext) : int =
    middleware.Invoke(ctx).GetAwaiter().GetResult()
    ctx.Response.StatusCode

let private newMiddleware (config: DiscoveryConfig) =
    let next =
        RequestDelegate(fun ctx ->
            ctx.Response.StatusCode <- 200
            Task.CompletedTask)

    DiscoveryMiddleware.DiscoveryMiddleware(
        next,
        config,
        emptyEndpoints,
        emptyApiDescriptionProvider,
        NullLogger<DiscoveryMiddleware.DiscoveryMiddleware>.Instance
    )

[<Tests>]
let tests =
    testList
        "DiscoveryMiddleware resolved-ALPS memoization (#398 /simplify item 6)"
        [ testCase "5 ALPS profile requests to the same origin resolve the descriptor tree exactly once"
          <| fun _ ->
              let middleware = newMiddleware sampleConfig

              for _ in 1..5 do
                  invoke middleware (makeContext "http" "example.com" sampleConfig.ProfileUri)
                  |> ignore

              Expect.equal
                  middleware.ResolvedAlpsBuildCount
                  1
                  "same origin repeated 5x ⇒ descriptor tree resolved exactly once, not once per request"

          testCase "a second, distinct origin triggers exactly one additional resolution"
          <| fun _ ->
              let middleware = newMiddleware sampleConfig

              invoke middleware (makeContext "http" "example.com" sampleConfig.ProfileUri)
              |> ignore

              invoke middleware (makeContext "http" "other.example" sampleConfig.ProfileUri)
              |> ignore

              invoke middleware (makeContext "http" "other.example" sampleConfig.ProfileUri)
              |> ignore

              Expect.equal
                  middleware.ResolvedAlpsBuildCount
                  2
                  "two distinct origins ⇒ two resolutions total, regardless of repeat requests to each"

          testCase "repeat requests to the same origin serve byte-identical ALPS bodies (no behavioral change)"
          <| fun _ ->
              let middleware = newMiddleware sampleConfig

              let readBody (ctx: HttpContext) =
                  ctx.Response.Body.Position <- 0L
                  use reader = new StreamReader(ctx.Response.Body)
                  reader.ReadToEnd()

              let ctx1 = makeContext "http" "example.com" sampleConfig.ProfileUri
              let sc1 = invoke middleware ctx1
              let body1 = readBody ctx1

              let ctx2 = makeContext "http" "example.com" sampleConfig.ProfileUri
              let sc2 = invoke middleware ctx2
              let body2 = readBody ctx2

              Expect.equal sc1 200 "first request served"
              Expect.equal sc2 200 "second (cached) request served"
              Expect.equal body2 body1 "cached resolved ALPS body must be byte-identical across repeat requests"

          testCase "a different origin gets its own resolved hrefs, not the first origin's cached ones"
          <| fun _ ->
              let middleware = newMiddleware relativeHrefConfig

              let readBody (ctx: HttpContext) =
                  ctx.Response.Body.Position <- 0L
                  use reader = new StreamReader(ctx.Response.Body)
                  reader.ReadToEnd()

              let ctxA = makeContext "http" "example.com" relativeHrefConfig.ProfileUri
              invoke middleware ctxA |> ignore
              let bodyA = readBody ctxA

              let ctxB = makeContext "http" "other.example" relativeHrefConfig.ProfileUri
              invoke middleware ctxB |> ignore
              let bodyB = readBody ctxB

              Expect.stringContains bodyA "example.com" "first origin's resolved href cites its own host"
              Expect.stringContains bodyB "other.example" "second origin's resolved href cites its own host"

              Expect.isFalse
                  (bodyB.Contains "example.com")
                  "second origin's body must not leak the first origin's resolved href"

              Expect.notEqual bodyB bodyA "distinct origins ⇒ distinct resolved bodies, not a stale cache hit" ]
