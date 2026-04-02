/// Order fulfillment reference application demonstrating:
/// - useHierarchyWith CE operation (HierarchySpec with XOR + AND composites)
/// - Hierarchical middleware dispatch (parent handler fallback)
/// - 4 MPST roles: Customer, PaymentService, Warehouse, ShippingProvider
/// - XOR composite Payment state (Authorize -> Capture, with Retry sub-state)
/// - AND composite Fulfillment state (Pick + Pack + Ship in parallel)
/// - Shallow history (Retry -> Capture via HistoryRecord)
/// - AND-state DeriveResult.Warnings surfaced at startup via /diagnostics
///
/// Lifecycle: Pending --(PlaceOrder)--> Authorize --(AuthorizePayment)--> Capture
///            Capture --(CapturePayment)--> Fulfillment --(FulfillOrder)--> Shipped
///            Shipped --(ConfirmDelivery)--> Delivered
///            Capture --(RetryPayment)--> Retry --(RecoverFromRetry)--> Capture (shallow history)
///            Pending/Authorize --(CancelOrder)--> Cancelled
///
/// All transitions go through the primary stateful resource `/orders/{orderId}`.
/// The POST handler at each state fires the state-specific event.
/// Dedicated sub-endpoints (cancel, retry, authorize, capture, etc.) directly manipulate
/// the store for demo purposes so the e2e test can force specific transitions.
/// Region-completion sub-resources (/pick, /pack, /ship) use statefulResource + middleware
/// to prove the library handles ALL hierarchy operations — no direct store manipulation.
///
/// Pipeline order:
///   1. Affordance middleware — OnStarting callback injects Link + Allow headers
///      (deferred until response is sent, after statechart middleware resolves state + roles)
///   2. Statechart middleware — hierarchical dispatch (useHierarchyWith)
module Frank.OrderFulfillment.Sample.Program

open System
open System.Threading.Tasks
open FSharp.Reflection
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Frank.Builder
open Frank.Auth
open Frank.Statecharts
open Frank.Affordances
open Frank.ContentNegotiation
open Frank.Resources.Model
open Frank.OrderFulfillment.Sample.Domain

// ALPS profile base URI for affordance Link headers.
let [<Literal>] AlpsBaseUri = "http://localhost:5060/alps"

// ===========================================================================
// State key helper (mirrors StateKeyExtractor — internal in Frank.Statecharts)
// ===========================================================================

/// Extract DU case name for use as a state key.
let private stateKeyOf (state: OrderState) : string =
    let tagReader = FSharpValue.PreComputeUnionTagReader(typeof<OrderState>)
    let cases = FSharpType.GetUnionCases(typeof<OrderState>, true)
    let caseNames = cases |> Array.map (fun c -> c.Name)
    caseNames.[tagReader (box state)]

// ===========================================================================
// Handlers — state-aware HTTP responses
// ===========================================================================

// ===========================================================================
// Shared helpers — eliminate duplication across handlers
// ===========================================================================

let private defaultSnapshot: InstanceSnapshot<OrderState, unit> =
    { State = orderMachine.Initial
      Context = ()
      HierarchyConfig = ActiveStateConfiguration.empty
      HistoryRecord = HistoryRecord.empty }

let private ensureConfig (stateKey: string) (snapshot: InstanceSnapshot<OrderState, unit>) =
    if ActiveStateConfiguration.isEmpty snapshot.HierarchyConfig then
        HierarchicalRuntime.enterState orderHierarchy stateKey ActiveStateConfiguration.empty
    else
        snapshot.HierarchyConfig

