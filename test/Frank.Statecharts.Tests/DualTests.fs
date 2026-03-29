module Frank.Statecharts.Tests.DualTests

open Expecto
open Frank.Resources.Model
open Frank.Statecharts.Dual

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
// TicTacToe fixture: 2 players + 1 spectator, 5 states
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
// Order Fulfillment fixture: 4 roles, 7 states
// (Buyer, Seller, Warehouse, Auditor)
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
        [ // Submitted: viewOrder (all), confirmOrder (Seller), rejectOrder (Seller)
          mkTransition "viewOrder" "Submitted" "Submitted" None Unrestricted
          mkTransition "confirmOrder" "Submitted" "Confirmed" (Some "SellerGuard") (RestrictedTo [ "Seller" ])
          mkTransition "rejectOrder" "Submitted" "Cancelled" (Some "SellerGuard") (RestrictedTo [ "Seller" ])
          // Confirmed: viewOrder (all), submitPayment (Buyer), cancelOrder (Buyer), cancelBySeller (Seller)
          mkTransition "viewOrder" "Confirmed" "Confirmed" None Unrestricted
          mkTransition "submitPayment" "Confirmed" "Paid" (Some "BuyerGuard") (RestrictedTo [ "Buyer" ])
          mkTransition "cancelOrder" "Confirmed" "Cancelled" (Some "BuyerGuard") (RestrictedTo [ "Buyer" ])
          mkTransition "cancelBySeller" "Confirmed" "Cancelled" (Some "SellerGuard") (RestrictedTo [ "Seller" ])
          // Paid: viewOrder (all), beginPicking (Warehouse)
          mkTransition "viewOrder" "Paid" "Paid" None Unrestricted
          mkTransition "beginPicking" "Paid" "Picking" (Some "WarehouseGuard") (RestrictedTo [ "Warehouse" ])
          // Picking: viewOrder (all), shipOrder (Warehouse)
          mkTransition "viewOrder" "Picking" "Picking" None Unrestricted
          mkTransition "shipOrder" "Picking" "Shipped" (Some "WarehouseGuard") (RestrictedTo [ "Warehouse" ])
          // Shipped: viewOrder (all), confirmDelivery (Seller)
          mkTransition "viewOrder" "Shipped" "Shipped" None Unrestricted
          mkTransition "confirmDelivery" "Shipped" "Completed" (Some "SellerGuard") (RestrictedTo [ "Seller" ])
          // Completed: viewOrder (all) — terminal
          mkTransition "viewOrder" "Completed" "Completed" None Unrestricted
          // Cancelled: viewOrder (all) — terminal
          mkTransition "viewOrder" "Cancelled" "Cancelled" None Unrestricted ] }

/// Helper: look up annotations for a specific (role, state) pair.
let private annotationsFor role state (result: DeriveResult) =
    result.Annotations |> Map.tryFind (role, state) |> Option.defaultValue []

/// Helper: find a specific annotation by descriptor name.
let private findAnnotation descriptor (annotations: DualAnnotation list) =
    annotations |> List.tryFind (fun a -> a.Descriptor = descriptor)

/// Helper: get all obligations for a (role, state) pair.
let private obligationsFor role state (result: DeriveResult) =
    annotationsFor role state result
    |> List.map (fun a -> a.Descriptor, a.Obligation)
    |> List.distinctBy fst
    |> Map.ofList

// ===========================================================================
// TicTacToe dual derivation tests
// ===========================================================================

