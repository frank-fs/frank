module Frank.Discovery.Tests.CeOrderingTests

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder
open Frank.Discovery

/// Mirrors WebHostBuilder.Run's NET10 wiring sequence (ResourceEndpointDataSource built
/// from the FULLY composed spec.Endpoints and registered as a DI singleton BEFORE
/// Build(), then added to IEndpointRouteBuilder.DataSources after) but with TestServer +
/// non-blocking Start() instead of the real Run() (which blocks forever waiting for host
/// shutdown) — the same test-only seam MiddlewareOrderingTests.fs (Frank.Tests) already
/// establishes for exercising WebHostSpec composition without invoking the CE's terminal,
/// blocking Run member.
let private runSpecOnTestServer (spec: WebHostSpec) : WebApplication =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    let dataSource = ResourceEndpointDataSource(spec.Endpoints)
    builder.Services.AddSingleton<IResourceEndpointDataSource>(dataSource) |> ignore
    spec.Services builder.Services |> ignore
    let app = builder.Build()

    (app :> IApplicationBuilder)
    |> spec.BeforeRoutingMiddleware
    |> fun app -> app.UseRouting()
    |> spec.Middleware
    |> ignore

    (app :> IEndpointRouteBuilder).DataSources.Add(dataSource)
    app.Start()
    app

/// #411 AC2: useDiscoveryWith produces identical, correct results regardless of where it's
/// placed in a webHost CE block relative to `resource` declarations. #397/#400's DI-based
/// design exists specifically to prevent the hazard of useDiscoveryWith closure-capturing
/// spec.Endpoints AT ITS OWN CALL SITE, which would silently miss any `resource` declared
/// AFTER it in the block. #411's narrow-DI-at-Run-time design (ResourceEndpointDataSource
/// registered once, in WebHostBuilder.Run, from the FULLY composed spec.Endpoints) must
/// preserve this same safety property.
[<Tests>]
let tests =
    testList
        "webHost CE — #411 AC2: useDiscoveryWith-before-resource ordering safety"
        [ testCase "useDiscoveryWith declared BEFORE the resource block still reconciles that resource's live ALPS Type"
          <| fun _ ->
              let builder = WebHostBuilder([||])

              let config =
                  { DiscoveryConfig.Empty with
                      ProfileUri = "/alps/test"
                      AlpsDescriptors =
                          [ { Id = "Game"
                              // Deliberately WRONG codegen default — must be overridden by live
                              // reconciliation for this test to prove anything (a vacuous fixture
                              // where the default already matches the reconciled value would pass
                              // even with reconciliation silently no-op'd).
                              Type = "unsafe"
                              Doc = None
                              Href = Some "https://schema.org/Game"
                              Descriptors = []
                              Rt = None
                              ClassIri = Some "https://schema.org/Game"
                              RequestClrTypeName = None } ] }

              let gameResource =
                  resource "/games/{id}" {
                      relation "https://schema.org/Game"
                      get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("game")))
                  }

              // useDiscoveryWith is composed BEFORE `resource` is appended to the spec — the
              // exact ordering that would break an eager, call-site-captured design.
              let spec =
                  WebHostSpec.Empty
                  |> fun s -> builder.UseDiscoveryWith(s, config)
                  |> fun s -> builder.Resource(s, gameResource)

              use app = runSpecOnTestServer spec
              use client = app.GetTestClient()
              let resp = client.GetAsync("/alps/test").GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "ALPS profile served"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()

              Expect.stringContains
                  body
                  "\"type\":\"safe\""
                  "Game reconciled to safe from the live GET declared AFTER useDiscoveryWith in the CE block — not left at the deliberately-wrong codegen default 'unsafe', and not silently missed" ]
