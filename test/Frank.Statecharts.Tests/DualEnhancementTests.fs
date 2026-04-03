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
      Constraint = roleConstraint
      Safety = Unsafe }

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
// Final state classification (C1: MayObserve removed per Harel/Wadler/Fielding)
// Final states with self-loops -> MayPoll (not truly final in the Harel sense)
// Final states without self-loops -> SessionComplete
// ===========================================================================

[<Tests>]
let finalStateClassificationTests =
    let projections = Projection.projectAll orderFulfillmentChart
    let result = derive orderFulfillmentChart projections

    testList
        "Final state classification: self-loops -> MayPoll, no self-loops -> SessionComplete"
        [ testCase "final state with self-loop transitions yields MayPoll"
          <| fun _ ->
              // Delivered has a viewOrder self-loop -- client can still read
              let annotations = annotationsFor "Buyer" "Delivered" result
              let viewOrder = findAnnotation "viewOrder" annotations
              Expect.isSome viewOrder "viewOrder annotation exists in Delivered"
              Expect.equal viewOrder.Value.Obligation MayPoll "self-loop in final state should be MayPoll"

          testCase "MayPoll in final state does not advance protocol"
          <| fun _ ->
              let annotations = annotationsFor "Buyer" "Delivered" result

              for ann in annotations do
                  if ann.Obligation = MayPoll then
                      Expect.isFalse ann.AdvancesProtocol "MayPoll should not advance protocol"

          testCase "all roles get MayPoll for self-loops in Delivered"
          <| fun _ ->
              for role in [ "Buyer"; "Seller"; "Warehouse"; "Shipper" ] do
                  let annotations = annotationsFor role "Delivered" result
                  let viewOrder = findAnnotation "viewOrder" annotations
                  Expect.isSome viewOrder $"{role} has viewOrder in Delivered"

                  Expect.equal viewOrder.Value.Obligation MayPoll $"{role} viewOrder in Delivered should be MayPoll"

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
              // No self-loops in Done, so SessionComplete (no annotations produced for empty transitions)
              Expect.isEmpty annotations "no self-loop in final state means no annotations (SessionComplete)" ]

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
// Enhancement 3: Race condition detection (C3: overlapping descriptors only)
// Multiple MustSelect from SAME role = external choice (valid, not a race).
// Multiple MustSelect from DIFFERENT roles on non-overlapping descriptors = valid interleaving.
// Only flag as race when different roles compete for the same transition/descriptor.
// ===========================================================================

[<Tests>]
let raceConditionTests =
    let projections = Projection.projectAll orderFulfillmentChart
    let result = derive orderFulfillmentChart projections

    testList
        "Enhancement 3: Race condition detection (overlapping descriptors only)"
        [ testCase "Confirmed state: no race (Buyer and Seller have non-overlapping descriptors)"
          <| fun _ ->
              // Buyer has submitPayment, cancelOrder; Seller has cancelBySeller
              // These are non-overlapping descriptors = valid interleaving, NOT a race
              let confirmedRace =
                  result.RaceConditions |> List.tryFind (fun rc -> rc.State = "Confirmed")

              Expect.isNone confirmedRace "Confirmed has no race (non-overlapping descriptors)"

          testCase "no race condition in Submitted (only Seller has MustSelect)"
          <| fun _ ->
              let submittedRace =
                  result.RaceConditions |> List.tryFind (fun rc -> rc.State = "Submitted")

              Expect.isNone submittedRace "Submitted has no race condition (only Seller acts)"

          testCase "no race condition in Paid (only Warehouse has MustSelect)"
          <| fun _ ->
              let paidRace = result.RaceConditions |> List.tryFind (fun rc -> rc.State = "Paid")
              Expect.isNone paidRace "Paid has no race condition (only Warehouse acts)"

          testCase "overlapping descriptors across roles creates race condition"
          <| fun _ ->
              // Chart where two roles both have MustSelect on the same descriptor
              let raceChart: ExtractedStatechart =
                  { RouteTemplate = "/race/{id}"
                    StateNames = [ "Competing"; "Done" ]
                    InitialStateKey = "Competing"
                    GuardNames = []
                    StateMetadata =
                      Map.ofList
                          [ "Competing",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None }
                            "Done",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None } ]
                    Roles =
                      [ { Name = "RoleA"; Description = None }
                        { Name = "RoleB"; Description = None } ]
                    Transitions =
                      [ mkTransition "claim" "Competing" "Done" None (RestrictedTo [ "RoleA" ])
                        mkTransition "claim" "Competing" "Done" None (RestrictedTo [ "RoleB" ])
                        mkTransition "view" "Competing" "Competing" None Unrestricted ] }

              let proj = Projection.projectAll raceChart
              let res = derive raceChart proj

              Expect.isNonEmpty res.RaceConditions "overlapping 'claim' descriptor should create race"

              let competingRace =
                  res.RaceConditions |> List.tryFind (fun rc -> rc.State = "Competing")

              Expect.isSome competingRace "Competing state should have a race condition"
              let rc = competingRace.Value
              Expect.isTrue (Set.contains "RoleA" rc.CompetingRoles) "RoleA competes"
              Expect.isTrue (Set.contains "RoleB" rc.CompetingRoles) "RoleB competes" ]

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

          testCase "alternating chart has circular wait (cross-state: mutual dependency across states)"
          <| fun _ ->
              // This chart has mutual cross-state dependencies:
              // WaitA: RoleA waits, RoleB acts (→WaitB). WaitB: RoleB waits, RoleA acts (→WaitA).
              // Cross-state edges: (RoleA,WaitA)→(RoleB,WaitB) and (RoleB,WaitB)→(RoleA,WaitA).
              // This forms a cycle: genuine circular wait detected via cross-state analysis (#253).
              let projections = Projection.projectAll deadlockCircularChart
              let result = derive deadlockCircularChart projections

              Expect.isNonEmpty result.CircularWaits "cross-state circular wait detected" ]

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
                    InitialChild = Some "Red"
                    CompletionTarget = None } ] }

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
// C2: deriveReverse now properly flips obligations (Wadler session type duality):
//   MustSelect -> MayPoll, MayPoll -> MustSelect, SessionComplete -> SessionComplete
// ===========================================================================

