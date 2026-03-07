---
work_package_id: WP03
title: StatefulResource Computation Expression
lane: "doing"
dependencies: [WP01]
base_branch: 004-frank-statecharts-WP01
base_commit: 08050bcc7e5921dcbe6341ee4587053d2a1e5295
created_at: '2026-03-07T05:40:43.514608+00:00'
subtasks:
- T010
- T011
- T012
- T013
- T014
- T015
phase: Phase 1 - Foundation
assignee: ''
agent: "claude-wp03"
shell_pid: "36411"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-06T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-002, FR-008, FR-009]
---

# Work Package Prompt: WP03 -- StatefulResource Computation Expression

## Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP03 --base WP01
```

Depends on WP01 (core types). Can run in parallel with WP02.

---

## Objectives & Success Criteria

- Implement `statefulResource` CE with `inState` blocks that register state-specific HTTP method handlers
- Implement `machine`, `onTransition`, and `resolveInstanceId` custom operations
- `Build` method produces a `Resource` with `StateMachineMetadata` in endpoint metadata
- CE compiles and is usable with simple state machines
- `StateMachineMetadata` is readable from endpoint metadata at runtime

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/003-statecharts-feasibility-research/plan.md` -- DD-01 (CE architecture: wraps ResourceBuilder)
- `kitty-specs/003-statecharts-feasibility-research/data-model.md` -- `StateMachineMetadata` entity
- `kitty-specs/003-statecharts-feasibility-research/research.md` -- Proposed `statefulResource` CE usage

**Reference code**:
- `src/Frank/Builder.fs` -- `ResourceBuilder`, `ResourceSpec`, `Yield`/`Run` pattern, `AddMetadata`/`AddHandler` static methods
- `src/Frank.Auth/ResourceBuilderExtensions.fs` -- Extension pattern for `ResourceBuilder`

**Key constraints (DD-01)**:
- The CE **wraps** `ResourceBuilder` rather than extending it
- `StatefulResourceSpec` accumulates: machine definition, per-state handler map, transition observers, instance ID resolver
- At build time: all handlers from all `inState` blocks are registered as endpoints, plus `StateMachineMetadata` is added to each endpoint's metadata
- Follow `ResourceBuilder`'s `Yield`/`Run` pattern
- `inState` blocks need to collect HTTP method handlers scoped to a specific state value

**Desired usage** (from research.md):
```fsharp
let gameResource = statefulResource "/games/{gameId}" {
    machine {
        initial XTurn
        transition gameTransition
        guard "isPlayerX" isPlayerXGuard
        guard "isPlayerO" isPlayerOGuard
    }
    resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)
    onTransition (fun event -> printfn "Transition: %A -> %A" event.PreviousState event.NewState)
    inState XTurn {
        post handleXMove
        get getGameState
    }
    inState OTurn {
        post handleOMove
        get getGameState
    }
    inState Won {
        get getGameResult
    }
    inState Draw {
        get getGameResult
    }
}
```

---

## Subtasks & Detailed Guidance

### Subtask T010 -- Create `StatefulResourceBuilder.fs` with `StatefulResourceSpec`

**Purpose**: Define the accumulator type and the CE builder class.

**Steps**:
1. Create `src/Frank.Statecharts/StatefulResourceBuilder.fs`
2. Add to `.fsproj` `<Compile>` list after `Store.fs`
3. Define the spec accumulator and metadata types:

```fsharp
namespace Frank.Statecharts

open System
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Frank.Builder

/// Endpoint metadata marker for stateful resources.
type StateMachineMetadata =
    { Machine: obj  // Boxed StateMachine<'S,'E,'C>
      StateHandlerMap: Map<string, (string * RequestDelegate) list>  // state.ToString() -> handlers
      ResolveInstanceId: HttpContext -> string
      TransitionObservers: (obj -> unit) list }  // Boxed transition event handlers

/// Per-state handler accumulator used during CE evaluation.
type StateHandlers<'State when 'State : equality> =
    { State: 'State
      Handlers: (string * RequestDelegate) list }

/// Accumulator for the statefulResource CE.
type StatefulResourceSpec<'State, 'Event, 'Context when 'State : equality> =
    { RouteTemplate: string
      Machine: StateMachine<'State, 'Event, 'Context> option
      StateHandlerMap: Map<'State, (string * RequestDelegate) list>
      TransitionObservers: (TransitionEvent<'State, 'Event, 'Context> -> unit) list
      ResolveInstanceId: (HttpContext -> string) option
      Metadata: (EndpointBuilder -> unit) list }

/// Event fired after a successful state transition.
and TransitionEvent<'State, 'Event, 'Context> =
    { PreviousState: 'State
      PreviousContext: 'Context
      NewState: 'State
      NewContext: 'Context
      Event: 'Event
      Timestamp: DateTimeOffset
      User: System.Security.Claims.ClaimsPrincipal option }
```

**Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
**Notes**:
- `StateMachineMetadata.Machine` is `obj` because endpoint metadata is untyped (same as LinkedData pattern)
- `StateHandlerMap` in metadata uses `string` keys (state `.ToString()`) since metadata is untyped
- `TransitionEvent` carries before/after state, event, timestamp, and optional user identity (for Provenance)

### Subtask T011 -- Implement `machine` custom operation

**Purpose**: Allow inline definition of the state machine within the CE.

**Steps**:
1. Consider whether `machine` should be a nested CE or a simple record-accepting operation
2. **Recommended approach**: Start with a simple record-accepting operation to avoid nested CE complexity (risk from plan.md). If CE nesting works well, enhance later.

**Option A -- Simple record (recommended for MVP)**:
```fsharp
[<CustomOperation("machine")>]
member _.Machine(spec: StatefulResourceSpec<'S,'E,'C>, machine: StateMachine<'S,'E,'C>) =
    { spec with Machine = Some machine }
```

Usage:
```fsharp
statefulResource "/games/{gameId}" {
    machine gameMachine  // pass pre-built record
    // ...
}
```

**Option B -- Nested CE (stretch goal)**:
Create a separate `MachineBuilder<'S,'E,'C>` CE with `initial`, `transition`, `guard` operations. This is more ergonomic but adds complexity. Implement Option A first, then B if time permits.

If implementing Option B:
```fsharp
type MachineBuilder<'State, 'Event, 'Context when 'State : equality>() =
    member _.Yield(_) = { Initial = Unchecked.defaultof<_>; Transition = (fun _ _ _ -> Invalid "not configured"); Guards = []; StateMetadata = Map.empty }

    [<CustomOperation("initial")>]
    member _.Initial(spec, state) = { spec with Initial = state }

    [<CustomOperation("transition")>]
    member _.Transition(spec, fn) = { spec with Transition = fn }

    [<CustomOperation("guard")>]
    member _.Guard(spec, name, predicate) =
        { spec with Guards = spec.Guards @ [{ Name = name; Predicate = predicate }] }

    member _.Run(spec) = spec

let machine<'S, 'E, 'C when 'S : equality> = MachineBuilder<'S, 'E, 'C>()
```

**Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
**Notes**: Start with Option A. The nested CE (Option B) depends on F# CE nesting capabilities which may have limitations. Test early.

### Subtask T012 -- Implement `inState` custom operation

**Purpose**: Register per-state HTTP method handlers within the CE.

**Approach**: Use `forState` helper functions (primary API). Nested CE syntax is a future enhancement — do not implement it in this WP.

**Steps**:
1. `inState` collects handlers for a specific state value
2. The handlers are HTTP method + `RequestDelegate` pairs, just like `ResourceBuilder`

**Implementation**:
The `inState` operation accepts a `StateHandlers` record built via `forState` helper functions. This avoids nested CE complexity (plan.md risk mitigation):

```fsharp
[<CustomOperation("inState")>]
member _.InState(spec: StatefulResourceSpec<'S,'E,'C>, stateHandlers: StateHandlers<'S>) =
    let existing = Map.tryFind stateHandlers.State spec.StateHandlerMap |> Option.defaultValue []
    { spec with
        StateHandlerMap = Map.add stateHandlers.State (existing @ stateHandlers.Handlers) spec.StateHandlerMap }
```

To build `StateHandlers`, provide helper functions:

