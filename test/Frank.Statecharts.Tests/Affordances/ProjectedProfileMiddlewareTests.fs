module Frank.Affordances.Tests.ProjectedProfileMiddlewareTests

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Primitives
open Frank.Affordances
open Frank.Resources.Model
open Frank.Statecharts
open Frank.Affordances.Tests.AffordanceTestHelpers

// -- Helpers --

/// Build a RoleProfileLookup for the ProjectedProfileMiddleware.
let private buildRoleLookup (entries: (string * (string * string) list) list) : RoleProfileLookup =
    let lookup = RoleProfileLookup(StringComparer.Ordinal)

    for routeTemplate, roles in entries do
        let roleMap = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

        for roleName, linkValue in roles do
            roleMap.[roleName] <- linkValue

        lookup.[routeTemplate] <- roleMap

    lookup

/// Run a test against a test server with both AffordanceMiddleware and ProjectedProfileMiddleware.
let private withServer
    (affordanceLookup: Dictionary<string, PreComputedAffordance>)
    (roleLookup: RoleProfileLookup)
    (featureSetter: HttpContext -> unit)
    (f: HttpClient -> Task)
    =
    task {
        let builder = WebApplication.CreateBuilder([||])
        builder.WebHost.UseTestServer() |> ignore
        builder.Services.AddRouting() |> ignore
        let app = builder.Build()

        app.UseRouting() |> ignore

        (app :> IApplicationBuilder)
            .Use(fun ctx (next: Func<Task>) ->
                featureSetter ctx
                next.Invoke())
        |> ignore

        (app :> IApplicationBuilder).UseMiddleware<AffordanceMiddleware>(affordanceLookup)
        |> ignore

        (app :> IApplicationBuilder).UseMiddleware<ProjectedProfileMiddleware>(roleLookup)
        |> ignore

        app.UseEndpoints(fun endpoints -> defaultEndpoints endpoints) |> ignore

        app.Start()
        let server = app.GetTestServer()
        let client = server.CreateClient()

        try
            do! f client
        finally
            client.Dispose()
            server.Dispose()
            (app :> IDisposable).Dispose()
    }
    :> Task

// -- Test data --

let private gamesRoleLookup =
    buildRoleLookup
        [ "/games/{gameId}",
          [ "playerx", "<https://example.com/alps/games-playerx>; rel=\"profile\""
            "playero", "<https://example.com/alps/games-playero>; rel=\"profile\"" ] ]

// -- Middleware Tests --

