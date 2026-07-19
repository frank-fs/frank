module Frank.Discovery.Tests.CorrelationDriftDiagnosticTests

open Expecto
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Patterns
open Microsoft.Extensions.Logging
open Frank.Discovery.Tests.TestHelpers
open Frank.Tests.Shared.TestEndpointDataSource

/// A RouteEndpoint carrying HttpMethodMetadata but deliberately WITHOUT the handler's own
/// MethodInfo — synthesizes exactly the condition TestHelpers.routeEndpoint's own doc
/// comment names as what EndpointMetadataApiDescriptionProvider silently skips, to prove
/// #400 Fix 3's drift diagnostic actually fires when the two correlation sources diverge.
let private routeEndpointMissingMethodInfo (pattern: string) (methods: string[]) : RouteEndpoint =
    let builder = RoutePatternFactory.Parse pattern
    let handler = RequestDelegate(fun _ -> System.Threading.Tasks.Task.CompletedTask)

    let metadataCollection =
        EndpointMetadataCollection(box (HttpMethodMetadata(methods)))

    RouteEndpoint(handler, builder, 0, metadataCollection, null)

let private loggerFrom (provider: CapturingLoggerProvider) : ILogger =
    (provider :> ILoggerProvider).CreateLogger("test")

[<Tests>]
let tests =
    testList
        "DiscoveryMiddleware — #400 Fix 3: correlation-source drift diagnostic"
        [ test "endpoint missing MethodInfo (silently excluded from ApiDescriptions) logs a warning" {
              let dataSource =
                  TestEndpointDataSource([| routeEndpointMissingMethodInfo "/widgets/{id}" [| "GET" |] |])

              let apiProvider = apiDescriptionProviderFor dataSource
              let capturing = new CapturingLoggerProvider()

              Frank.Discovery.DiscoveryMiddleware.checkCorrelationSourcesAgree
                  (loggerFrom capturing)
                  dataSource
                  apiProvider

              Expect.exists
                  capturing.Messages
                  (fun m -> m.Contains "does not match")
                  "a route-table/ApiDescription count mismatch must be logged as a warning"
          }

          test "matching endpoint (with MethodInfo, TestHelpers.routeEndpoint) does NOT log a warning" {
              let dataSource =
                  TestEndpointDataSource([| routeEndpoint "/widgets/{id}" [| "GET" |] [] |])

              let apiProvider = apiDescriptionProviderFor dataSource
              let capturing = new CapturingLoggerProvider()

              Frank.Discovery.DiscoveryMiddleware.checkCorrelationSourcesAgree
                  (loggerFrom capturing)
                  dataSource
                  apiProvider

              Expect.isEmpty capturing.Messages "matching route-table/ApiDescription counts must not log a warning"
          } ]
