module Frank.Alps.Tests.FilteringIntegrationTests

open System
open System.Net.Http
open System.Security.Claims
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

[<Literal>]
let private TestScheme = "TestScheme"

/// Same reasoning as `AlpsDocumentIntegrationTests`' own copy: Frank core's `ResourceEndpointDataSource`
/// is `internal` to `Frank.dll` with no `InternalsVisibleTo` for test projects.
type private TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

/// Verbatim from `Frank.JsonHome.Tests.IntegrationTests`: `X-Test-User` names the principal and
/// `X-Test-Roles` (`;`-separated) its roles; absent `X-Test-User` means anonymous.
type private TestAuthHandler(options, logger, encoder) =
    inherit AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)

    override this.HandleAuthenticateAsync() =
        let user = this.Request.Headers["X-Test-User"].ToString()

        if String.IsNullOrEmpty user then
            Task.FromResult(AuthenticateResult.NoResult())
        else
            let claims = ResizeArray [ Claim(ClaimTypes.Name, user) ]
            let roles = this.Request.Headers["X-Test-Roles"].ToString()

            if not (String.IsNullOrEmpty roles) then
                for role in roles.Split ';' do
                    claims.Add(Claim(ClaimTypes.Role, role))

            let identity = ClaimsIdentity(claims, TestScheme)
            Task.FromResult(AuthenticateResult.Success(AuthenticationTicket(ClaimsPrincipal identity, TestScheme)))

/// `Frank.Auth`'s `requireRole`, inlined rather than referenced: this test project deliberately has no
/// `ProjectReference` to `Frank.Auth` (neither does `Frank.Alps` itself -- the whole point of
/// `AuthorizationFilter` reading stock `IAuthorizeData`/`AuthorizationPolicy` metadata is that it works
/// without one). The two metadata objects are exactly what `Frank.Auth.EndpointAuth.toMetadataObjects`
/// emits for `AuthRequirement.Role`.
let private requireRole (role: string) (def: HandlerDefinition) : HandlerDefinition =
    let policy =
        let pb = AuthorizationPolicyBuilder()
        pb.RequireRole role |> ignore
        pb.Build()

    def
    |> HandlerDefinition.addMetadata (AuthorizeAttribute())
    |> HandlerDefinition.addMetadata policy

/// A profile authored in the NESTED shape `contains` exists for: the two transitions are children of
/// the semantic `game` state rather than top-level elements of the list handed to `useAlps`. Before the
/// tree-walking fix, `makeMove` was invisible to authorization filtering entirely (it was never a
/// top-level profile entry, so it never entered `pairs`), while its `Semantic` parent was kept
/// unconditionally and `Serialization.writeDescriptor` recursed into it regardless -- serving an
/// admin-only transition to every principal.
module private Catalog =
    let openState =
        semantic "open" |> doc "Accepting moves" |> def "https://tictactoe.example/states/open"

    let closedState =
        semantic "closed" |> doc "Game finished" |> def "https://tictactoe.example/states/closed"

    let viewGame = safe "viewGame" |> doc "Read the board"
    let makeMove = unsafe "makeMove" |> from [ openState ] |> rt closedState

    let game =
        semantic "game" |> doc "A tic-tac-toe game" |> contains [ viewGame; makeMove ]

let private profile = [ Catalog.openState; Catalog.closedState; Catalog.game ]

let private getGameJson (ctx: HttpContext) : Task =
    ctx.Response.WriteAsJsonAsync {| id = ctx.Request.RouteValues.["id"] |}

let private makeMoveHandler (ctx: HttpContext) : Task =
    ctx.Response.WriteAsJsonAsync {| ok = true |}

/// `GET` is public and negotiates JSON against the ALPS excerpt; `POST` is admin-only. Guarding POST
/// rather than GET is what lets a single anonymous `GET /games/1` request both reach the excerpt handler
/// and still exercise principal-dependent filtering (`makeMove` is denied, `viewGame` is not).
let private gameResource (resolver: CurrentStateResolver option) =
    resource "/games/{id}" {
        get (
            negotiate {
                accepts "application/json" (handler {
                    handle getGameJson
                    binds Catalog.viewGame
                })

                accepts "application/alps+json" (Alps.excerpt resolver)
            }
        )

        post (
            handler {
                handle makeMoveHandler
                binds Catalog.makeMove
            }
            |> requireRole "admin"
        )
    }