```fsharp
module StateHandlerBuilder =
    let forState<'S when 'S : equality> (state: 'S) (handlers: (string * RequestDelegate) list) : StateHandlers<'S> =
        { State = state; Handlers = handlers }

    let get handler = (HttpMethods.Get, RequestDelegate(fun ctx -> handler ctx :> Task))
    let post handler = (HttpMethods.Post, RequestDelegate(fun ctx -> handler ctx :> Task))
    let put handler = (HttpMethods.Put, RequestDelegate(fun ctx -> handler ctx :> Task))
    let delete handler = (HttpMethods.Delete, RequestDelegate(fun ctx -> handler ctx :> Task))
    let patch handler = (HttpMethods.Patch, RequestDelegate(fun ctx -> handler ctx :> Task))
```

Usage:
```fsharp
statefulResource "/games/{gameId}" {
    machine gameMachine
    inState (forState XTurn [post handleXMove; get getGameState])
    inState (forState OTurn [post handleOMove; get getGameState])
    inState (forState Won [get getGameResult])
    inState (forState Draw [get getGameResult])
}
```

**Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
**Notes**:
- This approach avoids nested CE complexity (plan.md risk mitigation)
- The helper functions mirror `ResourceBuilder`'s handler patterns
- Add overloads for `Task<'a>`, `Async<'a>`, and `HttpContext -> unit` handler signatures matching `ResourceBuilder.AddHandler`
- `StateMetadata` for the machine should be auto-populated from `inState` blocks at build time (T015)

### Subtask T013 -- Implement `onTransition` custom operation

**Purpose**: Register observable transition hooks for Provenance integration.

**Steps**:
```fsharp
[<CustomOperation("onTransition")>]
member _.OnTransition(spec: StatefulResourceSpec<'S,'E,'C>, handler: TransitionEvent<'S,'E,'C> -> unit) =
    { spec with TransitionObservers = spec.TransitionObservers @ [handler] }
```

**Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
**Notes**:
- Multiple `onTransition` handlers can be registered
- Handlers are invoked after successful state update (not before) -- see DD-04
- Observers receive the full `TransitionEvent` with before/after state and user identity

### Subtask T014 -- Implement `resolveInstanceId` custom operation

**Purpose**: Configure how the instance key is extracted from route parameters.

**Steps**:
```fsharp
[<CustomOperation("resolveInstanceId")>]
member _.ResolveInstanceId(spec: StatefulResourceSpec<'S,'E,'C>, resolver: HttpContext -> string) =
    { spec with ResolveInstanceId = Some resolver }
```

**Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
**Notes**:
- Default should be extracting the first route value if not specified
- Common usage: `fun ctx -> ctx.Request.RouteValues["gameId"] :?> string`

### Subtask T015 -- Implement `Build` method producing `Resource`

**Purpose**: Wire everything together: build a `Resource` with all handlers registered and `StateMachineMetadata` in endpoint metadata.

**Steps**:
1. Implement the `Yield` and `Run` methods on `StatefulResourceBuilder`:

```fsharp
[<Sealed>]
type StatefulResourceBuilder(routeTemplate: string) =
    member _.Yield(_) =
        { RouteTemplate = routeTemplate
          Machine = None
          StateHandlerMap = Map.empty
          TransitionObservers = []
          ResolveInstanceId = None
          Metadata = [] }

    member _.Run(spec: StatefulResourceSpec<'S,'E,'C>) : Resource =
        let machine = spec.Machine |> Option.defaultWith (fun () -> failwith "statefulResource requires a machine definition")
        let resolveId = spec.ResolveInstanceId |> Option.defaultWith (fun () ->
            fun (ctx: HttpContext) ->
                let routeData = ctx.GetRouteData()
                routeData.Values.Values |> Seq.head |> string)

        // Build StateMetadata from inState registrations
        let stateMetadata =
            spec.StateHandlerMap
            |> Map.map (fun _state handlers ->
                { AllowedMethods = handlers |> List.map fst |> List.distinct
                  IsFinal = handlers |> List.isEmpty
                  Description = None })

        let machineWithMetadata = { machine with StateMetadata = stateMetadata }

        // Create StateMachineMetadata for endpoint metadata
        let metadata : StateMachineMetadata =
            { Machine = box machineWithMetadata
              StateHandlerMap = spec.StateHandlerMap |> Map.toList |> List.map (fun (s, h) -> (s.ToString(), h)) |> Map.ofList
              ResolveInstanceId = resolveId
              TransitionObservers = spec.TransitionObservers |> List.map (fun h -> (fun (evt: obj) -> h (evt :?> TransitionEvent<'S,'E,'C>))) }

        // Flatten all handlers into a ResourceSpec and add metadata
        let allHandlers =
            spec.StateHandlerMap
            |> Map.toList
            |> List.collect snd

        let resourceSpec =
            { ResourceSpec.Empty with
                Handlers = allHandlers
                Metadata = [ fun (builder: EndpointBuilder) -> builder.Metadata.Add(metadata) ] @ spec.Metadata }

        resourceSpec.Build(routeTemplate)
```