/// Returns current order state with hierarchical config and AND-region statuses.
/// Uses content negotiation per RFC 9110 Section 12 — Content-Type matches Accept header.
let getOrderState (ctx: HttpContext) : Task =
    task {
        let store =
            ctx.RequestServices.GetRequiredService<IStatechartsStore<OrderState, unit>>()

        let orderId = ctx.Request.RouteValues["orderId"] :?> string
        let! result = store.Load orderId

        let state =
            match result with
            | Some snapshot -> snapshot.State
            | None -> orderMachine.Initial

        let key = stateKeyOf state

        let hierFeature = ctx.GetHierarchyFeature()

        let config, regions =
            match hierFeature with
            | Some f ->
                let activeSet = ActiveStateConfiguration.toSet f.ActiveConfiguration
                let cfg = activeSet |> Set.toList

                let regions =
                    if ActiveStateConfiguration.isActive "Fulfillment" f.ActiveConfiguration then
                        fulfillmentRegionNames
                        |> List.map (fun region ->
                            let displayName = regionDisplayNames |> Map.find region
                            let activeKey = regionActiveStates |> Map.find region
                            let doneKey = regionDoneStates |> Map.find region

                            let status =
                                if ActiveStateConfiguration.isActive doneKey f.ActiveConfiguration then
                                    "complete"
                                elif ActiveStateConfiguration.isActive activeKey f.ActiveConfiguration then
                                    "active"
                                else
                                    "unknown"

                            {| name = displayName; status = status |})
                    else
                        []

                cfg, regions
            | None -> [ key ], []

        let body =
            {| state = key
               config = config
               orderId = orderId
               regions = regions |}

        do! ctx.Negotiate(200, body)
    }
    :> Task

/// Create a handler that fires a state event and returns 202.
let private eventHandler (event: OrderEvent) (ctx: HttpContext) : Task =
    StateMachineContext.setEvent ctx event
    ctx.Response.StatusCode <- 202
    Task.CompletedTask

let handlePlaceOrder = eventHandler PlaceOrder
let handleAuthorizePayment = eventHandler AuthorizePayment
let handleCapturePayment = eventHandler CapturePayment
let handleConfirmDelivery = eventHandler ConfirmDelivery

/// Retry recovery via middleware hierarchy op: uses shallow history on Payment composite
/// to restore the last active Payment child (Capture by default).
let handleRecoverFromRetry (ctx: HttpContext) : Task =
    StateMachineContext.setHierarchyOp ctx (RecoverHistory("Payment", Frank.Statecharts.Ast.HistoryKind.Shallow))
    ctx.Response.StatusCode <- 202
    Task.CompletedTask

/// Complete the Pick AND-state region via middleware hierarchy op.
let handlePickComplete (ctx: HttpContext) : Task =
    StateMachineContext.setHierarchyOp ctx (CompleteRegion("Pick", "PickDone"))
    ctx.Response.StatusCode <- 202
    Task.CompletedTask

/// Complete the Pack AND-state region via middleware hierarchy op.
let handlePackComplete (ctx: HttpContext) : Task =
    StateMachineContext.setHierarchyOp ctx (CompleteRegion("Pack", "PackDone"))
    ctx.Response.StatusCode <- 202
    Task.CompletedTask

/// Complete the Ship AND-state region via middleware hierarchy op.
let handleShipComplete (ctx: HttpContext) : Task =
    StateMachineContext.setHierarchyOp ctx (CompleteRegion("Ship", "ShipDone"))
    ctx.Response.StatusCode <- 202
    Task.CompletedTask

// ===========================================================================
// Direct store manipulation helpers (sub-resource endpoints for e2e testing)
// These bypass state machine validation for demo/testing purposes.
// ===========================================================================

/// Directly set an order to a target state, bypassing statechart guards (for e2e testing).
/// Computes proper hierarchical transition so config and history are kept consistent.
let private directSetState (targetState: OrderState) (ctx: HttpContext) : Task =
    task {
        let store = ctx.RequestServices.GetRequiredService<IStatechartsStore<OrderState, unit>>()
        let orderId = ctx.Request.RouteValues["orderId"] :?> string
        let! current = store.Load orderId
        let snapshot = current |> Option.defaultValue defaultSnapshot

        let sourceKey = stateKeyOf snapshot.State
        let targetKey = stateKeyOf targetState
        let currentConfig = ensureConfig sourceKey snapshot
        let result = HierarchicalRuntime.transition orderHierarchy currentConfig sourceKey targetKey snapshot.HistoryRecord

        do! store.Save orderId { State = targetState; Context = (); HierarchyConfig = result.Configuration; HistoryRecord = result.HistoryRecord }
        ctx.Response.StatusCode <- 202
    }
    :> Task

