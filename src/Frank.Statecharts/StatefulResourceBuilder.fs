namespace Frank.Statecharts

open System
open System.Security.Claims
open System.Threading.Tasks
open FSharp.Reflection
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Frank.Resources.Model
open Frank.Builder

/// Cached, thread-safe state key extraction using FSharpValue.PreComputeUnionTagReader.
/// For DU types, extracts the case name (e.g., Won "X" -> "Won").
/// For non-DU types, falls back to ToString().
[<RequireQualifiedAccess>]
module internal StateKeyExtractor =
    let private cache =
        System.Collections.Concurrent.ConcurrentDictionary<System.Type, obj -> string>()

    let private buildExtractor (t: System.Type) : obj -> string =
        if FSharpType.IsUnion(t, true) then
            let tagReader = FSharpValue.PreComputeUnionTagReader(t)
            let cases = FSharpType.GetUnionCases(t, true)
            let caseNames = cases |> Array.map (fun c -> c.Name)
            fun (o: obj) -> caseNames.[tagReader o]
        else
            fun (o: obj) -> o.ToString()

    let keyOf<'S> (state: 'S) : string =
        let extractor = cache.GetOrAdd(typeof<'S>, System.Func<_, _>(buildExtractor))
        extractor (box state)

/// The outcome of a post-handler transition attempt evaluated by middleware.
[<RequireQualifiedAccess>]
type TransitionAttemptResult =
    | NoEvent
    | Succeeded of transitionEvent: obj
    | Blocked of BlockReason
    | Invalid of message: string

/// Helpers for communicating transition events between handlers and middleware via HttpContext.Items.
module StateMachineContext =
    let private eventKey = "Frank.Statecharts.Event"
    let private hierarchyOpKey = "Frank.Statecharts.HierarchyOp"

    /// Set the event that should trigger a state transition after handler execution.
    let setEvent (ctx: HttpContext) (event: 'Event) = ctx.Items[eventKey] <- box event

    /// Try to retrieve the event set by the handler.
    let tryGetEvent<'Event> (ctx: HttpContext) : 'Event option =
        match ctx.Items.TryGetValue(eventKey) with
        | true, value -> Some(value :?> 'Event)
        | false, _ -> None

    /// Set a hierarchy operation to be executed by middleware after handler execution.
    let setHierarchyOp (ctx: HttpContext) (op: HierarchyOp) = ctx.Items[hierarchyOpKey] <- box op

    /// Try to retrieve a hierarchy operation set by the handler.
    let tryGetHierarchyOp (ctx: HttpContext) : HierarchyOp option =
        match ctx.Items.TryGetValue(hierarchyOpKey) with
        | true, v -> Some(v :?> HierarchyOp)
        | _ -> None

/// Endpoint metadata marker for stateful resources.
/// All fields use obj/string keys because endpoint metadata is untyped.
/// Closure fields bridge the generic type gap for middleware.
type StateMachineMetadata =
    {
        /// Boxed StateMachine<'S,'E,'C>
        Machine: obj
        /// DU case name -> (httpMethod, handler) list; uses StateKeyExtractor for key derivation
        StateHandlerMap: Map<string, (string * RequestDelegate) list>
        /// Extracts the instance key from the request
        ResolveInstanceId: HttpContext -> string
        /// Boxed transition event handlers
        TransitionObservers: (obj -> unit) list
        /// The initial state key (DU case name via StateKeyExtractor)
        InitialStateKey: string
        /// Precomputed guard names from the StateMachine's Guards list.
        /// Avoids runtime reflection on the boxed Machine.
        GuardNames: string list
        /// Precomputed state metadata (IsFinal, AllowedMethods, Description) keyed by state string.
        /// Avoids runtime reflection on the boxed Machine.
        StateMetadataMap: Map<string, StateInfo>
        /// Resolve state from store, set IStatechartFeature on HttpContext.Features, return state key string.
        GetCurrentStateKey: IServiceProvider -> HttpContext -> string -> Task<string>
        /// Evaluate access-control guards using state from IStatechartFeature (pre-handler).
        EvaluateGuards: HttpContext -> GuardResult
        /// Evaluate event-validation guards after the handler has set the event (post-handler).
        EvaluateEventGuards: HttpContext -> GuardResult
        /// Post-handler: get event from HttpContext.Items, read state/context from IStatechartFeature, run transition, persist, return result.
        ExecuteTransition: IServiceProvider -> HttpContext -> string -> Task<TransitionAttemptResult>
        /// Role definitions for this resource (for spec pipeline extraction).
        Roles: RoleDefinition list
        /// Closure: evaluates role predicates against ctx.User, returns Set<string> of matching role names.
        ResolveRoles: HttpContext -> Set<string>
        /// Hierarchical runtime configuration. Always present (flat FSMs are auto-wrapped
        /// in a synthetic __root__ XOR composite at Run time). Middleware always uses
        /// HierarchicalRuntime.resolveHandlers/resolveAllowedMethods for dispatch.
        Hierarchy: StateHierarchy
        /// Extracted statechart snapshot built at Run time from CE state.
        /// Contains state names, transitions (with role constraints), roles, and guard names.
        /// Used by the spec pipeline to avoid re-running reflection at export time.
        Statechart: ExtractedStatechart option
    }

/// Per-state handler accumulator used during CE evaluation.
type StateHandlers<'State when 'State: equality> =
    { State: 'State
      Handlers: (string * RequestDelegate) list }

/// Event fired after a successful state transition.
type TransitionEvent<'State, 'Event, 'Context> =
    { PreviousState: 'State
      PreviousContext: 'Context
      NewState: 'State
      NewContext: 'Context
      /// The event that triggered the transition.
      /// None for hierarchy operations (CompleteRegion, RecoverHistory) that are not
      /// driven by a domain event — using None instead of Unchecked.defaultof avoids
      /// null reference exceptions in observers that inspect this field.
      Event: 'Event option
      Timestamp: DateTimeOffset
      User: ClaimsPrincipal option
      /// States exited in LCA-based order (source up to but not including LCA).
      /// Empty for flat FSM resources (no hierarchy configured).
      ExitedStates: string list
      /// States entered in LCA-based order (LCA down to target).
      /// Empty for flat FSM resources (no hierarchy configured).
      EnteredStates: string list }

/// Accumulator for the statefulResource CE.
type StatefulResourceSpec<'State, 'Event, 'Context when 'State: equality and 'State: comparison> =
    {
        RouteTemplate: string
        Machine: StateMachine<'State, 'Event, 'Context> option
        StateHandlerMap: Map<string, (string * RequestDelegate) list>
        TransitionObservers: (TransitionEvent<'State, 'Event, 'Context> -> unit) list
        ResolveInstanceId: (HttpContext -> string) option
        Metadata: (EndpointBuilder -> unit) list
        Roles: RoleDefinition list
        /// Opt-in hierarchy spec. When Some, the Run method wires StateHierarchy.build into
        /// StateMachineMetadata.Hierarchy, enabling hierarchical dispatch in middleware.
        HierarchySpec: HierarchySpec option
        /// MPST role-constrained transition declarations added via the `transition` CE operation.
        TransitionDeclarations: TransitionSpec list
    }

/// The result of evaluating a statefulResource CE.
/// Contains the routing Resource and an extracted statechart snapshot
/// (state names, transitions with role constraints, roles, guard names).
type StatefulResourceResult =
    { Resource: Resource
      Statechart: ExtractedStatechart }

/// Helper functions for building per-state handler lists.
[<RequireQualifiedAccess>]
module StateHandlerBuilder =

    /// Create a StateHandlers record for the given state with the specified handlers.
    let forState<'S when 'S: equality> (state: 'S) (handlers: (string * RequestDelegate) list) : StateHandlers<'S> =
        { State = state; Handlers = handlers }

    let get (handler: HttpContext -> Task) : string * RequestDelegate =
        (HttpMethods.Get, RequestDelegate(fun ctx -> handler ctx))

    let post (handler: HttpContext -> Task) : string * RequestDelegate =
        (HttpMethods.Post, RequestDelegate(fun ctx -> handler ctx))

    let put (handler: HttpContext -> Task) : string * RequestDelegate =
        (HttpMethods.Put, RequestDelegate(fun ctx -> handler ctx))

    let delete (handler: HttpContext -> Task) : string * RequestDelegate =
        (HttpMethods.Delete, RequestDelegate(fun ctx -> handler ctx))

    let patch (handler: HttpContext -> Task) : string * RequestDelegate =
        (HttpMethods.Patch, RequestDelegate(fun ctx -> handler ctx))

/// The statefulResource computation expression builder.
/// Wraps ResourceBuilder (DD-01) — it does NOT extend it.
[<Sealed>]
type StatefulResourceBuilder(routeTemplate: string) =

    member _.Yield(_) : StatefulResourceSpec<'State, 'Event, 'Context> =
        { RouteTemplate = routeTemplate
          Machine = None
          StateHandlerMap = Map.empty
          TransitionObservers = []
          ResolveInstanceId = None
          Metadata = []
          Roles = []
          HierarchySpec = None
          TransitionDeclarations = [] }

    /// Register a pre-built state machine definition.
    [<CustomOperation("machine")>]
    member _.Machine(spec: StatefulResourceSpec<'S, 'E, 'C>, machine: StateMachine<'S, 'E, 'C>) =
        { spec with Machine = Some machine }

    /// Register handlers for a specific state using forState helper.
    /// Parameterized DU cases (e.g., Won "X", Won "O") map to the same key ("Won").
    [<CustomOperation("inState")>]
    member _.InState(spec: StatefulResourceSpec<'S, 'E, 'C>, stateHandlers: StateHandlers<'S>) =
        let key = StateKeyExtractor.keyOf stateHandlers.State
        let existing = Map.tryFind key spec.StateHandlerMap |> Option.defaultValue []

        { spec with
            StateHandlerMap = Map.add key (existing @ stateHandlers.Handlers) spec.StateHandlerMap }

    /// Register a transition observer hook. Multiple observers can be registered.
    [<CustomOperation("onTransition")>]
    member _.OnTransition(spec: StatefulResourceSpec<'S, 'E, 'C>, handler: TransitionEvent<'S, 'E, 'C> -> unit) =
        { spec with
            TransitionObservers = spec.TransitionObservers @ [ handler ] }

    /// Configure how the instance key is extracted from route parameters.
    /// Default (if not specified): uses the first route value.
    [<CustomOperation("resolveInstanceId")>]
    member _.ResolveInstanceId(spec: StatefulResourceSpec<'S, 'E, 'C>, resolver: HttpContext -> string) =
        { spec with
            ResolveInstanceId = Some resolver }

    /// Marks this resource as a JSON Home entry point.
    /// Only entry-point resources appear in the home document when any are designated.
    [<CustomOperation("entryPoint")>]
    member _.EntryPoint(spec: StatefulResourceSpec<'S, 'E, 'C>) =
        { spec with
            Metadata =
                spec.Metadata
                @ [ fun (b: EndpointBuilder) -> b.Metadata.Add({ IsEntryPoint = true }: EntryPointMetadata) ] }

    /// Declare a named role with an identity-matching predicate.
    [<CustomOperation("role")>]
    member _.Role(spec: StatefulResourceSpec<'S, 'E, 'C>, name: string, predicate: ClaimsPrincipal -> bool) =
        { spec with
            Roles =
                { Name = name
                  ClaimsPredicate = predicate }
                :: spec.Roles }

    /// Opt-in hierarchical runtime. Accepts a HierarchySpec describing composite state relationships.
    /// When set, middleware uses HierarchicalRuntime.resolveHandlers/resolveAllowedMethods for dispatch.
    /// When absent, flat FSM dispatch is unchanged (zero breaking changes).
    [<CustomOperation("useHierarchyWith")>]
    member _.UseHierarchyWith(spec: StatefulResourceSpec<'S, 'E, 'C>, hierarchySpec: HierarchySpec) =
        { spec with
            HierarchySpec = Some hierarchySpec }

    /// Declare an MPST role-constrained transition.
    /// The event, source, and target are resolved to string keys via StateKeyExtractor.keyOf.
    /// Multiple transition declarations may be added; they accumulate in TransitionDeclarations.
    [<CustomOperation("transition")>]
    member _.Transition
        (
            spec: StatefulResourceSpec<'S, 'E, 'C>,
            event: 'E,
            source: 'S,
            target: 'S,
            roleConstraint: RoleConstraint
        ) =
        { spec with
            TransitionDeclarations =
                { Event = StateKeyExtractor.keyOf event
                  Source = StateKeyExtractor.keyOf source
                  Target = StateKeyExtractor.keyOf target
                  Guard = None
                  Constraint = roleConstraint }
                :: spec.TransitionDeclarations }

    member _.Run(spec: StatefulResourceSpec<'S, 'E, 'C>) : StatefulResourceResult =
        let machine =
            spec.Machine
            |> Option.defaultWith (fun () -> failwith "statefulResource requires a machine definition")

        let resolveId =
            spec.ResolveInstanceId
            |> Option.defaultWith (fun () ->
                fun (ctx: HttpContext) ->
                    let routeData = ctx.GetRouteData()
                    routeData.Values.Values |> Seq.head |> string)

        // Validate role definitions — fail fast on duplicates or empty names
        let emptyNames =
            spec.Roles |> List.filter (fun r -> String.IsNullOrWhiteSpace r.Name)

        if not (List.isEmpty emptyNames) then
            failwithf "Empty role name on resource '%s'" routeTemplate

        let roleNames = spec.Roles |> List.map (fun r -> r.Name)

        let duplicates =
            roleNames
            |> List.groupBy id
            |> List.filter (fun (_, g) -> g.Length > 1)
            |> List.map fst

        if not (List.isEmpty duplicates) then
            failwithf "Duplicate role names on resource '%s': %s" routeTemplate (String.concat ", " duplicates)

        // Closure: evaluate role predicates against ctx.User, catch exceptions per-role
        let resolveRoles (ctx: HttpContext) : Set<string> =
            if List.isEmpty spec.Roles then
                Set.empty
            else
                let logger =
                    ctx.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Frank.Statecharts.RoleResolution")

                spec.Roles
                |> List.choose (fun role ->
                    try
                        if role.ClaimsPredicate ctx.User then
                            Some role.Name
                        else
                            None
                    with ex ->
                        logger.LogWarning(
                            ex,
                            "Role predicate '{RoleName}' threw an exception for resource '{RouteTemplate}'",
                            role.Name,
                            routeTemplate
                        )

                        None)
                |> Set.ofList

        // Precomputed state key extractor: DU case name for unions, ToString() for others.
        let stateKey (state: 'S) = StateKeyExtractor.keyOf state

        let initialStateKey = stateKey machine.Initial

        // Pre-compute stateMetadataMap here so it's available for auto-wrap and helper closures.
        let stateMetadataMap =
            machine.StateMetadata
            |> Map.toList
            |> List.map (fun (s, info) -> (StateKeyExtractor.keyOf s, info))
            |> Map.ofList

        // Pre-compute hierarchy once; captured by both getCurrentStateKey and executeTransition closures.
        // Flat FSMs without a HierarchySpec are auto-wrapped in a synthetic __root__ XOR composite
        // so that ALL resources use hierarchical dispatch uniformly.
        let hierarchy =
            match spec.HierarchySpec with
            | Some hierarchySpec -> StateHierarchy.build hierarchySpec
            | None ->
                // Auto-wrap: collect all known state keys and wrap in a synthetic root XOR composite.
                let stateKeys =
                    (Map.toList spec.StateHandlerMap |> List.map fst)
                    @ (Map.toList stateMetadataMap |> List.map fst)
                    |> List.distinct

                let stateKeys =
                    if List.isEmpty stateKeys then
                        [ initialStateKey ]
                    else
                        stateKeys

                StateHierarchy.build
                    { States =
                        [ { Id = "__root__"
                            Kind = XOR
                            Children = stateKeys
                            InitialChild = Some initialStateKey
                            CompletionTarget = None } ] }

        // Reverse key map: state key string -> 'S DU case (for HierarchyOp result conversion).
        let reverseKeyMap: Map<string, 'S> =
            machine.StateMetadata
            |> Map.toList
            |> List.map (fun (s, _) -> (stateKey s, s))
            |> Map.ofList

        // Set of final state keys (for isCompositeComplete checks).
        let finalStates: Set<string> =
            stateMetadataMap
            |> Map.toList
            |> List.choose (fun (k, info) -> if info.IsFinal then Some k else None)
            |> Set.ofList

        // Closure: resolve state from store, set typed values on IStatechartFeature.
        // Always bootstraps IHierarchyFeature (hierarchy is now always configured).
        let getCurrentStateKey (sp: IServiceProvider) (ctx: HttpContext) (instanceId: string) : Task<string> =
            let store = sp.GetRequiredService<IStatechartsStore<'S, 'C>>()

            task {
                let! result = store.Load instanceId

                match result with
                | Some snapshot ->
                    let sk = stateKey snapshot.State
                    ctx.SetStatechartState(sk, snapshot.State, snapshot.Context, instanceId)

                    let config =
                        if ActiveStateConfiguration.isEmpty snapshot.HierarchyConfig then
                            // First access after upgrade or initial store: bootstrap from leaf state
                            HierarchicalRuntime.enterState hierarchy sk ActiveStateConfiguration.empty
                        else
                            snapshot.HierarchyConfig

                    ctx.SetHierarchyFeature(config, snapshot.HistoryRecord)
                    return sk
                | None ->
                    ctx.SetStatechartState(initialStateKey, machine.Initial, machine.InitialContext, instanceId)

                    let config =
                        HierarchicalRuntime.enterState hierarchy initialStateKey ActiveStateConfiguration.empty

                    ctx.SetHierarchyFeature(config, HistoryRecord.empty)
                    return initialStateKey
            }

        // Pre-compute constraint map: (sourceStateKey, eventKey) -> RoleConstraint list.
        // Grouped by key so guarded branching (same event from same state with different
        // constraints) preserves all constraints instead of silently dropping duplicates.
        let constraintMap: Map<string * string, RoleConstraint list> =
            spec.TransitionDeclarations
            |> List.groupBy (fun t -> (t.Source, t.Event))
            |> List.map (fun (key, transitions) -> key, transitions |> List.map (fun t -> t.Constraint))
            |> Map.ofList

        // Closed-world assumption: when both roles and transitions are declared, undeclared
        // transitions are denied (403) rather than allowed. This prevents privilege escalation
        // when an author declares constraints but misses a transition.
        let closedWorld =
            not (List.isEmpty spec.Roles) && not (List.isEmpty spec.TransitionDeclarations)

        // Partition guards by DU case for two-phase evaluation
        let accessGuards =
            machine.Guards
            |> List.choose (function
                | AccessControl(name, pred) -> Some(name, pred)
                | _ -> None)

        let eventGuards =
            machine.Guards
            |> List.choose (function
                | EventValidation(name, pred) -> Some(name, pred)
                | _ -> None)

        // Closure: evaluate access-control guards using state from IStatechartFeature (pre-handler)
        let evaluateGuards (ctx: HttpContext) : GuardResult =
            let feature = ctx.Features.Get<IStatechartFeature<'S, 'C>>()
            let state = feature.State.Value
            let context = feature.Context.Value

            let guardCtx: AccessControlContext<'S, 'C> =
                { User = ctx.User
                  CurrentState = state
                  Context = context
                  Roles = ctx.GetRoles() }

            accessGuards
            |> List.fold
                (fun acc (_, pred) ->
                    match acc with
                    | Blocked _ -> acc
                    | Allowed -> GuardResult.compose acc (pred guardCtx))
                GuardResult.identity

        // Closure: evaluate event-validation guards after handler has set the event (post-handler)
        let evaluateEventGuards (ctx: HttpContext) : GuardResult =
            match StateMachineContext.tryGetEvent<'E> ctx with
            | None -> Allowed // No event set -- skip event guards
            | Some event ->
                let feature = ctx.Features.Get<IStatechartFeature<'S, 'C>>()
                let state = feature.State.Value
                let context = feature.Context.Value
                let userRoles = ctx.GetRoles()

                // Role constraint check: block if transition is RestrictedTo roles the user lacks.
                // With grouped constraints, ANY matching constraint allows the transition
                // (supports guarded branching to different targets with different role requirements).
                let eventKey = StateKeyExtractor.keyOf event
                let stateKey' = StateKeyExtractor.keyOf state

                let constraintResult =
                    match constraintMap.TryFind(stateKey', eventKey) with
                    | Some constraints ->
                        if
                            constraints
                            |> List.exists (fun c ->
                                match c with
                                | Unrestricted -> true
                                | RestrictedTo roles -> roles |> List.exists (fun r -> Set.contains r userRoles))
                        then
                            Allowed
                        else
                            Blocked Forbidden
                    | None ->
                        // Closed-world: undeclared transitions denied when roles + transitions declared.
                        if closedWorld then Blocked Forbidden else Allowed

                match constraintResult with
                | Blocked _ -> constraintResult
                | Allowed ->
                    let guardCtx: EventValidationContext<'S, 'E, 'C> =
                        { User = ctx.User
                          CurrentState = state
                          Event = event
                          Context = context
                          Roles = userRoles }

                    eventGuards
                    |> List.fold
                        (fun acc (_, pred) ->
                            match acc with
                            | Blocked _ -> acc
                            | Allowed -> GuardResult.compose acc (pred guardCtx))
                        GuardResult.identity

        // Pure helper: checks whether the user has agency (any declared transition from current
        // state that they're allowed to trigger). Used by hierarchy op branch in computeTransition.
        let hasHierarchyAgency (ctx: HttpContext) (currentStateKey: string) : bool =
            if not closedWorld then
                true
            else
                let userRoles = ctx.GetRoles()

                spec.TransitionDeclarations
                |> List.exists (fun t ->
                    t.Source = currentStateKey
                    && match t.Constraint with
                       | Unrestricted -> true
                       | RestrictedTo roles -> roles |> List.exists (fun r -> Set.contains r userRoles))

        // Pure computation: determines what save (if any) and result to produce.
        // Extracted from task body to avoid FS3511 in Release builds (complex match
        // expressions inside task CE prevent static state machine compilation).
        let computeTransition (ctx: HttpContext) : InstanceSnapshot<'S, 'C> option * TransitionAttemptResult =
            match StateMachineContext.tryGetHierarchyOp ctx with
            | Some op ->
                let feature = ctx.Features.Get<IStatechartFeature<'S, 'C>>()

                // Role constraint check for hierarchy ops: closed-world requires agency.
                let currentStateKey = stateKey feature.State.Value

                if not (hasHierarchyAgency ctx currentStateKey) then
                    None, TransitionAttemptResult.Blocked Forbidden
                else

                let currentConfig, currentHistory =
                    match ctx.GetHierarchyFeature() with
                    | Some f -> f.ActiveConfiguration, f.History
                    | None ->
                        (ActiveStateConfiguration.empty |> ActiveStateConfiguration.add currentStateKey),
                        HistoryRecord.empty

                let currentState = feature.State.Value
                let currentContext = feature.Context.Value

                match op with
                | CompleteRegion(activeStateKey, doneStateKey) ->
                    let regionResult =
                        HierarchicalRuntime.transition
                            hierarchy
                            currentConfig
                            activeStateKey
                            doneStateKey
                            currentHistory

                    // Check if the AND composite owning this region is now complete.
                    // If so, fire the completion target transition automatically.
                    let finalResult, finalState =
                        match HierarchicalRuntime.findCompletionTarget hierarchy doneStateKey with
                        | Some(compositeId, targetKey) when
                            HierarchicalRuntime.isCompositeComplete
                                hierarchy
                                regionResult.Configuration
                                compositeId
                                finalStates
                            ->
                            let r2 =
                                HierarchicalRuntime.transition
                                    hierarchy
                                    regionResult.Configuration
                                    compositeId
                                    targetKey
                                    regionResult.HistoryRecord

                            r2, (Map.tryFind targetKey reverseKeyMap |> Option.defaultValue currentState)
                        | _ -> regionResult, currentState

                    let snapshot: InstanceSnapshot<'S, 'C> =
                        { State = finalState
                          Context = currentContext
                          HierarchyConfig = finalResult.Configuration
                          HistoryRecord = finalResult.HistoryRecord }

                    let evt: TransitionEvent<'S, 'E, 'C> =
                        { PreviousState = currentState
                          PreviousContext = currentContext
                          NewState = finalState
                          NewContext = currentContext
                          Event = None
                          Timestamp = DateTimeOffset.UtcNow
                          User = if isNull (box ctx.User) then None else Some ctx.User
                          ExitedStates = finalResult.ExitedStates
                          EnteredStates = finalResult.EnteredStates }

                    Some snapshot, TransitionAttemptResult.Succeeded(box evt)

                | RecoverHistory(compositeId, kind) ->
                    let targetConfig =
                        HierarchicalRuntime.enterWithHistory hierarchy kind compositeId currentConfig currentHistory

                    match HierarchicalRuntime.leafState hierarchy targetConfig with
                    | Some leafKey ->
                        let sourceKey = stateKey currentState

                        let hResult =
                            HierarchicalRuntime.transition hierarchy currentConfig sourceKey leafKey currentHistory

                        let newState =
                            Map.tryFind leafKey reverseKeyMap |> Option.defaultValue currentState

                        let snapshot: InstanceSnapshot<'S, 'C> =
                            { State = newState
                              Context = currentContext
                              HierarchyConfig = hResult.Configuration
                              HistoryRecord = hResult.HistoryRecord }

                        let evt: TransitionEvent<'S, 'E, 'C> =
                            { PreviousState = currentState
                              PreviousContext = currentContext
                              NewState = newState
                              NewContext = currentContext
                              Event = None
                              Timestamp = DateTimeOffset.UtcNow
                              User = if isNull (box ctx.User) then None else Some ctx.User
                              ExitedStates = hResult.ExitedStates
                              EnteredStates = hResult.EnteredStates }

                        Some snapshot, TransitionAttemptResult.Succeeded(box evt)
                    | None -> None, TransitionAttemptResult.NoEvent

            | None ->
                match StateMachineContext.tryGetEvent<'E> ctx with
                | None -> None, TransitionAttemptResult.NoEvent
                | Some event ->
                    let feature = ctx.Features.Get<IStatechartFeature<'S, 'C>>()
                    let state = feature.State.Value
                    let context = feature.Context.Value
                    let result = machine.Transition state event context

                    match result with
                    | TransitionResult.Transitioned(newState, newContext) ->
                        let sourceKey = stateKey state
                        let targetKey = stateKey newState

                        let currentConfig, currentHistory =
                            match ctx.GetHierarchyFeature() with
                            | Some f -> f.ActiveConfiguration, f.History
                            | None ->
                                (ActiveStateConfiguration.empty |> ActiveStateConfiguration.add sourceKey),
                                HistoryRecord.empty

                        let hResult =
                            HierarchicalRuntime.transition
                                hierarchy
                                currentConfig
                                sourceKey
                                targetKey
                                currentHistory

                        let snapshot: InstanceSnapshot<'S, 'C> =
                            { State = newState
                              Context = newContext
                              HierarchyConfig = hResult.Configuration
                              HistoryRecord = hResult.HistoryRecord }

                        let evt: TransitionEvent<'S, 'E, 'C> =
                            { PreviousState = state
                              PreviousContext = context
                              NewState = newState
                              NewContext = newContext
                              Event = Some event
                              Timestamp = DateTimeOffset.UtcNow
                              User = if isNull (box ctx.User) then None else Some ctx.User
                              ExitedStates = hResult.ExitedStates
                              EnteredStates = hResult.EnteredStates }

                        Some snapshot, TransitionAttemptResult.Succeeded(box evt)
                    | TransitionResult.Blocked reason -> None, TransitionAttemptResult.Blocked reason
                    | TransitionResult.Invalid msg -> None, TransitionAttemptResult.Invalid msg

        // Thin async wrapper: calls pure computeTransition, persists if needed, returns result.
        // Flat task body avoids FS3511 in Release builds.
        let executeTransition
            (sp: IServiceProvider)
            (ctx: HttpContext)
            (instanceId: string)
            : Task<TransitionAttemptResult> =
            task {
                let snapshotOpt, result = computeTransition ctx

                match snapshotOpt with
                | Some snapshot ->
                    let store = sp.GetRequiredService<IStatechartsStore<'S, 'C>>()
                    do! store.Save instanceId snapshot
                | None -> ()

                return result
            }

        let guardNames =
            machine.Guards
            |> List.map (function
                | AccessControl(name, _) -> name
                | EventValidation(name, _) -> name)

        // Build ExtractedStatechart from CE state.
        // StateNames: union of StateHandlerMap keys and StateMetadata keys, deduplicated.
        let stateNames =
            (Map.toList spec.StateHandlerMap |> List.map fst)
            @ (Map.toList stateMetadataMap |> List.map fst)
            |> List.distinct

        let extractedStatechart: ExtractedStatechart =
            { RouteTemplate = routeTemplate
              StateNames = stateNames
              InitialStateKey = initialStateKey
              GuardNames = guardNames
              StateMetadata = stateMetadataMap
              Roles =
                spec.Roles
                |> List.map (fun r ->
                    { Frank.Resources.Model.RoleInfo.Name = r.Name
                      Description = None })
              Transitions = spec.TransitionDeclarations }

        let metadata: StateMachineMetadata =
            { Machine = box machine
              StateHandlerMap = spec.StateHandlerMap
              ResolveInstanceId = resolveId
              TransitionObservers =
                spec.TransitionObservers
                |> List.map (fun h -> (fun (evt: obj) -> h (evt :?> TransitionEvent<'S, 'E, 'C>)))
              InitialStateKey = initialStateKey
              GuardNames = guardNames
              StateMetadataMap = stateMetadataMap
              GetCurrentStateKey = getCurrentStateKey
              EvaluateGuards = evaluateGuards
              EvaluateEventGuards = evaluateEventGuards
              ExecuteTransition = executeTransition
              Roles = spec.Roles
              ResolveRoles = resolveRoles
              Hierarchy = hierarchy
              Statechart = Some extractedStatechart }

        // Collect distinct HTTP methods across all states.
        // Middleware dispatches the real state-specific handler; endpoints just need routing targets.
        let distinctMethods =
            spec.StateHandlerMap
            |> Map.toList
            |> List.collect (snd >> List.map fst)
            |> List.distinct

        let placeholderHandler = RequestDelegate(fun _ -> Task.CompletedTask)
        let allHandlers = distinctMethods |> List.map (fun m -> (m, placeholderHandler))

        let resourceSpec =
            { ResourceSpec.Empty with
                Handlers = allHandlers
                Metadata =
                    [ fun (builder: EndpointBuilder) -> builder.Metadata.Add(metadata) ]
                    @ spec.Metadata }

        let resource = resourceSpec.Build(routeTemplate)

        { Resource = resource
          Statechart = extractedStatechart }

[<AutoOpen>]
module StatefulResourceBuilderModule =

    /// Create a statefulResource computation expression for the given route template.
    let statefulResource routeTemplate = StatefulResourceBuilder(routeTemplate)

    /// Re-export forState and HTTP method helpers for convenient usage.
    let forState state handlers =
        StateHandlerBuilder.forState state handlers
