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
                                Title = Some "Make a move" } ]
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
              Expect.equal (result.[wonKey].AllowHeaderValue.ToString()) "GET" "Won allows only GET" ]
