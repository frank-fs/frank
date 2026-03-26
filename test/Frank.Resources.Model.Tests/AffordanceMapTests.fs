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
              Expect.equal key "/games/{gameId}|XTurn|PlayerX" "Should combine with pipe separators"

          // #199: rel="self" Link headers are vacuous
          testCase "generateFromResources filters out rel=self from LinkRelations"
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
                          LinkRelation = "post"
                          IsSafe = false } ] }

              let map = AffordanceMap.generateFromResources [ resource ] "http://example.com/alps"
              let entry = map.Entries |> List.head
              Expect.contains entry.AllowedMethods "GET" "GET should still be in AllowedMethods"
              Expect.contains entry.AllowedMethods "POST" "POST should still be in AllowedMethods"
              let rels = entry.LinkRelations |> List.map _.Rel
              Expect.isFalse (rels |> List.contains "self") "rel=self should be filtered from LinkRelations"
              Expect.isTrue (rels |> List.contains "post") "other rels should remain"

          // #199: stateful resource — rel="self" filtered per-state
          testCase "generateFromResources filters rel=self from stateful resource entries"
          <| fun _ ->
              let resource: RuntimeResource =
                  { RouteTemplate = "/games/{gameId}"
                    ResourceSlug = "games"
                    Statechart =
                      { StateNames = [ "XTurn"; "Won" ]
                        InitialStateKey = "XTurn"
                        GuardNames = []
                        StateMetadata = Map.empty }
                    HttpCapabilities =
                      [ { Method = "GET"
                          StateKey = "*"
                          LinkRelation = "self"
                          IsSafe = true }
                        { Method = "POST"
                          StateKey = "XTurn"
                          LinkRelation = "makeMove"
                          IsSafe = false } ] }

              let map = AffordanceMap.generateFromResources [ resource ] "http://example.com/alps"
              // XTurn state: GET (self, filtered) + POST (makeMove, kept)
              let xTurn = map.Entries |> List.find (fun e -> e.StateKey = "XTurn")
              Expect.contains xTurn.AllowedMethods "GET" "XTurn should have GET in Allow"
              Expect.contains xTurn.AllowedMethods "POST" "XTurn should have POST in Allow"
              let xTurnRels = xTurn.LinkRelations |> List.map _.Rel
              Expect.isFalse (xTurnRels |> List.contains "self") "XTurn should not have rel=self"
              Expect.isTrue (xTurnRels |> List.contains "makeMove") "XTurn should have makeMove"
              // Won state: GET (self, filtered) only — no link relations at all
              let won = map.Entries |> List.find (fun e -> e.StateKey = "Won")
              Expect.contains won.AllowedMethods "GET" "Won should have GET in Allow"
              Expect.isEmpty won.LinkRelations "Won should have no link relations (only self, which is filtered)"

          // F-1: OPTIONS must always be in AllowedMethods (RFC 7231 §4.3.7)
          testCase "generateFromResources includes OPTIONS in AllowedMethods for stateless resource"
          <| fun _ ->
              let resource: RuntimeResource =
                  { RouteTemplate = "/items"
                    ResourceSlug = "items"
                    Statechart = RuntimeStatechart.empty
                    HttpCapabilities =
                      [ { Method = "GET"
                          StateKey = "*"
                          LinkRelation = "self"
                          IsSafe = true }
                        { Method = "POST"
                          StateKey = "*"
                          LinkRelation = "create"
                          IsSafe = false } ] }

              let map = AffordanceMap.generateFromResources [ resource ] "http://example.com/alps"
              let entry = map.Entries |> List.head
              Expect.contains entry.AllowedMethods "OPTIONS" "OPTIONS must be in AllowedMethods per RFC 7231 §4.3.7"

          // F-1: OPTIONS in AllowedMethods for each state of a stateful resource
          testCase "generateFromResources includes OPTIONS in AllowedMethods per state"
          <| fun _ ->
              let resource: RuntimeResource =
                  { RouteTemplate = "/games/{gameId}"
                    ResourceSlug = "games"
                    Statechart =
                      { StateNames = [ "XTurn"; "Won" ]
                        InitialStateKey = "XTurn"
                        GuardNames = []
                        StateMetadata = Map.empty }
                    HttpCapabilities =
                      [ { Method = "GET"
                          StateKey = "*"
                          LinkRelation = "self"
                          IsSafe = true }
                        { Method = "POST"
                          StateKey = "XTurn"
                          LinkRelation = "makeMove"
                          IsSafe = false } ] }

              let map = AffordanceMap.generateFromResources [ resource ] "http://example.com/alps"
              for entry in map.Entries do
                  Expect.contains entry.AllowedMethods "OPTIONS" (sprintf "OPTIONS must be in AllowedMethods for state %s" entry.StateKey) ]
