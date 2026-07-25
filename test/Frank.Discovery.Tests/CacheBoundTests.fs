module Frank.Discovery.Tests.CacheBoundTests

open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.Logging.Abstractions
open Expecto
open Frank.Builder
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
        NullLogger<DiscoveryMiddleware.DiscoveryMiddleware>.Instance,
        newBoundedMemoryCache (),
        newBoundedMemoryCache ()
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
                  Frank.Builder.CacheCapacity
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
                  Frank.Builder.CacheCapacity
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

              Expect.equal middleware.ResolvedAlpsCacheSize 3 "3 origins retained, nowhere near the capacity ceiling"

          // #468 AT2 (second half): a legitimate origin inserted once and then GENUINELY
          // never re-accessed while a sustained flood runs past capacity must eventually be
          // evicted like any other stale entry — proving the new IMemoryCache-based LRU
          // bounds memory rather than pinning entries forever (the complementary property to
          // AC2 above).
          testCase
              "a legitimate origin never re-accessed during a flood past capacity is eventually evicted and rebuilds on next access (AT2, ALPS)"
          <| fun _ ->
              let middleware = newMiddleware sampleConfig

              invoke middleware (makeContext "http" "never-touched-again.example" sampleConfig.ProfileUri)
              |> ignore

              Expect.equal middleware.ResolvedAlpsBuildCount 1 "the legitimate origin builds once on its first request"

              for i in 1..10_000 do
                  invoke middleware (makeContext "http" $"flood-{i}.example" sampleConfig.ProfileUri)
                  |> ignore

              // Every one of the 10,000 flood origins is itself a guaranteed-fresh key, so
              // the flood alone contributes exactly 10,000 builds to this middleware-global
              // counter — isolate "was never-touched-again SPECIFICALLY rebuilt" as the
              // delta across the recheck request, not an absolute count.
              let buildCountAfterFlood = middleware.ResolvedAlpsBuildCount

              invoke middleware (makeContext "http" "never-touched-again.example" sampleConfig.ProfileUri)
              |> ignore

              Expect.equal
                  middleware.ResolvedAlpsBuildCount
                  (buildCountAfterFlood + 1)
                  "an origin never re-accessed during a sustained flood past capacity must eventually be evicted like any other stale entry — LRU bounds memory, it does not pin forever"

          // #468 AT2 (second half), mirrored for the JSON Home cache (Discovery's OTHER
          // independently-keyed cache) — the same eventual-eviction guarantee must hold per
          // cache, not just for resolvedAlpsCache.
          testCase
              "a legitimate origin never re-accessed during a flood past capacity is eventually evicted and rebuilds on next access (AT2, JSON Home)"
          <| fun _ ->
              let middleware = newMiddleware sampleConfig

              let makeHomeContext (host: string) =
                  let ctx = makeContext "http" host sampleConfig.HomeRoute

                  ctx.Request.Headers.["Accept"] <-
                      Microsoft.Extensions.Primitives.StringValues("application/json-home")

                  ctx

              invoke middleware (makeHomeContext "never-touched-again.example") |> ignore

              Expect.equal middleware.ResolvedHomeBuildCount 1 "the legitimate origin builds once on its first request"

              for i in 1..10_000 do
                  invoke middleware (makeHomeContext $"flood-{i}.example") |> ignore

              // Every one of the 10,000 flood origins is itself a guaranteed-fresh key, so
              // the flood alone contributes exactly 10,000 builds to this middleware-global
              // counter — isolate "was never-touched-again SPECIFICALLY rebuilt" as the
              // delta across the recheck request, not an absolute count.
              let buildCountAfterFlood = middleware.ResolvedHomeBuildCount

              invoke middleware (makeHomeContext "never-touched-again.example") |> ignore

              Expect.equal
                  middleware.ResolvedHomeBuildCount
                  (buildCountAfterFlood + 1)
                  "an origin never re-accessed during a sustained flood past capacity must eventually be evicted like any other stale entry — LRU bounds memory, it does not pin forever"

          // #468 AT3: "touched key survives eviction" proven against the NEW IMemoryCache
          // mechanism via an EXPLICIT, SYNCHRONOUS MemoryCache.Compact call, never automatic
          // SizeLimit-triggered background compaction (thread-pool-queued, not
          // deterministically observable in a single-threaded test).
          testCase
              "a touched origin survives an explicit synchronous Compact call; the evicted origin is untouched, not the touched one (AT3)"
          <| fun _ ->
              let alpsCache = newBoundedMemoryCache ()

              let next =
                  RequestDelegate(fun ctx ->
                      ctx.Response.StatusCode <- 200
                      Task.CompletedTask)

              let middleware =
                  DiscoveryMiddleware.DiscoveryMiddleware(
                      next,
                      sampleConfig,
                      emptyEndpoints,
                      emptyResourceEndpoints,
                      NullLogger<DiscoveryMiddleware.DiscoveryMiddleware>.Instance,
                      alpsCache,
                      newBoundedMemoryCache ()
                  )

              // Fill to EXACTLY capacity with distinct origins K1..Kn — K1 = "origin-1.example".
              for i in 1 .. Frank.Builder.CacheCapacity do
                  invoke middleware (makeContext "http" $"origin-{i}.example" sampleConfig.ProfileUri)
                  |> ignore

              let buildCountAfterFill = middleware.ResolvedAlpsBuildCount

              // Re-access K1 (a cache HIT — must NOT rebuild).
              invoke middleware (makeContext "http" "origin-1.example" sampleConfig.ProfileUri)
              |> ignore

              Expect.equal
                  middleware.ResolvedAlpsBuildCount
                  buildCountAfterFill
                  "re-requesting origin-1 immediately after the fill is a cache HIT, not a rebuild"

              // One further NEW origin, over capacity (SizeLimit rejects it synchronously).
              invoke middleware (makeContext "http" "origin-overflow.example" sampleConfig.ProfileUri)
              |> ignore

              // Explicit, SYNCHRONOUS compaction — the AT3-mandated mechanism.
              (alpsCache :?> MemoryCache).Compact(0.3)

              Expect.isLessThan
                  (alpsCache :?> MemoryCache).Count
                  Frank.Builder.CacheCapacity
                  "Compact(0.3) must actually evict SOME untouched entries — sanity that this test exercises real eviction, not a no-op"

              let buildCountBeforeRecheck = middleware.ResolvedAlpsBuildCount

              invoke middleware (makeContext "http" "origin-1.example" sampleConfig.ProfileUri)
              |> ignore

              Expect.equal
                  middleware.ResolvedAlpsBuildCount
                  buildCountBeforeRecheck
                  "K1 (touched immediately before the explicit Compact call) must still be retrievable afterward — a rebuild here means Compact wrongly evicted the touched key instead of an untouched one"

          // #468 AT4: 50+ concurrent racers on a FRESH origin (via Task.WhenAll, not
          // sequential awaits) must build exactly once and every caller must observe the
          // same resulting ALPS body — proves Frank.CacheStriping.getOrBuild's
          // double-checked-locking closes the gap plain IMemoryCache.GetOrCreate leaves open
          // (verified empirically: without striping, 50 racers on one missing key produced
          // ~10 separate factory invocations and ~10 distinct returned values).
          testCase "50+ concurrent requests to a fresh origin build exactly once and all observe the same body (AT4)"
          <| fun _ ->
              let middleware = newMiddleware sampleConfig

              let readBody (ctx: HttpContext) =
                  ctx.Response.Body.Position <- 0L
                  use reader = new StreamReader(ctx.Response.Body)
                  reader.ReadToEnd()

              let racer () =
                  Task.Run(fun () ->
                      let ctx = makeContext "http" "stampede.example" sampleConfig.ProfileUri
                      invoke middleware ctx |> ignore
                      readBody ctx)

              let tasks = [| for _ in 1..50 -> racer () |]
              Task.WaitAll(tasks |> Array.map (fun t -> t :> Task))
              let distinctBodies = tasks |> Array.map (fun t -> t.Result) |> Array.distinct

              Expect.equal
                  middleware.ResolvedAlpsBuildCount
                  1
                  "50 concurrent racers on the SAME fresh origin must build the resolved ALPS tree exactly once"

              Expect.equal
                  distinctBodies.Length
                  1
                  "every one of the 50 concurrent callers must observe the SAME resolved ALPS body — no torn/inconsistent build"

          // #468 AT4, mirrored for the JSON Home cache (Discovery's OTHER independently-keyed
          // cache) — "for all four caches" requires each cache's own single-flight guarantee
          // proven, not just one representative.
          testCase
              "50+ concurrent requests to a fresh origin build JSON Home exactly once and all observe the same body (AT4)"
          <| fun _ ->
              let middleware = newMiddleware sampleConfig

              let readBody (ctx: HttpContext) =
                  ctx.Response.Body.Position <- 0L
                  use reader = new StreamReader(ctx.Response.Body)
                  reader.ReadToEnd()

              let racer () =
                  Task.Run(fun () ->
                      let ctx = makeContext "http" "stampede-home.example" sampleConfig.HomeRoute

                      ctx.Request.Headers.["Accept"] <-
                          Microsoft.Extensions.Primitives.StringValues("application/json-home")

                      invoke middleware ctx |> ignore
                      readBody ctx)

              let tasks = [| for _ in 1..50 -> racer () |]
              Task.WaitAll(tasks |> Array.map (fun t -> t :> Task))
              let distinctBodies = tasks |> Array.map (fun t -> t.Result) |> Array.distinct

              Expect.equal
                  middleware.ResolvedHomeBuildCount
                  1
                  "50 concurrent racers on the SAME fresh origin must build the resolved JSON Home resources exactly once"

              Expect.equal
                  distinctBodies.Length
                  1
                  "every one of the 50 concurrent callers must observe the SAME resolved JSON Home body — no torn/inconsistent build" ]
