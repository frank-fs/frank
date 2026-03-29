module Frank.Statecharts.Tests.DualEnhancementTests

open Expecto
open FsCheck
open FsCheck.FSharp
open Frank.Resources.Model
open Frank.Statecharts
open Frank.Statecharts.Dual

// ===========================================================================
// Helpers
// ===========================================================================

let private mkTransition event source target guard roleConstraint =
    { Event = event
      Source = source
      Target = target
      Guard = guard
      Constraint = roleConstraint }

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
// 4-role Order Fulfillment fixture (Buyer/Seller/Warehouse/Shipper)
// Per issue requirement: "Don't overfit to TicTacToe's symmetric 2-player structure."
// ===========================================================================

let private orderFulfillmentChart: ExtractedStatechart =
    { RouteTemplate = "/orders/{orderId}"
      StateNames =
        [ "Submitted"
          "Confirmed"
          "Paid"
          "Picking"
          "Shipped"
          "Delivered"
          "Cancelled" ]
      InitialStateKey = "Submitted"
      GuardNames = [ "SellerGuard"; "BuyerGuard"; "WarehouseGuard"; "ShipperGuard" ]
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
              "Delivered",
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
          { Name = "Shipper"; Description = None } ]
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
          // Shipped: viewOrder (all), confirmDelivery (Shipper)
          mkTransition "viewOrder" "Shipped" "Shipped" None Unrestricted
          mkTransition "confirmDelivery" "Shipped" "Delivered" (Some "ShipperGuard") (RestrictedTo [ "Shipper" ])
          // Delivered: viewOrder (all) -- terminal
          mkTransition "viewOrder" "Delivered" "Delivered" None Unrestricted
          // Cancelled: viewOrder (all) -- terminal
          mkTransition "viewOrder" "Cancelled" "Cancelled" None Unrestricted ] }

// ===========================================================================
// Enhancement 7: MayObserve for final states
// "Self-loops in final states classified as SessionComplete. Consider MayObserve
// to distinguish 'session done, can still read' from 'session done, go away.'"
// ===========================================================================

[<Tests>]
let mayObserveTests =
    let projections = Projection.projectAll orderFulfillmentChart
    let result = derive orderFulfillmentChart projections

    testList
        "Enhancement 7: MayObserve for final states"
        [ testCase "final state with self-loop transitions yields MayObserve"
          <| fun _ ->
              // Delivered has a viewOrder self-loop -- client can still read
              let annotations = annotationsFor "Buyer" "Delivered" result
              let viewOrder = findAnnotation "viewOrder" annotations
              Expect.isSome viewOrder "viewOrder annotation exists in Delivered"
              Expect.equal viewOrder.Value.Obligation MayObserve "self-loop in final state should be MayObserve"

          testCase "MayObserve in final state does not advance protocol"
          <| fun _ ->
              let annotations = annotationsFor "Buyer" "Delivered" result

              for ann in annotations do
                  if ann.Obligation = MayObserve then
                      Expect.isFalse ann.AdvancesProtocol "MayObserve should not advance protocol"

          testCase "all roles get MayObserve for self-loops in Delivered"
          <| fun _ ->
              for role in [ "Buyer"; "Seller"; "Warehouse"; "Shipper" ] do
                  let annotations = annotationsFor role "Delivered" result
                  let viewOrder = findAnnotation "viewOrder" annotations
                  Expect.isSome viewOrder $"{role} has viewOrder in Delivered"

                  Expect.equal
                      viewOrder.Value.Obligation
                      MayObserve
                      $"{role} viewOrder in Delivered should be MayObserve"

          testCase "final state with no self-loop yields SessionComplete"
          <| fun _ ->
              // Create a chart where final state has no self-loop
              let noSelfLoopChart: ExtractedStatechart =
                  { RouteTemplate = "/done"
                    StateNames = [ "Active"; "Done" ]
                    InitialStateKey = "Active"
                    GuardNames = []
                    StateMetadata =
                      Map.ofList
                          [ "Active",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None }
                            "Done",
                            { AllowedMethods = []
                              IsFinal = true
                              Description = None } ]
                    Roles = [ { Name = "User"; Description = None } ]
                    Transitions = [ mkTransition "finish" "Active" "Done" None Unrestricted ] }

              let proj = Projection.projectAll noSelfLoopChart
              let res = derive noSelfLoopChart proj
              let annotations = annotationsFor "User" "Done" res
              // No self-loops in Done, so no MayObserve annotations
              let hasMayObserve = annotations |> List.exists (fun a -> a.Obligation = MayObserve)
              Expect.isFalse hasMayObserve "no self-loop in final state means no MayObserve" ]