[<Tests>]
let projectedProfileMiddlewareTests =
    testList
        "ProjectedProfileMiddleware"
        [
          testCase "anonymous request preserves global profile link"
          <| fun _ ->
              let affordances =
                  buildAffordanceLookup [ "/games/{gameId}|XTurn", xTurnAffordance ]

              (withServer affordances gamesRoleLookup (fun ctx -> ctx.SetStatechartState("XTurn", "XTurn", 0)) (fun client ->
                  task {
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                      Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                      let links = getHeaderValues response "Link"
                      let allLinks = links |> String.concat " "

                      Expect.isTrue
                          (allLinks.Contains("alps/games>"))
                          "Should contain global profile link"

                      Expect.isFalse
                          (allLinks.Contains("alps/games-playerx>"))
                          "Should NOT contain role-specific profile link"

                      // Vary: Authorization present because this route has role projections
                      // (RFC 7234 §4.1: Vary describes selection algorithm, not this specific response)
                      let vary = getHeaderValues response "Vary"
                      Expect.isNonEmpty vary "Should have Vary header (route has role projections)"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "authenticated request with matching role swaps profile link"
          <| fun _ ->
              let affordances =
                  buildAffordanceLookup [ "/games/{gameId}|XTurn", xTurnAffordance ]

              (withServer
                  affordances
                  gamesRoleLookup
                  (fun ctx ->
                      ctx.SetStatechartState("XTurn", "XTurn", 0)
                      ctx.SetRoles(Set [ "PlayerX" ]))
                  (fun client ->
                      task {
                          let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let links = getHeaderValues response "Link"
                          let allLinks = links |> String.concat " "

                          Expect.isTrue
                              (allLinks.Contains("alps/games-playerx>"))
                              "Should contain role-specific profile link"

                          Expect.isFalse
                              (allLinks.Contains("alps/games>; rel=\"profile\""))
                              "Should NOT contain global profile link"

                          Expect.isTrue
                              (allLinks.Contains("rel=\"makeMove\""))
                              "Should preserve transition links"

                          let vary = getHeaderValues response "Vary"
                          Expect.isNonEmpty vary "Should have Vary header"
                          let varyValue = vary |> String.concat ", "
                          Expect.isTrue (varyValue.Contains("Authorization")) "Vary should include Authorization"
                      }))
                  .GetAwaiter()
                  .GetResult()

          testCase "authenticated request with non-matching role keeps global profile"
          <| fun _ ->
              let affordances =
                  buildAffordanceLookup [ "/games/{gameId}|XTurn", xTurnAffordance ]

              (withServer
                  affordances
                  gamesRoleLookup
                  (fun ctx ->
                      ctx.SetStatechartState("XTurn", "XTurn", 0)
                      ctx.SetRoles(Set [ "Spectator" ]))
                  (fun client ->
                      task {
                          let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let links = getHeaderValues response "Link"
                          let allLinks = links |> String.concat " "

                          Expect.isTrue
                              (allLinks.Contains("alps/games>"))
                              "Should contain global profile link (no match)"

                          // Vary: Authorization present because route has role projections
                          Expect.isTrue (hasHeader response "Vary") "Should have Vary header (route has role projections)"
                      }))
                  .GetAwaiter()
                  .GetResult()

          testCase "multiple roles uses first match"
          <| fun _ ->
              let affordances =
                  buildAffordanceLookup [ "/games/{gameId}|XTurn", xTurnAffordance ]

              (withServer
                  affordances
                  gamesRoleLookup
                  (fun ctx ->
                      ctx.SetStatechartState("XTurn", "XTurn", 0)
                      ctx.SetRoles(Set [ "PlayerO"; "PlayerX" ]))
                  (fun client ->
                      task {
                          let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let links = getHeaderValues response "Link"
                          let allLinks = links |> String.concat " "

                          // Both PlayerO and PlayerX have entries; one should match
                          let hasRoleProfile =
                              allLinks.Contains("alps/games-playerx>")
                              || allLinks.Contains("alps/games-playero>")

                          Expect.isTrue hasRoleProfile "Should contain a role-specific profile link"
                          Expect.isTrue (hasHeader response "Vary") "Should have Vary header"
                      }))
                  .GetAwaiter()
                  .GetResult()

          testCase "no Link header from affordances preserves Vary but skips link swap"
          <| fun _ ->
              let affordances = buildAffordanceLookup []

              (withServer
                  affordances
                  gamesRoleLookup
                  (fun ctx ->
                      ctx.SetStatechartState("XTurn", "XTurn", 0)
                      ctx.SetRoles(Set [ "PlayerX" ]))
                  (fun client ->
                      task {
                          let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
                          Expect.isFalse (hasHeader response "Link") "Should not have Link header"
                          // Vary: Authorization is still set because route has role projections
                          Expect.isTrue (hasHeader response "Vary") "Should have Vary header (route has role projections)"
                      }))
                  .GetAwaiter()
                  .GetResult()

          testCase "resource without role projections is a no-op"
          <| fun _ ->
              let affordances =
                  buildAffordanceLookup [ "/health|*", healthAffordance ]

              let emptyRoleLookup = buildRoleLookup []

              (withServer
                  affordances
                  emptyRoleLookup
                  (fun ctx -> ctx.SetRoles(Set [ "Admin" ]))
                  (fun client ->
                      task {
                          let! (response: HttpResponseMessage) = client.GetAsync("/health")

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let links = getHeaderValues response "Link"
                          let allLinks = links |> String.concat " "

                          Expect.isTrue
                              (allLinks.Contains("alps/health>"))
                              "Should contain original profile link"

                          Expect.isFalse (hasHeader response "Vary") "Should NOT have Vary header"
                      }))
                  .GetAwaiter()
                  .GetResult() ]

// -- RoleProfileOverlay.build unit tests --

[<Tests>]
let roleProfileOverlayTests =
    testList
        "RoleProfileOverlay.build"
        [ testCase "returns empty lookup when RoleAlpsProfiles is empty"
          <| fun _ ->
              let state =
                  { Resources =
                      [ { RouteTemplate = "/games/{gameId}"
                          ResourceSlug = "games"
                          Statechart = RuntimeStatechart.empty
                          HttpCapabilities = [] } ]
                    BaseUri = "https://example.com/alps"
                    Profiles = ProjectedProfiles.empty }

              let lookup = RoleProfileOverlay.build state
              Expect.equal lookup.Count 0 "Should be empty"

          testCase "maps role slug to route template and role name"
          <| fun _ ->
              let state =
                  { Resources =
                      [ { RouteTemplate = "/games/{gameId}"
                          ResourceSlug = "games"
                          Statechart = RuntimeStatechart.empty
                          HttpCapabilities = [] } ]
                    BaseUri = "https://example.com/alps"
                    Profiles =
                      { ProjectedProfiles.empty with
                          RoleAlpsProfiles = Map.ofList [ "games-playerx", "{}" ] } }

              let lookup = RoleProfileOverlay.build state
              Expect.equal lookup.Count 1 "Should have one route"
              Expect.isTrue (lookup.ContainsKey("/games/{gameId}")) "Should map to route template"

              let roleMap = lookup.["/games/{gameId}"]
              Expect.isTrue (roleMap.ContainsKey("playerx")) "Should map role name"

              Expect.equal
                  roleMap.["playerx"]
                  "<https://example.com/alps/games-playerx>; rel=\"profile\""
                  "Should format profile link correctly"

          testCase "handles multiple resources with different roles"
          <| fun _ ->
              let state =
                  { Resources =
                      [ { RouteTemplate = "/games/{gameId}"
                          ResourceSlug = "games"
                          Statechart = RuntimeStatechart.empty
                          HttpCapabilities = [] }
                        { RouteTemplate = "/matches/{matchId}"
                          ResourceSlug = "matches"
                          Statechart = RuntimeStatechart.empty
                          HttpCapabilities = [] } ]
                    BaseUri = "https://example.com/alps"
                    Profiles =
                      { ProjectedProfiles.empty with
                          RoleAlpsProfiles =
                            Map.ofList
                                [ "games-playerx", "{}"
                                  "games-playero", "{}"
                                  "matches-referee", "{}" ] } }

              let lookup = RoleProfileOverlay.build state
              Expect.equal lookup.Count 2 "Should have two routes"

              let gamesMap = lookup.["/games/{gameId}"]
              Expect.equal gamesMap.Count 2 "Games should have two roles"
              Expect.isTrue (gamesMap.ContainsKey("playerx")) "Should have playerx"
              Expect.isTrue (gamesMap.ContainsKey("playero")) "Should have playero"

              let matchesMap = lookup.["/matches/{matchId}"]
              Expect.equal matchesMap.Count 1 "Matches should have one role"
              Expect.isTrue (matchesMap.ContainsKey("referee")) "Should have referee"

          testCase "ignores role slugs that do not match any resource"
          <| fun _ ->
              let state =
                  { Resources =
                      [ { RouteTemplate = "/games/{gameId}"
                          ResourceSlug = "games"
                          Statechart = RuntimeStatechart.empty
                          HttpCapabilities = [] } ]
                    BaseUri = "https://example.com/alps"
                    Profiles =
                      { ProjectedProfiles.empty with
                          RoleAlpsProfiles = Map.ofList [ "unknown-admin", "{}" ] } }

              let lookup = RoleProfileOverlay.build state
              Expect.equal lookup.Count 0 "Should be empty when no slug matches" ]

// -- LinkHeaderRewriter unit tests --

[<Tests>]
let linkHeaderRewriterTests =
    testList
        "LinkHeaderRewriter.replaceProfileLink"
        [ testCase "replaces profile entry in StringValues"
          <| fun _ ->
              let existing =
                  StringValues(
                      [| "<https://example.com/alps/games>; rel=\"profile\""
                         "</games/123/move>; rel=\"makeMove\"" |]
                  )

              let replacement = "<https://example.com/alps/games-playerx>; rel=\"profile\""
              let result = LinkHeaderRewriter.replaceProfileLink existing replacement
              let values = result.ToArray()

              Expect.equal values.Length 2 "Should preserve array length"
              Expect.equal values.[0] replacement "First entry should be replaced"
              Expect.equal values.[1] "</games/123/move>; rel=\"makeMove\"" "Second entry should be preserved"

          testCase "returns original when no profile entry found"
          <| fun _ ->
              let existing =
                  StringValues([| "</games/123/move>; rel=\"makeMove\"" |])

              let replacement = "<https://example.com/alps/games-playerx>; rel=\"profile\""
              let result = LinkHeaderRewriter.replaceProfileLink existing replacement
              let values = result.ToArray()

              Expect.equal values.Length 1 "Should preserve array length"
              Expect.equal values.[0] "</games/123/move>; rel=\"makeMove\"" "Entry should be unchanged"

          testCase "replaces only first profile entry"
          <| fun _ ->
              let existing =
                  StringValues(
                      [| "<https://example.com/alps/games>; rel=\"profile\""
                         "<https://example.com/alps/other>; rel=\"profile\"" |]
                  )

              let replacement = "<https://example.com/alps/games-playerx>; rel=\"profile\""
              let result = LinkHeaderRewriter.replaceProfileLink existing replacement
              let values = result.ToArray()

              Expect.equal values.[0] replacement "First profile should be replaced"

              Expect.equal
                  values.[1]
                  "<https://example.com/alps/other>; rel=\"profile\""
                  "Second profile should be preserved" ]
