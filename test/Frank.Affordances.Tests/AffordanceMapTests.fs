module Frank.Affordances.Tests.AffordanceMapTests

open Expecto
open Frank.Affordances

[<Tests>]
let affordanceMapTests =
    testList
        "AffordanceMap"
        [ testCase "lookupKey combines route and state"
          <| fun _ ->
              let key = AffordanceMap.lookupKey "/games/{gameId}" "XTurn"
              Expect.equal key "/games/{gameId}|XTurn" "Should combine with pipe separator"

          testCase "lookupKey with wildcard for stateless resource"
          <| fun _ ->
              let key = AffordanceMap.lookupKey "/health" AffordanceMap.WildcardStateKey
              Expect.equal key "/health|*" "Should use wildcard"

          testCase "tryFind returns matching entry"
          <| fun _ ->
              let entry =
                  { RouteTemplate = "/games/{gameId}"
                    StateKey = "XTurn"
                    AllowedMethods = [ "GET"; "POST" ]
                    LinkRelations = []
                    ProfileUrl = "https://example.com/alps/games" }

              let map =
                  { Version = AffordanceMap.currentVersion
                    Entries = [ entry ] }

              let result = AffordanceMap.tryFind "/games/{gameId}" "XTurn" map
              Expect.isSome result "Should find the entry"
              Expect.equal result.Value.AllowedMethods [ "GET"; "POST" ] "Should have correct methods"

          testCase "tryFind returns None for missing entry"
          <| fun _ ->
              let map =
                  { Version = AffordanceMap.currentVersion
                    Entries = [] }

              let result = AffordanceMap.tryFind "/missing" "state" map
              Expect.isNone result "Should return None for missing"

          testCase "currentVersion is set"
          <| fun _ -> Expect.isNotEmpty AffordanceMap.currentVersion "Version should be non-empty" ]