// ===========================================================================
// Enhancement 2: Method safety integration
// "An unsafe POST self-loop should not be MayPoll."
// ===========================================================================

[<Tests>]
let methodSafetyTests =
    // Chart where a self-loop is associated with an unsafe method (PUT/POST)
    let unsafeSelfLoopChart: ExtractedStatechart =
        { RouteTemplate = "/items/{itemId}"
          StateNames = [ "Draft"; "Published" ]
          InitialStateKey = "Draft"
          GuardNames = []
          StateMetadata =
            Map.ofList
                [ "Draft",
                  { AllowedMethods = [ "GET"; "POST"; "PUT" ]
                    IsFinal = false
                    Description = None }
                  "Published",
                  { AllowedMethods = [ "GET" ]
                    IsFinal = true
                    Description = None } ]
          Roles = [ { Name = "Author"; Description = None } ]
          Transitions =
            [ // GET self-loop (safe)
              mkTransition "viewItem" "Draft" "Draft" None Unrestricted
              // POST self-loop (unsafe) -- updating draft content
              mkTransition "updateDraft" "Draft" "Draft" None Unrestricted
              // Advancing transition
              mkTransition "publish" "Draft" "Published" None Unrestricted ] }

    testList
        "Enhancement 2: Method safety integration"
        [ testCase "safe GET self-loop is MayPoll"
          <| fun _ ->
              let projections = Projection.projectAll unsafeSelfLoopChart

              let result =
                  deriveWithMethodInfo
                      unsafeSelfLoopChart
                      projections
                      Map.empty
                      Map.empty
                      (Map.ofList [ "viewItem", true; "updateDraft", false; "publish", false ])

              let annotations = annotationsFor "Author" "Draft" result
              let viewItem = findAnnotation "viewItem" annotations
              Expect.isSome viewItem "viewItem annotation exists"
              Expect.equal viewItem.Value.Obligation MayPoll "safe GET self-loop should be MayPoll"

          testCase "unsafe POST self-loop is MustSelect, not MayPoll"
          <| fun _ ->
              let projections = Projection.projectAll unsafeSelfLoopChart

              let result =
                  deriveWithMethodInfo
                      unsafeSelfLoopChart
                      projections
                      Map.empty
                      Map.empty
                      (Map.ofList [ "viewItem", true; "updateDraft", false; "publish", false ])

              let annotations = annotationsFor "Author" "Draft" result
              let updateDraft = findAnnotation "updateDraft" annotations
              Expect.isSome updateDraft "updateDraft annotation exists"
              Expect.equal updateDraft.Value.Obligation MustSelect "unsafe POST self-loop should be MustSelect"

          testCase "backward compat: derive without method info treats all self-loops as MayPoll"
          <| fun _ ->
              let projections = Projection.projectAll unsafeSelfLoopChart
              let result = derive unsafeSelfLoopChart projections
              let annotations = annotationsFor "Author" "Draft" result
              let updateDraft = findAnnotation "updateDraft" annotations
              Expect.isSome updateDraft "updateDraft annotation exists"
              Expect.equal updateDraft.Value.Obligation MayPoll "without method info, self-loops default to MayPoll" ]

// ===========================================================================
// Enhancement 3: Race condition detection
// "States where multiple roles have competing MustSelect transitions should be flagged."
// ===========================================================================

