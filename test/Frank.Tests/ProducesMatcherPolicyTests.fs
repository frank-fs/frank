module Frank.Tests.ProducesMatcherPolicyTests

open System.Net
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Matching
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder

/// Same pattern as `Frank.Alps.Tests.AlpsDocumentIntegrationTests`'s `TestEndpointDataSource` --
/// `ResourceEndpointDataSource` is `internal` to `Frank.dll` with no `InternalsVisibleTo`.
type private TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

/// Builds two RouteEndpoints at the identical path+verb, tagged with
/// ProducesMediaTypeMetadata directly -- bypassing NegotiateBuilder entirely, since
/// this task tests only the routing policy, not the CE that will produce these
/// endpoints starting in Task 3.
let private buildTaggedEndpoint (path: string) (mediaType: string) (ordinal: int) (body: string) : Endpoint =
    let pattern = Patterns.RoutePatternFactory.Parse path
    let handler =
        RequestDelegate(fun ctx ->
            ctx.Response.ContentType <- mediaType
            ctx.Response.WriteAsync(body))
    let builder = RouteEndpointBuilder(handler, pattern, 0)
    builder.Metadata.Add(HttpMethodMetadata [| "GET" |])
    builder.Metadata.Add(ProducesMediaTypeMetadata(mediaType, ordinal))
    builder.Build()

let private buildHost (endpoints: Endpoint[]) : IHost =
    Host
        .CreateDefaultBuilder([||])
        .ConfigureWebHost(fun webBuilder ->
            webBuilder
                .UseTestServer()
                .ConfigureServices(fun services ->
                    services.AddRouting() |> ignore
                    services.AddSingleton<MatcherPolicy, FrankProducesMatcherPolicy>() |> ignore)
                .Configure(fun app ->
                    app.UseRouting() |> ignore
                    app.UseEndpoints(fun endpoints' ->
                        endpoints'.DataSources.Add(TestEndpointDataSource endpoints))
                    |> ignore)
            |> ignore)
        .Build()

[<Tests>]
let tests =
    testList
        "FrankProducesMatcherPolicy"
        [ testCaseTask "selects the endpoint matching an exact Accept header"
          <| fun () -> task {
              let endpoints =
                  [| buildTaggedEndpoint "/x" "application/json" 0 "json"
                     buildTaggedEndpoint "/x" "text/html" 1 "html" |]
              use host = buildHost endpoints
              do! host.StartAsync()
              use client = host.GetTestClient()
              use request = new HttpRequestMessage(HttpMethod.Get, "/x")
              request.Headers.Accept.ParseAdd("text/html")
              let! response = client.SendAsync(request)
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "html" "The text/html-tagged endpoint should have been selected"
              Expect.equal (response.Content.Headers.ContentType.MediaType) "text/html" "Content-Type set to the winner"
          }

          testCaseTask "sets Vary: Accept on a successful dispatch"
          <| fun () -> task {
              let endpoints = [| buildTaggedEndpoint "/x" "application/json" 0 "json" |]
              use host = buildHost endpoints
              do! host.StartAsync()
              use client = host.GetTestClient()
              use request = new HttpRequestMessage(HttpMethod.Get, "/x")
              request.Headers.Accept.ParseAdd("application/json")
              let! response = client.SendAsync(request)
              Expect.contains (response.Headers.Vary |> List.ofSeq) "Accept" "Vary: Accept must be present"
          }

          testCaseTask "responds 406 with no body when nothing matches"
          <| fun () -> task {
              let endpoints = [| buildTaggedEndpoint "/x" "application/json" 0 "json" |]
              use host = buildHost endpoints
              do! host.StartAsync()
              use client = host.GetTestClient()
              use request = new HttpRequestMessage(HttpMethod.Get, "/x")
              request.Headers.Accept.ParseAdd("application/xml")
              let! response = client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.NotAcceptable "Should be 406"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "" "No body on 406"
          }

          testCaseTask "absent Accept selects the lowest-Ordinal (first-registered) endpoint"
          <| fun () -> task {
              let endpoints =
                  [| buildTaggedEndpoint "/x" "application/json" 0 "json"
                     buildTaggedEndpoint "/x" "text/html" 1 "html" |]
              use host = buildHost endpoints
              do! host.StartAsync()
              use client = host.GetTestClient()
              let! response = client.GetAsync("/x")
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "json" "Ordinal 0 wins on an absent Accept header"
          }

          testCaseTask "an unrelated endpoint at a different path is untouched"
          <| fun () -> task {
              let endpoints =
                  [| buildTaggedEndpoint "/x" "application/json" 0 "json"
                     buildTaggedEndpoint "/y" "text/plain" 0 "plain" |]
              use host = buildHost endpoints
              do! host.StartAsync()
              use client = host.GetTestClient()
              let! response = client.GetAsync("/y")
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "plain" "/y has only one representation, unaffected by /x's negotiation"
          } ]