[<Tests>]
let ticTacToeDualTests =
    let projections = Projection.projectAll ticTacToeChart
    let result = derive ticTacToeChart projections

    testList
        "Dual.derive TicTacToe"
        [ testCase "PlayerX in XTurn: MustSelect on makeMove"
          <| fun _ ->
              let obligations = obligationsFor "PlayerX" "XTurn" result
              Expect.equal (Map.find "makeMove" obligations) MustSelect "makeMove must-select in XTurn"

          testCase "PlayerX in XTurn: MayPoll on getGame"
          <| fun _ ->
              let obligations = obligationsFor "PlayerX" "XTurn" result
              Expect.equal (Map.find "getGame" obligations) MayPoll "getGame may-poll in XTurn"

          testCase "PlayerX in OTurn: MayPoll only"
          <| fun _ ->
              let obligations = obligationsFor "PlayerX" "OTurn" result

              for (_descriptor, obligation) in Map.toList obligations do
                  Expect.equal obligation MayPoll "PlayerX in OTurn should only have MayPoll"

          testCase "PlayerX in XWins: SessionComplete"
          <| fun _ ->
              let annotations = annotationsFor "PlayerX" "XWins" result

              let hasSessionComplete =
                  annotations |> List.exists (fun a -> a.Obligation = SessionComplete)

              Expect.isTrue hasSessionComplete "XWins should have SessionComplete"

          testCase "PlayerX in OWins: SessionComplete"
          <| fun _ ->
              let annotations = annotationsFor "PlayerX" "OWins" result

              let hasSessionComplete =
                  annotations |> List.exists (fun a -> a.Obligation = SessionComplete)

              Expect.isTrue hasSessionComplete "OWins should have SessionComplete"

          testCase "PlayerX in Draw: SessionComplete"
          <| fun _ ->
              let annotations = annotationsFor "PlayerX" "Draw" result

              let hasSessionComplete =
                  annotations |> List.exists (fun a -> a.Obligation = SessionComplete)

              Expect.isTrue hasSessionComplete "Draw should have SessionComplete"

          testCase "PlayerO in OTurn: MustSelect on makeMove"
          <| fun _ ->
              let obligations = obligationsFor "PlayerO" "OTurn" result
              Expect.equal (Map.find "makeMove" obligations) MustSelect "makeMove must-select in OTurn"

          testCase "PlayerO in XTurn: MayPoll only"
          <| fun _ ->
              let obligations = obligationsFor "PlayerO" "XTurn" result

              for (_descriptor, obligation) in Map.toList obligations do
                  Expect.equal obligation MayPoll "PlayerO in XTurn should only have MayPoll"

          testCase "PlayerO mirrors PlayerX in terminal states"
          <| fun _ ->
              for state in [ "XWins"; "OWins"; "Draw" ] do
                  let annotations = annotationsFor "PlayerO" state result

                  let hasSessionComplete =
                      annotations |> List.exists (fun a -> a.Obligation = SessionComplete)

                  Expect.isTrue hasSessionComplete $"PlayerO in {state} should be SessionComplete"

          testCase "Spectator: MayPoll in all non-terminal states"
          <| fun _ ->
              for state in [ "XTurn"; "OTurn" ] do
                  let obligations = obligationsFor "Spectator" state result

                  for (_descriptor, obligation) in Map.toList obligations do
                      Expect.equal obligation MayPoll $"Spectator in {state} should be MayPoll"

          testCase "Spectator: SessionComplete in all terminal states"
          <| fun _ ->
              for state in [ "XWins"; "OWins"; "Draw" ] do
                  let annotations = annotationsFor "Spectator" state result

                  let hasSessionComplete =
                      annotations |> List.exists (fun a -> a.Obligation = SessionComplete)

                  Expect.isTrue hasSessionComplete $"Spectator in {state} should be SessionComplete"

          testCase "makeMove AdvancesProtocol is true"
          <| fun _ ->
              let annotations = annotationsFor "PlayerX" "XTurn" result
              let makeMove = findAnnotation "makeMove" annotations
              Expect.isSome makeMove "makeMove annotation exists"
              Expect.isTrue makeMove.Value.AdvancesProtocol "makeMove advances protocol"

          testCase "getGame AdvancesProtocol is false"
          <| fun _ ->
              let annotations = annotationsFor "PlayerX" "XTurn" result
              let getGame = findAnnotation "getGame" annotations
              Expect.isSome getGame "getGame annotation exists"
              Expect.isFalse getGame.Value.AdvancesProtocol "getGame does not advance protocol"

          testCase "no deadlocks flagged"
          <| fun _ -> Expect.isEmpty result.ProtocolSinks "TicTacToe has no deadlocks"

          testCase "no cut points in TicTacToe"
          <| fun _ ->
              let allAnnotations =
                  result.Annotations |> Map.values |> Seq.collect id |> Seq.toList

              let cutPoints = allAnnotations |> List.choose (fun a -> a.CutPoint)
              Expect.isEmpty cutPoints "TicTacToe has no cut points" ]

