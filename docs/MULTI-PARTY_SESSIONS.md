# Multi-Party Sessions: Protocol Enforcement through Hypermedia

Frank does not implement session types in the type-theoretic sense. F# does not have the dependent or indexed type features that would make compile-time session type enforcement practical. Instead, Frank enforces multi-party protocol discipline at runtime through the composition of its existing layers: statecharts, guards, ALPS profiles, SHACL shapes, content negotiation, provenance, and the spec pipeline.

This is not merely an approximation of multi-party session types (MPSTs) with some gaps. Frank and formal MPST systems occupy overlapping but distinct design spaces. MPSTs provide static proof of safety and progress. Frank provides runtime enforcement, discoverability, auditability, reflective evolution, and open participation — properties that matter specifically because the trust boundary is HTTP and the participants may be unknown at design time.

This document maps the overlap, identifies what each approach provides that the other lacks, and describes the one significant gap in Frank’s coverage (explicit role projection) alongside the capabilities Frank offers that have no MPST equivalent.

## Background: What Multi-Party Session Types Guarantee

In the formal MPST literature (Honda, Yoshida, Carbone 2008; Scribble), a multi-party protocol is specified as a **global type** — a description of the complete interaction among all participants. The global type is then **projected** onto each role to produce a **local type**: the protocol as seen from one participant’s perspective.

The key guarantees are:

- **Safety**: no participant sends a message the protocol doesn’t expect at that point
- **Progress**: the protocol can always advance if all participants cooperate (no deadlocks)
- **Session fidelity**: the actual interaction conforms to the specified protocol
- **Role separation**: each participant sees only the actions relevant to their role

Static MPST systems enforce these at compile time. Frank enforces them at the HTTP boundary through middleware, with verification support from the spec pipeline. But Frank also provides guarantees that MPST systems do not address — discoverability, auditability, and reflective evolution among them.

## The Overlap: Where Frank and MPSTs Correspond

