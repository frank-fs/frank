module Frank.Cli.Core.Tests.DualCommandTests

open Expecto
open Frank.Resources.Model
open Frank.Statecharts.Dual
open Frank.Cli.Core.Commands.DualCommand

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private mkTransition event source target guard roleConstraint =
    { Event = event
      Source = source
      Target = target
      Guard = guard
      Constraint = roleConstraint }

// ---------------------------------------------------------------------------
// TicTacToe fixture
// ---------------------------------------------------------------------------

let private ticTacToeChart: ExtractedStatechart =
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

// ---------------------------------------------------------------------------
// Order Fulfillment fixture
// ---------------------------------------------------------------------------

let private orderFulfillmentChart: ExtractedStatechart =
    { RouteTemplate = "/orders/{orderId}"
      StateNames =
        [ "Submitted"
          "Confirmed"
          "Paid"
          "Picking"
          "Shipped"
          "Completed"
          "Cancelled" ]
      InitialStateKey = "Submitted"
      GuardNames = [ "SellerGuard"; "BuyerGuard"; "WarehouseGuard" ]
      StateMetadata =
        Map.ofList
            [ "Submitted",
              { AllowedMethods = [ "GET" ]
                IsFinal = false
                Description = None }
              "Confirmed",
              { AllowedMethods = [ "GET"; "PUT" ]
                IsFinal = false
                Description = None }
              "Paid",
              { AllowedMethods = [ "GET" ]
                IsFinal = false
                Description = None }
              "Picking",
              { AllowedMethods = [ "GET" ]
                IsFinal = false
                Description = None }
              "Shipped",
              { AllowedMethods = [ "GET" ]
                IsFinal = false
                Description = None }
              "Completed",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None }
              "Cancelled",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None } ]
      Roles =
        [ { Name = "Buyer"; Description = None }
          { Name = "Seller"; Description = None }
          { Name = "Warehouse"
            Description = None }
          { Name = "Auditor"; Description = None } ]
      Transitions =
        [ mkTransition "viewOrder" "Submitted" "Submitted" None Unrestricted
          mkTransition "confirmOrder" "Submitted" "Confirmed" (Some "SellerGuard") (RestrictedTo [ "Seller" ])
          mkTransition "rejectOrder" "Submitted" "Cancelled" (Some "SellerGuard") (RestrictedTo [ "Seller" ])
          mkTransition "viewOrder" "Confirmed" "Confirmed" None Unrestricted
          mkTransition "submitPayment" "Confirmed" "Paid" (Some "BuyerGuard") (RestrictedTo [ "Buyer" ])
          mkTransition "cancelOrder" "Confirmed" "Cancelled" (Some "BuyerGuard") (RestrictedTo [ "Buyer" ])
          mkTransition "cancelBySeller" "Confirmed" "Cancelled" (Some "SellerGuard") (RestrictedTo [ "Seller" ])
          mkTransition "viewOrder" "Paid" "Paid" None Unrestricted
          mkTransition "beginPicking" "Paid" "Picking" (Some "WarehouseGuard") (RestrictedTo [ "Warehouse" ])
          mkTransition "viewOrder" "Picking" "Picking" None Unrestricted
          mkTransition "shipOrder" "Picking" "Shipped" (Some "WarehouseGuard") (RestrictedTo [ "Warehouse" ])
          mkTransition "viewOrder" "Shipped" "Shipped" None Unrestricted
          mkTransition "confirmDelivery" "Shipped" "Completed" (Some "SellerGuard") (RestrictedTo [ "Seller" ])
          mkTransition "viewOrder" "Completed" "Completed" None Unrestricted
          mkTransition "viewOrder" "Cancelled" "Cancelled" None Unrestricted ] }

// ===========================================================================
// frank dual -- execute tests
// ===========================================================================

