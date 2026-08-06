module Frank.Tests.ResourceBuilderMultiHandlerTests

open System.Net
open System.Net.Http
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

type private TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

let private buildHost (endpoints: Endpoint[]) : IHost =
    Host
        .CreateDefaultBuilder([||])
        .ConfigureWebHost(fun webBuilder ->
            webBuilder
                .UseTestServer()
                .ConfigureServices(fun services -> services.AddRouting() |> ignore)
                .Configure(fun app ->
                    app.UseRouting() |> ignore
                    app.UseEndpoints(fun endpoints' -> endpoints'.DataSources.Add(TestEndpointDataSource endpoints))
                    |> ignore)
            |> ignore)
        .Build()

[<Tests>]
let tests =
    testList
        "ResourceBuilder multi-handler-per-method"
        [ testCaseTask "Get with a HandlerDefinition list expands to N RouteEndpoints, each with its own metadata"
          <| fun () -> task {
              let defA =
                  { Handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync("a"))
                    Metadata = [ box "marker-a" ] }
              let defB =
                  { Handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync("b"))
                    Metadata = [ box "marker-b" ] }

              let built = (resource "/x") { get [ defA; defB ] }

              Expect.equal built.Endpoints.Length 2 "Two representations become two RouteEndpoints"

              let metadataOf (e: Endpoint) = e.Metadata |> Seq.filter (fun m -> m :? string) |> List.ofSeq

              Expect.contains (metadataOf built.Endpoints.[0] @ metadataOf built.Endpoints.[1]) (box "marker-a") "First endpoint's own metadata is attached"
              Expect.contains (metadataOf built.Endpoints.[0] @ metadataOf built.Endpoints.[1]) (box "marker-b") "Second endpoint's own metadata is attached"

              // Each endpoint carries ONLY its own metadata, not the other's -- this is
              // exactly the bug the method-scoped-convention trick had once multiple
              // handlers share one method.
              Expect.equal (metadataOf built.Endpoints.[0]) [ box "marker-a" ] "Endpoint 0 has only its own metadata"
              Expect.equal (metadataOf built.Endpoints.[1]) [ box "marker-b" ] "Endpoint 1 has only its own metadata"
          }

          testCaseTask "both endpoints are independently reachable through real routing"
          <| fun () -> task {
              let defA =
                  { Handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync("a"))
                    Metadata = [] }
              let defB =
                  { Handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync("b"))
                    Metadata = [] }

              let built = (resource "/x") { get [ defA; defB ] }
              use host = buildHost built.Endpoints
              do! host.StartAsync()
              use client = host.GetTestClient()

              // With no policy to disambiguate (FrankProducesMatcherPolicy lands in Task 2/5,
              // not wired into this bare test host), the DFA matcher throws
              // AmbiguousMatchException -- this proves both endpoints reached routing,
              // which a broken per-entry Handlers shape would NOT do.
              let! thrown =
                  task {
                      try
                          let! _ = client.GetAsync("/x")
                          return None
                      with ex ->
                          return Some ex
                  }

              match thrown with
              | Some ex ->
                  Expect.stringContains
                      (ex.ToString())
                      "AmbiguousMatchException"
                      "Both endpoints registered and reached the DFA matcher"
              | None -> failtest "expected an ambiguous match exception proving both endpoints were registered"
          } ]