// ===========================================================================
// Order Fulfillment dual derivation tests
// ===========================================================================

[<Tests>]
let orderFulfillmentDualTests =
    let projections = Projection.projectAll orderFulfillmentChart
    let result = derive orderFulfillmentChart projections

    testList
        "Dual.derive OrderFulfillment"
        [ // -- Buyer tests --
          testCase "Buyer in Submitted: MayPoll only (observer)"
          <| fun _ ->
              let obligations = obligationsFor "Buyer" "Submitted" result

              for (_descriptor, obligation) in Map.toList obligations do
                  Expect.equal obligation MayPoll "Buyer in Submitted is observer (MayPoll only)"

          testCase "Buyer in Confirmed: MustSelect on submitPayment"
          <| fun _ ->
              let obligations = obligationsFor "Buyer" "Confirmed" result
              Expect.equal (Map.find "submitPayment" obligations) MustSelect "submitPayment must-select"

          testCase "Buyer in Confirmed: MustSelect on cancelOrder"
          <| fun _ ->
              let obligations = obligationsFor "Buyer" "Confirmed" result
              Expect.equal (Map.find "cancelOrder" obligations) MustSelect "cancelOrder must-select"

          testCase "Buyer in Confirmed: MayPoll on viewOrder"
          <| fun _ ->
              let obligations = obligationsFor "Buyer" "Confirmed" result
              Expect.equal (Map.find "viewOrder" obligations) MayPoll "viewOrder may-poll"

          testCase "Buyer in Paid: MayPoll only (observer again)"
          <| fun _ ->
              let obligations = obligationsFor "Buyer" "Paid" result

              for (_descriptor, obligation) in Map.toList obligations do
                  Expect.equal obligation MayPoll "Buyer in Paid is observer (MayPoll only)"

          testCase "Buyer in Picking: MayPoll only"
          <| fun _ ->
              let obligations = obligationsFor "Buyer" "Picking" result

              for (_descriptor, obligation) in Map.toList obligations do
                  Expect.equal obligation MayPoll "Buyer in Picking is observer"

          testCase "Buyer in Shipped: MayPoll only"
          <| fun _ ->
              let obligations = obligationsFor "Buyer" "Shipped" result

              for (_descriptor, obligation) in Map.toList obligations do
                  Expect.equal obligation MayPoll "Buyer in Shipped is observer"

          // -- Seller tests --
          testCase "Seller in Submitted: MustSelect on confirmOrder"
          <| fun _ ->
              let obligations = obligationsFor "Seller" "Submitted" result
              Expect.equal (Map.find "confirmOrder" obligations) MustSelect "confirmOrder must-select"

          testCase "Seller in Submitted: MustSelect on rejectOrder"
          <| fun _ ->
              let obligations = obligationsFor "Seller" "Submitted" result
              Expect.equal (Map.find "rejectOrder" obligations) MustSelect "rejectOrder must-select"

          testCase "Seller in Confirmed: MustSelect on cancelBySeller"
          <| fun _ ->
              let obligations = obligationsFor "Seller" "Confirmed" result
              Expect.equal (Map.find "cancelBySeller" obligations) MustSelect "cancelBySeller must-select"

          testCase "Seller in Confirmed: MayPoll on viewOrder"
          <| fun _ ->
              let obligations = obligationsFor "Seller" "Confirmed" result
              Expect.equal (Map.find "viewOrder" obligations) MayPoll "viewOrder may-poll"

          // -- Warehouse tests --
          testCase "Warehouse in Paid: MustSelect on beginPicking"
          <| fun _ ->
              let obligations = obligationsFor "Warehouse" "Paid" result
              Expect.equal (Map.find "beginPicking" obligations) MustSelect "beginPicking must-select"

          testCase "Warehouse in Picking: MustSelect on shipOrder"
          <| fun _ ->
              let obligations = obligationsFor "Warehouse" "Picking" result
              Expect.equal (Map.find "shipOrder" obligations) MustSelect "shipOrder must-select"

          testCase "Warehouse in Submitted: MayPoll only"
          <| fun _ ->
              let obligations = obligationsFor "Warehouse" "Submitted" result

              for (_descriptor, obligation) in Map.toList obligations do
                  Expect.equal obligation MayPoll "Warehouse in Submitted is observer"

          testCase "Warehouse in Confirmed: MayPoll only"
          <| fun _ ->
              let obligations = obligationsFor "Warehouse" "Confirmed" result

              for (_descriptor, obligation) in Map.toList obligations do
                  Expect.equal obligation MayPoll "Warehouse in Confirmed is observer"

          testCase "Warehouse in Shipped: MayPoll only"
          <| fun _ ->
              let obligations = obligationsFor "Warehouse" "Shipped" result

              for (_descriptor, obligation) in Map.toList obligations do
                  Expect.equal obligation MayPoll "Warehouse in Shipped is observer"

          // -- Auditor tests (pure observer) --
          testCase "Auditor in all non-terminal states: MayPoll only"
          <| fun _ ->
              for state in [ "Submitted"; "Confirmed"; "Paid"; "Picking"; "Shipped" ] do
                  let obligations = obligationsFor "Auditor" state result

                  for (_descriptor, obligation) in Map.toList obligations do
                      Expect.equal obligation MayPoll $"Auditor in {state} should be MayPoll"

          testCase "Auditor in terminal states: SessionComplete"
          <| fun _ ->
              for state in [ "Completed"; "Cancelled" ] do
                  let annotations = annotationsFor "Auditor" state result

                  let hasSessionComplete =
                      annotations |> List.exists (fun a -> a.Obligation = SessionComplete)

                  Expect.isTrue hasSessionComplete $"Auditor in {state} should be SessionComplete"

          // -- Multi-role advancing state (Confirmed) --
          testCase "Confirmed: both Buyer and Seller have MustSelect transitions"
          <| fun _ ->
              let buyerObligations = obligationsFor "Buyer" "Confirmed" result

              let buyerHasMustSelect = buyerObligations |> Map.exists (fun _ o -> o = MustSelect)

              let sellerObligations = obligationsFor "Seller" "Confirmed" result

              let sellerHasMustSelect =
                  sellerObligations |> Map.exists (fun _ o -> o = MustSelect)

              Expect.isTrue buyerHasMustSelect "Buyer has MustSelect in Confirmed"
              Expect.isTrue sellerHasMustSelect "Seller has MustSelect in Confirmed"

          // -- Terminal states --
          testCase "all roles: SessionComplete in Completed"
          <| fun _ ->
              for role in [ "Buyer"; "Seller"; "Warehouse"; "Auditor" ] do
                  let annotations = annotationsFor role "Completed" result

                  let hasSessionComplete =
                      annotations |> List.exists (fun a -> a.Obligation = SessionComplete)

                  Expect.isTrue hasSessionComplete $"{role} in Completed should be SessionComplete"

          testCase "all roles: SessionComplete in Cancelled"
          <| fun _ ->
              for role in [ "Buyer"; "Seller"; "Warehouse"; "Auditor" ] do
                  let annotations = annotationsFor role "Cancelled" result

                  let hasSessionComplete =
                      annotations |> List.exists (fun a -> a.Obligation = SessionComplete)

                  Expect.isTrue hasSessionComplete $"{role} in Cancelled should be SessionComplete"

          // -- AdvancesProtocol --
          testCase "confirmOrder advances protocol"
          <| fun _ ->
              let annotations = annotationsFor "Seller" "Submitted" result
              let confirmOrder = findAnnotation "confirmOrder" annotations
              Expect.isSome confirmOrder "confirmOrder annotation exists"
              Expect.isTrue confirmOrder.Value.AdvancesProtocol "confirmOrder advances protocol"

          testCase "viewOrder does not advance protocol"
          <| fun _ ->
              let annotations = annotationsFor "Buyer" "Submitted" result
              let viewOrder = findAnnotation "viewOrder" annotations
              Expect.isSome viewOrder "viewOrder annotation exists"
              Expect.isFalse viewOrder.Value.AdvancesProtocol "viewOrder does not advance protocol"

          // -- No deadlocks in the standard fixture --
          testCase "no deadlocks in order fulfillment"
          <| fun _ -> Expect.isEmpty result.ProtocolSinks "OrderFulfillment has no deadlocks" ]