[<Tests>]
let dualCommandTests =
    testList
        "DualCommand.execute"
        [ testCase "TicTacToe produces per-role annotations"
          <| fun _ ->
              let result = execute ticTacToeChart

              Expect.isOk result "should succeed"

              let dualResult =
                  Result.defaultValue
                      { RoleDuals = Map.empty
                        ProtocolSinks = []
                        Summary = "" }
                      result

              Expect.equal (dualResult.RoleDuals.Count) 3 "3 roles"
              Expect.isTrue (dualResult.RoleDuals.ContainsKey "PlayerX") "has PlayerX"
              Expect.isTrue (dualResult.RoleDuals.ContainsKey "PlayerO") "has PlayerO"
              Expect.isTrue (dualResult.RoleDuals.ContainsKey "Spectator") "has Spectator"

          testCase "TicTacToe PlayerX has MustSelect in XTurn"
          <| fun _ ->
              let result = execute ticTacToeChart

              let dualResult =
                  Result.defaultValue
                      { RoleDuals = Map.empty
                        ProtocolSinks = []
                        Summary = "" }
                      result

              let playerXDuals = dualResult.RoleDuals |> Map.find "PlayerX"

              let xTurnAnnotations = playerXDuals |> List.filter (fun a -> a.State = "XTurn")

              let hasMustSelect =
                  xTurnAnnotations
                  |> List.exists (fun a -> a.Obligation = MustSelect && a.Descriptor = "makeMove")

              Expect.isTrue hasMustSelect "PlayerX must-select makeMove in XTurn"

          testCase "TicTacToe no protocol sinks"
          <| fun _ ->
              let result = execute ticTacToeChart

              let dualResult =
                  Result.defaultValue
                      { RoleDuals = Map.empty
                        ProtocolSinks = []
                        Summary = "" }
                      result

              Expect.isEmpty dualResult.ProtocolSinks "no protocol sinks"

          testCase "Order Fulfillment produces 4 role duals"
          <| fun _ ->
              let result = execute orderFulfillmentChart
              Expect.isOk result "should succeed"

              let dualResult =
                  Result.defaultValue
                      { RoleDuals = Map.empty
                        ProtocolSinks = []
                        Summary = "" }
                      result

              Expect.equal (dualResult.RoleDuals.Count) 4 "4 roles"

          testCase "Order Fulfillment Auditor is observer-only (all MayPoll or SessionComplete)"
          <| fun _ ->
              let result = execute orderFulfillmentChart

              let dualResult =
                  Result.defaultValue
                      { RoleDuals = Map.empty
                        ProtocolSinks = []
                        Summary = "" }
                      result

              let auditorDuals = dualResult.RoleDuals |> Map.find "Auditor"

              let hasNoMustSelect =
                  auditorDuals |> List.forall (fun a -> a.Obligation <> MustSelect)

              Expect.isTrue hasNoMustSelect "Auditor should never have MustSelect"

          testCase "Order Fulfillment Seller MustSelect in Submitted"
          <| fun _ ->
              let result = execute orderFulfillmentChart

              let dualResult =
                  Result.defaultValue
                      { RoleDuals = Map.empty
                        ProtocolSinks = []
                        Summary = "" }
                      result

              let sellerDuals = dualResult.RoleDuals |> Map.find "Seller"

              let submittedMustSelect =
                  sellerDuals
                  |> List.filter (fun a -> a.State = "Submitted" && a.Obligation = MustSelect)
                  |> List.map (fun a -> a.Descriptor)
                  |> Set.ofList

              Expect.isTrue (Set.contains "confirmOrder" submittedMustSelect) "confirmOrder must-select"
              Expect.isTrue (Set.contains "rejectOrder" submittedMustSelect) "rejectOrder must-select"

          testCase "Order Fulfillment summary includes role count"
          <| fun _ ->
              let result = execute orderFulfillmentChart

              let dualResult =
                  Result.defaultValue
                      { RoleDuals = Map.empty
                        ProtocolSinks = []
                        Summary = "" }
                      result

              Expect.stringContains dualResult.Summary "4" "summary mentions 4 roles"

          testCase "empty roles chart returns empty duals"
          <| fun _ ->
              let emptyChart = { ticTacToeChart with Roles = [] }
              let result = execute emptyChart
              Expect.isOk result "should succeed"

              let dualResult =
                  Result.defaultValue
                      { RoleDuals = Map.empty
                        ProtocolSinks = []
                        Summary = "" }
                      result

              Expect.isTrue (Map.isEmpty dualResult.RoleDuals) "no roles = no duals" ]

