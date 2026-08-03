module Frank.Alps.Tests.AlpsDocumentIntegrationTests

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder
open Frank.Alps

/// Frank core's own `ResourceEndpointDataSource` (`src/Frank/ResourceBuilder.fs`) is `internal` to
/// `Frank.dll`, with no `InternalsVisibleTo` for test projects -- so, exactly like
/// `Frank.JsonHome.Tests.IntegrationTests`'s own `TestEndpointDataSource`, this test supplies its
/// own trivial `EndpointDataSource` wrapping a fixed endpoint array to feed `UseEndpoints`.
type private TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

/// Builds a real `IHost` around `spec` using the same pipeline shape as
/// `src/Frank/WebHostBuilder.fs`'s own `WebHostBuilder.Run` (UseRouting -> resource-scoped links ->
/// spec.Middleware -> UseEndpoints), substituted onto `UseTestServer()` instead of a real Kestrel
/// listener/socket, so `host.Start()` exercises the exact same IStartupFilter-composition code path
/// production does -- without blocking forever the way `WebHostBuilder.Run` itself does.
let private buildHost (spec: WebHostSpec) : IHost =
    Host
        .CreateDefaultBuilder([||])
        .ConfigureWebHost(fun webBuilder ->
            webBuilder
                .UseTestServer()
                .ConfigureServices(fun services ->
                    services.AddRouting() |> ignore
                    spec.Services services |> ignore)
                .Configure(fun app ->
                    app
                    |> WebLink.useAppWideLinks spec.LinkProviders
                    |> spec.BeforeRoutingMiddleware
                    |> fun app -> app.UseRouting()
                    |> WebLink.useResourceScopedLinks
                    |> spec.Middleware
                    |> fun app ->
                        app.UseEndpoints(fun endpoints -> endpoints.DataSources.Add(TestEndpointDataSource spec.Endpoints))
                    |> ignore)
            |> ignore)
        .Build()

[<Tests>]
let tests =
    testList
        "AlpsDocument startup validation (real host wiring)"
        [ test "useAlps with a descriptor bound to the wrong HTTP method fails host startup" {
              // Regression test for the bug an IHostedService-based implementation had: its
              // StartAsync ran before app.UseEndpoints(...) populated the EndpointDataSource, so
              // EndpointSurface.allDescriptors always saw zero endpoints and `validate` never
              // raised, no matter how badly a descriptor's DescriptorType mismatched its bound
              // HTTP method. This builds the real webHost {}-equivalent pipeline (not a
              // hand-built (Endpoint * Descriptor) list passed straight to
              // AlpsDocument.validate, which the other 5 tests in AlpsDocumentTests.fs already
              // cover) and asserts that starting the host itself fails.
              let mismatched = safe "listProducts" // Safe -> valid only bound to GET/HEAD

              let products =
                  resource "/products" {
                      post (
                          handler {
                              handle (fun (ctx: HttpContext) -> ctx.Response.WriteAsync "OK")
                              binds mismatched
                          }
                      )
                  }

              let spec = (webHost [||]).UseAlps(WebHostSpec.Empty, [ mismatched ])

              let spec =
                  { spec with
                      Endpoints = Array.append spec.Endpoints products.Endpoints }

              let host = buildHost spec

              Expect.throws (fun () -> host.Start()) "Starting the host runs the ValidationStartupFilter, which must fail"
          }

          test "useAlps with every descriptor correctly bound starts the host cleanly" {
              // Sanity counterpart: the same wiring must NOT raise when every transition's bound
              // HTTP method actually matches its DescriptorType, proving the previous test's
              // failure is really about the mismatch and not, say, the host shape itself.
              let correct = safe "listProducts"

              let products =
                  resource "/products" {
                      get (
                          handler {
                              handle (fun (ctx: HttpContext) -> ctx.Response.WriteAsync "OK")
                              binds correct
                          }
                      )
                  }

              let spec = (webHost [||]).UseAlps(WebHostSpec.Empty, [ correct ])

              let spec =
                  { spec with
                      Endpoints = Array.append spec.Endpoints products.Endpoints }

              let host = buildHost spec

              host.Start()
          } ]
