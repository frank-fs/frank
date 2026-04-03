module Frank.Affordances.Tests.AffordanceMiddlewareTests

open System.Net
open System.Net.Http
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
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
              Expect.equal (entry.AllowHeaderValue.ToString()) "GET, HEAD, OPTIONS, POST" "Allow should list GET, HEAD, OPTIONS, and POST"

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

          testCase "Allow header for role entry reflects only role-visible methods"
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

              // Base entry uses entry.AllowedMethods directly (unchanged)
              Expect.equal
                  (result.[baseKey].AllowHeaderValue.ToString())
                  "GET, POST"
                  "Base Allow header should reflect AllowedMethods"

              // Role entry derives Allow from role-visible link methods (GET + HEAD + OPTIONS always + POST from makeMove)
              Expect.equal
                  (result.[roleKey].AllowHeaderValue.ToString())
                  "GET, HEAD, OPTIONS, POST"
                  "PlayerX Allow header should include GET, HEAD, OPTIONS, and POST (from makeMove transition)"

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

          testCase "Unauthenticated sees only role-agnostic links (HATEOAS: don't advertise unfollowable transitions)"
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

                          Expect.isFalse
                              (allLinks.Contains("rel=\"makeMove\""))
                              "Unauthenticated should NOT see makeMove (role-restricted)"

                          Expect.isTrue
                              (allLinks.Contains("rel=\"viewGame\""))
                              "Unauthenticated should see viewGame (role-agnostic)"
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

          testCase "Allow header reflects role-visible methods: PlayerX sees POST, PlayerO does not"
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

              // PlayerX has a role-specific POST transition (makeMove) plus role-agnostic GET (viewGame)
              Expect.equal playerXAllow "GET, HEAD, OPTIONS, POST" "PlayerX Allow should include POST (makeMove transition)"
              // PlayerO has no role-specific entry; falls back to auth entry with only role-agnostic links (viewGame GET)
              Expect.equal playerOAllow "GET, HEAD, OPTIONS" "PlayerO Allow should NOT include POST (no matching role transitions)" ]

