/// #468 AT5 (continued from Frank.Tests.CacheDIWiringTests): two independently-constructed
/// DiscoveryMiddleware instances, built via ActivatorUtilities.CreateInstance — the SAME
/// mechanism ASP.NET Core's UseMiddleware<T>() uses internally — must receive the IDENTICAL
/// keyed IMemoryCache reference and observably SHARE its state, not just resolve to
/// reference-equal-but-separately-tested instances. This is the DiscoveryMiddleware-specific
/// half of AT5; the generic "keyed registrations are non-null / singleton / independent"
/// proof lives in Frank.Tests.CacheDIWiringTests (which lacks InternalsVisibleTo access to
/// DiscoveryMiddleware's internal build-count hooks used here).
module Frank.Discovery.Tests.DiCacheSharingTests

open System
open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging.Abstractions
open Expecto
open Frank.Builder
open Frank.Discovery
open Frank.Discovery.Tests.TestHelpers

let private newServiceProvider () : IServiceProvider =
    let services = ServiceCollection()
    registerBoundedMemoryCaches services |> ignore
    services.BuildServiceProvider()

let private makeContext (scheme: string) (host: string) (path: string) : HttpContext =
    let ctx = new DefaultHttpContext()
    ctx.Request.Method <- "GET"
    ctx.Request.Scheme <- scheme
    ctx.Request.Host <- HostString host
    ctx.Request.Path <- PathString path
    ctx.Response.Body <- new MemoryStream()
    ctx :> HttpContext

[<Tests>]
let tests =
    testList
        "Two independently-constructed DiscoveryMiddleware instances share DI-resolved caches (#468 AT5)"
        [ testCase
              "middlewareB observes a cache HIT (0 builds) for an origin middlewareA already cached via the shared keyed IMemoryCache"
          <| fun _ ->
              let sp = newServiceProvider ()
              let emptyDataSource = Frank.Builder.ResourceEndpointDataSource([||])

              let next =
                  RequestDelegate(fun ctx ->
                      ctx.Response.StatusCode <- 200
                      Task.CompletedTask)

              let makeMiddleware () =
                  ActivatorUtilities.CreateInstance<DiscoveryMiddleware.DiscoveryMiddleware>(
                      sp,
                      next,
                      sampleConfig,
                      (emptyDataSource :> EndpointDataSource),
                      emptyDataSource,
                      NullLogger<DiscoveryMiddleware.DiscoveryMiddleware>.Instance
                  )

              let middlewareA = makeMiddleware ()
              let middlewareB = makeMiddleware ()

              // middlewareA builds and caches the resolved ALPS tree for this origin.
              middlewareA.Invoke(makeContext "http" "shared.example" sampleConfig.ProfileUri).GetAwaiter().GetResult()
              |> ignore

              Expect.equal middlewareA.ResolvedAlpsBuildCount 1 "middlewareA builds once on its first request"

              // middlewareB, an INDEPENDENTLY-constructed instance sharing the SAME
              // DI-resolved keyed IMemoryCache, must observe the cache HIT middlewareA
              // already populated — never rebuild its own copy.
              middlewareB.Invoke(makeContext "http" "shared.example" sampleConfig.ProfileUri).GetAwaiter().GetResult()
              |> ignore

              Expect.equal
                  middlewareB.ResolvedAlpsBuildCount
                  0
                  "middlewareB must observe a cache HIT (0 builds of its own) for an origin middlewareA already cached — proving both instances share the SAME DI-resolved IMemoryCache, not ad-hoc per-instance construction" ]
