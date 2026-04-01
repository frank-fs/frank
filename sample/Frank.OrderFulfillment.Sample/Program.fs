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
///   1. State key resolver — reads store, sets IStatechartFeature
///   2. Affordance middleware — injects Link + Allow headers
///   3. Statechart middleware — hierarchical dispatch (useHierarchyWith)
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
open Frank.Statecharts
open Frank.Affordances
open Frank.OrderFulfillment.Sample.Domain

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
// State key bridge middleware
// ===========================================================================

/// Resolves statechart state key from the store and sets IStatechartFeature.
/// Must run AFTER routing and BEFORE statechart middleware.
let resolveStateKey (app: IApplicationBuilder) =
    app.Use(
        Func<HttpContext, Func<Task>, Task>(fun ctx next ->
            task {
                let endpoint = ctx.GetEndpoint()

                if not (isNull endpoint) then
                    let metadata = endpoint.Metadata.GetMetadata<StateMachineMetadata>()

                    if not (obj.ReferenceEquals(metadata, null)) then
                        let instanceId = metadata.ResolveInstanceId ctx
                        let! _stateKey = metadata.GetCurrentStateKey ctx.RequestServices ctx instanceId
                        ()

                do! next.Invoke()
            }
            :> Task)
    )

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

/// Returns current order state as plain text with hierarchical config and AND-region statuses.
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

        let configStr, regionStr =
            match hierFeature with
            | Some f ->
                let activeSet = ActiveStateConfiguration.toSet f.ActiveConfiguration
                let cfg = activeSet |> Set.toList |> String.concat ","

                let regions =
                    if ActiveStateConfiguration.isActive "Fulfillment" f.ActiveConfiguration then
                        let statuses =
                            fulfillmentRegionNames
                            |> List.map (fun region ->
                                let displayName = regionDisplayNames |> Map.find region
                                let activeKey = regionActiveStates |> Map.find region
                                let doneKey = regionDoneStates |> Map.find region
                                let status =
                                    if ActiveStateConfiguration.isActive doneKey f.ActiveConfiguration then "complete"
                                    elif ActiveStateConfiguration.isActive activeKey f.ActiveConfiguration then "active"
                                    else "unknown"
                                $"{displayName}:{status}")
                        ";regions=" + String.concat "," statuses
                    else ""

                cfg, regions
            | None -> key, ""

        do! ctx.Response.WriteAsync($"state={key};config={configStr}{regionStr};orderId={orderId}")
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
let orderResource =
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

/// Sub-resource for completing the Pick AND-region via the middleware hierarchy op path.
/// POST /orders/{orderId}/pick → CompleteRegion("Pick","PickDone") via middleware.
let pickResource =
    statefulResource "/orders/{orderId}/pick" {
        machine orderMachine
        resolveInstanceId (fun ctx -> ctx.Request.RouteValues["orderId"] :?> string)
        inState (forState Pick [ StateHandlerBuilder.post handlePickComplete ])
        useHierarchyWith orderHierarchySpec
    }

/// Sub-resource for completing the Pack AND-region via the middleware hierarchy op path.
/// POST /orders/{orderId}/pack → CompleteRegion("Pack","PackDone") via middleware.
let packResource =
    statefulResource "/orders/{orderId}/pack" {
        machine orderMachine
        resolveInstanceId (fun ctx -> ctx.Request.RouteValues["orderId"] :?> string)
        inState (forState Pack [ StateHandlerBuilder.post handlePackComplete ])
        useHierarchyWith orderHierarchySpec
    }

/// Sub-resource for completing the Ship AND-region via the middleware hierarchy op path.
/// POST /orders/{orderId}/ship → CompleteRegion("Ship","ShipDone") via middleware.
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