let directCancelOrder = directSetState Cancelled
let directRetryPayment = directSetState Retry
let directCapture = directSetState Capture
let directFulfillment = directSetState Fulfillment
let directShipped = directSetState Shipped
let directDelivered = directSetState Delivered

// ===========================================================================
// Resource definition — statefulResource CE with useHierarchyWith
// ===========================================================================

/// The primary order resource demonstrating hierarchy, 4 MPST roles, and lifecycle.
///
/// Key: useHierarchyWith sets up:
///   - XOR Processing / Payment (exclusive: Authorize | Capture | Retry)
///   - AND Fulfillment (parallel: Pick + Pack + Ship)
///   Middleware uses HierarchicalRuntime.resolveHandlers for dispatch with parent fallback.
let orderResult =
    statefulResource "/orders/{orderId}" {
        machine orderMachine
        resolveInstanceId (fun ctx -> ctx.Request.RouteValues["orderId"] :?> string)
        entryPoint

        // 4 MPST roles (issue #243/#245 proof point):
        //   Customer: MayPoll in most states; MustSelect to cancel
        //   PaymentService: MustSelect to authorize/capture
        //   Warehouse: MustSelect in Fulfillment
        //   ShippingProvider: MustSelect to confirm delivery
        role "Customer" (fun user ->
            let identity = user.Identity
            (isNull identity || not identity.IsAuthenticated) || user.IsInRole("Customer"))
        role "PaymentService" (fun user -> user.IsInRole("PaymentService"))
        role "Warehouse" (fun user -> user.IsInRole("Warehouse"))
        role "ShippingProvider" (fun user -> user.IsInRole("ShippingProvider"))

        // MPST role-constrained transition declarations:
        // Each role only declares transitions from states where it has agency.
        // Unrestricted = shared-input (any role may trigger).
        // RestrictedTo = directed message (only listed roles may trigger).
        transition PlaceOrder Pending Authorize Unrestricted
        transition CancelOrder Pending Cancelled (RestrictedTo [ "Customer" ])
        transition AuthorizePayment Authorize Capture (RestrictedTo [ "PaymentService" ])
        transition CapturePayment Capture Fulfillment (RestrictedTo [ "PaymentService" ])
        transition RetryPayment Capture Retry (RestrictedTo [ "PaymentService" ])
        // RecoverFromRetry uses hierarchy op (setHierarchyOp), not domain event.
        // Declared to give PaymentService agency in Retry for hierarchy op constraint check.
        transition RecoverFromRetry Retry Capture (RestrictedTo [ "PaymentService" ])
        transition ConfirmDelivery Shipped Delivered (RestrictedTo [ "ShippingProvider" ])

        // State handlers — each state's POST fires the state-specific lifecycle event
        inState (forState Pending [ StateHandlerBuilder.get getOrderState; StateHandlerBuilder.post handlePlaceOrder ])
        inState (forState Authorize [ StateHandlerBuilder.get getOrderState; StateHandlerBuilder.post handleAuthorizePayment ])
        inState (forState Capture [ StateHandlerBuilder.get getOrderState; StateHandlerBuilder.post handleCapturePayment ])
        inState (forState Retry [ StateHandlerBuilder.get getOrderState; StateHandlerBuilder.post handleRecoverFromRetry ])
        inState (forState Fulfillment [ StateHandlerBuilder.get getOrderState ])
        inState (forState Shipped [ StateHandlerBuilder.get getOrderState; StateHandlerBuilder.post handleConfirmDelivery ])
        inState (forState Pick [ StateHandlerBuilder.get getOrderState ])
        inState (forState PickDone [ StateHandlerBuilder.get getOrderState ])
        inState (forState Pack [ StateHandlerBuilder.get getOrderState ])
        inState (forState PackDone [ StateHandlerBuilder.get getOrderState ])
        inState (forState Ship [ StateHandlerBuilder.get getOrderState ])
        inState (forState ShipDone [ StateHandlerBuilder.get getOrderState ])
        inState (forState Delivered [ StateHandlerBuilder.get getOrderState ])
        inState (forState Cancelled [ StateHandlerBuilder.get getOrderState ])

        // Transition observer: log exited/entered states for each hierarchy transition.
        onTransition (fun evt ->
            if not (List.isEmpty evt.ExitedStates) || not (List.isEmpty evt.EnteredStates) then
                eprintfn "  exited: [%s]" (String.concat "; " evt.ExitedStates)
                eprintfn "  entered: [%s]" (String.concat "; " evt.EnteredStates))

        // Key acceptance criterion: useHierarchyWith CE operation wires hierarchical runtime.
        // HierarchySpec defines:
        //   - Processing (XOR): [Payment; Fulfillment]
        //   - Payment (XOR):    [Authorize; Capture; Retry]  (shallow history on retry-recover)
        //   - Fulfillment (AND): [Pick; Pack; Ship]           (parallel regions, AND-state warning)
        useHierarchyWith orderHierarchySpec
    }