[<Tests>]
let involutionTests =
    testList
        "Enhancement 1: Involution dual(dual(T)) = T"
        [ testCase "TicTacToe: deriveReverse flips obligations, double reverse restores original"
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
              // Single reverse flips obligations
              let reversed = deriveReverse tttChart firstDerive
              // Double reverse restores original
              let doubleReversed = deriveReverse tttChart reversed

              // Verify single reverse actually flips
              for role in tttChart.Roles do
                  for state in tttChart.StateNames do
                      let origAnns = annotationsFor role.Name state firstDerive
                      let revAnns = annotationsFor role.Name state reversed

                      for origAnn in origAnns do
                          let revAnn = findAnnotation origAnn.Descriptor revAnns

                          match revAnn with
                          | Some ra ->
                              match origAnn.Obligation with
                              | MustSelect ->
                                  Expect.equal
                                      ra.Obligation
                                      MayPoll
                                      $"Reverse: {role.Name}/{state}/{origAnn.Descriptor} MustSelect -> MayPoll"
                              | MayPoll ->
                                  Expect.equal
                                      ra.Obligation
                                      MustSelect
                                      $"Reverse: {role.Name}/{state}/{origAnn.Descriptor} MayPoll -> MustSelect"
                              | SessionComplete ->
                                  Expect.equal
                                      ra.Obligation
                                      SessionComplete
                                      $"Reverse: {role.Name}/{state}/{origAnn.Descriptor} SessionComplete symmetric"
                          | None -> ()

              // Verify double reverse restores original
              for role in tttChart.Roles do
                  for state in tttChart.StateNames do
                      let origAnns = annotationsFor role.Name state firstDerive
                      let dblRevAnns = annotationsFor role.Name state doubleReversed

                      for origAnn in origAnns do
                          let dblRevAnn = findAnnotation origAnn.Descriptor dblRevAnns

                          match dblRevAnn with
                          | Some rt ->
                              Expect.equal
                                  rt.Obligation
                                  origAnn.Obligation
                                  $"Involution: {role.Name}/{state}/{origAnn.Descriptor} obligation restored after double reverse"
                          | None -> ()

          testCase "FsCheck: involution (double reverse) holds for all generated statecharts"
          <| fun _ ->
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
                  let reversed = deriveReverse chart first
                  let doubleReversed = deriveReverse chart reversed

                  // Double reverse should restore original obligations
                  first.Annotations
                  |> Map.forall (fun key anns ->
                      let dblRevAnns =
                          doubleReversed.Annotations |> Map.tryFind key |> Option.defaultValue []

                      anns
                      |> List.forall (fun ann ->
                          let dblRevAnn = dblRevAnns |> List.tryFind (fun a -> a.Descriptor = ann.Descriptor)

                          match dblRevAnn with
                          | Some ra -> ra.Obligation = ann.Obligation && ra.AdvancesProtocol = ann.AdvancesProtocol
                          | None -> ann.Obligation = SessionComplete))

              let arbChart = Arb.fromGen genSmallChart

              Prop.forAll arbChart prop |> Check.QuickThrowOnFailure

          testCase "OrderFulfillment: double reverse restores 4-role obligations"
          <| fun _ ->
              let projections = Projection.projectAll orderFulfillmentChart
              let first = derive orderFulfillmentChart projections
              let reversed = deriveReverse orderFulfillmentChart first
              let doubleReversed = deriveReverse orderFulfillmentChart reversed

              for role in orderFulfillmentChart.Roles do
                  for state in orderFulfillmentChart.StateNames do
                      let origAnns = annotationsFor role.Name state first
                      let dblRevAnns = annotationsFor role.Name state doubleReversed

                      for origAnn in origAnns do
                          let dblRevAnn = findAnnotation origAnn.Descriptor dblRevAnns

                          match dblRevAnn with
                          | Some rt ->
                              Expect.equal
                                  rt.Obligation
                                  origAnn.Obligation
                                  $"Involution: {role.Name}/{state}/{origAnn.Descriptor}"
                          | None ->
                              Expect.isTrue
                                  (origAnn.Obligation = SessionComplete)
                                  $"Only SessionComplete can be absent in double reverse: {role.Name}/{state}/{origAnn.Descriptor}" ]

// ===========================================================================
// Issue #243: Circular wait positive detection test
// A deadlocking chart: RoleA in S1 waits for RoleB; RoleB in S2 waits for RoleA.
// detectCircularWaits MUST return a non-empty cycle for this to be considered correct.
// ===========================================================================

