module Frank.Resources.Model.Tests.AffordanceMapTests

open Expecto
open Frank.Resources.Model

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
                  { AffordanceMapEntry.RouteTemplate = "/games/{gameId}"
                    AffordanceMapEntry.StateKey = "XTurn"
                    AffordanceMapEntry.AllowedMethods = [ "GET"; "POST" ]
                    AffordanceMapEntry.LinkRelations = []
                    AffordanceMapEntry.ProfileUrl = "https://example.com/alps/games" }

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
          <| fun _ -> Expect.isNotEmpty AffordanceMap.currentVersion "Version should be non-empty"

          testCase "AffordanceLinkRelation with Roles constructs correctly"
          <| fun _ ->
              let lr =
                  { AffordanceLinkRelation.Rel = "makeMove"
                    AffordanceLinkRelation.Href = "/games/{gameId}/move"
                    AffordanceLinkRelation.Method = "POST"
                    AffordanceLinkRelation.Title = Some "Make a move"
                    AffordanceLinkRelation.Roles = [ "PlayerX"; "PlayerO" ] }

              Expect.equal lr.Roles [ "PlayerX"; "PlayerO" ] "Should preserve roles"

          testCase "AffordanceLinkRelation with empty Roles means all roles"
          <| fun _ ->
              let lr =
                  { AffordanceLinkRelation.Rel = "getState"
                    AffordanceLinkRelation.Href = "/games/{gameId}"
                    AffordanceLinkRelation.Method = "GET"
                    AffordanceLinkRelation.Title = None
                    AffordanceLinkRelation.Roles = [] }

              Expect.isEmpty lr.Roles "Empty roles means available to all"

          testCase "lookupKeyWithRole combines route, state, and role"
          <| fun _ ->
              let key = AffordanceMap.lookupKeyWithRole "/games/{gameId}" "XTurn" "PlayerX"
              Expect.equal key "/games/{gameId}|XTurn|PlayerX" "Should combine with pipe separators" ]
