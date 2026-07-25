/// #468 expert-review [FOWLER-IMPORTANT]: CacheStriping.getOrBuild's
/// `cache.TryGetValue(box key)` boxes `StaticBodyCacheKey` (a `[<Struct>]` record) on EVERY
/// call to cachedStaticBody, hit or miss — inherent to IMemoryCache's `object`-keyed API
/// (Microsoft.Extensions.Caching.Memory.IMemoryCache is not generic over key type), not
/// eliminable without diverging from constructor-injected IMemoryCache (this issue's core
/// requirement). Rather than leaving this unmeasured, this test MEASURES and DOCUMENTS the
/// cost: warmup performs ONE cache MISS (populating the entry), then `iterations` repeated
/// calls to the SAME (config, origin, mediaType) key are guaranteed HITS. Drives
/// LinkedDataMiddleware.ComputeStaticBodyForTest directly (bypassing HTTP
/// request/response plumbing entirely — ctx is never touched on the GraphFactory=None
/// branch) so the measured floor isolates cachedStaticBody's own cost, not noise from
/// DefaultHttpContext construction or response writing.
module Frank.LinkedData.Tests.AllocationTests

open Expecto
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging.Abstractions
open Frank.LinkedData
open Frank.TestSupport.AllocationHarness
open Frank.LinkedData.Tests.TestHelpers

let private newMiddlewareForAlloc () =
    LinkedDataMiddleware(
        RequestDelegate(fun _ -> System.Threading.Tasks.Task.CompletedTask),
        NullLogger<LinkedDataMiddleware>.Instance,
        LinkedDataVocabularyConfig.None,
        Frank.Builder.newBoundedMemoryCache ()
    )

[<Tests>]
let tests =
    testList
        "LinkedDataMiddleware static-body cache-HIT allocation floor (#468 Fowler-important)"
        [ testCase
              "repeated hits to the SAME (config,origin,mediaType) key allocate a small, stable, explained floor per call"
          <| fun _ ->
              let middleware = newMiddlewareForAlloc ()

              // warmup: exactly ONE cache MISS, populating the entry for this key.
              let warmup () =
                  middleware.ComputeStaticBodyForTest("application/ld+json", "http://alloc-test.example", sampleConfig)
                  |> ignore

              // request: repeated calls to the IDENTICAL key — guaranteed cache HITS.
              let request () =
                  middleware.ComputeStaticBodyForTest("application/ld+json", "http://alloc-test.example", sampleConfig)
                  |> ignore

              let deltas = measureAllocationDeltas warmup request 50

              // The floor this documents (measured against the real net10.0
              // Microsoft.Extensions.Caching.Memory assembly, on this machine: a rock-steady
              // 144 bytes/call across all 50 repeats — no growth trend, confirming this is a
              // genuine fixed floor, not a leak): the boxed StaticBodyCacheKey struct
              // (Config reference + Origin string ref + MediaType string ref, plus object
              // header) PLUS the two small per-call closures cachedStaticBody/computeBody
              // construct as CacheStriping.getOrBuild's `build` argument (allocated whether
              // or not the cache ends up invoking them — F# does not defer closure
              // construction based on whether the closure is later called). IMemoryCache's
              // own per-hit bookkeeping (LastAccessed touch) allocates nothing measurable on
              // top of that. Bounded at 2x the observed floor so ordinary JIT/GC noise across
              // machines doesn't flake this test, while still catching a REAL regression
              // (e.g. a reintroduced tuple allocation, or losing the cache hit and falling
              // through to a full rebuild — either would be several times this floor, not a
              // marginal increase).
              let flooredCeilingBytes = 288L

              Expect.all
                  deltas
                  (fun delta -> delta >= 0L && delta <= flooredCeilingBytes)
                  $"cache-HIT allocation per call must stay at the small, explained floor (boxed key + two small closures, IMemoryCache's own per-hit bookkeeping allocates nothing) — got %A{deltas}, ceiling={flooredCeilingBytes}" ]
