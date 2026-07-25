module Frank.Validation.Tests.CacheBoundTests

open System
open System.IO
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging.Abstractions
open Expecto
open Frank.Builder
open Frank.Validation
open Frank.Validation.Tests.MiddlewareTestHelpers

/// #405 (extended to Validation, per adversarial review of the #422 cluster): an
/// unauthenticated client varying the Host header mints one permanent entry per distinct
/// origin in hostRelativeShapesCache — unbounded before this fix, the SAME defect #405
/// already closed for DiscoveryMiddleware's resolvedAlpsCache/resolvedHomeResourcesCache
/// and LinkedDataMiddleware's staticBodyCache. These tests drive the middleware directly
/// (no TestServer/Kestrel) with a flood of syntactically-valid, distinct Host header
/// values and assert the cache SIZE (not just build count) plateaus at a hard ceiling,
/// mirroring Frank.Discovery.Tests.CacheBoundTests.fs /
/// Frank.LinkedData.Tests.CacheBoundTests.fs exactly (Constitution #8: same fix, same
/// shape of proof, applied uniformly to a third middleware).

let private offlineLoader = JsonLdLoader.synthesizing [ "https://schema.org/" ]

let private hostRelativeConfig () : ValidationConfig =
    { Shapes = Shapes.toShapesGraph []
      ContextLoader = offlineLoader
      MaxBodyBytes = ValidationConfig.defaultMaxBodyBytes
      HostRelativeProperties = [ Uri "https://schema.org/MoveAction", "/tictactoe#square", None ] }

/// Missing the required host-relative "tictactoe#square" property — triggers a SHACL
/// MinCount violation (422), but critically still exercises getOrBuildShapesGraph BEFORE
/// that verdict is known, so it still touches/builds the per-origin cache entry under test.
let private body =
    """{
  "@context": "https://schema.org",
  "@type": "MoveAction",
  "@id": "https://example.org/move/1"
}"""

let private makeContext (scheme: string) (host: string) : HttpContext =
    let ctx = new DefaultHttpContext()
    ctx.Request.Method <- "POST"
    ctx.Request.Scheme <- scheme
    ctx.Request.Host <- HostString host
    ctx.Request.Path <- PathString "/echo"
    ctx.Request.ContentType <- "application/ld+json"
    let bytes = Encoding.UTF8.GetBytes body
    ctx.Request.Body <- new MemoryStream(bytes)
    ctx.Request.ContentLength <- Nullable(int64 bytes.Length)
    ctx.Response.Body <- new MemoryStream()
    ctx :> HttpContext

let private invoke (middleware: ValidationMiddleware) (ctx: HttpContext) : int =
    middleware.InvokeAsync(ctx).GetAwaiter().GetResult()
    ctx.Response.StatusCode

/// SHACL Report blank-node IDs (e.g. "_:-1113320198") are minted fresh on every
/// ShapesGraph.Validate call regardless of caching — the ShapesGraph itself is the cached
/// artifact, not the report — so a stampede test comparing raw report bodies across racers
/// would see spurious differences unrelated to this fix. Normalizing isolates the
/// substantive violation content a stale/torn cache WOULD corrupt.
let private normalizeBlankNodes (body: string) : string =
    Text.RegularExpressions.Regex.Replace(body, "_:-?[0-9]+", "_:BNODE")

let private newMiddleware () =
    let next =
        RequestDelegate(fun ctx ->
            ctx.Response.StatusCode <- 200
            Task.CompletedTask)

    ValidationMiddleware(
        next,
        hostRelativeConfig (),
        NullLogger<ValidationMiddleware>.Instance,
        newBoundedMemoryCache ()
    )