[<Tests>]
let raceConditionTests =
    let projections = Projection.projectAll orderFulfillmentChart
    let result = derive orderFulfillmentChart projections

    testList
        "Enhancement 3: Race condition detection"
        [ testCase "Confirmed state has race condition between Buyer and Seller"
          <| fun _ ->
              // Both Buyer (submitPayment, cancelOrder) and Seller (cancelBySeller) have MustSelect in Confirmed
              Expect.isNonEmpty result.RaceConditions "should detect at least one race condition"

              let confirmedRace =
                  result.RaceConditions |> List.tryFind (fun rc -> rc.State = "Confirmed")

              Expect.isSome confirmedRace "Confirmed should have a race condition"
              let rc = confirmedRace.Value
              Expect.isTrue (Set.contains "Buyer" rc.CompetingRoles) "Buyer competes in Confirmed"
              Expect.isTrue (Set.contains "Seller" rc.CompetingRoles) "Seller competes in Confirmed"

          testCase "no race condition in Submitted (only Seller has MustSelect)"
          <| fun _ ->
              let submittedRace =
                  result.RaceConditions |> List.tryFind (fun rc -> rc.State = "Submitted")

              Expect.isNone submittedRace "Submitted has no race condition (only Seller acts)"

          testCase "no race condition in Paid (only Warehouse has MustSelect)"
          <| fun _ ->
              let paidRace = result.RaceConditions |> List.tryFind (fun rc -> rc.State = "Paid")
              Expect.isNone paidRace "Paid has no race condition (only Warehouse acts)" ]

// ===========================================================================
// Enhancement 4: Cut point enrichment
// "Cut points are opaque strings. Should carry: target URI template, authority boundary, link relation."
// ===========================================================================

[<Tests>]
let cutPointEnrichmentTests =
    let projections = Projection.projectAll orderFulfillmentChart

    let enrichedCutPoints =
        Map.ofList
            [ "beginPicking",
              { TargetUriTemplate = "/warehouse/{warehouseId}/orders/{orderId}"
                AuthorityBoundary = "warehouse-service.internal"
                LinkRelation = Some "https://rels.example.com/pick-order" } ]

    let result =
        deriveWithEnrichedCutPoints orderFulfillmentChart projections enrichedCutPoints Map.empty

    testList
        "Enhancement 4: Cut point enrichment"
        [ testCase "enriched cut point has target URI template"
          <| fun _ ->
              let annotations = annotationsFor "Warehouse" "Paid" result
              let beginPicking = findAnnotation "beginPicking" annotations
              Expect.isSome beginPicking "beginPicking annotation exists"

              match beginPicking.Value.CutPoint with
              | Some(Enriched cp) ->
                  Expect.equal cp.TargetUriTemplate "/warehouse/{warehouseId}/orders/{orderId}" "URI template"
              | _ -> failtest "expected enriched cut point"

          testCase "enriched cut point has authority boundary"
          <| fun _ ->
              let annotations = annotationsFor "Warehouse" "Paid" result
              let beginPicking = findAnnotation "beginPicking" annotations

              match beginPicking.Value.CutPoint with
              | Some(Enriched cp) -> Expect.equal cp.AuthorityBoundary "warehouse-service.internal" "authority boundary"
              | _ -> failtest "expected enriched cut point"

          testCase "enriched cut point has link relation"
          <| fun _ ->
              let annotations = annotationsFor "Warehouse" "Paid" result
              let beginPicking = findAnnotation "beginPicking" annotations

              match beginPicking.Value.CutPoint with
              | Some(Enriched cp) ->
                  Expect.equal cp.LinkRelation (Some "https://rels.example.com/pick-order") "link relation"
              | _ -> failtest "expected enriched cut point"

          testCase "backward compat: plain string cut points still work"
          <| fun _ ->
              let plainCutPoints = Map.ofList [ "beginPicking", "service-b#acceptOrder" ]

              let plainResult =
                  deriveWithCutPoints orderFulfillmentChart projections plainCutPoints

              let annotations = annotationsFor "Warehouse" "Paid" plainResult
              let beginPicking = findAnnotation "beginPicking" annotations
              Expect.isSome beginPicking "beginPicking annotation exists"

              match beginPicking.Value.CutPoint with
              | Some(Opaque s) -> Expect.equal s "service-b#acceptOrder" "opaque cut point preserved"
              | _ -> failtest "expected opaque cut point" ]

// ===========================================================================
// Enhancement 5: Circular wait detection
// "Current deadlock/protocol-sink detection misses circular wait
//  (Role A waits for B, B waits for A)."
// ===========================================================================

