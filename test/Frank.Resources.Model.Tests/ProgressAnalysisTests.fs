module Frank.Resources.Model.Tests.ProgressAnalysisTests

open Expecto
open Frank.Resources.Model

// -- Helpers --

let private mkTransition event source target guard roleConstraint =
    { Event = event
      Source = source
      Target = target
      Guard = guard
      Constraint = roleConstraint }

let private mkState isFinal =
    { AllowedMethods = [ "GET" ]
      IsFinal = isFinal
      Description = None }

// -- Fixtures --

/// TicTacToe: 2 players + spectator. No deadlocks, no starvation.
/// Spectator is read-only. Turn-taking is weak starvation (no warning).
let private ticTacToeChart: ExtractedStatechart =
    { RouteTemplate = "/games/{gameId}"
      StateNames = [ "XTurn"; "OTurn"; "XWins"; "OWins"; "Draw" ]
      InitialStateKey = "XTurn"
      GuardNames = [ "TurnGuard" ]
      StateMetadata =
        Map.ofList
            [ "XTurn", mkState false
              "OTurn", mkState false
              "XWins", mkState true
              "OWins", mkState true
              "Draw", mkState true ]
      Roles =
        [ { Name = "PlayerX"; Description = Some "Player X" }
          { Name = "PlayerO"; Description = Some "Player O" }
          { Name = "Spectator"; Description = Some "Observer" } ]
      Transitions =
        [ mkTransition "getGame" "XTurn" "XTurn" None Unrestricted
          mkTransition "getGame" "OTurn" "OTurn" None Unrestricted
          mkTransition "getGame" "XWins" "XWins" None Unrestricted
          mkTransition "getGame" "OWins" "OWins" None Unrestricted
          mkTransition "getGame" "Draw" "Draw" None Unrestricted
          mkTransition "makeMove" "XTurn" "OTurn" (Some "TurnGuard") (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "XTurn" "XWins" (Some "TurnGuard") (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "XTurn" "Draw" (Some "TurnGuard") (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "OTurn" "XTurn" (Some "TurnGuard") (RestrictedTo [ "PlayerO" ])
          mkTransition "makeMove" "OTurn" "OWins" (Some "TurnGuard") (RestrictedTo [ "PlayerO" ])
          mkTransition "makeMove" "OTurn" "Draw" (Some "TurnGuard") (RestrictedTo [ "PlayerO" ]) ] }

/// Non-final state with only self-loops for all roles.
let private deadlockSelfLoopChart: ExtractedStatechart =
    { RouteTemplate = "/stuck"
      StateNames = [ "Stuck"; "Done" ]
      InitialStateKey = "Stuck"
      GuardNames = []
      StateMetadata =
        Map.ofList [ "Stuck", mkState false; "Done", mkState true ]
      Roles = [ { Name = "Admin"; Description = None } ]
      Transitions =
        [ mkTransition "refresh" "Stuck" "Stuck" None Unrestricted ] }

/// Only advancing transition is RestrictedTo [] (dead).
let private deadTransitionDeadlockChart: ExtractedStatechart =
    { RouteTemplate = "/dead"
      StateNames = [ "Active"; "Archived" ]
      InitialStateKey = "Active"
      GuardNames = []
      StateMetadata =
        Map.ofList [ "Active", mkState false; "Archived", mkState true ]
      Roles = [ { Name = "Admin"; Description = None } ]
      Transitions =
        [ mkTransition "view" "Active" "Active" None Unrestricted
          mkTransition "archive" "Active" "Archived" None (RestrictedTo []) ] }

/// Role permanently excluded after a state on all forward paths.
let private starvationChart: ExtractedStatechart =
    { RouteTemplate = "/workflow"
      StateNames = [ "Phase1"; "Phase2"; "Phase3"; "Done" ]
      InitialStateKey = "Phase1"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Phase1", mkState false
              "Phase2", mkState false
              "Phase3", mkState false
              "Done", mkState true ]
      Roles =
        [ { Name = "Admin"; Description = None }
          { Name = "Worker"; Description = None } ]
      Transitions =
        [ mkTransition "start" "Phase1" "Phase2" None (RestrictedTo [ "Admin" ])
          mkTransition "advance" "Phase2" "Phase3" None (RestrictedTo [ "Admin" ])
          mkTransition "complete" "Phase3" "Done" None (RestrictedTo [ "Admin" ])
          mkTransition "view" "Phase1" "Phase1" None Unrestricted
          mkTransition "view" "Phase2" "Phase2" None Unrestricted
          mkTransition "view" "Phase3" "Phase3" None Unrestricted ] }

/// Recovery path only via dead transition — must still report starved.
let private deadTransitionForwardPathChart: ExtractedStatechart =
    { RouteTemplate = "/dead-path"
      StateNames = [ "Start"; "Middle"; "Recovery"; "End" ]
      InitialStateKey = "Start"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Start", mkState false
              "Middle", mkState false
              "Recovery", mkState false
              "End", mkState true ]
      Roles =
        [ { Name = "RoleA"; Description = None }
          { Name = "RoleB"; Description = None } ]
      Transitions =
        [ mkTransition "go" "Start" "Middle" None (RestrictedTo [ "RoleA" ])
          // Only path from Middle to Recovery is dead — no one can fire it
          mkTransition "recover" "Middle" "Recovery" None (RestrictedTo [])
          mkTransition "act" "Recovery" "End" None (RestrictedTo [ "RoleB" ])
          mkTransition "finish" "Middle" "End" None (RestrictedTo [ "RoleA" ]) ] }

/// All transitions unrestricted — no starvation possible.
let private allUnrestrictedChart: ExtractedStatechart =
    { RouteTemplate = "/open"
      StateNames = [ "A"; "B"; "Done" ]
      InitialStateKey = "A"
      GuardNames = []
      StateMetadata =
        Map.ofList [ "A", mkState false; "B", mkState false; "Done", mkState true ]
      Roles =
        [ { Name = "User"; Description = None }
          { Name = "Admin"; Description = None } ]
      Transitions =
        [ mkTransition "step1" "A" "B" None Unrestricted
          mkTransition "step2" "B" "Done" None Unrestricted ] }

/// Single final state — empty report.
let private singleFinalChart: ExtractedStatechart =
    { RouteTemplate = "/final"
      StateNames = [ "Done" ]
      InitialStateKey = "Done"
      GuardNames = []
      StateMetadata = Map.ofList [ "Done", mkState true ]
      Roles = [ { Name = "User"; Description = None } ]
      Transitions = [] }

/// Initial state only, no transitions — deadlock.
let private emptyTransitionsChart: ExtractedStatechart =
    { RouteTemplate = "/empty"
      StateNames = [ "Idle" ]
      InitialStateKey = "Idle"
      GuardNames = []
      StateMetadata = Map.ofList [ "Idle", mkState false ]
      Roles = [ { Name = "User"; Description = None } ]
      Transitions = [] }

/// Non-terminating cycle, all roles active — no deadlock, no starvation.
let private cycleNoFinalChart: ExtractedStatechart =
    { RouteTemplate = "/cycle"
      StateNames = [ "A"; "B" ]
      InitialStateKey = "A"
      GuardNames = []
      StateMetadata = Map.ofList [ "A", mkState false; "B", mkState false ]
      Roles = [ { Name = "User"; Description = None } ]
      Transitions =
        [ mkTransition "forward" "A" "B" None Unrestricted
          mkTransition "back" "B" "A" None Unrestricted ] }

/// Diamond: two paths reconverge at active state.
/// RoleB inactive at B and C, but both paths reach D where RoleB can act.
let private diamondChart: ExtractedStatechart =
    { RouteTemplate = "/diamond"
      StateNames = [ "A"; "B"; "C"; "D"; "Done" ]
      InitialStateKey = "A"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "A", mkState false
              "B", mkState false
              "C", mkState false
              "D", mkState false
              "Done", mkState true ]
      Roles =
        [ { Name = "RoleA"; Description = None }
          { Name = "RoleB"; Description = None } ]
      Transitions =
        [ mkTransition "left" "A" "B" None (RestrictedTo [ "RoleA" ])
          mkTransition "right" "A" "C" None (RestrictedTo [ "RoleA" ])
          mkTransition "converge1" "B" "D" None (RestrictedTo [ "RoleA" ])
          mkTransition "converge2" "C" "D" None (RestrictedTo [ "RoleA" ])
          mkTransition "finish" "D" "Done" None (RestrictedTo [ "RoleB" ])
          mkTransition "act" "A" "A" None (RestrictedTo [ "RoleB" ]) ] }

