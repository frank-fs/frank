module Frank.Statecharts.Tests.Analysis.ProjectionValidatorTests

open Expecto
open Frank.Resources.Model
open Frank.Statecharts.Analysis.ProjectionValidator

// -- Test fixtures --

let private mkTransition event source target guard roleConstraint =
    { Event = event
      Source = source
      Target = target
      Guard = guard
      Constraint = roleConstraint }

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

let private mixedChoiceChart: ExtractedStatechart =
    { RouteTemplate = "/orders/{orderId}"
      StateNames = [ "Pending"; "Approved"; "Rejected" ]
      InitialStateKey = "Pending"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Pending", { AllowedMethods = [ "GET"; "PUT" ]; IsFinal = false; Description = None }
              "Approved", { AllowedMethods = [ "GET" ]; IsFinal = true; Description = None }
              "Rejected", { AllowedMethods = [ "GET" ]; IsFinal = true; Description = None } ]
      Roles =
        [ { Name = "Manager"; Description = None }
          { Name = "Clerk"; Description = None } ]
      Transitions =
        [ mkTransition "view" "Pending" "Pending" None Unrestricted
          mkTransition "approve" "Pending" "Approved" None (RestrictedTo [ "Manager" ])
          mkTransition "reject" "Pending" "Rejected" None (RestrictedTo [ "Clerk" ]) ] }

let private deadlockChart: ExtractedStatechart =
    { RouteTemplate = "/tasks/{taskId}"
      StateNames = [ "Open"; "Blocked"; "Done" ]
      InitialStateKey = "Open"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Open", { AllowedMethods = [ "GET"; "PUT" ]; IsFinal = false; Description = None }
              "Blocked", { AllowedMethods = [ "GET" ]; IsFinal = false; Description = None }
              "Done", { AllowedMethods = [ "GET" ]; IsFinal = true; Description = None } ]
      Roles =
        [ { Name = "Worker"; Description = None }
          { Name = "Reviewer"; Description = None } ]
      Transitions =
        [ mkTransition "view" "Open" "Open" None Unrestricted
          mkTransition "block" "Open" "Blocked" None (RestrictedTo [ "Worker" ])
          mkTransition "complete" "Open" "Done" None (RestrictedTo [ "Worker" ]) ] }

let private staleRoleChart: ExtractedStatechart =
    { RouteTemplate = "/docs/{docId}"
      StateNames = [ "Draft"; "Published" ]
      InitialStateKey = "Draft"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Draft", { AllowedMethods = [ "GET"; "PUT" ]; IsFinal = false; Description = None }
              "Published", { AllowedMethods = [ "GET" ]; IsFinal = true; Description = None } ]
      Roles = [ { Name = "Editor"; Description = None } ]
      Transitions =
        [ mkTransition "view" "Draft" "Draft" None Unrestricted
          mkTransition "publish" "Draft" "Published" None (RestrictedTo [ "Ghost" ]) ] }

// -- Tests --

[<Tests>]
let connectednessTests =
    testList
        "ProjectionValidator.checkConnectedness"
        [ testCase "TicTacToe has no connectedness issues"
          <| fun _ ->
              let issues = checkConnectedness ticTacToeChart
              Expect.isEmpty issues "All transitions have roles"

          testCase "RestrictedTo [] produces error"
          <| fun _ ->
              let issues = checkConnectedness deadTransitionChart
              Expect.equal issues.Length 1 "One dead transition"
              Expect.equal issues[0].Severity Severity.Error "Severity is Error"
              Expect.stringContains issues[0].Message "archive" "Mentions the transition"

          testCase "stale role reference produces error"
          <| fun _ ->
              let issues = checkConnectedness staleRoleChart
              Expect.equal issues.Length 1 "One stale reference"
              Expect.stringContains issues[0].Message "Ghost" "Mentions the unknown role" ]