|MPST Concept                 |Frank Layer                            |Mechanism                                                                                                   |
|-----------------------------|---------------------------------------|------------------------------------------------------------------------------------------------------------|
|Global type                  |Frank.Statecharts                      |SCXML statechart defining all states, transitions, and events across all roles                              |
|Local type (per-role view)   |ALPS profile + role projection         |Per-role ALPS profile derived from global SCXML + role definitions (see [Role Projection](#role-projection))|
|Role                         |ASP.NET Core authentication            |`ClaimsPrincipal` with role claims (e.g., `player=X`, `player=O`)                                           |
|Message type (payload schema)|Frank.Validation                       |SHACL shapes constraining request and response bodies                                                       |
|Send action                  |ALPS `unsafe` / `idempotent` transition|POST/PUT/PATCH/DELETE with SHACL-validated payload                                                          |
|Receive action               |ALPS `safe` transition                 |GET returning state-appropriate representation                                                              |
|Protocol state               |`IStateMachineStore`                   |Persisted per-instance state determining available transitions                                              |
|Safety (no invalid sends)    |Guards + state-dependent routing       |Middleware returns 405 (method unavailable in state) or 403/409 (guard blocked)                             |
|Session fidelity             |Spec pipeline                          |Extract spec from running app, compare against design spec                                                  |
|Progress (liveness)          |Cross-validator + projection analysis  |Static check on global type + projections for deadlock-free states                                          |

## What Frank Provides That MPSTs Do Not

### Provenance and Auditability

MPSTs guarantee conformance by construction — if it compiles, it follows the protocol. But they do not record what happened. There is no trace. Once the interaction completes, the only evidence of protocol conformance is the fact that the program compiled and ran without a type error.

Frank’s PROV-O layer (`Frank.Provenance`, built on `onTransition` observers) records every state transition: who triggered it, when, from what state, to what state, with what event. This creates an auditable history of the actual interaction that can be queried after the fact.

This matters for:

- **Compliance**: demonstrating that an interaction followed the specified protocol
- **Debugging**: tracing how a resource reached an unexpected state
- **Agent reasoning**: an agent can inspect the provenance graph to understand not just what the current state is, but how the resource arrived there
- **Post-hoc conformance checking**: even if a bug allowed a protocol violation, the provenance record captures it rather than silently proceeding

A formal MPST system can tell you violations are impossible. Frank can tell you exactly what did happen.

### Runtime Discoverability

MPST systems assume all participants are compiled against a shared protocol definition. Every participant knows the protocol before the interaction begins. The protocol is a build-time artifact, not a runtime one.

Frank’s semantic layer means the protocol is discoverable at runtime by participants who have no prior knowledge of it. An agent with HTTP tool use can:

1. `OPTIONS /` to discover supported media types
1. Follow `Link` headers to the discovery endpoint
1. `GET /` with `Accept: application/alps+json` to retrieve the ALPS profile
1. `GET /` with `Accept: application/scxml+xml` to retrieve the statechart definition
1. Inspect SHACL shapes to understand payload constraints
1. Begin participating in the protocol

No shared code, no shared type definitions, no compile-time coupling. The protocol is self-describing at the HTTP boundary. MPSTs have no equivalent to this — they are closed-world systems where participants must be compiled against the protocol definition.

### Late Binding and Open Participation

MPSTs typically bind roles to participants at session initiation, and the set of participants is fixed for the session’s lifetime. The session channel is established between specific processes, and replacing a participant requires session delegation (which is itself a protocol extension with its own typing rules).

Frank enforces the protocol per-request against whoever presents the right credentials. The statechart and guards do not care about connection identity — only about claims. A player can disconnect, reconnect from a different client, switch devices, or even be replaced by another user who holds the same role claim. The protocol survives participant mobility because the enforcement is stateless with respect to connection identity.

This is not an accidental property — it follows directly from HTTP’s request/response model and Frank’s decision to enforce at the HTTP boundary rather than at the channel/process boundary.

### Observation Without Participation

In Frank, any agent can `GET` a resource and observe its state without being a protocol participant. A spectator watching a tic-tac-toe game, an admin monitoring system state, or an LLM agent evaluating the application can all read the resource’s current representation.

The projected ALPS profile for an unauthenticated or observer-role agent would contain only `safe` transitions — pure read access. The observer sees the protocol unfolding without having send obligations.

MPSTs do not typically model observers. Every party in the global type is an active participant with send and receive obligations. Pure observation requires either modeling the observer as a degenerate role (which pollutes the protocol definition) or operating outside the session type system entirely.

### Incremental Constraint Coverage

SHACL validation does not need to be exhaustive. The SEMANTIC-RESOURCES.md design document explicitly states this: “even partial SHACL coverage adds value for agent-driven analysis.”

You can add shapes incrementally — validate the critical fields first, add more constraints as the application matures. The protocol is not invalidated by incomplete constraint coverage; it is strengthened by each new shape.

MPST message types are all-or-nothing. The payload type is part of the protocol definition and must be complete for the type system to check it. Adding or changing a field in a message type is a protocol change that requires re-projection and re-compilation of all participants.

### Reflective Evolution

The feedback loop from <SEMANTIC-RESOURCES.md> — build → deploy → reflect → refine → rebuild — means the protocol itself evolves based on runtime observation:

```
Running Frank app serves semantic model
    → Agent queries model, identifies gap
        (e.g., "makeMove transition has no SHACL constraint on position range")
    → Agent proposes refinement
    → Developer implements SHACL shape for position
    → Updated app serves updated model
    → Agent verifies the constraint is present
    → Cycle continues
```

The application participates in its own evolution. The protocol is not a static specification frozen at design time — it is a living artifact that grows through the reflection/refinement loop.

MPSTs are static specifications. Once the global type is defined, projected, and compiled, the protocol does not participate in its own improvement. Changing the protocol means editing the Scribble definition, re-projecting, and recompiling all participants.

### Cross-Application Interop Without Shared Definitions

Because Frank’s protocol surface is expressed in durable standards (HTTP, RDF, ALPS, SCXML, SHACL), two independently developed Frank applications can interoperate if they share vocabulary — without sharing code or type definitions. An ALPS profile from one application can reference semantic descriptors from another via standard URIs.

MPSTs require a shared Scribble (or equivalent) definition compiled into every participant. They are closed-world systems by design. Cross-system interop requires explicit protocol composition, which is an active research area with limited practical tooling.

### Transport Semantics

HTTP provides caching, conditional requests, ETags, content negotiation, idempotency semantics, and range requests — all of which Frank’s statecharts can leverage. The ETag-from-state issue (#93 in the spec pipeline) is a concrete example: the protocol state directly informs HTTP caching behavior, so clients and intermediaries can make correct caching decisions based on the resource’s domain state.

MPST systems operate over abstract channels that have no concept of caching, conditional requests, or content negotiation. These transport-level properties must be either ignored or reimplemented at the application layer.

## What MPSTs Provide That Frank Does Not (Yet)

### Explicit Role Projection

This is the significant gap. Currently, the global protocol (SCXML statechart) exists as a single artifact. Each role’s view of the protocol is an emergent property of guards and state-dependent handler registration — it works correctly at runtime, but there is no formal artifact representing “what Player X can do at each protocol state.” The projection is implicit, spread across guard predicates, `inState` blocks, and claims checks.

In MPST theory, the projection operator takes a global type and a role name and produces a local type — a complete description of what that participant can send, receive, and observe at each point in the protocol. That local type is what enables static verification: you check each role’s implementation against its projected local type independently.

Making projection explicit closes the gap and unlocks several capabilities that Frank currently lacks.

#### The Projection Operator

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

#### Per-Role ALPS Profile Example

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

#### Projection via Content Negotiation

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

#### Where Projection Lives in the Architecture

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

At build time, the spec pipeline generates all per-role profiles and validates them. At request time, the content negotiation layer filters the ALPS response based on the authenticated user’s role and the resource’s current state.

### Progress Analysis

The other MPST guarantee that Frank does not currently provide is **progress** (liveness): the assurance that the protocol can always advance if all participants cooperate. Deadlock and starvation are properties of the protocol design, not of individual requests — they cannot be detected by middleware that handles one request at a time.

Frank can support progress analysis as a static check in the spec pipeline:

#### Deadlock Detection

A state is a **deadlock** if no role has an available transition. Given the global SCXML and role projections, enumerate all reachable states and verify that at least one role has at least one available transition in each non-final state.

#### Starvation Detection

A role is **starved** if there exists a reachable execution path where that role is permanently unable to act — the protocol continues, but only through other roles’ actions, and the starved role never regains an available transition. This requires path analysis on the projected state machines.

#### Implementation

Both analyses operate on the typed ASTs from the spec pipeline parsers. The cross-validator (#91) could include a `--check-progress` flag:

```
frank validate --check-progress game.scxml --roles PlayerX,PlayerO
```

This is a build-time check, not a runtime enforcement mechanism. It tells the developer whether their protocol design guarantees progress before any code is written.

## Session Establishment

The tic-tac-toe SCXML models two parallel regions: **GamePlay** (turn progression) and **PlayerIdentity** (role assignment). Issue [panesofglass/tic-tac-toe#12](https://github.com/panesofglass/tic-tac-toe/issues/12) recommends folding PlayerIdentity into `GameContext` data rather than modeling it as a separate state region.

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

Notably, MPSTs also struggle with session establishment — the standard theory assumes roles are bound before the protocol begins. Session initiation protocols and dynamic role assignment are active research areas in the MPST community. Frank’s per-request enforcement model, where role binding is just a claims check, sidesteps much of this complexity.

## Full Comparison

|Property                     |Formal MPSTs (Scribble, etc.)            |Frank                                                                              |
|-----------------------------|-----------------------------------------|-----------------------------------------------------------------------------------|
|**When checked**             |Compile time (static)                    |Mix: build-time (spec pipeline) + request-time (middleware) + post-hoc (provenance)|
|**Global type**              |Scribble protocol definition             |SCXML statechart                                                                   |
|**Projection**               |Automatic, type-level                    |Derived from SCXML + role defs → per-role ALPS profiles                            |
|**Safety**                   |Type errors for protocol violations      |403/405/409 HTTP responses for protocol violations                                 |
|**Progress**                 |Checked by projection algorithm          |Static analysis via spec pipeline cross-validator                                  |
|**Message typing**           |Payload types in protocol definition     |SHACL shapes (incremental, partial coverage valid)                                 |
|**Session fidelity**         |Guaranteed by construction               |Verified by spec extraction + comparison; auditable via PROV-O                     |
|**Auditability**             |None — conformance by construction       |Full provenance graph via PROV-O                                                   |
|**Discoverability**          |None — compile-time coupling required    |Runtime self-description via ALPS, RDF, content negotiation                        |
|**Participant binding**      |Fixed at session initiation              |Per-request claims evaluation; participants can change                             |
|**Observation**              |Not modeled                              |Any agent can GET; observer role via projected ALPS                                |
|**Constraint evolution**     |Protocol change requires recompilation   |Incremental SHACL shapes; reflective refinement loop                               |
|**Cross-application interop**|Shared Scribble definition required      |Shared ALPS vocabulary sufficient                                                  |
|**Transport semantics**      |Abstract channels                        |HTTP caching, ETags, content negotiation, idempotency                              |
|**Enforcement boundary**     |Process/channel boundary                 |HTTP request/response boundary                                                     |
|**Language support required**|Type system extensions or code generation|None — standard F#, standard HTTP                                                  |

Frank trades compile-time completeness for **deployment-time verifiability** and **runtime enforcement at the network boundary**, while gaining capabilities — provenance, discoverability, reflective evolution, open participation — that static MPST systems do not address. For HTTP applications where the trust boundary is between client and server, and where participants may be unknown at design time, this is the appropriate set of tradeoffs.

## Integration with Existing Documents

This document connects to the other design documents as follows:

- **<STATECHARTS.md>** provides the global type: the `StateMachine<'State, 'Event, 'Context>` definition and the `statefulResource` CE that enforces state-dependent behavior.
- **<SEMANTIC-RESOURCES.md>** provides the self-description mechanism: ALPS profiles, RDF models, and content negotiation are the substrate for serving projected local types to authenticated agents. The reflective evolution loop is what gives Frank’s protocol enforcement its ability to improve over time.
- **<SPEC-PIPELINE.md>** provides the verification loop: the cross-validator can be extended with projection consistency checks and progress analysis.
- **<COMPARISON.md>** clarifies why Frank operates at the application level rather than the protocol level — the same reasoning applies here. Multi-party session enforcement belongs at the application/domain layer, not in a reimplementation of HTTP.

## Implementation Priorities

The layers that already exist (statecharts, guards, SHACL, ALPS, provenance) provide the runtime safety guarantees and the capabilities that go beyond what MPSTs offer. The missing piece — explicit role projection — can be built incrementally:

1. **Role definition schema**: a declarative way to map authentication claims to protocol roles, associated with a `statefulResource`. This is the foundation everything else depends on.
1. **Projection operator**: given the SCXML + role definitions + ALPS descriptors, derive per-role ALPS profiles. This can be a build-time tool in the spec pipeline initially.
1. **Projected content negotiation**: serve the projected ALPS profile to authenticated agents based on their role and the resource’s current state. This extends `Frank.LinkedData`’s content negotiation.
1. **Cross-validator extensions**: add projection consistency checks and progress analysis to the spec pipeline’s `frank validate` command.
1. **Session establishment patterns**: document the Option A / Option B patterns and provide examples for both simple (context data) and complex (separate protocol phase) establishment scenarios.

## Propositions as Sessions: The Logical Foundation

The operational MPST framework (Honda, Yoshida, Carbone 2008) describes session types as a process algebra — rules for well-typed communication. Wadler’s "Propositions as Sessions" (2012, 2014) establishes a deeper foundation: session types correspond to propositions in classical linear logic via the Curry-Howard correspondence. Proofs are processes, propositions are session types, and cut elimination is communication.

This is not merely a theoretical reframing. The logical foundation reveals structure that the operational framework leaves implicit, and that structure has direct consequences for Frank’s design — particularly for client protocol derivation, composability, and the clef-lang port.

### Duality and Client Protocol Derivation

In Wadler’s framework, every session type has a **dual**. If the server’s type says "in state S, offer actions {A, B}" then the client’s dual says "in state S, select from {A, B}." The dual is not a separate artifact — it is *structurally determined* by the server’s type.

Frank’s ALPS profiles describe the server’s offerings. The **dual** of an ALPS profile is the client’s protocol — the precise set of HTTP operations an agent may perform at each protocol state. Currently, agents discover this at runtime through OPTIONS requests and ALPS content negotiation. With explicit duality, the client protocol can be **derived** from the server’s profile before the first request.

This is distinct from role projection (#107). Projection gives per-role *server-side* views: "what does the server offer to PlayerX in state XTurn?" Duality gives the *client-side* obligations: "given what the server offers, what is the complete set of valid client interaction sequences?"

For agentic clients, this is the difference between:

- **Discovery mode**: Request ALPS profile → parse available actions → choose one → request again. Each step is an independent discovery.
- **Derivation mode**: Given the server’s session type (SCXML + ALPS), derive the client’s dual — a complete protocol specifying all valid request sequences to accomplish a goal. The agent follows the derived protocol, falling back to discovery only when the protocol diverges from expectation.

Discovery mode is Frank’s strength for open-world participation. Derivation mode adds a **pre-computed fast path** for agents that know the protocol in advance. Both modes coexist — the derived protocol is the same information as runtime discovery, just computed ahead of time.

#### Example: Tic-Tac-Toe Client Dual

Given the server’s projected ALPS for PlayerX:

| Server State | Server Offers (ALPS) | Client Dual (Derived Protocol) |
|---|---|---|
| `XTurn` | `makeMove(position)`, `viewGame` | MUST select `makeMove` or `viewGame`; if goal is "win", select `makeMove` |
| `OTurn` | `viewGame` | MAY `viewGame` (poll); MUST wait for state change |
| `Won(X)` | `viewGame` | Session complete; `viewGame` for confirmation |

The dual tells the agent not just what it *can* do (that’s the ALPS profile) but what it *must* do to advance the protocol. An agent that holds the dual can plan a complete game strategy as a sequence of typed interactions.

### Cut Elimination as Protocol Composition

In linear logic, the **cut rule** connects two proofs on a shared proposition — one proves A, the other consumes A. Cut elimination guarantees the connection terminates and the composed proof is well-typed.

For Frank, cut corresponds to **composing two protocol participants on a shared resource**. If service A’s output (an ALPS profile it produces) matches service B’s input (an ALPS profile it consumes), their composition is protocol-correct by construction. This formalizes what Frank’s middleware composition does operationally: the `plug` operation composes middlewares, and the resulting pipeline is correct if each middleware’s contract is satisfied.

This matters for:

- **Multi-service choreography**: If service A transitions a resource to state S and service B’s projected profile expects state S, the handoff is cut-safe.
- **Spec pipeline validation**: Cross-format validation (#91) could verify that two services’ ALPS profiles are dual-compatible — one’s sends match the other’s receives.
- **Agent orchestration**: An agent coordinating multiple Frank services could verify that its orchestration plan is cut-safe before executing.

### Linearity and HTTP

Linear logic’s core property is that every resource is used **exactly once**. HTTP’s request/response model is inherently linear: each request produces exactly one response, and neither can be replayed (ignoring caching, which is a controlled relaxation of linearity with explicit invalidation semantics via ETags and Cache-Control).

Frank’s statechart transitions are linear: each event is consumed exactly once and produces exactly one state transition. The `TransitionResult` type (with `StateChanged` / `NoChange` / `InvalidTransition` outcomes) is a linear return — the event is consumed regardless of outcome.

The tension is at the implementation boundary. `HttpContext.Items` stores state in a mutable dictionary with `box`/`:?>` casts — the opposite of linear discipline. The closures that capture generic types recover safety by inspection, not by construction (as the Wadler review notes). This is an unavoidable consequence of ASP.NET Core’s untyped middleware pipeline, not a design choice.

### Session Types Across Target Platforms

This document’s opening statement — "F# does not have the dependent or indexed type features that would make compile-time session type enforcement practical" — identifies the constraint for the F# implementation. But Frank’s multi-platform vision means session type enforcement will be evaluated on three additional platforms, each with different affordances. Ordered by immediacy:

#### CloudflareFS: External State Makes Linearity Visible

CloudflareFS runs F# on Cloudflare Workers — no long-running server, no in-memory state. Every state access crosses a network boundary to KV, Durable Objects, or D1. This changes the session type story fundamentally:

- **The `obj` boundary moves outward.** In ASP.NET Core, the `obj` boxing happens inside the process at `HttpContext.Items`. On Workers, the equivalent boxing happens at the KV/Durable Object API boundary. The linearity violation becomes a *network call* — far more expensive and visible than an in-process cast.
- **External state demands explicit contracts.** When state is remote, the contract between "read state, evaluate guard, perform transition, write state" must account for concurrency and latency. Session types formalize this: each state access is a typed interaction with the state store, and the session type guarantees the interaction sequence is valid.
- **Durable Objects are natural session hosts.** A Durable Object is a single-threaded, addressable actor with persistent state — structurally similar to a session channel. Each Durable Object instance could host one protocol session, with incoming HTTP requests as session messages. The Durable Object’s single-threaded guarantee provides linearity that Workers (stateless, multi-instance) cannot.
- **Guards must tolerate latency.** Frank’s guard evaluation assumes low-latency state access. With 50ms state fetches, guard evaluation that requires multiple state reads becomes a performance concern. Session types that encode which guards apply in which states could pre-compute guard evaluation paths, reducing round-trips.

CloudflareFS still runs F#, so the type system constraints are identical to the main implementation. The session type benefits are operational (formal contracts with external state stores) rather than type-level.

#### BEAM (Elixir + Gleam): Session Types as OTP Patterns

The BEAM runtime provides several properties that MPST systems must normally prove or enforce, making it the most natural fit for session type enforcement:

- **Processes give linearity for free.** An Erlang/Elixir process has a single mailbox. Messages are received by exactly one process and consumed exactly once. This is the linear channel that Wadler’s framework requires — not simulated, but built into the runtime.
- **GenServer IS a session-typed process.** A GenServer’s `handle_call`/`handle_cast` callbacks define the messages it accepts, and pattern matching on the current state determines which messages are valid. This is a runtime encoding of a local session type: "in state S, accept messages {A, B}; in state T, accept messages {C}."
- **Supervision gives progress.** OTP supervision trees guarantee that crashed participants are restarted. If a participant in a multi-party protocol fails, the supervisor can restart it (and potentially replay its session from a checkpoint). This is an operational progress guarantee that static MPST systems prove but never enforce at runtime.
- **Gleam’s type system can encode session types.** Gleam compiles to BEAM but has a strict, ML-family type system with generics and exhaustive pattern matching. While Gleam does not have linear types, its type system is strict enough to encode session type discipline as phantom types or indexed state machines — more than Elixir’s dynamic types allow, less than full linear type enforcement.
- **PubSub enables observation without participation.** Phoenix.PubSub allows any process to observe state changes without being a protocol participant — directly mapping to Frank’s "observation without participation" advantage over traditional MPSTs.

The BEAM port should model each `statefulResource` as a GenServer (or GenStateMachine) where:
- The process state is `(ProtocolState * Context)`
- `handle_call` dispatches on `(Event * ProtocolState)` — invalid combinations return an error tuple
- Guard evaluation happens inside the process (single-threaded, no concurrency concern)
- The ALPS profile is derived from the GenServer’s message type + current state
- Supervision handles participant failure and recovery

This is not a reimplementation of session types on BEAM — it is a recognition that BEAM already provides the operational substrate. Frank’s contribution is the semantic layer (ALPS, SCXML, SHACL, content negotiation) that makes BEAM’s session-like properties discoverable and machine-readable at the HTTP boundary.

#### clef-lang: Session Types in the Type System

clef-lang’s MLIR-based compiler, designed from scratch with F#-like syntax, is the long-term opportunity to make session type enforcement a compile-time guarantee. Wadler’s work identifies exactly what’s needed: **linear types** and **session type duality** in the type system.

If clef-lang incorporates linear types at the IR level:

- Protocol violations become **compile-time errors**, not runtime 403/405/409 responses
- The `obj` boundary disappears — linear channels replace `HttpContext.Items` / KV stores / GenServer mailboxes
- Session type duality is checked by the compiler — the client dual is not a derived artifact but a type-level guarantee
- Guard composition becomes monadic rather than list-based — the type system enforces that guards form a lawful monoid

clef-lang also targets BEAM, so it inherits the operational linearity of processes while adding static linearity checking. This is the best of both worlds: runtime enforcement (BEAM process isolation) plus compile-time verification (linear session types).

#### Summary: Platform Progression

| Property | F# / ASP.NET Core | CloudflareFS | BEAM (Elixir/Gleam) | clef-lang |
|---|---|---|---|---|
| Linearity | Violated at `obj` boundary | Violated at KV/DO boundary | Provided by process mailboxes | Enforced by type system |
| Session typing | Runtime middleware | Runtime middleware + external state contracts | Runtime GenServer + Gleam phantom types | Compile-time linear types |
| Progress | Spec pipeline static analysis | Same | OTP supervision + static analysis | Compile-time proof + OTP supervision |
| State isolation | `HttpContext.Items` (shared dict) | Durable Objects (per-instance actor) | Process (single-threaded mailbox) | Linear channel (compiler-verified) |
| Guard latency | Microseconds (in-process) | ~50ms (network) | Microseconds (in-process) | Microseconds (in-process) |

This does not mean the F# implementation is wrong. It means F# is the proving ground for concepts that gain stronger enforcement properties on platforms designed for them. The portable concepts (statechart-enforced HATEOAS, ALPS-as-local-type, projection, duality) transfer unchanged; only the enforcement mechanism improves at each step.

### Algebraic Structure: The Implementation Gap

Wadler’s type-theoretic lens highlights missing algebraic structure in Frank’s F# implementation that limits composability:

| Component | Current Shape | Algebraic Gap | Consequence |
|---|---|---|---|
| `TransitionResult` | `Either`-like DU | No `map` / `bind` | Cannot compose transition outcomes without pattern matching |
| Guards | `(string * GuardFn) list` | No monoid (identity + composition) | Guards compose by list concatenation + first-match-wins, not algebraically |
| Handlers | `(string * RequestDelegate) list` | No functor / composition operator | Handler selection is linear scan, not composed dispatch |
| HTTP methods | `string` | No type-level encoding | Method constraints are runtime string comparisons |

Adding `map`/`bind` to `TransitionResult` and a monoid instance to guards would enable:

- Composing sub-protocols into larger protocols (the operational equivalent of cut)
- Property-based testing of guard composition laws
- A foundation for ports: type-class instances on clef-lang, protocol implementations on BEAM/Gleam, same algebraic laws everywhere

This is a portable concept improvement, not an F#-specific optimization — the algebraic structure translates directly to any target platform. On BEAM, guard composition maps to GenServer callback dispatch; on clef-lang, it maps to type-class instances with compiler-verified laws.

## References

### Operational MPST

- Honda, Yoshida, Carbone (2008) — [Multiparty Asynchronous Session Types](https://doi.org/10.1145/1328438.1328472) (POPL ‘08)
- [Scribble](http://www.scribble.org/) — Protocol description language for multiparty session types
- [MPST at Imperial College](https://mrg.doc.ic.ac.uk/publications/multiparty-asynchronous-session-types/) — Research group and publications

### Propositions as Sessions (Wadler)

- Wadler (2012) — [Propositions as Sessions](https://doi.org/10.1145/2364527.2364568) (ICFP ‘12) — Curry-Howard correspondence between classical linear logic and session types
- Wadler (2014) — [Propositions as Types](https://doi.org/10.1145/2699407) (Communications of the ACM) — Accessible overview of the propositions-as-types correspondence including sessions
- Lindley & Morris (2015) — [A Semantics for Propositions as Sessions](https://doi.org/10.1007/978-3-662-46669-8_23) (ESOP ‘15) — Operational semantics for GV (functional session types)
- Gay & Vasconcelos (2010) — [Linear Type Theory for Asynchronous Session Types](https://doi.org/10.1017/S0956796809990268) (JFP) — Linear types for async sessions

### Standards

- [ALPS Specification](http://alps.io/spec/drafts/draft-01.html) — Application-Level Profile Semantics
- [SCXML W3C Recommendation](https://www.w3.org/TR/scxml/) — State Chart XML standard
- [SHACL](https://www.w3.org/TR/shacl/) — Shapes Constraint Language
- [PROV-O](https://www.w3.org/TR/prov-o/) — Provenance Ontology
- [JSON-LD](https://www.w3.org/TR/json-ld11/) — JSON for Linking Data

### Frank Design Documents

- [Frank.Statecharts](STATECHARTS.md) — Application-level state machines
- [Semantic Resources](SEMANTIC-RESOURCES.md) — Agent-legible application architecture
- [Spec Pipeline](SPEC-PIPELINE.md) — Bidirectional design spec pipeline
- [Comparison](COMPARISON.md) — Frank.Statecharts vs. Webmachine and Freya
