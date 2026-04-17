# Frank Statecharts Architecture

**Status**: Living document
**Authors**: Ryan Riley, with architectural consultation

-----

## Executive Summary

Frank provides stateful HTTP resources backed by Harel statecharts. Resources expose state-dependent affordances, validate inputs against state-dependent SHACL shapes, serve RDF representations via content negotiation, emit PROV-O audit graphs derived from an event-sourced journal, and enforce multi-party protocols through mechanically derived role projections.

The architecture rests on seven load-bearing decisions:

1. **Separate types per concern.** Statecharts, validation, provenance, and linked data each own their types in their own packages. Nothing is “embedded” — concerns compose through explicit bindings, not unified ASTs.
1. **Three algebras with disjoint carriers.** `IStatechartBuilder<'r>` constructs structure, `ITransitionAlgebra<'r>` interprets transition behavior, `IConfigurationPredicate<'r>` evaluates predicates over active configurations. No algebra mixes carriers.
1. **Journal-sourced runtime.** `IStatechartJournal` is the source of truth; in-memory `AgentState` is a derived projection. Snapshots cache; they do not persist. PROV-O is derived from the journal by a dedicated bridge package that keeps HTTP provenance and statechart provenance independently usable.
1. **Predicate-keyed bindings.** Affordances, handlers, and validation attach to `ConfigurationPredicate`s, not single state IDs. Parallel regions compose correctly; single-state cases stay concise.
1. **Derived role projections.** Following multi-party session types, per-role views are mechanically projected from a statechart plus a `RoleParticipation` schema. Hand-authored overrides are an analyzer-warned escape hatch.
1. **First-class guards and timeouts.** Guards use a closed expression language. Scheduled events are first-class actions backed by a durable `IStatechartScheduler`.
1. **SCXML conformance.** The macrostep algorithm is a direct transliteration of the W3C SCXML Algorithm for SCXML Interpretation. Where Frank deviates, the deviation is documented and tested.

The architecture is built **depth-first**. Each layer ships complete against its conformance suite before the layer above is started. No layer makes simplifying assumptions that the layer above will inherit as constraints.

-----

## Table of Contents