[<Tests>]
let circularWaitTests =
    // Chart with circular dependency: Buyer waiting for Seller, Seller waiting for Buyer
    let circularChart: ExtractedStatechart =
        { RouteTemplate = "/negotiation/{id}"
          StateNames = [ "BuyerOffer"; "SellerCounter"; "Agreed"; "Cancelled" ]
          InitialStateKey = "BuyerOffer"
          GuardNames = []
          StateMetadata =
            Map.ofList
                [ "BuyerOffer",
                  { AllowedMethods = [ "GET"; "PUT" ]
                    IsFinal = false
                    Description = None }
                  "SellerCounter",
                  { AllowedMethods = [ "GET"; "PUT" ]
                    IsFinal = false
                    Description = None }
                  "Agreed",
                  { AllowedMethods = [ "GET" ]
                    IsFinal = true
                    Description = None }
                  "Cancelled",
                  { AllowedMethods = [ "GET" ]
                    IsFinal = true
                    Description = None } ]
          Roles =
            [ { Name = "Buyer"; Description = None }
              { Name = "Seller"; Description = None } ]
          Transitions =
            [ mkTransition "view" "BuyerOffer" "BuyerOffer" None Unrestricted
              mkTransition "view" "SellerCounter" "SellerCounter" None Unrestricted
              // Buyer can counter from SellerCounter -> BuyerOffer
              mkTransition "counterOffer" "SellerCounter" "BuyerOffer" None (RestrictedTo [ "Buyer" ])
              // Seller can counter from BuyerOffer -> SellerCounter
              mkTransition "counterOffer" "BuyerOffer" "SellerCounter" None (RestrictedTo [ "Seller" ])
              // Both can agree or cancel
              mkTransition "agree" "BuyerOffer" "Agreed" None (RestrictedTo [ "Seller" ])
              mkTransition "agree" "SellerCounter" "Agreed" None (RestrictedTo [ "Buyer" ])
              mkTransition "cancel" "BuyerOffer" "Cancelled" None Unrestricted
              mkTransition "cancel" "SellerCounter" "Cancelled" None Unrestricted ] }

    // Chart with actual circular wait (deadlock): each role only advances
    // the state that the OTHER role needs to act from
    let deadlockCircularChart: ExtractedStatechart =
        { RouteTemplate = "/deadlock/{id}"
          StateNames = [ "WaitA"; "WaitB" ]
          InitialStateKey = "WaitA"
          GuardNames = []
          StateMetadata =
            Map.ofList
                [ "WaitA",
                  { AllowedMethods = [ "GET"; "POST" ]
                    IsFinal = false
                    Description = None }
                  "WaitB",
                  { AllowedMethods = [ "GET"; "POST" ]
                    IsFinal = false
                    Description = None } ]
          Roles =
            [ { Name = "RoleA"; Description = None }
              { Name = "RoleB"; Description = None } ]
          Transitions =
            [ mkTransition "view" "WaitA" "WaitA" None Unrestricted
              mkTransition "view" "WaitB" "WaitB" None Unrestricted
              // RoleA can only advance from WaitB -> WaitA
              mkTransition "actionA" "WaitB" "WaitA" None (RestrictedTo [ "RoleA" ])
              // RoleB can only advance from WaitA -> WaitB
              mkTransition "actionB" "WaitA" "WaitB" None (RestrictedTo [ "RoleB" ]) ] }

    testList
        "Enhancement 5: Circular wait detection"
        [ testCase "negotiation chart has no circular wait (both can agree/cancel)"
          <| fun _ ->
              let projections = Projection.projectAll circularChart
              let result = derive circularChart projections
              Expect.isEmpty result.CircularWaits "negotiation has escape paths (agree/cancel)"

          testCase "deadlock circular chart detects circular dependency"
          <| fun _ ->
              let projections = Projection.projectAll deadlockCircularChart
              let result = derive deadlockCircularChart projections

              Expect.isNonEmpty result.CircularWaits "should detect circular wait"

              let cycle = result.CircularWaits |> List.head
              // Should contain both roles in the cycle
              Expect.isTrue (cycle.Length >= 2) "cycle should have at least 2 edges" ]

// ===========================================================================
// Enhancement 6: Conditional request modeling
// "MustSelect means 'must attempt' not 'will succeed.'"
// ===========================================================================