// ===========================================================================
// frank validate --check-dual tests
// ===========================================================================

[<Tests>]
let checkDualTests =
    testList
        "DualCommand.checkDual"
        [ testCase "valid TicTacToe protocol passes check-dual"
          <| fun _ ->
              let result = checkDual ticTacToeChart
              Expect.isOk result "should succeed"

              let checkResult = Result.defaultValue { Issues = []; IsConsistent = true } result

              Expect.isTrue checkResult.IsConsistent "consistent protocol"
              Expect.isEmpty checkResult.Issues "no issues"

          testCase "valid OrderFulfillment protocol passes check-dual"
          <| fun _ ->
              let result = checkDual orderFulfillmentChart
              Expect.isOk result "should succeed"

              let checkResult = Result.defaultValue { Issues = []; IsConsistent = true } result

              Expect.isTrue checkResult.IsConsistent "consistent protocol"

          testCase "protocol with deadlock reports error"
          <| fun _ ->
              // Remove beginPicking from Paid -> deadlock
              let deadlockChart =
                  { orderFulfillmentChart with
                      Transitions =
                          orderFulfillmentChart.Transitions
                          |> List.filter (fun t -> t.Event <> "beginPicking") }

              let result = checkDual deadlockChart
              Expect.isOk result "should not fail"

              let checkResult = Result.defaultValue { Issues = []; IsConsistent = true } result

              Expect.isFalse checkResult.IsConsistent "deadlock makes it inconsistent"

              let hasDeadlockIssue =
                  checkResult.Issues |> List.exists (fun i -> i.Message.Contains("Paid"))

              Expect.isTrue hasDeadlockIssue "should mention Paid as protocol sink"

          testCase "terminal states have session-complete obligations only"
          <| fun _ ->
              let result = checkDual ticTacToeChart

              let checkResult = Result.defaultValue { Issues = []; IsConsistent = true } result

              // No issue about session-complete at non-final states
              let hasSessionCompleteAtNonFinal =
                  checkResult.Issues
                  |> List.exists (fun i -> i.Message.Contains("session-complete at non-final"))

              Expect.isFalse hasSessionCompleteAtNonFinal "no session-complete at non-final states" ]

// ===========================================================================
// frank validate --check-laws tests
// ===========================================================================

[<Tests>]
let checkLawsTests =
    testList
        "DualCommand.checkLaws"
        [ testCase "guard monoid laws pass"
          <| fun _ ->
              let result = checkLaws ()
              Expect.isOk result "should succeed"

              let lawsResult = Result.defaultValue { LawResults = []; AllPassed = true } result

              let guardLaws =
                  lawsResult.LawResults
                  |> List.filter (fun l -> l.Category = "GuardResult monoid")

              Expect.isNonEmpty guardLaws "should check guard monoid laws"

              for law in guardLaws do
                  Expect.isTrue law.Passed $"guard monoid law '{law.Name}' should pass"

          testCase "TransitionResult functor laws pass"
          <| fun _ ->
              let result = checkLaws ()

              let lawsResult = Result.defaultValue { LawResults = []; AllPassed = true } result

              let functorLaws =
                  lawsResult.LawResults
                  |> List.filter (fun l -> l.Category = "TransitionResult functor")

              Expect.isNonEmpty functorLaws "should check functor laws"

              for law in functorLaws do
                  Expect.isTrue law.Passed $"functor law '{law.Name}' should pass"

          testCase "all laws pass overall"
          <| fun _ ->
              let result = checkLaws ()

              let lawsResult = Result.defaultValue { LawResults = []; AllPassed = true } result

              Expect.isTrue lawsResult.AllPassed "all algebraic laws should pass" ]
