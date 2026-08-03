module Frank.Alps.Tests.SampleIntegrationTests

open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
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
/// `Frank.JsonHome.Tests.IntegrationTests`'s and `AlpsDocumentIntegrationTests`'s own
/// `TestEndpointDataSource`, this test supplies its own trivial `EndpointDataSource` wrapping a
/// fixed endpoint array to feed `UseEndpoints`.
type private TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

/// Rebuilds `sample/Frank.Alps.Sample/Program.fs`'s `Catalog`/`gameResource`/`useAlps` wiring
/// in-line, rather than adding a `ProjectReference` from this test project to the sample: the
/// established convention in this repo (`test/Frank.Rdf.Tests/Frank.Rdf.Tests.fsproj` has no
/// `ProjectReference` to `sample/Frank.Rdf.Sample`) is that a sample's own test project exercises
/// the library directly, not the sample's `Exe` project (which also can't be referenced without an
/// entry-point conflict). Kept identical field-for-field to the sample so this test is genuinely
/// exercising what a reader of the sample would run.
module private Sample =
    module Catalog =
        let openState =
            semantic "open" |> doc "Accepting moves" |> def "https://tictactoe.example/states/open"

        let closedState =
            semantic "closed" |> doc "Game finished" |> def "https://tictactoe.example/states/closed"

        let game = semantic "game" |> doc "A tic-tac-toe game"

        let viewGame = safe "viewGame" |> rt game
        let makeMove = unsafe "makeMove" |> from [ openState ] |> rt closedState

    let private getGameJson (ctx: HttpContext) : Task =
        ctx.Response.WriteAsJsonAsync {| id = ctx.Request.RouteValues.["id"] |}

    let private makeMoveHandler (ctx: HttpContext) : Task =
        ctx.Response.WriteAsJsonAsync {| ok = true |}

    let gameResource =
        resource "/games/{id}" {
            link (fun ctx ->
                Seq.singleton
                    { Target = string ctx.Request.Path
                      Rel = "profile"
                      Params = [ "type", "application/alps+json" ] })

            get (
                negotiate {
                    accepts "application/json" (handler {
                        handle getGameJson
                        binds Catalog.viewGame
                    })

                    accepts "application/alps+json" (Alps.excerpt None)
                }
            )

            post (handler {
                handle makeMoveHandler
                binds Catalog.makeMove
            })
        }

    let profile =
        [ Catalog.openState; Catalog.closedState; Catalog.game; Catalog.viewGame; Catalog.makeMove ]

/// Builds a real `IHost` around `spec` on `UseTestServer()`, using the exact same pipeline shape as
/// `src/Frank/WebHostBuilder.fs`'s own `WebHostBuilder.Run` (app-wide links -> UseRouting ->
/// resource-scoped links -> spec.Middleware -> UseEndpoints) -- matching
/// `AlpsDocumentIntegrationTests.fs`'s `buildHost` verbatim, plus `AddAuthorization` /
/// `UseAuthorization` so `AuthorizationFilter.filter` (which every `Alps.excerpt`/document call runs
/// through) has an authorization service to call, matching `Frank.JsonHome.Tests.IntegrationTests`'s
/// `createServer`.
let private createServer (spec: WebHostSpec) : HttpClient =
    let host =
        Host
            .CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        spec.Services services |> ignore
                        services.AddAuthorization() |> ignore)
                    .Configure(fun app ->
                        app
                        |> WebLink.useAppWideLinks spec.LinkProviders
                        |> spec.BeforeRoutingMiddleware
                        |> fun app -> app.UseRouting()
                        |> WebLink.useResourceScopedLinks
                        |> spec.Middleware
                        |> fun app ->
                            app
                                .UseAuthorization()
                                .UseEndpoints(fun endpoints -> endpoints.DataSources.Add(TestEndpointDataSource spec.Endpoints))
                        |> ignore)
                |> ignore)
            .Build()

    host.Start()
    host.GetTestClient()

let private buildSampleServer () : HttpClient =
    let spec = (webHost [||]).UseAlps(WebHostSpec.Empty, Sample.profile)

    let spec =
        { spec with
            Endpoints = Array.append spec.Endpoints Sample.gameResource.Endpoints }

    createServer spec