[<Tests>]
let circularWaitDetectionTests =
    testList
        "CircularWait: positive detection"
        [ testCase "two-role deadlock: A waits for B in S1, B waits for A in S2 — cycle detected"
          <| fun _ ->
              // Minimal 2-role deadlock: RoleA waits in S1, RoleB waits in S2.
              // Cross-state edges (#253): (RoleA,S1)→(RoleB,S2) and (RoleB,S2)→(RoleA,S1)
              // form a genuine cycle.
              let deadlockChart: ExtractedStatechart =
                  { RouteTemplate = "/deadlock"
                    StateNames = [ "S1"; "S2" ]
                    InitialStateKey = "S1"
                    GuardNames = []
                    StateMetadata =
                      Map.ofList
                          [ "S1",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None }
                            "S2",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None } ]
                    Roles =
                      [ { Name = "RoleA"; Description = None }
                        { Name = "RoleB"; Description = None } ]
                    Transitions =
                      [ // S1: RoleB acts (MustSelect: S1->S2), RoleA only observes (MayPoll: S1->S1)
                        mkTransition "actB" "S1" "S2" None (RestrictedTo [ "RoleB" ])
                        mkTransition "getA" "S1" "S1" None (RestrictedTo [ "RoleA" ])
                        // S2: RoleA acts (MustSelect: S2->S1), RoleB only observes (MayPoll: S2->S2)
                        mkTransition "actA" "S2" "S1" None (RestrictedTo [ "RoleA" ])
                        mkTransition "getB" "S2" "S2" None (RestrictedTo [ "RoleB" ]) ] }

              let projections = Projection.projectAll deadlockChart
              let result = derive deadlockChart projections

              // Verify the dependency edges ARE generated (waiting roles are identified):
              let s1Anns = result.Annotations
              let roleAS1 = s1Anns |> Map.tryFind ("RoleA", "S1") |> Option.defaultValue []
              let roleBS2 = s1Anns |> Map.tryFind ("RoleB", "S2") |> Option.defaultValue []

              // RoleA in S1 should be MayPoll only (waiting)
              Expect.isTrue
                  (roleAS1
                   |> List.forall (fun a -> a.Obligation = MayPoll || a.Obligation = SessionComplete))
                  "RoleA in S1 is MayPoll-only (waiting)"

              // RoleB in S2 should be MayPoll only (waiting)
              Expect.isTrue
                  (roleBS2
                   |> List.forall (fun a -> a.Obligation = MayPoll || a.Obligation = SessionComplete))
                  "RoleB in S2 is MayPoll-only (waiting)"

              // Cross-state edges (#253): (RoleA,S1)→(RoleB,S2) and (RoleB,S2)→(RoleA,S1)
              // form a cycle. This is a genuine circular wait: A depends on B in S1, B depends
              // on A in S2, and neither can independently break out of the cycle.
              Expect.isNonEmpty result.CircularWaits "Two-state two-role deadlock: cross-state cycle detected"

              let allNodes = result.CircularWaits |> List.collect id |> Set.ofList
              Expect.isTrue (Set.contains ("RoleA", "S1") allNodes) "cycle contains (RoleA, S1)"
              Expect.isTrue (Set.contains ("RoleB", "S2") allNodes) "cycle contains (RoleB, S2)"

          testCase "self-referential wait in same state: both roles waiting with no actor — no cycle (protocol sink)"
          <| fun _ ->
              // If both roles are MayPoll in a state with no MustSelect actor, no edges are generated
              // (buildRoleDependencyGraph only creates edges when mustSelectRoles is non-empty).
              let sinkChart: ExtractedStatechart =
                  { RouteTemplate = "/sink"
                    StateNames = [ "Stuck" ]
                    InitialStateKey = "Stuck"
                    GuardNames = []
                    StateMetadata =
                      Map.ofList
                          [ "Stuck",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = false
                              Description = None } ]
                    Roles =
                      [ { Name = "RoleA"; Description = None }
                        { Name = "RoleB"; Description = None } ]
                    Transitions =
                      [ mkTransition "peek" "Stuck" "Stuck" None (RestrictedTo [ "RoleA" ])
                        mkTransition "watch" "Stuck" "Stuck" None (RestrictedTo [ "RoleB" ]) ] }

              let projections = Projection.projectAll sinkChart
              let result = derive sinkChart projections

              // Protocol sink: no advancing transitions, so no dependency edges, so no cycles.
              Expect.equal result.CircularWaits [] "Protocol sink with no actor produces no cycle"
              Expect.isNonEmpty result.ProtocolSinks "Stuck state detected as protocol sink"

          testCase "DFS visited fix: race condition (both actors, no waiter) produces no circular wait"
          <| fun _ ->
              // Build a chart where both roles share the same "act" transition (overlapping MustSelect).
              // Both roles are actors (MustSelect), neither is a waiter (MayPoll-only) — so no
              // dependency edges are generated by buildRoleDependencyGraph, and no cycles exist.
              // This confirms the DFS produces no spurious cycles when both nodes are actors.
              let raceChart: ExtractedStatechart =
                  { RouteTemplate = "/race"
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
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None } ]
                    Roles =
                      [ { Name = "RoleA"; Description = None }
                        { Name = "RoleB"; Description = None } ]
                    // Both roles can trigger "act" — Unrestricted means both get it in their projection.
                    // Both have MustSelect = race condition, neither is a waiter.
                    Transitions = [ mkTransition "act" "Active" "Done" None Unrestricted ] }

              let projections = Projection.projectAll raceChart
              let result = derive raceChart projections

              // Both roles have MustSelect on "act" in Active = race condition, no waiter, no circular wait.
              Expect.equal result.CircularWaits [] "Race condition (both actors) produces no circular wait"
              Expect.isNonEmpty result.RaceConditions "Race condition detected (both roles have overlapping MustSelect)" ]

// ===========================================================================
// Issue #243: Extended involution tests — DualOf, CutPoint, ProtocolSinks,
// RaceConditions, CircularWaits must all be re-derived in deriveReverse,
// not passed through unchanged.
// ===========================================================================

