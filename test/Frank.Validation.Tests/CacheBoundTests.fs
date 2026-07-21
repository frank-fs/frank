module Frank.Validation.Tests.CacheBoundTests

open System
open System.IO
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging.Abstractions
open Expecto
open Frank.Validation

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

let private newMiddleware () =
    let next =
        RequestDelegate(fun ctx ->
            ctx.Response.StatusCode <- 200
            Task.CompletedTask)

    ValidationMiddleware(next, hostRelativeConfig (), NullLogger<ValidationMiddleware>.Instance)

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
                  Frank.BoundedCache.DefaultCapacity
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
                  "3 origins retained, nowhere near the capacity ceiling" ]
