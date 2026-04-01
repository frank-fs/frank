/// Order fulfillment domain types and state machine definition.
/// Exercises hierarchy (XOR + AND composite states), 4 MPST roles,
/// shallow history (Payment retry), and AND-state DeriveResult.Warnings.
///
/// State hierarchy:
///   Order (top-level XOR):
///     Pending  → Processing → Shipped → Delivered
///                            → Cancelled
///     Processing (XOR composite):
///       Payment (XOR composite with shallow history):
///         Authorize → Capture → Retry
///       Fulfillment (AND composite — parallel regions):
///         Pick | Pack | Ship
///
/// MPST roles:
///   Customer         — MayPoll (most states); MustSelect to cancel
///   PaymentService   — MustSelect to authorize/capture
///   Warehouse        — MustSelect to fulfill
///   ShippingProvider — MustSelect to confirm delivery
module Frank.OrderFulfillment.Sample.Domain

open Frank.Statecharts

// ===========================================================================
// State DU
// ===========================================================================

/// Top-level order lifecycle states.
/// Hierarchy (below) maps composite relationships; these are the flat FSM nodes.
/// PickDone/PackDone/ShipDone are final sub-states within their respective XOR regions.
type OrderState =
    | Pending
    | Authorize
    | Capture
    | Retry
    | Fulfillment
    | Pick
    | PickDone
    | Pack
    | PackDone
    | Ship
    | ShipDone
    | Shipped
    | Delivered
    | Cancelled

// ===========================================================================
// Event DU
// ===========================================================================

type OrderEvent =
    /// Customer places the order (Pending -> Authorize).
    | PlaceOrder
    /// PaymentService authorizes payment (Authorize -> Capture).
    | AuthorizePayment
    /// PaymentService captures payment (Capture -> Fulfillment).
    | CapturePayment
    /// Payment fails; retry from last payment step (Capture -> Retry).
    | RetryPayment
    /// Retry recovery returns to Capture via shallow history.
    | RecoverFromRetry
    /// Warehouse completes pick+pack+ship (Fulfillment -> Shipped).
    | FulfillOrder
    /// Pick region of Fulfillment AND-state is complete.
    | PickComplete
    /// Pack region of Fulfillment AND-state is complete.
    | PackComplete
    /// Ship region of Fulfillment AND-state is complete.
    | ShipComplete
    /// ShippingProvider confirms delivery (Shipped -> Delivered).
    | ConfirmDelivery
    /// Customer cancels (Pending or Authorize -> Cancelled).
    | CancelOrder

// ===========================================================================
// Transition function
// ===========================================================================

let orderTransition (state: OrderState) (event: OrderEvent) (_ctx: unit) =
    match state, event with
    | Pending, PlaceOrder -> TransitionResult.Transitioned(Authorize, ())
    | Pending, CancelOrder -> TransitionResult.Transitioned(Cancelled, ())
    | Authorize, AuthorizePayment -> TransitionResult.Transitioned(Capture, ())
    | Authorize, CancelOrder -> TransitionResult.Transitioned(Cancelled, ())
    | Capture, CapturePayment -> TransitionResult.Transitioned(Fulfillment, ())
    | Capture, RetryPayment -> TransitionResult.Transitioned(Retry, ())
    | Fulfillment, FulfillOrder -> TransitionResult.Transitioned(Shipped, ())
    | Shipped, ConfirmDelivery -> TransitionResult.Transitioned(Delivered, ())
    | _, _ -> TransitionResult.Invalid $"No transition from {state} on {event}"

// ===========================================================================
// State machine definition
// ===========================================================================