2. Create the `statefulResource` helper function:

```fsharp
let statefulResource routeTemplate = StatefulResourceBuilder(routeTemplate)
```

**Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
**Notes**:
- `Build` validates that `machine` was provided (fail fast, not silent)
- `StateMetadata` is auto-populated from `inState` registrations -- `AllowedMethods` are derived from the HTTP methods registered for each state
- **IsFinal heuristic**: A state is considered "final" (terminal) if no `inState` block registers a handler that would trigger a transition (i.e., only GET/read-only handlers, or no handlers at all). This is derived at build time by checking whether any non-GET handler exists in that state's block. If uncertain, default to `IsFinal = false` (non-terminal). Document this derivation in a code comment on `StateInfo.IsFinal`.
- All handlers are flattened into a single `ResourceSpec` -- the middleware (WP04) handles routing to the correct handler based on current state
- The `StateMachineMetadata` is boxed and added to every endpoint's metadata via `EndpointBuilder.Metadata.Add`

---

## Test Strategy

- Create a simple test that builds a `statefulResource` and verifies:
  1. The returned `Resource` has endpoints
  2. Each endpoint has `StateMachineMetadata` in its metadata
  3. The metadata contains the correct state handler map
  4. The metadata contains the correct machine definition
- Test that missing `machine` operation fails with a clear error message
- Test that `resolveInstanceId` defaults to first route value

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| F# CE nesting limitations for `machine` | Start with record-accepting API (Option A); nested CE is stretch goal |
| Type inference for `inState` blocks | Use explicit type annotations on builder or helper functions |
| Handler signature overloads | Mirror `ResourceBuilder.AddHandler` patterns exactly |
| `obj` boxing in metadata | Required by `EndpointBuilder.Metadata` (IList<obj>) -- same as LinkedData |

---

## Cross-WP Contract: StateMachineMetadata

WP04 (Middleware) depends on the shape of `StateMachineMetadata`. The metadata record MUST include at minimum:
- `StateHandlerMap: Map<string, (string * RequestDelegate) list>` — per-state allowed HTTP methods (keyed by `state.ToString()`)
- `Machine: obj` — the boxed `StateMachine<'State, 'Event, 'Context>` definition
- `ResolveInstanceId: HttpContext -> string` — instance key extractor
- `TransitionObservers: (obj -> unit) list` — boxed transition event handlers

Because F# generics are erased at runtime, the middleware stores these as closures over concrete types (boxed). WP04's middleware accesses typed behavior via closure calls, not via casting the generic parameters. Additional typed closure fields (`GetCurrentStateKey`, `SetStateAfterTransition`, `EvaluateGuards`, `TryGetEventAndTransition`) will be added by WP04 to bridge the type gap.

---

## Review Guidance

- Verify `StatefulResourceBuilder` follows `ResourceBuilder`'s `Yield`/`Run` pattern
- Verify `StateMachineMetadata` is added to every endpoint's metadata
- Verify `StateMetadata` is auto-populated from `inState` registrations
- Verify handler signature overloads match `ResourceBuilder.AddHandler`
- Verify `machine` operation fails fast if not provided
- Check that `TransitionEvent` carries all fields needed for Provenance
- Confirm CE compiles and is usable with a simple state machine example

---

## Activity Log

- 2026-03-06T00:00:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-07T05:40:43Z – claude-wp03 – shell_pid=36411 – lane=doing – Assigned agent via workflow command
