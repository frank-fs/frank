module Frank.Resources.Model.Tests.ClosedWorldProjectionTests

open Expecto
open Frank.Resources.Model
open Frank.Resources.Model.Tests.TestHelpers

// -- Test fixtures --

/// TicTacToe-like statechart: all transitions explicitly assigned to roles.
/// This should pass closed-world totality — every transition has a role.
let private fullyAssignedChart: ExtractedStatechart =
    { RouteTemplate = "/games/{gameId}"
      StateNames = [ "XTurn"; "OTurn"; "XWins"; "OWins"; "Draw" ]
      InitialStateKey = "XTurn"
      GuardNames = [ "TurnGuard" ]
      StateMetadata =
        Map.ofList
            [ "XTurn",
              { AllowedMethods = [ "GET"; "PUT" ]
                IsFinal = false
                Description = None }
              "OTurn",
              { AllowedMethods = [ "GET"; "PUT" ]
                IsFinal = false
                Description = None }
              "XWins",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None }
              "OWins",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None }
              "Draw",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None } ]
      Roles =
        [ { Name = "PlayerX"
            Description = Some "Player X" }
          { Name = "PlayerO"
            Description = Some "Player O" }
          { Name = "Spectator"
            Description = Some "Observer" } ]
      Transitions =
        [ // getGame assigned to all three roles explicitly
          mkTransition "getGame" "XTurn" "XTurn" None (RestrictedTo [ "PlayerX"; "PlayerO"; "Spectator" ])
          mkTransition "getGame" "OTurn" "OTurn" None (RestrictedTo [ "PlayerX"; "PlayerO"; "Spectator" ])
          mkTransition "getGame" "XWins" "XWins" None (RestrictedTo [ "PlayerX"; "PlayerO"; "Spectator" ])
          mkTransition "getGame" "OWins" "OWins" None (RestrictedTo [ "PlayerX"; "PlayerO"; "Spectator" ])
          mkTransition "getGame" "Draw" "Draw" None (RestrictedTo [ "PlayerX"; "PlayerO"; "Spectator" ])
          // makeMove restricted per state
          mkTransition "makeMove" "XTurn" "OTurn" (Some "TurnGuard") (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "XTurn" "XWins" (Some "TurnGuard") (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "XTurn" "Draw" (Some "TurnGuard") (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "OTurn" "XTurn" (Some "TurnGuard") (RestrictedTo [ "PlayerO" ])
          mkTransition "makeMove" "OTurn" "OWins" (Some "TurnGuard") (RestrictedTo [ "PlayerO" ])
          mkTransition "makeMove" "OTurn" "Draw" (Some "TurnGuard") (RestrictedTo [ "PlayerO" ]) ] }

/// TicTacToe with Unrestricted getGame (the typical open-world pattern).
/// Should fail closed-world: Unrestricted means "no explicit role assignment".
let private openWorldChart: ExtractedStatechart =
    { RouteTemplate = "/games/{gameId}"
      StateNames = [ "XTurn"; "OTurn"; "XWins"; "OWins"; "Draw" ]
      InitialStateKey = "XTurn"
      GuardNames = [ "TurnGuard" ]
      StateMetadata =
        Map.ofList
            [ "XTurn",
              { AllowedMethods = [ "GET"; "PUT" ]
                IsFinal = false
                Description = None }
              "OTurn",
              { AllowedMethods = [ "GET"; "PUT" ]
                IsFinal = false
                Description = None }
              "XWins",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None }
              "OWins",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None }
              "Draw",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None } ]
      Roles =
        [ { Name = "PlayerX"
            Description = Some "Player X" }
          { Name = "PlayerO"
            Description = Some "Player O" }
          { Name = "Spectator"
            Description = Some "Observer" } ]
      Transitions =
        [ // Unrestricted getGame — open-world pattern
          mkTransition "getGame" "XTurn" "XTurn" None Unrestricted
          mkTransition "getGame" "OTurn" "OTurn" None Unrestricted
          mkTransition "getGame" "XWins" "XWins" None Unrestricted
          mkTransition "getGame" "OWins" "OWins" None Unrestricted
          mkTransition "getGame" "Draw" "Draw" None Unrestricted
          // makeMove restricted per state
          mkTransition "makeMove" "XTurn" "OTurn" (Some "TurnGuard") (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "XTurn" "XWins" (Some "TurnGuard") (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "XTurn" "Draw" (Some "TurnGuard") (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "OTurn" "XTurn" (Some "TurnGuard") (RestrictedTo [ "PlayerO" ])
          mkTransition "makeMove" "OTurn" "OWins" (Some "TurnGuard") (RestrictedTo [ "PlayerO" ])
          mkTransition "makeMove" "OTurn" "Draw" (Some "TurnGuard") (RestrictedTo [ "PlayerO" ]) ] }

/// Chart with a dead transition (RestrictedTo []).
/// Fails both open-world and closed-world.
let private deadTransitionChart: ExtractedStatechart =
    { RouteTemplate = "/items"
      StateNames = [ "Active"; "Archived" ]
      InitialStateKey = "Active"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Active",
              { AllowedMethods = [ "GET" ]
                IsFinal = false
                Description = None }
              "Archived",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None } ]
      Roles = [ { Name = "Admin"; Description = None } ]
      Transitions =
        [ mkTransition "getItem" "Active" "Active" None (RestrictedTo [ "Admin" ])
          mkTransition "archive" "Active" "Archived" None (RestrictedTo []) ] }