/// Sub-resource for Pick AND-region completion.
/// POST /orders/{orderId}/pick → CompleteRegion("Pick","PickDone") via middleware.
/// Shares orderMachine and orderHierarchySpec with the parent orderResource —
/// guards, transition function, and state metadata are inherited.
/// This coupling is acceptable for a sample; production designs should
/// consider region-scoped state machines for independent guard evaluation.
let pickResource =
    statefulResource "/orders/{orderId}/pick" {
        machine orderMachine
        resolveInstanceId (fun ctx -> ctx.Request.RouteValues["orderId"] :?> string)
        inState (forState Pick [ StateHandlerBuilder.post handlePickComplete ])
        useHierarchyWith orderHierarchySpec
    }

/// Sub-resource for Pack AND-region completion.
/// POST /orders/{orderId}/pack → CompleteRegion("Pack","PackDone") via middleware.
/// Shares orderMachine and orderHierarchySpec with the parent orderResource —
/// guards, transition function, and state metadata are inherited.
/// This coupling is acceptable for a sample; production designs should
/// consider region-scoped state machines for independent guard evaluation.
let packResource =
    statefulResource "/orders/{orderId}/pack" {
        machine orderMachine
        resolveInstanceId (fun ctx -> ctx.Request.RouteValues["orderId"] :?> string)
        inState (forState Pack [ StateHandlerBuilder.post handlePackComplete ])
        useHierarchyWith orderHierarchySpec
    }

/// Sub-resource for Ship AND-region completion.
/// POST /orders/{orderId}/ship → CompleteRegion("Ship","ShipDone") via middleware.
/// Shares orderMachine and orderHierarchySpec with the parent orderResource —
/// guards, transition function, and state metadata are inherited.
/// This coupling is acceptable for a sample; production designs should
/// consider region-scoped state machines for independent guard evaluation.
let shipResource =
    statefulResource "/orders/{orderId}/ship" {
        machine orderMachine
        resolveInstanceId (fun ctx -> ctx.Request.RouteValues["orderId"] :?> string)
        inState (forState Ship [ StateHandlerBuilder.post handleShipComplete ])
        useHierarchyWith orderHierarchySpec
    }

// Sub-resource endpoints for direct state manipulation (e2e test control paths).
// These bypass the statechart middleware (no StateMachineMetadata) so transitions
// are applied directly to the store without guard evaluation.
let cancelResource =
    resource "/orders/{orderId}/cancel" {
        name "OrderCancel"
        post (RequestDelegate(fun ctx -> directCancelOrder ctx))
    }