1. [Problem Statement](#1-problem-statement)
1. [Theoretical Foundations](#2-theoretical-foundations)
1. [Architectural Decisions](#3-architectural-decisions)
1. [Rejected Alternatives](#4-rejected-alternatives)
1. [Package Structure](#5-package-structure)
1. [Type Specifications](#6-type-specifications)
1. [Algebra Specifications](#7-algebra-specifications)
1. [Runtime Specifications](#8-runtime-specifications)
1. [Composition Model](#9-composition-model)
1. [Multi-Party Projections](#10-multi-party-projections)
1. [Build Plan](#11-build-plan)
1. [Conformance and Falsifiability](#12-conformance-and-falsifiability)
1. [References](#13-references)
1. [Appendices](#14-appendices)

-----

## 1. Problem Statement

### 1.1 Goals

Frank aims to provide:

1. **Stateful HTTP resources** — Resources whose behavior changes based on application state, with state expressed as a Harel statechart.
1. **Self-describing APIs** — Agents and humans discover capabilities at runtime via state-dependent affordances.
1. **Protocol enforcement** — Invalid state transitions are structurally impossible; multi-party protocols cannot deadlock or starve.
1. **Audit and provenance** — Full, durable traceability of state changes via PROV-O, surviving process restart.
1. **Semantic richness** — RDF/Linked Data representations, ALPS profiles, SHACL validation.
1. **Multi-party correctness** — Per-role views are derivable from a global protocol; the global protocol is checkable for progress, deadlock-freedom, and role-completeness.

### 1.2 Constraints

1. **Harel + SCXML fidelity.** Operational semantics match the W3C SCXML algorithm. Where SCXML and Harel-1987 diverge, SCXML wins (SCXML is the implementation contract; Harel is the conceptual contract).
1. **Durability.** Statechart agents recover their state after process restart without replaying business logic side effects.
1. **Hierarchy and parallelism are first-class.** Compound states, parallel (AND) regions, history (shallow and deep), and joins/forks across regions all work correctly. No piece of the design silently assumes a flat or non-parallel statechart.
1. **F# idioms.** Computation expressions, immutability, type safety, exhaustive pattern matching.
1. **ASP.NET Core integration.** Works with existing middleware patterns.
1. **Independent utility.** `Statecharts.*` packages work without Frank or HTTP. Frank packages work without statecharts where the concern is independent.
1. **Reusability.** Statechart runtime is reusable outside Frank for non-HTTP contexts (CLIs, background workers, agent runtimes).

### 1.3 Prior Approaches Rejected

|Approach                                                               |Why it failed                                                                       |
|-----------------------------------------------------------------------|------------------------------------------------------------------------------------|
|Unified AST with all concerns embedded                                 |Created circular dependencies; couldn’t use validation without importing statecharts|
|Duplicate types in `Frank.Resources.Model` and `Frank.Statecharts.Core`|Maintenance burden; unclear which was canonical                                     |
|Ad-hoc runtime interpretation of AST                                   |No formal model of how concerns interact; hard to reason about combinations         |
|Pattern-matching interpreters over AST                                 |Verbose; adding new interpretations required modifying visitor functions            |
|Single algebra over uniform `'r` carrier                               |Conflated builders, transition behavior, and queries into one carrier               |
|In-memory `AgentState` as source of truth                              |Lost trace and history on restart; provenance store was disconnected                |
|`Map<StateId, _>` bindings                                             |Could not express affordances conditional on multiple parallel regions              |
|Hand-authored per-role projections                                     |No soundness check; scales poorly with role count; duplicates transition-level info |
|Open-ended guard expression strings                                    |Required embedded evaluator; defeated static analysis                               |

The common thread: each of these approaches made a simplifying assumption that the layer above inherited as a constraint. The architecture presented here resolves each one at its source.

-----

## 2. Theoretical Foundations

### 2.1 Harel Statecharts (1987) and SCXML (W3C 2015)

David Harel’s statecharts extend finite-state machines with hierarchy, orthogonality, broadcast, and history. The W3C SCXML recommendation gives an executable operational semantics for these features.

Where Harel and SCXML diverge, **SCXML is the implementation contract for Frank**. SCXML is unambiguous, has a reference algorithm, and is the lingua franca for tooling (statechart-io, XState importers, scion, etc.). Harel-1987 is the conceptual contract: when intuition and SCXML diverge, the architecture exposes the divergence rather than papering over it.

Features adopted in full:

|Feature         |Description                                     |Frank relevance                    |
|----------------|------------------------------------------------|-----------------------------------|
|Hierarchy       |States contain substates (OR-decomposition)     |Resource nesting, visibility levels|
|Orthogonality   |Parallel regions execute concurrently (AND)     |Multi-party protocols              |
|Broadcast       |Internal events propagate within same macrostep |Event-driven coordination          |
|History         |Shallow and deep history pseudostates           |Session resume, caching            |
|Guards          |Conditions enabling/disabling transitions       |Authorization, business rules      |
|Joins/forks     |Multi-source/multi-target transitions           |Cross-region synchronization       |
|Final states    |Region completion signals to enclosing parallel |Workflow completion semantics      |
|Internal events |Actions raise events processed in same macrostep|Reactive coordination              |
|Scheduled events|`<send delay="…">` for timeouts                 |Workflow timeouts                  |

Critical semantic properties:

1. **Configuration** — Maximal orthogonal set of active states. Global property, non-compositional.
1. **Macrostep** — Process external event plus all chained internal events to quiescence (no internal event pending).
1. **Microstep** — Single set of non-conflicting transitions fired together.
1. **Priority** — Inner transitions override outer; document order breaks ties.
1. **Conflict** — Two transitions conflict if their exit sets overlap. Conflict resolution by priority.

These semantics are not compositional in the structure of the statechart. You cannot compute `currentConfiguration` by folding over the AST. This justifies the agent-based runtime and disqualifies a pure tagless-final encoding for execution.

References:

- Harel, D. (1987). Statecharts: A Visual Formalism for Complex Systems. *SCP* 8(3).
- W3C (2015). State Chart XML (SCXML): State Machine Notation for Control Abstraction. https://www.w3.org/TR/scxml/

### 2.2 HMBS — Hypermedia Model Based on Statecharts (2001)

De Oliveira, Turine, and Masiero’s HMBS model formally connects statecharts to hypermedia:

```
Hip = ⟨ST, P, m, ae, N⟩
```

|Component     |Definition                        |Frank analog                                        |
|--------------|----------------------------------|----------------------------------------------------|
|`ST`          |Statechart structure              |`StatechartDocument`                                |
|`P`           |Set of pages (content units)      |HTTP handlers                                       |
|`m : Sₛ → P`  |State-to-page mapping             |`Handlers : (ConfigurationPredicate × Handler) list`|
|`ae : Anc → E`|Anchor-to-event mapping           |`Affordance.TransitionRef` (explicit, not by-name)  |
|`N`           |Visibility level (hierarchy depth)|Projection depth                                    |

Frank generalizes the mapping `m` from `Sₛ → P` (single state to page) to `ConfigPred → P` (predicate over configuration to page). This is the minimal change required to support parallel regions correctly while preserving the HMBS structure.

Reference: de Oliveira, M.C.F., Turine, M.A.S., & Masiero, P.C. (2001). A Statechart-Based Model for Hypermedia Applications. *ACM TOIS* 19(1).

### 2.3 Tagless-Final Encoding (2009)

Carette, Kiselyov, and Shan’s tagless-final approach: programs are polymorphic over an algebra interface, allowing multiple interpretations of the same program.

Tagless-final is used in Frank for *compositional* operations only — those whose result can be computed by folding over local structure. Three such operations exist, each with its own algebra:

1. **Construction** (`IStatechartBuilder<'r>`) — building structural fragments.
1. **Transition behavior** (`ITransitionAlgebra<'r>`) — interpreting what a transition does (Enter/Exit/RecordHistory/RaiseEvent).
1. **Configuration queries** (`IConfigurationPredicate<'r>`) — evaluating predicates over the active configuration.

Operations that are not compositional — macrostep execution, conflict detection, priority resolution — live in the runtime, not in algebras.

Limitations that shape the design:

1. Parsing into polymorphic programs is awkward. Parse into AST, then `reflect` to polymorphic form when interpretation is needed.
1. Inspection requires reification.
1. F# lacks type classes; interfaces stand in for them.

Reference: Carette, J., Kiselyov, O., & Shan, C. (2009). Finally Tagless, Partially Evaluated. *JFP* 19(5).

### 2.4 Multi-Party Session Types (Honda/Yoshida/Carbone 2008, 2016)

Multi-party session types (MPST) describe a global protocol from which per-role local protocols are mechanically derived by **projection**. Projection is sound by construction: if the global protocol is well-formed, projected roles compose to implement it without deadlock or orphan messages.

A statechart augmented with a role-participation schema — which role triggers which transition, which role observes which state — constitutes a global protocol approximation. Per-role projections are derivable. The MPST literature provides the well-formedness conditions to check on the global form.

References:

- Honda, K., Yoshida, N., & Carbone, M. (2008). Multiparty Asynchronous Session Types. *POPL 2008*.
- Honda, K., Yoshida, N., & Carbone, M. (2016). Multiparty Asynchronous Session Types. *JACM* 63(1).
- Deniélou, P.M., & Yoshida, N. (2012). Multiparty Session Types Meet Communicating Automata. *ESOP 2012*.

### 2.5 Event Sourcing as Runtime Substrate

The statechart agent is an event-sourced actor: the journal of events is the source of truth, in-memory state is a derived projection. This is the standard event-sourcing pattern (Fowler, 2005; Young, 2010), specialized to statechart events.

This substrate satisfies three requirements simultaneously:

1. Durability across process restart (replay journal on agent start).
1. Provenance audit trail (the journal *is* the audit trail; PROV-O is a view over it).
1. Snapshot consistency (snapshots are cached projections, validated by journal position).

Reference: Young, G. (2010). CQRS and Event Sourcing. https://cqrs.wordpress.com/

### 2.6 “Code as Model” (Azariah, 2025)

Azariah’s elevator example demonstrates that the same abstract program can be interpreted for production execution, verification, visualization, and auditing.

This principle applies to the *compositional* parts of Frank. Statechart **structure** (built via `IStatechartBuilder`) is interpretable as SCXML, ALPS, smcat, reachability graph, and more. Statechart **execution** is not; it lives in the runtime.

Reference: Azariah, J. (2025). Tagless Final in F# - Part 6: The Power of Tagless-Final: Code as Model.

-----

## 3. Architectural Decisions

### AD-1: Separate Types Per Concern

Each semantic concern (statecharts, validation, provenance, linked data) owns its type definitions in its own package.

Concerns are orthogonal — each has independent value. This avoids circular dependencies, lets users pay only for what they use, and aligns with the single-responsibility principle.

### AD-2: Three Algebras with Disjoint Carriers

Structural interpretation uses three algebras whose carriers are semantically distinct:

1. `IStatechartBuilder<'r>` — `'r` represents a statechart fragment (a state subtree or a transition set). Used for construction and structural interpretation (SCXML/ALPS/smcat generation, pretty printing, reachability).
1. `ITransitionAlgebra<'r>` — `'r` represents the effect of a transition (composed Enter/Exit/RecordHistory/RaiseEvent operations). Used for runtime state updates and code generation from SCXML.
1. `IConfigurationPredicate<'r>` — `'r` represents the truth value of a predicate over a configuration. Used for binding affordances/handlers/validation to configuration predicates and for symbolic analysis of satisfiability.

A single algebra over a uniform `'r` carrier would conflate incompatible concepts and force interpreters to inspect what kind of `'r` they have, defeating the point of tagless-final. Splitting eliminates the awkwardness and makes each algebra small and focused.

### AD-3: Journal-Sourced Runtime

`IStatechartJournal` is the source of truth for agent state. `AgentState` is a derived projection, recomputable by journal replay. `MailboxProcessor` serializes journal append plus projection update; it does not own the state.

Every `Fire` journal-appends before acknowledging. This is a synchronous I/O point; agents are not pure-memory-fast, but they are durable. Snapshots are a performance optimization (cache the projection at journal position N; validate cache by checking journal head). PROV-O records are derivable from the journal by a separate projection — no additional write path needed.

The journal interface is pluggable: in-memory (for tests), file-backed (single-node), database-backed, or NATS JetStream / Kafka (clustered).

### AD-4: AST and Algebra Bridged Per Algebra

Each algebra has its own reify/reflect bridge to a corresponding AST type. `StatechartDocument` (AST) ↔ `Statechart` (polymorphic `IStatechartBuilder` program). `TransitionEdge` ↔ `TransitionProgram` (polymorphic `ITransitionAlgebra` program). Configuration predicates have no separate polymorphic form because the AST is already small enough to evaluate directly.

Parsing produces AST. Interpretation uses algebra. Conversion is explicit.

### AD-5: Composition Model with Predicate Keys

Bindings (affordances, validation, handlers) are keyed on `ConfigurationPredicate`, not `StateId`. The composition model retains the extended HMBS tuple with this substitution.

```fsharp
type StatefulResourceBinding<'TEvent> = {
    Statechart: StatechartDocument
    Handlers: (ConfigurationPredicate * Handler<'TEvent>) list
    Affordances: (ConfigurationPredicate * Affordance list) list
    Validation: (ConfigurationPredicate * NodeShape) list option
    // ...
}
```

A `Map<StateId, _>` key would silently drop expressivity for parallel regions — the common case of “this affordance only applies when region A is in `Approved` AND region B is in `PaymentCleared`” would be inexpressible. A predicate key (`InState s`, `InAll [s; t]`, etc.) is the minimal correct generalization. Single-state cases stay concise: `whenIn "Draft" [...]` desugars to `InState (StateId "Draft")`.

Lookup is O(n × predicates) instead of O(log n) Map lookup, mitigated by predicate indexing on the leading `InState` term. Analyzers detect overlapping predicates (FRANK105) and unreachable predicates (FRANK205).

### AD-6: Role Projections Are Derived

Per-role projections are mechanically derived from the statechart plus a `RoleParticipation` schema. The schema specifies, per transition, the triggering role and observing roles. Manual `RoleOverride` exists as an escape hatch with analyzer warnings (FRANK210).

Authors specify role participation at the transition level (where the information naturally lives), not at every state. Projections are checked for completeness (every role has a defined view of every reachable configuration), progress (every role can eventually act), and deadlock-freedom. The schema is itself a first-class artifact — exportable as a multi-party protocol description.

Hand-authored projections were a correctness hazard at scale: no soundness check, every projection a place a bug could hide, duplication of transition-level information across per-state entries.

### AD-7: Statecharts Package Is Independent

`Statecharts.Core`, `Statecharts.Runtime`, `Statecharts.Parsers`, `Statecharts.Generators`, and `Statecharts.Multiparty` have no dependencies on Frank, ASP.NET Core, or HTTP concepts. They are reusable for non-HTTP contexts: CLI tools, background services, agent runtimes, test harnesses that replay journals offline.

Integration concepts (handlers, affordances, middleware) live in `Frank.Statecharts`.

### AD-8: Closed Guard Language

Guards use a closed expression language, not arbitrary strings:

```fsharp
type Guard =
    | True | False
    | InState of StateId
    | InAny of StateId list
    | EventDataEquals of path: string * value: Value
    | VariableEquals of name: VariableName * value: Value
    | Compare of op: CompareOp * left: GuardExpr * right: GuardExpr
    | And of Guard * Guard
    | Or of Guard * Guard
    | Not of Guard
```

A closed form is statically analyzable, comparable for equivalence, and evaluable without an embedded language runtime. SCXML import maps `cond="…"` expressions into the closed form; expressions outside the form fail at import time with clear errors. Analyzer rules reason about guard satisfiability symbolically.

An open extension point (`Custom of obj`) is reserved for future work if needed; v1 ships closed.

### AD-9: Scheduled Events Are First-Class

SCXML’s `<send delay="…">` and `<cancel>` are first-class. Scheduled events are journaled at the moment they are scheduled, with the target firing time. The runtime includes a scheduler that fires them; firing is itself journaled.

Half of realistic HTTP workflows have timeouts. Deferring them forces every adopter to invent their own scheduler, which then doesn’t compose with snapshots, projections, or audit.

`IStatechartScheduler` interface has implementations: in-memory (tests), file-backed (single-node), database-backed (multi-process). Scheduled events are durable across restart because they are journaled. Cancellation is journaled; the scheduler reconciles by skipping cancelled-then-fired events.

### AD-10: Macrostep Matches W3C SCXML Algorithm Exactly

The macrostep implementation is a direct transliteration of the SCXML Algorithm for SCXML Interpretation (W3C SCXML Appendix D). Where Frank deviates, the deviation is documented and tested.

SCXML conformance provides a published test suite (W3C SCXML 1.0 Implementation Report) and tooling interoperability. The runtime specification in §8 includes pseudocode that maps line-by-line to SCXML Appendix D.

-----

## 4. Rejected Alternatives

### 4.1 Unified AST

All concerns in one type:

```fsharp
type FrankDocument = {
    States: StateNode list
    Transitions: TransitionEdge list
    Shapes: NodeShape list
    Affordances: Affordance list
    Provenance: ProvenanceConfig
    Roles: RoleDefinition list
}
```

Rejected because: creates dependency on all concerns when using any; adding a new concern requires modifying the core type; subset use carries dead fields; violates single responsibility.

### 4.2 Pure Tagless-Final Without AST

No AST, only polymorphic programs.

Rejected because: parsing into polymorphic functions is awkward in F#; no way to serialize or checkpoint; analyzers need pattern matching; round-tripping is impossible.

### 4.3 Denotational Semantics for Runtime

Statechart as function from event traces to configuration traces.

Rejected because: denotational semantics for statecharts are complex; history and broadcast are notoriously hard to model; loses operational intuition; academic elegance over practical utility.

### 4.4 Role-Aware Interpreters

Every algebra carries a role parameter.

Rejected because: combinatorial explosion with other filters; violates single responsibility; projection is a separate concern from formatting.

### 4.5 Coupled Statecharts and HTTP

Statechart types carrying HTTP handlers.

Rejected because: prevents non-HTTP reuse; testing requires HTTP infrastructure; violates dependency direction (stable below, volatile above).

### 4.6 In-Memory `AgentState` as Source of Truth

Agent holds state in memory; `Snapshot`/`Restore` for persistence.

Rejected because: loses trace data on restart; disconnects provenance store from agent; cannot tolerate partial failure (crash between state change and external publication); forces every adopter to invent durability.

Replaced by AD-3 journal-sourced runtime.

### 4.7 Single `IStatechartAlgebra`

One algebra over a uniform `'r` carrier, handling states, transitions, and documents simultaneously.

Rejected because: conflates structure, behavior, and queries into one carrier; forces an awkward `combine: 'r -> 'r -> 'r` operation; standard interpreters must inspect what kind of `'r` they have, defeating the point of tagless-final.

Replaced by AD-2 three algebras with disjoint carriers.

### 4.8 `Map<StateId, _>` Bindings

Single-state keys for affordances, handlers, validation.

Rejected because: cannot express affordances/handlers conditional on multiple parallel regions being active; silently incorrect for any non-trivial workflow.

Replaced by AD-5 predicate-keyed bindings.

### 4.9 Hand-Authored Role Projections

`RoleProjection` as a primary authoring artifact.

Rejected because: no soundness check; every projection is a place a bug can hide; scales poorly with role count; duplicates information that lives more naturally at the transition level.

Replaced by AD-6 derived projections from `RoleParticipation` schema.

### 4.10 Open-Ended Guard Strings

`Expression of string` as guard syntax.

Rejected because: requires an embedded expression evaluator; defeats static analysis; leaks into every analyzer rule.

Replaced by AD-8 closed guard language.

-----

## 5. Package Structure

### 5.1 Dependency Graph

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  LAYER 0: Statechart Formalism (FSharp.Core only)                           │
│                                                                             │
│  ┌─────────────────┐                                                        │
│  │ Statecharts     │  AST, three algebras, ConfigurationPredicate,          │
│  │ .Core           │  Guard, Action, parse-result types                     │
│  └────────┬────────┘                                                        │
│           │                                                                 │
│  ┌────────┴────────┐  Journal interface, Scheduler interface,               │
│  │ Statecharts     │  StatechartAgent, SCXML algorithm,                     │
│  │ .Runtime        │  in-memory journal/scheduler implementations           │
│  └────────┬────────┘                                                        │
│           │                                                                 │
│  ┌────────┴────────┐                                                        │
│  │ Statecharts     │  WSD, SCXML, smcat parsers → AST                       │
│  │ .Parsers        │                                                        │
│  └────────┬────────┘                                                        │
│           │                                                                 │
│  ┌────────┴────────┐                                                        │
│  │ Statecharts     │  AST → SCXML, XState, smcat, ALPS, mermaid             │
│  │ .Generators     │                                                        │
│  └────────┬────────┘                                                        │
│           │                                                                 │
│  ┌────────┴────────┐                                                        │
│  │ Statecharts     │  RoleParticipation, projection algorithm,              │
│  │ .Multiparty     │  global well-formedness checks                         │
│  └─────────────────┘                                                        │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│  LAYER 0.5: Sibling Concerns (FSharp.Core only)                             │
│                                                                             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐              │
│  │ Validation      │  │ Provenance      │  │ LinkedData      │              │
│  │ .Core           │  │ .Core           │  │ .Core           │              │
│  └─────────────────┘  └────────┬────────┘  └─────────────────┘              │
│                                │                                            │
│                       ┌────────┴────────┐                                   │
│                       │ Statecharts     │  Journal → PROV-O derivation      │
│                       │ .Provenance     │  (deps: Statecharts.Core +        │
│                       │                 │   Provenance.Core)                │
│                       └─────────────────┘                                   │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│  LAYER 1: Frank HTTP Sibling Integrations (+ ASP.NET Core)                  │
│                                                                             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐              │
│  │ Frank           │  │ Frank           │  │ Frank           │              │
│  │ .LinkedData     │  │ .Validation     │  │ .Provenance     │              │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘              │
│                                                                             │
│  ┌─────────────────┐                                                        │
│  │ Frank           │  Static ALPS, Link headers, OPTIONS                    │
│  │ .Discovery      │                                                        │
│  └─────────────────┘                                                        │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│  LAYER 2: Statechart Integration                                            │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ Frank.Statecharts                                                   │    │
│  │  • StatefulResourceBinding (predicate-keyed composition)            │    │
│  │  • StatefulResourceBuilder CE                                       │    │
│  │  • State-dependent affordances, validation, provenance              │    │
│  │  • Derived role projections                                         │    │
│  │  • Middleware (useAffordances, useStatecharts, useRoleProjection)   │    │
│  │  • Persistent journal/scheduler for ASP.NET Core hosting            │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ Frank.Statecharts.Analyzers                                         │    │
│  │  • FRANK101–108: Structural validation                              │    │
│  │  • FRANK201–212: Semantic validation (reachability, deadlock,       │    │
│  │                  predicate overlap, role completeness)              │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 5.2 Package Descriptions

|Package                      |Dependencies                                          |Responsibility                                       |
|-----------------------------|------------------------------------------------------|-----------------------------------------------------|
|`Statecharts.Core`           |FSharp.Core                                           |AST, three algebras, predicates, guards, actions     |
|`Statecharts.Runtime`        |Statecharts.Core                                      |Journal, Scheduler, Agent, SCXML algorithm           |
|`Statecharts.Parsers`        |Statecharts.Core                                      |WSD/SCXML/smcat → AST                                |
|`Statecharts.Generators`     |Statecharts.Core                                      |AST → SCXML/XState/smcat/ALPS/mermaid                |
|`Statecharts.Multiparty`     |Statecharts.Core                                      |RoleParticipation, projection, well-formedness       |
|`Validation.Core`            |FSharp.Core                                           |SHACL types, shape algebra                           |
|`Provenance.Core`            |FSharp.Core                                           |PROV-O types, provenance algebra                     |
|`LinkedData.Core`            |FSharp.Core                                           |RDF types, graph algebra                             |
|`Statecharts.Provenance`     |Statecharts.Core, Provenance.Core                     |Journal → PROV-O graph derivation                    |
|`Frank.LinkedData`           |LinkedData.Core, Frank                                |Content negotiation middleware                       |
|`Frank.Validation`           |Validation.Core, Frank                                |Request/response validation middleware               |
|`Frank.Provenance`           |Provenance.Core, Frank                                |HTTP request audit middleware (statechart-unaware)   |
|`Frank.Discovery`            |Frank                                                 |Static ALPS, Link headers, OPTIONS                   |
|`Frank.Statecharts`          |Statecharts.*, Statecharts.Provenance, siblings, Frank|Composition model, CE, middleware, persistent journal|
|`Frank.Statecharts.Analyzers`|Statecharts.Core                                      |Compile-time validation                              |

### 5.3 Composition Matrix

Each Frank.* middleware package is independently usable on a plain Frank resource. Any combination composes on the same resource through ASP.NET Core’s middleware pipeline and, when `Frank.Statecharts` is present, through `StatefulResourceBinding`’s optional fields.

Each Frank.* package delivers standalone value:

|Package            |Standalone deliverable                                                        |
|-------------------|------------------------------------------------------------------------------|
|`Frank.Validation` |SHACL validation on request and response bodies. No statecharts required.     |
|`Frank.LinkedData` |Content negotiation across RDF representations (Turtle, JSON-LD, N-Triples).  |
|`Frank.Provenance` |HTTP-level audit: who called what, when, with what status. Statechart-unaware.|
|`Frank.Discovery`  |Static ALPS document, Link headers, and OPTIONS responses computed at startup.|
|`Frank.Statecharts`|Stateful resources with predicate-keyed bindings and derived role projections.|

Pairwise combinations describe the combined capability when both packages are installed on the same resource:

|               |LinkedData                |Provenance          |Discovery                |Statecharts                                      |
|---------------|--------------------------|--------------------|-------------------------|-------------------------------------------------|
|**Validation** |Validate in, negotiate out|Validated + audited |Validated + discoverable |State-dependent SHACL                            |
|**LinkedData** |—                         |Audit in RDF formats|RDF + ALPS + Link headers|State-dependent RDF views                        |
|**Provenance** |—                         |—                   |Audited + discoverable   |Journal-derived PROV-O                           |
|**Discovery**  |—                         |—                   |—                        |Static ALPS + dynamic affordances (complementary)|
|**Statecharts**|—                         |—                   |—                        |—                                                |

Static ALPS (from `Frank.Discovery`) and state-dependent affordances (from `Frank.Statecharts`) are deliberately complementary rather than overlapping: static ALPS describes the protocol at design time; state-dependent affordances describe what a client can do right now at runtime. Adopters use both, not one or the other.

Journal-derived PROV-O is the richer provenance mode: when `Frank.Statecharts` and `Frank.Provenance` are both present, `Statecharts.Provenance` produces Entity-per-configuration, Activity-per-transition, and Derivation-per-state-change records from the journal. `Frank.Provenance` alone continues to handle HTTP-level audit. The two modes coexist on one resource.

### 5.4 Usage Profiles

Four profiles cover the common adoption paths:

1. **Plain REST with validation** — `Frank.Validation` only. Useful when the primary value is enforcing request/response shapes on an otherwise stateless HTTP API.
1. **Agent-legible REST** — `Frank.Validation` + `Frank.LinkedData` + `Frank.Discovery`. Resources negotiate across RDF representations, advertise ALPS profiles, and validate inputs against SHACL. No state machine, no audit.
1. **Stateful workflow with audit** — `Frank.Statecharts` + `Frank.Provenance` (+ `Statecharts.Provenance` transitively). Multi-party workflows with derived role projections and journal-derived PROV-O. Content negotiation optional.
1. **Full semantic stack** — all five Frank.* packages. Self-describing, self-validating, self-auditing stateful resources that negotiate across representations and enforce multi-party protocols. This is the target for Frank’s v7.3.0 semantic-resources vision.

An adopter moves between profiles without refactoring: adding `Frank.Statecharts` to a profile-2 deployment is a dependency change and a CE invocation, not a rewrite. This is the payoff of AD-1 and the independent-utility constraint in §1.2.

-----

## 6. Type Specifications

### 6.1 Statecharts.Core — Structure

```fsharp
namespace Statecharts.Core

/// State decomposition type (Harel's ψ function)
[<RequireQualifiedAccess>]
type StateType =
    | Basic         // Leaf state, no children
    | Compound      // OR-decomposition: exactly one child active
    | Parallel      // AND-decomposition: all children active concurrently
    | Final         // Terminal state of an enclosing region

/// History pseudostate type
[<RequireQualifiedAccess>]
type HistoryKind =
    | Shallow       // Remember immediate child only
    | Deep          // Remember full sub-configuration

/// Strongly-typed identifiers
type StateId = StateId of string
type TransitionId = TransitionId of string
type EventId = EventId of string
type VariableName = VariableName of string

/// Closed value type for guards and event data
type Value =
    | VString of string
    | VInt of int64
    | VFloat of double
    | VBool of bool
    | VNull

/// Guard expression — closed form (AD-8)
type Guard =
    | True
    | False
    | InState of StateId
    | InAny of StateId list
    | EventDataEquals of path: string * value: Value
    | VariableEquals of name: VariableName * value: Value
    | Compare of op: CompareOp * left: GuardExpr * right: GuardExpr
    | And of Guard * Guard
    | Or of Guard * Guard
    | Not of Guard

and CompareOp = Lt | Le | Eq | Ne | Ge | Gt

and GuardExpr =
    | GLiteral of Value
    | GVariable of VariableName
    | GEventData of path: string

/// Action — executed on entry, exit, or transition
type Action =
    | Raise of EventId                                  // Internal event (same macrostep)
    | Send of event: EventId * delay: TimeSpan option   // External; delay → scheduler
    | Cancel of sendId: string                          // Cancel a scheduled send
    | Assign of target: VariableName * value: GuardExpr
    | Log of label: string * value: GuardExpr
    | Custom of string                                  // Reserved extension point

/// Hierarchical state node (Harel's S with ρ structure)
type StateNode = {
    Id: StateId
    Type: StateType
    Children: StateNode list
    /// For Compound states: the default child (δ function). Required.
    /// For Parallel/Basic/Final: must be None.
    Initial: StateId option
    /// History pseudostates as children (modeled separately because they don't
    /// participate in active configurations).
    History: HistoryDefinition list
    OnEntry: Action list
    OnExit: Action list
}

and HistoryDefinition = {
    Id: StateId
    Kind: HistoryKind
    /// Default target if no history exists yet.
    DefaultTarget: StateId
}

/// Transition between states
type TransitionEdge = {
    Id: TransitionId
    /// Source states. Single for normal transitions, multiple for joins.
    Sources: StateId list
    /// Triggering event. None means eventless (always-active when source active).
    Event: EventId option
    Guard: Guard
    /// Target states. Single for normal, multiple for forks. Empty for internal
    /// transitions that fire actions without changing configuration.
    Targets: StateId list
    Actions: Action list
    /// SCXML "internal" type: do not exit/re-enter the source's compound parent.
    Internal: bool
}

/// Complete statechart document
type StatechartDocument = {
    Id: string
    Root: StateNode
    Transitions: TransitionEdge list
    /// Datamodel: variable declarations with initial values.
    Datamodel: (VariableName * Value) list
    /// Document order is the source-text order of states; used for SCXML priority.
    DocumentOrder: StateId list
}

/// Active configuration: maximal orthogonal set of active states
type Configuration = Set<StateId>

/// Predicate over configurations (AD-5)
type ConfigurationPredicate =
    | InState of StateId
    | InAll of StateId list
    | InAnyOf of StateId list
    | NotIn of StateId
    | PAnd of ConfigurationPredicate * ConfigurationPredicate
    | POr of ConfigurationPredicate * ConfigurationPredicate
    | PNot of ConfigurationPredicate
    | PTrue
    | PFalse

module ConfigurationPredicate =
    /// Evaluate a predicate against a configuration. Total, no failure cases.
    val eval : pred: ConfigurationPredicate -> config: Configuration -> bool
    /// Set of state IDs referenced. Used for indexing.
    val referencedStates : ConfigurationPredicate -> Set<StateId>
    /// Structural equivalence (not satisfiability).
    val equivalent : ConfigurationPredicate -> ConfigurationPredicate -> bool

/// Parse result
type ParseResult<'T> =
    | Success of 'T
    | Failure of ParseError list

and ParseError = {
    Message: string
    Location: SourceLocation option
    Severity: ErrorSeverity
}

and SourceLocation = { Line: int; Column: int; Source: string option }
and ErrorSeverity = Info | Warning | Error
```

### 6.2 Statecharts.Runtime — Journal, Scheduler, Agent

```fsharp
namespace Statecharts.Runtime

open Statecharts.Core

// ═══════════════════════════════════════════════════════════════════════════
// Journal (AD-3): the source of truth for agent state
// ═══════════════════════════════════════════════════════════════════════════

/// A single journal entry. Append-only, monotonically positioned.
type JournalEntry = {
    Position: int64
    Timestamp: DateTimeOffset
    Kind: JournalEntryKind
}

and JournalEntryKind =
    | EventReceived of EventId * data: Value option * source: EventSource
    | TransitionsFired of TransitionId list * fromConfig: Configuration * toConfig: Configuration
    | ActionsExecuted of Action list
    | InternalEventQueued of EventId * data: Value option
    | ScheduledEventAdded of sendId: string * EventId * data: Value option * fireAt: DateTimeOffset
    | ScheduledEventCancelled of sendId: string
    | ScheduledEventFired of sendId: string * EventId
    | VariableAssigned of VariableName * Value
    | HistoryRecorded of historyStateId: StateId * Configuration
    | ExternalSnapshotTaken of position: int64
    | ExternalRestorePerformed of fromPosition: int64
    | RuntimeError of message: string * recoverable: bool

and EventSource =
    | External                       // From outside the agent (Fire call)
    | Internal                       // Generated by Raise within a macrostep
    | Scheduled of sendId: string    // From the scheduler
    | Replay                         // During journal replay

/// Journal interface. Implementations: in-memory, file, database, NATS JetStream.
type IStatechartJournal =
    /// Append entries atomically. Returns the position of the first entry.
    abstract Append : entries: JournalEntryKind list -> Async<int64>
    /// Read entries from a given position (inclusive) to head.
    abstract ReadFrom : position: int64 -> Async<JournalEntry list>
    /// Current head position.
    abstract Head : unit -> Async<int64>
    /// Optional: persistent snapshot for fast recovery.
    abstract TrySnapshot : unit -> Async<SnapshotRef option>
    abstract WriteSnapshot : position: int64 * state: AgentState -> Async<SnapshotRef>

and SnapshotRef = { Position: int64; Reference: string }

// ═══════════════════════════════════════════════════════════════════════════
// Scheduler (AD-9): durable scheduled events
// ═══════════════════════════════════════════════════════════════════════════

type IStatechartScheduler =
    /// Schedule an event to fire at the given time. Returns sendId for cancellation.
    abstract Schedule : event: EventId * data: Value option * fireAt: DateTimeOffset -> Async<string>
    /// Cancel a previously scheduled event. Idempotent if already fired.
    abstract Cancel : sendId: string -> Async<unit>
    /// Subscribe to events as they fire. The agent installs one subscriber per instance.
    abstract Subscribe : handler: (string * EventId * Value option -> Async<unit>) -> IDisposable

// ═══════════════════════════════════════════════════════════════════════════
// Agent state — derived projection of the journal
// ═══════════════════════════════════════════════════════════════════════════

type AgentState = {
    /// Position in the journal that this state reflects.
    Position: int64
    Configuration: Configuration
    /// History memory: for each history pseudostate, the configuration to restore.
    History: Map<StateId, Configuration>
    Variables: Map<VariableName, Value>
    /// Internal event queue (within-macrostep broadcast).
    InternalQueue: (EventId * Value option) list
    /// Active scheduled sends.
    ScheduledSends: Map<string, EventId * DateTimeOffset>
}

// ═══════════════════════════════════════════════════════════════════════════
// Agent interface
// ═══════════════════════════════════════════════════════════════════════════

/// Result of firing an external event.
type FireResult =
    | Transitioned of TransitionResult
    | NoTransition
    | Blocked of BlockReason
    | RuntimeError of message: string

and TransitionResult = {
    JournalRangeStart: int64
    JournalRangeEnd: int64
    FromConfiguration: Configuration
    ToConfiguration: Configuration
    TransitionsFired: TransitionId list
    InternalEventsProcessed: EventId list
}

and BlockReason =
    | NoEnabledTransitions
    | GuardFailedAll
    | InvalidEvent of EventId

/// Queries against current state (derived from journal).
type StateQuery =
    | IsActive of StateId
    | CurrentConfiguration
    | EnabledTransitionsFor of EventId
    | HistoryOf of StateId
    | VariableValue of VariableName
    | Position

type QueryResult =
    | QBool of bool
    | QConfig of Configuration
    | QTransitions of TransitionId list
    | QValue of Value option
    | QPosition of int64

type IStatechartAgent =
    abstract Fire : event: EventId * data: Value option -> Async<FireResult>
    abstract Query : query: StateQuery -> Async<QueryResult>
    abstract State : unit -> Async<AgentState>
    /// Force replay from journal. Clears in-memory cache.
    abstract Reload : unit -> Async<unit>

module StatechartAgent =
    /// Construct an agent. Replays the journal to derive initial AgentState.
    val create :
        document: StatechartDocument *
        journal: IStatechartJournal *
        scheduler: IStatechartScheduler ->
        Async<IStatechartAgent>
```

### 6.3 Validation.Core

```fsharp
namespace Validation.Core

/// SHACL property path
type PropertyPath =
    | Direct of string
    | Inverse of PropertyPath
    | Sequence of PropertyPath list
    | Alternative of PropertyPath list

/// SHACL constraint
type Constraint =
    | MinCount of int
    | MaxCount of int
    | Datatype of string
    | Class of string
    | NodeKind of NodeKind
    | Pattern of regex: string * flags: string option
    | MinLength of int
    | MaxLength of int
    | MinInclusive of decimal
    | MaxInclusive of decimal
    | MinExclusive of decimal
    | MaxExclusive of decimal
    | In of string list
    | HasValue of string
    | Equals of PropertyPath
    | Disjoint of PropertyPath
    | LessThan of PropertyPath
    | QualifiedValueShape of shape: NodeShape * min: int * max: int option

and NodeKind = IRI | BlankNode | Literal | BlankNodeOrIRI | BlankNodeOrLiteral | IRIOrLiteral

and PropertyShape = {
    Path: PropertyPath
    Name: string option
    Description: string option
    Constraints: Constraint list
}

and NodeShape = {
    Id: ShapeId
    TargetClass: string option
    TargetNode: string option
    Properties: PropertyShape list
    Closed: bool
    IgnoredProperties: PropertyPath list
}

and ShapeId = ShapeId of string

type ValidationResult =
    | Conforms
    | Violations of Violation list

and Violation = {
    FocusNode: string
    Path: PropertyPath option
    Value: string option
    Constraint: Constraint
    Message: string
    Severity: Severity
}

and Severity = Info | Warning | Violation
```

### 6.4 Provenance.Core

```fsharp
namespace Provenance.Core

type Entity = {
    Id: EntityId
    GeneratedAtTime: DateTimeOffset option
    InvalidatedAtTime: DateTimeOffset option
    Value: obj option
}

and EntityId = EntityId of string

type Activity = {
    Id: ActivityId
    StartedAtTime: DateTimeOffset
    EndedAtTime: DateTimeOffset option
    Type: string option
}

and ActivityId = ActivityId of string

type Agent = {
    Id: AgentId
    Name: string option
    Type: AgentType
}

and AgentId = AgentId of string
and AgentType = Person | Organization | SoftwareAgent

type Derivation = {
    GeneratedEntity: EntityId
    UsedEntity: EntityId
    Activity: ActivityId option
    Type: DerivationType
}

and DerivationType =
    | Derivation
    | Revision
    | Quotation
    | PrimarySource

type Attribution = { Entity: EntityId; Agent: AgentId }
type Association = { Activity: ActivityId; Agent: AgentId; Role: string option }
type Usage = { Activity: ActivityId; Entity: EntityId; AtTime: DateTimeOffset option }
type Generation = { Entity: EntityId; Activity: ActivityId; AtTime: DateTimeOffset option }
type Delegation = { DelegateAgent: AgentId; Responsible: AgentId; Activity: ActivityId option }

type ProvenanceGraph = {
    Entities: Entity list
    Activities: Activity list
    Agents: Agent list
    Derivations: Derivation list
    Attributions: Attribution list
    Associations: Association list
    Usages: Usage list
    Generations: Generation list
    Delegations: Delegation list
}
```

### 6.5 Statecharts.Multiparty — Role Participation

```fsharp
namespace Statecharts.Multiparty

open Statecharts.Core

type RoleId = RoleId of string

/// How a role relates to a transition.
type ParticipationKind =
    | Triggers     // This role initiates the transition (sends the event).
    | Observes     // This role sees the transition occur (receives notification).
    | Forbidden    // This role is explicitly denied this transition.

/// Per-transition role participation.
type TransitionParticipation = {
    TransitionId: TransitionId
    Triggers: Set<RoleId>      // Usually exactly one, but can be a group.
    Observes: Set<RoleId>
    Forbidden: Set<RoleId>
}

/// Per-state role visibility.
type StateVisibility = {
    StateId: StateId
    /// Roles that can observe this state being active. If empty, all roles see it.
    VisibleTo: Set<RoleId>
}

/// The role participation schema for a statechart.
type RoleParticipation = {
    Roles: Set<RoleId>
    Transitions: Map<TransitionId, TransitionParticipation>
    States: Map<StateId, StateVisibility>
}

/// A derived per-role local view.
type LocalProjection = {
    RoleId: RoleId
    /// States this role observes. Subset of statechart states.
    VisibleStates: Set<StateId>
    /// Transitions this role can trigger.
    TriggerableTransitions: Set<TransitionId>
    /// Transitions this role observes (without triggering).
    ObservableTransitions: Set<TransitionId>
}

/// Well-formedness conditions on the global protocol.
type WellFormednessIssue =
    | UnreachableForRole of RoleId * StateId
    | NoProgressForRole of RoleId * configuration: Configuration
    | TransitionWithoutTrigger of TransitionId
    | ConflictingParticipation of TransitionId * RoleId * triggers: bool * forbidden: bool
    | OrphanRole of RoleId  // Role declared but never referenced

module Projection =
    /// Mechanically derive a per-role projection from the global statechart + schema.
    val project :
        document: StatechartDocument *
        schema: RoleParticipation *
        role: RoleId ->
        LocalProjection

    /// Project all roles at once.
    val projectAll :
        document: StatechartDocument *
        schema: RoleParticipation ->
        Map<RoleId, LocalProjection>

    /// Check the global protocol for well-formedness.
    val checkWellFormedness :
        document: StatechartDocument *
        schema: RoleParticipation ->
        WellFormednessIssue list
```

### 6.6 Statecharts.Provenance — Journal → PROV-O Derivation

This package sits at Layer 0.5 and bridges two otherwise-independent concerns: the statechart journal (a runtime primitive) and PROV-O graphs (a provenance primitive). It depends on `Statecharts.Core` and `Provenance.Core`; it does not depend on `Frank`, `Statecharts.Runtime` beyond the journal types, or `Frank.Provenance`.

The package exists so that `Frank.Provenance` remains statechart-unaware and the journal-to-PROV-O mapping is reusable from CLI tools, background workers, and test harnesses that replay journals offline.

```fsharp
namespace Statecharts.Provenance

open Statecharts.Core
open Statecharts.Runtime
open Provenance.Core

/// Configuration for deriving PROV-O from a statechart journal.
type DerivationOptions = {
    /// Base IRI for generated PROV-O identifiers. Entity/Activity/Agent IDs are
    /// minted relative to this base.
    BaseIri: string
    /// The software agent identifier for the runtime itself (the Activity's
    /// wasAssociatedWith target when no human agent is attributed).
    RuntimeAgent: Agent
    /// Whether to emit an Entity per configuration (richer graph) or only per
    /// externally-observed state (smaller graph). Default: per configuration.
    EntityGranularity: EntityGranularity
    /// Whether to include scheduled-event firings as distinct Activities.
    /// Default: true.
    IncludeScheduledEvents: bool
}

and EntityGranularity =
    | PerConfiguration      // Entity for every distinct configuration reached
    | PerExternalEvent      // Entity only at macrostep boundaries

/// Sink abstraction: where derived PROV-O records are written.
/// Implementations in other packages (in-memory, SPARQL endpoint, file, etc.).
type IProvenanceSink =
    abstract Append : graph: ProvenanceGraph -> Async<unit>
    abstract Query : sparql: string -> Async<ProvenanceGraph>

module Derivation =
    /// Derive a PROV-O graph from a range of journal entries.
    /// Pure function: same inputs always produce the same graph.
    val derive :
        options: DerivationOptions *
        entries: JournalEntry list ->
        ProvenanceGraph

    /// Derive and append incrementally. Intended for live agents:
    /// called after each successful Fire with the new journal range.
    val deriveAndAppend :
        options: DerivationOptions *
        sink: IProvenanceSink *
        entries: JournalEntry list ->
        Async<unit>

    /// Full replay: derive the complete PROV-O graph for an entire journal.
    /// Intended for offline analysis and CLI tools.
    val replay :
        options: DerivationOptions *
        journal: IStatechartJournal ->
        Async<ProvenanceGraph>
```

Mapping from journal entries to PROV-O:

|Journal entry kind            |PROV-O records produced                                                                                                                                   |
|------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------|
|`EventReceived (e, d, src)`   |Entity (the event); Usage (Activity `used` this Entity)                                                                                                   |
|`TransitionsFired (ts, c, c')`|Activity (the macrostep); Entity (new configuration); Generation (Entity `wasGeneratedBy` Activity); Derivation (new config `wasDerivedFrom` prior config)|
|`ActionsExecuted actions`     |Entities for any `Assign` targets; Derivation chains                                                                                                      |
|`ScheduledEventAdded …`       |Activity of type “schedule”; Entity (the pending send)                                                                                                    |
|`ScheduledEventFired …`       |Entity invalidation for the pending-send Entity; new Event entity                                                                                         |
|`ScheduledEventCancelled …`   |Entity invalidation; no firing Activity                                                                                                                   |
|`VariableAssigned (n, v)`     |Entity for the variable at its new value; Derivation from prior value                                                                                     |
|`HistoryRecorded (h, cfg)`    |Entity for the history snapshot; Attribution to the runtime agent                                                                                         |
|`ExternalSnapshotTaken p`     |Annotation only; no new Entity                                                                                                                            |
|`RuntimeError …`              |Activity ended with `prov:wasInvalidatedBy`                                                                                                               |

Attribution to human agents (via `wasAttributedTo`) happens at the Frank.Statecharts layer, where `ProvenanceOptions.AgentExtractor` turns `HttpContext` into an `Agent`. `Statecharts.Provenance` is HTTP-unaware; the runtime agent is always the software agent unless Frank.Statecharts injects a human agent before calling `deriveAndAppend`.

### 6.7 Frank.Statecharts — Composition Model

```fsharp
namespace Frank.Statecharts

open Statecharts.Core
open Statecharts.Runtime
open Statecharts.Multiparty
open Validation.Core
open Provenance.Core

/// Affordance definition with explicit transition reference (AD-5).
type Affordance = {
    Rel: string
    Href: string
    Method: HttpMethod option
    Accepts: string list
    Title: string option
    /// Explicit reference to the transition this affordance triggers, if any.
    TransitionRef: TransitionId option
}

/// HTTP handler bound to a configuration predicate.
type Handler<'TEvent> = HttpContext -> Task<unit>

/// Manual role override (escape hatch with analyzer warning, AD-6).
type RoleOverride = {
    RoleId: RoleId
    /// Predicates where this role gains additional affordances beyond the projection.
    ExtraAffordances: (ConfigurationPredicate * Affordance list) list
    /// Affordances to hide from this role.
    HiddenAffordanceRels: (ConfigurationPredicate * string list) list
}

/// Provenance configuration. The journal is the source; this configures derivation.
type ProvenanceOptions = {
    AgentExtractor: HttpContext -> Agent
    /// Whether to include read operations as PROV-O Activities.
    TrackReads: bool
    /// Sink for derived PROV-O records. Often the same backing store as the journal.
    Sink: IProvenanceSink
}

/// The composition model: extended HMBS tuple, predicate-keyed.
type StatefulResourceBinding<'TEvent> = {
    /// ST: statechart structure
    Statechart: StatechartDocument

    /// P + m: predicate-conditioned handler bindings (AD-5)
    Handlers: (ConfigurationPredicate * Handler<'TEvent>) list

    /// ae: predicate-conditioned affordance bindings
    Affordances: (ConfigurationPredicate * Affordance list) list

    /// N: visibility level for hierarchy projection
    VisibilityLevel: int

    /// State-dependent validation, predicate-conditioned
    Validation: (ConfigurationPredicate * NodeShape) list option

    /// Provenance: configuration for deriving PROV-O from the journal
    Provenance: ProvenanceOptions option

    /// Multi-party role participation. Projections derived from this (AD-6).
    RoleParticipation: RoleParticipation option

    /// Manual role overrides. Analyzer warns if used (FRANK210).
    RoleOverrides: RoleOverride list

    /// Event mapping function: domain events → SCXML EventId
    EventMapper: 'TEvent -> EventId * Value option

    /// Resource path template
    Path: string

    /// Resource name for discovery
    Name: string
}

/// Result of projecting for a role at a configuration.
type ProjectedView = {
    Role: RoleId
    Configuration: Configuration
    /// Affordances visible to this role at this configuration.
    Affordances: Affordance list
    /// Validation shapes applicable.
    Validation: NodeShape list
    /// Subset of configuration visible to this role.
    VisibleConfiguration: Configuration
    /// Transitions this role can trigger from this configuration.
    EnabledTransitions: TransitionId list
}
```

-----

## 7. Algebra Specifications

### 7.1 IStatechartBuilder<’r> — Construction (AD-2)

```fsharp
namespace Statecharts.Core

/// Algebra for building statechart structure.
/// Carrier 'r represents a structural fragment (state subtree or transition collection).
type IStatechartBuilder<'r> =
    // ── State construction ──────────────────────────────────────────────
    abstract basic : id: StateId * onEntry: Action list * onExit: Action list -> 'r
    abstract compound :
        id: StateId *
        initial: StateId *
        children: 'r list *
        history: HistoryDefinition list *
        onEntry: Action list *
        onExit: Action list ->
        'r
    abstract parallel_ :
        id: StateId *
        regions: 'r list *
        onEntry: Action list *
        onExit: Action list ->
        'r
    abstract final : id: StateId * onEntry: Action list -> 'r

    // ── Transition construction ─────────────────────────────────────────
    abstract transition : t: TransitionEdge -> 'r

    // ── Document construction ───────────────────────────────────────────
    abstract document :
        id: string *
        root: 'r *
        transitions: 'r list *
        datamodel: (VariableName * Value) list ->
        'r

/// A polymorphic statechart program.
[<Struct>]
type Statechart = { Run: IStatechartBuilder<'a> -> 'a }

module Statechart =
    /// Interpret a polymorphic statechart with a given algebra.
    val interpret : alg: IStatechartBuilder<'a> -> sc: Statechart -> 'a

    /// Reflect an AST to polymorphic form.
    val reflect : doc: StatechartDocument -> Statechart

    /// Reify a polymorphic program to AST.
    val reify : sc: Statechart -> StatechartDocument
```

Standard interpreters, each in its own module:

```fsharp
namespace Statecharts.Core.Interpreters

/// Build the AST.
type ReifyAlgebra = ...               // IStatechartBuilder<ASTFragment>

/// Pretty-print to indented text.
type PrettyPrintAlgebra = ...          // IStatechartBuilder<string>

/// Collect all state IDs.
type StateCollectorAlgebra = ...       // IStatechartBuilder<Set<StateId>>

/// Compute reachability graph.
type ReachabilityAlgebra = ...         // IStatechartBuilder<ReachabilityGraph>

/// Generate SCXML.
type SCXMLAlgebra = ...                // IStatechartBuilder<XElement>

/// Generate ALPS profile.
type ALPSAlgebra = ...                 // IStatechartBuilder<ALPSDocument>

/// Generate mermaid stateDiagram-v2.
type MermaidAlgebra = ...              // IStatechartBuilder<string>

/// Generate smcat (state-machine-cat).
type SmcatAlgebra = ...                // IStatechartBuilder<string>
```

### 7.2 ITransitionAlgebra<’r> — Transition Behavior

```fsharp
namespace Statecharts.Core

/// Algebra for interpreting what a transition does at runtime.
/// Carrier 'r is the effect representation (e.g., AgentState -> AgentState,
/// or a free-monad-style command list, or a code-generation output).
type ITransitionAlgebra<'r> =
    /// Exit a state (run OnExit actions, remove from configuration).
    abstract exit : state: StateId -> 'r
    /// Enter a state (run OnEntry actions, add to configuration).
    abstract enter : state: StateId -> 'r
    /// Record history at a given history pseudostate.
    abstract recordHistory : historyState: StateId * snapshot: Configuration -> 'r
    /// Restore from history (or default if no history yet).
    abstract restoreHistory : historyState: StateId -> 'r
    /// Execute an action.
    abstract execute : action: Action -> 'r
    /// Raise an internal event for processing in this macrostep.
    abstract raise_ : event: EventId * data: Value option -> 'r
    /// Sequence two effects.
    abstract sequence : 'r * 'r -> 'r
    /// No-op.
    abstract noop : 'r

/// A polymorphic transition program.
[<Struct>]
type TransitionProgram = { Run: ITransitionAlgebra<'a> -> 'a }
```

Standard interpreters:

```fsharp
namespace Statecharts.Core.Interpreters

/// Interpret transitions as state updates (used by the runtime).
type StateUpdateAlgebra = ...          // ITransitionAlgebra<AgentState -> AgentState * Action list>

/// Generate F# code for transitions.
type FSharpCodeGenAlgebra = ...        // ITransitionAlgebra<string>

/// Generate trace entries (used for provenance derivation).
type TraceAlgebra = ...                // ITransitionAlgebra<JournalEntryKind list>
```

### 7.3 IConfigurationPredicate<’r> — Predicate Evaluation

```fsharp
namespace Statecharts.Core

/// Algebra for evaluating predicates over configurations.
/// Carrier 'r is the predicate result type (typically bool, but could be
/// a satisfying configuration set, or a symbolic constraint).
type IConfigurationPredicate<'r> =
    abstract inState : StateId -> 'r
    abstract inAll : StateId list -> 'r
    abstract inAnyOf : StateId list -> 'r
    abstract notIn : StateId -> 'r
    abstract pAnd : 'r * 'r -> 'r
    abstract pOr : 'r * 'r -> 'r
    abstract pNot : 'r -> 'r
    abstract pTrue : 'r
    abstract pFalse : 'r
```

Standard interpreters:

```fsharp
/// Evaluate against a concrete configuration.
type EvalAlgebra(config: Configuration) = ...        // IConfigurationPredicate<bool>

/// Symbolic: return the set of configurations that satisfy the predicate.
type SatisfyingSetAlgebra(allStates: Set<StateId>) = ... // IConfigurationPredicate<Set<Configuration>>

/// Render as readable text.
type RenderAlgebra() = ...                           // IConfigurationPredicate<string>
```

### 7.4 Affordance and Provenance Algebras

```fsharp
namespace Frank.Statecharts

type IAffordanceAlgebra<'r> =
    abstract affordance :
        rel: string *
        href: string *
        method: HttpMethod option *
        transitionRef: TransitionId option ->
        'r
    abstract link : href: string * rel: string -> 'r
    abstract combine : 'r list -> 'r
    abstract empty : 'r

/// Link header interpreter.
type LinkHeaderAlgebra() =
    interface IAffordanceAlgebra<string list> with
        member _.affordance(rel, href, method, _) =
            let methodAttr = method |> Option.map (sprintf "; method=\"%O\"") |> Option.defaultValue ""
            [sprintf "<%s>; rel=\"%s\"%s" href rel methodAttr]
        member _.link(href, rel) = [sprintf "<%s>; rel=\"%s\"" href rel]
        member _.combine items = List.concat items
        member _.empty = []

/// Allow header interpreter.
type AllowHeaderAlgebra() =
    interface IAffordanceAlgebra<Set<HttpMethod>> with
        member _.affordance(_, _, method, _) =
            method |> Option.map Set.singleton |> Option.defaultValue Set.empty
        member _.link(_, _) = Set.empty
        member _.combine items = Set.unionMany items
        member _.empty = Set.empty

/// ALPS descriptor interpreter.
type ALPSAlgebra() =
    interface IAffordanceAlgebra<ALPSDescriptor list> with
        // ... produces ALPS descriptors
```

```fsharp
namespace Provenance.Core

type IProvOAlgebra<'r> =
    abstract entity : id: string -> 'r
    abstract activity : id: string * startedAt: DateTimeOffset -> 'r
    abstract agent : id: string * agentType: AgentType -> 'r
    abstract wasGeneratedBy : entity: 'r * activity: 'r -> 'r
    abstract used : activity: 'r * entity: 'r -> 'r
    abstract wasDerivedFrom : generated: 'r * used: 'r -> 'r
    abstract wasAttributedTo : entity: 'r * agent: 'r -> 'r
    abstract wasAssociatedWith : activity: 'r * agent: 'r -> 'r
    abstract actedOnBehalfOf : delegateAgent: 'r * responsible: 'r -> 'r
    abstract combine : 'r list -> 'r

/// Build ProvenanceGraph.
type GraphBuildingAlgebra() =
    interface IProvOAlgebra<ProvenanceGraph -> ProvenanceGraph> with
        // ... accumulates into graph

/// Render as Turtle.
type TurtleAlgebra(baseUri: string) =
    interface IProvOAlgebra<string> with
        // ... produces Turtle RDF
```

-----

## 8. Runtime Specifications

### 8.1 SCXML Algorithm Transliteration (AD-10)

The macrostep implementation is a direct transliteration of the W3C SCXML Algorithm for SCXML Interpretation (Appendix D of the recommendation). Pseudocode below is annotated with SCXML procedure names; implementation is line-by-line.

```fsharp
namespace Statecharts.Runtime

module internal Semantics =
    open Statecharts.Core

    // SCXML §D.1: enterStates
    let enterStates
        (doc: StatechartDocument)
        (transitions: TransitionEdge list)
        (state: AgentState)
        : AgentState * JournalEntryKind list = ...

    // SCXML §D.2: exitStates
    let exitStates
        (doc: StatechartDocument)
        (transitions: TransitionEdge list)
        (state: AgentState)
        : AgentState * JournalEntryKind list = ...

    // SCXML §D.3: computeExitSet
    let computeExitSet
        (doc: StatechartDocument)
        (config: Configuration)
        (transitions: TransitionEdge list)
        : Set<StateId> = ...

    // SCXML §D.4: computeEntrySet (with addDescendantStatesToEnter,
    // addAncestorStatesToEnter, getEffectiveTargetStates)
    let computeEntrySet
        (doc: StatechartDocument)
        (transitions: TransitionEdge list)
        (state: AgentState)
        : Set<StateId> * Map<StateId, Configuration> = ...

    // SCXML §D.5: selectTransitions for an external event
    let selectTransitions
        (doc: StatechartDocument)
        (config: Configuration)
        (event: EventId option)
        (variables: Map<VariableName, Value>)
        : TransitionEdge list = ...

    // SCXML §D.6: removeConflictingTransitions (priority rules)
    let removeConflictingTransitions
        (doc: StatechartDocument)
        (transitions: TransitionEdge list)
        : TransitionEdge list = ...

    // SCXML §D.7: microstep — fire one set of non-conflicting transitions
    let microstep
        (doc: StatechartDocument)
        (transitions: TransitionEdge list)
        (state: AgentState)
        : AgentState * JournalEntryKind list = ...

    // SCXML §D.8: mainEventLoop iteration — process one external event
    //             plus all chained internal events to quiescence.
    let macrostep
        (doc: StatechartDocument)
        (event: EventId * Value option)
        (state: AgentState)
        : AgentState * FireResult * JournalEntryKind list =

        let mutable currentState = state
        let mutable journalEntries = [
            EventReceived (fst event, snd event, External)
        ]

        // 1. Process the external event.
        let externalTransitions =
            selectTransitions doc currentState.Configuration (Some (fst event)) currentState.Variables
            |> removeConflictingTransitions doc

        let updatedState, microstepEntries = microstep doc externalTransitions currentState
        currentState <- updatedState
        journalEntries <- journalEntries @ microstepEntries

        // 2. Process all internal events to quiescence (SCXML §D.8 inner loop).
        let rec drainInternalQueue (state: AgentState) (acc: JournalEntryKind list) =
            match state.InternalQueue with
            | [] ->
                // Check for eventless transitions newly enabled.
                let eventless =
                    selectTransitions doc state.Configuration None state.Variables
                    |> removeConflictingTransitions doc
                if List.isEmpty eventless then
                    state, acc  // Quiescence reached.
                else
                    let state', entries = microstep doc eventless state
                    drainInternalQueue state' (acc @ entries)
            | (evt, data) :: rest ->
                let state' = { state with InternalQueue = rest }
                let entries = [EventReceived (evt, data, Internal)]
                let internalTrans =
                    selectTransitions doc state'.Configuration (Some evt) state'.Variables
                    |> removeConflictingTransitions doc
                let state'', microEntries = microstep doc internalTrans state'
                drainInternalQueue state'' (acc @ entries @ microEntries)

        let finalState, drainEntries = drainInternalQueue currentState []
        journalEntries <- journalEntries @ drainEntries

        let result =
            if List.isEmpty externalTransitions && List.isEmpty drainEntries then
                NoTransition
            else
                Transitioned {
                    JournalRangeStart = 0L  // Filled in by caller after Append.
                    JournalRangeEnd = 0L
                    FromConfiguration = state.Configuration
                    ToConfiguration = finalState.Configuration
                    TransitionsFired = collectFiredTransitions journalEntries
                    InternalEventsProcessed = collectInternalEvents journalEntries
                }

        finalState, result, journalEntries
```

This implementation is tested against the W3C SCXML 1.0 Implementation Report Plan (IRP) test suite, restricted to features Frank supports (no `<invoke>` in v1; no XPath datamodel — Frank uses its closed `Value` type).

### 8.2 Agent Construction (Journal-Sourced)

```fsharp
module StatechartAgent =

    let create
        (document: StatechartDocument)
        (journal: IStatechartJournal)
        (scheduler: IStatechartScheduler)
        : Async<IStatechartAgent> = async {

        // 1. Recover state by replaying the journal (or restoring from snapshot).
        let! initialState = async {
            match! journal.TrySnapshot() with
            | Some snapshotRef ->
                let! snapshotState = loadSnapshot snapshotRef
                let! newer = journal.ReadFrom (snapshotRef.Position + 1L)
                return replayEntries document snapshotState newer
            | None ->
                let! allEntries = journal.ReadFrom 0L
                let initialConfig = computeInitialConfiguration document
                let emptyState = {
                    Position = 0L
                    Configuration = initialConfig
                    History = Map.empty
                    Variables =
                        document.Datamodel
                        |> List.map (fun (n, v) -> n, v)
                        |> Map.ofList
                    InternalQueue = []
                    ScheduledSends = Map.empty
                }
                return replayEntries document emptyState allEntries
        }

        // 2. Re-subscribe to scheduler for any in-flight scheduled sends.
        let schedulerSubscription =
            scheduler.Subscribe (fun (sendId, evt, data) -> async {
                // Scheduled events become external events from the agent's view.
                ()  // Posted to mailbox below.
            })

        // 3. Mailbox serializes Fire and Query operations.
        let mailbox = MailboxProcessor.Start(fun inbox ->
            let rec loop (state: AgentState) = async {
                let! msg = inbox.Receive()
                match msg with
                | FireMsg(event, data, reply) ->
                    let newState, result, entries =
                        Semantics.macrostep document (event, data) state
                    let! startPos = journal.Append entries
                    let endPos = startPos + int64 (List.length entries) - 1L
                    let resultWithRange =
                        match result with
                        | Transitioned t ->
                            Transitioned { t with
                                JournalRangeStart = startPos
                                JournalRangeEnd = endPos }
                        | other -> other
                    let projectedState = { newState with Position = endPos }
                    reply.Reply resultWithRange
                    return! loop projectedState

                | QueryMsg(query, reply) ->
                    reply.Reply (handleQuery state query)
                    return! loop state

                | StateMsg reply ->
                    reply.Reply state
                    return! loop state

                | ReloadMsg reply ->
                    let! recovered = recoverFromJournal document journal
                    reply.Reply ()
                    return! loop recovered
            }
            loop initialState)

        return { new IStatechartAgent with
            member _.Fire(event, data) =
                mailbox.PostAndAsyncReply(fun ch -> FireMsg(event, data, ch))
            member _.Query(q) =
                mailbox.PostAndAsyncReply(fun ch -> QueryMsg(q, ch))
            member _.State() =
                mailbox.PostAndAsyncReply(StateMsg)
            member _.Reload() =
                mailbox.PostAndAsyncReply(ReloadMsg) }
    }
```

Crash recovery semantics:

1. Journal append is the commit point. If the agent crashes after `Append` but before notifying external observers, recovery replays the entry and notifications can be re-derived from the journal.
1. If the agent crashes during `macrostep` (in-memory), no journal entries are written; on restart the external event is not visible and can be retried by the caller.
1. Idempotency for callers requires the caller to pass a deduplication key (out of scope for the runtime; provided by Frank.Statecharts middleware).

### 8.3 Scheduler Implementations

```fsharp
namespace Statecharts.Runtime.Schedulers

/// In-memory scheduler. Suitable for tests and non-durable scenarios.
/// Scheduled sends are lost on process restart.
type InMemoryScheduler() = ...

/// File-backed scheduler. Single-process durability.
/// On restart, reads outstanding sends from disk and re-arms timers.
type FileScheduler(path: string) = ...

/// Database-backed scheduler. Multi-process durability.
/// Uses a lease-based polling loop to prevent duplicate firing across instances.
type DatabaseScheduler(connection: DbConnection) = ...
```

The runtime does not ship NATS or Kafka schedulers; those live in `Frank.Statecharts.Schedulers.NATS` etc. as opt-in packages.

-----

## 9. Composition Model

### 9.1 Building a StatefulResourceBinding

```fsharp
namespace Frank.Statecharts

type StatefulResourceBuilder<'TEvent>(path: string) =

    let mutable state : BuilderState<'TEvent> = {
        Statechart = None
        Handlers = []
        Affordances = []
        Validation = None
        Provenance = None
        RoleParticipation = None
        RoleOverrides = []
        VisibilityLevel = 0
        EventMapper = None
        Path = path
        Name = ""
    }

    [<CustomOperation("name")>]
    member _.Name(s, value) = { s with Name = value }

    [<CustomOperation("statechart")>]
    member _.Statechart(s, sc: Statechart) =
        { s with Statechart = Some (Statechart.reify sc) }

    [<CustomOperation("statechartDoc")>]
    member _.StatechartDoc(s, doc: StatechartDocument) =
        { s with Statechart = Some doc }

    [<CustomOperation("eventMapper")>]
    member _.EventMapper(s, mapper) = { s with EventMapper = Some mapper }

    /// Single-state predicate: convenience over the predicate-keyed form.
    [<CustomOperation("inState")>]
    member _.InState(s, stateId: string, handlers: StateHandlers<'TEvent>) =
        let pred = ConfigurationPredicate.InState (StateId stateId)
        { s with
            Handlers = (pred, handlers.Handler) :: s.Handlers
            Affordances = (pred, handlers.Affordances) :: s.Affordances }

    /// Predicate-keyed binding for parallel-region cases.
    [<CustomOperation("whenConfig")>]
    member _.WhenConfig(s, predicate: ConfigurationPredicate, handlers: StateHandlers<'TEvent>) =
        { s with
            Handlers = (predicate, handlers.Handler) :: s.Handlers
            Affordances = (predicate, handlers.Affordances) :: s.Affordances }

    /// Affordances only (no handler change).
    [<CustomOperation("whenIn")>]
    member _.WhenIn(s, stateId: string, affordances: Affordance list) =
        let pred = ConfigurationPredicate.InState (StateId stateId)
        { s with Affordances = (pred, affordances) :: s.Affordances }

    [<CustomOperation("whenInAll")>]
    member _.WhenInAll(s, stateIds: string list, affordances: Affordance list) =
        let pred = ConfigurationPredicate.InAll (List.map StateId stateIds)
        { s with Affordances = (pred, affordances) :: s.Affordances }

    [<CustomOperation("validateInState")>]
    member _.ValidateInState(s, stateId: string, shape: NodeShape) =
        let pred = ConfigurationPredicate.InState (StateId stateId)
        let v = s.Validation |> Option.defaultValue []
        { s with Validation = Some ((pred, shape) :: v) }

    [<CustomOperation("trackTransitions")>]
    member _.TrackTransitions(s, options: ProvenanceOptions) =
        { s with Provenance = Some options }

    [<CustomOperation("roles")>]
    member _.Roles(s, schema: RoleParticipation) =
        { s with RoleParticipation = Some schema }

    [<CustomOperation("roleOverride")>]
    member _.RoleOverride(s, ovr: RoleOverride) =
        { s with RoleOverrides = ovr :: s.RoleOverrides }

    [<CustomOperation("visibilityLevel")>]
    member _.VisibilityLevel(s, level) = { s with VisibilityLevel = level }

    member _.Run(s) : StatefulResourceBinding<'TEvent> =
        match s.Statechart, s.EventMapper with
        | Some doc, Some mapper ->
            { Statechart = doc
              Handlers = List.rev s.Handlers
              Affordances = List.rev s.Affordances
              VisibilityLevel = s.VisibilityLevel
              Validation = s.Validation
              Provenance = s.Provenance
              RoleParticipation = s.RoleParticipation
              RoleOverrides = s.RoleOverrides
              EventMapper = mapper
              Path = s.Path
              Name = s.Name }
        | None, _ -> failwith "statechart is required"
        | _, None -> failwith "eventMapper is required"

let statefulResource path = StatefulResourceBuilder(path)
```

### 9.2 Example Usage

```fsharp
// Define role participation alongside the statechart.
let orderRoles : RoleParticipation = {
    Roles = Set.ofList [RoleId "Customer"; RoleId "Approver"; RoleId "Warehouse"]
    Transitions = Map.ofList [
        TransitionId "submit",  { TransitionId = TransitionId "submit"
                                  Triggers = Set.singleton (RoleId "Customer")
                                  Observes = Set.ofList [RoleId "Approver"]
                                  Forbidden = Set.empty }
        TransitionId "cancel",  { TransitionId = TransitionId "cancel"
                                  Triggers = Set.singleton (RoleId "Customer")
                                  Observes = Set.empty
                                  Forbidden = Set.empty }
        TransitionId "approve", { TransitionId = TransitionId "approve"
                                  Triggers = Set.singleton (RoleId "Approver")
                                  Observes = Set.ofList [RoleId "Customer"; RoleId "Warehouse"]
                                  Forbidden = Set.empty }
        TransitionId "reject",  { TransitionId = TransitionId "reject"
                                  Triggers = Set.singleton (RoleId "Approver")
                                  Observes = Set.singleton (RoleId "Customer")
                                  Forbidden = Set.empty }
        TransitionId "ship",    { TransitionId = TransitionId "ship"
                                  Triggers = Set.singleton (RoleId "Warehouse")
                                  Observes = Set.singleton (RoleId "Customer")
                                  Forbidden = Set.empty }
    ]
    States = Map.empty  // Default: all states visible to all roles.
}

let orderResource = statefulResource "/orders/{id}" {
    name "Order"
    statechart orderStatechart
    eventMapper (fun (e: OrderEvent) ->
        match e with
        | Submit -> EventId "submit", None
        | Cancel -> EventId "cancel", None
        | Approve -> EventId "approve", None
        | Reject reason -> EventId "reject", Some (VString reason)
        | Ship -> EventId "ship", None)

    trackTransitions {
        AgentExtractor = extractUserAgent
        TrackReads = false
        Sink = provenanceSink
    }

    roles orderRoles  // Projections derived from this — no manual per-role data.

    // Affordances are bound to predicates. For non-parallel statecharts,
    // single-state predicates are the common case.
    whenIn "Draft" [
        { Rel = "submit"; Href = "./submit"; Method = Some POST
          Accepts = []; Title = Some "Submit order"
          TransitionRef = Some (TransitionId "submit") }
        { Rel = "edit"; Href = "."; Method = Some PUT
          Accepts = ["application/json"]; Title = Some "Edit order"
          TransitionRef = None }
        { Rel = "cancel"; Href = "./cancel"; Method = Some POST
          Accepts = []; Title = Some "Cancel order"
          TransitionRef = Some (TransitionId "cancel") }
    ]
    whenIn "Submitted" [
        { Rel = "approve"; Href = "./approve"; Method = Some POST
          Accepts = []; Title = Some "Approve order"
          TransitionRef = Some (TransitionId "approve") }
        { Rel = "reject"; Href = "./reject"; Method = Some POST
          Accepts = []; Title = Some "Reject order"
          TransitionRef = Some (TransitionId "reject") }
    ]
    whenIn "Approved" [
        { Rel = "ship"; Href = "./ship"; Method = Some POST
          Accepts = []; Title = Some "Ship order"
          TransitionRef = Some (TransitionId "ship") }
    ]

    // For a workflow with parallel regions for Fulfillment and Payment,
    // some affordances depend on both being in the right sub-state.
    // Example (hypothetical):
    // whenInAll ["Approved"; "PaymentCleared"] [
    //     { Rel = "ship"; ... }
    // ]

    validateInState "Draft" draftOrderShape
    validateInState "Submitted" submittedOrderShape

    inState "Draft" {
        get (fun ctx -> getDraftOrder ctx)
        put (fun ctx -> updateDraft ctx)
    }
    inState "Submitted" { get (fun ctx -> getSubmittedOrder ctx) }
    inState "Approved"  { get (fun ctx -> getApprovedOrder ctx) }
}
```

-----

## 10. Multi-Party Projections

### 10.1 Derivation Algorithm

```fsharp
namespace Statecharts.Multiparty

module Projection =

    let project
        (document: StatechartDocument)
        (schema: RoleParticipation)
        (role: RoleId)
        : LocalProjection =

        // 1. Triggerable transitions: those where the role appears in Triggers.
        let triggerable =
            schema.Transitions
            |> Map.filter (fun _ tp -> Set.contains role tp.Triggers)
            |> Map.toSeq |> Seq.map fst |> Set.ofSeq

        // 2. Observable transitions: those where the role appears in Observes
        //    (and not Forbidden).
        let observable =
            schema.Transitions
            |> Map.filter (fun _ tp ->
                Set.contains role tp.Observes && not (Set.contains role tp.Forbidden))
            |> Map.toSeq |> Seq.map fst |> Set.ofSeq

        // 3. Visible states: states with the role in VisibleTo, plus states that
        //    are sources or targets of transitions the role triggers/observes.
        let explicitlyVisible =
            schema.States
            |> Map.toSeq
            |> Seq.filter (fun (_, sv) ->
                Set.isEmpty sv.VisibleTo || Set.contains role sv.VisibleTo)
            |> Seq.map fst |> Set.ofSeq

        let derivedVisible =
            (Set.union triggerable observable)
            |> Set.toSeq
            |> Seq.collect (fun tid ->
                document.Transitions
                |> List.find (fun t -> t.Id = tid)
                |> fun t -> List.append t.Sources t.Targets)
            |> Set.ofSeq

        // If schema.States is empty, default to "everything visible".
        let visibleStates =
            if Map.isEmpty schema.States then
                document |> StatechartDocument.allStates
            else
                Set.union explicitlyVisible derivedVisible

        { RoleId = role
          VisibleStates = visibleStates
          TriggerableTransitions = triggerable
          ObservableTransitions = observable }

    let projectAll document schema =
        schema.Roles
        |> Set.toSeq
        |> Seq.map (fun r -> r, project document schema r)
        |> Map.ofSeq
```

### 10.2 Well-Formedness Checks

```fsharp
    let checkWellFormedness
        (document: StatechartDocument)
        (schema: RoleParticipation)
        : WellFormednessIssue list =

        let issues = ResizeArray<WellFormednessIssue>()

        // WF-1: every transition has at least one triggering role (unless eventless).
        for t in document.Transitions do
            match Map.tryFind t.Id schema.Transitions with
            | Some tp when Set.isEmpty tp.Triggers && t.Event.IsSome ->
                issues.Add (TransitionWithoutTrigger t.Id)
            | None when t.Event.IsSome ->
                issues.Add (TransitionWithoutTrigger t.Id)
            | _ -> ()

        // WF-2: no role is both Triggers and Forbidden for the same transition.
        for KeyValue(tid, tp) in schema.Transitions do
            for r in Set.intersect tp.Triggers tp.Forbidden do
                issues.Add (ConflictingParticipation (tid, r, true, true))

        // WF-3: every reachable state is visible to at least one role.
        let reachable = Reachability.compute document
        let projections = projectAll document schema
        for s in reachable do
            let visibleTo =
                projections
                |> Map.filter (fun _ p -> Set.contains s p.VisibleStates)
                |> Map.toSeq |> Seq.map fst |> Set.ofSeq
            if Set.isEmpty visibleTo then
                for r in schema.Roles do
                    issues.Add (UnreachableForRole (r, s))

        // WF-4: every role has at least one triggerable transition reachable from
        //       the initial configuration. (No-op roles are flagged as orphans.)
        for r in schema.Roles do
            let proj = Map.find r projections
            if Set.isEmpty proj.TriggerableTransitions
               && Set.isEmpty proj.ObservableTransitions then
                issues.Add (OrphanRole r)

        // WF-5: progress — for every reachable configuration, at least one role
        //       has a triggerable transition. (Otherwise the protocol deadlocks.)
        let configs = Reachability.allConfigurations document
        for c in configs do
            let anyEnabled =
                schema.Transitions
                |> Map.exists (fun tid tp ->
                    let t = document.Transitions |> List.find (fun e -> e.Id = tid)
                    not (Set.isEmpty tp.Triggers) &&
                    t.Sources |> List.forall (fun s -> Set.contains s c))
            if not anyEnabled then
                for r in schema.Roles do
                    issues.Add (NoProgressForRole (r, c))

        List.ofSeq issues
```

### 10.3 Integration with HTTP Pipeline

```fsharp
namespace Frank.Statecharts.Middleware

module RoleProjection =

    let useRoleProjection<'TEvent>
        (binding: StatefulResourceBinding<'TEvent>)
        (agent: IStatechartAgent)
        (roleExtractor: HttpContext -> RoleId option)
        : HttpHandler =

        // Pre-compute projections at startup (they are derived from static data).
        let projections =
            binding.RoleParticipation
            |> Option.map (fun schema ->
                Projection.projectAll binding.Statechart schema)
            |> Option.defaultValue Map.empty

        fun next ctx -> task {
            let! configResult = agent.Query CurrentConfiguration
            let config =
                match configResult with
                | QConfig c -> c
                | _ -> Set.empty

            let role = roleExtractor ctx |> Option.defaultValue (RoleId "anonymous")
            let projection = Map.tryFind role projections

            // Filter affordances: a binding's predicate must hold AND the affordance's
            // TransitionRef must be in the role's TriggerableTransitions (if any).
            let visibleAffordances =
                binding.Affordances
                |> List.filter (fun (pred, _) ->
                    ConfigurationPredicate.eval pred config)
                |> List.collect snd
                |> List.filter (fun a ->
                    match projection, a.TransitionRef with
                    | None, _ -> true  // No projection = full access.
                    | Some p, None -> true  // Affordance not bound to a transition.
                    | Some p, Some tref ->
                        Set.contains tref p.TriggerableTransitions)
                // Apply manual overrides.
                |> applyOverrides role config binding.RoleOverrides

            let view = {
                Role = role
                Configuration = config
                Affordances = visibleAffordances
                Validation = collectValidation config binding.Validation
                VisibleConfiguration =
                    match projection with
                    | Some p -> Set.intersect config p.VisibleStates
                    | None -> config
                EnabledTransitions =
                    match projection with
                    | Some p -> Set.toList p.TriggerableTransitions
                    | None -> binding.Statechart.Transitions |> List.map (fun t -> t.Id)
            }

            ctx.Items.["ProjectedView"] <- view

            // Set Link headers from filtered affordances.
            for aff in visibleAffordances do
                ctx.Response.Headers.Append("Link", formatLinkHeader aff)

            return! next ctx
        }
```

-----

## 11. Build Plan

The plan is strictly depth-first. Each layer ships complete (against its conformance suite) before the next layer is started. No layer assumes simplifications it intends to revisit.

### Phase 0: Statecharts.Core (Weeks 1–3)

Deliverables:

- All AST types from §6.1.
- `IStatechartBuilder<'r>`, `ITransitionAlgebra<'r>`, `IConfigurationPredicate<'r>`.
- Reflect/reify for statecharts and transition programs.
- All standard interpreters listed in §7.
- `ConfigurationPredicate` evaluation and indexing.
- Closed `Guard` evaluator.

Conformance suite:

- Round-trip property: `reify (reflect doc) ≡ doc` for all generated documents.
- All interpreters total on all generated inputs (FsCheck).
- `ConfigurationPredicate.eval` matches a reference implementation on 10,000 generated configurations.
- Guard evaluator total on all guard expressions (FsCheck).

Exit gate: all three algebras have at least three interpreters each, all conformance properties pass, no `obj`-typed escapes anywhere.

### Phase 1: Statecharts.Runtime (Weeks 3–7)

Deliverables:

- `IStatechartJournal` with in-memory and file-backed implementations.
- `IStatechartScheduler` with in-memory and file-backed implementations.
- `StatechartAgent` with full SCXML algorithm transliteration (§8.1).
- Crash recovery: agent restart from journal produces identical state.
- Snapshot/replay: snapshots are validated against journal head.
- Trace ordering matches SCXML reference.

Conformance suite:

- W3C SCXML 1.0 IRP test subset (compound, parallel, history, internal events, eventless transitions, send/cancel, datamodel — restricted to features Frank supports).
- Property: agent state after N events equals replayed state from journal of those N events.
- Property: snapshot at position P, then N more events, equals fresh agent fed all P+N events.
- Property: scheduled events fire at the correct time, survive restart.
- Soak test: 100,000 events without memory leak; journal grows linearly; snapshot path stays bounded.

Exit gate: all SCXML IRP tests Frank claims to support pass; restart-recovery property holds across all generated event sequences; scheduler tests pass under simulated process kill.

### Phase 2: Statecharts.Parsers and Statecharts.Generators (Weeks 7–9)

Deliverables:

- WSD parser → AST.
- SCXML parser → AST (full feature subset matching runtime).
- smcat parser → AST.
- Generators: SCXML, XState, smcat, mermaid, ALPS — all driven by `IStatechartBuilder` interpreters.

Conformance suite:

- For each parser: parse → AST → generate → parse equals original AST (semantic round-trip).
- For each generator: output validates against the format’s schema.
- Cross-tool: SCXML produced by Frank loads in scion and qm-scxml without error.

Exit gate: round-trip preservation on all sample files; cross-tool interop verified for SCXML and XState.

### Phase 3: Statecharts.Multiparty (Weeks 9–11)

Deliverables:

- `RoleParticipation` types and projection algorithm (§10.1).
- All five well-formedness checks (§10.2).
- Generators: per-role local SCXML (the projection rendered as a sub-statechart).
- Documentation: how to express common multi-party patterns.

Conformance suite:

- Property: for any well-formed global protocol, projecting all roles and re-composing produces a statechart bisimilar to the original (within Frank’s bisimulation definition — to be specified during Phase 3).
- Property: well-formedness checks have no false positives on a curated corpus of known-good protocols.
- Property: well-formedness checks have no false negatives on a curated corpus of intentionally-broken protocols.

Exit gate: projection algorithm matches the published MPST projection rules on a documented mapping; well-formedness checks have an annotated test corpus.

### Phase 4: Sibling Concerns (Weeks 11–13, parallelizable)

Deliverables:

- `Validation.Core`, `Provenance.Core`, `LinkedData.Core` — all to full target schema.
- `Statecharts.Provenance` — journal → PROV-O derivation. Depends on `Statecharts.Core` (Phase 0) and `Provenance.Core` (this phase). Pure functions; no runtime state.
- `Frank.Validation`, `Frank.Provenance`, `Frank.LinkedData`, `Frank.Discovery` — middleware integrations. `Frank.Provenance` ships HTTP-level audit only; journal-sourced PROV-O is wired up in Phase 5 via `Statecharts.Provenance`.

Conformance suite:

- SHACL validator passes the W3C SHACL test suite (subset for shapes Frank supports).
- PROV-O serialization round-trips through Apache Jena.
- LinkedData round-trips Turtle ↔ JSON-LD ↔ N-Triples.
- `Statecharts.Provenance`: for a curated corpus of journal sequences, `Derivation.derive` produces PROV-O graphs that round-trip through Apache Jena unchanged.
- `Statecharts.Provenance`: property — `derive(entries[0..k]) ∪ derive(entries[k+1..n]) = derive(entries[0..n])` (append-only derivation).

Exit gate: each sibling package passes its respective standard’s conformance suite; `Statecharts.Provenance` derivation is pure and append-associative.

### Phase 5: Frank.Statecharts (Weeks 13–16)

Deliverables:

- `StatefulResourceBinding` (predicate-keyed).
- `StatefulResourceBuilder` CE.
- `useStatecharts`, `useRoleProjection`, `useAffordances` middleware.
- Persistent journal/scheduler implementations using ASP.NET Core hosted services.
- Wiring: when `ProvenanceOptions` is present, `Frank.Statecharts` invokes `Statecharts.Provenance.Derivation.deriveAndAppend` after each successful `Fire`, injecting the human agent from `AgentExtractor`.
- Full integration: derived projections feed Link headers; PROV-O is derived from the journal by `Statecharts.Provenance`.

Conformance suite:

- Order workflow sample: end-to-end test demonstrates each role sees only their projected affordances.
- TicTacToe sample: multi-party game with derived role projections.
- Provenance sample: PROV-O graph derived from journal matches hand-constructed expected graph.
- Property: across all role × configuration combinations, projected view affordances are exactly the intersection of (binding affordances satisfying predicate) and (role’s triggerable transitions).
- Property: the PROV-O graph produced by running an agent end-to-end equals the graph produced by replaying the final journal through `Statecharts.Provenance.Derivation.replay`.

Exit gate: three samples pass full integration tests; projection consistency property holds; live-vs-replay provenance equivalence holds.

### Phase 6: Frank.Statecharts.Analyzers (Weeks 16–18)

Deliverables:

|Rule    |Detects                                                   |
|--------|----------------------------------------------------------|
|FRANK101|Duplicate handler for the same predicate                  |
|FRANK102|State has no handler for any reachable configuration      |
|FRANK103|Affordance references unknown TransitionId                |
|FRANK104|Transition target is undefined                            |
|FRANK105|Two predicates overlap with conflicting affordances       |
|FRANK106|Predicate references a state that doesn’t exist           |
|FRANK107|Compound state missing required Initial                   |
|FRANK108|Final state has children                                  |
|FRANK201|Unreachable state                                         |
|FRANK202|Deadlock-reachable configuration                          |
|FRANK203|Livelock detected (eventless transition cycle)            |
|FRANK204|Unreachable transition (source set never co-active)       |
|FRANK205|Predicate is unsatisfiable for any reachable configuration|
|FRANK206|Guard is unsatisfiable                                    |
|FRANK207|Variable used in guard but never assigned                 |
|FRANK208|Scheduled event with no canceller; potentially leaks      |
|FRANK210|Manual RoleOverride conflicts with derived projection     |
|FRANK211|Role has no triggerable transitions (orphan)              |
|FRANK212|Configuration with no triggerable transition for any role |

Conformance suite:

- For each rule: positive cases (intentionally violating) and negative cases (intentionally clean) in test fixtures.
- No false positives on the order, TicTacToe, and provenance samples.

Exit gate: all rules pass positive/negative tests; samples produce zero analyzer warnings.

### Phase 7: Documentation and Samples (Weeks 18–20)

- Order Workflow sample (multi-party, derived projections, provenance from journal).
- TicTacToe sample (capability-envelope hierarchy, multi-party).
- API Documentation sample (ALPS, LinkedData content negotiation).
- Conformance report: which SCXML features Frank supports, which it intentionally omits, and where it deviates.

Exit gate: documentation review by an external reader (someone who has not been part of the design process) yields a working sample within an hour.

### Calendar Reality

Twenty weeks of focused work. Given competing priorities on Frank’s other v7.3.0 work (semantic resources, spec pipeline) and day-job context, an honest elapsed-time estimate is **8–12 months**. The phase boundaries are firm; the calendar between them is flexible.

The key discipline this plan enforces: no layer is started until the layer beneath it has passed its conformance gate. This is the trade-off for depth-first: slower arrival at visible HTTP behavior, but that behavior, when it arrives, rests on a foundation that doesn’t require revision.

-----

## 12. Conformance and Falsifiability

Each layer has a published conformance suite. The suite is the architecture’s contract with future maintainers and adopters.

### 12.1 Test Suites by Layer

|Layer                      |Test suite                                                                     |
|---------------------------|-------------------------------------------------------------------------------|
|Statecharts.Core           |FsCheck properties on AST round-trip, predicate evaluation, guard evaluation   |
|Statecharts.Runtime        |W3C SCXML IRP subset; restart-recovery property; scheduler under simulated kill|
|Statecharts.Parsers        |Round-trip on sample corpora; cross-tool load tests                            |
|Statecharts.Generators     |Schema validation; cross-tool load tests                                       |
|Statecharts.Multiparty     |MPST projection rule conformance; well-formedness check corpus                 |
|Validation.Core            |W3C SHACL test suite (Frank-supported shapes)                                  |
|Provenance.Core            |PROV-O serialization round-trip via Apache Jena                                |
|Statecharts.Provenance     |Journal → PROV-O derivation purity and append-associativity properties         |
|LinkedData.Core            |RDF format round-trips                                                         |
|Frank.Statecharts          |Sample-based integration; projection consistency; live-vs-replay provenance    |
|Frank.Statecharts.Analyzers|Per-rule positive/negative fixtures                                            |

### 12.2 Falsifiable Acceptance Criteria

Every deliverable has a falsifiable acceptance criterion. Examples:

- **Statecharts.Runtime AC-1:** Given any sequence of N events fired against a fresh agent, then snapshotting at position k and replaying events k+1..N from the journal produces identical `AgentState`. Falsifiable by any divergence.
- **Statecharts.Multiparty AC-1:** Given a global protocol with N roles, projecting each role and re-composing as parallel regions produces a statechart bisimilar to the original. Falsifiable by any role × configuration where the projection cannot reproduce an enabled transition of the original.
- **Statecharts.Provenance AC-1:** For any split point k in a journal of length N, `derive(entries[0..k]) ∪ derive(entries[k+1..N]) ≡ derive(entries[0..N])` as RDF graphs. Falsifiable by any structural inequivalence.
- **Frank.Statecharts AC-1:** For all role × configuration combinations on the order workflow sample, the set of affordances in `ProjectedView.Affordances` equals the set computed by manually intersecting binding predicates with role triggerable transitions. Falsifiable by any divergence.
- **Frank.Statecharts AC-2:** For any completed workflow run, the PROV-O graph accumulated through live `deriveAndAppend` calls equals the graph produced by `Derivation.replay` over the final journal. Falsifiable by any graph-isomorphism failure.

The full criteria list lives in each phase’s GitHub milestone.

-----

## 13. References

### 13.1 Academic Papers

1. **Harel, D. (1987).** Statecharts: A Visual Formalism for Complex Systems. *Science of Computer Programming*, 8(3), 231–274.
1. **Harel, D., & Naamad, A. (1996).** The STATEMATE Semantics of Statecharts. *ACM TOSEM*, 5(4), 293–333.
1. **de Oliveira, M.C.F., Turine, M.A.S., & Masiero, P.C. (2001).** A Statechart-Based Model for Hypermedia Applications. *ACM TOIS*, 19(1), 28–52.
1. **Carette, J., Kiselyov, O., & Shan, C. (2009).** Finally Tagless, Partially Evaluated. *JFP*, 19(5), 509–543.
1. **Honda, K., Yoshida, N., & Carbone, M. (2008).** Multiparty Asynchronous Session Types. *POPL 2008*.
1. **Honda, K., Yoshida, N., & Carbone, M. (2016).** Multiparty Asynchronous Session Types. *JACM* 63(1).
1. **Deniélou, P.M., & Yoshida, N. (2012).** Multiparty Session Types Meet Communicating Automata. *ESOP 2012*, LNCS 7211, 194–213.
1. **Young, G. (2010).** CQRS Documents. https://cqrs.wordpress.com/

### 13.2 Online Resources

1. **Kiselyov, O.** Typed Tagless Final Interpreters. SSGIP 2010 Lecture Notes.
1. **Azariah, J. (2025).** Tagless Final in F# (6-part series). FsAdvent 2025.
1. **Fowler, M.** Event Sourcing. https://martinfowler.com/eaaDev/EventSourcing.html

### 13.3 Standards

1. **W3C.** State Chart XML (SCXML): State Machine Notation for Control Abstraction. https://www.w3.org/TR/scxml/
1. **W3C.** SCXML 1.0 Implementation Report Plan. https://www.w3.org/Voice/2013/scxml-irp/
1. **ALPS.** Application-Level Profile Semantics. http://alps.io/spec/
1. **W3C.** PROV-O: The PROV Ontology. https://www.w3.org/TR/prov-o/
1. **W3C.** SHACL: Shapes Constraint Language. https://www.w3.org/TR/shacl/

-----

## 14. Appendices

### Appendix A: Glossary

|Term                  |Definition                                                                                       |
|----------------------|-------------------------------------------------------------------------------------------------|
|Configuration         |Maximal orthogonal set of active states                                                          |
|ConfigurationPredicate|Boolean expression over a Configuration; the binding key for affordances etc.                    |
|Macrostep             |Processing one external event plus all chained internal events to quiescence                     |
|Microstep             |One set of non-conflicting transitions firing together                                           |
|Broadcast             |Internal events generated by actions, processed in same macrostep                                |
|Journal               |Append-only log of agent events; the source of truth for agent state                             |
|Snapshot              |Cached projection of journal state at a known position                                           |
|History               |Pseudostate that remembers and restores sub-configurations                                       |
|LCA                   |Least Common Ancestor of a set of states                                                         |
|Orthogonal            |States that can be simultaneously active (AND-decomposition)                                     |
|Tagless-final         |Encoding programs as polymorphic functions over algebras                                         |
|Reflect               |Convert AST to polymorphic program                                                               |
|Reify                 |Convert polymorphic program to AST                                                               |
|RoleParticipation     |Schema specifying triggers/observes/forbidden per transition × role                              |
|LocalProjection       |Per-role view derived from RoleParticipation                                                     |
|Well-formedness       |Properties of a global protocol that ensure sound projection (no deadlock, no orphan roles, etc.)|
|Quiescence            |State of an agent with no internal events pending                                                |

### Appendix B: Decision Log

|Date   |Decision                              |Rationale                                                                   |
|-------|--------------------------------------|----------------------------------------------------------------------------|
|2026-04|Separate types per concern            |Orthogonal concerns, independent utility                                    |
|2026-04|Three algebras with disjoint carriers |Single algebra conflated structure/behavior/queries                         |
|2026-04|Journal-sourced runtime               |Durability, provenance derivation, snapshot consistency                     |
|2026-04|AST + algebra bridge per algebra      |Parsing produces AST; interpretation uses algebra                           |
|2026-04|Predicate-keyed bindings              |Single-state keys silently dropped parallel-region cases                    |
|2026-04|Derived role projections              |Hand-authored projections were unsound and didn’t scale                     |
|2026-04|Independent statecharts package       |Reusability outside HTTP contexts                                           |
|2026-04|Closed guard language                 |Open-string guards required embedded evaluator                              |
|2026-04|Scheduled events first-class          |Half of HTTP workflows have timeouts; can’t defer                           |
|2026-04|Macrostep matches SCXML exactly       |Almost-SCXML is worse than either SCXML or explicit divergence              |
|2026-04|Statecharts.Provenance as seam package|Keeps Frank.Provenance statechart-unaware; journal → PROV-O reusable offline|
|2026-04|Depth-first build plan                |Breadth-first accumulates inherited constraints across layers               |

### Appendix C: Open Questions

1. **Bisimulation definition for Phase 3.** The “projection + recomposition is bisimilar to the original” property requires a precise bisimulation definition. Candidates: weak bisimulation over external events, branching bisimulation over configurations. To be specified during Phase 3; possible publication candidate.
1. **NATS / Kafka journal implementations.** Out of scope for the runtime package, but worth specifying the integration contract early so external implementers can target it. Tracked separately.
1. **Distributed scheduler correctness.** The clustered `IStatechartScheduler` interface is specified; lease-based polling is one strategy. Whether to use that, distributed locks, or consensus (Raft) is an implementation choice for the eventual database/NATS scheduler packages.
1. **Snapshot format and migration.** When the AST evolves, existing snapshots may become unreadable. Strategy options: version snapshots and migrate on load; refuse to load and force replay from journal. Default: refuse to load and replay.
1. **Multi-region affordance authoring ergonomics.** `whenInAll [...]` is correct but verbose. Whether to add CE sugar for common patterns is a post-v1 consideration.
1. **`<invoke>` and child state machines.** Currently out of scope. Future consideration: integration with .NET hosted-service patterns to model invoked actors.

### Appendix D: Non-Goals

Explicitly out of scope for this architecture:

- **XPath or ECMAScript datamodel.** Frank uses its closed `Value` type and `Guard` language. SCXML documents using XPath or ECMAScript fail at parse time with clear errors.
- **Live hot-reload of statechart structure.** Changing the statechart requires a new agent instance. Migration of running agents across structural changes is explicitly not addressed.
- **Cross-agent transactions.** Each agent’s journal is independent. Coordinating state changes across multiple agents atomically is a higher-level concern, not the runtime’s responsibility.
- **General-purpose workflow engine.** Frank is specifically stateful HTTP resources. It is not a replacement for Temporal, Camunda, or similar workflow systems.