[<Tests>]
let extendedInvolutionTests =
    testList
        "Extended involution: DualOf, ProtocolSinks, RaceConditions, CircularWaits re-derived after reverse"
        [ testCase "DualOf involution: # prefix toggled twice restores original"
          <| fun _ ->
              // DualOf toggle: "confirmOrder" -> "#confirmOrder" -> "confirmOrder"
              //                "#cancelOrder" -> "cancelOrder" -> "#cancelOrder"
              let result: DeriveResult =
                  { Annotations =
                      Map.ofList
                          [ ("RoleA", "S1"),
                            [ { Descriptor = "act"
                                Obligation = MustSelect
                                AdvancesProtocol = true
                                DualOf = Some "confirmOrder"
                                CutPoint = None
                                ChoiceGroupId = Some 0
                                IsConditional = true } ]
                            ("RoleB", "S1"),
                            [ { Descriptor = "ack"
                                Obligation = MustSelect
                                AdvancesProtocol = true
                                DualOf = Some "#cancelOrder"
                                CutPoint = None
                                ChoiceGroupId = Some 1
                                IsConditional = true } ] ]
                    ProtocolSinks = []
                    RaceConditions = []
                    CircularWaits = []
                    Warnings = [] }

              let simpleChart: ExtractedStatechart =
                  { RouteTemplate = "/test"
                    StateNames = [ "S1" ]
                    InitialStateKey = "S1"
                    GuardNames = []
                    StateMetadata =
                      Map.ofList
                          [ "S1",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = false
                              Description = None } ]
                    Roles =
                      [ { Name = "RoleA"; Description = None }
                        { Name = "RoleB"; Description = None } ]
                    Transitions = [] }

              let rev = deriveReverse simpleChart result
              let dblRev = deriveReverse simpleChart rev

              // DualOf for "act" in RoleA/S1: original "confirmOrder" -> rev "#confirmOrder" -> dblRev "confirmOrder"
              let origActDualOf =
                  result.Annotations
                  |> Map.find ("RoleA", "S1")
                  |> List.find (fun a -> a.Descriptor = "act")
                  |> fun a -> a.DualOf

              let dblRevActDualOf =
                  dblRev.Annotations
                  |> Map.find ("RoleA", "S1")
                  |> List.find (fun a -> a.Descriptor = "act")
                  |> fun a -> a.DualOf

              Expect.equal
                  dblRevActDualOf
                  origActDualOf
                  "DualOf involution: double-reverse restores original DualOf (no # prefix)"

              // DualOf for "ack" in RoleB/S1: original "#cancelOrder" -> rev "cancelOrder" -> dblRev "#cancelOrder"
              let origAckDualOf =
                  result.Annotations
                  |> Map.find ("RoleB", "S1")
                  |> List.find (fun a -> a.Descriptor = "ack")
                  |> fun a -> a.DualOf

              let dblRevAckDualOf =
                  dblRev.Annotations
                  |> Map.find ("RoleB", "S1")
                  |> List.find (fun a -> a.Descriptor = "ack")
                  |> fun a -> a.DualOf

              Expect.equal
                  dblRevAckDualOf
                  origAckDualOf
                  "DualOf involution: double-reverse restores original DualOf (# prefix)"

          testCase "FsCheck involution property also checks DualOf, AdvancesProtocol, and IsConditional"
          <| fun _ ->
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
                  let reversed = deriveReverse chart first
                  let doubleReversed = deriveReverse chart reversed

                  first.Annotations
                  |> Map.forall (fun key anns ->
                      let dblRevAnns =
                          doubleReversed.Annotations |> Map.tryFind key |> Option.defaultValue []

                      anns
                      |> List.forall (fun ann ->
                          let dblRevAnn = dblRevAnns |> List.tryFind (fun a -> a.Descriptor = ann.Descriptor)

                          match dblRevAnn with
                          | Some ra ->
                              // Obligation restored
                              ra.Obligation = ann.Obligation
                              // AdvancesProtocol unchanged by obligation flip
                              && ra.AdvancesProtocol = ann.AdvancesProtocol
                              // IsConditional consistent with obligation
                              && ra.IsConditional = (ann.Obligation = MustSelect)
                              // DualOf toggles twice = identity
                              && ra.DualOf = ann.DualOf
                          | None -> ann.Obligation = SessionComplete))

              let arbChart = Arb.fromGen genSmallChart
              Prop.forAll arbChart prop |> Check.QuickThrowOnFailure

          testCase "deriveReverse re-derives RaceConditions from reversed annotations, not pass-through"
          <| fun _ ->
              // Build a chart where forward derivation has a race condition (two roles with MustSelect
              // on overlapping descriptors in the same state). After obligation reversal, those
              // MustSelect annotations become MayPoll, removing the race condition.
              // If deriveReverse passes RaceConditions through unchanged (bug), reversed.RaceConditions
              // will still contain the original race — the test fails.
              // If RaceConditions is re-derived from reversed annotations (correct), it should be empty.
              let chart: ExtractedStatechart =
                  { RouteTemplate = "/race-test"
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
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None } ]
                    Roles =
                      [ { Name = "RoleA"; Description = None }
                        { Name = "RoleB"; Description = None } ]
                    // Both roles can trigger the same "act" event (overlapping MustSelect = race condition).
                    Transitions = [ mkTransition "act" "Active" "Done" None Unrestricted ] }

              let projections = Projection.projectAll chart
              let forward = derive chart projections

              // Forward: both RoleA and RoleB have MustSelect on "act" in Active — race condition.
              Expect.isNonEmpty forward.RaceConditions "Forward: race condition detected (both roles can 'act')"

              let reversed = deriveReverse chart forward

              // After reversal: "act" (was MustSelect) becomes MayPoll for both roles.
              // Race condition requires MustSelect; after flip to MayPoll, no race exists.
              // If RaceConditions is passed through unchanged (bug), reversed.RaceConditions is non-empty.
              // If RaceConditions is re-derived (correct), reversed.RaceConditions should be empty.
              Expect.isEmpty
                  reversed.RaceConditions
                  "Reversed: no race condition after obligation flip (MustSelect->MayPoll removes race)"

          testCase "deriveReverse re-derives CircularWaits from reversed annotations, not pass-through"
          <| fun _ ->
              // Build a chart where circular wait detection results differ between forward and reversed.
              // The key insight: if deriveReverse passes CircularWaits through unchanged (bug),
              // the double-reversed result's CircularWaits will differ from a fresh derivation.
              // We verify by comparing deriveReverse(chart, forward).CircularWaits against
              // what fresh derivation of the reversed annotations would produce.
              let chart: ExtractedStatechart =
                  { RouteTemplate = "/circ-test"
                    StateNames = [ "S1"; "S2" ]
                    InitialStateKey = "S1"
                    GuardNames = []
                    StateMetadata =
                      Map.ofList
                          [ "S1",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None }
                            "S2",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None } ]
                    Roles =
                      [ { Name = "RoleA"; Description = None }
                        { Name = "RoleB"; Description = None } ]
                    Transitions =
                      [ mkTransition "actB" "S1" "S2" None (RestrictedTo [ "RoleB" ])
                        mkTransition "getA" "S1" "S1" None (RestrictedTo [ "RoleA" ])
                        mkTransition "actA" "S2" "S1" None (RestrictedTo [ "RoleA" ])
                        mkTransition "getB" "S2" "S2" None (RestrictedTo [ "RoleB" ]) ] }

              let projections = Projection.projectAll chart
              let forward = derive chart projections
              let reversed = deriveReverse chart forward

              // Forward has a cross-state circular wait (#253): (RoleA,S1)→(RoleB,S2)→(RoleA,S1).
              // reversed.CircularWaits must be re-derived from reversed annotations, not passed through.
              // After reversal: actB/actA (advancing) become MayPoll, getA/getB (self-loops) become MustSelect.
              // In each state: the role with MayPoll also has AdvancesProtocol=true, so it's NOT classified
              // as a waiter (waiters need hasOnlyMayPoll AND NOT hasAdvancing). No waiters → no edges → [].
              // This proves re-derivation: forward is non-empty, reversed is empty.
              Expect.isNonEmpty forward.CircularWaits "forward has cross-state circular wait"

              Expect.equal
                  reversed.CircularWaits
                  []
                  "Reversed chart: CircularWaits is re-derived (forward non-empty, reversed empty)" ]