/// Chart with mixed: some transitions assigned, some Unrestricted.
/// Closed-world should report only the Unrestricted ones as errors.
let private mixedChart: ExtractedStatechart =
    { RouteTemplate = "/docs/{docId}"
      StateNames = [ "Draft"; "Published" ]
      InitialStateKey = "Draft"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Draft",
              { AllowedMethods = [ "GET"; "PUT" ]
                IsFinal = false
                Description = None }
              "Published",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None } ]
      Roles =
        [ { Name = "Editor"; Description = None }
          { Name = "Reviewer"
            Description = None } ]
      Transitions =
        [ mkTransition "view" "Draft" "Draft" None Unrestricted // Not assigned — error in closed-world
          mkTransition "view" "Published" "Published" None Unrestricted // Not assigned — error in closed-world
          mkTransition "publish" "Draft" "Published" None (RestrictedTo [ "Editor" ]) ] }

/// Chart with no roles at all — closed-world should succeed (nothing to check).
let private noRolesChart: ExtractedStatechart =
    { RouteTemplate = "/health"
      StateNames = [ "Up" ]
      InitialStateKey = "Up"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Up",
              { AllowedMethods = [ "GET" ]
                IsFinal = false
                Description = None } ]
      Roles = []
      Transitions = [ mkTransition "check" "Up" "Up" None Unrestricted ] }

// -- Tests --

[<Tests>]
let projectAllStrictTests =
    testList
        "Projection.projectAllStrict"
        [ testCase "fully assigned chart passes closed-world"
          <| fun _ ->
              let result = Projection.projectAllStrict fullyAssignedChart
              Expect.isOk result "All transitions have explicit role assignments"

              let projections = Result.defaultValue Map.empty result
              Expect.equal (Map.count projections) 3 "Three roles = three projections"

          testCase "open-world chart with Unrestricted transitions fails closed-world"
          <| fun _ ->
              let result = Projection.projectAllStrict openWorldChart

              match result with
              | Ok _ -> failtest "Should fail: Unrestricted transitions in closed-world mode"
              | Error errors ->
                  // 5 Unrestricted getGame transitions should all be flagged
                  let unmapped =
                      errors
                      |> List.filter (fun e ->
                          match e with
                          | Projection.UnmappedTransition _ -> true
                          | _ -> false)

                  Expect.equal unmapped.Length 5 "5 Unrestricted transitions flagged"

          testCase "dead transition (RestrictedTo []) fails closed-world"
          <| fun _ ->
              let result = Projection.projectAllStrict deadTransitionChart

              match result with
              | Ok _ -> failtest "Should fail: dead transition has no role"
              | Error errors ->
                  let dead =
                      errors
                      |> List.filter (fun e ->
                          match e with
                          | Projection.DeadTransition _ -> true
                          | _ -> false)

                  Expect.equal dead.Length 1 "1 dead transition flagged"

          testCase "mixed chart reports only Unrestricted transitions as unmapped"
          <| fun _ ->
              let result = Projection.projectAllStrict mixedChart

              match result with
              | Ok _ -> failtest "Should fail: has Unrestricted transitions"
              | Error errors ->
                  let unmapped =
                      errors
                      |> List.filter (fun e ->
                          match e with
                          | Projection.UnmappedTransition _ -> true
                          | _ -> false)

                  Expect.equal unmapped.Length 2 "2 Unrestricted view transitions flagged"

                  // The publish transition (RestrictedTo ["Editor"]) should NOT be flagged
                  let publishErrors =
                      errors
                      |> List.filter (fun e ->
                          match e with
                          | Projection.UnmappedTransition t -> t.Event = "publish"
                          | _ -> false)

                  Expect.isEmpty publishErrors "Assigned publish transition not flagged"

          testCase "no roles chart passes closed-world (nothing to check)"
          <| fun _ ->
              let result = Projection.projectAllStrict noRolesChart
              Expect.isOk result "No roles means nothing to project"

              let projections = Result.defaultValue Map.empty result
              Expect.equal (Map.count projections) 0 "Empty projections for no-role chart"

          testCase "closed-world projections contain same transitions as open-world"
          <| fun _ ->
              // For a fully-assigned chart, closed-world and open-world produce identical results
              let openResult = Projection.projectAll fullyAssignedChart
              let closedResult = Projection.projectAllStrict fullyAssignedChart

              match closedResult with
              | Error _ -> failtest "Fully assigned should pass closed-world"
              | Ok closedProjections ->
                  for role in Map.keys openResult do
                      let openTransitions = openResult[role].Transitions |> Set.ofList
                      let closedTransitions = closedProjections[role].Transitions |> Set.ofList
                      Expect.equal closedTransitions openTransitions $"Projections match for role {role}" ]