[<Tests>]
let tests =
    testList
        "ValidationMiddleware host-relative-shapes cache bounding (#405)"
        [ testCase "10,000+ distinct Host headers plateau the cache at a hard ceiling, not unbounded growth (AC1)"
          <| fun _ ->
              let middleware = newMiddleware ()

              for i in 1..10_000 do
                  invoke middleware (makeContext "http" $"host-{i}.example") |> ignore

              Expect.isLessThanOrEqual
                  middleware.HostRelativeShapesCacheSize
                  Frank.Builder.CacheCapacity
                  "10,000 distinct Host headers must not grow the cache past its configured hard ceiling"

              Expect.isGreaterThan
                  middleware.HostRelativeShapesCacheSize
                  0
                  "sanity: the cache isn't accidentally empty/broken — it did retain entries, just bounded"

          testCase
              "a small set of 3 legitimate origins repeated many times still builds exactly once each, unaffected by bounding (AC2)"
          <| fun _ ->
              let middleware = newMiddleware ()
              let origins = [ "a.example"; "b.example"; "c.example" ]

              for _ in 1..500 do
                  for origin in origins do
                      invoke middleware (makeContext "http" origin) |> ignore

              Expect.equal
                  middleware.HostRelativeShapesBuildCount
                  3
                  "3 legitimate origins, 500 requests each ⇒ built exactly 3 times total — the bounding fix does not weaken build-once-per-origin memoization for a real deployment's small host set"

              Expect.equal
                  middleware.HostRelativeShapesCacheSize
                  3
                  "3 origins retained, nowhere near the capacity ceiling"

          // #468 AT2 (second half): a legitimate key inserted once and then GENUINELY never
          // re-accessed while a sustained flood runs past capacity must eventually be
          // evicted like any other stale entry — proving the new IMemoryCache-based LRU
          // bounds memory rather than pinning entries forever (the complementary property to
          // AC2 above).
          testCase
              "a legitimate origin never re-accessed during a flood past capacity is eventually evicted and rebuilds on next access (AT2)"
          <| fun _ ->
              let middleware = newMiddleware ()

              invoke middleware (makeContext "http" "never-touched-again.example") |> ignore

              Expect.equal
                  middleware.HostRelativeShapesBuildCount
                  1
                  "the legitimate origin builds once on its first request"

              for i in 1..10_000 do
                  invoke middleware (makeContext "http" $"flood-{i}.example") |> ignore

              // Every one of the 10,000 flood origins is itself a guaranteed-fresh key, so
              // the flood alone contributes exactly 10,000 builds to this middleware-global
              // counter — isolate "was never-touched-again SPECIFICALLY rebuilt" as the
              // delta across the recheck request, not an absolute count.
              let buildCountAfterFlood = middleware.HostRelativeShapesBuildCount

              invoke middleware (makeContext "http" "never-touched-again.example") |> ignore

              Expect.equal
                  middleware.HostRelativeShapesBuildCount
                  (buildCountAfterFlood + 1)
                  "an origin never re-accessed during a sustained flood past capacity must eventually be evicted like any other stale entry — LRU bounds memory, it does not pin forever"

          // #468 AT4: 50+ concurrent racers on a FRESH origin (via Task.WhenAll, not
          // sequential awaits) must build the host-relative ShapesGraph exactly once and
          // every caller must observe the same resulting 422 report body.
          testCase
              "50+ concurrent requests to a fresh origin build the ShapesGraph exactly once and all observe the same report (AT4)"
          <| fun _ ->
              let middleware = newMiddleware ()

              let readBody (ctx: HttpContext) =
                  ctx.Response.Body.Position <- 0L
                  use reader = new StreamReader(ctx.Response.Body)
                  reader.ReadToEnd()

              let racer () =
                  Task.Run(fun () ->
                      let ctx = makeContext "http" "stampede.example"
                      invoke middleware ctx |> ignore
                      readBody ctx)

              let tasks = [| for _ in 1..50 -> racer () |]
              Task.WaitAll(tasks |> Array.map (fun t -> t :> Task))

              let distinctBodies =
                  tasks |> Array.map (fun t -> normalizeBlankNodes t.Result) |> Array.distinct

              Expect.equal
                  middleware.HostRelativeShapesBuildCount
                  1
                  "50 concurrent racers on the SAME fresh origin must build the host-relative ShapesGraph exactly once"

              Expect.equal
                  distinctBodies.Length
                  1
                  "every one of the 50 concurrent callers must observe the SAME 422 report body (modulo blank-node IDs, which vary per Validate call regardless of caching) — no torn/inconsistent build" ]
