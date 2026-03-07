namespace Frank.Statecharts

open System
open System.Security.Claims
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Frank.Builder

/// Endpoint metadata marker for stateful resources.
/// All fields use obj/string keys because endpoint metadata is untyped.
type StateMachineMetadata =
    {
        /// Boxed StateMachine<'S,'E,'C>
        Machine: obj
        /// state.ToString() -> (httpMethod, handler) list
        StateHandlerMap: Map<string, (string * RequestDelegate) list>
        /// Extracts the instance key from the request
        ResolveInstanceId: HttpContext -> string
        /// Boxed transition event handlers
        TransitionObservers: (obj -> unit) list
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
      StateHandlerMap: Map<'State, (string * RequestDelegate) list>
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
    [<CustomOperation("inState")>]
    member _.InState(spec: StatefulResourceSpec<'S, 'E, 'C>, stateHandlers: StateHandlers<'S>) =
        let existing =
            Map.tryFind stateHandlers.State spec.StateHandlerMap |> Option.defaultValue []

        { spec with
            StateHandlerMap = Map.add stateHandlers.State (existing @ stateHandlers.Handlers) spec.StateHandlerMap }

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

        // Build StateMetadata from inState registrations.
        // IsFinal heuristic: a state is final if it has no non-GET handlers registered.
        // States with only GET (read-only) handlers are considered terminal because
        // they cannot trigger transitions. States with no handlers at all are also final.
        // If uncertain, default to IsFinal = false (non-terminal).
        let stateMetadata =
            spec.StateHandlerMap
            |> Map.map (fun _state handlers ->
                let methods = handlers |> List.map fst |> List.distinct

                let hasNonGetHandler =
                    methods
                    |> List.exists (fun m ->
                        not (String.Equals(m, HttpMethods.Get, StringComparison.OrdinalIgnoreCase)))

                { AllowedMethods = methods
                  IsFinal = not hasNonGetHandler
                  Description = None })

        let machineWithMetadata =
            { machine with
                StateMetadata = stateMetadata }

        // Create StateMachineMetadata for endpoint metadata (untyped/boxed).
        let metadata: StateMachineMetadata =
            { Machine = box machineWithMetadata
              StateHandlerMap =
                spec.StateHandlerMap
                |> Map.toList
                |> List.map (fun (s, h) -> (s.ToString(), h))
                |> Map.ofList
              ResolveInstanceId = resolveId
              TransitionObservers =
                spec.TransitionObservers
                |> List.map (fun h -> (fun (evt: obj) -> h (evt :?> TransitionEvent<'S, 'E, 'C>))) }

        // Flatten all state handlers into a single handler list for ResourceSpec.
        let allHandlers = spec.StateHandlerMap |> Map.toList |> List.collect snd

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