/// Non-initial non-final state with no outgoing transitions — deadlock.
let private reachableDeadEndChart: ExtractedStatechart =
    { RouteTemplate = "/deadend"
      StateNames = [ "Start"; "DeadEnd"; "Done" ]
      InitialStateKey = "Start"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Start", mkState false
              "DeadEnd", mkState false
              "Done", mkState true ]
      Roles = [ { Name = "User"; Description = None } ]
      Transitions =
        [ mkTransition "enter" "Start" "DeadEnd" None Unrestricted
          mkTransition "skip" "Start" "Done" None Unrestricted ] }

/// Same role excluded from two independent states — two diagnostics.
let private multipleStarvationChart: ExtractedStatechart =
    { RouteTemplate = "/multi-starve"
      StateNames = [ "Start"; "Branch1"; "Branch2"; "End1"; "End2" ]
      InitialStateKey = "Start"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Start", mkState false
              "Branch1", mkState false
              "Branch2", mkState false
              "End1", mkState true
              "End2", mkState true ]
      Roles =
        [ { Name = "RoleA"; Description = None }
          { Name = "RoleB"; Description = None } ]
      Transitions =
        [ mkTransition "go1" "Start" "Branch1" None (RestrictedTo [ "RoleA" ])
          mkTransition "go2" "Start" "Branch2" None (RestrictedTo [ "RoleA" ])
          mkTransition "end1" "Branch1" "End1" None (RestrictedTo [ "RoleA" ])
          mkTransition "end2" "Branch2" "End2" None (RestrictedTo [ "RoleA" ]) ] }

