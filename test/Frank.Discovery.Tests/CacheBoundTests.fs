module Frank.Discovery.Tests.CacheBoundTests

open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging.Abstractions
open Expecto
open Frank.Discovery
open Frank.Discovery.Tests.TestHelpers

/// #405: an unauthenticated client varying the Host header mints one permanent cache entry
/// per distinct origin in resolvedAlpsCache/resolvedHomeResourcesCache — unbounded before
/// this fix. These tests drive the middleware directly (no TestServer/Kestrel) with a
/// flood of syntactically-valid, distinct Host header values and assert the cache SIZE
/// (not just build count) plateaus at a hard ceiling, per AC1's literal load-test shape.

let private emptyEndpoints =
    Frank.Builder.ResourceEndpointDataSource([||]) :> Microsoft.AspNetCore.Routing.EndpointDataSource

let private emptyResourceEndpoints = Frank.Builder.ResourceEndpointDataSource([||])

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
        emptyResourceEndpoints,
        NullLogger<DiscoveryMiddleware.DiscoveryMiddleware>.Instance
    )

[<Tests>]
let tests =
    testList
        "DiscoveryMiddleware resolved-cache bounding (#405)"
        [ testCase
              "10,000+ distinct Host headers against the ALPS profile plateau the cache at a hard ceiling, not unbounded growth (AC1)"
          <| fun _ ->
              let middleware = newMiddleware sampleConfig

              for i in 1..10_000 do
                  invoke middleware (makeContext "http" $"host-{i}.example" sampleConfig.ProfileUri)
                  |> ignore

              Expect.isLessThanOrEqual
                  middleware.ResolvedAlpsCacheSize
                  Frank.BoundedCache.DefaultCapacity
                  "10,000 distinct Host headers must not grow the cache past its configured hard ceiling"

              Expect.isGreaterThan
                  middleware.ResolvedAlpsCacheSize
                  0
                  "sanity: the cache isn't accidentally empty/broken — it did retain entries, just bounded"

          testCase
              "10,000+ distinct Host headers against JSON Home plateau the cache at a hard ceiling, not unbounded growth (AC1)"
          <| fun _ ->
              let middleware = newMiddleware sampleConfig

              for i in 1..10_000 do
                  let ctx = makeContext "http" $"host-{i}.example" sampleConfig.HomeRoute

                  ctx.Request.Headers.["Accept"] <-
                      Microsoft.Extensions.Primitives.StringValues("application/json-home")

                  invoke middleware ctx |> ignore

              Expect.isLessThanOrEqual
                  middleware.ResolvedHomeCacheSize
                  Frank.BoundedCache.DefaultCapacity
                  "10,000 distinct Host headers must not grow the JSON Home cache past its configured hard ceiling"

          testCase
              "a small set of 3 legitimate origins repeated many times still builds exactly once each, unaffected by bounding (AC2)"
          <| fun _ ->
              let middleware = newMiddleware sampleConfig
              let origins = [ "a.example"; "b.example"; "c.example" ]

              for _ in 1..500 do
                  for origin in origins do
                      invoke middleware (makeContext "http" origin sampleConfig.ProfileUri) |> ignore

              Expect.equal
                  middleware.ResolvedAlpsBuildCount
                  3
                  "3 legitimate origins, 500 requests each ⇒ built exactly 3 times total — the bounding fix does not weaken build-once-per-origin memoization for a real deployment's small host set"

              Expect.equal middleware.ResolvedAlpsCacheSize 3 "3 origins retained, nowhere near the capacity ceiling" ]
