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
type OrderState =
    | Pending
    | Authorize
    | Capture
    | Retry
    | Fulfillment
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
    | Retry, RecoverFromRetry -> TransitionResult.Transitioned(Capture, ())
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
///   Fulfillment (AND): children = [Pick, Pack, Ship]
///
/// NOTE: The flat FSM uses Authorize/Capture/Retry/Fulfillment as leaf states.
/// The hierarchy spec maps them as children to support hierarchical dispatch.
/// The AND composite Fulfillment enables AND-state DeriveResult.Warnings at startup.
let orderHierarchySpec: HierarchySpec =
    { States =
        [ { Id = "Processing"
            Kind = XOR
            Children = [ "Payment"; "Fulfillment" ]
            InitialChild = Some "Payment" }
          { Id = "Payment"
            Kind = XOR
            Children = [ "Authorize"; "Capture"; "Retry" ]
            InitialChild = Some "Authorize" }
          { Id = "Fulfillment"
            Kind = AND
            Children = [ "Pick"; "Pack"; "Ship" ]
            InitialChild = None } ] }
