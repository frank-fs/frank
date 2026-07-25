module Frank.Validation.Tests.MemoizationTests

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

/// #382: ValidationMiddleware.HostRelative used to rebuild the SHACL ShapesGraph
/// (Shapes.toShapesGraph) on every ld+json request — deterministic per origin, so this is
/// pure per-request waste. These tests drive the middleware directly (no TestServer/Kestrel)
/// so the internal HostRelativeShapesBuildCount counter — incremented at the exact point a
/// ShapesGraph is actually (re)built — gives a deterministic, non-flaky proof of
/// build-once-per-origin, instead of a noise-prone GC-allocation measurement.

let private offlineLoader = JsonLdLoader.synthesizing [ "https://schema.org/" ]

let private hostRelativeConfig () : ValidationConfig =
    { Shapes = Shapes.toShapesGraph []
      ContextLoader = offlineLoader
      MaxBodyBytes = ValidationConfig.defaultMaxBodyBytes
      HostRelativeProperties = [ Uri "https://schema.org/MoveAction", "/tictactoe#square", None ] }

/// Body missing the required host-relative "tictactoe#square" property — triggers a SHACL
/// MinCount violation whose report cites the origin-resolved sh:path IRI, so two reports from
/// different origins are trivially distinguishable (no per-origin string interpolation needed).
let private missingSquareBody =
    """{
  "@context": "https://schema.org",
  "@type": "MoveAction",
  "@id": "https://example.org/move/1"
}"""

let private conformingBody (origin: string) =
    $$"""{
  "@context": "https://schema.org",
  "@type": "MoveAction",
  "@id": "https://example.org/move/1",
  "{{origin}}/tictactoe#square": {"@value": "TopLeft"}
}"""

let private makeContext (scheme: string) (host: string) (body: string) : HttpContext =
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
    ctx

let private invoke (middleware: ValidationMiddleware) (ctx: HttpContext) : int =
    middleware.InvokeAsync(ctx).GetAwaiter().GetResult()
    ctx.Response.StatusCode

let private readResponseBody (ctx: HttpContext) : string =
    ctx.Response.Body.Position <- 0L
    use reader = new StreamReader(ctx.Response.Body)
    reader.ReadToEnd()

/// SHACL Report blank-node IDs (e.g. "_:-1113320198") are minted fresh on every
/// `ShapesGraph.Validate` call regardless of caching — this is the underlying dotNetRDF
/// library's own behavior, orthogonal to the ShapesGraph-build memoization under test here.
/// Normalizing them isolates the part of the report that caching actually governs: the
/// substantive violation content (conforms/severity/message/resultPath), which IS deterministic
/// per origin and is what a stale-cache bug would corrupt.
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
        "ValidationMiddleware host-relative ShapesGraph memoization (#382)"
        [ testCase "5 requests to the same origin build the ShapesGraph exactly once"
          <| fun _ ->
              let middleware = newMiddleware ()

              for _ in 1..5 do
                  invoke middleware (makeContext "https" "example.com" (conformingBody "https://example.com"))
                  |> ignore

              Expect.equal
                  middleware.HostRelativeShapesBuildCount
                  1
                  "same origin repeated 5x ⇒ ShapesGraph built exactly once, not once per request"

          testCase "a second, distinct origin triggers exactly one additional build"
          <| fun _ ->
              let middleware = newMiddleware ()

              invoke middleware (makeContext "https" "example.com" (conformingBody "https://example.com"))
              |> ignore

              invoke middleware (makeContext "https" "other.example" (conformingBody "https://other.example"))
              |> ignore

              invoke middleware (makeContext "https" "other.example" (conformingBody "https://other.example"))
              |> ignore

              Expect.equal
                  middleware.HostRelativeShapesBuildCount
                  2
                  "two distinct origins ⇒ two builds total, regardless of repeat requests to each"

          testCase
              "repeat requests to the same origin serve identical 422 reports modulo blank-node IDs (no behavioral change)"
          <| fun _ ->
              let middleware = newMiddleware ()

              let ctx1 = makeContext "https" "example.com" missingSquareBody
              let sc1 = invoke middleware ctx1
              let body1 = readResponseBody ctx1

              let ctx2 = makeContext "https" "example.com" missingSquareBody
              let sc2 = invoke middleware ctx2
              let body2 = readResponseBody ctx2

              Expect.equal sc1 422 "first request: missing host-relative property ⇒ 422"
              Expect.equal sc2 422 "second (cached) request: same violation ⇒ 422"

              Expect.equal
                  (normalizeBlankNodes body2)
                  (normalizeBlankNodes body1)
                  "cached ShapesGraph must serve identical 422 report content (blank-node IDs vary per Validate call, not per cache state)"

          testCase "a different origin gets its own report, not the first origin's cached one"
          <| fun _ ->
              let middleware = newMiddleware ()

              let ctxA = makeContext "https" "example.com" missingSquareBody
              invoke middleware ctxA |> ignore
              let bodyA = readResponseBody ctxA

              let ctxB = makeContext "https" "other.example" missingSquareBody
              let scB = invoke middleware ctxB
              let bodyB = readResponseBody ctxB

              Expect.equal scB 422 "second origin: same violation shape ⇒ 422"
              Expect.stringContains bodyB "other.example" "second origin's report cites its own host-relative IRI"

              Expect.isFalse
                  (bodyB.Contains "example.com")
                  "second origin's report must not leak the first origin's IRI"

              Expect.notEqual
                  (normalizeBlankNodes bodyB)
                  (normalizeBlankNodes bodyA)
                  "distinct origins ⇒ distinct report bodies, not a stale cache hit" ]