let orderMachine: StateMachine<OrderState, OrderEvent, unit> =
    { Initial = Pending
      InitialContext = ()
      Transition = orderTransition
      Guards = []
      StateMetadata =
        Map.ofList
            [ Pending,
              { AllowedMethods = [ "GET"; "POST" ]
                IsFinal = false
                Description = Some "Order placed, awaiting payment authorization" }
              Authorize,
              { AllowedMethods = [ "GET"; "POST" ]
                IsFinal = false
                Description = Some "Payment authorization in progress (PaymentService role)" }
              Capture,
              { AllowedMethods = [ "GET"; "POST" ]
                IsFinal = false
                Description = Some "Payment authorized; ready to capture (PaymentService role)" }
              Retry,
              { AllowedMethods = [ "GET"; "POST" ]
                IsFinal = false
                Description = Some "Payment retry in progress (shallow history to Capture)" }
              Fulfillment,
              { AllowedMethods = [ "GET"; "POST" ]
                IsFinal = false
                Description = Some "Warehouse fulfillment: Pick + Pack + Ship parallel (AND-state)" }
              Pick,
              { AllowedMethods = [ "GET" ]
                IsFinal = false
                Description = Some "Pick region of fulfillment (AND-state)" }
              PickDone,
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = Some "Pick region completed" }
              Pack,
              { AllowedMethods = [ "GET" ]
                IsFinal = false
                Description = Some "Pack region of fulfillment (AND-state)" }
              PackDone,
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = Some "Pack region completed" }
              Ship,
              { AllowedMethods = [ "GET" ]
                IsFinal = false
                Description = Some "Ship region of fulfillment (AND-state)" }
              ShipDone,
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = Some "Ship region completed" }
              Shipped,
              { AllowedMethods = [ "GET"; "POST" ]
                IsFinal = false
                Description = Some "Order shipped, awaiting delivery confirmation (ShippingProvider role)" }
              Delivered,
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = Some "Order delivered (terminal)" }
              Cancelled,
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = Some "Order cancelled (terminal)" } ] }

// ===========================================================================
// HierarchySpec — XOR + AND composite states + shallow history
// ===========================================================================

/// Hierarchy spec for the order fulfillment statechart.
///
/// Structure:
///   Processing (XOR): children = [Payment, Fulfillment]
///   Payment    (XOR): children = [Authorize, Capture, Retry]   — shallow history on parent
///   Fulfillment (AND): children = [PickRegion, PackRegion, ShipRegion]
///     PickRegion (XOR): children = [Pick, PickDone]  — final sub-state per Harel formalism
///     PackRegion (XOR): children = [Pack, PackDone]
///     ShipRegion (XOR): children = [Ship, ShipDone]
///
/// Each AND region is a sub-XOR with an active state and a final (Done) state.
/// A completed region stays active in its Done state (Harel: completion by final sub-state,
/// not by absence). All regions Done -> Fulfillment -> Shipped auto-transition.
let orderHierarchySpec: HierarchySpec =
    { States =
        [ { Id = "Order"
            Kind = XOR
            Children = [ "Pending"; "Processing"; "Shipped"; "Delivered"; "Cancelled" ]
            InitialChild = Some "Pending" }
          { Id = "Processing"
            Kind = XOR
            Children = [ "Payment"; "Fulfillment" ]
            InitialChild = Some "Payment" }
          { Id = "Payment"
            Kind = XOR
            Children = [ "Authorize"; "Capture"; "Retry" ]
            InitialChild = Some "Authorize" }
          { Id = "Fulfillment"
            Kind = AND
            Children = [ "PickRegion"; "PackRegion"; "ShipRegion" ]
            InitialChild = None }
          { Id = "PickRegion"
            Kind = XOR
            Children = [ "Pick"; "PickDone" ]
            InitialChild = Some "Pick" }
          { Id = "PackRegion"
            Kind = XOR
            Children = [ "Pack"; "PackDone" ]
            InitialChild = Some "Pack" }
          { Id = "ShipRegion"
            Kind = XOR
            Children = [ "Ship"; "ShipDone" ]
            InitialChild = Some "Ship" } ] }

let orderHierarchy = StateHierarchy.build orderHierarchySpec

/// Children of the Fulfillment AND-state (the sub-XOR region names).
let fulfillmentRegionNames =
    orderHierarchy.ChildrenMap |> Map.find "Fulfillment"

/// Maps each region XOR to its Done (final) state key.
let regionDoneStates =
    Map.ofList [ "PickRegion", "PickDone"; "PackRegion", "PackDone"; "ShipRegion", "ShipDone" ]

/// Maps each region XOR to its active (non-final) state key.
let regionActiveStates =
    Map.ofList [ "PickRegion", "Pick"; "PackRegion", "Pack"; "ShipRegion", "Ship" ]

/// Maps each region XOR to its display name (used in URLs and Link headers).
let regionDisplayNames =
    Map.ofList [ "PickRegion", "Pick"; "PackRegion", "Pack"; "ShipRegion", "Ship" ]

/// Children of the Payment XOR-state — used by shallow history recovery.
let paymentChildren =
    orderHierarchy.ChildrenMap |> Map.find "Payment"

/// Map state key string back to OrderState DU case.
let private orderStateCases =
    FSharp.Reflection.FSharpType.GetUnionCases(typeof<OrderState>)
    |> Array.map (fun c -> c.Name, FSharp.Reflection.FSharpValue.MakeUnion(c, [||]) :?> OrderState)
    |> Map.ofArray

let parseOrderState (key: string) : OrderState option =
    Map.tryFind key orderStateCases
