# Multi-Party Sessions: Protocol Enforcement through Hypermedia

Frank does not implement session types in the type-theoretic sense. F# does not have the dependent or indexed type features that would make compile-time session type enforcement practical. Instead, Frank approximates the guarantees of multi-party session types (MPSTs) at runtime through the composition of its existing layers: statecharts, guards, ALPS profiles, SHACL shapes, content negotiation, and the spec pipeline.

This document explains how those layers compose into a coherent multi-party protocol enforcement mechanism, identifies the gap that makes this approximation incomplete (explicit role projection), and describes how to close it.

## Background: What Multi-Party Session Types Guarantee

In the formal MPST literature (Honda, Yoshida, Carbone 2008; Scribble), a multi-party protocol is specified as a **global type** — a description of the complete interaction among all participants. The global type is then **projected** onto each role to produce a **local type**: the protocol as seen from one participant’s perspective.

The key guarantees are:

- **Safety**: no participant sends a message the protocol doesn’t expect at that point
- **Progress**: the protocol can always advance if all participants cooperate (no deadlocks)
- **Session fidelity**: the actual interaction conforms to the specified protocol
- **Role separation**: each participant sees only the actions relevant to their role

Static MPST systems enforce these at compile time. Frank enforces them at the HTTP boundary through middleware, with verification support from the spec pipeline.

## How Frank’s Layers Map to MPST Concepts

