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

/// Regression coverage for the DI-threaded `rootUri` wiring (Frank.Alps multi-doc-linking plan, task
/// 2): `WebHostBuilderExtensions.install` registers the `AlpsOptions` a `useAlps` call was actually
/// composed with as a DI singleton, and `Excerpt.rootUriFor` reads it back from
/// `ctx.RequestServices` at request time, falling back to `AlpsOptions.Default` when nothing is
/// registered. Neither this file's `buildSampleServer` (always the default `Path`) nor
/// `FilteringIntegrationTests.fs` (same) ever calls the `useAlps(profile, configure)` 3-arg overload
/// with a non-default `Path`, nor wires `Alps.excerpt` into a host with no `useAlps` composed at all
/// -- so a bug that always resolved `AlpsOptions.Default` regardless of DI registration, or an
/// inverted null-check in `rootUriFor`, would still compile and pass every other test in this suite.
module private RootUriWiring =
    // `shared` is deliberately never added to the profile handed to `useAlps` in either test below --
    // it exists only as an `href` target guaranteed absent from what's served, forcing
    // Serialization.toJson's cross-document (`rootUri#id`) resolution branch rather than its local
    // (`#id`) one.
    let shared = semantic "shared"
    let local = safe "local" |> href shared

    let private getLocalJson (ctx: HttpContext) : Task = ctx.Response.WriteAsJsonAsync {| ok = true |}

    /// `local`'s `application/alps+json` excerpt is exactly what asserts on `rootUriFor`'s
    /// resolution; the `application/json` branch exists only so `local` has an endpoint to `binds`
    /// (a descriptor with no bound endpoint is pruned from `served` entirely, per
    /// `DescriptorTree.prune`, and would never appear in the response body to assert on).
    let localResource =
        resource "/local" {
            get (
                negotiate {
                    accepts "application/json" (handler {
                        handle getLocalJson
                        binds local
                    })

                    accepts "application/alps+json" (Alps.excerpt None)
                }
            )
        }

let private hrefOf (id: string) (body: string) =
    JsonDocument
        .Parse(body)
        .RootElement.GetProperty("alps")
        .GetProperty("descriptor")
        .EnumerateArray()
    |> Seq.find (fun d -> d.GetProperty("id").GetString() = id)
    |> fun d -> d.GetProperty("href").GetString()

let private getAlpsExcerpt (client: HttpClient) (path: string) =
    task {
        let request = new HttpRequestMessage(HttpMethod.Get, path)
        request.Headers.Accept.ParseAdd "application/alps+json"
        return! client.SendAsync request
    }

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
          }

          testTask
              "Alps.excerpt resolves a cross-document href against a non-default useAlps Path, not AlpsOptions.Default" {
              // useAlps composed via the 3-arg `configure` overload, at a non-default Path -- proving
              // `Excerpt.rootUriFor` reads back the SAME AlpsOptions `install` registered in DI, not a
              // hardcoded default.
              let spec =
                  (webHost [||])
                      .UseAlps(WebHostSpec.Empty, [ RootUriWiring.local ], (fun opts -> { opts with Path = "/custom/alps.json" }))

              let spec =
                  { spec with
                      Endpoints = Array.append spec.Endpoints RootUriWiring.localResource.Endpoints }

              let client = createServer spec
              let! (response: HttpResponseMessage) = getAlpsExcerpt client "/local"
              let! (body: string) = response.Content.ReadAsStringAsync()

              Expect.equal (int response.StatusCode) 200 "the excerpt is served"

              Expect.equal
                  (hrefOf "local" body)
                  "/custom/alps.json#shared"
                  "the excerpt's href for a descriptor absent from the profile resolves against the \
                   configured non-default useAlps Path -- proof Excerpt.rootUriFor read the \
                   DI-registered AlpsOptions rather than falling through to AlpsOptions.Default"
          }

          testTask "Alps.excerpt falls back to AlpsOptions.Default when no useAlps was ever composed" {
              // No .UseAlps(...) call anywhere in this spec -- AlpsOptions is never registered in DI,
              // so Excerpt.rootUriFor's GetService<AlpsOptions>() call must return null and its
              // fallback branch must fire.
              let spec =
                  { WebHostSpec.Empty with
                      Endpoints = RootUriWiring.localResource.Endpoints }

              let client = createServer spec
              let! (response: HttpResponseMessage) = getAlpsExcerpt client "/local"
              let! (body: string) = response.Content.ReadAsStringAsync()

              Expect.equal (int response.StatusCode) 200 "the excerpt still serves correctly with no useAlps composed at all"

              Expect.equal
                  (hrefOf "local" body)
                  "/.well-known/alps.json#shared"
                  "with no useAlps composed, AlpsOptions is never registered in DI -- rootUriFor's null \
                   check must fall back to AlpsOptions.Default rather than throwing or resolving \
                   incorrectly"
          } ]