[<Tests>]
let conditionalRequestTests =
    testList
        "Enhancement 6: Conditional request modeling"
        [ testCase "MustSelect annotations carry ConditionalSemantics metadata"
          <| fun _ ->
              let projections = Projection.projectAll orderFulfillmentChart
              let result = derive orderFulfillmentChart projections
              let annotations = annotationsFor "Seller" "Submitted" result
              let confirmOrder = findAnnotation "confirmOrder" annotations
              Expect.isSome confirmOrder "confirmOrder exists"
              Expect.isTrue confirmOrder.Value.IsConditional "MustSelect implies conditional semantics"

          testCase "MayPoll annotations are not conditional"
          <| fun _ ->
              let projections = Projection.projectAll orderFulfillmentChart
              let result = derive orderFulfillmentChart projections
              let annotations = annotationsFor "Buyer" "Submitted" result
              let viewOrder = findAnnotation "viewOrder" annotations
              Expect.isSome viewOrder "viewOrder exists"
              Expect.isFalse viewOrder.Value.IsConditional "MayPoll is not conditional"

          testCase "SessionComplete annotations are not conditional"
          <| fun _ ->
              let projections = Projection.projectAll orderFulfillmentChart
              let result = derive orderFulfillmentChart projections
              let annotations = annotationsFor "Buyer" "Delivered" result

              for ann in annotations do
                  if ann.Obligation = SessionComplete then
                      Expect.isFalse ann.IsConditional "SessionComplete is not conditional" ]

// ===========================================================================
// Enhancement 8: Hierarchy-aware dual derivation
// "Current derivation is flat-only. Composite state self-loops at parent level
//  misclassified as MayPoll when children are transitioning."
// ===========================================================================