|MPST Concept                    |Frank Layer                            |Mechanism                                                                                                       |
|--------------------------------|---------------------------------------|----------------------------------------------------------------------------------------------------------------|
|Global type                     |Frank.Statecharts                      |SCXML statechart defining all states, transitions, and events across all roles                                  |
|Local type (per-role view)      |ALPS profile + role projection         |Per-role ALPS profile derived from global SCXML + role definitions (see [Role Projection](#role-projection))    |
|Role                            |ASP.NET Core authentication            |`ClaimsPrincipal` with role claims (e.g., `player=X`, `player=O`)                                               |
|Message type (payload schema)   |Frank.Validation                       |SHACL shapes constraining request and response bodies                                                           |
|Send action                     |ALPS `unsafe` / `idempotent` transition|POST/PUT/PATCH/DELETE with SHACL-validated payload                                                              |
|Receive action                  |ALPS `safe` transition                 |GET returning state-appropriate representation                                                                  |
|Protocol state                  |`IStateMachineStore`                   |Persisted per-instance state determining available transitions                                                  |
|Safety (no invalid sends)       |Guards + state-dependent routing       |Middleware returns 405 (method unavailable in state) or 403/409 (guard blocked)                                 |
|Session fidelity                |Spec pipeline                          |Extract spec from running app, compare against design spec                                                      |
|Provenance (interaction history)|Frank.Provenance                       |PROV-O annotations recording actual state transitions for post-hoc conformance checking                         |
|Progress (liveness)             |Cross-validator + projection analysis  |Static check on global type + projections for deadlock-free states (see [Progress Analysis](#progress-analysis))|

## What Frank Already Enforces

The existing layers provide runtime enforcement of most MPST safety properties without any new machinery:

### State-Dependent Method Availability (Safety)

`Frank.Statecharts` middleware checks the resource’s current state before dispatching to a handler. If POST is not registered for the `Won` state, the middleware returns 405 with an `Allow` header listing the methods that are available. This is the runtime analog of a session type system rejecting a send action that doesn’t match the current protocol state.

### Role-Based Guard Evaluation (Role Separation)

Guards evaluate `ClaimsPrincipal` claims against the current state. In the tic-tac-toe example, a `TurnGuard` checks whether the authenticated user holds the claim matching the active player. The middleware returns 409 (Conflict) for a valid participant acting out of turn, and 403 (Forbidden) for a non-participant. This enforces that each role can only perform actions the protocol permits at the current state — the runtime equivalent of local type conformance.

### Typed Transitions (Session Fidelity)

The `StateMachine<'State, 'Event, 'Context>` transition function is a total function over the state × event product. `TransitionResult.Invalid` rejects events that are not valid for the current state. Combined with F#’s exhaustive pattern matching on discriminated unions, this ensures that every state/event combination is explicitly handled — either producing a valid successor state or an explicit rejection.

### SHACL Validation (Message Typing)

Frank.Validation applies SHACL shapes to request and response bodies. In MPST terms, this constrains the payload types at each communication point. A `makeMove` transition might require a SHACL shape specifying that `position` is an integer in range 0–8 — this is the message type associated with that protocol action.

### Provenance (After-the-Fact Conformance)

Frank.Provenance records PROV-O annotations for every state transition via `onTransition` observers. An agent or auditor can query the provenance graph to verify that the actual interaction history conforms to the protocol. This is weaker than compile-time checking but provides accountability: if a bug allows a protocol violation, the provenance record captures it.

## What Is Missing: Role Projection

The significant gap is **explicit projection from the global type to per-role local types**.

Currently, the global protocol (SCXML statechart) exists as a single artifact. Each role’s view of the protocol is an emergent property of guards and state-dependent handler registration — it works correctly at runtime, but there is no formal artifact representing “what Player X can do at each protocol state.” The projection is implicit, spread across guard predicates, `inState` blocks, and claims checks.

Making projection explicit closes the gap between Frank’s runtime enforcement and the formal MPST guarantees.

### The Projection Operator

Projection takes the global SCXML statechart plus a set of role definitions and produces a **per-role ALPS profile** for each role at each reachable state.

Inputs:

- The global SCXML statechart (states, transitions, events, guards)
- Role definitions mapping roles to authentication claims
- ALPS descriptors for each transition (semantic type, parameters, return types)

Output per role:

- An ALPS profile containing only the descriptors for transitions that role can trigger or observe in each state
- Guard annotations indicating the conditions under which each transition is available
- Return type descriptors for the representations that role receives

The projection is not a separate runtime system — it is a **derivation step** that produces ALPS documents from existing artifacts. It can run at build time (as part of the spec pipeline) or at request time (filtered by the authenticated user’s role).

### Per-Role ALPS Profile Example

Given a tic-tac-toe global protocol with states `XTurn`, `OTurn`, `Won`, `Draw` and two roles (`PlayerX`, `PlayerO`):

**Global ALPS profile** (simplified):

```json
{
  "alps": {
    "descriptor": [
      { "id": "gameState", "type": "semantic", "doc": "Current game state" },
      { "id": "makeMove", "type": "unsafe",
        "doc": "Place a mark on the board",
        "descriptor": [
          { "id": "position", "type": "semantic" }
        ],
        "ext": [
          { "id": "guard", "value": "role=activePlayer" },
          { "id": "availableInStates", "value": "XTurn,OTurn" }
        ]
      },
      { "id": "viewGame", "type": "safe", "doc": "Retrieve current game state" }
    ]
  }
}
```

**Projected ALPS for PlayerX in state XTurn**:

```json
{
  "alps": {
    "descriptor": [
      { "id": "gameState", "type": "semantic" },
      { "id": "makeMove", "type": "unsafe",
        "descriptor": [
          { "id": "position", "type": "semantic" }
        ]
      },
      { "id": "viewGame", "type": "safe" }
    ],
    "ext": [
      { "id": "projectedRole", "value": "PlayerX" },
      { "id": "protocolState", "value": "XTurn" }
    ]
  }
}
```

**Projected ALPS for PlayerO in state XTurn**:

```json
{
  "alps": {
    "descriptor": [
      { "id": "gameState", "type": "semantic" },
      { "id": "viewGame", "type": "safe" }
    ],
    "ext": [
      { "id": "projectedRole", "value": "PlayerO" },
      { "id": "protocolState", "value": "XTurn" }
    ]
  }
}
```

Player O cannot see the `makeMove` transition in `XTurn` state — it does not exist in their projected local type.

### Projection via Content Negotiation

An authenticated agent requesting their ALPS profile receives the projected view:

```
GET /games/abc123
Accept: application/alps+json
Cookie: auth=<PlayerO-session>

→ 200 OK
Content-Type: application/alps+json
```

The response contains only the descriptors available to PlayerO given the current game state. This is the hypermedia realization of a local type: the agent sees exactly what it can do right now.

For unauthenticated or administrative access, the global (unprojected) ALPS profile is served instead, giving a complete view of the protocol.

### Where Projection Lives in the Architecture

Projection is a function of existing artifacts, not a new runtime system:

```
Frank.Statecharts (global SCXML)
    + Role definitions (claims → role mapping)
    + Frank.LinkedData (ALPS descriptors)
    ────────────────────────────
    → Projection operator
    ────────────────────────────
    → Per-role ALPS profiles (build-time artifacts)
    → Filtered ALPS responses (request-time, via content negotiation)
```

At build time, the spec pipeline can generate all per-role profiles and validate them. At request time, the content negotiation layer filters the ALPS response based on the authenticated user’s role and the resource’s current state.

## Session Establishment

The tic-tac-toe SCXML models two parallel regions: **GamePlay** (turn progression) and **PlayerIdentity** (role assignment). Issue [#12](https://github.com/panesofglass/tic-tac-toe/issues/12) recommends folding PlayerIdentity into `GameContext` data rather than modeling it as a separate state region.

From the MPST perspective, session establishment is a **distinct protocol phase** that precedes the main interaction. The session establishment protocol has its own global type:

```
Lobby
    → PlayerX joins → XAssigned
    → PlayerO joins → BothAssigned
    → Game begins (transition to GamePlay protocol)
```

There are two viable approaches, and the choice depends on whether session establishment needs the same level of formal treatment as the main protocol:

### Option A: Establishment as Context Data

Player assignment is tracked in `GameContext` (e.g., a `PlayerAssignment` record). The main statechart’s guards enforce that the game cannot progress until both players are assigned. This is simple and matches the current implementation.

This works well when the establishment phase is simple (join/assign) and does not itself involve multi-step negotiation between participants.

### Option B: Establishment as a Preceding Protocol Phase

Model session establishment as its own `StateMachine<LobbyState, LobbyEvent, LobbyContext>` with its own `statefulResource`. When the lobby reaches `BothAssigned`, a transition hook creates the game instance and initializes the GamePlay state machine.

This is appropriate when:

- The establishment phase itself is a multi-party protocol (e.g., negotiation, bidding for roles, readiness confirmation)
- You want projection and formal verification of the establishment phase
- The establishment protocol may be reused across different game types or application domains

For tic-tac-toe, Option A is sufficient. For more complex applications (e.g., multi-player games with role selection, auction protocols with registration phases), Option B provides the same guarantees for establishment that Frank.Statecharts provides for the main protocol.

## Progress Analysis

The one MPST guarantee that is hardest to enforce at runtime is **progress** (liveness): the assurance that the protocol can always advance if all participants cooperate. Deadlock and starvation are properties of the protocol design, not of individual requests.

Frank can support progress analysis as a static check in the spec pipeline:

### Deadlock Detection

A state is a **deadlock** if no role has an available transition. Given the global SCXML and role projections, enumerate all reachable states and verify that at least one role has at least one available transition in each non-final state.

### Starvation Detection

A role is **starved** if there exists a reachable execution path where that role is permanently unable to act — the protocol continues, but only through other roles’ actions, and the starved role never regains an available transition. This requires path analysis on the projected state machines.

### Implementation

Both analyses operate on the typed ASTs from the spec pipeline parsers. The cross-validator (#91) could include a `--check-progress` flag:

```
frank validate --check-progress game.scxml --roles PlayerX,PlayerO
```

This is a build-time check, not a runtime enforcement mechanism. It tells the developer whether their protocol design guarantees progress before any code is written.

## Comparison with Formal MPST Systems

|Property                     |Formal MPSTs (Scribble, etc.)            |Frank                                                                              |
|-----------------------------|-----------------------------------------|-----------------------------------------------------------------------------------|
|**When checked**             |Compile time (static)                    |Mix: build-time (spec pipeline) + request-time (middleware) + post-hoc (provenance)|
|**Global type**              |Scribble protocol definition             |SCXML statechart                                                                   |
|**Projection**               |Automatic, type-level                    |Derived from SCXML + role defs → per-role ALPS profiles                            |
|**Safety**                   |Type errors for protocol violations      |403/405/409 HTTP responses for protocol violations                                 |
|**Progress**                 |Checked by projection algorithm          |Static analysis via spec pipeline cross-validator                                  |
|**Message typing**           |Payload types in protocol definition     |SHACL shapes                                                                       |
|**Session fidelity**         |Guaranteed by construction               |Verified by spec extraction + comparison; auditable via PROV-O                     |
|**Enforcement boundary**     |Process/channel boundary                 |HTTP request/response boundary                                                     |
|**Transport independence**   |Yes (abstract channels)                  |No (HTTP-specific, by design)                                                      |
|**Language support required**|Type system extensions or code generation|None — standard F#, standard HTTP                                                  |

Frank trades compile-time completeness for **deployment-time verifiability** and **runtime enforcement at the network boundary**. The protocol is not proven correct by the compiler, but it is checkable by the spec pipeline, enforceable by middleware, and auditable through provenance. For HTTP applications where the trust boundary is between client and server, this is the appropriate level of enforcement — you cannot statically verify that a remote client will follow the protocol regardless of what your type system guarantees about your own code.

## Integration with Existing Documents

This document connects to the other design documents as follows:

- **<STATECHARTS.md>** provides the global type: the `StateMachine<'State, 'Event, 'Context>` definition and the `statefulResource` CE that enforces state-dependent behavior.
- **<SEMANTIC-RESOURCES.md>** provides the self-description mechanism: ALPS profiles, RDF models, and content negotiation are the substrate for serving projected local types to authenticated agents.
- **<SPEC-PIPELINE.md>** provides the verification loop: the cross-validator can be extended with projection consistency checks and progress analysis.
- **<COMPARISON.md>** clarifies why Frank operates at the application level rather than the protocol level — the same reasoning applies here. Multi-party session enforcement belongs at the application/domain layer, not in a reimplementation of HTTP.

## Implementation Priorities

The layers that already exist (statecharts, guards, SHACL, ALPS, provenance) provide the runtime safety guarantees. The missing piece — explicit role projection — can be built incrementally:

1. **Role definition schema**: a declarative way to map authentication claims to protocol roles, associated with a `statefulResource`. This is the foundation everything else depends on.
1. **Projection operator**: given the SCXML + role definitions + ALPS descriptors, derive per-role ALPS profiles. This can be a build-time tool in the spec pipeline initially.
1. **Projected content negotiation**: serve the projected ALPS profile to authenticated agents based on their role and the resource’s current state. This extends `Frank.LinkedData`’s content negotiation.
1. **Cross-validator extensions**: add projection consistency checks and progress analysis to the spec pipeline’s `frank validate` command.
1. **Session establishment patterns**: document the Option A / Option B patterns and provide examples for both simple (context data) and complex (separate protocol phase) establishment scenarios.

## References

- Honda, Yoshida, Carbone (2008) — [Multiparty Asynchronous Session Types](https://doi.org/10.1145/1328438.1328472) (POPL ’08)
- [Scribble](http://www.scribble.org/) — Protocol description language for multiparty session types
- [MPST at Imperial College](https://mrg.doc.ic.ac.uk/publications/multiparty-asynchronous-session-types/) — Research group and publications
- [ALPS Specification](http://alps.io/spec/drafts/draft-01.html) — Application-Level Profile Semantics
- [SCXML W3C Recommendation](https://www.w3.org/TR/scxml/) — State Chart XML standard
- [SHACL](https://www.w3.org/TR/shacl/) — Shapes Constraint Language
- [PROV-O](https://www.w3.org/TR/prov-o/) — Provenance Ontology
- [Frank.Statecharts](STATECHARTS.md) — Application-level state machines
- [Semantic Resources](SEMANTIC-RESOURCES.md) — Agent-legible application architecture
- [Spec Pipeline](SPEC-PIPELINE.md) — Bidirectional design spec pipeline
- [Comparison](COMPARISON.md) — Frank.Statecharts vs. Webmachine and Freya
