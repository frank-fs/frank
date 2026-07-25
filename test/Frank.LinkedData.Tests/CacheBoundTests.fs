module Frank.LinkedData.Tests.CacheBoundTests

open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Caching.Memory
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

    LinkedDataMiddleware(
        next,
        NullLogger<LinkedDataMiddleware>.Instance,
        LinkedDataVocabularyConfig.None,
        newBoundedMemoryCache ()
    )

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

              let size = middleware.StaticBodyCacheSize

              Expect.isLessThanOrEqual
                  size
                  Frank.Builder.CacheCapacity
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
                  middleware.StaticBodyCacheSize
                  3
                  "3 (origin,mediaType) entries retained, nowhere near the capacity ceiling"

          // #468 AT2 (second half): a legitimate (origin,mediaType) key inserted once and
          // then GENUINELY never re-accessed while a sustained flood runs past capacity must
          // eventually be evicted like any other stale entry — proving the new
          // IMemoryCache-based LRU bounds memory rather than pinning entries forever.
          testCase
              "a legitimate (origin,mediaType) entry never re-accessed during a flood past capacity is eventually evicted and rebuilds on next access (AT2)"
          <| fun _ ->
              let middleware = newMiddleware ()

              invoke middleware (makeContext "http" "never-touched-again.example" "application/ld+json" sampleConfig)
              |> ignore

              Expect.equal middleware.StaticBodyBuildCount 1 "the legitimate entry builds once on its first request"

              for i in 1..10_000 do
                  invoke middleware (makeContext "http" $"flood-{i}.example" "application/ld+json" sampleConfig)
                  |> ignore

              // Every one of the 10,000 flood (origin,mediaType) pairs is itself a
              // guaranteed-fresh key, so the flood alone contributes exactly 10,000 builds to
              // this middleware-global counter — isolate "was never-touched-again
              // SPECIFICALLY rebuilt" as the delta across the recheck request, not an
              // absolute count.
              let buildCountAfterFlood = middleware.StaticBodyBuildCount

              invoke middleware (makeContext "http" "never-touched-again.example" "application/ld+json" sampleConfig)
              |> ignore

              Expect.equal
                  middleware.StaticBodyBuildCount
                  (buildCountAfterFlood + 1)
                  "an entry never re-accessed during a sustained flood past capacity must eventually be evicted like any other stale entry — LRU bounds memory, it does not pin forever"

          // #468 AT4: 50+ concurrent racers on a FRESH origin (via Task.WhenAll, not
          // sequential awaits) must build the static body exactly once and every caller must
          // observe the same resulting body.
          testCase
              "50+ concurrent requests to a fresh origin build the static body exactly once and all observe the same body (AT4)"
          <| fun _ ->
              let middleware = newMiddleware ()

              let readBody (ctx: HttpContext) =
                  ctx.Response.Body.Position <- 0L
                  use reader = new StreamReader(ctx.Response.Body)
                  reader.ReadToEnd()

              let racer () =
                  Task.Run(fun () ->
                      let ctx = makeContext "http" "stampede.example" "application/ld+json" sampleConfig
                      invoke middleware ctx |> ignore
                      readBody ctx)

              let tasks = [| for _ in 1..50 -> racer () |]
              Task.WaitAll(tasks |> Array.map (fun t -> t :> Task))
              let distinctBodies = tasks |> Array.map (fun t -> t.Result) |> Array.distinct

              Expect.equal
                  middleware.StaticBodyBuildCount
                  1
                  "50 concurrent racers on the SAME fresh (origin,mediaType) must build the static body exactly once"

              Expect.equal
                  distinctBodies.Length
                  1
                  "every one of the 50 concurrent callers must observe the SAME static body — no torn/inconsistent build"

          // #468 AT3: LinkedData's nested/compound cache key (config identity + origin +
          // mediaType, StaticBodyCacheKey) is the structurally hardest case — this proves the
          // "touched key survives eviction" property against the NEW IMemoryCache mechanism
          // via an EXPLICIT, SYNCHRONOUS MemoryCache.Compact call, never automatic
          // SizeLimit-triggered background compaction (thread-pool-queued, not
          // deterministically observable in a single-threaded test).
          testCase
              "a touched entry survives an explicit synchronous Compact call; the evicted key is untouched, not the touched one (AT3)"
          <| fun _ ->
              let cache = newBoundedMemoryCache ()

              let next =
                  RequestDelegate(fun ctx ->
                      ctx.Response.StatusCode <- 200
                      Task.CompletedTask)

              let middleware =
                  LinkedDataMiddleware(
                      next,
                      NullLogger<LinkedDataMiddleware>.Instance,
                      LinkedDataVocabularyConfig.None,
                      cache
                  )

              // Fill to EXACTLY capacity with distinct origins K1..Kn, insertion order —
              // K1 = "origin-1.example".
              for i in 1 .. Frank.Builder.CacheCapacity do
                  invoke middleware (makeContext "http" $"origin-{i}.example" "application/ld+json" sampleConfig)
                  |> ignore

              let buildCountAfterFill = middleware.StaticBodyBuildCount

              // Re-access K1 (a cache HIT — must NOT rebuild).
              invoke middleware (makeContext "http" "origin-1.example" "application/ld+json" sampleConfig)
              |> ignore

              Expect.equal
                  middleware.StaticBodyBuildCount
                  buildCountAfterFill
                  "re-requesting origin-1 immediately after the fill is a cache HIT, not a rebuild"

              // One further NEW key, over capacity (SizeLimit rejects it synchronously — the
              // cache stays at exactly its prior Count; this is expected and not asserted on).
              invoke middleware (makeContext "http" "origin-overflow.example" "application/ld+json" sampleConfig)
              |> ignore

              // Explicit, SYNCHRONOUS compaction — the AT3-mandated mechanism.
              (cache :?> MemoryCache).Compact(0.3)

              Expect.isLessThan
                  (cache :?> MemoryCache).Count
                  Frank.Builder.CacheCapacity
                  "Compact(0.3) must actually evict SOME untouched entries — sanity that this test exercises real eviction, not a no-op"

              let buildCountBeforeRecheck = middleware.StaticBodyBuildCount

              invoke middleware (makeContext "http" "origin-1.example" "application/ld+json" sampleConfig)
              |> ignore

              Expect.equal
                  middleware.StaticBodyBuildCount
                  buildCountBeforeRecheck
                  "K1 (touched immediately before the explicit Compact call) must still be retrievable afterward — a rebuild here means Compact wrongly evicted the touched key instead of an untouched one" ]