// ===========================================================================
// Issue #243: ChoiceGroupId — assignChoiceGroups correctness tests
// ===========================================================================

[<Tests>]
let choiceGroupIdTests =
    testList
        "ChoiceGroupId: assignChoiceGroups semantics"
        [ testCase "single MustSelect in (role, state) gets a ChoiceGroupId"
          <| fun _ ->
              let chart: ExtractedStatechart =
                  { RouteTemplate = "/choice"
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
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None } ]
                    Roles = [ { Name = "RoleA"; Description = None } ]
                    Transitions = [ mkTransition "act" "Active" "Done" None Unrestricted ] }

              let projections = Projection.projectAll chart
              let result = derive chart projections
              let anns = annotationsFor "RoleA" "Active" result
              let actAnn = findAnnotation "act" anns

              Expect.isSome actAnn "act annotation exists"
              Expect.equal actAnn.Value.Obligation MustSelect "act is MustSelect"
              Expect.isSome actAnn.Value.ChoiceGroupId "MustSelect annotation has a ChoiceGroupId"

          testCase "multiple MustSelect in same (role, state) share the same ChoiceGroupId"
          <| fun _ ->
              let chart: ExtractedStatechart =
                  { RouteTemplate = "/multichoice"
                    StateNames = [ "Active"; "DoneA"; "DoneB" ]
                    InitialStateKey = "Active"
                    GuardNames = []
                    StateMetadata =
                      Map.ofList
                          [ "Active",
                            { AllowedMethods = [ "GET"; "POST"; "PUT" ]
                              IsFinal = false
                              Description = None }
                            "DoneA",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None }
                            "DoneB",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None } ]
                    Roles = [ { Name = "RoleA"; Description = None } ]
                    Transitions =
                      [ mkTransition "chooseA" "Active" "DoneA" None Unrestricted
                        mkTransition "chooseB" "Active" "DoneB" None Unrestricted ] }

              let projections = Projection.projectAll chart
              let result = derive chart projections
              let anns = annotationsFor "RoleA" "Active" result
              let chooseAAnn = findAnnotation "chooseA" anns
              let chooseBAnn = findAnnotation "chooseB" anns

              Expect.isSome chooseAAnn "chooseA annotation exists"
              Expect.isSome chooseBAnn "chooseB annotation exists"
              Expect.equal chooseAAnn.Value.Obligation MustSelect "chooseA is MustSelect"
              Expect.equal chooseBAnn.Value.Obligation MustSelect "chooseB is MustSelect"
              Expect.isSome chooseAAnn.Value.ChoiceGroupId "chooseA has ChoiceGroupId"
              Expect.isSome chooseBAnn.Value.ChoiceGroupId "chooseB has ChoiceGroupId"

              Expect.equal
                  chooseAAnn.Value.ChoiceGroupId
                  chooseBAnn.Value.ChoiceGroupId
                  "chooseA and chooseB share the same ChoiceGroupId (external choice group)"

          testCase "MayPoll annotation has no ChoiceGroupId"
          <| fun _ ->
              let chart: ExtractedStatechart =
                  { RouteTemplate = "/poll"
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
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None } ]
                    Roles = [ { Name = "RoleA"; Description = None } ]
                    Transitions =
                      [ mkTransition "act" "Active" "Done" None Unrestricted
                        mkTransition "view" "Active" "Active" None Unrestricted ] }

              let projections = Projection.projectAll chart
              let result = derive chart projections
              let anns = annotationsFor "RoleA" "Active" result
              let viewAnn = findAnnotation "view" anns

              Expect.isSome viewAnn "view annotation exists"
              Expect.equal viewAnn.Value.Obligation MayPoll "view is MayPoll"
              Expect.isNone viewAnn.Value.ChoiceGroupId "MayPoll annotation has no ChoiceGroupId"

          testCase "different (role, state) pairs get different ChoiceGroupIds"
          <| fun _ ->
              let chart: ExtractedStatechart =
                  { RouteTemplate = "/multistate"
                    StateNames = [ "S1"; "S2"; "Done" ]
                    InitialStateKey = "S1"
                    GuardNames = []
                    StateMetadata =
                      Map.ofList
                          [ "S1",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None }
                            "S2",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None }
                            "Done",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None } ]
                    Roles = [ { Name = "RoleA"; Description = None } ]
                    Transitions =
                      [ mkTransition "toS2" "S1" "S2" None Unrestricted // makes S2 reachable
                        mkTransition "act1" "S1" "Done" None Unrestricted
                        mkTransition "act2" "S2" "Done" None Unrestricted ] }

              let projections = Projection.projectAll chart
              let result = derive chart projections
              let annsS1 = annotationsFor "RoleA" "S1" result
              let annsS2 = annotationsFor "RoleA" "S2" result
              let act1Ann = findAnnotation "act1" annsS1
              let act2Ann = findAnnotation "act2" annsS2

              Expect.isSome act1Ann "act1 annotation exists"
              Expect.isSome act2Ann "act2 annotation exists"
              Expect.isSome act1Ann.Value.ChoiceGroupId "act1 has ChoiceGroupId"
              Expect.isSome act2Ann.Value.ChoiceGroupId "act2 has ChoiceGroupId"

              Expect.notEqual
                  act1Ann.Value.ChoiceGroupId
                  act2Ann.Value.ChoiceGroupId
                  "Different (role, state) pairs get different ChoiceGroupIds" ]

// ===========================================================================
// Issue #253: Cross-state circular wait detection
// The current buildRoleDependencyGraph only creates intra-state edges, making
// cycle detection structurally impossible. These acceptance tests require
// cross-state edges to pass.
// ===========================================================================

