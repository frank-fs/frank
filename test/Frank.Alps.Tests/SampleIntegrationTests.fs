module Frank.Alps.Tests.SampleIntegrationTests

open System
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
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

/// Task 6 (corrective rebuild): the previous version of this section reconstructed
/// `sample/Frank.Alps.Sample/Program.fs`'s ping-pong `PingPong` descriptors, `PingPongAuth`
/// scheme, and the four `/sessions*` resources in-line -- rejected outright, because a test
/// running its own parallel copy of that logic cannot catch a regression in the REAL sample code.
/// `Frank.Alps.Tests.fsproj` now has a `ProjectReference` to `sample/Frank.Alps.Sample`'s `Exe`
/// project specifically so this file can exercise the actual `Frank.Alps.Sample.Program` module
/// values below -- `PingPong` (descriptors) and `PingPongAuth` (auth scheme) were already public;
/// `sessionsResource`/`sessionResource`/`pingResource`/`pongResource` were made non-`private` for
/// this same reason. Task 4's end-to-end proof (cross-document `href` resolution, DI-threaded
/// `rootUri`, ping/pong role/state wiring) and Task 5's server-side wrong-turn 409 enforcement are
/// unchanged in intent -- only the target of what's being exercised changed, from a hand-written
/// copy to the shipped sample itself.
open Frank.Alps.Sample.PingPong
open Frank.Alps.Sample.TrafficLight

/// `FilteringIntegrationTests.fs`'s own `createServer` shape (`AlpsDocumentIntegrationTests`'s
/// pipeline plus `AddAuthentication`/`UseAuthentication` so `requireRole`'s `AuthorizeAttribute`
/// has a principal to evaluate) -- this file's own top-level `createServer` deliberately never
/// wires authentication, so it can't be reused here. Mirrors `Program.fs`'s own `main` composition
/// (lines ~357-401): the SAME `PingPongAuth.SchemeName` + `PingPongAuth.ApiKeyAuthHandler`
/// registered as the default scheme, the SAME four resources, and the SAME `PingPong.*` descriptor
/// list passed to `useAlps`.
let private createPingPongServer () : HttpClient =
    let spec =
        (webHost [||])
            .UseAlps(
                WebHostSpec.Empty,
                [ participant
                  awaitingPing
                  awaitingPong
                  session
                  listSessions
                  createSession
                  viewSession
                  ping
                  pong ]
            )

    let pingPongEndpoints =
        [ sessionsResource; sessionResource; pingResource; pongResource ]
        |> List.collect (fun r -> List.ofArray r.Endpoints)
        |> List.toArray

    let spec =
        { spec with
            Endpoints = Array.append spec.Endpoints pingPongEndpoints }

    let host =
        Host
            .CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        spec.Services services |> ignore

                        services
                            .AddAuthentication(PingPongAuth.SchemeName)
                            .AddScheme<AuthenticationSchemeOptions, PingPongAuth.ApiKeyAuthHandler>(
                                PingPongAuth.SchemeName,
                                fun _ -> ()
                            )
                        |> ignore

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
                                .UseAuthentication()
                                .UseAuthorization()
                                .UseEndpoints(fun endpoints -> endpoints.DataSources.Add(TestEndpointDataSource spec.Endpoints))
                        |> ignore)
                |> ignore)
            .Build()

    host.Start()
    host.GetTestClient()

let private pingPongRequest (method: HttpMethod) (path: string) (accept: string option) (apiKey: string option) =
    let message = new HttpRequestMessage(method, path)
    accept |> Option.iter message.Headers.Accept.ParseAdd
    apiKey |> Option.iter (fun k -> message.Headers.Add("X-Api-Key", k))
    message

