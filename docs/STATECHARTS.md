# Frank.Statecharts

> **Status (April 2026):** The flat FSM features described in "Core Concepts" below work correctly — state-dependent method filtering, guards, transitions, stores, and the `statefulResource` CE. The "Hierarchical Statecharts" section describes hierarchy via nested DUs, which works for *modeling* but does not provide true Harel hierarchical semantics (AND-state parallelism, Fork, tree-structured transition results). The runtime internally computes hierarchy (LCA, exit/entry paths) but results are flat lists. See [AUDIT.md](AUDIT.md) for the full analysis and reset plan.

Frank.Statecharts adds application-level state machines to Frank, enabling resources whose HTTP surface changes based on persisted domain state. Each resource instance has its own state, and the framework enforces which HTTP methods are available, evaluates guards, and manages transitions automatically.

State machines can be defined directly in F# (as shown below) or generated from design specifications such as SCXML, WSD, or ALPS profiles. See [SPEC-PIPELINE.md](SPEC-PIPELINE.md) for the design-to-implementation workflow.

For how this differs from Webmachine and Freya, see [COMPARISON.md](COMPARISON.md).

## Core Concepts

### State Machine Definition

Define your domain as F# types and wire them into a `StateMachine`:

```fsharp
open Frank.Statecharts

type GameState = XTurn | OTurn | Won of winner: string | Draw

type GameEvent = MakeMove of position: int

let gameMachine: StateMachine<GameState, GameEvent, int> =
    { Initial = XTurn
      InitialContext = 0
      Transition = fun state event moveCount ->
          match state with
          | XTurn ->
              let n = moveCount + 1
              if n >= 5 then TransitionResult.Transitioned(Won "X", n)
              else TransitionResult.Transitioned(OTurn, n)
          | OTurn ->
              let n = moveCount + 1
              if n >= 9 then TransitionResult.Transitioned(Draw, n)
              else TransitionResult.Transitioned(XTurn, n)
          | Won _ | Draw -> TransitionResult.Invalid "Game already over"
      Guards = []
      StateMetadata = Map.empty }
```

The three type parameters are:
- **`'State`** -- discriminated union of domain states
- **`'Event`** -- discriminated union of domain events
- **`'Context`** -- application data persisted alongside state (move count, player assignments, etc.)

### Stateful Resource

The `statefulResource` computation expression maps states to HTTP handlers:

```fsharp
open Frank.Builder
open Frank.Statecharts

let gameResource = statefulResource "/games/{gameId}" {
    machine gameMachine
    resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)

    inState (forState XTurn [
        StateHandlerBuilder.get getGame
        StateHandlerBuilder.post handleMove
    ])
    inState (forState OTurn [
        StateHandlerBuilder.get getGame
        StateHandlerBuilder.post handleMove
    ])
    inState (forState (Won "X") [
        StateHandlerBuilder.get getGame
        // No POST -- 405 Method Not Allowed automatically
    ])
    inState (forState Draw [
        StateHandlerBuilder.get getGame
    ])

    onTransition (fun evt -> printfn "State changed: %A -> %A" evt.PreviousState evt.NewState)
}
```

At runtime, middleware:
1. Resolves the instance ID from the route
2. Loads current state from `IStateMachineStore`
3. Returns 405 if the HTTP method isn't registered for the current state
4. Evaluates guards (403, 409, 412, etc. if blocked)
5. Invokes the state-specific handler
6. If the handler set an event via `StateMachineContext.setEvent`, applies the transition and persists the new state
7. Fires `onTransition` observers

### Guards

Guards are named predicates that run before the handler. They receive a `GuardContext` with the current user's `ClaimsPrincipal`, the current state, and the persisted application context:

```fsharp
let turnGuard: Guard<GameState, GameEvent, int> =
    { Name = "TurnGuard"
      Predicate = fun ctx ->
          match ctx.CurrentState with
          | XTurn ->
              if ctx.User.HasClaim("player", "X") then Allowed
              else Blocked NotYourTurn
          | OTurn ->
              if ctx.User.HasClaim("player", "O") then Allowed
              else Blocked NotYourTurn
          | _ -> Allowed }
```

`BlockReason` maps to HTTP status codes:
| BlockReason | HTTP Status |
|-------------|-------------|
| `NotAllowed` | 403 Forbidden |
| `NotYourTurn` | 409 Conflict |
| `InvalidTransition` | 409 Conflict |
| `PreconditionFailed` | 412 Precondition Failed |
| `Custom(code, message)` | Custom status code |

### Triggering Transitions

Handlers signal events via `StateMachineContext.setEvent`. If no event is set, no transition occurs:

```fsharp
let handleMove (ctx: HttpContext) : Task =
    // Parse move from request body...
    StateMachineContext.setEvent ctx (MakeMove position)
    Task.CompletedTask
```

### State Store

Register an `IStateMachineStore<'State, 'Context>` implementation. A built-in in-memory store is provided for development:

```fsharp
services.AddStateMachineStore<GameState, int>()
```

## Hierarchical Statecharts

> **Status:** This section describes modeling hierarchy via nested DUs, which is a valid F# pattern for representing compound states. However, this is not the same as Harel hierarchical statechart semantics — AND-state parallelism, Fork operations, history pseudo-states as first-class constructs, and tree-structured transition results. The runtime has a `StateHierarchy` type that computes LCA-based exit/entry paths (PR #221, #259), but its output is flat `string list` fields. The `TransitionStep` tree type specified in [gh-286](https://github.com/frank-fs/frank/issues/286) was never created. `onTransition` below is the only observer mechanism and is scheduled for replacement by a TraceAlgebra interpreter that does not yet exist. See [AUDIT.md](AUDIT.md) for the full timeline.

Frank.Statecharts supports hierarchical (nested) state machines through F#'s discriminated unions -- no special framework support required.

Model a multi-level statechart by nesting DUs:

```fsharp
type PlayingSubState = XTurn | OTurn | Won of winner: string | Draw

type GameState =
    | Playing of PlayingSubState
    | Disposed

type GameContext =
    { MoveCount: int
      Assignment: PlayerAssignment }
```

This models the same structure as an SCXML statechart with nested states:

```
GameState
  Playing (compound state)
    XTurn
    OTurn
    Won
    Draw
  Disposed (final state)
```

Transitions within the `Playing` compound state change only the sub-state. Transitions from `Playing` to `Disposed` cross the top-level boundary. The transition function handles both naturally:

```fsharp
let transition state event ctx =
    match state, event with
    | Playing XTurn, MakeMove(player, _) ->
        // Sub-state transition: Playing XTurn -> Playing OTurn
        TransitionResult.Transitioned(Playing OTurn, { ctx with MoveCount = ctx.MoveCount + 1 })
    | Playing _, DisposeGame _ ->
        // Top-level transition: Playing -> Disposed
        TransitionResult.Transitioned(Disposed, ctx)
    | Disposed, _ ->
        TransitionResult.Invalid "Game disposed"
```

Guards work across the hierarchy, pattern-matching on nested states:

```fsharp
let turnGuard =
    { Name = "TurnGuard"
      Predicate = fun ctx ->
          match ctx.CurrentState with
          | Playing XTurn -> if ctx.User.HasClaim("player", "X") then Allowed else Blocked NotYourTurn
          | Playing OTurn -> if ctx.User.HasClaim("player", "O") then Allowed else Blocked NotYourTurn
          | _ -> Allowed }
```

SCXML parallel regions (e.g., a `PlayerIdentity` region tracking who has joined the game) are modeled as fields in the `'Context` type rather than as parallel states, since F# DUs naturally serialize/deserialize the state tree while context carries the orthogonal data.

For a real-world example of this pattern, see the [TicTacToe sample application](../sample/Frank.TicTacToe.Sample/) (stateful resource with affordance middleware, guards, and Datastar SSE), the [tic-tac-toe SCXML](https://github.com/panesofglass/tic-tac-toe/blob/master/docs/game.scxml), and the corresponding [hierarchical statechart integration tests](../test/Frank.Statecharts.Tests/StatefulResourceTests.fs) (search for "Multi-level statecharts").

## Test Coverage

The integration test suite validates:

- **Method filtering** -- correct 405 responses for methods not registered in the current state
- **Guard evaluation** -- turn enforcement, role-based access, context-aware participant checks
- **Transition hooks** -- observer invocation, error resilience
- **Store lifecycle** -- state persistence and retrieval across requests
- **Multiple instances** -- independent state per instance ID
- **Edge cases** -- `TransitionResult.Blocked`, response-already-started, multiple guards with short-circuit
- **Hierarchical statecharts** -- nested DU states, sub-state transitions, top-level transitions, cross-hierarchy guards, context-aware participant guards

See [`test/Frank.Statecharts.Tests/`](../test/Frank.Statecharts.Tests/) for the full test suite.

## See Also

- [SPEC-PIPELINE.md](SPEC-PIPELINE.md) — Bidirectional design spec pipeline: how statecharts are designed from WSD/SCXML/ALPS and extracted from running applications for verification
- [SEMANTIC-RESOURCES.md](SEMANTIC-RESOURCES.md) — How statecharts participate in the semantic layer, enabling agent-legible applications
- [COMPARISON.md](COMPARISON.md) — How Frank.Statecharts differs from Webmachine and Freya
