module Frank.LinkedData.Tests.CacheBoundTests

open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Primitives
open Expecto
open Frank.LinkedData
open Frank.LinkedData.Tests.TestHelpers

/// #405: an unauthenticated client varying the Host header mints one permanent entry per
/// distinct (origin, mediaType) pair in staticBodyCache — unbounded before this fix. These
/// tests drive the middleware directly (no TestServer/Kestrel) with a flood of
/// syntactically-valid, distinct Host header values and assert the cache SIZE (not just
/// build count) plateaus at a hard ceiling, per AC1's literal load-test shape — mirroring
/// Frank.Discovery.Tests.CacheBoundTests.fs exactly (Constitution #8: same fix, same shape
/// of proof, applied uniformly to both middlewares).

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

let private newMiddleware () =
    let next =
        RequestDelegate(fun ctx ->
            ctx.Response.StatusCode <- 200
            Task.CompletedTask)

    LinkedDataMiddleware(next, NullLogger<LinkedDataMiddleware>.Instance, LinkedDataVocabularyConfig.None)

[<Tests>]
let tests =
    testList
        "LinkedDataMiddleware static-body cache bounding (#405)"
        [ testCase "10,000+ distinct Host headers plateau the cache at a hard ceiling, not unbounded growth (AC1)"
          <| fun _ ->
              let middleware = newMiddleware ()

              for i in 1..10_000 do
                  invoke middleware (makeContext "http" $"host-{i}.example" "application/ld+json" sampleConfig)
                  |> ignore

              let size = middleware.StaticBodyCacheSizeFor sampleConfig

              Expect.isLessThanOrEqual
                  size
                  Frank.BoundedCache.DefaultCapacity
                  "10,000 distinct Host headers must not grow the cache past its configured hard ceiling"

              Expect.isGreaterThan
                  size
                  0
                  "sanity: the cache isn't accidentally empty/broken — it did retain entries, just bounded"

          testCase
              "a small set of 3 legitimate origins repeated many times still builds exactly once each, unaffected by bounding (AC2)"
          <| fun _ ->
              let middleware = newMiddleware ()
              let origins = [ "a.example"; "b.example"; "c.example" ]

              for _ in 1..500 do
                  for origin in origins do
                      invoke middleware (makeContext "http" origin "application/ld+json" sampleConfig)
                      |> ignore

              Expect.equal
                  middleware.StaticBodyBuildCount
                  3
                  "3 legitimate origins, 500 requests each ⇒ built exactly 3 times total — the bounding fix does not weaken build-once-per-origin memoization for a real deployment's small host set"

              Expect.equal
                  (middleware.StaticBodyCacheSizeFor sampleConfig)
                  3
                  "3 (origin,mediaType) entries retained, nowhere near the capacity ceiling" ]