[<Tests>]
let crossStateCircularWaitTests =
    testList
        "Issue #253: Cross-state circular wait detection"
        [
          // ---------------------------------------------------------------
          // Acceptance test 1: Positive detection — genuine circular wait
          // S1: RoleA = MayPoll (waiter), RoleB = MustSelect (actor, S1→S2)
          // S2: RoleB = MayPoll (waiter), RoleA = MustSelect (actor, S2→S1)
          // Cycle: (A,S1) → (B,S2) → (A,S1)
          // ---------------------------------------------------------------
          testCase "genuine circular wait: A waits in S1, B waits in S2 — cycle detected"
          <| fun _ ->
              let chart: ExtractedStatechart =
                  { RouteTemplate = "/circular"
                    StateNames = [ "S1"; "S2" ]
                    InitialStateKey = "S1"
                    GuardNames = []
                    StateMetadata =
                      Map.ofList
                          [ "S1",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None }
                            "S2",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None } ]
                    Roles =
                      [ { Name = "RoleA"; Description = None }
                        { Name = "RoleB"; Description = None } ]
                    Transitions =
                      [ // S1: RoleA observes (self-loop), RoleB advances to S2
                        mkTransition "observe" "S1" "S1" None (RestrictedTo [ "RoleA" ])
                        mkTransition "advance" "S1" "S2" None (RestrictedTo [ "RoleB" ])
                        // S2: RoleB observes (self-loop), RoleA advances to S1
                        mkTransition "observe" "S2" "S2" None (RestrictedTo [ "RoleB" ])
                        mkTransition "advance" "S2" "S1" None (RestrictedTo [ "RoleA" ]) ] }

              let projections = Projection.projectAll chart
              let result = derive chart projections

              // Cross-state edges: (A,S1)→(B,S2) and (B,S2)→(A,S1) form a cycle.
              // This is the first positive detection test in the codebase.
              Expect.isNonEmpty result.CircularWaits "genuine circular wait must be detected"

              // The cycle must contain both (RoleA, S1) and (RoleB, S2).
              let allNodesInCycles =
                  result.CircularWaits |> List.collect id |> Set.ofList

              Expect.isTrue
                  (Set.contains ("RoleA", "S1") allNodesInCycles)
                  "cycle contains (RoleA, S1)"

              Expect.isTrue
                  (Set.contains ("RoleB", "S2") allNodesInCycles)
                  "cycle contains (RoleB, S2)"

          // ---------------------------------------------------------------
          // Acceptance test 2: No false positives — healthy chain
          // S1→S2→S3 (terminal). Cross-state edges form a chain, not a cycle.
          // ---------------------------------------------------------------
          testCase "healthy chain: S1→S2→S3 with no cycle — empty"
          <| fun _ ->
              let chart: ExtractedStatechart =
                  { RouteTemplate = "/chain"
                    StateNames = [ "S1"; "S2"; "S3" ]
                    InitialStateKey = "S1"
                    GuardNames = []
                    StateMetadata =
                      Map.ofList
                          [ "S1",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None }
                            "S2",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None }
                            "S3",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None } ]
                    Roles =
                      [ { Name = "RoleA"; Description = None }
                        { Name = "RoleB"; Description = None } ]
                    Transitions =
                      [ // S1: RoleA acts (MustSelect, S1→S2), RoleB observes
                        mkTransition "act" "S1" "S2" None (RestrictedTo [ "RoleA" ])
                        mkTransition "watch" "S1" "S1" None (RestrictedTo [ "RoleB" ])
                        // S2: RoleB acts (MustSelect, S2→S3), RoleA observes
                        mkTransition "act" "S2" "S3" None (RestrictedTo [ "RoleB" ])
                        mkTransition "watch" "S2" "S2" None (RestrictedTo [ "RoleA" ])
                        // S3: terminal, both observe
                        mkTransition "view" "S3" "S3" None Unrestricted ] }

              let projections = Projection.projectAll chart
              let result = derive chart projections

              // Chain: (B,S1)→(A,S2) and (A,S2)→(B,S3). No path back to (B,S1).
              Expect.isEmpty result.CircularWaits "chain dependencies must not produce false positive"

          // ---------------------------------------------------------------
          // Acceptance test 3: Order fulfillment — document circular wait status
          // The order fulfillment pipeline is a linear chain (Seller→Buyer→
          // Warehouse→Seller) across different states with no cycle.
          // ---------------------------------------------------------------
          testCase "order fulfillment: linear pipeline has no circular wait"
          <| fun _ ->
              let orderChart: ExtractedStatechart =
                  { RouteTemplate = "/orders/{orderId}"
                    StateNames =
                      [ "Submitted"; "Confirmed"; "Paid"; "Picking"
                        "Shipped"; "Completed"; "Cancelled" ]
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
                        { Name = "Warehouse"; Description = None }
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

              let projections = Projection.projectAll orderChart
              let result = derive orderChart projections

              // The order fulfillment pipeline has no circular waits:
              // Seller acts in Submitted → Buyer acts in Confirmed → Warehouse acts in Paid/Picking
              // → Seller acts in Shipped → terminal. This is a DAG across (role, state) pairs.
              Expect.isEmpty
                  result.CircularWaits
                  "order fulfillment is a linear pipeline with no circular dependency"

          // ---------------------------------------------------------------
          // Acceptance test 4: DeriveResult.CircularWaits reflects cross-state analysis
          // The public API (derive, deriveWithHierarchy) must surface the
          // cross-state circular wait analysis in the result.
          // ---------------------------------------------------------------
          testCase "public API surfaces cross-state circular waits in DeriveResult"
          <| fun _ ->
              let chart: ExtractedStatechart =
                  { RouteTemplate = "/api-test"
                    StateNames = [ "S1"; "S2" ]
                    InitialStateKey = "S1"
                    GuardNames = []
                    StateMetadata =
                      Map.ofList
                          [ "S1",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None }
                            "S2",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None } ]
                    Roles =
                      [ { Name = "RoleA"; Description = None }
                        { Name = "RoleB"; Description = None } ]
                    Transitions =
                      [ mkTransition "poll" "S1" "S1" None (RestrictedTo [ "RoleA" ])
                        mkTransition "act" "S1" "S2" None (RestrictedTo [ "RoleB" ])
                        mkTransition "poll" "S2" "S2" None (RestrictedTo [ "RoleB" ])
                        mkTransition "act" "S2" "S1" None (RestrictedTo [ "RoleA" ]) ] }

              let projections = Projection.projectAll chart

              // Test via derive (simple API)
              let simpleResult = derive chart projections
              Expect.isNonEmpty simpleResult.CircularWaits "derive must detect cross-state circular wait"

              // Test via deriveWithHierarchy (full API)
              let hierarchyResult = deriveWithHierarchy chart projections Map.empty Map.empty None
              Expect.isNonEmpty hierarchyResult.CircularWaits "deriveWithHierarchy must detect cross-state circular wait"

              // Both must agree
              Expect.equal
                  simpleResult.CircularWaits
                  hierarchyResult.CircularWaits
                  "derive and deriveWithHierarchy produce identical CircularWaits"

          // ---------------------------------------------------------------
          // 3-role cycle: A waits in S1, B waits in S2, C waits in S3
          // Cycle: (A,S1) → (B,S2) → (C,S3) → (A,S1)
          // ---------------------------------------------------------------
          testCase "three-role cycle: A→B→C→A across three states"
          <| fun _ ->
              let chart: ExtractedStatechart =
                  { RouteTemplate = "/three-role"
                    StateNames = [ "S1"; "S2"; "S3" ]
                    InitialStateKey = "S1"
                    GuardNames = []
                    StateMetadata =
                      Map.ofList
                          [ "S1",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None }
                            "S2",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None }
                            "S3",
                            { AllowedMethods = [ "GET"; "POST" ]
                              IsFinal = false
                              Description = None } ]
                    Roles =
                      [ { Name = "RoleA"; Description = None }
                        { Name = "RoleB"; Description = None }
                        { Name = "RoleC"; Description = None } ]
                    Transitions =
                      [ // S1: A observes, B advances to S2, C observes
                        mkTransition "poll" "S1" "S1" None (RestrictedTo [ "RoleA" ])
                        mkTransition "poll" "S1" "S1" None (RestrictedTo [ "RoleC" ])
                        mkTransition "advance" "S1" "S2" None (RestrictedTo [ "RoleB" ])
                        // S2: B observes, C advances to S3, A observes
                        mkTransition "poll" "S2" "S2" None (RestrictedTo [ "RoleB" ])
                        mkTransition "poll" "S2" "S2" None (RestrictedTo [ "RoleA" ])
                        mkTransition "advance" "S2" "S3" None (RestrictedTo [ "RoleC" ])
                        // S3: C observes, A advances to S1, B observes
                        mkTransition "poll" "S3" "S3" None (RestrictedTo [ "RoleC" ])
                        mkTransition "poll" "S3" "S3" None (RestrictedTo [ "RoleB" ])
                        mkTransition "advance" "S3" "S1" None (RestrictedTo [ "RoleA" ]) ] }

              let projections = Projection.projectAll chart
              let result = derive chart projections

              // Cross-state edges: (A,S1)→(B,S2), (B,S2)→(C,S3), (C,S3)→(A,S1) form a 3-node cycle.
              Expect.isNonEmpty result.CircularWaits "three-role circular dependency must be detected"

              let allNodes = result.CircularWaits |> List.collect id |> Set.ofList
              Expect.isTrue (Set.contains ("RoleA", "S1") allNodes) "cycle contains (RoleA, S1)"
              Expect.isTrue (Set.contains ("RoleB", "S2") allNodes) "cycle contains (RoleB, S2)"
              Expect.isTrue (Set.contains ("RoleC", "S3") allNodes) "cycle contains (RoleC, S3)" ]