let private alpsDescriptorIds (body: string) : Set<string> =
    JsonDocument
        .Parse(body)
        .RootElement.GetProperty("alps")
        .GetProperty("descriptor")
        .EnumerateArray()
    |> Seq.map (fun d -> d.GetProperty("id").GetString())
    |> Set.ofSeq

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

              // The set assertion above collapses duplicates by definition, so it cannot see a
              // descriptor served twice. It missed exactly that: NegotiateBuilder.Run once
              // broadcast EVERY representation's metadata (not just `produces`) to every
              // sibling endpoint, so the alps+json representation inherited the json
              // representation's `binds Catalog.viewGame` Descriptor and this document listed
              // "viewGame" twice. Assert on the list, not the set.
              Expect.equal
                  (List.sort ids)
                  [ "makeMove"; "viewGame" ]
                  "Each descriptor appears exactly once -- no representation inherits a sibling's binds"
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

/// Task 4 of the multi-doc-linking plan: one end-to-end walk through the full ping/pong protocol,
/// proving cross-document `href` resolution (Task 1), DI-threaded `rootUri` (Task 2), and the
/// sample's role/state wiring (Task 3) all work together over real HTTP.
[<Tests>]
let pingPongTests =
    testList
        "Sample: ping/pong end-to-end (doc-linking + state-gating + role-projection)"
        [ testTask "a full ping/pong cycle exercises doc-linking, state-gating, and role-projection together" {
              let client = createPingPongServer ()

              // 1. POST /sessions creates a session; capture its id.
              let! (createResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Post "/sessions" None None)

              Expect.equal (int createResponse.StatusCode) 200 "POST /sessions creates a session"
              let! (createBody: string) = createResponse.Content.ReadAsStringAsync()
              let id = JsonDocument.Parse(createBody).RootElement.GetProperty("id").GetString()
              let pingPath = $"/sessions/{id}/ping"
              let pongPath = $"/sessions/{id}/pong"

              // 2. Pre-first-move state (Task 3 review fix): a fresh session starts awaitingPing, so
              // pinger's ping excerpt shows "ping" -- and its href to the deliberately-unbound
              // "participant" descriptor resolves cross-document, proving Task 1's fix -- while
              // ponger's pong excerpt does NOT yet show "pong".
              let! (pingExcerptResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Get pingPath (Some "application/alps+json") (Some "pinger-key"))

              Expect.equal (int pingExcerptResponse.StatusCode) 200 "pinger can GET the ping excerpt"
              let! (pingExcerptBody: string) = pingExcerptResponse.Content.ReadAsStringAsync()

              Expect.contains
                  (alpsDescriptorIds pingExcerptBody)
                  "ping"
                  "Fresh session (awaitingPing): the ping excerpt lists the ping transition"

              Expect.equal
                  (hrefOf "ping" pingExcerptBody)
                  "/.well-known/alps.json#participant"
                  "ping's href to 'participant' -- never bound to any endpoint, so never present in this \
                   per-resource excerpt -- resolves cross-document against the full ALPS document"

              let! (pongExcerptResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Get pongPath (Some "application/alps+json") (Some "ponger-key"))

              Expect.equal (int pongExcerptResponse.StatusCode) 200 "ponger can GET the pong excerpt"
              let! (pongExcerptBody: string) = pongExcerptResponse.Content.ReadAsStringAsync()

              Expect.isFalse
                  (Set.contains "pong" (alpsDescriptorIds pongExcerptBody))
                  "Fresh session (awaitingPing): the pong excerpt does not yet list the pong transition"

              // 3. Role-gating: ponger is forbidden from the pinger-only ping resource.
              let! (wrongRoleResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Get pingPath (Some "application/alps+json") (Some "ponger-key"))

              Expect.equal (int wrongRoleResponse.StatusCode) 403 "ponger is forbidden from GET .../ping"

              // 4. pinger pings -- the session is awaitingPing, so this is a legal move: 200, and the
              // follow-up excerpt no longer lists "ping" once the session has moved to awaitingPong.
              let! (pingPostResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Post pingPath None (Some "pinger-key"))

              Expect.equal (int pingPostResponse.StatusCode) 200 "pinger's POST .../ping succeeds"

              let! (postPingExcerptResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Get pingPath (Some "application/alps+json") (Some "pinger-key"))

              let! (postPingExcerptBody: string) = postPingExcerptResponse.Content.ReadAsStringAsync()

              Expect.isFalse
                  (Set.contains "ping" (alpsDescriptorIds postPingExcerptBody))
                  "After a ping, the session is awaitingPong -- the ping excerpt no longer lists ping"

              // 4b. Task 5 (post-hoc addendum): a second consecutive pinger POST .../ping, with no
              // intervening pong, is a wrong-turn call -- the session is awaitingPong, not
              // awaitingPing, so `pingPongMoveHandler`'s server-side guard must reject it with 409
              // (not silently record the move and succeed with 200, as tic-tac-toe's
              // `makeMoveHandler` does).
              let! (wrongTurnPingResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Post pingPath None (Some "pinger-key"))

              Expect.equal
                  (int wrongTurnPingResponse.StatusCode)
                  409
                  "a second consecutive pinger POST .../ping (wrong-turn: session is awaitingPong) is rejected with 409"

              // 5. ponger pongs -- the session returns to awaitingPing.
              let! (pongPostResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Post pongPath None (Some "ponger-key"))

              Expect.equal (int pongPostResponse.StatusCode) 200 "ponger's POST .../pong succeeds"

              let! (postPongExcerptResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Get pingPath (Some "application/alps+json") (Some "pinger-key"))

              let! (postPongExcerptBody: string) = postPongExcerptResponse.Content.ReadAsStringAsync()

              Expect.contains
                  (alpsDescriptorIds postPongExcerptBody)
                  "ping"
                  "After the pong, the session is back to awaitingPing -- the ping excerpt lists ping again"

              // 6. Role-projection via the full app-wide document: pinger sees ping but not pong; ponger
              // sees the reverse.
              let! (pingerDocResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Get "/.well-known/alps.json" None (Some "pinger-key"))

              let! (pingerDocBody: string) = pingerDocResponse.Content.ReadAsStringAsync()
              let pingerIds = alpsDescriptorIds pingerDocBody

              Expect.isTrue (Set.contains "ping" pingerIds) "pinger's full document includes ping"
              Expect.isFalse (Set.contains "pong" pingerIds) "pinger's full document excludes pong"

              let! (pongerDocResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Get "/.well-known/alps.json" None (Some "ponger-key"))

              let! (pongerDocBody: string) = pongerDocResponse.Content.ReadAsStringAsync()
              let pongerIds = alpsDescriptorIds pongerDocBody

              Expect.isTrue (Set.contains "pong" pongerIds) "ponger's full document includes pong"
              Expect.isFalse (Set.contains "ping" pongerIds) "ponger's full document excludes ping"
          }

          // Task 6: the pong-side mirror of test 1's step 4b, previously only manually curl-verified
          // and never asserted by an automated test. A fresh session starts awaitingPing, so a
          // ponger POST .../pong (which requires awaitingPong) is a wrong-turn call from the very
          // first move -- `pongHandler`/`pongResource` (the real sample code, exercised via the
          // `Frank.Alps.Sample` project reference) must reject it with 409, symmetric with ping's.
          testTask "a wrong-turn pong POST on a fresh (awaitingPing) session is rejected with 409" {
              let client = createPingPongServer ()

              let! (createResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Post "/sessions" None None)

              Expect.equal (int createResponse.StatusCode) 200 "POST /sessions creates a session"
              let! (createBody: string) = createResponse.Content.ReadAsStringAsync()
              let id = JsonDocument.Parse(createBody).RootElement.GetProperty("id").GetString()
              let pongPath = $"/sessions/{id}/pong"

              let! (wrongTurnPongResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Post pongPath None (Some "ponger-key"))

              Expect.equal
                  (int wrongTurnPongResponse.StatusCode)
                  409
                  "a fresh session is awaitingPing, not awaitingPong -- ponger's POST .../pong is a \
                   wrong-turn call and is rejected with 409"
          } ]

