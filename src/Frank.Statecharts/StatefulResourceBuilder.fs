namespace Frank.Statecharts

open System
open System.Security.Claims
open System.Threading.Tasks
open FSharp.Reflection
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Frank.Builder

/// Cached, thread-safe state key extraction using FSharpValue.PreComputeUnionTagReader.
/// For DU types, extracts the case name (e.g., Won "X" -> "Won").
/// For non-DU types, falls back to ToString().
[<RequireQualifiedAccess>]
module internal StateKeyExtractor =
    let private cache = System.Collections.Concurrent.ConcurrentDictionary<System.Type, obj -> string>()

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

/// Helpers for communicating events between handlers and middleware via HttpContext.Items.
module StateMachineContext =
    let private eventKey = "Frank.Statecharts.Event"
    let internal stateKey = "Frank.Statecharts.State"
    let internal contextKey = "Frank.Statecharts.Context"

    /// Set the event that should trigger a state transition after handler execution.
    let setEvent (ctx: HttpContext) (event: 'Event) = ctx.Items[eventKey] <- box event

    /// Try to retrieve the event set by the handler.
    let tryGetEvent<'Event> (ctx: HttpContext) : 'Event option =
        match ctx.Items.TryGetValue(eventKey) with
        | true, value -> Some(value :?> 'Event)
        | false, _ -> None

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
        /// Resolve state from store, cache in HttpContext.Items, return state key string.
        GetCurrentStateKey: IServiceProvider -> HttpContext -> string -> Task<string>
        /// Evaluate access-control guards using cached state from HttpContext.Items (pre-handler).
        EvaluateGuards: HttpContext -> GuardResult
        /// Evaluate event-validation guards after the handler has set the event (post-handler).
        EvaluateEventGuards: HttpContext -> GuardResult
        /// Post-handler: get event from HttpContext.Items, run transition, persist, return result.
        ExecuteTransition: IServiceProvider -> HttpContext -> string -> Task<TransitionAttemptResult>
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
      Event: 'Event
      Timestamp: DateTimeOffset
      User: ClaimsPrincipal option }

/// Accumulator for the statefulResource CE.
type StatefulResourceSpec<'State, 'Event, 'Context when 'State: equality and 'State: comparison> =
    { RouteTemplate: string
      Machine: StateMachine<'State, 'Event, 'Context> option
      StateHandlerMap: Map<string, (string * RequestDelegate) list>
      TransitionObservers: (TransitionEvent<'State, 'Event, 'Context> -> unit) list
      ResolveInstanceId: (HttpContext -> string) option
      Metadata: (EndpointBuilder -> unit) list }

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
          Metadata = [] }

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

    member _.Run(spec: StatefulResourceSpec<'S, 'E, 'C>) : Resource =
        let machine =
            spec.Machine
            |> Option.defaultWith (fun () -> failwith "statefulResource requires a machine definition")

        let resolveId =
            spec.ResolveInstanceId
            |> Option.defaultWith (fun () ->
                fun (ctx: HttpContext) ->
                    let routeData = ctx.GetRouteData()
                    routeData.Values.Values |> Seq.head |> string)

        // Precomputed state key extractor: DU case name for unions, ToString() for others.
        let stateKey (state: 'S) = StateKeyExtractor.keyOf state

        let initialStateKey = stateKey machine.Initial

        // Closure: resolve state from store, cache typed values in HttpContext.Items
        let getCurrentStateKey (sp: IServiceProvider) (ctx: HttpContext) (instanceId: string) : Task<string> =
            let store = sp.GetRequiredService<IStateMachineStore<'S, 'C>>()

            task {
                let! result = store.GetState instanceId

                match result with
                | Some(state, context) ->
                    ctx.Items[StateMachineContext.stateKey] <- box state
                    ctx.Items[StateMachineContext.contextKey] <- box context
                    return stateKey state
                | None ->
                    ctx.Items[StateMachineContext.stateKey] <- box machine.Initial
                    ctx.Items[StateMachineContext.contextKey] <- box machine.InitialContext
                    return initialStateKey
            }

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

        // Closure: evaluate access-control guards using cached typed state and context (pre-handler)
        let evaluateGuards (ctx: HttpContext) : GuardResult =
            let state = ctx.Items[StateMachineContext.stateKey] :?> 'S
            let context = ctx.Items[StateMachineContext.contextKey] :?> 'C

            let guardCtx: AccessControlContext<'S, 'C> =
                { User = ctx.User
                  CurrentState = state
                  Context = context }

            accessGuards
            |> List.tryPick (fun (_, pred) ->
                match pred guardCtx with
                | Allowed -> None
                | Blocked reason -> Some(Blocked reason))
            |> Option.defaultValue Allowed

        // Closure: evaluate event-validation guards after handler has set the event (post-handler)
        let evaluateEventGuards (ctx: HttpContext) : GuardResult =
            let state = ctx.Items[StateMachineContext.stateKey] :?> 'S
            let context = ctx.Items[StateMachineContext.contextKey] :?> 'C

            match StateMachineContext.tryGetEvent<'E> ctx with
            | None -> Allowed // No event set -- skip event guards
            | Some event ->
                let guardCtx: EventValidationContext<'S, 'E, 'C> =
                    { User = ctx.User
                      CurrentState = state
                      Event = event
                      Context = context }

                eventGuards
                |> List.tryPick (fun (_, pred) ->
                    match pred guardCtx with
                    | Allowed -> None
                    | Blocked reason -> Some(Blocked reason))
                |> Option.defaultValue Allowed

        // Closure: get event from Items, run transition, persist, return result
        let executeTransition
            (sp: IServiceProvider)
            (ctx: HttpContext)
            (instanceId: string)
            : Task<TransitionAttemptResult> =
            task {
                match StateMachineContext.tryGetEvent<'E> ctx with
                | None -> return TransitionAttemptResult.NoEvent
                | Some event ->
                    let state = ctx.Items[StateMachineContext.stateKey] :?> 'S
                    let context = ctx.Items[StateMachineContext.contextKey] :?> 'C
                    let result = machine.Transition state event context

                    match result with
                    | TransitionResult.Transitioned(newState, newContext) ->
                        let store = sp.GetRequiredService<IStateMachineStore<'S, 'C>>()
                        do! store.SetState instanceId newState newContext

                        let evt: TransitionEvent<'S, 'E, 'C> =
                            { PreviousState = state
                              PreviousContext = context
                              NewState = newState
                              NewContext = newContext
                              Event = event
                              Timestamp = DateTimeOffset.UtcNow
                              User = if isNull (box ctx.User) then None else Some ctx.User }

                        return TransitionAttemptResult.Succeeded(box evt)
                    | TransitionResult.Blocked reason -> return TransitionAttemptResult.Blocked reason
                    | TransitionResult.Invalid msg -> return TransitionAttemptResult.Invalid msg
            }

        let metadata: StateMachineMetadata =
            { Machine = box machine
              StateHandlerMap = spec.StateHandlerMap
              ResolveInstanceId = resolveId
              TransitionObservers =
                spec.TransitionObservers
                |> List.map (fun h -> (fun (evt: obj) -> h (evt :?> TransitionEvent<'S, 'E, 'C>)))
              InitialStateKey = initialStateKey
              GetCurrentStateKey = getCurrentStateKey
              EvaluateGuards = evaluateGuards
              EvaluateEventGuards = evaluateEventGuards
              ExecuteTransition = executeTransition }

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

        resourceSpec.Build(routeTemplate)

[<AutoOpen>]
module StatefulResourceBuilderModule =

    /// Create a statefulResource computation expression for the given route template.
    let statefulResource routeTemplate = StatefulResourceBuilder(routeTemplate)

    /// Re-export forState and HTTP method helpers for convenient usage.
    let forState state handlers =
        StateHandlerBuilder.forState state handlers