/// `AlpsDocumentIntegrationTests.buildHost`'s pipeline shape, plus the authentication/authorization
/// stages `AuthorizationFilter` needs to see a real principal -- matching
/// `Frank.JsonHome.Tests.IntegrationTests.createServer`.
let private createServer (resolver: CurrentStateResolver option) : HttpClient =
    let spec = (webHost [||]).UseAlps(WebHostSpec.Empty, profile)

    let spec =
        { spec with
            Endpoints = Array.append spec.Endpoints (gameResource resolver).Endpoints }

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
                            .AddAuthentication(TestScheme)
                            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, fun _ -> ())
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
                                .UseEndpoints(fun endpoints ->
                                    endpoints.DataSources.Add(TestEndpointDataSource spec.Endpoints))
                        |> ignore)
                |> ignore)
            .Build()

    host.Start()
    host.GetTestClient()

let private request (method: HttpMethod) (path: string) (accept: string option) (roles: string option) =
    let message = new HttpRequestMessage(method, path)
    accept |> Option.iter message.Headers.Accept.ParseAdd

    roles
    |> Option.iter (fun r ->
        message.Headers.Add("X-Test-User", "alice")
        message.Headers.Add("X-Test-Roles", r))

    message

let private topLevelIds (body: string) =
    let root = JsonDocument.Parse(body).RootElement

    [ for d in root.GetProperty("alps").GetProperty("descriptor").EnumerateArray() -> d.GetProperty("id").GetString() ]

/// The ids of the descriptor array nested inside the top-level descriptor with id `parentId`, or `None`
/// when that descriptor has no nested array at all.
let private nestedIdsOf (parentId: string) (body: string) =
    let root = JsonDocument.Parse(body).RootElement

    root.GetProperty("alps").GetProperty("descriptor").EnumerateArray()
    |> Seq.find (fun d -> d.GetProperty("id").GetString() = parentId)
    |> fun parent ->
        match parent.TryGetProperty "descriptor" with
        | true, nested -> Some [ for d in nested.EnumerateArray() -> d.GetProperty("id").GetString() ]
        | _ -> None