[<Tests>]
let hierarchyAwareDualTests =
    // Hierarchical traffic light: Active is a composite state with Red, Yellow, Green children.
    // For flat derivation, we flatten composite transitions: parent-level transitions
    // (checkStatus: Active->Active, turnOff: Active->Off) are distributed to leaf states.
    // This is how the caller would flatten before calling derive.
    let hierarchicalChart: ExtractedStatechart =
        { RouteTemplate = "/traffic/{id}"
          StateNames = [ "Red"; "Yellow"; "Green"; "Off" ]
          InitialStateKey = "Red"
          GuardNames = []
          StateMetadata =
            Map.ofList
                [ "Red",
                  { AllowedMethods = [ "GET"; "POST" ]
                    IsFinal = false
                    Description = None }
                  "Yellow",
                  { AllowedMethods = [ "GET"; "POST" ]
                    IsFinal = false
                    Description = None }
                  "Green",
                  { AllowedMethods = [ "GET"; "POST" ]
                    IsFinal = false
                    Description = None }
                  "Off",
                  { AllowedMethods = [ "GET" ]
                    IsFinal = true
                    Description = None } ]
          Roles =
            [ { Name = "Controller"
                Description = None }
              { Name = "Observer"
                Description = None } ]
          Transitions =
            [ // Flattened parent self-loop: appears as self-loop on each child
              mkTransition "checkStatus" "Red" "Red" None Unrestricted
              mkTransition "checkStatus" "Green" "Green" None Unrestricted
              mkTransition "checkStatus" "Yellow" "Yellow" None Unrestricted
              // Child transitions
              mkTransition "advance" "Red" "Green" None (RestrictedTo [ "Controller" ])
              mkTransition "advance" "Green" "Yellow" None (RestrictedTo [ "Controller" ])
              mkTransition "advance" "Yellow" "Red" None (RestrictedTo [ "Controller" ])
              // Flattened parent-to-Off: each child can turnOff
              mkTransition "turnOff" "Red" "Off" None (RestrictedTo [ "Controller" ])
              mkTransition "turnOff" "Green" "Off" None (RestrictedTo [ "Controller" ])
              mkTransition "turnOff" "Yellow" "Off" None (RestrictedTo [ "Controller" ])
              // Self-loops for observation
              mkTransition "view" "Red" "Red" None Unrestricted
              mkTransition "view" "Green" "Green" None Unrestricted
              mkTransition "view" "Yellow" "Yellow" None Unrestricted ] }

    let hierarchy: StateHierarchy =
        StateHierarchy.build
            { States =
                [ { Id = "Active"
                    Kind = XOR
                    Children = [ "Red"; "Yellow"; "Green" ]
                    InitialChild = Some "Red" } ] }

    testList
        "Enhancement 8: Hierarchy-aware dual derivation"
        [ testCase "flattened parent self-loop in child state classified as MayPoll"
          <| fun _ ->
              let projections = Projection.projectAll hierarchicalChart

              let result =
                  deriveWithHierarchy hierarchicalChart projections Map.empty Map.empty (Some hierarchy)

              // checkStatus is a flattened parent self-loop on Red.
              // With hierarchy awareness, this should be MayPoll (the parent self-loop
              // doesn't advance protocol from the child's perspective).
              let annotations = annotationsFor "Observer" "Red" result
              let checkStatus = findAnnotation "checkStatus" annotations
              Expect.isSome checkStatus "checkStatus annotation exists"
              Expect.equal checkStatus.Value.Obligation MayPoll "flattened parent self-loop is MayPoll"

          testCase "flattened turnOff from child to Off is MustSelect"
          <| fun _ ->
              let projections = Projection.projectAll hierarchicalChart

              let result =
                  deriveWithHierarchy hierarchicalChart projections Map.empty Map.empty (Some hierarchy)

              let annotations = annotationsFor "Controller" "Red" result
              let turnOff = findAnnotation "turnOff" annotations
              Expect.isSome turnOff "turnOff annotation exists"
              Expect.equal turnOff.Value.Obligation MustSelect "turnOff from child to Off is MustSelect"

          testCase "child state transitions are MustSelect for Controller"
          <| fun _ ->
              let projections = Projection.projectAll hierarchicalChart

              let result =
                  deriveWithHierarchy hierarchicalChart projections Map.empty Map.empty (Some hierarchy)

              let annotations = annotationsFor "Controller" "Red" result
              let advance = findAnnotation "advance" annotations
              Expect.isSome advance "advance annotation exists in Red"
              Expect.equal advance.Value.Obligation MustSelect "advance from Red to Green is MustSelect"

          testCase "hierarchy=None falls back to flat derivation"
          <| fun _ ->
              let projections = Projection.projectAll hierarchicalChart

              let flatResult =
                  deriveWithHierarchy hierarchicalChart projections Map.empty Map.empty None

              let normalResult = derive hierarchicalChart projections
              Expect.equal flatResult.Annotations normalResult.Annotations "None hierarchy = flat derivation"
              Expect.equal flatResult.ProtocolSinks normalResult.ProtocolSinks "None hierarchy = flat sinks" ]

// ===========================================================================
// Enhancement 1: Involution — dual(dual(T)) = T [FsCheck property test]
// ===========================================================================

