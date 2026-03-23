module Frank.Resources.Model.Tests.ProjectionTests

open Expecto
open Frank.Resources.Model

// -- Test fixtures --

let private mkTransition event source target guard roleConstraint =
    { Event = event
      Source = source
      Target = target
      Guard = guard
      Constraint = roleConstraint }

/// TicTacToe-like statechart with two players and a spectator.
let private ticTacToeChart: ExtractedStatechart =
    { RouteTemplate = "/games/{gameId}"
      StateNames = [ "XTurn"; "OTurn"; "XWins"; "OWins"; "Draw" ]
      InitialStateKey = "XTurn"
      GuardNames = [ "TurnGuard" ]
      StateMetadata =
        Map.ofList
            [ "XTurn", { AllowedMethods = [ "GET"; "PUT" ]; IsFinal = false; Description = None }
              "OTurn", { AllowedMethods = [ "GET"; "PUT" ]; IsFinal = false; Description = None }
              "XWins", { AllowedMethods = [ "GET" ]; IsFinal = true; Description = None }
              "OWins", { AllowedMethods = [ "GET" ]; IsFinal = true; Description = None }
              "Draw", { AllowedMethods = [ "GET" ]; IsFinal = true; Description = None } ]
      Roles =
        [ { Name = "PlayerX"; Description = Some "Player X" }
          { Name = "PlayerO"; Description = Some "Player O" }
          { Name = "Spectator"; Description = Some "Observer" } ]
      Transitions =
        [ // Unguarded GET — all roles can observe
          mkTransition "getGame" "XTurn" "XTurn" None Unrestricted
          mkTransition "getGame" "OTurn" "OTurn" None Unrestricted
          mkTransition "getGame" "XWins" "XWins" None Unrestricted
          mkTransition "getGame" "OWins" "OWins" None Unrestricted
          mkTransition "getGame" "Draw" "Draw" None Unrestricted
          // Guarded makeMove — role-restricted per state
          mkTransition "makeMove" "XTurn" "OTurn" (Some "TurnGuard") (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "XTurn" "XWins" (Some "TurnGuard") (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "XTurn" "Draw" (Some "TurnGuard") (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "OTurn" "XTurn" (Some "TurnGuard") (RestrictedTo [ "PlayerO" ])
          mkTransition "makeMove" "OTurn" "OWins" (Some "TurnGuard") (RestrictedTo [ "PlayerO" ])
          mkTransition "makeMove" "OTurn" "Draw" (Some "TurnGuard") (RestrictedTo [ "PlayerO" ]) ] }

/// Simple chart with a dead transition (RestrictedTo []).
let private deadTransitionChart: ExtractedStatechart =
    { RouteTemplate = "/items"
      StateNames = [ "Active"; "Archived" ]
      InitialStateKey = "Active"
      GuardNames = [ "DeadGuard" ]
      StateMetadata =
        Map.ofList
            [ "Active", { AllowedMethods = [ "GET" ]; IsFinal = false; Description = None }
              "Archived", { AllowedMethods = [ "GET" ]; IsFinal = true; Description = None } ]
      Roles = [ { Name = "Admin"; Description = None } ]
      Transitions =
        [ mkTransition "getItem" "Active" "Active" None Unrestricted
          mkTransition "archive" "Active" "Archived" (Some "DeadGuard") (RestrictedTo []) ] }

/// Chart with disconnected state (no path from initial).
let private disconnectedChart: ExtractedStatechart =
    { RouteTemplate = "/docs"
      StateNames = [ "Draft"; "Published"; "Orphan" ]
      InitialStateKey = "Draft"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Draft", { AllowedMethods = [ "GET"; "PUT" ]; IsFinal = false; Description = None }
              "Published", { AllowedMethods = [ "GET" ]; IsFinal = true; Description = None }
              "Orphan", { AllowedMethods = [ "GET" ]; IsFinal = false; Description = None } ]
      Roles = [ { Name = "Editor"; Description = None } ]
      Transitions =
        [ mkTransition "publish" "Draft" "Published" None Unrestricted
          mkTransition "view" "Draft" "Draft" None Unrestricted
          mkTransition "view" "Published" "Published" None Unrestricted
          // Orphan state has a transition but is unreachable from initial
          mkTransition "orphanAction" "Orphan" "Draft" None Unrestricted ] }

// -- Tests --

[<Tests>]
let filterTransitionsByRoleTests =
    testList
        "filterTransitionsByRole"
        [ testCase "keeps unrestricted transitions for all roles"
          <| fun _ ->
              let projected = Projection.filterTransitionsByRole "PlayerX" ticTacToeChart
              let getTransitions = projected.Transitions |> List.filter (fun t -> t.Event = "getGame")
              Expect.equal (List.length getTransitions) 5 "All 5 getGame transitions survive"

          testCase "keeps restricted transitions only for permitted roles"
          <| fun _ ->
              let projected = Projection.filterTransitionsByRole "PlayerX" ticTacToeChart

              let movesFromXTurn =
                  projected.Transitions
                  |> List.filter (fun t -> t.Event = "makeMove" && t.Source = "XTurn")

              let movesFromOTurn =
                  projected.Transitions
                  |> List.filter (fun t -> t.Event = "makeMove" && t.Source = "OTurn")

              Expect.equal (List.length movesFromXTurn) 3 "PlayerX has 3 makeMove transitions from XTurn"
              Expect.equal (List.length movesFromOTurn) 0 "PlayerX has no makeMove transitions from OTurn"

          testCase "unknown role keeps only unrestricted transitions"
          <| fun _ ->
              let projected = Projection.filterTransitionsByRole "Unknown" ticTacToeChart
              let allMoves = projected.Transitions |> List.filter (fun t -> t.Event = "makeMove")
              Expect.equal (List.length allMoves) 0 "Unknown role has no makeMove transitions"

              let allGets = projected.Transitions |> List.filter (fun t -> t.Event = "getGame")
              Expect.equal (List.length allGets) 5 "Unknown role still has all getGame transitions"

          testCase "spectator sees only unrestricted transitions"
          <| fun _ ->
              let projected = Projection.filterTransitionsByRole "Spectator" ticTacToeChart
              let allMoves = projected.Transitions |> List.filter (fun t -> t.Event = "makeMove")
              Expect.equal (List.length allMoves) 0 "Spectator has no makeMove transitions"

          testCase "RestrictedTo empty list filters out the transition"
          <| fun _ ->
              let projected = Projection.filterTransitionsByRole "Admin" deadTransitionChart
              let archives = projected.Transitions |> List.filter (fun t -> t.Event = "archive")
              Expect.equal (List.length archives) 0 "Dead transition filtered out" ]

[<Tests>]
let pruneUnreachableStatesTests =
    testList
        "pruneUnreachableStates"
        [ testCase "removes disconnected states"
          <| fun _ ->
              let pruned = Projection.pruneUnreachableStates disconnectedChart
              Expect.isFalse (List.contains "Orphan" pruned.StateNames) "Orphan state removed"
              Expect.isTrue (List.contains "Draft" pruned.StateNames) "Draft survives"
              Expect.isTrue (List.contains "Published" pruned.StateNames) "Published survives"

          testCase "always retains initial state"
          <| fun _ ->
              // Filter all transitions from a chart, then prune
              let emptyTransitions = { ticTacToeChart with Transitions = [] }
              let pruned = Projection.pruneUnreachableStates emptyTransitions
              Expect.isTrue (List.contains "XTurn" pruned.StateNames) "Initial state retained"

          testCase "updates StateMetadata for pruned states"
          <| fun _ ->
              let pruned = Projection.pruneUnreachableStates disconnectedChart
              Expect.isFalse (Map.containsKey "Orphan" pruned.StateMetadata) "Orphan metadata removed"
              Expect.isTrue (Map.containsKey "Draft" pruned.StateMetadata) "Draft metadata retained"

          testCase "removes transitions referencing pruned states"
          <| fun _ ->
              let pruned = Projection.pruneUnreachableStates disconnectedChart

              let orphanTransitions =
                  pruned.Transitions
                  |> List.filter (fun t -> t.Source = "Orphan" || t.Target = "Orphan")

              Expect.equal (List.length orphanTransitions) 0 "No transitions reference Orphan" ]

[<Tests>]
let projectForRoleTests =
    testList
        "projectForRole"
        [ testCase "PlayerX projection includes makeMove in XTurn only"
          <| fun _ ->
              let projected = Projection.projectForRole "PlayerX" ticTacToeChart

              let xTurnMoves =
                  projected.Transitions
                  |> List.filter (fun t -> t.Event = "makeMove" && t.Source = "XTurn")

              let oTurnMoves =
                  projected.Transitions
                  |> List.filter (fun t -> t.Event = "makeMove" && t.Source = "OTurn")

              Expect.isNonEmpty xTurnMoves "PlayerX has makeMove from XTurn"
              Expect.isEmpty oTurnMoves "PlayerX has no makeMove from OTurn"

          testCase "PlayerO projection includes makeMove in OTurn only"
          <| fun _ ->
              let projected = Projection.projectForRole "PlayerO" ticTacToeChart

              let oTurnMoves =
                  projected.Transitions
                  |> List.filter (fun t -> t.Event = "makeMove" && t.Source = "OTurn")

              let xTurnMoves =
                  projected.Transitions
                  |> List.filter (fun t -> t.Event = "makeMove" && t.Source = "XTurn")

              Expect.isNonEmpty oTurnMoves "PlayerO has makeMove from OTurn"
              Expect.isEmpty xTurnMoves "PlayerO has no makeMove from XTurn"

          testCase "both players can observe all states"
          <| fun _ ->
              let playerX = Projection.projectForRole "PlayerX" ticTacToeChart
              let playerO = Projection.projectForRole "PlayerO" ticTacToeChart

              // All states reachable via unguarded getGame + guarded makeMove transitions
              Expect.equal (List.length playerX.StateNames) 5 "PlayerX sees all 5 states"
              Expect.equal (List.length playerO.StateNames) 5 "PlayerO sees all 5 states" ]

[<Tests>]
let projectAllTests =
    testList
        "projectAll"
        [ testCase "produces one entry per role"
          <| fun _ ->
              let projections = Projection.projectAll ticTacToeChart
              Expect.equal (Map.count projections) 3 "Three roles = three projections"
              Expect.isTrue (Map.containsKey "PlayerX" projections) "PlayerX projection exists"
              Expect.isTrue (Map.containsKey "PlayerO" projections) "PlayerO projection exists"
              Expect.isTrue (Map.containsKey "Spectator" projections) "Spectator projection exists"

          testCase "returns empty map for statechart with no roles"
          <| fun _ ->
              let noRoles = { ticTacToeChart with Roles = [] }
              let projections = Projection.projectAll noRoles
              Expect.equal (Map.count projections) 0 "No roles = empty map" ]

[<Tests>]
let findOrphanedTransitionsTests =
    testList
        "findOrphanedTransitions"
        [ testCase "returns empty for fully covered transitions"
          <| fun _ ->
              let projections = Projection.projectAll ticTacToeChart
              let orphans = Projection.findOrphanedTransitions ticTacToeChart projections
              Expect.isEmpty orphans "TicTacToe has no orphaned transitions"

          testCase "detects dead transitions (RestrictedTo [])"
          <| fun _ ->
              let projections = Projection.projectAll deadTransitionChart
              let orphans = Projection.findOrphanedTransitions deadTransitionChart projections

              let archiveOrphans = orphans |> List.filter (fun t -> t.Event = "archive")
              Expect.isNonEmpty archiveOrphans "Dead transition detected as orphan" ]