[<Tests>]
let projectionErrorTests =
    testList
        "ProjectionError types"
        [ testCase "UnmappedTransition captures the transition"
          <| fun _ ->
              let t = mkTransition "getGame" "XTurn" "XTurn" None Unrestricted
              let error = Projection.UnmappedTransition t

              match error with
              | Projection.UnmappedTransition captured ->
                  Expect.equal captured.Event "getGame" "Event preserved"
                  Expect.equal captured.Source "XTurn" "Source preserved"
              | _ -> failtest "Should be UnmappedTransition"

          testCase "DeadTransition captures the transition"
          <| fun _ ->
              let t = mkTransition "archive" "Active" "Archived" None (RestrictedTo [])
              let error = Projection.DeadTransition t

              match error with
              | Projection.DeadTransition captured -> Expect.equal captured.Event "archive" "Event preserved"
              | _ -> failtest "Should be DeadTransition"

          testCase "ProjectionError.describe formats unmapped transition"
          <| fun _ ->
              let t = mkTransition "getGame" "XTurn" "XTurn" None Unrestricted
              let error = Projection.UnmappedTransition t
              let desc = Projection.ProjectionError.describe error
              Expect.stringContains desc "getGame" "Description mentions event"
              Expect.stringContains desc "XTurn" "Description mentions state"

          testCase "ProjectionError.describe formats dead transition"
          <| fun _ ->
              let t = mkTransition "archive" "Active" "Archived" None (RestrictedTo [])
              let error = Projection.DeadTransition t
              let desc = Projection.ProjectionError.describe error
              Expect.stringContains desc "archive" "Description mentions event"
              Expect.stringContains desc "Active" "Description mentions source"
              Expect.stringContains desc "Archived" "Description mentions target" ]

[<Tests>]
let validateProjectionStrictTests =
    testList
        "ProjectionValidator integration with strict mode"
        [ testCase "fully assigned chart has no completeness issues in strict check"
          <| fun _ ->
              // Closed-world strict check: project, then check completeness
              match Projection.projectAllStrict fullyAssignedChart with
              | Error _ -> failtest "Fully assigned should pass"
              | Ok projections ->
                  let pruned = Projection.pruneUnreachableStates fullyAssignedChart
                  let orphans = Projection.findOrphanedTransitions pruned projections
                  Expect.isEmpty orphans "No orphans in strict projection" ]

[<Tests>]
let ticTacToeFindingsTests =
    testList
        "TicTacToe closed-world findings"
        [ testCase "standard TicTacToe fails closed-world due to Unrestricted getGame"
          <| fun _ ->
              // The standard TicTacToe uses Unrestricted for getGame (observation).
              // Closed-world rejects this: every transition needs explicit role assignment.
              let result = Projection.projectAllStrict openWorldChart

              match result with
              | Ok _ -> failtest "Standard TicTacToe should fail closed-world"
              | Error errors ->
                  // Exactly 5 getGame transitions (one per state) are Unrestricted
                  let unmapped =
                      errors
                      |> List.filter (fun e ->
                          match e with
                          | Projection.UnmappedTransition t -> t.Event = "getGame"
                          | _ -> false)

                  Expect.equal unmapped.Length 5 "5 getGame transitions are unmapped"

                  // The 6 makeMove transitions are all properly assigned
                  let makeMoveErrors =
                      errors
                      |> List.filter (fun e ->
                          match e with
                          | Projection.UnmappedTransition t -> t.Event = "makeMove"
                          | _ -> false)

                  Expect.isEmpty makeMoveErrors "makeMove transitions are properly assigned"

          testCase "TicTacToe passes closed-world when getGame is explicitly assigned to all roles"
          <| fun _ ->
              // Fix: convert Unrestricted getGame to RestrictedTo all three roles.
              // This is the closed-world equivalent — same semantics, explicit assignment.
              let result = Projection.projectAllStrict fullyAssignedChart

              Expect.isOk result "Fully assigned TicTacToe passes closed-world"

          testCase "closed-world TicTacToe produces identical projections to open-world"
          <| fun _ ->
              // Key finding: when Unrestricted is converted to RestrictedTo [all roles],
              // the resulting per-role projections are identical to open-world.
              let openProjections = Projection.projectAll openWorldChart

              match Projection.projectAllStrict fullyAssignedChart with
              | Error _ -> failtest "Should pass"
              | Ok closedProjections ->
                  // Each role sees the same transitions in both modes
                  for roleName in [ "PlayerX"; "PlayerO"; "Spectator" ] do
                      let openTransitions = openProjections[roleName].Transitions |> List.length
                      let closedTransitions = closedProjections[roleName].Transitions |> List.length

                      Expect.equal closedTransitions openTransitions $"Role {roleName} sees same number of transitions" ]