[<Tests>]
let mergeTests =
    testList
        "AffordancePreCompute.merge"
        [ testCase "merges two entries: union of methods, union of rels"
          <| fun _ ->
              let entry1 =
                  { AllowHeaderValue = StringValues("GET, HEAD, OPTIONS, POST")
                    LinkHeaderValues =
                      StringValues(
                          [| "<https://example.com/alps/orders>; rel=\"profile\""
                             "</orders/{orderId}/refund>; rel=\"refund\"" |]
                      )
                    HasTemplateLinks = true
                    Methods = [ "GET"; "HEAD"; "OPTIONS"; "POST" ] }

              let entry2 =
                  { AllowHeaderValue = StringValues("GET, HEAD, OPTIONS, PUT")
                    LinkHeaderValues =
                      StringValues(
                          [| "<https://example.com/alps/orders>; rel=\"profile\""
                             "</orders/{orderId}/ship>; rel=\"ship\"" |]
                      )
                    HasTemplateLinks = true
                    Methods = [ "GET"; "HEAD"; "OPTIONS"; "PUT" ] }

              let merged = AffordancePreCompute.merge [ entry1; entry2 ]

              // Allow: union of methods, sorted
              Expect.equal
                  (merged.AllowHeaderValue.ToString())
                  "GET, HEAD, OPTIONS, POST, PUT"
                  "Merged Allow should be union of both entries' methods"

              // Link: union of rels, deduplicated (profile appears once)
              let links = merged.LinkHeaderValues.ToArray()
              Expect.equal links.Length 3 "Should have 3 links: profile + refund + ship (no duplicate profile)"

              let allLinks = links |> String.concat " "
              Expect.isTrue (allLinks.Contains("rel=\"profile\"")) "Should contain profile"
              Expect.isTrue (allLinks.Contains("rel=\"refund\"")) "Should contain refund"
              Expect.isTrue (allLinks.Contains("rel=\"ship\"")) "Should contain ship"

              // HasTemplateLinks: true if any entry has templates
              Expect.isTrue merged.HasTemplateLinks "Should have template links"

          testCase "merge preserves HasTemplateLinks=false when no entries have templates"
          <| fun _ ->
              let entry1 =
                  { AllowHeaderValue = StringValues("GET, HEAD, OPTIONS")
                    LinkHeaderValues =
                      StringValues([| "<https://example.com/alps/orders>; rel=\"profile\"" |])
                    HasTemplateLinks = false
                    Methods = [ "GET"; "HEAD"; "OPTIONS" ] }

              let entry2 =
                  { AllowHeaderValue = StringValues("GET, HEAD, OPTIONS, POST")
                    LinkHeaderValues =
                      StringValues(
                          [| "<https://example.com/alps/orders>; rel=\"profile\""
                             "</orders/123/ship>; rel=\"ship\"" |]
                      )
                    HasTemplateLinks = false
                    Methods = [ "GET"; "HEAD"; "OPTIONS"; "POST" ] }

              let merged = AffordancePreCompute.merge [ entry1; entry2 ]
              Expect.isFalse merged.HasTemplateLinks "Should be false when no entries have template links"

          testCase "merge single entry returns it unchanged"
          <| fun _ ->
              let entry =
                  { AllowHeaderValue = StringValues("GET, HEAD, OPTIONS, POST")
                    LinkHeaderValues =
                      StringValues(
                          [| "<https://example.com/alps/orders>; rel=\"profile\""
                             "</orders/{orderId}/refund>; rel=\"refund\"" |]
                      )
                    HasTemplateLinks = true
                    Methods = [ "GET"; "HEAD"; "OPTIONS"; "POST" ] }

              let merged = AffordancePreCompute.merge [ entry ]
              Expect.equal (merged.AllowHeaderValue.ToString()) "GET, HEAD, OPTIONS, POST" "Single entry methods unchanged"
              Expect.equal (merged.LinkHeaderValues.ToArray().Length) 2 "Single entry links unchanged" ]

