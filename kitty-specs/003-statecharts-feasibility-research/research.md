# Research: Isomorphic Statechart-to-Code Feasibility for Frank

**Feature**: 003-statecharts-feasibility-research
**Date**: 2026-03-06
**Status**: In Progress

## Executive Summary

This research investigates whether Frank can support isomorphic round-tripping between statechart specifications (WSD, ALPS, XState, SCXML, smcat) and runtime F# code via a `statefulResource` computation expression. The core question is whether a state machine defined in code can be comprehensively projected to multiple spec formats, and whether specs can drive runtime behavior on a best-effort basis.

## Decision Log

### D-001: Feasibility Threshold

**Decision**: Lossy-but-documented round-tripping is acceptable.
**Rationale**: Each spec format intentionally omits certain facets (ALPS omits workflow ordering, XState omits HTTP semantics, WSD omits semantic meaning). Requiring lossless round-tripping in any single format would guarantee failure. Instead:
- **Code-to-spec**: Comprehensive -- generate all formats (WSD, ALPS, XState, SCXML, smcat)
- **Spec-to-code**: Best-effort -- use what the format can express, document gaps
- **Invariant**: No behavioral information may be lost in the runtime code itself
**Evidence**: [E-001], [E-002], [E-003]

### D-002: API Integration Strategy

**Decision**: Deep CE integration via `statefulResource` that auto-generates allowed methods/responses per state.
**Rationale**: The tic-tac-toe prior art demonstrates that state-dependent HTTP behavior (different affordances per user per state) works well with Frank's resource model. A `statefulResource` CE wrapping the existing `resource` CE follows Frank's established extension patterns (LinkedData, Auth, OpenApi, Datastar all use `[<AutoOpen>] module` + `[<CustomOperation>]`).
**Alternative considered**: Library-level composition (separate state machine + standard resource). Rejected because it requires manual handler-per-state wiring, losing the auto-generation benefit.
**Evidence**: [E-004], [E-005]

### D-003: State Storage Default

**Decision**: `MailboxProcessor` as default `IStateMachineStore` implementation. `IStateMachineStore` abstraction enables future backends without API changes.
**Rationale**: MailboxProcessor serializes access naturally (ideal for sequential resources like turn-based games), has negligible per-message overhead (~1-5us), and is proven in the tic-tac-toe prior art. Distributed backends (Redis, Orleans) are explicitly out of scope for v7.3.0.
**Evidence**: [E-004]

### D-004: Generation Architecture