// POST /orders/{orderId}/authorize → "authorization succeeded" → state = Capture
let authorizeResource =
    resource "/orders/{orderId}/authorize" {
        name "OrderAuthorize"
        post (RequestDelegate(fun ctx -> directCapture ctx))
    }

// POST /orders/{orderId}/retry → "retry initiated" → state = Retry
let retryResource =
    resource "/orders/{orderId}/retry" {
        name "OrderRetry"
        post (RequestDelegate(fun ctx -> directRetryPayment ctx))
    }

// POST /orders/{orderId}/capture → "payment captured" → state = Fulfillment (AND-state)
let captureResource =
    resource "/orders/{orderId}/capture" {
        name "OrderCapture"
        post (RequestDelegate(fun ctx -> directFulfillment ctx))
    }

// POST /orders/{orderId}/fulfill → "order fulfilled" → state = Shipped
let fulfillResource =
    resource "/orders/{orderId}/fulfill" {
        name "OrderFulfill"
        post (RequestDelegate(fun ctx -> directShipped ctx))
    }

// POST /orders/{orderId}/deliver → "delivery confirmed" → state = Delivered
let deliverResource =
    resource "/orders/{orderId}/deliver" {
        name "OrderDeliver"
        post (RequestDelegate(fun ctx -> directDelivered ctx))
    }

// ===========================================================================
// AND-state DeriveResult.Warnings — formalism proof point (issue #244)
// ===========================================================================

/// Mutable container for AND-state warnings so the /diagnostics endpoint can surface them.
/// Populated at application startup by computeAndStateWarnings.
type ResolvableWarnings() =
    member val Warnings: string list = [] with get, set