[<Tests>]
let multiRoleMiddlewareTests =
    // Build lookup with two roles that have distinct affordances for the same (route, state)
    let multiRoleMap =
        { Version = AffordanceMap.currentVersion
          Entries =
            [ { RouteTemplate = "/orders/{orderId}"
                StateKey = "Fulfillment"
                AllowedMethods = [ "GET"; "HEAD"; "OPTIONS"; "POST"; "PUT" ]
                LinkRelations =
                  [ { Rel = "refund"
                      Href = "/orders/{orderId}/refund"
                      Method = "POST"
                      Title = None
                      Roles = [ "PaymentService" ] }
                    { Rel = "ship"
                      Href = "/orders/{orderId}/ship"
                      Method = "POST"
                      Title = None
                      Roles = [ "Warehouse" ] }
                    { Rel = "pack"
                      Href = "/orders/{orderId}/pack"
                      Method = "PUT"
                      Title = None
                      Roles = [ "Warehouse" ] }
                    { Rel = "view-order"
                      Href = "/orders/{orderId}"
                      Method = "GET"
                      Title = None
                      Roles = [] } ]
                ProfileUrl = "https://example.com/alps/orders" } ] }

    let multiRoleLookup = AffordancePreCompute.preCompute multiRoleMap

    let orderEndpoints (endpoints: IEndpointRouteBuilder) =
        endpoints.MapGet(
            "/orders/{orderId}",
            RequestDelegate(fun ctx -> ctx.Response.WriteAsync("OK"))
        )
        |> ignore

    testList
        "AffordanceMiddleware multi-role resolution"
        [
          // Acceptance test 1: Multi-role user sees union of affordances
          testCase "multi-role user sees union of methods and rels from both PaymentService and Warehouse"
          <| fun _ ->
              (withAffordanceServer
                  multiRoleLookup
                  (fun ctx ->
                      ctx.SetStatechartState("Fulfillment", "Fulfillment", 0)
                      ctx.SetRoles(Set [ "PaymentService"; "Warehouse" ]))
                  orderEndpoints
                  (fun client ->
                      task {
                          let! (response: HttpResponseMessage) = client.GetAsync("/orders/o1")

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          // Allow header includes methods from BOTH roles
                          let allow = getHeaderValues response "Allow" |> String.concat ", "
                          Expect.isTrue (allow.Contains("POST")) "Allow should include POST (from refund + ship)"
                          Expect.isTrue (allow.Contains("PUT")) "Allow should include PUT (from pack)"
                          Expect.isTrue (allow.Contains("GET")) "Allow should include GET"

                          // Link header includes rels from BOTH roles
                          let links = getHeaderValues response "Link"
                          let allLinks = links |> String.concat " "
                          Expect.isTrue (allLinks.Contains("rel=\"refund\"")) "Should contain refund (PaymentService)"
                          Expect.isTrue (allLinks.Contains("rel=\"ship\"")) "Should contain ship (Warehouse)"
                          Expect.isTrue (allLinks.Contains("rel=\"pack\"")) "Should contain pack (Warehouse)"
                          Expect.isTrue (allLinks.Contains("rel=\"view-order\"")) "Should contain view-order (all roles)"
                          Expect.isTrue (allLinks.Contains("rel=\"profile\"")) "Should contain profile"

                          // Vary: Authorization must be present for role-dependent responses
                          let vary = getHeaderValues response "Vary" |> String.concat ", "
                          Expect.isTrue (vary.Contains("Authorization")) "Vary should include Authorization for multi-role response"
                      }))
                  .GetAwaiter()
                  .GetResult()

          // Acceptance test 2: Single-role behavior unchanged
          testCase "single-role user sees only their role's affordances (no regression)"
          <| fun _ ->
              (withAffordanceServer
                  multiRoleLookup
                  (fun ctx ->
                      ctx.SetStatechartState("Fulfillment", "Fulfillment", 0)
                      ctx.SetRoles(Set [ "PaymentService" ]))
                  orderEndpoints
                  (fun client ->
                      task {
                          let! (response: HttpResponseMessage) = client.GetAsync("/orders/o1")

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          let links = getHeaderValues response "Link"
                          let allLinks = links |> String.concat " "
                          Expect.isTrue (allLinks.Contains("rel=\"refund\"")) "PaymentService should see refund"
                          Expect.isFalse (allLinks.Contains("rel=\"ship\"")) "PaymentService should NOT see ship"
                          Expect.isFalse (allLinks.Contains("rel=\"pack\"")) "PaymentService should NOT see pack"
                          Expect.isTrue (allLinks.Contains("rel=\"view-order\"")) "Should see view-order (all roles)"
                      }))
                  .GetAwaiter()
                  .GetResult()

          // Acceptance test 3: No role duplication in merged output
          testCase "multi-role merged output has no duplicate methods or rels"
          <| fun _ ->
              (withAffordanceServer
                  multiRoleLookup
                  (fun ctx ->
                      ctx.SetStatechartState("Fulfillment", "Fulfillment", 0)
                      ctx.SetRoles(Set [ "PaymentService"; "Warehouse" ]))
                  orderEndpoints
                  (fun client ->
                      task {
                          let! (response: HttpResponseMessage) = client.GetAsync("/orders/o1")

                          Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                          // Check Allow header has no duplicate methods
                          let allow = getHeaderValues response "Allow" |> String.concat ", "
                          let methods = allow.Split(',') |> Array.map (fun s -> s.Trim()) |> Array.toList
                          Expect.equal methods (methods |> List.distinct) "Allow header should have no duplicate methods"

                          // Check Link header has no duplicate rels
                          let links = getHeaderValues response "Link"
                          Expect.equal links (links |> List.distinct) "Link header should have no duplicate rels"
                      }))
                  .GetAwaiter()
                  .GetResult() ]