/// Compute AND-state DeriveResult.Warnings at startup.
/// The Fulfillment AND-state triggers a warning about synchronization barriers
/// not being modeled in the dual derivation engine (formalism bound 1 per issue #244).
/// Returns the warnings list for surfacing via the /diagnostics endpoint.
let computeAndStateWarnings (logger: ILogger) : string list =
    // Build the hierarchy so deriveWithHierarchy detects AND-state composites.
    let hierarchy = StateHierarchy.build orderHierarchySpec |> Some

    let roleNames = [ "Customer"; "PaymentService"; "Warehouse"; "ShippingProvider" ]

    let mkTransition src tgt evt : Frank.Resources.Model.TransitionSpec =
        { Source = src
          Target = tgt
          Event = evt
          Guard = None
          Constraint = Frank.Resources.Model.Unrestricted }

    // Minimal ExtractedStatechart built from domain knowledge.
    let statechart: Frank.Resources.Model.ExtractedStatechart =
        { RouteTemplate = "/orders/{orderId}"
          InitialStateKey = "Pending"
          GuardNames = []
          StateNames =
            [ "Pending"
              "Authorize"
              "Capture"
              "Retry"
              "Fulfillment"
              "Pick"
              "PickDone"
              "Pack"
              "PackDone"
              "Ship"
              "ShipDone"
              "Shipped"
              "Delivered"
              "Cancelled" ]
          Transitions =
            [ mkTransition "Pending" "Authorize" "placeOrder"
              mkTransition "Pending" "Cancelled" "cancelOrder"
              mkTransition "Authorize" "Capture" "authorizePayment"
              mkTransition "Authorize" "Cancelled" "cancelOrder"
              mkTransition "Capture" "Fulfillment" "capturePayment"
              mkTransition "Capture" "Retry" "retryPayment"
              mkTransition "Retry" "Capture" "recoverFromRetry"
              mkTransition "Fulfillment" "Shipped" "fulfillOrder"
              mkTransition "Shipped" "Delivered" "confirmDelivery" ]
          StateMetadata =
            Map.ofList
                [ "Delivered",
                  { Frank.Resources.Model.StateInfo.AllowedMethods = [ "GET" ]
                    IsFinal = true
                    Description = Some "terminal" }
                  "Cancelled",
                  { Frank.Resources.Model.StateInfo.AllowedMethods = [ "GET" ]
                    IsFinal = true
                    Description = Some "terminal" }
                  "Pending",
                  { Frank.Resources.Model.StateInfo.AllowedMethods = [ "GET"; "POST" ]
                    IsFinal = false
                    Description = None }
                  "Authorize",
                  { Frank.Resources.Model.StateInfo.AllowedMethods = [ "GET"; "POST" ]
                    IsFinal = false
                    Description = None }
                  "Capture",
                  { Frank.Resources.Model.StateInfo.AllowedMethods = [ "GET"; "POST" ]
                    IsFinal = false
                    Description = None }
                  "Retry",
                  { Frank.Resources.Model.StateInfo.AllowedMethods = [ "GET"; "POST" ]
                    IsFinal = false
                    Description = None }
                  "Fulfillment",
                  { Frank.Resources.Model.StateInfo.AllowedMethods = [ "GET"; "POST" ]
                    IsFinal = false
                    Description = None }
                  "Shipped",
                  { Frank.Resources.Model.StateInfo.AllowedMethods = [ "GET"; "POST" ]
                    IsFinal = false
                    Description = None } ]
          Roles =
            roleNames
            |> List.map (fun n ->
                { Frank.Resources.Model.RoleInfo.Name = n
                  Description = None }) }

    // Per-role projection: each role sees the full statechart for this demo.
    let projections = roleNames |> List.map (fun r -> r, statechart) |> Map.ofList

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
        // Seed 3 order instances for e2e test scenarios
        for orderId in [ "o1"; "o2"; "o3" ] do
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

                let body =
                    if warnings.IsEmpty then
                        "warnings=none"
                    else
                        let joined = String.concat "; " warnings
                        $"AND-state warnings: {joined}"

                do! ctx.Response.WriteAsync(body)
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

        // Pipeline order:
        // 1. State key resolver (reads store, sets IStatechartFeature on HttpContext.Features)
        plug resolveStateKey
        // 2. Affordance middleware (reads IStatechartFeature, injects Link + Allow headers)
        useAffordances
        // 3. Statechart middleware (hierarchical dispatch via useHierarchyWith)
        useStatecharts

        // Seed order instances and compute AND-state warnings after services are built
        plug seedInitialStates

        // Primary stateful resource (demonstrates hierarchy + 4 MPST roles)
        resource orderResource

        // Region-completion stateful resources — each dispatches through middleware
        // (CompleteRegion hierarchy op via handlePickComplete/handlePackComplete/handleShipComplete)
        resource pickResource
        resource packResource
        resource shipResource

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
