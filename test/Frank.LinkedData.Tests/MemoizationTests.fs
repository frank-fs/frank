module Frank.LinkedData.Tests.MemoizationTests

open System
open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Primitives
open Expecto
open VDS.RDF
open Frank.Builder
open Frank.LinkedData
open Frank.LinkedData.Tests.TestHelpers

/// #382: LinkedDataMiddleware.buildJsonLdResponse (and the Turtle/RDF-XML equivalents) used to
/// run the full expand→compact serialization pipeline on every request — deterministic per
/// (origin, mediaType) for the static Graph branch, so this is pure per-request waste. These
/// tests drive the middleware directly (no TestServer/Kestrel), constructing the HttpContext and
/// endpoint metadata by hand, so the internal StaticBodyBuildCount counter — incremented at the
/// exact point a body is actually (re)built — gives a deterministic, non-flaky proof of
/// build-once-per-(origin,mediaType).

let private makeContext (scheme: string) (host: string) (accept: string) (config: LinkedDataConfig) : HttpContext =
    let ctx = new DefaultHttpContext()
    ctx.Request.Method <- "GET"
    ctx.Request.Scheme <- scheme
    ctx.Request.Host <- HostString host
    ctx.Request.Path <- PathString "/data"
    ctx.Request.Headers.Add("Accept", StringValues accept)
    ctx.Response.Body <- new MemoryStream()
    let metadata = EndpointMetadataCollection([ box config ])

    let endpoint =
        Endpoint(RequestDelegate(fun _ -> Task.CompletedTask), metadata, "test")

    ctx.SetEndpoint(endpoint)
    ctx :> HttpContext

let private invoke (middleware: LinkedDataMiddleware) (ctx: HttpContext) : int =
    middleware.InvokeAsync(ctx).GetAwaiter().GetResult()
    ctx.Response.StatusCode

let private readResponseBody (ctx: HttpContext) : string =
    ctx.Response.Body.Position <- 0L
    use reader = new StreamReader(ctx.Response.Body)
    reader.ReadToEnd()

let private newMiddleware () =
    let next =
        RequestDelegate(fun ctx ->
            ctx.Response.StatusCode <- 200
            Task.CompletedTask)

    LinkedDataMiddleware(
        next,
        NullLogger<LinkedDataMiddleware>.Instance,
        LinkedDataVocabularyConfig.None,
        newBoundedMemoryCache ()
    )

[<Tests>]
let tests =
    testList
        "LinkedDataMiddleware static-graph body memoization (#382)"
        [ testCase "5 ld+json requests to the same origin build the body exactly once"
          <| fun _ ->
              let middleware = newMiddleware ()

              for _ in 1..5 do
                  invoke middleware (makeContext "http" "example.com" "application/ld+json" sampleConfig)
                  |> ignore

              Expect.equal
                  middleware.StaticBodyBuildCount
                  1
                  "same origin+mediaType repeated 5x ⇒ body built exactly once, not once per request"

          testCase "a second, distinct origin triggers exactly one additional build"
          <| fun _ ->
              let middleware = newMiddleware ()

              invoke middleware (makeContext "http" "example.com" "application/ld+json" sampleConfig)
              |> ignore

              invoke middleware (makeContext "http" "other.example" "application/ld+json" sampleConfig)
              |> ignore

              invoke middleware (makeContext "http" "other.example" "application/ld+json" sampleConfig)
              |> ignore

              Expect.equal
                  middleware.StaticBodyBuildCount
                  2
                  "two distinct origins ⇒ two builds total, regardless of repeat requests to each"

          testCase "turtle and ld+json for the same origin are cached independently (per-mediaType key)"
          <| fun _ ->
              let middleware = newMiddleware ()

              invoke middleware (makeContext "http" "example.com" "application/ld+json" sampleConfig)
              |> ignore

              invoke middleware (makeContext "http" "example.com" "text/turtle" sampleConfig)
              |> ignore

              invoke middleware (makeContext "http" "example.com" "application/ld+json" sampleConfig)
              |> ignore

              invoke middleware (makeContext "http" "example.com" "text/turtle" sampleConfig)
              |> ignore

              Expect.equal
                  middleware.StaticBodyBuildCount
                  2
                  "one build per (origin,mediaType) pair — turtle and ld+json don't share a cache entry"

          testCase "repeat requests to the same origin serve byte-identical ld+json bodies (no behavioral change)"
          <| fun _ ->
              let middleware = newMiddleware ()

              let ctx1 = makeContext "http" "example.com" "application/ld+json" sampleConfig
              let sc1 = invoke middleware ctx1
              let body1 = readResponseBody ctx1

              let ctx2 = makeContext "http" "example.com" "application/ld+json" sampleConfig
              let sc2 = invoke middleware ctx2
              let body2 = readResponseBody ctx2

              Expect.equal sc1 200 "first request served"
              Expect.equal sc2 200 "second (cached) request served"
              Expect.equal body2 body1 "cached static body must be byte-identical across repeat requests"

          testCase "a different origin gets a distinct body (own @base), not the first origin's cached one"
          <| fun _ ->
              let middleware = newMiddleware ()

              let ctxA = makeContext "http" "example.com" "application/ld+json" sampleConfig
              invoke middleware ctxA |> ignore
              let bodyA = readResponseBody ctxA

              let ctxB = makeContext "http" "other.example" "application/ld+json" sampleConfig
              invoke middleware ctxB |> ignore
              let bodyB = readResponseBody ctxB

              Expect.stringContains bodyB "other.example" "second origin's @base cites its own host"

              Expect.isFalse
                  (bodyB.Contains "example.com")
                  "second origin's body must not leak the first origin's @base"

              Expect.notEqual bodyB bodyA "distinct origins ⇒ distinct bodies, not a stale cache hit"

          testCase "GraphFactory (dynamic) branch is NEVER cached: factory runs once per request"
          <| fun _ ->
              let middleware = newMiddleware ()
              let mutable factoryCalls = 0

              let dynamicConfig =
                  { sampleConfig with
                      GraphFactory =
                          Some(fun _ ->
                              factoryCalls <- factoryCalls + 1
                              buildFixtureGraph ()) }

              for _ in 1..5 do
                  invoke middleware (makeContext "http" "example.com" "application/ld+json" dynamicConfig)
                  |> ignore

              Expect.equal factoryCalls 5 "GraphFactory must run on every request, never memoized"
              Expect.equal middleware.StaticBodyBuildCount 0 "dynamic branch must never touch the static-body cache" ]
