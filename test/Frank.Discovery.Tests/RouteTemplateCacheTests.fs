module Frank.Discovery.Tests.RouteTemplateCacheTests

open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging.Abstractions
open Expecto
open Frank.Discovery
open Frank.Discovery.Tests.TestHelpers

/// #421: handleOptions/relationsForPath re-parsed every registered route template's
/// TemplateParser.Parse on EVERY call, violating this project's own documented rule
/// (src/CLAUDE.md: cache immutable RouteTemplate objects, construct TemplateMatcher fresh
/// per match). These tests drive the middleware directly (no TestServer/Kestrel), so the
/// internal RouteTemplateParseCount counter — incremented at the exact point each
/// endpoint's RouteTemplate is actually parsed — gives a deterministic, non-flaky proof of
/// parse-once-per-endpoint-at-cache-build-time, mirroring AlpsMemoizationTests.fs's pattern.

let private threeEndpoints: Endpoint[] =
    [| routeEndpoint
           "/games/{id}"
           [| "GET" |]
           [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata) ]
       routeEndpoint "/widgets/{id}" [| "PUT" |] []
       routeEndpoint "/gadgets/{id}" [| "DELETE" |] [] |]

let private makeOptionsContext (path: string) : HttpContext =
    let ctx = new DefaultHttpContext()
    ctx.Request.Method <- "OPTIONS"
    ctx.Request.Scheme <- "http"
    ctx.Request.Host <- HostString "example.com"
    ctx.Request.Path <- PathString path
    ctx.Response.Body <- new MemoryStream()
    ctx :> HttpContext

let private newMiddleware (endpoints: Endpoint[]) =
    let dataSource = Frank.Builder.ResourceEndpointDataSource(endpoints)

    let next =
        RequestDelegate(fun ctx ->
            ctx.Response.StatusCode <- 200
            Task.CompletedTask)

    DiscoveryMiddleware.DiscoveryMiddleware(
        next,
        DiscoveryConfig.Empty,
        dataSource :> Microsoft.AspNetCore.Routing.EndpointDataSource,
        dataSource,
        NullLogger<DiscoveryMiddleware.DiscoveryMiddleware>.Instance,
        newBoundedMemoryCache (),
        newBoundedMemoryCache ()
    )

let private invoke (middleware: DiscoveryMiddleware.DiscoveryMiddleware) (ctx: HttpContext) : unit =
    middleware.Invoke(ctx).GetAwaiter().GetResult()

[<Tests>]
let tests =
    testList
        "DiscoveryMiddleware route-template parse caching (#421)"
        [ testCase "5 OPTIONS requests to the same path parse each endpoint's route template exactly once, not 5x"
          <| fun _ ->
              let middleware = newMiddleware threeEndpoints

              for _ in 1..5 do
                  invoke middleware (makeOptionsContext "/games/42")

              Expect.equal
                  middleware.RouteTemplateParseCount
                  3
                  "3 endpoints parsed once at cache-build time, regardless of 5 OPTIONS requests to the same path"

          testCase
              "a single OPTIONS request needing both Allow (methodsForPath) and rel=type (relationsForPath) parses templates only once, not twice"
          <| fun _ ->
              let middleware = newMiddleware threeEndpoints
              invoke middleware (makeOptionsContext "/games/42")

              Expect.equal
                  middleware.RouteTemplateParseCount
                  3
                  "methodsForPath and relationsForPath share the same cached RouteTemplate set — one request needing both doesn't parse twice"

          testCase "OPTIONS requests to different matching paths still parse each endpoint's template only once, total"
          <| fun _ ->
              let middleware = newMiddleware threeEndpoints
              invoke middleware (makeOptionsContext "/games/42")
              invoke middleware (makeOptionsContext "/widgets/7")
              invoke middleware (makeOptionsContext "/gadgets/9")

              Expect.equal
                  middleware.RouteTemplateParseCount
                  3
                  "cache is built once at construction/first-use, regardless of how many distinct paths are subsequently matched against it" ]