// ===========================================================================
// Cut point tests
// ===========================================================================

[<Tests>]
let cutPointTests =
    // Cut points come from ALPS annotations, passed as a separate parameter to deriveWithCutPoints.
    // The chart itself is unchanged — we just use the standard orderFulfillmentChart.
    let projections = Projection.projectAll orderFulfillmentChart

    let cutPoints = Map.ofList [ "beginPicking", "service-b#acceptOrder" ]
    let result = deriveWithCutPoints orderFulfillmentChart projections cutPoints

    testList
        "Dual.derive CutPoints"
        [ testCase "beginPicking has cut point annotation"
          <| fun _ ->
              let annotations = annotationsFor "Warehouse" "Paid" result
              let beginPicking = findAnnotation "beginPicking" annotations
              Expect.isSome beginPicking "beginPicking annotation exists"
              Expect.equal beginPicking.Value.CutPoint (Some "service-b#acceptOrder") "cut point value"

          testCase "non-cut-point transitions have no cut point"
          <| fun _ ->
              let annotations = annotationsFor "Seller" "Submitted" result
              let confirmOrder = findAnnotation "confirmOrder" annotations
              Expect.isSome confirmOrder "confirmOrder annotation exists"
              Expect.isNone confirmOrder.Value.CutPoint "confirmOrder has no cut point" ]