[<Tests>]
let mixedChoiceTests =
    testList
        "ProjectionValidator.checkMixedChoice"
        [ testCase "TicTacToe has no mixed choice"
          <| fun _ ->
              let issues = checkMixedChoice ticTacToeChart
              Expect.isEmpty issues "Each state has only one role's restricted transitions"

          testCase "different roles from same state produces warning"
          <| fun _ ->
              let issues = checkMixedChoice mixedChoiceChart
              Expect.equal issues.Length 1 "One mixed-choice warning"
              Expect.equal issues[0].Severity Severity.Warning "Severity is Warning"
              Expect.stringContains issues[0].Message "Pending" "Mentions the state"

          testCase "only unrestricted transitions produce no warnings"
          <| fun _ ->
              let chart =
                  { ticTacToeChart with
                      Transitions =
                          ticTacToeChart.Transitions
                          |> List.filter (fun t -> t.Constraint = Unrestricted) }

              let issues = checkMixedChoice chart
              Expect.isEmpty issues "Unrestricted transitions don't count" ]

[<Tests>]
let completenessTests =
    testList
        "ProjectionValidator.checkCompleteness"
        [ testCase "TicTacToe is fully covered"
          <| fun _ ->
              let projections = Projection.projectAll ticTacToeChart
              let pruned = Projection.pruneUnreachableStates ticTacToeChart
              let issues = checkCompleteness projections pruned
              Expect.isEmpty issues "All transitions covered"

          testCase "dead transition is orphaned"
          <| fun _ ->
              let projections = Projection.projectAll deadTransitionChart
              let pruned = Projection.pruneUnreachableStates deadTransitionChart
              let issues = checkCompleteness projections pruned
              Expect.equal issues.Length 1 "One orphaned transition"
              Expect.stringContains issues[0].Message "archive" "Mentions the transition"
              Expect.equal issues[0].Severity Severity.Error "Severity is Error" ]

[<Tests>]
let deadlockTests =
    testList
        "ProjectionValidator.checkDeadlock"
        [ testCase "TicTacToe has no deadlocks"
          <| fun _ ->
              let projections = Projection.projectAll ticTacToeChart
              let pruned = Projection.pruneUnreachableStates ticTacToeChart
              let issues = checkDeadlock projections pruned
              Expect.isEmpty issues "All non-final states have role transitions"

          testCase "non-final state with no role outgoing is flagged"
          <| fun _ ->
              let projections = Projection.projectAll deadlockChart
              let pruned = Projection.pruneUnreachableStates deadlockChart
              let issues = checkDeadlock projections pruned
              Expect.equal issues.Length 1 "One deadlock"
              Expect.stringContains issues[0].Message "Blocked" "Mentions the state"
              Expect.equal issues[0].Severity Severity.Warning "Severity is Warning"

          testCase "final states are not flagged"
          <| fun _ ->
              let projections = Projection.projectAll ticTacToeChart
              let pruned = Projection.pruneUnreachableStates ticTacToeChart
              let issues = checkDeadlock projections pruned
              let finalIssues = issues |> List.filter (fun i -> i.Message.Contains("Wins") || i.Message.Contains("Draw"))
              Expect.isEmpty finalIssues "Final states not flagged" ]

[<Tests>]
let validateProjectionTests =
    testList
        "ProjectionValidator.validateProjection"
        [ testCase "no roles returns 0 checks"
          <| fun _ ->
              let chart = { ticTacToeChart with Roles = [] }
              let result = validateProjection chart.RouteTemplate chart
              Expect.equal result.ChecksRun 0 "No checks for roleless chart"
              Expect.isEmpty result.Issues "No issues"

          testCase "TicTacToe returns 4 checks, 0 issues"
          <| fun _ ->
              let result = validateProjection ticTacToeChart.RouteTemplate ticTacToeChart
              Expect.equal result.ChecksRun 4 "All 4 checks run"
              Expect.isEmpty result.Issues "No issues"

          testCase "dead transition chart has issues"
          <| fun _ ->
              let result = validateProjection deadTransitionChart.RouteTemplate deadTransitionChart
              Expect.equal result.ChecksRun 4 "All 4 checks run"
              Expect.isGreaterThan result.Issues.Length 0 "Has issues" ]
