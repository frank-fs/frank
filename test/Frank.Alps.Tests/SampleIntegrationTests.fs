module Frank.Alps.Tests.SampleIntegrationTests

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Net.Http
open System.Security.Claims
open System.Text.Encodings.Web
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authorization
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

/// Rebuilds `sample/Frank.Alps.Sample/Program.fs`'s ping-pong `PingPong` descriptors,
/// `PingPongAuth` scheme, and the four `/sessions*` resources in-line, exactly like `Sample`
/// above does for the tic-tac-toe half -- same rationale (no `ProjectReference` to the sample's
/// `Exe` project, entry-point conflict). Task 4 of the multi-doc-linking plan: end-to-end proof
/// that cross-document `href` resolution (Task 1), DI-threaded `rootUri` (Task 2), and the
/// ping/pong sample itself (Task 3) all work together over real HTTP -- doc-linking,
/// state-gating (via excerpt-absence, not a 409 -- ping/pong's handlers always record the move
/// and return 200, mirroring `Sample.makeMoveHandler`'s own posture), and role-projection.
module private PingPong =
    let participant = semantic "participant" |> doc "A session participant"

    let awaitingPing =
        semantic "awaitingPing" |> doc "Waiting for a ping" |> def "https://pingpong.example/states/awaitingPing"

    let awaitingPong =
        semantic "awaitingPong" |> doc "Waiting for a pong" |> def "https://pingpong.example/states/awaitingPong"

    let session = semantic "session" |> doc "A ping-pong session"

    let listSessions = safe "listSessions" |> rt session
    let createSession = unsafe "createSession" |> rt session
    let viewSession = safe "viewSession" |> rt session

    let ping = unsafe "ping" |> from [ awaitingPing ] |> rt awaitingPong |> href participant
    let pong = unsafe "pong" |> from [ awaitingPong ] |> rt awaitingPing |> href participant

    let profile =
        [ participant
          awaitingPing
          awaitingPong
          session
          listSessions
          createSession
          viewSession
          ping
          pong ]

/// Verbatim shape of `sample/Frank.Alps.Sample/Program.fs`'s own `PingPongAuth` module (itself
/// modeled on `sample/Frank.JsonHome.Sample/ApiKeyAuth.fs`): an "X-Api-Key" header mapped to a
/// hardcoded user/roles table, with the SAME two keys the sample ships ("pinger-key" ->
/// role "pinger", "ponger-key" -> role "ponger") so this test's HTTP requests are exactly what a
/// reader of the sample would send.
module private PingPongAuth =
    [<Literal>]
    let SchemeName = "PingPongApiKey"

    let private users: IDictionary<string, string * string list> =
        dict [ "pinger-key", ("pinger", [ "pinger" ]); "ponger-key", ("ponger", [ "ponger" ]) ]

    type ApiKeyAuthHandler(options, logger, encoder: UrlEncoder) =
        inherit AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)

        override this.HandleAuthenticateAsync() =
            let key = this.Request.Headers["X-Api-Key"].ToString()

            match users.TryGetValue key with
            | true, (name, roles) ->
                let claims = Claim(ClaimTypes.Name, name) :: (roles |> List.map (fun r -> Claim(ClaimTypes.Role, r)))
                let identity = ClaimsIdentity(claims, SchemeName)
                let ticket = AuthenticationTicket(ClaimsPrincipal identity, SchemeName)
                Task.FromResult(AuthenticateResult.Success ticket)
            | false, _ -> Task.FromResult(AuthenticateResult.NoResult())

/// `Frank.Auth`'s `requireRole` custom operation, reimplemented at the `HandlerDefinition` level:
/// verbatim rationale of `FilteringIntegrationTests.fs`'s own copy -- this test project has no
/// `ProjectReference` to `Frank.Auth`, and `AuthorizationFilter`/ASP.NET's own authorization
/// middleware only need the stock `IAuthorizeData`/`AuthorizationPolicy` metadata objects
/// `Frank.Auth.EndpointAuth.toMetadataObjects` would emit for `AuthRequirement.Role`. Applied to
/// BOTH the GET (excerpt) and POST handler on each of `pingResource`/`pongResource`, matching the
/// real sample's resource-level `resource { requireRole "..." }` (which gates every method on the
/// resource, not just POST).
let private requireRole (role: string) (def: HandlerDefinition) : HandlerDefinition =
    let policy =
        let pb = AuthorizationPolicyBuilder()
        pb.RequireRole role |> ignore
        pb.Build()

    def
    |> HandlerDefinition.addMetadata (AuthorizeAttribute())
    |> HandlerDefinition.addMetadata policy

/// In-memory stand-in for the real sample's `Frank.Provenance`-backed `pingPongStateResolver`
/// (this test project has no `ProjectReference` to `Frank.Provenance` either) -- same contract:
/// keyed by the session's own path (the `/ping`/`/pong` suffix stripped, so a POST to either
/// action and a later excerpt GET on the other both resolve to the SAME session's current state),
/// and a session with no recorded move yet falls back to `awaitingPing` (not `[]`) for the exact
/// reason `Program.fs`'s own doc comment gives: `Alps.excerpt` treats a resolver's `[]` as "state
/// filtering does not apply", which would wrongly leave `pong` visible in a fresh session's
/// excerpt.
let private sessionStates = ConcurrentDictionary<string, Uri>()