// ===========================================================================
// Deadlock detection tests
// ===========================================================================

[<Tests>]
let deadlockTests =
    // Remove beginPicking from Paid -> no role can advance from Paid state
    let deadlockChart: ExtractedStatechart =
        { orderFulfillmentChart with
            Transitions =
                orderFulfillmentChart.Transitions
                |> List.filter (fun t -> t.Event <> "beginPicking") }

    let projections = Projection.projectAll deadlockChart
    let result = derive deadlockChart projections

    testList
        "Dual.derive ProtocolSink detection"
        [ testCase "Paid is the only protocol sink when beginPicking removed"
          <| fun _ -> Expect.equal result.ProtocolSinks [ "Paid" ] "Paid should be the only protocol sink" ]

// ===========================================================================
// DualOf annotation tests
// ===========================================================================

[<Tests>]
let dualOfTests =
    let projections = Projection.projectAll orderFulfillmentChart
    let dualOfMap = Map.ofList [ "submitPayment", "#confirmOrder" ]
    let result = deriveWithDualOf orderFulfillmentChart projections dualOfMap

    testList
        "Dual.derive DualOf annotations"
        [ testCase "submitPayment has dualOf annotation"
          <| fun _ ->
              let annotations = annotationsFor "Buyer" "Confirmed" result
              let submitPayment = findAnnotation "submitPayment" annotations
              Expect.isSome submitPayment "submitPayment annotation exists"
              Expect.equal submitPayment.Value.DualOf (Some "#confirmOrder") "dualOf references confirmOrder" ]

// ===========================================================================
// Empty and edge case tests
// ===========================================================================

[<Tests>]
let edgeCaseTests =
    testList
        "Dual.derive edge cases"
        [ testCase "no roles yields empty result"
          <| fun _ ->
              let noRoleChart: ExtractedStatechart = { ticTacToeChart with Roles = [] }

              let projections = Projection.projectAll noRoleChart
              let result = derive noRoleChart projections
              Expect.isTrue (Map.isEmpty result.Annotations) "empty roles → empty annotations"
              Expect.isEmpty result.ProtocolSinks "no deadlocks with no roles"

          testCase "single state chart with final state"
          <| fun _ ->
              let singleStateChart: ExtractedStatechart =
                  { RouteTemplate = "/done"
                    StateNames = [ "Done" ]
                    InitialStateKey = "Done"
                    GuardNames = []
                    StateMetadata =
                      Map.ofList
                          [ "Done",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None } ]
                    Roles = [ { Name = "User"; Description = None } ]
                    Transitions = [ mkTransition "view" "Done" "Done" None Unrestricted ] }

              let projections = Projection.projectAll singleStateChart
              let result = derive singleStateChart projections
              let annotations = annotationsFor "User" "Done" result

              let hasSessionComplete =
                  annotations |> List.exists (fun a -> a.Obligation = SessionComplete)

              Expect.isTrue hasSessionComplete "single final state yields SessionComplete" ]

