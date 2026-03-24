module Frank.Cli.Core.Tests.Unified.ProjectionIntegrationTests

open System.Text.Json
open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Alps.JsonParser
open Frank.Statecharts.TransitionExtractor
open Frank.Resources.Model
open Frank.Resources.Model.Projection
open Frank.Cli.Core.Unified.ProjectionPipeline

// ══════════════════════════════════════════════════════════════════════════════
// Shared ALPS JSON fixture (TicTacToe with role-based projection)
// ══════════════════════════════════════════════════════════════════════════════

let private alpsJson =
    """
{
  "alps": {
    "version": "1.0",
    "doc": { "format": "text", "value": "Tic-Tac-Toe game state machine with role-based projection" },
    "ext": [
      { "id": "projectedRole", "value": "PlayerX,PlayerO,Spectator" }
    ],
    "descriptor": [
      {
        "id": "position",
        "type": "semantic",
        "doc": { "format": "text", "value": "Board position (0-8)" }
      },
      {
        "id": "player",
        "type": "semantic",
        "doc": { "format": "text", "value": "Player identifier (X or O)" }
      },
      {
        "id": "XTurn",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Player X's turn" },
        "descriptor": [
          {
            "id": "makeMove-XTurn-OTurn",
            "type": "unsafe",
            "rt": "#OTurn",
            "doc": { "format": "text", "value": "Player X makes a move, transitions to O's turn" },
            "descriptor": [
              { "href": "#position" },
              { "href": "#player" }
            ],
            "ext": [
              { "id": "guard", "value": "TurnGuard" },
              { "id": "projectedRole", "value": "PlayerX" },
              { "id": "availableInStates", "value": "XTurn" }
            ]
          },
          {
            "id": "makeMove-XTurn-XWins",
            "type": "unsafe",
            "rt": "#XWins",
            "doc": { "format": "text", "value": "Player X makes a winning move" },
            "descriptor": [
              { "href": "#position" },
              { "href": "#player" }
            ],
            "ext": [
              { "id": "guard", "value": "TurnGuard" },
              { "id": "projectedRole", "value": "PlayerX" },
              { "id": "availableInStates", "value": "XTurn" }
            ]
          },
          {
            "id": "makeMove-XTurn-Draw",
            "type": "unsafe",
            "rt": "#Draw",
            "doc": { "format": "text", "value": "Player X makes a move that fills the board" },
            "descriptor": [
              { "href": "#position" },
              { "href": "#player" }
            ],
            "ext": [
              { "id": "guard", "value": "TurnGuard" },
              { "id": "projectedRole", "value": "PlayerX" },
              { "id": "availableInStates", "value": "XTurn" }
            ]
          },
          { "href": "#getGame" }
        ]
      },
      {
        "id": "OTurn",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Player O's turn" },
        "descriptor": [
          {
            "id": "makeMove-OTurn-XTurn",
            "type": "unsafe",
            "rt": "#XTurn",
            "doc": { "format": "text", "value": "Player O makes a move, transitions to X's turn" },
            "descriptor": [
              { "href": "#position" },
              { "href": "#player" }
            ],
            "ext": [
              { "id": "guard", "value": "TurnGuard" },
              { "id": "projectedRole", "value": "PlayerO" },
              { "id": "availableInStates", "value": "OTurn" }
            ]
          },
          {
            "id": "makeMove-OTurn-OWins",
            "type": "unsafe",
            "rt": "#OWins",
            "doc": { "format": "text", "value": "Player O makes a winning move" },
            "descriptor": [
              { "href": "#position" },
              { "href": "#player" }
            ],
            "ext": [
              { "id": "guard", "value": "TurnGuard" },
              { "id": "projectedRole", "value": "PlayerO" },
              { "id": "availableInStates", "value": "OTurn" }
            ]
          },
          {
            "id": "makeMove-OTurn-Draw",
            "type": "unsafe",
            "rt": "#Draw",
            "doc": { "format": "text", "value": "Player O makes a move that fills the board" },
            "descriptor": [
              { "href": "#position" },
              { "href": "#player" }
            ],
            "ext": [
              { "id": "guard", "value": "TurnGuard" },
              { "id": "projectedRole", "value": "PlayerO" },
              { "id": "availableInStates", "value": "OTurn" }
            ]
          },
          { "href": "#getGame" }
        ]
      },
      {
        "id": "XWins",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Player X has won" },
        "descriptor": [
          { "href": "#getGame" }
        ]
      },
      {
        "id": "OWins",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Player O has won" },
        "descriptor": [
          { "href": "#getGame" }
        ]
      },
      {
        "id": "Draw",
        "type": "semantic",
        "doc": { "format": "text", "value": "State: Game ended in a draw" },
        "descriptor": [
          { "href": "#getGame" }
        ]
      },
      {
        "id": "getGame",
        "type": "safe",
        "rt": "#XTurn",
        "doc": { "format": "text", "value": "View the current game state" }
      }
    ],
    "link": [
      { "rel": "self", "href": "http://example.com/alps/tic-tac-toe" }
    ]
  }
}
"""

