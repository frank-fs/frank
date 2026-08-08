module Frank.JsonHome.Tests.HrefVarStartupFilterTests

open System
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
open Frank.JsonHome

type private TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

let private noop: RequestDelegate = RequestDelegate(fun ctx -> ctx.Response.WriteAsync "")

let private buildHost (resources: Resource list) : IHost =
    let endpoints = resources |> List.collect (fun r -> List.ofArray r.Endpoints) |> Array.ofList

    Host
        .CreateDefaultBuilder([||])
        .ConfigureWebHost(fun webBuilder ->
            webBuilder
                .UseTestServer()
                .ConfigureServices(fun services ->
                    services.AddRouting() |> ignore
                    services.AddEndpointsApiExplorer() |> ignore
                    services.AddSingleton<EndpointDataSource>(TestEndpointDataSource endpoints) |> ignore
                    services.AddSingleton<IStartupFilter, HrefVarStartupFilter>() |> ignore)
                .Configure(fun app ->
                    app.UseRouting().UseEndpoints(fun e -> e.DataSources.Add(TestEndpointDataSource endpoints))
                    |> ignore)
            |> ignore)
        .Build()

// host.Start() propagates HrefVarStartupFilter's exception bare (confirmed by
// running this test) rather than wrapping it in an AggregateException, so no
// unwrapping is needed here.
let private startAndCaptureFailure (host: IHost) : string list option =
    try
        host.Start()
        None
    with HrefVarValidationException messages ->
        Some messages

[<Tests>]
let tests =
    testList
        "HrefVarStartupFilter"
        [ test "starts successfully when hrefVar matches the route template" {
              let productResource =
                  resource "/products/{id}" {
                      rel "tag:example.com,2026:product"
                      hrefVar "id" "https://example.com/param/product-id"
                      get noop
                  }

              use host = buildHost [ productResource ]
              Expect.isNone (startAndCaptureFailure host) "expected the host to start"
          }

          test "fails to start when hrefVar doesn't match any route template variable" {
              let productResource =
                  resource "/products/{id}" {
                      rel "tag:example.com,2026:product"
                      hrefVar "prodId" "https://example.com/param/product-id"
                      get noop
                  }

              use host = buildHost [ productResource ]

              match startAndCaptureFailure host with
              | Some messages -> Expect.stringContains (String.concat " " messages) "prodId" "names the mismatched hrefVar"
              | None -> failtest "expected startup to fail"
          }

          test "fails to start when a route template variable has no hrefVar declaration" {
              let productResource =
                  resource "/products/{id}" {
                      rel "tag:example.com,2026:product"
                      get noop
                  }

              use host = buildHost [ productResource ]

              match startAndCaptureFailure host with
              | Some messages -> Expect.stringContains (String.concat " " messages) "id" "names the missing variable"
              | None -> failtest "expected startup to fail"
          }

          // FR-007: failures must aggregate across every mismatched resource,
          // not just the first one found. A filter that raises on the first
          // bad resource (List.tryFind / List.exists short-circuit instead
          // of List.collect over all of them) fails this test even though
          // it passes the three single-resource tests above.
          test "aggregates mismatches across multiple resources into one failure" {
              let productResource =
                  resource "/products/{id}" {
                      rel "tag:example.com,2026:product"
                      hrefVar "prodId" "https://example.com/param/product-id"
                      get noop
                  }

              let orderResource =
                  resource "/orders/{orderId}" {
                      rel "tag:example.com,2026:order"
                      get noop
                  }

              use host = buildHost [ productResource; orderResource ]

              match startAndCaptureFailure host with
              | Some messages ->
                  let text = String.concat " " messages
                  Expect.stringContains text "prodId" "names the product mismatch"
                  Expect.stringContains text "orderId" "also names the order mismatch"
              | None -> failtest "expected startup to fail"
          } ]