[<Tests>]
let involutionTests =
    testList
        "Enhancement 1: Involution dual(dual(T)) = T"
        [ testCase "TicTacToe: deriveReverse(derive(chart)) reproduces original obligations"
          <| fun _ ->
              let tttChart: ExtractedStatechart =
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

              let projections = Projection.projectAll tttChart
              let firstDerive = derive tttChart projections
              let secondDerive = deriveReverse tttChart firstDerive

              // After derive -> deriveReverse, the obligations should map back
              // to the server's original semantics for each (role, state)
              for role in tttChart.Roles do
                  for state in tttChart.StateNames do
                      let originalAnns = annotationsFor role.Name state firstDerive
                      let roundTripAnns = annotationsFor role.Name state secondDerive

                      for origAnn in originalAnns do
                          let rtAnn = findAnnotation origAnn.Descriptor roundTripAnns

                          match rtAnn with
                          | Some rt ->
                              Expect.equal
                                  rt.Obligation
                                  origAnn.Obligation
                                  $"Involution: {role.Name}/{state}/{origAnn.Descriptor} obligation preserved"

                              Expect.equal
                                  rt.AdvancesProtocol
                                  origAnn.AdvancesProtocol
                                  $"Involution: {role.Name}/{state}/{origAnn.Descriptor} advances preserved"
                          | None ->
                              Expect.isTrue
                                  (origAnn.Obligation = SessionComplete || origAnn.Obligation = MayObserve)
                                  $"Missing annotation in reverse only acceptable for terminal: {role.Name}/{state}/{origAnn.Descriptor}"

          testCase "FsCheck: involution holds for all generated statecharts"
          <| fun _ ->
              // Generate small statecharts and verify derive(derive(chart)) = chart
              let genStateName = Gen.elements [ "S1"; "S2"; "S3"; "S4" ]

              let genRoleName = Gen.elements [ "RoleA"; "RoleB" ]

              let genSmallChart =
                  gen {
                      let! numStates = Gen.choose (2, 4)
                      let stateNames = [ for i in 1..numStates -> $"S{i}" ]
                      let! numFinal = Gen.choose (1, max 1 (numStates / 2))
                      let finalStates = stateNames |> List.rev |> List.take numFinal |> Set.ofList

                      let stateMetadata =
                          stateNames
                          |> List.map (fun s ->
                              s,
                              { AllowedMethods = [ "GET" ]
                                IsFinal = Set.contains s finalStates
                                Description = None })
                          |> Map.ofList

                      let nonFinalStates =
                          stateNames |> List.filter (fun s -> not (Set.contains s finalStates))

                      // Generate transitions between non-final states
                      let! transitions =
                          gen {
                              let! pairs =
                                  Gen.listOfLength
                                      (max 1 (nonFinalStates.Length * 2))
                                      (gen {
                                          let! src = Gen.elements nonFinalStates
                                          let! tgt = Gen.elements stateNames
                                          return (src, tgt)
                                      })

                              return
                                  pairs
                                  |> List.mapi (fun i (src, tgt) -> mkTransition $"event{i}" src tgt None Unrestricted)
                          }

                      // Add self-loop view transitions
                      let viewTransitions =
                          stateNames |> List.map (fun s -> mkTransition "view" s s None Unrestricted)

                      return
                          { RouteTemplate = "/test"
                            StateNames = stateNames
                            InitialStateKey = List.head stateNames
                            GuardNames = []
                            StateMetadata = stateMetadata
                            Roles = [ { Name = "RoleA"; Description = None } ]
                            Transitions = transitions @ viewTransitions }
                  }

              let prop (chart: ExtractedStatechart) =
                  let projections = Projection.projectAll chart
                  let first = derive chart projections
                  let second = deriveReverse chart first

                  // For each annotation in the first derive, the obligation should be preserved
                  first.Annotations
                  |> Map.forall (fun key anns ->
                      let reverseAnns = second.Annotations |> Map.tryFind key |> Option.defaultValue []

                      anns
                      |> List.forall (fun ann ->
                          let reverseAnn =
                              reverseAnns |> List.tryFind (fun a -> a.Descriptor = ann.Descriptor)

                          match reverseAnn with
                          | Some ra -> ra.Obligation = ann.Obligation && ra.AdvancesProtocol = ann.AdvancesProtocol
                          | None -> ann.Obligation = SessionComplete || ann.Obligation = MayObserve))

              let arbChart = Arb.fromGen genSmallChart

              Prop.forAll arbChart prop |> Check.QuickThrowOnFailure

          testCase "OrderFulfillment: involution preserves 4-role obligations"
          <| fun _ ->
              let projections = Projection.projectAll orderFulfillmentChart
              let first = derive orderFulfillmentChart projections
              let second = deriveReverse orderFulfillmentChart first

              for role in orderFulfillmentChart.Roles do
                  for state in orderFulfillmentChart.StateNames do
                      let origAnns = annotationsFor role.Name state first
                      let rtAnns = annotationsFor role.Name state second

                      for origAnn in origAnns do
                          let rtAnn = findAnnotation origAnn.Descriptor rtAnns

                          match rtAnn with
                          | Some rt ->
                              Expect.equal
                                  rt.Obligation
                                  origAnn.Obligation
                                  $"Involution: {role.Name}/{state}/{origAnn.Descriptor}"
                          | None ->
                              Expect.isTrue
                                  (origAnn.Obligation = SessionComplete || origAnn.Obligation = MayObserve)
                                  $"Only terminal obligations can be absent in reverse: {role.Name}/{state}/{origAnn.Descriptor}" ]