/// Serialize a value to JSON using System.Text.Json.
let private toJson (value: 'T) : string =
    System.Text.Json.JsonSerializer.Serialize(value)

/// Compute AND-state DeriveResult.Warnings at startup.
/// The Fulfillment AND-state triggers a warning about synchronization barriers
/// not being modeled in the dual derivation engine (formalism bound 1 per issue #244).
/// Returns the warnings list for surfacing via the /diagnostics endpoint.
let computeAndStateWarnings (logger: ILogger) : string list =
    // Build the hierarchy so deriveWithHierarchy detects AND-state composites.
    let hierarchy = StateHierarchy.build orderHierarchySpec |> Some

    // Use the extracted statechart from the CE (includes role-constrained transitions).
    let statechart = orderResult.Statechart

    // Per-role projection using the Projection module.
    let projections = Projection.projectAll statechart

    // deriveWithHierarchy detects AND-state composites and surfaces formalism bound 1.
    let result =
        Frank.Statecharts.Dual.deriveWithHierarchy statechart projections Map.empty Map.empty hierarchy

    if result.Warnings.IsEmpty then
        logger.LogInformation(
            "AND-state dual derivation: no warnings (unexpected — Fulfillment is AND-state)"
        )
    else
        for warning in result.Warnings do
            logger.LogWarning("AND-state formalism bound (issue #244): {Warning}", warning)

    result.Warnings

// ===========================================================================
// Seed initial order states
// ===========================================================================

let seedInitialStates (app: IApplicationBuilder) =
    let lifetime =
        app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>()

    let store =
        app.ApplicationServices.GetRequiredService<IStatechartsStore<OrderState, unit>>()

    let logger =
        app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("OrderFulfillment.Seed")

    lifetime.ApplicationStarted.Register(fun () ->
        // Seed 4 order instances for e2e test scenarios (o4 for role projection tests)
        for orderId in [ "o1"; "o2"; "o3"; "o4" ] do
            store.Save orderId defaultSnapshot |> fun t -> t.Wait()

        // Compute AND-state warnings and cache for /diagnostics
        let warnings = computeAndStateWarnings logger

        let warningsRef = app.ApplicationServices.GetService<ResolvableWarnings>()

        if not (obj.ReferenceEquals(warningsRef, null)) then
            warningsRef.Warnings <- warnings)
    |> ignore

    app

// ===========================================================================
// Diagnostics endpoint — surfaces AND-state DeriveResult.Warnings
// ===========================================================================

let diagnosticsResource =
    resource "/diagnostics" {
        name "Diagnostics"

        get (RequestDelegate(fun ctx ->
            task {
                let warningsRef = ctx.RequestServices.GetService<ResolvableWarnings>()

                let warnings =
                    if obj.ReferenceEquals(warningsRef, null) then
                        []
                    else
                        warningsRef.Warnings

                // Role projection: ?role=X returns a projected view of the statechart.
                let roleParam =
                    match ctx.Request.Query.TryGetValue("role") with
                    | true, values when values.Count > 0 -> Some(values.[0])
                    | _ -> None

                ctx.Response.ContentType <- "application/health+json"

                match roleParam with
                | Some roleName ->
                    let projected = Projection.projectForRole roleName orderResult.Statechart

                    let transitions =
                        projected.Transitions
                        |> List.map (fun t ->
                            {| event = t.Event
                               source = t.Source
                               target = t.Target |})

                    let response =
                        {| status = "pass"
                           description = $"Order fulfillment statechart projection for role: {roleName}"
                           checks =
                            {| ``projected-transitions`` =
                                [| {| status = "pass"
                                      observedValue = transitions |} |] |} |}

                    do! ctx.Response.WriteAsync(toJson response)
                | None ->
                    let warningStatus = if warnings.IsEmpty then "pass" else "warn"

                    let allProjections = Projection.projectAll orderResult.Statechart

                    let roleProjectionValue =
                        orderResult.Statechart.Roles
                        |> List.map (fun role ->
                            let projected = allProjections |> Map.tryFind role.Name
                            let count = projected |> Option.map (fun sc -> sc.Transitions.Length) |> Option.defaultValue 0
                            role.Name, {| transitions = count |})
                        |> dict

                    let response =
                        {| status = warningStatus
                           description = "Order fulfillment statechart diagnostics"
                           checks =
                            {| ``and-state-warnings`` =
                                [| {| status = warningStatus
                                      observedValue = warnings
                                      observedUnit = "warnings" |} |]
                               ``role-projection`` =
                                [| {| status = "pass"
                                      observedValue = roleProjectionValue |} |] |} |}

                    do! ctx.Response.WriteAsync(toJson response)
            }
            :> Task))
    }

// ===========================================================================
// Application entry point
// ===========================================================================

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults

        service (fun services ->
            services.AddStatechartsStore<OrderState, unit>() |> ignore
            services.AddSingleton<ResolvableWarnings>() |> ignore
            services)

        // X-Role header authentication for development/testing (reads roles from X-Role header)
        useRoleHeaderAuth

        // Pipeline order:
        // 1. Affordance middleware (OnStarting callback — reads IStatechartFeature + roles
        //    just before response is sent, after statechart middleware has resolved state + roles)
        useRuntimeAffordances orderResult.Statechart AlpsBaseUri
        // 2. Statechart middleware (hierarchical dispatch via useHierarchyWith)
        useStatecharts

        // Seed order instances and compute AND-state warnings after services are built
        plug seedInitialStates

        // Primary stateful resource (demonstrates hierarchy + 4 MPST roles)
        resource orderResult.Resource

        // Region-completion stateful resources — each dispatches through middleware
        // (CompleteRegion hierarchy op via handlePickComplete/handlePackComplete/handleShipComplete)
        resource pickResource.Resource
        resource packResource.Resource
        resource shipResource.Resource

        // Direct state manipulation sub-resources (e2e test control paths)
        resource cancelResource
        resource authorizeResource
        resource retryResource
        resource captureResource
        resource fulfillResource
        resource deliverResource

        // Diagnostics (AND-state DeriveResult.Warnings proof point)
        resource diagnosticsResource
    }

    0