// ===========================================================================
// FsCheck property tests
// ===========================================================================

[<Tests>]
let propertyTests =
    testList
        "Dual.derive properties"
        [ testCase "every final state for every role has SessionComplete"
          <| fun _ ->
              let charts = [ ticTacToeChart; orderFulfillmentChart ]

              for chart in charts do
                  let projections = Projection.projectAll chart
                  let result = derive chart projections

                  let finalStates =
                      chart.StateMetadata
                      |> Map.filter (fun _ info -> info.IsFinal)
                      |> Map.keys
                      |> Seq.toList

                  for role in chart.Roles do
                      for state in finalStates do
                          let annotations = annotationsFor role.Name state result

                          let hasSessionComplete =
                              annotations |> List.exists (fun a -> a.Obligation = SessionComplete)

                          Expect.isTrue
                              hasSessionComplete
                              $"{role.Name} in final state {state} should have SessionComplete"

          testCase "self-loop transitions in non-final states always produce MayPoll"
          <| fun _ ->
              let charts = [ ticTacToeChart; orderFulfillmentChart ]

              for chart in charts do
                  let projections = Projection.projectAll chart
                  let result = derive chart projections

                  let finalStates =
                      chart.StateMetadata
                      |> Map.filter (fun _ info -> info.IsFinal)
                      |> Map.keys
                      |> Set.ofSeq

                  for role in chart.Roles do
                      for state in chart.StateNames do
                          if not (Set.contains state finalStates) then
                              let annotations = annotationsFor role.Name state result

                              for ann in annotations do
                                  // Find matching self-loop transition
                                  let matchingTransitions =
                                      chart.Transitions
                                      |> List.filter (fun t ->
                                          t.Event = ann.Descriptor && t.Source = state && t.Target = state)

                                  if not matchingTransitions.IsEmpty && not ann.AdvancesProtocol then
                                      Expect.equal
                                          ann.Obligation
                                          MayPoll
                                          $"{role.Name}/{state}/{ann.Descriptor}: self-loop should be MayPoll"

          testCase "advancing transitions never produce MayPoll (except in final states)"
          <| fun _ ->
              let charts = [ ticTacToeChart; orderFulfillmentChart ]

              for chart in charts do
                  let projections = Projection.projectAll chart
                  let result = derive chart projections

                  let finalStates =
                      chart.StateMetadata
                      |> Map.filter (fun _ info -> info.IsFinal)
                      |> Map.keys
                      |> Set.ofSeq

                  for role in chart.Roles do
                      for state in chart.StateNames do
                          if not (Set.contains state finalStates) then
                              let annotations = annotationsFor role.Name state result

                              for ann in annotations do
                                  if ann.AdvancesProtocol then
                                      Expect.notEqual
                                          ann.Obligation
                                          MayPoll
                                          $"{role.Name}/{state}/{ann.Descriptor}: advancing should not be MayPoll"

          testCase "observer-to-actor transitions: Buyer oscillates between observer and actor"
          <| fun _ ->
              let projections = Projection.projectAll orderFulfillmentChart
              let result = derive orderFulfillmentChart projections

              // Buyer in Submitted: observer (only viewOrder, MayPoll)
              let buyerSubmitted = obligationsFor "Buyer" "Submitted" result

              let buyerSubmittedAllMayPoll = buyerSubmitted |> Map.forall (fun _ o -> o = MayPoll)

              Expect.isTrue buyerSubmittedAllMayPoll "Buyer is observer in Submitted"

              // Buyer in Confirmed: actor (has MustSelect)
              let buyerConfirmed = obligationsFor "Buyer" "Confirmed" result

              let buyerConfirmedHasMustSelect =
                  buyerConfirmed |> Map.exists (fun _ o -> o = MustSelect)

              Expect.isTrue buyerConfirmedHasMustSelect "Buyer is actor in Confirmed"

              // Buyer in Paid: observer again
              let buyerPaid = obligationsFor "Buyer" "Paid" result
              let buyerPaidAllMayPoll = buyerPaid |> Map.forall (fun _ o -> o = MayPoll)
              Expect.isTrue buyerPaidAllMayPoll "Buyer is observer again in Paid" ]
