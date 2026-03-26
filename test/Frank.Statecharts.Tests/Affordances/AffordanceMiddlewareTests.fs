module Frank.Affordances.Tests.AffordanceMiddlewareTests

open System.Net
open System.Net.Http
open Expecto
open Microsoft.Extensions.Primitives
open Frank.Affordances
open Frank.Resources.Model
open Frank.Statecharts
open Frank.Affordances.Tests.AffordanceTestHelpers

// -- Tests --

[<Tests>]
let affordanceMiddlewareTests =
    testList
        "AffordanceMiddleware"
        [
          // T040: Stateful resource header injection
          testCase "injects Allow and Link headers for stateful resource (XTurn)"
          <| fun _ ->
              let lookup =
                  buildAffordanceLookup [ "/games/{gameId}|XTurn", xTurnAffordance ]

              (withAffordanceServer lookup (fun ctx -> ctx.SetStatechartState("XTurn", "XTurn", 0)) defaultEndpoints (fun client -> task {
                  let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                  Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                  let allow = getHeaderValues response "Allow"
                  Expect.isNonEmpty allow "Should have Allow header"
                  let allowValue = allow |> String.concat ", "

                  Expect.isTrue
                      (allowValue.Contains("GET") && allowValue.Contains("POST"))
                      "Allow header should list GET and POST for XTurn state"

                  let links = getHeaderValues response "Link"
                  Expect.isNonEmpty links "Should have Link header"
                  let allLinks = links |> String.concat " "

                  Expect.isTrue
                      (allLinks.Contains("rel=\"profile\""))
                      "Should contain profile link"

                  Expect.isTrue
                      (allLinks.Contains("rel=\"makeMove\""))
                      "Should contain makeMove transition link"
              }))
                  .GetAwaiter()
                  .GetResult()

          // T040: Different state yields different headers
          testCase "injects correct headers for Won state (no POST)"
          <| fun _ ->
              let lookup =
                  buildAffordanceLookup [ "/games/{gameId}|Won", wonAffordance ]

              (withAffordanceServer lookup (fun ctx -> ctx.SetStatechartState("Won", "Won", 0)) defaultEndpoints (fun client -> task {
                  let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                  Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                  let allow = getHeaderValues response "Allow"
                  Expect.isNonEmpty allow "Should have Allow header"
                  let allowValue = allow |> String.concat ", "
                  Expect.isTrue (allowValue.Contains("GET")) "Allow header should list GET"
                  Expect.isFalse (allowValue.Contains("POST")) "Allow header should NOT list POST for Won state"

                  let links = getHeaderValues response "Link"
                  Expect.isNonEmpty links "Should have Link header"
                  let allLinks = links |> String.concat " "
                  Expect.isTrue (allLinks.Contains("rel=\"profile\"")) "Should contain profile link"
                  Expect.isFalse (allLinks.Contains("makeMove")) "Should NOT contain makeMove link"
              }))
                  .GetAwaiter()
                  .GetResult()

          // T041: Plain resource uses wildcard state key
          testCase "uses wildcard state key for plain resources"
          <| fun _ ->
              let lookup =
                  buildAffordanceLookup [ "/health|*", healthAffordance ]

              (withAffordanceServer lookup ignore defaultEndpoints (fun client -> task {
                  let! (response: HttpResponseMessage) = client.GetAsync("/health")

                  Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                  let allow = getHeaderValues response "Allow"
                  Expect.isNonEmpty allow "Should have Allow header"
                  let allowValue = allow |> String.concat ", "
                  Expect.isTrue (allowValue.Contains("GET")) "Allow header should list GET for health endpoint"

                  let links = getHeaderValues response "Link"
                  Expect.isNonEmpty links "Should have Link header"
                  let allLinks = links |> String.concat " "
                  Expect.isTrue (allLinks.Contains("rel=\"profile\"")) "Should contain profile link for health"
              }))
                  .GetAwaiter()
                  .GetResult()

          // T042: Graceful degradation -- no matching entry
          testCase "passes through when no matching affordance entry exists"
          <| fun _ ->
              let lookup =
                  buildAffordanceLookup [ "/other|*", healthAffordance ]

              (withAffordanceServer lookup (fun ctx -> ctx.SetStatechartState("SomeState", "SomeState", 0)) defaultEndpoints (fun client -> task {
                  let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                  Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
                  Expect.isFalse (hasHeader response "Link") "Should not have Link header"
              }))
                  .GetAwaiter()
                  .GetResult()

          // T042: Graceful degradation -- empty lookup
          testCase "passes through with empty affordance lookup"
          <| fun _ ->
              let lookup = buildAffordanceLookup []

              (withAffordanceServer lookup ignore defaultEndpoints (fun client -> task {
                  let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                  Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
                  Expect.isFalse (hasHeader response "Link") "Should not have Link header"
              }))
                  .GetAwaiter()
                  .GetResult()

          // T040: Unknown state key -- no fallback to wildcard
          testCase "does not fall back to wildcard when state key is present but unmatched"
          <| fun _ ->
              let lookup =
                  buildAffordanceLookup
                      [ "/games/{gameId}|XTurn", xTurnAffordance
                        "/games/{gameId}|*", wonAffordance ]

              (withAffordanceServer lookup (fun ctx -> ctx.SetStatechartState("UnknownState", "UnknownState", 0)) defaultEndpoints (fun client -> task {
                  let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                  Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
                  // Should NOT use the wildcard entry -- state key was present but didn't match
                  Expect.isFalse (hasHeader response "Link") "Should not have Link header for unknown state"
              }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let preComputeTests =
    testList
        "AffordancePreCompute.preCompute"
        [ testCase "produces correct Allow header"
          <| fun _ ->
              let map =
                  { Version = AffordanceMap.currentVersion
                    Entries =
                      [ { RouteTemplate = "/games/{gameId}"
                          StateKey = "XTurn"
                          AllowedMethods = [ "GET"; "POST" ]
                          LinkRelations = []
                          ProfileUrl = "https://example.com/alps/games" } ] }

              let result = AffordancePreCompute.preCompute map
              let key = AffordanceMap.lookupKey "/games/{gameId}" "XTurn"
              Expect.isTrue (result.ContainsKey(key)) "Should contain the key"
              let entry = result.[key]
              Expect.equal (entry.AllowHeaderValue.ToString()) "GET, POST" "Allow header should be pre-formatted"

          testCase "produces correct Link headers with profile and transitions"
          <| fun _ ->
              let map =
                  { Version = AffordanceMap.currentVersion
                    Entries =
                      [ { RouteTemplate = "/games/{gameId}"
                          StateKey = "XTurn"
                          AllowedMethods = [ "GET"; "POST" ]
                          LinkRelations =
                            [ { Rel = "makeMove"
                                Href = "/games/{gameId}/move"
                                Method = "POST"
                                Title = Some "Make a move"
                                Roles = [] } ]
                          ProfileUrl = "https://example.com/alps/games" } ] }

              let result = AffordancePreCompute.preCompute map
              let key = AffordanceMap.lookupKey "/games/{gameId}" "XTurn"
              let entry = result.[key]
              let linkValues = entry.LinkHeaderValues.ToArray()
              Expect.equal linkValues.Length 2 "Should have profile + transition link"

              Expect.equal
                  linkValues.[0]
                  "<https://example.com/alps/games>; rel=\"profile\""
                  "First link should be profile"

              Expect.equal
                  linkValues.[1]
                  "</games/{gameId}/move>; rel=\"makeMove\""
                  "Second link should be transition"

          // #199: end-to-end: generateFromResources filters self, preCompute omits it from headers
          testCase "rel=self absent from precomputed Link headers after generateFromResources pipeline"
          <| fun _ ->
              let resource: RuntimeResource =
                  { RouteTemplate = "/games/{gameId}"
                    ResourceSlug = "games"
                    Statechart = RuntimeStatechart.empty
                    HttpCapabilities =
                      [ { Method = "GET"
                          StateKey = "*"
                          LinkRelation = "self"
                          IsSafe = true }
                        { Method = "POST"
                          StateKey = "*"
                          LinkRelation = "makeMove"
                          IsSafe = false } ] }

              let map =
                  AffordanceMap.generateFromResources [ resource ] "https://example.com/alps"

              let result = AffordancePreCompute.preCompute map
              let key = AffordanceMap.lookupKey "/games/{gameId}" "*"
              let entry = result.[key]
              let linkValues = entry.LinkHeaderValues.ToArray()
              // profile + makeMove = 2 (self filtered by buildLinkRelations)
              Expect.equal linkValues.Length 2 "Should have profile + makeMove (no self)"
              let allLinks = linkValues |> String.concat " "
              Expect.isFalse (allLinks.Contains("rel=\"self\"")) "Should not contain rel=self in Link headers"
              Expect.isTrue (allLinks.Contains("rel=\"profile\"")) "Should contain profile"
              Expect.isTrue (allLinks.Contains("rel=\"makeMove\"")) "Should contain makeMove"
              // Allow header should still have GET
              Expect.equal (entry.AllowHeaderValue.ToString()) "GET, POST" "Allow should still list GET and POST"

          testCase "omits profile link when ProfileUrl is empty"
          <| fun _ ->
              let map =
                  { Version = AffordanceMap.currentVersion
                    Entries =
                      [ { RouteTemplate = "/health"
                          StateKey = "*"
                          AllowedMethods = [ "GET" ]
                          LinkRelations = []
                          ProfileUrl = "" } ] }

              let result = AffordancePreCompute.preCompute map
              let key = AffordanceMap.lookupKey "/health" "*"
              let entry = result.[key]
              let linkValues = entry.LinkHeaderValues.ToArray()
              Expect.equal linkValues.Length 0 "Should have no link values when profile is empty and no transitions"

          testCase "handles multiple entries"
          <| fun _ ->
              let map =
                  { Version = AffordanceMap.currentVersion
                    Entries =
                      [ { RouteTemplate = "/games/{gameId}"
                          StateKey = "XTurn"
                          AllowedMethods = [ "GET"; "POST" ]
                          LinkRelations = []
                          ProfileUrl = "https://example.com/alps/games" }
                        { RouteTemplate = "/games/{gameId}"
                          StateKey = "Won"
                          AllowedMethods = [ "GET" ]
                          LinkRelations = []
                          ProfileUrl = "https://example.com/alps/games" } ] }

              let result = AffordancePreCompute.preCompute map
              Expect.equal result.Count 2 "Should have two entries"

              let xTurnKey = AffordanceMap.lookupKey "/games/{gameId}" "XTurn"
              let wonKey = AffordanceMap.lookupKey "/games/{gameId}" "Won"
              Expect.isTrue (result.ContainsKey(xTurnKey)) "Should contain XTurn key"
              Expect.isTrue (result.ContainsKey(wonKey)) "Should contain Won key"
              Expect.equal (result.[xTurnKey].AllowHeaderValue.ToString()) "GET, POST" "XTurn allows GET, POST"
              Expect.equal (result.[wonKey].AllowHeaderValue.ToString()) "GET" "Won allows only GET"

          testCase "preCompute generates role-specific entries"
          <| fun _ ->
              let map =
                  { Version = AffordanceMap.currentVersion
                    Entries =
                      [ { RouteTemplate = "/games/{gameId}"
                          StateKey = "XTurn"
                          AllowedMethods = [ "GET"; "POST" ]
                          LinkRelations =
                            [ { Rel = "makeMove"
                                Href = "/games/{gameId}/move"
                                Method = "POST"
                                Title = Some "Make a move"
                                Roles = [ "PlayerX" ] }
                              { Rel = "viewGame"
                                Href = "/games/{gameId}"
                                Method = "GET"
                                Title = None
                                Roles = [] } ]
                          ProfileUrl = "https://example.com/alps/games" } ] }

              let result = AffordancePreCompute.preCompute map

              // Base entry with ALL links
              let baseKey = AffordanceMap.lookupKey "/games/{gameId}" "XTurn"
              Expect.isTrue (result.ContainsKey(baseKey)) "Should contain base key"
              let baseEntry = result.[baseKey]
              let baseLinks = baseEntry.LinkHeaderValues.ToArray()
              // profile + makeMove + viewGame = 3
              Expect.equal baseLinks.Length 3 "Base entry should have all links (profile + 2 transitions)"

              // Role-specific entry for PlayerX
              let playerXKey = AffordanceMap.lookupKeyWithRole "/games/{gameId}" "XTurn" "PlayerX"
              Expect.isTrue (result.ContainsKey(playerXKey)) "Should contain PlayerX role key"
              let playerXEntry = result.[playerXKey]
              let playerXLinks = playerXEntry.LinkHeaderValues.ToArray()
              // profile + makeMove (PlayerX role) + viewGame (all roles) = 3
              Expect.equal playerXLinks.Length 3 "PlayerX should see profile + makeMove + viewGame"
              let playerXLinksStr = playerXLinks |> String.concat " "
              Expect.isTrue (playerXLinksStr.Contains("makeMove")) "PlayerX should see makeMove"
              Expect.isTrue (playerXLinksStr.Contains("viewGame")) "PlayerX should see viewGame"

          // Step 3b gap: multiple distinct roles generate independent entries
          testCase "preCompute generates distinct entries for PlayerX and PlayerO with correct filtering"
          <| fun _ ->
              let map =
                  { Version = AffordanceMap.currentVersion
                    Entries =
                      [ { RouteTemplate = "/games/{gameId}"
                          StateKey = "XTurn"
                          AllowedMethods = [ "GET"; "POST" ]
                          LinkRelations =
                            [ { Rel = "makeMove"
                                Href = "/games/{gameId}/move"
                                Method = "POST"
                                Title = None
                                Roles = [ "PlayerX" ] }
                              { Rel = "offerDraw"
                                Href = "/games/{gameId}/draw"
                                Method = "POST"
                                Title = None
                                Roles = [ "PlayerO" ] }
                              { Rel = "viewGame"
                                Href = "/games/{gameId}"
                                Method = "GET"
                                Title = None
                                Roles = [] } ]
                          ProfileUrl = "https://example.com/alps/games" } ] }

              let result = AffordancePreCompute.preCompute map

              // Base entry: ALL links (profile + makeMove + offerDraw + viewGame = 4)
              let baseKey = AffordanceMap.lookupKey "/games/{gameId}" "XTurn"
              let baseEntry = result.[baseKey]
              let baseLinksStr = baseEntry.LinkHeaderValues.ToArray() |> String.concat " "
              Expect.isTrue (baseLinksStr.Contains("makeMove")) "Base should contain makeMove"
              Expect.isTrue (baseLinksStr.Contains("offerDraw")) "Base should contain offerDraw"
              Expect.isTrue (baseLinksStr.Contains("viewGame")) "Base should contain viewGame"
              Expect.isTrue (baseLinksStr.Contains("profile")) "Base should contain profile"

              // PlayerX entry: profile + makeMove + viewGame (no offerDraw)
              let playerXKey = AffordanceMap.lookupKeyWithRole "/games/{gameId}" "XTurn" "PlayerX"
              Expect.isTrue (result.ContainsKey(playerXKey)) "Should contain PlayerX key"
              let playerXLinksStr = result.[playerXKey].LinkHeaderValues.ToArray() |> String.concat " "
              Expect.isTrue (playerXLinksStr.Contains("makeMove")) "PlayerX should see makeMove"
              Expect.isFalse (playerXLinksStr.Contains("offerDraw")) "PlayerX should NOT see offerDraw"
              Expect.isTrue (playerXLinksStr.Contains("viewGame")) "PlayerX should see viewGame"

              // PlayerO entry: profile + offerDraw + viewGame (no makeMove)
              let playerOKey = AffordanceMap.lookupKeyWithRole "/games/{gameId}" "XTurn" "PlayerO"
              Expect.isTrue (result.ContainsKey(playerOKey)) "Should contain PlayerO key"
              let playerOLinksStr = result.[playerOKey].LinkHeaderValues.ToArray() |> String.concat " "
              Expect.isFalse (playerOLinksStr.Contains("makeMove")) "PlayerO should NOT see makeMove"
              Expect.isTrue (playerOLinksStr.Contains("offerDraw")) "PlayerO should see offerDraw"
              Expect.isTrue (playerOLinksStr.Contains("viewGame")) "PlayerO should see viewGame"

              // Authenticated fallback: profile + viewGame only (no role-restricted links)
              let authKey = AffordanceMap.lookupKeyAuthenticated "/games/{gameId}" "XTurn"
              Expect.isTrue (result.ContainsKey(authKey)) "Should contain authenticated fallback key"
              let authLinksStr = result.[authKey].LinkHeaderValues.ToArray() |> String.concat " "
              Expect.isFalse (authLinksStr.Contains("makeMove")) "Auth fallback should NOT have makeMove"
              Expect.isFalse (authLinksStr.Contains("offerDraw")) "Auth fallback should NOT have offerDraw"
              Expect.isTrue (authLinksStr.Contains("viewGame")) "Auth fallback should have viewGame"

          testCase "Allow header identical across role variants"
          <| fun _ ->
              let map =
                  { Version = AffordanceMap.currentVersion
                    Entries =
                      [ { RouteTemplate = "/games/{gameId}"
                          StateKey = "XTurn"
                          AllowedMethods = [ "GET"; "POST" ]
                          LinkRelations =
                            [ { Rel = "makeMove"
                                Href = "/games/{gameId}/move"
                                Method = "POST"
                                Title = None
                                Roles = [ "PlayerX" ] } ]
                          ProfileUrl = "https://example.com/alps/games" } ] }

              let result = AffordancePreCompute.preCompute map
              let baseKey = AffordanceMap.lookupKey "/games/{gameId}" "XTurn"
              let roleKey = AffordanceMap.lookupKeyWithRole "/games/{gameId}" "XTurn" "PlayerX"

              Expect.equal
                  (result.[roleKey].AllowHeaderValue.ToString())
                  (result.[baseKey].AllowHeaderValue.ToString())
                  "Allow header should be identical for base and role variant"

          testCase "Links with Roles=[] appear in all role variants"
          <| fun _ ->
              let map =
                  { Version = AffordanceMap.currentVersion
                    Entries =
                      [ { RouteTemplate = "/games/{gameId}"
                          StateKey = "XTurn"
                          AllowedMethods = [ "GET"; "POST" ]
                          LinkRelations =
                            [ { Rel = "makeMove"
                                Href = "/games/{gameId}/move"
                                Method = "POST"
                                Title = None
                                Roles = [ "PlayerX" ] }
                              { Rel = "spectate"
                                Href = "/games/{gameId}/watch"
                                Method = "GET"
                                Title = None
                                Roles = [] } ]
                          ProfileUrl = "" } ] }

              let result = AffordancePreCompute.preCompute map
              let roleKey = AffordanceMap.lookupKeyWithRole "/games/{gameId}" "XTurn" "PlayerX"
              Expect.isTrue (result.ContainsKey(roleKey)) "Should have PlayerX entry"
              let links = result.[roleKey].LinkHeaderValues.ToArray() |> String.concat " "
              Expect.isTrue (links.Contains("spectate")) "Role-agnostic link should appear in PlayerX variant"
              Expect.isTrue (links.Contains("makeMove")) "Role-specific link should appear in PlayerX variant" ]

[<Tests>]
let roleFilteredMiddlewareTests =
    // Build lookup via preCompute from a map with role-tagged links
    let roleMap =
        { Version = AffordanceMap.currentVersion
          Entries =
            [ { RouteTemplate = "/games/{gameId}"
                StateKey = "XTurn"
                AllowedMethods = [ "GET"; "POST" ]
                LinkRelations =
                  [ { Rel = "makeMove"
                      Href = "/games/{gameId}/move"
                      Method = "POST"
                      Title = Some "Make a move"
                      Roles = [ "PlayerX" ] }
                    { Rel = "viewGame"
                      Href = "/games/{gameId}"
                      Method = "GET"
                      Title = None
                      Roles = [] } ]
                ProfileUrl = "https://example.com/alps/games" } ] }

    let roleLookup = AffordancePreCompute.preCompute roleMap

    testList
        "AffordanceMiddleware role-filtered transition links"
        [
          testCase "PlayerX in XTurn sees rel=makeMove"
          <| fun _ ->
              (withAffordanceServer
                  roleLookup
                  (fun ctx ->
                      ctx.SetStatechartState("XTurn", "XTurn", 0)
                      ctx.SetRoles(Set [ "PlayerX" ]))
                  defaultEndpoints
                  (fun client ->
                      task {
                          let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let links = getHeaderValues response "Link"
                          let allLinks = links |> String.concat " "

                          Expect.isTrue
                              (allLinks.Contains("rel=\"makeMove\""))
                              "PlayerX should see makeMove link"

                          Expect.isTrue
                              (allLinks.Contains("rel=\"viewGame\""))
                              "PlayerX should see viewGame link (available to all)"
                      }))
                  .GetAwaiter()
                  .GetResult()

          testCase "PlayerO in XTurn does NOT see rel=makeMove"
          <| fun _ ->
              (withAffordanceServer
                  roleLookup
                  (fun ctx ->
                      ctx.SetStatechartState("XTurn", "XTurn", 0)
                      ctx.SetRoles(Set [ "PlayerO" ]))
                  defaultEndpoints
                  (fun client ->
                      task {
                          let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let links = getHeaderValues response "Link"
                          let allLinks = links |> String.concat " "

                          Expect.isFalse
                              (allLinks.Contains("rel=\"makeMove\""))
                              "PlayerO should NOT see makeMove link in XTurn"

                          Expect.isTrue
                              (allLinks.Contains("rel=\"viewGame\""))
                              "PlayerO should still see viewGame link"
                      }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Unauthenticated sees all links (fallback)"
          <| fun _ ->
              (withAffordanceServer
                  roleLookup
                  (fun ctx -> ctx.SetStatechartState("XTurn", "XTurn", 0))
                  defaultEndpoints
                  (fun client ->
                      task {
                          let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let links = getHeaderValues response "Link"
                          let allLinks = links |> String.concat " "

                          Expect.isTrue
                              (allLinks.Contains("rel=\"makeMove\""))
                              "Unauthenticated should see makeMove (all links fallback)"

                          Expect.isTrue
                              (allLinks.Contains("rel=\"viewGame\""))
                              "Unauthenticated should see viewGame"
                      }))
                  .GetAwaiter()
                  .GetResult()

          // Step 3c gap: Spectator role — authenticated but no matching role-restricted links
          testCase "Spectator sees only role-agnostic links (authenticated fallback)"
          <| fun _ ->
              (withAffordanceServer
                  roleLookup
                  (fun ctx ->
                      ctx.SetStatechartState("XTurn", "XTurn", 0)
                      ctx.SetRoles(Set [ "Spectator" ]))
                  defaultEndpoints
                  (fun client ->
                      task {
                          let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let links = getHeaderValues response "Link"
                          let allLinks = links |> String.concat " "

                          Expect.isFalse
                              (allLinks.Contains("rel=\"makeMove\""))
                              "Spectator should NOT see makeMove link (PlayerX only)"

                          Expect.isTrue
                              (allLinks.Contains("rel=\"viewGame\""))
                              "Spectator should see viewGame link (available to all roles)"

                          Expect.isTrue
                              (allLinks.Contains("rel=\"profile\""))
                              "Spectator should see profile link"
                      }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Allow header identical for all roles"
          <| fun _ ->
              let mutable playerXAllow = ""
              let mutable playerOAllow = ""

              (withAffordanceServer
                  roleLookup
                  (fun ctx ->
                      ctx.SetStatechartState("XTurn", "XTurn", 0)
                      ctx.SetRoles(Set [ "PlayerX" ]))
                  defaultEndpoints
                  (fun client ->
                      task {
                          let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")
                          playerXAllow <- (getHeaderValues response "Allow") |> String.concat ", "
                      }))
                  .GetAwaiter()
                  .GetResult()

              (withAffordanceServer
                  roleLookup
                  (fun ctx ->
                      ctx.SetStatechartState("XTurn", "XTurn", 0)
                      ctx.SetRoles(Set [ "PlayerO" ]))
                  defaultEndpoints
                  (fun client ->
                      task {
                          let! (response: HttpResponseMessage) = client.GetAsync("/games/abc")
                          playerOAllow <- (getHeaderValues response "Allow") |> String.concat ", "
                      }))
                  .GetAwaiter()
                  .GetResult()

              Expect.equal playerXAllow playerOAllow "Allow header should be identical for PlayerX and PlayerO" ]