// ══════════════════════════════════════════════════════════════════════════════
// Helpers
// ══════════════════════════════════════════════════════════════════════════════

/// Parse the ALPS JSON and return the StatechartDocument (fails test on error).
let private parseDocument () : StatechartDocument =
    let result = parseAlpsJson alpsJson
    Expect.isEmpty result.Errors "ALPS JSON should parse without errors"
    result.Document

/// Build an ExtractedStatechart from parsed transitions and roles.
let private buildStatechart (doc: StatechartDocument) : ExtractedStatechart =
    let transitions = extract doc
    let roles = extractRoles doc

    let stateNames =
        transitions
        |> List.collect (fun t -> [ t.Source; t.Target ])
        |> List.distinct
        |> List.sort

    { RouteTemplate = "/games/{gameId}"
      StateNames = stateNames
      InitialStateKey = "XTurn"
      GuardNames = [ "TurnGuard" ]
      StateMetadata = Map.empty
      Roles = roles
      Transitions = transitions }

// ══════════════════════════════════════════════════════════════════════════════
// Tests
// ══════════════════════════════════════════════════════════════════════════════

[<Tests>]
let projectionIntegrationTests =
    testList
        "Projection integration"
        [ testList
              "Parse ALPS JSON and extract transitions"
              [ testCase "parses without errors"
                <| fun _ ->
                    let result = parseAlpsJson alpsJson
                    Expect.isEmpty result.Errors "No parse errors"
                    Expect.isEmpty result.Warnings "No parse warnings"

                testCase "extracts transitions from parsed document"
                <| fun _ ->
                    let doc = parseDocument ()
                    let transitions = extract doc
                    Expect.isGreaterThan transitions.Length 0 "Should extract at least one transition"

                testCase "XTurn makeMove transitions are restricted to PlayerX"
                <| fun _ ->
                    let doc = parseDocument ()
                    let transitions = extract doc

                    let xTurnMoves =
                        transitions
                        |> List.filter (fun t -> t.Source = "XTurn" && t.Event.StartsWith("makeMove"))

                    Expect.isGreaterThan xTurnMoves.Length 0 "Should have makeMove transitions from XTurn"

                    for t in xTurnMoves do
                        Expect.equal
                            t.Constraint
                            (RestrictedTo [ "PlayerX" ])
                            $"XTurn transition %s{t.Event} should be restricted to PlayerX"

                testCase "OTurn makeMove transitions are restricted to PlayerO"
                <| fun _ ->
                    let doc = parseDocument ()
                    let transitions = extract doc

                    let oTurnMoves =
                        transitions
                        |> List.filter (fun t -> t.Source = "OTurn" && t.Event.StartsWith("makeMove"))

                    Expect.isGreaterThan oTurnMoves.Length 0 "Should have makeMove transitions from OTurn"

                    for t in oTurnMoves do
                        Expect.equal
                            t.Constraint
                            (RestrictedTo [ "PlayerO" ])
                            $"OTurn transition %s{t.Event} should be restricted to PlayerO"

                testCase "getGame transitions from states are unrestricted"
                <| fun _ ->
                    let doc = parseDocument ()
                    let transitions = extract doc

                    let getGameTransitions =
                        transitions |> List.filter (fun t -> t.Event.StartsWith("getGame"))

                    Expect.isGreaterThan getGameTransitions.Length 0 "Should have getGame transitions"

                    for t in getGameTransitions do
                        Expect.equal t.Constraint Unrestricted $"getGame from %s{t.Source} should be Unrestricted" ]

          testList
              "Extract transitions and project per role"
              [ testCase "PlayerX projection includes only XTurn makeMove transitions"
                <| fun _ ->
                    let doc = parseDocument ()
                    let statechart = buildStatechart doc
                    let projected = projectForRole "PlayerX" statechart

                    let makeMoves =
                        projected.Transitions |> List.filter (fun t -> t.Event.StartsWith("makeMove"))

                    Expect.isGreaterThan makeMoves.Length 0 "PlayerX should have makeMove transitions"

                    for t in makeMoves do
                        Expect.equal
                            t.Source
                            "XTurn"
                            $"PlayerX makeMove transition should be from XTurn, got %s{t.Source}"

                testCase "PlayerO projection includes only OTurn makeMove transitions"
                <| fun _ ->
                    let doc = parseDocument ()
                    let statechart = buildStatechart doc
                    let projected = projectForRole "PlayerO" statechart

                    let makeMoves =
                        projected.Transitions |> List.filter (fun t -> t.Event.StartsWith("makeMove"))

                    Expect.isGreaterThan makeMoves.Length 0 "PlayerO should have makeMove transitions"

                    for t in makeMoves do
                        Expect.equal
                            t.Source
                            "OTurn"
                            $"PlayerO makeMove transition should be from OTurn, got %s{t.Source}"

                testCase "Spectator projection has no makeMove transitions"
                <| fun _ ->
                    let doc = parseDocument ()
                    let statechart = buildStatechart doc
                    let projected = projectForRole "Spectator" statechart

                    let makeMoves =
                        projected.Transitions |> List.filter (fun t -> t.Event.StartsWith("makeMove"))

                    Expect.isEmpty makeMoves "Spectator should have no makeMove transitions"

                testCase "all projections retain getGame transitions"
                <| fun _ ->
                    let doc = parseDocument ()
                    let statechart = buildStatechart doc

                    for role in [ "PlayerX"; "PlayerO"; "Spectator" ] do
                        let projected = projectForRole role statechart

                        let getGames =
                            projected.Transitions |> List.filter (fun t -> t.Event.StartsWith("getGame"))

                        Expect.isGreaterThan getGames.Length 0 $"%s{role} should retain getGame transitions" ]

          testList
              "Completeness check"
              [ testCase "findOrphanedTransitions returns empty for all roles"
                <| fun _ ->
                    let doc = parseDocument ()
                    let statechart = buildStatechart doc
                    let projections = projectAll statechart
                    let orphaned = findOrphanedTransitions statechart projections
                    Expect.isEmpty orphaned "All transitions should be covered by at least one role projection"

                testCase "projectAll produces a projection for each role"
                <| fun _ ->
                    let doc = parseDocument ()
                    let statechart = buildStatechart doc
                    let projections = projectAll statechart
                    Expect.equal projections.Count 3 "Should have 3 role projections"
                    Expect.isTrue (projections.ContainsKey "PlayerX") "Should have PlayerX projection"
                    Expect.isTrue (projections.ContainsKey "PlayerO") "Should have PlayerO projection"
                    Expect.isTrue (projections.ContainsKey "Spectator") "Should have Spectator projection" ]

          testList
              "Extract roles from document"
              [ testCase "document-level roles are extracted"
                <| fun _ ->
                    let doc = parseDocument ()
                    let roles = extractRoles doc
                    let roleNames = roles |> List.map (fun r -> r.Name)

                    Expect.contains roleNames "PlayerX" "Should contain PlayerX"
                    Expect.contains roleNames "PlayerO" "Should contain PlayerO"
                    Expect.contains roleNames "Spectator" "Should contain Spectator"

                testCase "exactly 3 roles extracted"
                <| fun _ ->
                    let doc = parseDocument ()
                    let roles = extractRoles doc
                    Expect.equal roles.Length 3 "Should extract exactly 3 roles" ]

          testList
              "End-to-end extract -> project -> per-role ALPS"
              [ testCase "projectResource produces role profiles for all 3 roles"
                <| fun _ ->
                    let doc = parseDocument ()
                    let sc = buildStatechart doc

                    let resource: UnifiedResource =
                        { RouteTemplate = "/games/{gameId}"
                          ResourceSlug = "games"
                          TypeInfo = []
                          Statechart = Some sc
                          HttpCapabilities =
                            [ { Method = "GET"
                                StateKey = Some "XTurn"
                                LinkRelation = "self"
                                IsSafe = true }
                              { Method = "POST"
                                StateKey = Some "XTurn"
                                LinkRelation = "makeMove"
                                IsSafe = false }
                              { Method = "GET"
                                StateKey = Some "OTurn"
                                LinkRelation = "self"
                                IsSafe = true }
                              { Method = "POST"
                                StateKey = Some "OTurn"
                                LinkRelation = "makeMove"
                                IsSafe = false }
                              { Method = "GET"
                                StateKey = Some "XWins"
                                LinkRelation = "self"
                                IsSafe = true }
                              { Method = "GET"
                                StateKey = Some "OWins"
                                LinkRelation = "self"
                                IsSafe = true }
                              { Method = "GET"
                                StateKey = Some "Draw"
                                LinkRelation = "self"
                                IsSafe = true } ]
                          DerivedFields = ResourceModel.emptyDerivedFields }

                    let result = projectResource resource "http://example.org"

                    Expect.equal result.RoleProfiles.Count 3 "Should produce 3 role profiles"

                    Expect.isTrue (result.RoleProfiles.ContainsKey "games-playerx") "Should have PlayerX profile"

                    Expect.isTrue (result.RoleProfiles.ContainsKey "games-playero") "Should have PlayerO profile"

                    Expect.isTrue (result.RoleProfiles.ContainsKey "games-spectator") "Should have Spectator profile"

                testCase "PlayerX profile is valid ALPS JSON with descriptors"
                <| fun _ ->
                    let doc = parseDocument ()
                    let sc = buildStatechart doc

                    let resource: UnifiedResource =
                        { RouteTemplate = "/games/{gameId}"
                          ResourceSlug = "games"
                          TypeInfo = []
                          Statechart = Some sc
                          HttpCapabilities =
                            [ { Method = "GET"
                                StateKey = Some "XTurn"
                                LinkRelation = "self"
                                IsSafe = true }
                              { Method = "POST"
                                StateKey = Some "XTurn"
                                LinkRelation = "makeMove"
                                IsSafe = false }
                              { Method = "GET"
                                StateKey = Some "OTurn"
                                LinkRelation = "self"
                                IsSafe = true }
                              { Method = "POST"
                                StateKey = Some "OTurn"
                                LinkRelation = "makeMove"
                                IsSafe = false } ]
                          DerivedFields = ResourceModel.emptyDerivedFields }

                    let result = projectResource resource "http://example.org"
                    let json = result.RoleProfiles.["games-playerx"]
                    use jsonDoc = JsonDocument.Parse(json)
                    let alps = jsonDoc.RootElement.GetProperty("alps")

                    match alps.TryGetProperty("descriptor") with
                    | true, descriptors ->
                        Expect.isGreaterThan (descriptors.GetArrayLength()) 0 "Should have descriptors"
                    | _ -> failtest "Should have descriptors"

                testCase "capability filtering removes Spectator POST with restricted-only transitions"
                <| fun _ ->
                    // Use a fixture with only role-restricted transitions (no unrestricted getGame)
                    let sc: ExtractedStatechart =
                        { RouteTemplate = "/games/{gameId}"
                          StateNames = [ "XTurn"; "OTurn"; "Won"; "Draw" ]
                          InitialStateKey = "XTurn"
                          GuardNames = []
                          StateMetadata = Map.empty
                          Roles =
                            [ { Name = "PlayerX"; Description = None }
                              { Name = "PlayerO"; Description = None }
                              { Name = "Spectator"
                                Description = None } ]
                          Transitions =
                            [ { Event = "MakeMove"
                                Source = "XTurn"
                                Target = "OTurn"
                                Guard = None
                                Constraint = RestrictedTo [ "PlayerX" ] }
                              { Event = "MakeMove"
                                Source = "OTurn"
                                Target = "XTurn"
                                Guard = None
                                Constraint = RestrictedTo [ "PlayerO" ] } ] }

                    let caps =
                        [ { Method = "GET"
                            StateKey = Some "XTurn"
                            LinkRelation = "self"
                            IsSafe = true }
                          { Method = "POST"
                            StateKey = Some "XTurn"
                            LinkRelation = "makeMove"
                            IsSafe = false }
                          { Method = "GET"
                            StateKey = Some "OTurn"
                            LinkRelation = "self"
                            IsSafe = true }
                          { Method = "POST"
                            StateKey = Some "OTurn"
                            LinkRelation = "makeMove"
                            IsSafe = false } ]

                    let resource: UnifiedResource =
                        { RouteTemplate = "/games/{gameId}"
                          ResourceSlug = "games"
                          TypeInfo = []
                          Statechart = Some sc
                          HttpCapabilities = caps
                          DerivedFields = ResourceModel.emptyDerivedFields }

                    let result = projectResource resource "http://example.org"

                    // Spectator has no transitions, so no unsafe capabilities survive
                    let spectatorJson = result.RoleProfiles.["games-spectator"]
                    use spectatorDoc = JsonDocument.Parse(spectatorJson)
                    let alps = spectatorDoc.RootElement.GetProperty("alps")

                    match alps.TryGetProperty("descriptor") with
                    | true, descriptors ->
                        let rec findUnsafe (elem: JsonElement) =
                            [ match elem.TryGetProperty("type") with
                              | true, t when t.GetString() = "unsafe" ->
                                  match elem.TryGetProperty("id") with
                                  | true, id -> yield id.GetString()
                                  | _ -> ()
                              | _ -> ()
                              match elem.TryGetProperty("descriptor") with
                              | true, children when children.ValueKind = JsonValueKind.Array ->
                                  for child in children.EnumerateArray() do
                                      yield! findUnsafe child
                              | _ -> () ]

                        let unsafeIds =
                            [ for d in descriptors.EnumerateArray() do
                                  yield! findUnsafe d ]

                        Expect.isEmpty unsafeIds "Spectator should have no unsafe descriptors"
                    | _ -> ()

                testCase "no orphaned transitions in TicTacToe projection"
                <| fun _ ->
                    let doc = parseDocument ()
                    let sc = buildStatechart doc

                    let resource: UnifiedResource =
                        { RouteTemplate = "/games/{gameId}"
                          ResourceSlug = "games"
                          TypeInfo = []
                          Statechart = Some sc
                          HttpCapabilities = []
                          DerivedFields = ResourceModel.emptyDerivedFields }

                    let result = projectResource resource "http://example.org"
                    Expect.isEmpty result.Orphans "Should have no orphaned transitions"
                    Expect.isEmpty result.Errors "Should have no errors"

                testCase "global profile is regenerated with cross-links"
                <| fun _ ->
                    let doc = parseDocument ()
                    let sc = buildStatechart doc

                    let resource: UnifiedResource =
                        { RouteTemplate = "/games/{gameId}"
                          ResourceSlug = "games"
                          TypeInfo = []
                          Statechart = Some sc
                          HttpCapabilities =
                            [ { Method = "GET"
                                StateKey = Some "XTurn"
                                LinkRelation = "self"
                                IsSafe = true } ]
                          DerivedFields = ResourceModel.emptyDerivedFields }

                    let result = projectResource resource "http://example.org"
                    Expect.isSome result.UpdatedGlobalProfile "Should regenerate global profile"

                testCase "resource without roles produces empty projection result"
                <| fun _ ->
                    let noRolesSc =
                        { RouteTemplate = "/items"
                          StateNames = [ "Active" ]
                          InitialStateKey = "Active"
                          GuardNames = []
                          StateMetadata = Map.empty
                          Roles = []
                          Transitions = [] }

                    let resource: UnifiedResource =
                        { RouteTemplate = "/items"
                          ResourceSlug = "items"
                          TypeInfo = []
                          Statechart = Some noRolesSc
                          HttpCapabilities = []
                          DerivedFields = ResourceModel.emptyDerivedFields }

                    let result = projectResource resource "http://example.org"
                    Expect.isEmpty result.RoleProfiles "Should have no role profiles"
                    Expect.isNone result.UpdatedGlobalProfile "Should not regenerate global"

                testCase "projectAllResources merges multiple resources"
                <| fun _ ->
                    let doc = parseDocument ()
                    let sc = buildStatechart doc

                    let resource1: UnifiedResource =
                        { RouteTemplate = "/games/{gameId}"
                          ResourceSlug = "games"
                          TypeInfo = []
                          Statechart = Some sc
                          HttpCapabilities =
                            [ { Method = "GET"
                                StateKey = Some "XTurn"
                                LinkRelation = "self"
                                IsSafe = true } ]
                          DerivedFields = ResourceModel.emptyDerivedFields }

                    let plainResource: UnifiedResource =
                        { RouteTemplate = "/health"
                          ResourceSlug = "health"
                          TypeInfo = []
                          Statechart = None
                          HttpCapabilities =
                            [ { Method = "GET"
                                StateKey = None
                                LinkRelation = "self"
                                IsSafe = true } ]
                          DerivedFields = ResourceModel.emptyDerivedFields }

                    let batchResult, results =
                        projectAllResources [ resource1; plainResource ] "http://example.org"

                    Expect.equal batchResult.RoleAlpsProfiles.Count 3 "Should have 3 role profiles from games"
                    Expect.isTrue (batchResult.ProjectedSlugs.Contains "games") "games should be projected"
                    Expect.isFalse (batchResult.ProjectedSlugs.Contains "health") "health should not be projected"
                    Expect.equal results.Length 2 "Should have results for both resources" ] ]
