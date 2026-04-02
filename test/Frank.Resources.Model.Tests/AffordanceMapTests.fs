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
                  Expect.contains entry.AllowedMethods "OPTIONS" (sprintf "OPTIONS must be in AllowedMethods for state %s" entry.StateKey)

          // toKebabCase
          testCase "toKebabCase converts PascalCase to kebab-case"
          <| fun _ ->
              Expect.equal (AffordanceMap.toKebabCase "AuthorizePayment") "authorize-payment" "PascalCase two words"
              Expect.equal (AffordanceMap.toKebabCase "PlaceOrder") "place-order" "PascalCase two words"
              Expect.equal (AffordanceMap.toKebabCase "makeMove") "make-move" "camelCase"
              Expect.equal (AffordanceMap.toKebabCase "getGame") "get-game" "camelCase get prefix"

          testCase "toKebabCase leaves single-word lowercase unchanged"
          <| fun _ ->
              Expect.equal (AffordanceMap.toKebabCase "play") "play" "already lowercase"

          testCase "toKebabCase handles single uppercase word"
          <| fun _ ->
              Expect.equal (AffordanceMap.toKebabCase "Move") "move" "single PascalCase word"

          testCase "toKebabCase handles empty string"
          <| fun _ ->
              Expect.equal (AffordanceMap.toKebabCase "") "" "empty string round-trips"

          testCase "toKebabCase keeps acronym at start together"
          <| fun _ ->
              Expect.equal (AffordanceMap.toKebabCase "OAuthCallback") "oauth-callback" "acronym at start"

          testCase "toKebabCase keeps acronym in middle together"
          <| fun _ ->
              Expect.equal (AffordanceMap.toKebabCase "getHTMLParser") "get-html-parser" "acronym in middle"

          testCase "toKebabCase handles all-caps"
          <| fun _ ->
              Expect.equal (AffordanceMap.toKebabCase "HTML") "html" "all-caps becomes lowercase"

          testCase "toKebabCase handles single letter before word"
          <| fun _ ->
              Expect.equal (AffordanceMap.toKebabCase "ATest") "atest" "single letter word"

          testCase "toKebabCase splits three-letter acronym prefix"
          <| fun _ ->
              Expect.equal (AffordanceMap.toKebabCase "ABTest") "ab-test" "three-letter acronym prefix"

          testCase "toKebabCase handles multiple consecutive acronyms"
          <| fun _ ->
              Expect.equal (AffordanceMap.toKebabCase "HTTPSURLParser") "httpsurl-parser" "multiple acronyms"

          // fromStatechart
          testCase "fromStatechart produces one entry per state"
          <| fun _ ->
              let sc: ExtractedStatechart =
                  { RouteTemplate = "/games/{gameId}"
                    StateNames = [ "XTurn"; "OTurn"; "GameOver" ]
                    InitialStateKey = "XTurn"
                    GuardNames = []
                    StateMetadata = Map.empty
                    Roles = []
                    Transitions =
                      [ { Event = "makeMove"
                          Source = "XTurn"
                          Target = "OTurn"
                          Guard = None
                          Constraint = Unrestricted }
                        { Event = "makeMove"
                          Source = "OTurn"
                          Target = "XTurn"
                          Guard = None
                          Constraint = Unrestricted } ] }

              let map = AffordanceMap.fromStatechart "http://example.com/alps" sc
              Expect.equal map.Entries.Length 3 "One entry per state"

          testCase "fromStatechart uses currentVersion"
          <| fun _ ->
              let sc: ExtractedStatechart =
                  { RouteTemplate = "/items"
                    StateNames = [ "Active" ]
                    InitialStateKey = "Active"
                    GuardNames = []
                    StateMetadata = Map.empty
                    Roles = []
                    Transitions = [] }

              let map = AffordanceMap.fromStatechart "http://example.com/alps" sc
              Expect.equal map.Version AffordanceMap.currentVersion "Version must match currentVersion"

          testCase "fromStatechart converts event names to kebab-case rel"
          <| fun _ ->
              let sc: ExtractedStatechart =
                  { RouteTemplate = "/games/{gameId}"
                    StateNames = [ "XTurn" ]
                    InitialStateKey = "XTurn"
                    GuardNames = []
                    StateMetadata = Map.empty
                    Roles = []
                    Transitions =
                      [ { Event = "makeMove"
                          Source = "XTurn"
                          Target = "OTurn"
                          Guard = None
                          Constraint = Unrestricted } ] }

              let map = AffordanceMap.fromStatechart "http://example.com/alps" sc
              let entry = map.Entries |> List.head
              let rel = entry.LinkRelations |> List.head |> _.Rel
              Expect.equal rel "make-move" "Event name should be kebab-cased"

          testCase "fromStatechart sets Method to POST for all transitions"
          <| fun _ ->
              let sc: ExtractedStatechart =
                  { RouteTemplate = "/orders/{orderId}"
                    StateNames = [ "Pending" ]
                    InitialStateKey = "Pending"
                    GuardNames = []
                    StateMetadata = Map.empty
                    Roles = []
                    Transitions =
                      [ { Event = "authorizePayment"
                          Source = "Pending"
                          Target = "Authorized"
                          Guard = None
                          Constraint = Unrestricted } ] }

              let map = AffordanceMap.fromStatechart "http://example.com/alps" sc
              let entry = map.Entries |> List.head
              let methods = entry.LinkRelations |> List.map _.Method
              Expect.allEqual methods "POST" "All transition methods must be POST"

          testCase "fromStatechart AllowedMethods includes GET OPTIONS POST when transitions exist"
          <| fun _ ->
              let sc: ExtractedStatechart =
                  { RouteTemplate = "/games/{gameId}"
                    StateNames = [ "XTurn" ]
                    InitialStateKey = "XTurn"
                    GuardNames = []
                    StateMetadata = Map.empty
                    Roles = []
                    Transitions =
                      [ { Event = "makeMove"
                          Source = "XTurn"
                          Target = "OTurn"
                          Guard = None
                          Constraint = Unrestricted } ] }

              let map = AffordanceMap.fromStatechart "http://example.com/alps" sc
              let entry = map.Entries |> List.head
              Expect.contains entry.AllowedMethods "GET" "GET required"
              Expect.contains entry.AllowedMethods "OPTIONS" "OPTIONS required per RFC 7231"
              Expect.contains entry.AllowedMethods "POST" "POST required when transitions exist"

          testCase "fromStatechart AllowedMethods excludes POST for terminal state"
          <| fun _ ->
              let sc: ExtractedStatechart =
                  { RouteTemplate = "/games/{gameId}"
                    StateNames = [ "GameOver" ]
                    InitialStateKey = "GameOver"
                    GuardNames = []
                    StateMetadata = Map.empty
                    Roles = []
                    Transitions = [] }

              let map = AffordanceMap.fromStatechart "http://example.com/alps" sc
              let entry = map.Entries |> List.head
              Expect.contains entry.AllowedMethods "GET" "GET required for terminal state"
              Expect.contains entry.AllowedMethods "OPTIONS" "OPTIONS required for terminal state"
              Expect.isFalse (List.contains "POST" entry.AllowedMethods) "POST must not appear when no transitions"
              Expect.isEmpty entry.LinkRelations "No link relations for terminal state"

          testCase "fromStatechart Unrestricted constraint maps to empty Roles"
          <| fun _ ->
              let sc: ExtractedStatechart =
                  { RouteTemplate = "/games/{gameId}"
                    StateNames = [ "XTurn" ]
                    InitialStateKey = "XTurn"
                    GuardNames = []
                    StateMetadata = Map.empty
                    Roles = []
                    Transitions =
                      [ { Event = "makeMove"
                          Source = "XTurn"
                          Target = "OTurn"
                          Guard = None
                          Constraint = Unrestricted } ] }

              let map = AffordanceMap.fromStatechart "http://example.com/alps" sc
              let lr = map.Entries |> List.head |> _.LinkRelations |> List.head
              Expect.isEmpty lr.Roles "Unrestricted constraint should yield empty Roles list"

          testCase "fromStatechart RestrictedTo constraint maps to Roles list"
          <| fun _ ->
              let sc: ExtractedStatechart =
                  { RouteTemplate = "/games/{gameId}"
                    StateNames = [ "XTurn" ]
                    InitialStateKey = "XTurn"
                    GuardNames = []
                    StateMetadata = Map.empty
                    Roles = []
                    Transitions =
                      [ { Event = "makeMove"
                          Source = "XTurn"
                          Target = "OTurn"
                          Guard = None
                          Constraint = RestrictedTo [ "PlayerX" ] } ] }

              let map = AffordanceMap.fromStatechart "http://example.com/alps" sc
              let lr = map.Entries |> List.head |> _.LinkRelations |> List.head
              Expect.equal lr.Roles [ "PlayerX" ] "RestrictedTo roles should be preserved"

          testCase "fromStatechart only includes transitions sourced from each state"
          <| fun _ ->
              let sc: ExtractedStatechart =
                  { RouteTemplate = "/games/{gameId}"
                    StateNames = [ "XTurn"; "OTurn" ]
                    InitialStateKey = "XTurn"
                    GuardNames = []
                    StateMetadata = Map.empty
                    Roles = []
                    Transitions =
                      [ { Event = "makeMove"
                          Source = "XTurn"
                          Target = "OTurn"
                          Guard = None
                          Constraint = RestrictedTo [ "PlayerX" ] }
                        { Event = "makeMove"
                          Source = "OTurn"
                          Target = "XTurn"
                          Guard = None
                          Constraint = RestrictedTo [ "PlayerO" ] } ] }

              let map = AffordanceMap.fromStatechart "http://example.com/alps" sc
              let xEntry = map.Entries |> List.find (fun e -> e.StateKey = "XTurn")
              let oEntry = map.Entries |> List.find (fun e -> e.StateKey = "OTurn")
              Expect.equal xEntry.LinkRelations.Length 1 "XTurn should have exactly one transition"
              Expect.equal (xEntry.LinkRelations |> List.head |> _.Roles) [ "PlayerX" ] "XTurn transition is PlayerX only"
              Expect.equal oEntry.LinkRelations.Length 1 "OTurn should have exactly one transition"
              Expect.equal (oEntry.LinkRelations |> List.head |> _.Roles) [ "PlayerO" ] "OTurn transition is PlayerO only"

          testCase "fromStatechart sets ProfileUrl from baseUri and route slug"
          <| fun _ ->
              let sc: ExtractedStatechart =
                  { RouteTemplate = "/games/{gameId}"
                    StateNames = [ "XTurn" ]
                    InitialStateKey = "XTurn"
                    GuardNames = []
                    StateMetadata = Map.empty
                    Roles = []
                    Transitions = [] }

              let map = AffordanceMap.fromStatechart "http://example.com/alps" sc
              let entry = map.Entries |> List.head
              Expect.equal entry.ProfileUrl "http://example.com/alps/games" "ProfileUrl should use resource slug"

          testCase "fromStatechart sets RouteTemplate on each entry"
          <| fun _ ->
              let sc: ExtractedStatechart =
                  { RouteTemplate = "/orders/{orderId}"
                    StateNames = [ "Pending"; "Shipped" ]
                    InitialStateKey = "Pending"
                    GuardNames = []
                    StateMetadata = Map.empty
                    Roles = []
                    Transitions = [] }

              let map = AffordanceMap.fromStatechart "http://example.com/alps" sc
              for entry in map.Entries do
                  Expect.equal entry.RouteTemplate "/orders/{orderId}" "RouteTemplate must match statechart"

          testCase "fromStatechart Href on link relations equals route template"
          <| fun _ ->
              let sc: ExtractedStatechart =
                  { RouteTemplate = "/games/{gameId}"
                    StateNames = [ "XTurn" ]
                    InitialStateKey = "XTurn"
                    GuardNames = []
                    StateMetadata = Map.empty
                    Roles = []
                    Transitions =
                      [ { Event = "makeMove"
                          Source = "XTurn"
                          Target = "OTurn"
                          Guard = None
                          Constraint = Unrestricted } ] }

              let map = AffordanceMap.fromStatechart "http://example.com/alps" sc
              let lr = map.Entries |> List.head |> _.LinkRelations |> List.head
              Expect.equal lr.Href "/games/{gameId}" "Href should be the route template"

          testCase "fromStatechart returns empty Entries for statechart with no states"
          <| fun _ ->
              let sc: ExtractedStatechart =
                  { RouteTemplate = "/items"
                    StateNames = []
                    InitialStateKey = ""
                    GuardNames = []
                    StateMetadata = Map.empty
                    Roles = []
                    Transitions = [] }

              let map = AffordanceMap.fromStatechart "http://example.com/alps" sc
              Expect.isEmpty map.Entries "No states means no entries" ]