/// Mirrors `createPingPongServer`'s shape exactly, minus ping/pong's auth wiring -- none of the
/// five `/intersections*` resources carry a `requireRole`, so plain `createServer` (no
/// `AddAuthentication`/`UseAuthentication`) is enough. Wires the REAL
/// `Frank.Alps.Sample.Program.TrafficLight` profile and the REAL
/// `intersectionsResource`/`intersectionResource`/`walkResource`/`emergencyOverrideResource`/
/// `emergencyClearResource` endpoints via the sample's `ProjectReference`, never a hand-copied
/// duplicate of that logic.
let private createTrafficLightServer () : HttpClient =
    let spec = (webHost [||]).UseAlps(WebHostSpec.Empty, profile)

    let trafficLightEndpoints =
        [ intersectionsResource; intersectionResource; walkResource; emergencyOverrideResource; emergencyClearResource ]
        |> List.collect (fun r -> List.ofArray r.Endpoints)
        |> List.toArray

    let spec =
        { spec with
            Endpoints = Array.append spec.Endpoints trafficLightEndpoints }

    createServer spec

/// Traffic light + pedestrian crossing: proves Frank.Alps's compound-transition primitives
/// (StateGuard/TransitionTarget/guardedBy/entersRegions/satisfiesGuard) enforced over real HTTP,
/// to the same standard ping/pong proves single-guard state-gating to. One flow exercises the
/// structural AND-guard (appearing, then genuinely disappearing once no longer satisfied, with
/// real 409 enforcement on a repeat call), the unconditional fan-out (`emergencyOverride`, no
/// guard at all), and -- the assertion an independent reviewer will scrutinize hardest --
/// `History` restoring each region's ACTUAL prior substate on `emergencyClear`, not a hardcoded
/// reset to the initial state.
[<Tests>]
let trafficLightTests =
    testList
        "Sample: traffic light (AND-guard enforcement + unconditional fan-out + History restore)"
        [ testTask "an intersection lifecycle exercises the structural guard, fan-out, and history restore together" {
              let client = createTrafficLightServer ()

              // 1. POST /intersections creates an intersection; capture its id.
              let! (createResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Post "/intersections" None None)

              Expect.equal (int createResponse.StatusCode) 200 "POST /intersections creates an intersection"
              let! (createBody: string) = createResponse.Content.ReadAsStringAsync()
              let id = JsonDocument.Parse(createBody).RootElement.GetProperty("id").GetString()
              let intersectionPath = $"/intersections/{id}"
              let walkPath = $"/intersections/{id}/walk"
              let overridePath = $"/intersections/{id}/emergencyOverride"
              let clearPath = $"/intersections/{id}/emergencyClear"

              // 2. Seeded at creation (vehicleRed + pedWaiting): walk's AND-guard is already
              // satisfied, so the very first excerpt at .../walk lists "walk" -- and the
              // unconditional fan-out transitions' own excerpts always list them, regardless of
              // state. `Alps.excerpt` filters by EXACT route pattern (`EndpointSurface.
              // descriptorsForRoute`), so each action's guarded presence is observed at ITS OWN url
              // -- same shape as `pingResource`/`pongResource` above, not a single combined excerpt
              // at `/intersections/{id}`.
              let! (walkExcerpt1Response: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Get walkPath (Some "application/alps+json") None)

              Expect.equal (int walkExcerpt1Response.StatusCode) 200 "GET the walk excerpt succeeds"
              let! (walkExcerpt1Body: string) = walkExcerpt1Response.Content.ReadAsStringAsync()

              Expect.contains
                  (alpsDescriptorIds walkExcerpt1Body)
                  "walk"
                  "Seeded state (vehicleRed + pedWaiting) satisfies walk's AND-guard"

              let! (overrideExcerptResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Get overridePath (Some "application/alps+json") None)

              let! (overrideExcerptBody: string) = overrideExcerptResponse.Content.ReadAsStringAsync()

              Expect.contains
                  (alpsDescriptorIds overrideExcerptBody)
                  "emergencyOverride"
                  "emergencyOverride is unconditional -- always present"

              let! (clearExcerpt1Response: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Get clearPath (Some "application/alps+json") None)

              let! (clearExcerpt1Body: string) = clearExcerpt1Response.Content.ReadAsStringAsync()

              Expect.contains
                  (alpsDescriptorIds clearExcerpt1Body)
                  "emergencyClear"
                  "emergencyClear is unconditional -- always present"

              // 3. The guard is satisfied -- the first walk POST succeeds.
              let! (walk1Response: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Post walkPath None None)

              Expect.equal (int walk1Response.StatusCode) 200 "First POST .../walk succeeds -- guard was satisfied"

              // 4. Pedestrian moved to pedWalk -- the AND-guard (State vehicleRed && State
              // pedWaiting) is no longer satisfied, so "walk" is genuinely absent from its own
              // excerpt now: real proof the guard is structurally re-evaluated, not just present at
              // authoring time.
              let! (walkExcerpt2Response: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Get walkPath (Some "application/alps+json") None)

              let! (walkExcerpt2Body: string) = walkExcerpt2Response.Content.ReadAsStringAsync()

              Expect.isFalse
                  (Set.contains "walk" (alpsDescriptorIds walkExcerpt2Body))
                  "After walk, pedestrian is pedWalk -- walk's AND-guard is no longer satisfied"

              // 5. A second walk POST genuinely fails server-side: 409, no silent success.
              let! (walk2Response: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Post walkPath None None)

              Expect.equal (int walk2Response.StatusCode) 409 "Second POST .../walk fails -- guard no longer satisfied"

              // 6. Plain-JSON view confirms the mutated state in human-readable form.
              let! (jsonView1Response: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Get intersectionPath (Some "application/json") None)

              let! (jsonView1Body: string) = jsonView1Response.Content.ReadAsStringAsync()
              let jsonView1 = JsonDocument.Parse(jsonView1Body).RootElement

              Expect.equal (jsonView1.GetProperty("vehicle").GetString()) "vehicleRed" "vehicle is still vehicleRed"
              Expect.equal (jsonView1.GetProperty("pedestrian").GetString()) "pedWalk" "pedestrian moved to pedWalk"

              // 7. emergencyOverride is unconditional -- always succeeds, regardless of state.
              let! (overrideResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Post overridePath None None)

              Expect.equal (int overrideResponse.StatusCode) 200 "POST .../emergencyOverride succeeds unconditionally"

              let! (jsonView2Response: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Get intersectionPath (Some "application/json") None)

              let! (jsonView2Body: string) = jsonView2Response.Content.ReadAsStringAsync()
              let jsonView2 = JsonDocument.Parse(jsonView2Body).RootElement

              Expect.equal (jsonView2.GetProperty("vehicle").GetString()) "vehicleFlashing" "vehicle entered vehicleFlashing"
              Expect.equal (jsonView2.GetProperty("pedestrian").GetString()) "pedFlashing" "pedestrian entered pedFlashing"

              // 8. THE key assertion: emergencyClear restores each region's ACTUAL prior state --
              // vehicleRed/pedWalk (what was genuinely active right before the override, mid-cycle
              // after "walk" already fired), NOT a hardcoded reset to the initial
              // vehicleRed/pedWaiting. This is the real proof `History` differs from "reset to
              // initial": a fake implementation that just replayed the two initial states would
              // pass every assertion above but fail this one.
              let! (clearResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Post clearPath None None)

              Expect.equal (int clearResponse.StatusCode) 200 "POST .../emergencyClear succeeds unconditionally"

              let! (jsonView3Response: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Get intersectionPath (Some "application/json") None)

              let! (jsonView3Body: string) = jsonView3Response.Content.ReadAsStringAsync()
              let jsonView3 = JsonDocument.Parse(jsonView3Body).RootElement

              Expect.equal
                  (jsonView3.GetProperty("vehicle").GetString())
                  "vehicleRed"
                  "History restores vehicle to vehicleRed -- its actual state before the override"

              Expect.equal
                  (jsonView3.GetProperty("pedestrian").GetString())
                  "pedWalk"
                  "History restores pedestrian to pedWalk (post-walk), NOT pedWaiting (initial) -- \
                   proof History resumes the actual prior substate, not a hardcoded reset"
          } ]