**Decision**: Both build-time (frank-cli) and runtime (middleware endpoints).
**Rationale**: Build-time generation integrates with existing frank-cli infrastructure (OWL/SHACL generation from #79, MSBuild integration). Runtime introspection follows the OpenAPI pattern already established in Frank.OpenApi. Both paths serve different use cases: build-time for CI/documentation, runtime for live discovery.
**Evidence**: [E-006]

### D-005: CLI Tool Strategy

**Decision**: Integrate statechart generation into frank-cli rather than a separate tool.
**Rationale**: frank-cli already has assembly analysis, type extraction, MSBuild integration, and semantic artifact generation. A separate tool would duplicate this infrastructure. The `wsd-gen` F# parser can be consumed as a library dependency.
**Evidence**: [E-006]

## Case Study Analysis

### Case Study 1: Tic-Tac-Toe State Machine

**Source**: `../tic-tac-toe/src/TicTacToe.Engine/Model.fs`

#### Informal State Machine (as implemented)

```
States: XTurn, OTurn, Won, Draw, Error
Events: XMove(position), OMove(position)
Context: GameState (board), ValidMoves, Winner
Guards: Turn-based (X can only move during XTurn, O during OTurn)
        Position-based (square must be Empty)
```

#### State Transition Diagram

```
         startGame
            |
            v
     +--- XTurn <---+
     |      |       |
     | XMove|  OMove|
     |      v       |
     |    OTurn ----+
     |      |
     +------+----> Won(player)
     |      |
     +------+----> Draw
     |      |
     +------+----> Error (invalid move, preserves state)
```

#### Key Observations

1. **State carries context**: Each DU case carries `GameState` and valid moves. This is richer than simple state machine notation -- it's a statechart with extended state (context).
2. **Guards are implicit**: Turn validation is encoded in the `match` pattern (`XTurn _, XMove pos -> ...`). Per-user discrimination happens in the Web layer (`PlayerAssignmentManager`), not the Engine.
3. **Transitions are pure**: `moveX`, `moveO`, `makeMove` are pure functions `(State, Event) -> State`. Side effects (broadcasting, HTTP responses) are in the Web layer.
4. **Error recovery**: `Error` state preserves the previous `GameState`, allowing the game to continue after invalid moves.

#### Mapping to Spec Formats

**WSD representation**:
```
participant Home
participant XTurn
participant OTurn
participant Won
participant Draw

Home->XTurn: startGame
note over XTurn: [guard: role=PlayerX]
XTurn->OTurn: makeMove(position)
note over OTurn: [guard: role=PlayerO]
OTurn->XTurn: makeMove(position)
XTurn->Won: makeMove(position) [wins]
OTurn->Won: makeMove(position) [wins]
XTurn->Draw: makeMove(position) [board full]
OTurn->Draw: makeMove(position) [board full]
```

**Information preserved**: States, transitions, parameters, guards (via note extension)
**Information lost**: Context data shape (GameState, ValidMoves), error recovery semantics, win detection logic

**ALPS representation**:
```json
{
  "alps": {
    "descriptor": [
      { "id": "gameState", "type": "semantic" },
      { "id": "position", "type": "semantic" },
      { "id": "player", "type": "semantic" },
      { "id": "XTurn", "type": "semantic",
        "descriptor": [
          { "href": "#gameState" },
          { "href": "#makeMove" }
        ] },
      { "id": "makeMove", "type": "unsafe", "rt": "#OTurn",
        "descriptor": [{ "href": "#position" }],
        "ext": [{ "id": "guard", "value": "role=currentPlayer" }] }
    ]
  }
}
```

**Information preserved**: Semantic descriptors, transition types (safe/unsafe), return types, parameter schemas
**Information lost**: Workflow ordering (by design -- ALPS FAQ A.2), multiple return types per transition (Won/Draw/OTurn), guard predicates (only labels via ext)

**XState JSON representation**:
```json
{
  "id": "ticTacToe",
  "initial": "xTurn",
  "context": { "board": {}, "validMoves": [] },
  "states": {
    "xTurn": {
      "on": {
        "MAKE_MOVE": [
          { "target": "won", "guard": "isWinningMove" },
          { "target": "draw", "guard": "isBoardFull" },
          { "target": "oTurn" }
        ]
      }
    },
    "oTurn": {
      "on": {
        "MAKE_MOVE": [
          { "target": "won", "guard": "isWinningMove" },
          { "target": "draw", "guard": "isBoardFull" },
          { "target": "xTurn" }
        ]
      }
    },
    "won": { "type": "final" },
    "draw": { "type": "final" }
  }
}
```

**Information preserved**: States, transitions with conditional targets, guards (named), context shape, final states
**Information lost**: Guard implementation details, per-user discrimination (XState guards are pure predicates, not user-aware), HTTP semantics

**SCXML representation**:
```xml
<scxml initial="xTurn" xmlns="http://www.w3.org/2005/07/scxml">
  <datamodel>
    <data id="board"/>
    <data id="validMoves"/>
  </datamodel>
  <state id="xTurn">
    <transition event="makeMove" target="won" cond="isWinningMove()"/>
    <transition event="makeMove" target="draw" cond="isBoardFull()"/>
    <transition event="makeMove" target="oTurn"/>
  </state>
  <state id="oTurn">
    <transition event="makeMove" target="won" cond="isWinningMove()"/>
    <transition event="makeMove" target="draw" cond="isBoardFull()"/>
    <transition event="makeMove" target="xTurn"/>
  </state>
  <final id="won"/>
  <final id="draw"/>
</scxml>
```

**Information preserved**: States, transitions, conditions (expressions), data model, final states, parallel states (if needed)
**Information lost**: HTTP semantics, per-user discrimination, F#-specific type information

**smcat representation**:
```
initial => xTurn: startGame;
xTurn => oTurn: makeMove [valid];
xTurn => won: makeMove [wins];
xTurn => draw: makeMove [boardFull];
oTurn => xTurn: makeMove [valid];
oTurn => won: makeMove [wins];
oTurn => draw: makeMove [boardFull];
won => final;
draw => final;
```

**Information preserved**: States, transitions, labels, conditions (bracket notation)
**Information lost**: Context data, guard implementation, parameters, semantic types, HTTP semantics

### Case Study 2: Onboarding Workflow (from #57)

Already fully mapped in #57 issue body (WSD, ALPS, XState, F# examples). Key additional observations:

1. **Linear with branches**: Unlike tic-tac-toe's cyclic XTurn/OTurn pattern, onboarding is more linear (home -> WIP -> collect data -> finalize)
2. **No per-user guards**: All transitions are available to the current user -- no role-based discrimination needed
3. **Multiple collection paths**: WIP branches to customerData OR accountData, then returns. This is a simple parallel state pattern.

### Transformation Matrix

| Information | WSD | ALPS | XState | SCXML | smcat | F# Runtime |
|-------------|-----|------|--------|-------|-------|------------|
| States | Yes | Yes (semantic descriptors) | Yes | Yes | Yes | Yes (DU cases) |
| Transitions | Yes (arrows) | Yes (safe/unsafe/idempotent) | Yes (events) | Yes (events) | Yes (arrows) | Yes (match patterns) |
| Guards | Partial (note extension) | Partial (ext labels) | Yes (named guards) | Yes (cond expressions) | Partial (bracket labels) | Yes (match patterns + functions) |
| Context/Data | No | No | Yes (context) | Yes (datamodel) | No | Yes (DU payloads) |
| HTTP Methods | Implicit (arrow types) | Yes (safe=GET, unsafe=POST) | No | No | No | Yes (handler methods) |
| Workflow Order | Yes (sequence) | No (by design) | No (event-driven) | No (event-driven) | No (graph) | Yes (match ordering) |
| Per-user Auth | Partial (note) | Partial (ext) | No | No | No | Yes (ClaimsPrincipal) |
| Semantic Meaning | No | Yes (descriptors) | No | No | No | Partial (type names) |
| Parameters | Yes (in messages) | Yes (nested descriptors) | No (in context) | No (in data) | No | Yes (DU fields) |
| Final States | No | No | Yes | Yes | Yes | Implicit (terminal DU cases) |
| Parallel States | No | No | Yes | Yes | No | No (manual) |
| History States | No | No | Yes | Yes | No | No |

### Key Finding: Union Completeness

The union of (WSD + ALPS + XState) covers all information needed by the F# runtime except:
- **Per-user authorization**: Requires Frank.Auth integration, not expressible in any standard spec format. Guards in WSD/ALPS carry labels but not implementation.
- **Error recovery semantics**: Tic-tac-toe's `Error` state preserves previous GameState -- this is an implementation detail not captured by any format.
- **F#-specific type structure**: DU payloads, Option types, etc. are language-specific.

**Conclusion**: Code-to-spec generation can be comprehensive for the spec formats' domains. Spec-to-code is viable for structure (states, transitions, guard names, context shape) but requires developer-supplied implementations for guard predicates, error handling, and authorization logic.

## Proposed API Surface

### Core Types

```fsharp
/// State machine definition (compile-time, generic)
type StateMachine<'State, 'Event, 'Context> =
    { Initial: 'State
      Transition: 'State -> 'Event -> 'Context -> TransitionResult<'State, 'Context>
      Guards: Guard<'State, 'Event, 'Context> list
      StateMetadata: Map<'State, StateInfo> }

/// Result of a transition attempt
type TransitionResult<'State, 'Context> =
    | Transitioned of state: 'State * context: 'Context
    | Blocked of reason: BlockReason
    | Invalid of message: string

/// Why a transition was blocked (maps to HTTP status codes)
type BlockReason =
    | NotAllowed          // 403 Forbidden
    | NotYourTurn         // 409 Conflict
    | InvalidTransition   // 400 Bad Request
    | PreconditionFailed  // 412 Precondition Failed
    | Custom of code: int * message: string

/// Guard predicate with optional user-awareness
type Guard<'State, 'Event, 'Context> =
    { Name: string
      Predicate: GuardContext<'State, 'Event, 'Context> -> GuardResult }

type GuardContext<'State, 'Event, 'Context> =
    { State: 'State
      Event: 'Event
      Context: 'Context
      User: System.Security.Claims.ClaimsPrincipal option }

type GuardResult =
    | Allowed
    | Blocked of BlockReason

/// Metadata about a state (for affordance generation)
type StateInfo =
    { AllowedMethods: string list    // HTTP methods available in this state
      IsFinal: bool                   // Terminal state (no outgoing transitions)
      Description: string option }
```

### State Machine Store Abstraction

```fsharp
/// Abstraction for state persistence
type IStateMachineStore<'State, 'Context> =
    abstract GetState: instanceId: string -> Task<('State * 'Context) option>
    abstract SetState: instanceId: string -> state: 'State -> context: 'Context -> Task<unit>
    abstract Subscribe: instanceId: string -> IObserver<'State * 'Context> -> IDisposable

/// Default MailboxProcessor-backed implementation
type MailboxProcessorStore<'State, 'Context>() =
    interface IStateMachineStore<'State, 'Context>
```

### Computation Expression

```fsharp
/// Usage example: tic-tac-toe as statefulResource
let gameResource gameId = statefulResource $"/games/{gameId}" {
    name "game"

    machine {
        initial XTurn

        transition (fun state event context ->
            match state, event with
            | XTurn, MakeMove pos -> // ... transition logic
            | OTurn, MakeMove pos -> // ...
            | _ -> Invalid "not allowed")

        guard "isPlayersTurn" (fun ctx ->
            match ctx.State, ctx.User with
            | XTurn, Some user when isPlayerX user -> Allowed
            | OTurn, Some user when isPlayerO user -> Allowed
            | _ -> Blocked NotYourTurn)
    }

    // State-specific handlers: only registered methods are available
    inState XTurn {
        get (fun ctx -> // Return board with X's valid moves highlighted)
        post (fun ctx -> // Accept X's move)
    }

    inState OTurn {
        get (fun ctx -> // Return board with O's valid moves highlighted)
        post (fun ctx -> // Accept O's move)
    }

    inState Won {
        get (fun ctx -> // Return final board with winner)
        // No POST -- game is over, method not allowed (405)
    }

    inState Draw {
        get (fun ctx -> // Return final board)
    }

    // Transition event hook (for Provenance)
    onTransition (fun oldState newState event context ->
        // Observable hook -- Frank.Provenance subscribes here
        ())
}
```

### How It Works at Runtime

1. Request arrives at `/games/{gameId}` with method POST
2. `statefulResource` middleware retrieves current state from `IStateMachineStore`
3. If current state is `Won` and method is POST: return 405 Method Not Allowed (no POST handler registered for Won state)
4. If current state is `XTurn` and method is POST: evaluate guards, then invoke the POST handler
5. If guard returns `Blocked NotYourTurn`: return 409 Conflict
6. If transition succeeds: update store, fire `onTransition` hook, return response
7. GET always returns the current state's representation with filtered affordances (only links to available transitions)

### Filtered Affordances

The `statefulResource` CE auto-generates the affordance list per state:
- In `XTurn`: response includes `POST /games/{id}` link (make move) but NOT delete
- In `Won`: response includes only `GET /games/{id}` -- no mutation affordances
- Per-user: if user is PlayerO and state is XTurn, the POST link is still present but the guard will block it (409 vs 405 distinction preserves discoverability)

### Extension Pattern Compliance

Following Frank's established patterns:

```fsharp
[<AutoOpen>]
module Frank.Statecharts.ResourceBuilderExtensions =
    type ResourceBuilder with
        [<CustomOperation("stateMachine")>]
        member _.StateMachine(spec, machine) =
            // Adds StateMachineMetadata to endpoint metadata
            // Middleware reads this metadata to intercept requests
            ResourceBuilder.AddMetadata(spec, fun builder ->
                builder.Metadata.Add(StateMachineMetadata(machine)))
```

This mirrors `Frank.LinkedData`'s `linkedData` marker, `Frank.Auth`'s `requireAuth`, and `Frank.OpenApi`'s handler definitions.

## frank-cli Commands

### New Commands Required

1. **`frank statechart extract <assembly>`**: Extract state machine definitions from compiled Frank assemblies. Reads `StateMachineMetadata` from endpoint metadata, reconstructs the state/transition/guard graph.

2. **`frank statechart generate <format> <assembly>`**: Generate spec artifacts from extracted state machines.
   - `--format wsd` -- Web Sequence Diagram
   - `--format alps` -- ALPS JSON/XML
   - `--format xstate` -- XState JSON
   - `--format scxml` -- SCXML
   - `--format smcat` -- state-machine-cat notation
   - `--format all` -- generate all formats

3. **`frank statechart validate <spec-file> <assembly>`**: Cross-validate a spec file against the runtime state machine. Reports mismatches (missing states, extra transitions, guard name mismatches).

4. **`frank statechart import <spec-file>`**: Best-effort code generation from a spec file. Generates F# DU types and transition skeleton. Developer fills in guard implementations and handler logic.

### MSBuild Integration

```xml
<!-- In .fsproj, similar to existing Frank.Cli.MSBuild -->
<Target Name="GenerateStatechartSpecs" AfterTargets="Build">
  <Exec Command="frank statechart generate all $(TargetPath) --output $(IntermediateOutputPath)statecharts/" />
</Target>
```

### Runtime Endpoints

```fsharp
// In Frank.Statecharts WebHostBuilder extension
type WebHostBuilder with
    [<CustomOperation("useStatecharts")>]
    member _.UseStatecharts(spec) =
        // Adds middleware that serves:
        // GET /_statecharts/{resourceName}.xstate.json
        // GET /_statecharts/{resourceName}.alps.json
        // GET /_statecharts/{resourceName}.scxml
        // GET /_statecharts/{resourceName}.smcat
        // GET /_statecharts/{resourceName}.wsd
```

## Complexity Ceiling Assessment

### Simple (Tic-Tac-Toe, Onboarding)

**Feasibility**: High (90%)
- Linear or cyclic state machines with 3-6 states
- Simple guards (role-based, turn-based)
- No parallel or hierarchical states
- Full round-trip achievable

### Moderate (Stripe Payment Lifecycle)

**Feasibility**: Medium-High (75%)
- 5-8 states with branching (succeeded/failed paths)
- External trigger guards (webhook-driven transitions)
- Timeout-based transitions (pending -> expired)
- Round-trip achievable; external triggers need manual handler implementation

### Complex (FoxyCart API)

**Feasibility**: Medium (60%)
- Multi-entity state coordination (cart, order, shipment, payment -- each with own state machine)
- Hierarchical states (order contains sub-states for fulfillment)
- Parallel states (payment processing concurrent with inventory reservation)
- Round-trip partially achievable; hierarchical/parallel states supported by XState and SCXML but not WSD or smcat
- Would require composing multiple `statefulResource` instances with cross-resource transition coordination

### Assessment

The `statefulResource` CE should target simple-to-moderate complexity as the primary use case. Complex multi-entity coordination can be built by composing multiple state machines, but this is an advanced pattern that may not need first-class CE support in v7.3.0.

## Open Questions

1. **Parallel state composition**: How should multiple `statefulResource` instances coordinate? (e.g., FoxyCart's cart + order + payment). Defer to post-v7.3.0?
2. **History states**: XState and SCXML support history states (return to previous sub-state). Is this needed for Frank's use cases?
3. **Timeout transitions**: Some state machines have time-based transitions (e.g., session expiry). Should `statefulResource` support timer-based events, or is this left to external scheduling?
4. **Existing `wsd-gen` fork status**: How complete is the F# WSD parser? This affects #57 timeline significantly.

## References

See `research/source-register.csv` for full citations.