let private sessionPathOf (path: string) : string =
    if path.EndsWith("/ping") then path.Substring(0, path.Length - "/ping".Length)
    elif path.EndsWith("/pong") then path.Substring(0, path.Length - "/pong".Length)
    else path

let private pingPongStateResolver: CurrentStateResolver =
    fun path ->
        match sessionStates.TryGetValue(sessionPathOf path) with
        | true, state -> [ state ]
        | false, _ -> [ PingPong.awaitingPing.Def.Value ]

/// Adapts `Alps.excerpt`'s `RequestDelegate` result into the `HttpContext -> Task` shape
/// `handler { handle ... }` accepts, so the excerpt handler can be piped through `requireRole`
/// exactly like the POST handlers below.
let private excerptHandler (resolver: CurrentStateResolver) : HttpContext -> Task =
    (Alps.excerpt (Some resolver)).Invoke

/// Records the session's move, typed as the state it transitions INTO -- mirrors
/// `Program.fs`'s `recordPingPongMove` convention (ping types `awaitingPong`, pong types
/// `awaitingPing`), minus the `Frank.Provenance` plumbing this test project doesn't reference.
let private recordMove (ctx: HttpContext) (targetStateDef: Uri option) : unit =
    targetStateDef |> Option.iter (fun d -> sessionStates.[sessionPathOf ctx.Request.Path.Value] <- d)

let private pingHandler (ctx: HttpContext) : Task =
    task {
        recordMove ctx PingPong.awaitingPong.Def
        do! ctx.Response.WriteAsJsonAsync {| ok = true |}
    }
    :> Task

let private pongHandler (ctx: HttpContext) : Task =
    task {
        recordMove ctx PingPong.awaitingPing.Def
        do! ctx.Response.WriteAsJsonAsync {| ok = true |}
    }
    :> Task

let private sessionIds = ConcurrentBag<Guid>()

let private listSessionsHandler (ctx: HttpContext) : Task =
    ctx.Response.WriteAsJsonAsync {| sessions = sessionIds |> Seq.map string |> Seq.toList |}

let private createSessionHandler (ctx: HttpContext) : Task =
    task {
        let id = Guid.NewGuid()
        sessionIds.Add id
        do! ctx.Response.WriteAsJsonAsync {| id = string id |}
    }
    :> Task

let private getSessionHandler (ctx: HttpContext) : Task =
    ctx.Response.WriteAsJsonAsync {| id = ctx.Request.RouteValues.["id"] |}

let private sessionsResource =
    resource "/sessions" {
        get (handler {
            handle listSessionsHandler
            binds PingPong.listSessions
        })

        post (handler {
            handle createSessionHandler
            binds PingPong.createSession
        })
    }

let private sessionResource =
    resource "/sessions/{id}" {
        get (
            negotiate {
                accepts "application/json" (handler {
                    handle getSessionHandler
                    binds PingPong.viewSession
                })

                accepts "application/alps+json" (Alps.excerpt (Some pingPongStateResolver))
            }
        )
    }

/// GET here serves only the ALPS excerpt (there is no plain-JSON representation of "the ping
/// action") -- `requireRole "pinger"` on both methods so an unauthorized GET 403s before it ever
/// reaches `Alps.excerpt`, exactly like the POST does.
let private pingResource =
    resource "/sessions/{id}/ping" {
        get (handler { handle (excerptHandler pingPongStateResolver) } |> requireRole "pinger")

        post (
            handler {
                handle pingHandler
                binds PingPong.ping
            }
            |> requireRole "pinger"
        )
    }

let private pongResource =
    resource "/sessions/{id}/pong" {
        get (handler { handle (excerptHandler pingPongStateResolver) } |> requireRole "ponger")

        post (
            handler {
                handle pongHandler
                binds PingPong.pong
            }
            |> requireRole "ponger"
        )
    }

/// `FilteringIntegrationTests.fs`'s own `createServer` shape (`AlpsDocumentIntegrationTests`'s
/// pipeline plus `AddAuthentication`/`UseAuthentication` so `requireRole`'s `AuthorizeAttribute`
/// has a principal to evaluate) -- this file's own top-level `createServer` deliberately never
/// wires authentication, so it can't be reused here.
let private createPingPongServer () : HttpClient =
    let spec = (webHost [||]).UseAlps(WebHostSpec.Empty, PingPong.profile)

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

              // 4. pinger pings -- always 200 (no server-side 409; ping/pong POST handlers always
              // record the move, mirroring Sample.makeMoveHandler's own posture). State-gating is
              // enforced purely via HATEOAS: a follow-up excerpt no longer lists "ping" once the
              // session has moved to awaitingPong.
              let! (pingPostResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Post pingPath None (Some "pinger-key"))

              Expect.equal (int pingPostResponse.StatusCode) 200 "pinger's POST .../ping succeeds"

              let! (postPingExcerptResponse: HttpResponseMessage) =
                  client.SendAsync(pingPongRequest HttpMethod.Get pingPath (Some "application/alps+json") (Some "pinger-key"))

              let! (postPingExcerptBody: string) = postPingExcerptResponse.Content.ReadAsStringAsync()

              Expect.isFalse
                  (Set.contains "ping" (alpsDescriptorIds postPingExcerptBody))
                  "After a ping, the session is awaitingPong -- the ping excerpt no longer lists ping \
                   (state-gating via excerpt-absence, not a 409)"

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
          } ]