// ===========================================================================
// Issue #253: FsCheck property tests for circular wait algebraic claims
// ===========================================================================

[<Tests>]
let circularWaitPropertyTests =
    // Generator for multi-role statecharts with RestrictedTo constraints.
    // Each non-final state has one actor role (advancing transition) and all other
    // roles observe (self-loop). This ensures well-formed waiter/actor classification.
    let genMultiRoleChart =
        gen {
            let! numStates = Gen.choose (2, 5)
            let stateNames = [ for i in 1..numStates -> $"S{i}" ]
            let! numRoles = Gen.choose (2, 4)
            let roleNames = [ for i in 1..numRoles -> $"R{i}" ]

            let! numFinal = Gen.choose (0, max 0 (numStates / 3))
            let finalStates = stateNames |> List.rev |> List.take numFinal |> Set.ofList

            let stateMetadata =
                stateNames
                |> List.map (fun s ->
                    s,
                    { AllowedMethods = [ "GET"; "POST" ]
                      IsFinal = Set.contains s finalStates
                      Description = None })
                |> Map.ofList

            let nonFinalStates =
                stateNames |> List.filter (fun s -> not (Set.contains s finalStates))

            // For each non-final state, pick one role as the actor with an advancing transition
            let! actorRoles = Gen.listOfLength nonFinalStates.Length (Gen.elements roleNames)
            let! targets = Gen.listOfLength nonFinalStates.Length (Gen.elements stateNames)

            let advancingTransitions =
                List.zip3 nonFinalStates actorRoles targets
                |> List.map (fun (src, actor, tgt) ->
                    mkTransition "advance" src tgt None (RestrictedTo [ actor ]))

            // All roles get self-loop observation in every state
            let selfLoops =
                stateNames
                |> List.collect (fun s ->
                    roleNames |> List.map (fun r -> mkTransition "poll" s s None (RestrictedTo [ r ])))

            return
                { RouteTemplate = "/prop"
                  StateNames = stateNames
                  InitialStateKey = List.head stateNames
                  GuardNames = []
                  StateMetadata = stateMetadata
                  Roles = roleNames |> List.map (fun r -> { Name = r; Description = None })
                  Transitions = advancingTransitions @ selfLoops }
        }

    // Generator for DAG charts: states ordered S1→S2→...→Sn with no backward edges.
    let genDagChart =
        gen {
            let! numStates = Gen.choose (2, 5)
            let stateNames = [ for i in 1..numStates -> $"S{i}" ]
            let! numRoles = Gen.choose (2, 3)
            let roleNames = [ for i in 1..numRoles -> $"R{i}" ]

            let stateMetadata =
                stateNames
                |> List.map (fun s ->
                    s,
                    { AllowedMethods = [ "GET"; "POST" ]
                      IsFinal = (s = List.last stateNames)
                      Description = None })
                |> Map.ofList

            let nonFinalStates = stateNames |> List.take (numStates - 1)

            // Each non-final state transitions FORWARD only (to a later state)
            let! dagActorRoles = Gen.listOfLength nonFinalStates.Length (Gen.elements roleNames)

            // For forward-only edges: each state picks from its later states using an index offset
            let! dagTargetOffsets = Gen.listOfLength nonFinalStates.Length (Gen.choose (1, numStates))

            let advancingTransitions =
                nonFinalStates
                |> List.mapi (fun i src ->
                    let srcIdx = stateNames |> List.findIndex (fun s -> s = src)
                    let laterStates = stateNames |> List.skip (srcIdx + 1)
                    let tgt = laterStates[dagTargetOffsets[i] % laterStates.Length]
                    mkTransition "advance" src tgt None (RestrictedTo [ dagActorRoles[i] ]))

            let selfLoops =
                stateNames
                |> List.collect (fun s ->
                    roleNames |> List.map (fun r -> mkTransition "poll" s s None (RestrictedTo [ r ])))

            return
                { RouteTemplate = "/dag"
                  StateNames = stateNames
                  InitialStateKey = List.head stateNames
                  GuardNames = []
                  StateMetadata = stateMetadata
                  Roles = roleNames |> List.map (fun r -> { Name = r; Description = None })
                  Transitions = advancingTransitions @ selfLoops }
        }

    testList
        "FsCheck: circular wait algebraic properties"
        [
          // Property 1: Every reported cycle has a dependency edge between each
          // consecutive pair. We reconstruct the dependency graph independently from
          // annotations + projections and verify edge connectivity.
          testCase "FsCheck: reported cycles have valid dependency edges"
          <| fun _ ->
              let prop (chart: ExtractedStatechart) =
                  let projections = Projection.projectAll chart
                  let result = derive chart projections

                  // Reconstruct the dependency edge set from annotations + projections
                  let byState =
                      result.Annotations
                      |> Map.toList
                      |> List.groupBy (fun ((_, state), _) -> state)

                  let edges =
                      byState
                      |> List.collect (fun (state, entries) ->
                          let roleInfo =
                              entries
                              |> List.map (fun ((role, _), anns) ->
                                  let hasMustSelect =
                                      anns |> List.exists (fun a -> a.Obligation = MustSelect)

                                  let isWaiter =
                                      anns
                                      |> List.forall (fun a ->
                                          a.Obligation = MayPoll || a.Obligation = SessionComplete)
                                      && not (anns |> List.exists (fun a -> a.AdvancesProtocol))

                                  role, hasMustSelect, isWaiter)

                          let actors =
                              roleInfo |> List.filter (fun (_, ms, _) -> ms) |> List.map (fun (r, _, _) -> r)

                          let waiters =
                              roleInfo |> List.filter (fun (_, _, w) -> w) |> List.map (fun (r, _, _) -> r)

                          // Intra-state edges
                          let intra =
                              waiters
                              |> List.collect (fun w ->
                                  actors |> List.map (fun a -> ((w, state), (a, state))))

                          // Cross-state edges
                          let cross =
                              waiters
                              |> List.collect (fun w ->
                                  actors
                                  |> List.collect (fun a ->
                                      let targets =
                                          projections
                                          |> Map.tryFind a
                                          |> Option.map (fun p ->
                                              p.Transitions
                                              |> List.filter (fun t ->
                                                  t.Source = state && t.Source <> t.Target)
                                              |> List.map (fun t -> t.Target)
                                              |> List.distinct)
                                          |> Option.defaultValue []

                                      targets |> List.map (fun tgt -> ((w, state), (a, tgt)))))

                          intra @ cross)
                      |> Set.ofList

                  // Verify every cycle has >= 2 nodes and every consecutive edge exists
                  result.CircularWaits
                  |> List.forall (fun cycle ->
                      cycle.Length >= 2
                      && (let pairs =
                              cycle
                              |> List.pairwise
                              |> List.append [ (List.last cycle, List.head cycle) ]

                          pairs |> List.forall (fun edge -> Set.contains edge edges)))

              let arbChart = Arb.fromGen genMultiRoleChart
              Prop.forAll arbChart prop |> Check.QuickThrowOnFailure

          // Property 2: DAG charts (forward-only transitions) never produce circular waits.
          testCase "FsCheck: DAG charts have no circular waits"
          <| fun _ ->
              let prop (chart: ExtractedStatechart) =
                  let projections = Projection.projectAll chart
                  let result = derive chart projections
                  result.CircularWaits = []

              let arbChart = Arb.fromGen genDagChart
              Prop.forAll arbChart prop |> Check.QuickThrowOnFailure

          // Property 3: Charts with 0 or 1 role never produce circular waits.
          // Circular waits require at least 2 roles with mutual dependencies.
          testCase "FsCheck: single-role charts have no circular waits"
          <| fun _ ->
              let genSingleRoleChart =
                  gen {
                      let! numStates = Gen.choose (2, 5)
                      let stateNames = [ for i in 1..numStates -> $"S{i}" ]

                      let stateMetadata =
                          stateNames
                          |> List.map (fun s ->
                              s,
                              { AllowedMethods = [ "GET"; "POST" ]
                                IsFinal = (s = List.last stateNames)
                                Description = None })
                          |> Map.ofList

                      let! transitions =
                          gen {
                              let! pairs =
                                  Gen.listOfLength
                                      (max 1 (numStates * 2))
                                      (gen {
                                          let! src = Gen.elements stateNames
                                          let! tgt = Gen.elements stateNames
                                          return (src, tgt)
                                      })

                              return
                                  pairs
                                  |> List.mapi (fun i (src, tgt) ->
                                      mkTransition $"e{i}" src tgt None (RestrictedTo [ "Solo" ]))
                          }

                      let selfLoops =
                          stateNames |> List.map (fun s -> mkTransition "poll" s s None (RestrictedTo [ "Solo" ]))

                      return
                          { RouteTemplate = "/solo"
                            StateNames = stateNames
                            InitialStateKey = List.head stateNames
                            GuardNames = []
                            StateMetadata = stateMetadata
                            Roles = [ { Name = "Solo"; Description = None } ]
                            Transitions = transitions @ selfLoops }
                  }

              let prop (chart: ExtractedStatechart) =
                  let projections = Projection.projectAll chart
                  let result = derive chart projections
                  result.CircularWaits = []

              let arbChart = Arb.fromGen genSingleRoleChart
              Prop.forAll arbChart prop |> Check.QuickThrowOnFailure ]