[<Tests>]
let tests =
    testList
        "Sample: both Link headers"
        [ testTask "GET /.well-known/alps.json is advertised app-wide via Link: rel=profile" {
              // Arrange: start the sample's webHost via TestServer.
              let client = buildSampleServer ()

              // Act: GET /games/1 -- useAppWideLinks applies to every response, matched route or not.
              let! (response: HttpResponseMessage) = client.GetAsync "/games/1"

              // Assert: response Link header contains the app-wide profile link.
              Expect.contains
                  (response.Headers.GetValues "Link")
                  "</.well-known/alps.json>; rel=\"profile\""
                  "App-wide Link header advertises the ALPS document"
          }

          testTask "GET /games/1 advertises the per-resource excerpt via a resource-scoped Link header" {
              let client = buildSampleServer ()

              // Act: GET /games/1 with Accept: application/json -- the primary representation.
              let request = new HttpRequestMessage(HttpMethod.Get, "/games/1")
              request.Headers.Accept.ParseAdd "application/json"
              let! (response: HttpResponseMessage) = client.SendAsync request

              // Assert: response Link header contains the resource-scoped excerpt link.
              Expect.contains
                  (response.Headers.GetValues "Link")
                  "</games/1>; rel=\"profile\"; type=\"application/alps+json\""
                  "Resource-scoped Link header advertises the per-resource ALPS excerpt"
          }

          testTask "GET /games/1 with Accept: application/alps+json returns the excerpt containing makeMove" {
              let client = buildSampleServer ()

              // Act: GET /games/1, Accept: application/alps+json.
              let request = new HttpRequestMessage(HttpMethod.Get, "/games/1")
              request.Headers.Accept.ParseAdd "application/alps+json"
              let! (response: HttpResponseMessage) = client.SendAsync request

              Expect.equal (int response.StatusCode) 200 "GET /games/1 with Accept: application/alps+json succeeds"

              // Assert: response body parses as ALPS JSON and contains EXACTLY the two descriptors
              // bound to this route's endpoints -- "viewGame" (GET, bound inside negotiate {}'s
              // "application/json" accepts case) and "makeMove" (POST, bound on a plain handler {
              // } outside negotiate {}). Exact-set equality, not Expect.contains "makeMove": a bare
              // "contains makeMove" assertion is satisfied by the POST endpoint's own binds alone,
              // regardless of whether negotiate {} propagates the GET-side HandlerDefinition's
              // Metadata at all -- it would not catch a regression where NegotiateBuilder.Accepts's
              // HandlerDefinition overload silently dropped binds's Descriptor, since "viewGame"
              // could vanish from this list and the assertion would still pass. Requiring the exact
              // set also catches descriptorsForRoute degenerating to allDescriptors (which would
              // pull in every other resource's descriptors too).
              let! (body: string) = response.Content.ReadAsStringAsync()
              let root = JsonDocument.Parse(body).RootElement
              let descriptors = root.GetProperty("alps").GetProperty("descriptor")

              let ids =
                  [ for d in descriptors.EnumerateArray() -> d.GetProperty("id").GetString() ]

              Expect.equal
                  (Set.ofList ids)
                  (Set.ofList [ "viewGame"; "makeMove" ])
                  "Excerpt for /games/1 contains exactly the GET (negotiate{}-routed) and POST descriptors"
          }

          testTask "GET /.well-known/alps.json returns the full profile including openState/closedState/game" {
              let client = buildSampleServer ()

              // Act: GET /.well-known/alps.json.
              let! (response: HttpResponseMessage) = client.GetAsync "/.well-known/alps.json"
              let! (body: string) = response.Content.ReadAsStringAsync()

              // Assert: response body contains descriptors "open", "closed", "game", "viewGame", "makeMove".
              let root = JsonDocument.Parse(body).RootElement
              let descriptors = root.GetProperty("alps").GetProperty("descriptor")

              let ids =
                  [ for d in descriptors.EnumerateArray() -> d.GetProperty("id").GetString() ]
                  |> Set.ofList

              Expect.equal
                  ids
                  (Set.ofList [ "open"; "closed"; "game"; "viewGame"; "makeMove" ])
                  "Full profile contains every descriptor id"
          } ]
