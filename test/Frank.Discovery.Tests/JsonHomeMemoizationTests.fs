module Frank.Discovery.Tests.JsonHomeMemoizationTests

open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Primitives
open Expecto
open Frank.Builder
open Frank.Discovery
open Frank.Discovery.Tests.TestHelpers

/// Mirrors AlpsMemoizationTests.fs (#398 /simplify item 6), applied to JSON Home's
/// resources (Relation IRI + HrefVars meaning IRIs — the fix this file drives): they must
/// be resolved against the live request origin exactly once per distinct origin, not once
/// per request. Drives the middleware directly (no TestServer/Kestrel) via the internal
/// ResolvedHomeBuildCount counter, same deterministic-non-flaky approach as ALPS.

let private declaredOnlyHomeEndpoints =
    Frank.Builder.ResourceEndpointDataSource(
        [| routeEndpoint
               "/games/{id}"
               [| "GET" |]
               [ box ({ Relation = "https://tictactoe.invalid/ex#Game" }: ResourceRelationMetadata) ] |]
    )

/// A relative, app-owned relation/href-vars pair (via classIriHrefMap's #415 relativization
/// and a relative meaning IRI) — the case whose resolved value actually differs per origin,
/// so the "distinct origin ⇒ distinct resolved body" test below can observe a real
/// difference (mirrors AlpsMemoizationTests.relativeHrefConfig).
let private declaredOnlyHomeConfig: DiscoveryConfig =
    { ProfileUri = "/alps/test"
      HomeRoute = "/"
      AlpsDescriptors =
        [ { Id = "Game"
            Type = "semantic"
            Doc = None
            Href = Some "/ex#Game"
            Descriptors = []
            Rt = None
            ClassIri = Some "https://tictactoe.invalid/ex#Game"
            RequestClrTypeName = None } ]
      DescribedByLinks = []
      ResourceHrefVars = Map.ofList [ "https://tictactoe.invalid/ex#Game", Map.ofList [ "id", "/ex#identifier" ] ] }

let private makeJsonHomeContext (scheme: string) (host: string) : HttpContext =
    let ctx = new DefaultHttpContext()
    ctx.Request.Method <- "GET"
    ctx.Request.Scheme <- scheme
    ctx.Request.Host <- HostString host
    ctx.Request.Path <- PathString declaredOnlyHomeConfig.HomeRoute
    ctx.Request.Headers.["Accept"] <- StringValues("application/json-home")
    ctx.Response.Body <- new MemoryStream()
    ctx :> HttpContext

let private invoke (middleware: DiscoveryMiddleware.DiscoveryMiddleware) (ctx: HttpContext) : int =
    middleware.Invoke(ctx).GetAwaiter().GetResult()
    ctx.Response.StatusCode

let private newMiddleware () =
    let next =
        RequestDelegate(fun ctx ->
            ctx.Response.StatusCode <- 200
            Task.CompletedTask)

    DiscoveryMiddleware.DiscoveryMiddleware(
        next,
        declaredOnlyHomeConfig,
        (declaredOnlyHomeEndpoints :> Microsoft.AspNetCore.Routing.EndpointDataSource),
        declaredOnlyHomeEndpoints,
        NullLogger<DiscoveryMiddleware.DiscoveryMiddleware>.Instance,
        newBoundedMemoryCache (),
        newBoundedMemoryCache ()
    )

let private readBody (ctx: HttpContext) =
    ctx.Response.Body.Position <- 0L
    use reader = new StreamReader(ctx.Response.Body)
    reader.ReadToEnd()

[<Tests>]
let tests =
    testList
        "DiscoveryMiddleware resolved-JSON-Home memoization (mirrors #398 /simplify item 6)"
        [ testCase "5 JSON Home requests to the same origin resolve resources exactly once"
          <| fun _ ->
              let middleware = newMiddleware ()

              for _ in 1..5 do
                  invoke middleware (makeJsonHomeContext "http" "example.com") |> ignore

              Expect.equal
                  middleware.ResolvedHomeBuildCount
                  1
                  "same origin repeated 5x ⇒ resources resolved exactly once, not once per request"

          testCase "a second, distinct origin triggers exactly one additional resolution"
          <| fun _ ->
              let middleware = newMiddleware ()

              invoke middleware (makeJsonHomeContext "http" "example.com") |> ignore
              invoke middleware (makeJsonHomeContext "http" "other.example") |> ignore
              invoke middleware (makeJsonHomeContext "http" "other.example") |> ignore

              Expect.equal
                  middleware.ResolvedHomeBuildCount
                  2
                  "two distinct origins ⇒ two resolutions total, regardless of repeat requests to each"

          testCase "repeat requests to the same origin serve byte-identical JSON Home bodies (no behavioral change)"
          <| fun _ ->
              let middleware = newMiddleware ()

              let ctx1 = makeJsonHomeContext "http" "example.com"
              let sc1 = invoke middleware ctx1
              let body1 = readBody ctx1

              let ctx2 = makeJsonHomeContext "http" "example.com"
              let sc2 = invoke middleware ctx2
              let body2 = readBody ctx2

              Expect.equal sc1 200 "first request served"
              Expect.equal sc2 200 "second (cached) request served"
              Expect.equal body2 body1 "cached resolved JSON Home body must be byte-identical across repeat requests"

          testCase "a different origin gets its own resolved relation/href-vars, not the first origin's cached ones"
          <| fun _ ->
              let middleware = newMiddleware ()

              let ctxA = makeJsonHomeContext "http" "example.com"
              invoke middleware ctxA |> ignore
              let bodyA = readBody ctxA

              let ctxB = makeJsonHomeContext "http" "other.example"
              invoke middleware ctxB |> ignore
              let bodyB = readBody ctxB

              Expect.stringContains bodyA "example.com" "first origin's resolved relation cites its own host"
              Expect.stringContains bodyB "other.example" "second origin's resolved relation cites its own host"

              Expect.isFalse
                  (bodyB.Contains "example.com")
                  "second origin's body must not leak the first origin's resolved relation"

              Expect.notEqual bodyB bodyA "distinct origins ⇒ distinct resolved bodies, not a stale cache hit"

          testCase "a malformed Host header is rejected with 400, never a garbage-but-valid resolved IRI"
          <| fun _ ->
              let middleware = newMiddleware ()
              let ctx = makeJsonHomeContext "http" "ex ample.com"
              let status = invoke middleware ctx
              Expect.equal status 400 "malformed Host header ⇒ 400, not 500 or a garbage resolved body" ]