/// Disconnected chart — unreachable non-final state should not produce false deadlock.
let private disconnectedChart: ExtractedStatechart =
    { RouteTemplate = "/disconnected"
      StateNames = [ "Start"; "Done"; "Orphan" ]
      InitialStateKey = "Start"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Start", mkState false
              "Done", mkState true
              "Orphan", mkState false ]
      Roles = [ { Name = "User"; Description = None } ]
      Transitions =
        [ mkTransition "finish" "Start" "Done" None Unrestricted
          mkTransition "orphanAct" "Orphan" "Start" None Unrestricted ] }

// -- Tests --

[<Tests>]
let predicateTests =
    testList
        "predicates"
        [ testCase "isAdvancing returns true for state-changing transition"
          <| fun _ ->
              let t = mkTransition "go" "A" "B" None Unrestricted
              Expect.isTrue (ProgressAnalysis.isAdvancing t) "A->B is advancing"

          testCase "isAdvancing returns false for self-loop"
          <| fun _ ->
              let t = mkTransition "view" "A" "A" None Unrestricted
              Expect.isFalse (ProgressAnalysis.isAdvancing t) "A->A is not advancing"

          testCase "isLive returns true for unrestricted"
          <| fun _ ->
              let t = mkTransition "go" "A" "B" None Unrestricted
              Expect.isTrue (ProgressAnalysis.isLive t) "Unrestricted is live"

          testCase "isLive returns true for non-empty RestrictedTo"
          <| fun _ ->
              let t = mkTransition "go" "A" "B" None (RestrictedTo [ "R" ])
              Expect.isTrue (ProgressAnalysis.isLive t) "RestrictedTo [R] is live"

          testCase "isLive returns false for empty RestrictedTo"
          <| fun _ ->
              let t = mkTransition "go" "A" "B" None (RestrictedTo [])
              Expect.isFalse (ProgressAnalysis.isLive t) "RestrictedTo [] is dead" ]