[<Tests>]
let tests =
    testList
        "Filtering through both HTTP exposures"
        [ testTask "the app-wide document serves a nested guarded transition to an authorized principal" {
              let client = createServer None

              let message = request HttpMethod.Get "/.well-known/alps.json" None (Some "admin")
              let! (response: HttpResponseMessage) = client.SendAsync message
              let! (body: string) = response.Content.ReadAsStringAsync()

              Expect.equal
                  (Set.ofList (topLevelIds body))
                  (Set.ofList [ "open"; "closed"; "game" ])
                  "Top level is the three profile roots"

              Expect.equal
                  (nestedIdsOf "game" body)
                  (Some [ "viewGame"; "makeMove" ])
                  "An admin sees both transitions nested under the semantic 'game' state"
          }

          testTask "the app-wide document hides a nested guarded transition from an unauthorized principal" {
              let client = createServer None

              // Anonymous: no X-Test-User header at all.
              let message = request HttpMethod.Get "/.well-known/alps.json" None None
              let! (response: HttpResponseMessage) = client.SendAsync message
              let! (body: string) = response.Content.ReadAsStringAsync()

              // The parent Semantic state is present in BOTH responses -- vocabulary is never filtered.
              // What differs is what survives inside it.
              Expect.equal
                  (Set.ofList (topLevelIds body))
                  (Set.ofList [ "open"; "closed"; "game" ])
                  "The semantic parent is still served to an anonymous principal"

              Expect.equal
                  (nestedIdsOf "game" body)
                  (Some [ "viewGame" ])
                  "The admin-only nested 'makeMove' transition is pruned for an anonymous principal"
          }

          testTask "Alps.excerpt sets Cache-Control/Vary when authorization filtering is active" {
              let client = createServer None

              let message = request HttpMethod.Get "/games/1" (Some "application/alps+json") None
              let! (response: HttpResponseMessage) = client.SendAsync message

              Expect.equal (int response.StatusCode) 200 "The excerpt is served"

              Expect.isTrue response.Headers.CacheControl.Private "Cache-Control marks the excerpt private"
              Expect.isTrue response.Headers.CacheControl.NoCache "Cache-Control marks the excerpt no-cache"

              let vary = Set.ofSeq (response.Headers.GetValues "Vary")

              Expect.isTrue (vary.Contains "Authorization") "Vary names Authorization -- the excerpt is principal-dependent"

              // negotiate { } appends "Accept" before invoking this handler; the excerpt must add to
              // that rather than replace it.
              Expect.isTrue (vary.Contains "Accept") "negotiate {}'s own Vary: Accept survives"
          }

          testTask "Alps.excerpt filters by principal, not just the app-wide document" {
              let client = createServer None

              let anonymous = request HttpMethod.Get "/games/1" (Some "application/alps+json") None
              let! (anonResponse: HttpResponseMessage) = client.SendAsync anonymous
              let! (anonBody: string) = anonResponse.Content.ReadAsStringAsync()

              let admin = request HttpMethod.Get "/games/1" (Some "application/alps+json") (Some "admin")
              let! (adminResponse: HttpResponseMessage) = client.SendAsync admin
              let! (adminBody: string) = adminResponse.Content.ReadAsStringAsync()

              Expect.equal (Set.ofList (topLevelIds anonBody)) (Set.ofList [ "viewGame" ]) "Anonymous sees only the public transition"

              Expect.equal
                  (Set.ofList (topLevelIds adminBody))
                  (Set.ofList [ "viewGame"; "makeMove" ])
                  "An admin sees the guarded transition too"
          }

          testTask "Alps.excerpt (Some resolver) excludes a from-declared transition whose state is unsatisfied" {
              // The resolver reports "/games/1" open and "/games/2" closed. `makeMove` declares
              // `from [ openState ]`, whose Def is the open-state IRI; `viewGame` declares no `from` at
              // all and is therefore never state-filtered. Both requests are made as an admin so
              // authorization can't account for the difference.
              let resolver: CurrentStateResolver =
                  fun path ->
                      match path with
                      | "/games/1" -> [ Uri "https://tictactoe.example/states/open" ]
                      | "/games/2" -> [ Uri "https://tictactoe.example/states/closed" ]
                      | _ -> []

              let client = createServer (Some resolver)

              let openGame = request HttpMethod.Get "/games/1" (Some "application/alps+json") (Some "admin")
              let! (openResponse: HttpResponseMessage) = client.SendAsync openGame
              let! (openBody: string) = openResponse.Content.ReadAsStringAsync()

              let closedGame = request HttpMethod.Get "/games/2" (Some "application/alps+json") (Some "admin")
              let! (closedResponse: HttpResponseMessage) = client.SendAsync closedGame
              let! (closedBody: string) = closedResponse.Content.ReadAsStringAsync()

              Expect.equal
                  (Set.ofList (topLevelIds openBody))
                  (Set.ofList [ "viewGame"; "makeMove" ])
                  "In the 'open' state, makeMove's from-declaration is satisfied"

              Expect.equal
                  (Set.ofList (topLevelIds closedBody))
                  (Set.ofList [ "viewGame" ])
                  "In the 'closed' state, makeMove is excluded and the unconditional viewGame remains"
          }

          testTask "Alps.excerpt (Some resolver) matches existentially across a multi-region active-state list" {
              // Simulates two concurrently-active orthogonal regions for "/games/1": the FIRST returned
              // state is "closed" (which does NOT satisfy makeMove's `from [ openState ]`), and the
              // SECOND is "open" (which does). If the filtering call site only checked the first element
              // of the list -- the old singleton behavior -- makeMove would be wrongly excluded here.
              let resolver: CurrentStateResolver =
                  fun path ->
                      match path with
                      | "/games/1" ->
                          [ Uri "https://tictactoe.example/states/closed"
                            Uri "https://tictactoe.example/states/open" ]
                      | _ -> []

              let client = createServer (Some resolver)

              let message = request HttpMethod.Get "/games/1" (Some "application/alps+json") (Some "admin")
              let! (response: HttpResponseMessage) = client.SendAsync message
              let! (body: string) = response.Content.ReadAsStringAsync()

              Expect.equal
                  (Set.ofList (topLevelIds body))
                  (Set.ofList [ "viewGame"; "makeMove" ])
                  "makeMove is satisfied by the SECOND active state in the list, not just the first"
          } ]
