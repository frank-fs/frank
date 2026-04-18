# Frank Statecharts Architecture

**Status**: Living document
**Authors**: Ryan Riley, with architectural consultation

-----

## Executive Summary

Frank provides stateful HTTP resources backed by a denotational substrate of coalgebraic session types. Statecharts are one presentation of session-typed protocols; resources expose state-dependent affordances, validate inputs against SHACL shapes, serve RDF representations via content negotiation, emit PROV-O audit graphs as homomorphic projections of the protocol’s denotation, and enforce multi-party protocols through coalgebra-homomorphism-sound role projections.

The architecture rests on fourteen load-bearing decisions:

1. **Separate types per concern.** Five Core packages — session types, statecharts, validation, provenance, linked data — each own their types. Concerns compose through explicit bindings at the denotation level and through explicit composition packages at the builder level, not through a unified AST.
1. **Algebras as the contract surface for each concern.** Ten tagless-final algebras across five concerns; each interpreter is a homomorphism into a specific carrier representing one way of using the concern’s values. The homomorphism-into-the-denotation property is the uniform soundness criterion.
1. **Journal-sourced runtime as coalgebra unfolding.** Per-role `MailboxProcessor` actors are the runtime; the journal is what they produce by communicating. SQLite-per-actor as the default implementation, matching the Cloudflare Durable Objects / Gleam Warp pattern. LMDB as an alternative under memory pressure.
1. **Homomorphism bridges, not AST conversion.** Each algebra has a lossless reflect/reify bridge between its AST and its polymorphic form. Parsing produces AST; interpretation uses the polymorphic form.
1. **Composition model with predicate keys.** Statechart bindings attach to `ConfigurationPredicate`s, not single state IDs. Parallel regions compose correctly because predicates are conjunction-closed, matching how AND-states compose in the denotation.
1. **Role projections are coalgebra homomorphisms, with pluggable assignment.** Projection soundness is the commutative-diagram property `αᵣ ∘ π_r = F(π_r) ∘ α`, verified by well-formedness checks. Role assignment via pluggable `RoleExtractor` interface; `ClaimsRoleExtractor` is the v1 default; actor-backed and external-service variants are user-implementable.
1. **Core packages are independent of ASP.NET Core and of each other.** Every `Frank.*.Core` package depends only on FSharp.Core (plus `System.Security.Claims` for `Frank.SessionTypes.Core`). No Core depends on any other Core. Independence is enforced structurally.
1. **Closed guard language is part of the denotation.** Guards are refinement predicates on message payloads; the closed form makes refinement satisfiability decidable via SMT, and refinement well-formedness is part of the denotational correctness claim.
1. **Scheduled events are first-class.** SCXML `<send delay>` and `<cancel>` are journaled operations with a durable `IStatechartScheduler`; denotationally, they extend the coalgebraic functor with a time dimension.
1. **SCXML operational algorithm as one adequate homomorphism.** The macrostep implementation is justified by an adequacy theorem relating it to the coalgebraic denotation. Property-based testing verifies adequacy in v1; mechanical proof is a v2/v3 aspiration.
1. **Analyzer suite as extended type checker.** Six rule categories with hundred-block numbering (001–599 active, 600+ reserved). SMT-backed refinement checking, composition-failure detection, and codegen consistency are all part of the analyzer.
1. **Codegen bridges from specifications to F# code.** V1 CE-driven codegen via Myriad for session types and statecharts (producing fully-implemented `MailboxProcessor` actors with typed hooks; typed state and event DUs). V2 format-driven codegen via the FSharp.GrpcCodeGenerator / FsGrpc.Tools pattern for WSD, SCXML, Scribble, AsyncAPI, SHACL, and other external formats. Codegen is where the architecture’s duality hypothesis lives operationally — formal specifications produce legible operational artifacts via verifiable transformation, and the feedback loop between hard formal structure and capable AI tooling is present in v1 through CE authoring.
1. **Concerns extend Frank core via three contribution surfaces.** Every concern ships algebras (internal contract), nested CEs (authoring), and type extensions to `ResourceBuilder` and `WebHostBuilder` (composition). Composition packages contribute only type extensions. V1 ships eight composition packages; gaps in the composition grid are visible in the package list, not hidden inside packages doing two things.
1. **Analyzer rules for composition.** FRANK400 series catches operation-name collisions, missing prerequisites, builder-output mismatches, and missing role-extractor registration at compile time, enforcing the Voltron discipline mechanically.

The architecture is built **depth-first**. Each phase ships complete against its conformance suite before the next is started. The denotational foundation is Phases 0a–0c (parallel Core package development, foundational composition packages for the translations the runtime depends on, CE-driven codegen infrastructure). The runtime realizes the denotation in Phase 1. Refinement verification, composition packages, HTTP integration, analyzers, and documentation follow in Phases 2–7. V2 format-driven codegen is named explicitly as Phase 8 so the deferred scope remains visible.

-----

## Table of Contents

1. [Problem Statement](#1-problem-statement)
1. [Denotational Foundations](#2-denotational-foundations)
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
1. **Independent utility.** `Frank.Statecharts.Core`, `.Runtime`, `.Parsers`, `.Generators`, `.Multiparty`, and `.Provenance` (plus sibling `Frank.Validation.Core`, `Frank.Provenance.Core`, `Frank.LinkedData.Core`) work without ASP.NET Core or any HTTP context — consumable from CLI tools, background services, and offline analysis. Frank middleware packages work without statecharts where the concern is independent.
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

## 2. Denotational Foundations

### 2.1 Why Denotational Semantics

Frank’s statechart layer makes claims that operational semantics cannot underwrite. *Per-role projections preserve observable behavior.* *Multiple interpreters of the same protocol agree on what they implement.* *Provenance derived from a journal faithfully records what happened.* *A protocol composed of fragments behaves as the composition of their behaviors.* Each of these is a statement about meaning — about what a protocol *is* — not a statement about what an algorithm produces step by step.

Operational semantics specifies an algorithm. Denotational semantics specifies what the algorithm is computing. The relationship between them is operational adequacy: the algorithm computes the denotation correctly. When you have only the algorithm, you have nothing to be adequate *to*; soundness questions reduce to “the algorithm produces what it produces,” which is unfalsifiable. The algorithm becomes the spec, and properties that should follow from the spec — projection soundness, interpreter agreement, provenance correctness — instead require ad-hoc proofs against algorithmic detail.

Frank takes the denotation as primary. The SCXML algorithm becomes one homomorphism among several into representations of the denotation, justified by an adequacy theorem rather than by definition. The role-projection function becomes a structure-preserving map between denotations rather than a syntactic operation that happens to seem right. Provenance derivation becomes another homomorphism into a different carrier (PROV-O graphs), and “provenance is correct” becomes a checkable relationship between two homomorphisms of the same denotation.

This is more work than operational specification alone, and the work is concentrated in §2 and §3 — once the foundation is set, the downstream sections become specifications of *what* rather than apologetics for *how*.

### 2.2 The Denotation: Coalgebraic Session Types

Frank’s denotation domain is **coalgebraic session types**: every protocol denotes both a global session type (in the Honda-Yoshida-Carbone tradition) and a coalgebra over a state space (in the Jacobs/Rutten tradition), where the two views are related by a precise mathematical correspondence. Neither view is primary. They are two faces of the same denotational object.

#### Global session types (the syntactic face)

A **global type** `G` describes a multi-party protocol from a god’s-eye view. The grammar:

```
G ::=  end                              -- protocol terminates
    |  μX. G                            -- recursive protocol
    |  X                                -- recursion variable
    |  p → q : { lᵢ⟨Tᵢ⟩. Gᵢ }ᵢ∈I       -- p sends q one of the labeled messages, continues per choice
```

The form `p → q : { lᵢ⟨Tᵢ⟩. Gᵢ }` says: role `p` sends to role `q` a message tagged with one of the labels `lᵢ`, carrying a payload of type `Tᵢ`, and the protocol continues as `Gᵢ` depending on which label was sent. The set `I` is non-empty; when it has size one, the message is unconditional; when larger, it is a *choice* point.

A global type is **well-formed** if it satisfies three conditions central to the MPST theory:

1. **Sender determinacy.** At every choice point `p → q : { lᵢ⟨Tᵢ⟩. Gᵢ }`, the sender `p` must be the same across all branches (no role ambiguity at choice points).
1. **Knowledge of choice.** Every role whose subsequent behavior depends on which branch was taken must learn the choice through some message in that branch — explicitly or transitively.
1. **No orphan messages.** Every message expected by some role must be sent by some role; every message sent must be expected by its receiver.

Well-formedness is checkable on the global type by structural recursion. It is the syntactic precondition for soundness of projection.

#### Coalgebras (the behavioral face)

A **coalgebra** for a functor `F` is a pair `(S, α)` where `S` is a state space and `α : S → F(S)` is a structure map describing what one step of the system can do from each state. For session-typed multi-party systems, the relevant functor is:

```
F(X) = Role → ( SendBranch(Msg × X)
              + ReceiveBranch(Msg → X)
              + End )
```

Read: at each state, for each role, the system either *sends* a message and continues (a branch is the choice of which message to send), *receives* a message and continues (the continuation is indexed by which message arrives), or *terminates*. The coalgebra `(S, α)` for a specific protocol assigns to each state in `S` exactly which of these holds for each role.

The coalgebra is the *meaning* of the protocol: it specifies, exhaustively, what behaviors are possible. Two protocols denote the same coalgebra (up to bisimulation) iff they have the same observable behavior.

#### The duality

The global session type `G` and the coalgebra `(S, α)` it denotes are related by a constructive correspondence: `G` is a syntactic *presentation* of `(S, α)`. The state space `S` is the set of subterms of `G` (modulo recursion unfolding). The structure map `α` is computed by structural recursion on `G`. The end state is `end`; the structure map at a choice point `p → q : { lᵢ⟨Tᵢ⟩. Gᵢ }` returns, for role `p`, a `SendBranch` over the labels `lᵢ` with continuations `Gᵢ`; for role `q`, a `ReceiveBranch` indexed similarly; for any other role, the same as the continuation `Gᵢ` (since their behavior doesn’t depend on this choice if knowledge-of-choice holds).

This is a *duality* in the precise mathematical sense: for every well-formed global type there is a unique corresponding coalgebra, and the operations available on each side correspond:

|Global type operation                 |Coalgebra operation                        |
|--------------------------------------|-------------------------------------------|
|Projection `G ↾ p` to role `p`        |Coalgebra homomorphism into `p`’s view     |
|Composition of fragments by sequencing|Colimit of coalgebras over shared interface|
|Recursion `μX. G`                     |Greatest fixed point of `α`                |
|Well-formedness                       |Existence of bisimulation to canonical form|

The duality is what justifies treating either view as primary depending on what we want to do. For specification and human authorship, the global type is convenient (sequential reading, named choice points, structural composition). For analysis and runtime implementation, the coalgebra is convenient (state-and-step structure maps directly onto actor implementations and onto bisimulation-based equivalence checking).

#### Projection as homomorphism

Per-role projection is the central operation Frank performs on protocols. The projection of global type `G` to role `r`, written `G ↾ r`, is a *local type* describing `r`’s view of the protocol — the messages `r` sends, the messages `r` receives, the choices `r` makes, the choices `r` learns about.

The local type for role `r` is itself a coalgebra over `r`’s local state space. The projection function from global to local is, mathematically, a coalgebra homomorphism: a structure-preserving map from the global coalgebra `(S, α)` to the local coalgebra `(Sᵣ, αᵣ)` such that the diagram

```
    α
S ─────► F(S)
│           │
│ π_r       │ F(π_r)
▼           ▼
Sᵣ ────► F(Sᵣ)
    αᵣ
```

commutes. Equationally: `αᵣ ∘ π_r = F(π_r) ∘ α`. This is the *soundness criterion* for projection. It is not a wish; it is a mathematical statement that can be checked, and Frank checks it.

The well-formedness conditions on global types are exactly the syntactic conditions under which this homomorphism exists for every role. Sender determinacy ensures the projection is single-valued. Knowledge of choice ensures the projection is well-defined after every choice point. No orphan messages ensures the local coalgebras compose back to the global coalgebra.

When projection is sound — which the well-formedness check verifies — the per-role implementations Frank deploys are by construction faithful to the global protocol. There is no separate proof obligation; the soundness *is* the homomorphism, and the homomorphism follows from well-formedness.

### 2.3 Refinements: Guards as Refined Message Types

The classical MPST grammar above carries unrefined payload types `Tᵢ`. Frank extends this by allowing each label to carry a refinement predicate over the payload, drawn from the closed `Guard` language (§3, AD-8):

```
G ::=  ...
    |  p → q : { lᵢ⟨Tᵢ | φᵢ⟩. Gᵢ }ᵢ∈I       -- payload typed Tᵢ refined by predicate φᵢ
```

A refined branch `lᵢ⟨Tᵢ | φᵢ⟩` is enabled only when the sender’s payload satisfies `φᵢ` and the receiver’s continuation can rely on `φᵢ` holding. Refinements turn business rules and authorization checks from runtime errors into protocol structure.

Refinement well-formedness adds two conditions to the classical three:

1. **Sender provability.** At each branch, the sender’s projection must be able to prove `φᵢ` of the payload it constructs. Unprovable refinements indicate either a bug in the protocol or a missing precondition earlier in the protocol.
1. **Receiver completeness.** The disjunction of refinements at a choice point must cover the input space of the carried type, modulo a default branch. Incomplete coverage indicates a stuck protocol.

These conditions are not always decidable in general, but they are decidable for the fragment of `Guard` Frank uses (Boolean combinations of equalities, comparisons, and state-predicates over a finite signature) by reduction to SMT. Frank’s analyzer suite (§3, AD-X) discharges these obligations at compile time by querying Z3 with the refinement obligations extracted from the typed AST.

Refinements give Frank’s protocols the expressive power that, without them, would force authorization and validation logic into either runtime guards (operational, opaque to the projection theorem) or external specifications (separate from the protocol, drift-prone). With refinements, authorization is part of the protocol and is preserved by projection.

### 2.4 The F# Realization

The denotation lives in mathematics. The F# implementation lives in the artifact. The relationship between them is what Frank’s design is built on, and the relationship is mediated by four F# features that match the formal structure closely:

#### Discriminated unions for the syntactic face

Global types, local types, and refined branches are F# discriminated unions. Pattern matching is exhaustive by default. Structural recursion on protocol terms is the F# pattern matches the compiler verifies. The DU representation is the F# embodiment of the syntactic face of the denotation.

```fsharp
type GlobalType =
    | End
    | Mu of var: TypeVar * body: GlobalType
    | Var of TypeVar
    | Communication of
        sender: RoleId *
        receiver: RoleId *
        branches: Branch list

and Branch = {
    Label: Label
    PayloadType: PayloadType
    Refinement: Guard option
    Continuation: GlobalType
}
```

The denotation correspondence: each value of `GlobalType` is a syntactic presentation of a coalgebra. The value need not carry the coalgebra explicitly; the coalgebra is *derivable* from the value by the structural-recursion procedure described in §2.2.

#### Records of functions for the behavioral face

Coalgebras are records of functions in F#. The structure map of the coalgebra is the record. This is the tagless-final encoding adapted to the coalgebraic setting:

```fsharp
type ICoalgebra<'S> =
    abstract step : state: 'S -> role: RoleId -> Step<'S>

and Step<'S> =
    | SendBranch of Branch<'S> list
    | ReceiveBranch of (Msg -> 'S option)
    | EndStep

and Branch<'S> = {
    Label: Label
    Payload: Msg
    Continuation: 'S
}
```

A specific protocol’s coalgebra is constructed from its global type by a function `coalgebraOf : GlobalType -> ICoalgebra<GlobalType>`. The state space `'S` is `GlobalType` itself (subterms of the original); the structure map is computed by pattern-matching on the current state.

Multiple coalgebras can share a state space and differ only in their structure map — this is how distinct interpreters of the same protocol relate to one another. Each interpreter is a different `ICoalgebra<'S>` instance over the same `'S`. Agreement between interpreters becomes the statement that they yield bisimilar behavior, which is checkable by FsCheck properties at the value level.

#### SRTP for generic operations on representations

Some operations on protocols are independent of the specific representation: duality of session types, structural equality up to recursion unfolding, projection to a role. F#’s statically-resolved type parameters express these as compile-time-resolved generics over types that provide the required structural members:

```fsharp
let inline project< ^G when ^G : (static member Project : ^G * RoleId -> LocalType)>
                  (g: ^G) (r: RoleId) : LocalType =
    (^G : (static member Project : ^G * RoleId -> LocalType) (g, r))

let inline dual< ^L when ^L : (static member Dual : ^L -> ^L)> (l: ^L) : ^L =
    (^L : (static member Dual : ^L -> ^L) l)
```

This lets multiple representations of session types — the canonical `GlobalType` DU, an alternative graph encoding, a generated form produced by codegen — all participate in the same generic operations without an inheritance hierarchy or runtime dispatch. The duality between syntactic and behavioral faces is reflected in the fact that both sides can implement the same SRTP-resolved operations.

#### Codegen for the bridge between specification and runtime

Protocols specified externally (in Scribble notation, in AsyncAPI, in Frank’s own protocol DSL) are compiled to F# by source generators. The generator produces:

1. The `GlobalType` value for the protocol.
1. The per-role `LocalType` values, projected by the projection function.
1. The coalgebra structure map.
1. FsCheck properties asserting that projection is sound for this specific protocol.
1. A `MailboxProcessor`-based runtime stub for each role (§2.4 below).

The generated F# code is normal F# that the compiler typechecks normally. The constraints that make the generation sound are enforced by the generator at generation time. This is the technique TypeProviders use, except generated at build time as static F# rather than dynamically — which is more inspectable and more amenable to AI tooling.

The codegen step is where the duality between formal structure and AI tooling lives operationally. The protocol specification is the formal artifact (or is straightforwardly translatable to one); the generated F# is the operational artifact; the relationship between them is a deterministic, verifiable transformation. AI tools that author or modify protocols work at the specification level and rely on codegen to produce correct runtime structure. AI tools that work with running Frank systems rely on the generated artifacts having predictable shape derived from the specifications.

#### MailboxProcessor for the operational face

Per-role local types deploy as `MailboxProcessor` actors. The match between the coalgebraic functor and `MailboxProcessor`’s operational model is direct:

|Coalgebraic concept            |MailboxProcessor concept             |
|-------------------------------|-------------------------------------|
|State space `S`                |Actor’s internal state               |
|Structure map `α`              |Actor’s `Receive` body               |
|`SendBranch` step              |`Post` to another actor’s mailbox    |
|`ReceiveBranch` step           |`Receive` with pattern matching      |
|`EndStep`                      |Actor termination                    |
|Bisimulation between coalgebras|Behavioral equivalence between actors|
|Coalgebra colimit              |System of communicating actors       |

The match is not approximate. `MailboxProcessor` is asynchronous by default (so asynchronous MPST is native, not an extension). `MailboxProcessor` has a built-in mailbox buffer (so buffer-bounded asynchronous variants are expressible directly). `MailboxProcessor` is purely functional in its message-handling code (so the homomorphism from coalgebra to actor is a translation, not an interpretation). Codegen produces, for each role, a `MailboxProcessor` whose `Receive` body is the local coalgebra’s structure map specialized to that role.

The journal-sourced architecture and the actor-based deployment are the same mechanism viewed at different granularities: the journal is the causally-ordered event log of inter-actor messages, and the actors are the per-role state machines whose state is recoverable by replaying the journal. There is no separate runtime to maintain in addition to the actors; the actors *are* the runtime, and the journal is what they produce by communicating.

### 2.5 The Operational Bridge: SCXML as One Homomorphism

Frank’s operational behavior includes hierarchical states, history pseudostates, internal events, and the SCXML macrostep algorithm. None of these is part of the coalgebraic-session-type denotation as presented above. They are part of how Frank *implements* protocols in HTTP-shaped resources, not part of what the protocols *mean*.

The relationship is: the SCXML algorithm is one homomorphism from the coalgebraic denotation into a particular operational representation (configurations, microsteps, history-tracked transitions). The algorithm is justified not by being the definition of meaning, but by an *adequacy theorem*: for every protocol expressible in Frank’s fragment, the SCXML algorithm computes a trace that is consistent with the coalgebraic denotation’s set of possible behaviors.

This shifts the role of the SCXML transliteration in §8. It is no longer “the specification of what statecharts mean”; it is “the specification of one operational realization, with a stated adequacy property.” Where SCXML’s prose and algorithm disagree (and they do, in corners — the behavior of `<finalize>` in nested invocations, the ordering of history restoration in parallel regions when one region’s history is invalid), the denotation tells you which is correct. Where SCXML and the denotation diverge, the divergence is documented and the implementation follows the denotation.

Hierarchy and history are not denotationally primitive in the coalgebraic-session-type framing. They are *encodings*: a hierarchical state with substates is encoded as a sub-protocol whose recursion structure mirrors the substate structure; history is encoded as additional state in the coalgebra carrying the last-active sub-configuration. The encoding is mechanical and is what the SCXML algorithm is implementing under the hood. Documenting the encoding makes hierarchy and history checkable against the denotation rather than treating them as operational add-ons.

### 2.6 What This Foundation Buys

The denotational foundation is not free — it concentrates work in §2 and §3 that operational specification alone could defer. The payoff is that subsequent sections become specifications of *what* rather than negotiations with *how*:

- **§3 (Architectural Decisions)** recasts the algebras as homomorphisms into representations of the denotation. Each interpreter is justified by what homomorphism it implements; agreement between interpreters is the statement that they implement homomorphisms into related carriers.
- **§7 (Algebra Specifications)** specifies the homomorphism each algebra implements, with the soundness criterion stated as the commutative-diagram law.
- **§10 (Multi-Party Projections)** is the section where the foundation pays off most directly. Projection soundness is the homomorphism property; well-formedness is the precondition; the analyzer suite discharges the well-formedness obligations at compile time.
- **§12 (Conformance and Falsifiability)** can state precise properties: bisimulation between interpreters, projection soundness for specific protocols, refinement satisfiability. Each property has an FsCheck or analyzer-based check.
- **The MPST approximation paper** has its denotational target named. The publication contribution is the bridge between coalgebraic semantics and session-typed projection in a production framework — the duality made operational.

References:

- Honda, K., Yoshida, N., & Carbone, M. (2016). Multiparty Asynchronous Session Types. *JACM* 63(1).
- Jacobs, B. (2017). *Introduction to Coalgebra: Towards Mathematical Modelling of State-Based Systems*. Cambridge University Press.
- Castellan, S., Yoshida, N. (2019). Two Sides of the Same Coin: Session Types and Game Semantics. *POPL 2019*.
- Carette, J., Kiselyov, O., & Shan, C. (2009). Finally Tagless, Partially Evaluated. *JFP* 19(5).
- Rutten, J.J.M.M. (2000). Universal coalgebra: a theory of systems. *TCS* 249(1).
- W3C (2015). State Chart XML (SCXML). https://www.w3.org/TR/scxml/
- Harel, D. (1987). Statecharts: A Visual Formalism for Complex Systems. *SCP* 8(3).

Categorical detail for the duality between global types and coalgebras, the precise statement of projection as homomorphism, and the operational adequacy theorem for the SCXML algorithm appears in Appendix [TBD: §15.X to be added in pass 3].

-----

## 3. Architectural Decisions

The architectural decisions below are organized around the denotational foundation in §2. Each decision names what it commits Frank to — in terms of the denotation, the F# realization, and the properties the architecture claims. Concerns compose through three contribution surfaces (algebras, builders, type extensions), explicitly enumerated, so composition is never accidental and never hidden.

### AD-1: Separate Types Per Concern

Each semantic concern — session types, statecharts, validation, provenance, linked data — owns its type definitions in its own Core package. Concerns compose through explicit bindings at the denotation level and through explicit type extensions at the builder level (AD-13), not through a unified AST.

`Frank.SessionTypes.Core` holds global types, local types, refinement predicates, projection, the generic hook family, and the `RoleExtractor` interface. `Frank.Statecharts.Core` holds statechart AST, transition edges, and configuration predicates. `Frank.Validation.Core` holds SHACL shape types. `Frank.Provenance.Core` holds PROV-O types. `Frank.LinkedData.Core` holds RDF types. None depends structurally on the others; they are related at the denotational level by homomorphisms (AD-4) and at the integration level by explicit composition packages (AD-13).

The rejected alternative — a unified AST embedding every concern into every other — was considered and rejected in §4. The rejection is grounded in what §2 establishes: the denotation is a mathematical object that multiple syntactic presentations refer to. Forcing one presentation to subsume the others is the wrong direction; presentations diverge, denotations compose, and composition happens at named joints.

### AD-2: Algebras as the Contract Surface for Each Concern

Every concern exposes its operations as tagless-final algebras. Each algebra’s carrier is a type parameter `'r`; each interpreter is a homomorphism from the concern’s syntactic presentation into a specific carrier that represents one way of using the concern’s values. Algebras are the *internal contract surface* — the commitment a concern makes to interpreter implementers about what operations exist and what they mean.

Frank ships ten algebras across five concerns:

**Session types (`Frank.SessionTypes.Core`):**

- `IProtocolBuilder<'r>` — constructs global session types. Interpreters: build a `GlobalType` DU; emit Scribble source; emit AsyncAPI; emit a graphical representation; emit a session-type summary for documentation.
- `IProjectionAlgebra<'r>` — projects global types to per-role local types. Interpreters: compute the local type as a `LocalType` DU; compute the projection symbolically for analysis; compute per-role `MailboxProcessor` message handlers.
- `ILocalTypeAlgebra<'r>` — operates on local types (duality, sequencing, composition). Interpreters: compute the dual; check structural conformance with a candidate implementation; derive the local type’s runtime behavior.

**Statecharts (`Frank.Statecharts.Core`):**

- `IStatechartBuilder<'r>` — constructs statechart structure. Interpreters: build the AST; emit SCXML, smcat, mermaid, ALPS; compute a reachability graph; translate to session-type form.
- `ITransitionAlgebra<'r>` — interprets transition effects. Interpreters: runtime state update; generate F# source for a transition; symbolic analysis; trace collection.
- `IConfigurationPredicate<'r>` — evaluates predicates over active configurations. Interpreters: Boolean evaluation; SMT satisfiability; predicate-overlap analysis.

**Validation (`Frank.Validation.Core`):**

- `IShapeBuilder<'r>` — constructs SHACL shapes. Interpreters: build the shape AST; emit Turtle; emit JSON-LD; derive shape descriptions from F# types at runtime (type → SHACL emission).
- `IConstraintAlgebra<'r>` — checks and analyzes constraints. Interpreters: check a candidate RDF graph against a shape; symbolic satisfiability of the constraint language; constraint-to-refinement translation for integration with session-type refinements.

**Provenance (`Frank.Provenance.Core`):**

- `IProvenanceBuilder<'r>` — constructs PROV-O graph fragments. Interpreters: build the RDF graph representing a provenance record; emit a PROV-O trace for visualization; derive provenance summaries for audit reports.

**Linked data (`Frank.LinkedData.Core`):**

- `IGraphBuilder<'r>` — constructs RDF graph fragments. Interpreters: build a dotNetRdf `IGraph`; emit Turtle, JSON-LD, or N-Triples; compute graph isomorphism certificates for round-trip testing.

A single algebra over a uniform `'r` carrier would conflate incompatible concepts and force interpreters to inspect what kind of `'r` they have, defeating the point of tagless-final. More importantly, the ten algebras correspond to distinct mathematical structures in §2 — each concern has its own denotational object — and conflating them would blur the homomorphism claims that make the architecture sound.

Each algebra has the same soundness criterion: an interpreter is valid iff it is a homomorphism into its carrier, meaning the structure-preservation equations of §2.2 hold. For session types specifically, projection soundness (§2.2’s commutative-diagram property `αᵣ ∘ π_r = F(π_r) ∘ α`) is checked by the analyzer suite (AD-11). For other algebras, soundness is verified by FsCheck properties over generated inputs.

### AD-3: Journal-Sourced Runtime as Coalgebra Unfolding

`IStatechartJournal` is the source of truth for agent state. `AgentState` is a derived projection, recomputable by journal replay. Per §2.4, the journal is the causally-ordered event log of per-role `MailboxProcessor` actors; the actors *are* the runtime, and the journal is what they produce by communicating. The architecture does not maintain a separate “runtime” layer distinct from the actors.

Operationally: every `Fire` journal-appends before acknowledging, snapshots cache the projection at journal position N, and the journal interface is pluggable.

Denotationally: the journal is an unfolding of the coalgebra `α : S → F(S)` along a specific event history. Each journal entry records one coalgebra step taken by one role. Replaying the journal reconstructs the state by composing the recorded steps. Provenance derivation (the journal-to-PROV-O homomorphism established in §2.5, implemented in the `Frank.Statecharts.Provenance` composition package) reads the journal and produces a PROV-O graph; this is *another* homomorphism, not a separate data path.

**SQLite per actor as the default implementation.** The default `IStatechartJournal` is SQLite-per-actor: each `MailboxProcessor` actor owns a SQLite database file containing its journal (the event log) and snapshots (cached projections). Per-actor isolation matches the coalgebraic framing — each actor is a coalgebra over its own state space, and the denotation has no shared state between roles. Crash recovery per actor is a local operation: open the database, read the journal, reconstruct state; other actors are unaffected.

Memory budget: ~400 KB per resident actor with default SQLite settings (configurable down to ~200 KB per actor by reducing page cache to 100 KiB via `PRAGMA cache_size`). At 10,000 resident actors on default settings, the budget is ~4 GB for SQLite bookkeeping alone; tuning for density is possible but bounds how much performance headroom hot actors have. The runtime closes idle actors’ SQLite handles after a configurable timeout (reopening at ~5 ms latency), preserving `MailboxProcessor` addressability while releasing storage resources. This matches the pattern validated by Cloudflare Durable Objects and by Gleam Warp, both of which use SQLite-per-actor at production scale.

**LMDB as an alternative under memory pressure.** For deployments that exceed SQLite’s per-actor footprint, LMDB is a viable alternative: ~50 KB per-actor overhead, memory-mapped reads that can share backing pages across actors when the operating system permits, MVCC isolation, and append-only writes. LMDB’s trade-off is a weaker crash model — memory-mapped writes can leave the database in an inconsistent state under power loss, though LMDB’s MVCC design mitigates this for normal process crashes. Snapshot recovery requires more care than SQLite’s WAL mode provides. The `IStatechartJournal` interface remains pluggable so LMDB (or Redis, NATS JetStream, Kafka) can replace SQLite without changes to the runtime above.

The interface is pluggable; SQLite-per-actor is the default and the target for operational adequacy testing in Phase 1.

### AD-4: Homomorphism Bridges, Not AST Conversion

Each algebra has a bridge between its syntactic presentation (an AST type) and its polymorphic form (a program over the algebra interface). `StatechartDocument` ↔ `Statechart`. `TransitionEdge` ↔ `TransitionProgram`. `GlobalType` ↔ `Protocol`. `NodeShape` ↔ `Shape`. `Graph` ↔ `GraphFragment`. And so on for each concern’s AST/polymorphic pair.

These bridges are explicit homomorphisms: reflecting an AST into its polymorphic form does not lose information, and reifying a polymorphic form back produces an AST that denotes the same object. Parsing produces AST; interpretation uses the polymorphic form; conversion is explicit and lossless.

Having *one bridge per algebra* rather than a universal reflect/reify mechanism follows from AD-2’s disjoint carriers. A universal mechanism would imply a universal carrier, which would imply a universal algebra, which would conflate the distinct mathematical structures. Each concern owns its bridge.

### AD-5: Composition Model with Predicate Keys

Statechart-keyed bindings (affordances, validation shapes, handlers) in a stateful resource are keyed on `ConfigurationPredicate`, not `StateId`. The composition model at the statechart-binding level uses predicate keys:

```fsharp
type StatefulResourceBinding<'TEvent> = {
    Statechart: StatechartDocument
    Handlers: (ConfigurationPredicate * Handler<'TEvent>) list
    Affordances: (ConfigurationPredicate * Affordance list) list
    Validation: (ConfigurationPredicate * NodeShape) list option
    // ...
}
```

A `Map<StateId, _>` key would silently drop expressivity for parallel regions — “this affordance only applies when region A is in `Approved` AND region B is in `PaymentCleared`” would be inexpressible. A predicate key (`InState s`, `InAll [s; t]`, etc.) is the minimal correct generalization that parallel composition requires. Single-state cases stay concise: `whenIn "Draft" [...]` desugars to `InState (StateId "Draft")`.

This decision aligns composition with the coalgebraic denotation. At each active configuration, predicates applicable there select their bindings; the set of active bindings is determined by the configuration, which is determined by the coalgebra’s unfolding. Parallel regions compose correctly because predicates over configurations are conjunction-closed, matching how AND-states compose in the denotation.

Lookup is O(n × predicates) instead of O(log n) Map lookup, mitigated by predicate indexing on the leading `InState` term. Analyzers detect overlapping predicates (FRANK215) and unsatisfiable predicates (FRANK216).

`StatefulResourceBinding` is one of several concern-specific bindings (see AD-13); it is not the universal composition model. Other concerns have their own binding types produced by their own builders. The cross-concern composition model is the set of type extensions on Frank core’s builders (AD-13) plus the composition packages (AD-13) that weave concerns together.

### AD-6: Role Projections Are Coalgebra Homomorphisms, with Pluggable Assignment

Per-role projections are not mechanical syntactic operations that happen to seem right — they are coalgebra homomorphisms from the global denotation to per-role local denotations, as specified in §2.2. Frank derives them from the global session type (or, equivalently, from the statechart plus a `RoleParticipation` schema; the two surfaces produce the same `RoleParticipation`, per the session-type-to-statechart correspondence in §2.5).

The soundness criterion is the commutative-diagram property `αᵣ ∘ π_r = F(π_r) ∘ α`. This is not a wish; it is a mathematical statement that Frank’s analyzer suite checks by verifying the well-formedness conditions of §2.2 and §2.3 (sender determinacy, knowledge of choice, no orphan messages, sender provability, receiver completeness).

Hand-authored `RoleOverride` exists as an escape hatch for cases where mechanical projection needs adjustment, but it produces analyzer warnings (FRANK220) and is expected to be rare. The primary pathway: authors specify role participation at the transition level (or write the protocol directly in `Frank.SessionTypes.Core`), well-formedness is machine-checked, projection is the homomorphism, and the per-role view that deploys as a `MailboxProcessor` actor is by construction faithful to the global protocol.

**Role assignment is pluggable.** Projection is the mathematical operation; *assignment* — how the runtime knows which role a given request belongs to — is the operational question of who’s playing which role. These are different concerns and are settled separately. Frank defines `RoleExtractor` as an interface:

```fsharp
type RoleExtractor =
    HttpContext -> ProtocolInstance -> RoleId option

and ProtocolInstance = {
    ProtocolId: ProtocolId
    InstanceScope: ResourcePath
}
```

Role assignment is per-protocol-instance, not per-protocol-type. A user may be `X` in `/games/abc123` and `Viewer` in `/games/xyz789`, simultaneously. The extractor is called with the specific protocol-instance the request is targeting and returns the role (if any) the user has for that instance.

**Hierarchical role scoping: most-specific-wins.** The runtime resolves role assignments by walking the resource hierarchy from most-specific to least-specific scope. A user who is `Viewer` at `/games` and `X` at `/games/abc123` is resolved as `X` when the request targets `/games/abc123` or any descendant. Standard hierarchical lookup pattern (as in URL routing, CSS specificity, filesystem permissions); analyzer rules FRANK225 and FRANK226 detect incompatible child-scope roles and unreachable parent-scope roles respectively.

**Frank ships `Frank.SessionTypes.Core` with the `RoleExtractor` interface and a `ClaimsRoleExtractor` default implementation.** The default reads from `ClaimsPrincipal.Claims` using a Frank-specific claim URN (`urn:frank:session-role`; exact URN specified in §6). `System.Security.Claims` is base .NET (not ASP.NET-Core-specific), so the default implementation preserves AD-7’s independence properties.

`ClaimsRoleExtractor` is suitable for static role assignment — claims issued at login, stable for the session. Other assignment patterns are user-implementable against the interface:

- *Actor-backed role tracking* (for dynamic in-session role changes, the pattern used in `frank-fs/tic-tac-toe` where a user becomes `X` when joining a game and ceases to be `X` when leaving). A separate actor maintains authoritative role state; the extractor consults it per request.
- *External-service role lookup* (for roles governed by an entitlement API, policy engine, or organizational directory). The extractor calls out to the service.

Both patterns are straightforward to implement in user code. Optional future packages (`Frank.SessionTypes.ActorTracked`, `Frank.SessionTypes.External`) may ship if demand warrants; v1 provides the interface and the claims-based default only.

Hand-authored projections without the denotational target are a correctness hazard at scale: no soundness check, every projection a place a bug could hide, duplication of transition-level information across per-state entries. With the denotational target, the soundness check *is* the well-formedness check, and the projection *is* the homomorphism.

### AD-7: Core Packages Are Independent of ASP.NET Core and of Each Other

Every `Frank.*.Core` package depends only on FSharp.Core and on `System.Security.Claims` where the Core package’s interface requires it (specifically `Frank.SessionTypes.Core`’s `ClaimsRoleExtractor`; `System.Security.Claims` is base .NET, not ASP.NET-Core-specific). None depends on ASP.NET Core or on HTTP concepts. They are reusable for non-HTTP contexts: CLI tools, background services, agent runtimes, test harnesses that replay journals offline.

**A stronger independence property: each `Frank.*.Core` package depends on no other `Frank.*.Core` package.** This is a deliberate constraint. It means a user who imports `Frank.Validation.Core` for standalone SHACL work does not transitively pull in `Frank.SessionTypes.Core`, `Frank.Provenance.Core`, or any other concern’s types. Each concern’s Core is a true standalone package.

The `Frank.` prefix is a project-wide naming convention, not a dependency claim. Independence here means *compiles and runs without ASP.NET Core and without other concerns*, which every `Frank.*.Core` package does.

HTTP integration concepts (handlers, affordances, middleware, content negotiation) live in the non-Core packages: `Frank.Statecharts`, `Frank.Validation`, `Frank.Provenance`, `Frank.LinkedData`, `Frank.Discovery`, and in composition packages that require HTTP integration. These depend on ASP.NET Core by design.

Composition packages (AD-13) are where concerns meet; they depend on the Core packages they compose. The composition-package-level dependencies are the *only* places cross-concern dependencies exist.

### AD-8: Closed Guard Language Is Part of the Denotation

Guards use a closed expression language that §2.3 incorporates into the denotation as refinement predicates on message payloads:

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

The closed form is load-bearing at multiple layers:

- **Denotationally:** refinement session types from §2.3 require a decidable predicate language to keep projection soundness checkable. The closed `Guard` language is Frank’s decidable fragment.
- **Operationally:** guards are statically analyzable, comparable for equivalence, and evaluable without an embedded language runtime.
- **Analyzer-wise:** refinement satisfiability reduces to SMT queries over Boolean combinations of equalities, comparisons, and state predicates — a fragment Z3 handles efficiently.

SCXML import maps `cond="…"` expressions into the closed form; expressions outside the form fail at import time with clear errors. An open extension point (`Custom of obj`) is reserved for future work if needed; v1 ships closed.

### AD-9: Scheduled Events Are First-Class

SCXML’s `<send delay="…">` and `<cancel>` are first-class. Scheduled events are journaled at the moment they are scheduled, with the target firing time. The runtime includes a scheduler that fires them; firing is itself journaled.

Half of realistic HTTP workflows have timeouts. Deferring them forces every adopter to invent their own scheduler, which then doesn’t compose with snapshots, projections, or audit.

`IStatechartScheduler` has implementations: in-memory (tests), file-backed (single-node), database-backed (multi-process). Scheduled events are durable across restart because they are journaled. Cancellation is journaled; the scheduler reconciles by skipping cancelled-then-fired events.

Denotationally, scheduled events extend the coalgebraic functor with a time dimension — each step can optionally commit to a future event at a specified time. This is the natural fit for asynchronous MPST extensions; the scheduler is the operational realization of that time-indexed coalgebra.

### AD-10: SCXML Operational Algorithm as One Adequate Homomorphism

The macrostep implementation is a direct transliteration of the SCXML Algorithm for SCXML Interpretation (W3C SCXML Appendix D). The implementation is justified not by being the *definition* of what statecharts mean, but by an *operational adequacy theorem* relating it to the coalgebraic denotation of §2.2.

Per §2.5, the SCXML algorithm is one homomorphism among several from the denotation into operational representations. The adequacy claim: for every protocol expressible in Frank’s fragment, the SCXML algorithm computes a trace consistent with the coalgebraic denotation’s set of possible behaviors. Where SCXML prose and algorithm disagree — and they do, in corners — the denotation is the arbiter, and the implementation follows the denotation.

**Adequacy is verified by property-based testing in v1.** Frank builds a reference interpreter that operates directly on the coalgebraic structure map (`ITransitionAlgebra<'r>` with `'r` a reference-interpreter carrier, per AD-2); Phase 1’s conformance suite includes FsCheck properties asserting that the trace produced by the SCXML algorithm for any generated well-formed protocol is contained in the set of traces the reference interpreter produces. This is engineering-grade verification — bounded random testing across the protocol space, failures blocking PR merges, no formal proof but no unchecked claim either. Mechanical proof of operational adequacy (in Coq, Lean, or F*) is a known v2/v3 aspiration; the architecture aims high and gates just below with property-based testing.

SCXML conformance is maintained where the W3C SCXML 1.0 Implementation Report Plan tests pass. Where Frank deviates for denotational correctness, the deviation is documented and tested. The runtime specification in §8 includes pseudocode that maps line-by-line to SCXML Appendix D, with specific deviations called out.

Hierarchy and history are not denotationally primitive in the coalgebraic-session-type framing; they are encodings (§2.5). The encoding is mechanical and is what the SCXML algorithm implements under the hood. Statechart parsers and generators treat statecharts as a presentation of session-typed protocols, using the encoding to convert between the two presentations losslessly.

### AD-11: Analyzer Suite as Extended Type Checker

Frank’s analyzer suite is part of the language extension, not an add-on. The analyzers discharge proof obligations that F#’s type system cannot express but that the denotation requires.

**Analyzer rule numbering is organized by category.** Each category occupies a hundred-block, giving room to grow:

|Range       |Category   |Purpose                                                                                                                        |
|------------|-----------|-------------------------------------------------------------------------------------------------------------------------------|
|FRANK001–099|Conventions|Project-wide style and structural conventions (existing: `FRANK001` — module declarations only)                                |
|FRANK100–199|Structural |Bindings-level and AST-level correctness — duplicate handlers, missing handlers, unresolved references, unreachable states     |
|FRANK200–299|Semantic   |Denotational-level correctness — reachability, deadlock, predicate satisfiability, role completeness, role-override consistency|
|FRANK300–399|Refinement |Session-type refinement correctness — sender provability, receiver completeness, refinement satisfiability via Z3              |
|FRANK400–499|Composition|Cross-concern composition failures — operation-name collisions, missing prerequisites, builder-output mismatches               |
|FRANK500–599|Codegen    |Per-protocol hook-record completeness, signature match, role coherence, generated-file hand-modification                       |
|FRANK600+   |Reserved   |Future categories                                                                                                              |

Within structural, 100–109 are reserved for the child-resource and codegen rules defined in D-GH19 (issue #285).

Representative rules in each category (full list in §6 or §7; this AD commits Frank to the categories and to maintaining rules within them):

- **Structural (FRANK110–118):** duplicate handler, missing handler coverage, unresolved transition reference, undefined transition target, overlapping predicates with conflicting affordances, reference to non-existent state, compound state missing initial child, final state has children.
- **Semantic (FRANK210–222):** unreachable state, deadlock-reachable configuration, eventless-transition cycle, unreachable transition, unsatisfiable predicate, unsatisfiable guard, variable used but never assigned, scheduled event with no canceller, manual `RoleOverride` conflicts with derived projection (FRANK220), role with no triggerable transitions, configuration with no triggerable transition for any role, child-scope role incompatible with parent protocol (FRANK225), parent-scope role never assigned by any child (FRANK226).
- **Refinement (FRANK300–301):** refinement on branch is unprovable by sender (sender provability), refinements at choice point do not cover input space (receiver completeness). Discharged via Microsoft.Z3.
- **Composition (FRANK400–402, FRANK420):** operation-name collision across concerns (FRANK400); composition operation used without prerequisite concerns imported (FRANK401); builder output passed to incompatible operation (FRANK402); `useSessionTypes` called without a registered `RoleExtractor` (FRANK420).
- **Codegen (FRANK500–510):** hook field required by protocol but not supplied in `roleHandlers { }` CE (FRANK500); hook supplied with wrong signature (FRANK501); hook supplied for role the caller isn’t implementing (FRANK502); stale hook supplied for field removed from protocol (FRANK503); generated file hand-modified without regeneration (FRANK510).

Refinement rules invoke Microsoft.Z3 via the analyzer pipeline. Refinement obligations are extracted from the typed AST, shipped to Z3, and reported as compile errors with unsat-core highlighting when unsatisfiable. Verification time is bounded per protocol (30-second target).

This is the F#-native analog of what dependently-typed languages enforce at the type level. The user experience is normal F# compile errors; the verification machinery is in the analyzer. AI tools working with Frank code target the analyzer rules as a checkable specification — the analyzer suite is a contract surface that AI generation can be held accountable to.

### AD-12: Codegen Bridges from Specifications to F# Code

Frank ships codegen in two flows with different v1 and v2 scopes, distinguished by the input: CE-driven codegen takes an F# CE value as input and runs at build time via Myriad; format-driven codegen takes an external specification file as input and runs via the gRPC-style MSBuild integration.

**CE-driven codegen (v1).** Each concern’s nested CE produces a typed value (`GlobalType`, `StatechartDocument`, etc.). Myriad plugins operate on the CE source syntactically (via Fantomas AST extraction, not via CE evaluation) and produce specialized F# artifacts. The Myriad substrate sidesteps the chicken-and-egg problem that design-time type providers would introduce: the plugin reads source, not compiled values, so the protocol CE and the generated artifacts compile in a single build pass without multi-project or multi-pass ceremony.

For session-typed protocols, CE-driven codegen via `Frank.SessionTypes.Codegen` produces **fully-implemented per-role `MailboxProcessor` actors**, not stubs. The generated actor includes: the projected local type as a strongly-typed message DU; the state machine (the actor’s `Receive` body) generated from the local type’s structure-map with message dispatch, state transitions, and conformance-checking-by-construction (a message the local type forbids is unrepresentable in the typed DU; unexpected incoming messages hit a generated default branch that records protocol violation); refinement enforcement at message boundaries (outgoing refinements checked for sender provability before send; incoming refinements trusted per sender provability obligation); outgoing message construction to the correct destination actor; and a generated `RoleHooks<ThisRole>` record type whose fields are typed to the application hooks this role needs.

The `RoleHooks<...>` record is the contract surface between the protocol (generated) and the application (user-written). Its field count and signatures are determined by the local type — choice points that the protocol leaves to the application become hook fields, domain computations needed from received data become hook fields, side effects intrinsic to the role’s behavior become hook fields. **The hook signatures conform to a generic hook family defined in `Frank.SessionTypes.Core`.** This is the architectural contract surface for hooks regardless of generation path: the generic family is present in Core; the Myriad-generated `RoleHooks<...>` is a specialization of the generic family against the specific protocol’s user types. Users can opt out of Myriad codegen and write against the generic family directly — useful for simple protocols, testing, and non-MSBuild scenarios — but the default v1 path uses the Myriad-generated specialization for better ergonomics.

Hooks are supplied via a hand-written `roleHandlers { }` CE shipped with `Frank.SessionTypes`; the CE’s `Run` produces an inhabited `RoleHooks<...>` record passed to the generated actor constructor. Analyzer rules FRANK500–503 check hook-record completeness at compile time.

For statecharts, `Frank.Statecharts.Codegen` provides a Myriad plugin that operates on `statechart { }` CE source and produces typed state DUs (replacing stringly-typed `StateId`), typed event DUs (replacing stringly-typed `EventId`), and transition program functions that the runtime can execute without re-walking the AST.

CE-driven codegen for other concerns is not included in v1. Validation, Provenance, and LinkedData ship without per-concern Myriad plugins in v1; runtime construction of their types via the algebra interfaces from `Frank.*.Core` is the v1 pathway. Runtime type → SHACL emission lives in `Frank.Validation.Core` as a serialization helper for content-negotiated shape descriptions; this is runtime serialization, not codegen.

Phase 0c of the build plan (§11) delivers `Frank.SessionTypes.Codegen` and `Frank.Statecharts.Codegen`. The generated F# is normal F# that the compiler typechecks normally. Constraints that make the generation sound are enforced by the generator at generation time. This is the technique TypeProviders use, except generated at build time as static F# rather than dynamically — which is more inspectable and more amenable to AI tooling.

**Format-driven codegen (v2).** Compilation from external specification formats to F# artifacts, using the FSharp.GrpcCodeGenerator / FsGrpc.Tools pattern referenced in issue #285 as the MSBuild-integration template. The pipeline is parser → AST → algebra interpreter → emitted F# file. Format-driven codegen presupposes the canonical F# shape produced by CE-driven codegen has stabilized, which is why it follows v1. Scope is finalized after v1 ships. Formats in view for Phase 8 include: WSD, SCXML, smcat, mermaid, XState, PlantUML (statecharts); Scribble, AsyncAPI (session types); SHACL files in Turtle, JSON-LD, N-Triples (validation); JSON Schema (validation); ALPS in YAML, XML, JSON (linked data and hypermedia); OpenAPI YAML/JSON (for the deferred `Frank.Validation.OpenApi` composition). The full v2 phase lives in §11 Phase 8; this AD names the direction.

Codegen is where the duality hypothesis of the architecture lives operationally. In v1, the duality is embodied through CE authoring itself: the CE is both the specification and the implementation, with Myriad producing verified specializations and the F# compiler plus analyzer suite verifying both sides fit together. The AI-tools-and-formal-structure feedback loop is present in v1 — the harder the formal structure at the CE level, the more legible the generated artifacts become, and the more capable AI tooling becomes at extending the specifications. V2 broadens the specification surface to external formats; it doesn’t introduce the mechanism.

Codegen lets Frank express combinators that F#’s type system cannot enforce at the type level: linear channel usage, type-level role enumeration, message-tag uniqueness. The generator enforces them at generation time; the generated code uses normal F# types. This is the F# realization of what Haskell and Idris achieve through type-level programming.

### AD-13: Concerns Extend Frank Core via Three Contribution Surfaces

Every concern contributes to Frank through exactly three surfaces:

1. **Algebras** (AD-2) — the internal contract for interpreter implementers. Tagless-final, carrier-polymorphic, homomorphisms into the concern’s denotation.
1. **Nested CEs (builders)** — the user-facing authoring surface for that concern’s values in isolation. Each nested CE’s `Run` produces a typed value (a `Graph`, a `NodeShape`, a `GlobalType`, a `StatechartDocument`, a `StatefulResourceBinding`, a `RoleParticipation`, etc.). Nested CEs are the pit-of-success for authoring the concern correctly; they are available whether or not Frank’s HTTP machinery is in use.
1. **Type extensions to Frank core** — operations added to `ResourceBuilder` and `WebHostBuilder` via F# type extensions, accepting the nested CEs’ output values and integrating them with HTTP. This is the composition surface where the concern meets other concerns through the shared outer builders.

The pattern follows the established Frank idiom from `Frank.Auth`, `Frank.OpenApi`, and `Frank.Datastar` (D-SK27, D-SK29, D-SK3, D-PR129), generalized to the full concern set.

Contribution inventory (nested CEs, key operations, CE-driven codegen outputs):

|Concern           |Algebras|Nested CEs                                                 |Key ResourceBuilder ops     |Key WebHostBuilder ops                                 |CE-driven codegen output (v1)                                                                               |
|------------------|--------|-----------------------------------------------------------|----------------------------|-------------------------------------------------------|------------------------------------------------------------------------------------------------------------|
|Frank.SessionTypes|3       |`protocol { }`, `roleParticipation { }`, `roleHandlers { }`|`role`                      |`useSessionTypes`                                      |Specialized `RoleHooks<...>`, message DUs, `MailboxProcessor` actor, projection-soundness FsCheck properties|
|Frank.Statecharts |3       |`statechart { }`, `stateful { }`                           |`stateful`                  |`useStatecharts`, `useRoleProjection`, `useAffordances`|Typed state DUs, typed event DUs, transition program functions                                              |
|Frank.Validation  |2       |`shapes { }`                                               |`validate`, `validateWhenIn`|`useValidation`                                        |— (v2 via format-driven codegen)                                                                            |
|Frank.Provenance  |1       |`provenance { }`                                           |`track`, `provenance`       |`useProvenance`                                        |—                                                                                                           |
|Frank.LinkedData  |1       |`graph { }`                                                |`representation`, `graph`   |`useLinkedData`                                        |—                                                                                                           |

Operations may be flat (accept a built value as a parameter) or accept nested-CE output directly (`stateful { ... }` inside `resource { }`). The choice is per-operation based on whether the carried value is simple or compound. Both flat and nested operations remain extensible — additional operations, additional sub-operations within nested CEs, and additional composition-package extensions can be added without modifying the concern’s Core.

**Response representations delegate to ASP.NET Core, view engines, and existing Frank machinery.** Frank does not ship a representation system. Handlers return whatever ASP.NET Core can serialize (`IResult`, Oxpecker views, Giraffe view engine output, raw strings, JSON-serializable records); the handler context exposes Frank-specific values (active configuration, role projection, affordances) that the developer composes with their chosen view library’s primitives. Format negotiation for RDF formats is handled by `Frank.LinkedData`; other formats are configured at the ASP.NET Core host level via standard output formatters. Reactive UI integrations (Datastar, etc.) are hand-wired in application code per the `frank-fs/tic-tac-toe` pattern; no Frank package is needed because the existing machinery already supports it.

**Composition packages contribute only type extensions.** A composition package named `Frank.X.Y` (where X and Y are two concerns) adds operations to `ResourceBuilder` and `WebHostBuilder` that make sense only when both `Frank.X` and `Frank.Y` are imported. Composition packages do not introduce new algebras and do not introduce new nested CEs. They weave existing concerns at the resource or webhost level.

Frank v1 ships eight composition packages:

- `Frank.Statecharts.SessionTypes` — statechart-to-session-type translation (§2.5); session-type-to-statechart presentation. *Built in Phase 0b (foundational for runtime).*
- `Frank.SessionTypes.Validation` — refinements on message payloads (§2.3). *Built in Phase 0b (foundational for refinement-aware runtime).*
- `Frank.SessionTypes.Auth` — role-based authorization composing `ClaimsPrincipal` with session-typed role constraints.
- `Frank.Statecharts.Validation` — SHACL shapes bound to configuration predicates.
- `Frank.Statecharts.Provenance` — journal-to-PROV-O homomorphism.
- `Frank.Statecharts.LinkedData` — ALPS affordance derivation from statechart plus role projection.
- `Frank.Validation.LinkedData` — SHACL shape Link headers and content-negotiated shape serving.
- `Frank.Provenance.LinkedData` — PROV-O graphs as RDF, content-negotiated provenance serving.

Deferred composition packages (v2 candidates, shipped if demand appears):

- `Frank.Validation.OpenApi` — SHACL-to-OpenAPI-schema derivation. Most of the value is captured by format-driven codegen producing F# types from SHACL shapes (Phase 8); the composition becomes worthwhile if SHACL shapes become primary schema-authoring surface for Frank users.

Out of scope (not planned):

- `Frank.Statecharts.Datastar` — Datastar composition with role-projected SSE streams. Not planned because Datastar sits downstream of response data as a delivery choice; composition happens naturally in user-written handlers (per the `frank-fs/tic-tac-toe` pattern), without a Frank-provided package.
- `Frank.Statecharts.OpenApi` — state-annotated OpenAPI. Not planned because OpenAPI is used in the standard way; users wanting state-aware hypermedia features look to ALPS, Link headers, and the affordance machinery that Frank already provides.

**The Voltron property.** Every concern works standalone — algebras, nested CEs, operations all available via that concern’s Core plus its HTTP integration package. Every pair of concerns that Frank v1 supports has an explicit composition package; the composition is never implicit. When a composition gap appears (as with the historical “SHACL shapes not exposed to ALPS affordances” failure), the gap is a missing composition package, visible in the package list, not a silent omission inside a package that should have done two things.

### AD-14: Analyzer Rules for Composition

Operation-name collisions and composition-package prerequisites are checked by dedicated analyzer rules in the FRANK400 series (AD-11). Four rule categories at v1:

**FRANK400 — Operation-name collision across concerns.** When two imported packages contribute operations with the same name to the same builder type, the analyzer reports the collision at compile time with both operations’ sources named. Users resolve by qualifying one of the uses or by not importing both packages. The collision itself does not prevent compilation, but the warning is promoted to an error if `<TreatWarningsAsErrors>` is set (per the existing FRANK09 convention from D-PR128).

**FRANK401 — Composition operation used without prerequisite concerns.** Composition packages contribute operations that depend on types from two or more concerns. When such an operation is used in a CE where one of the prerequisite concerns is not imported, the analyzer reports the missing concern. Catches the failure mode where a user adds a composition-package dependency but forgets to import one of the underlying concerns.

**FRANK402 — Builder output passed to an incompatible operation.** When a nested CE’s output is passed to an operation expecting a different concern’s type (e.g., a `GlobalType` value passed where a `NodeShape` is expected), the analyzer reports the mismatch. F#’s type system catches most cases of this; the analyzer catches cases where structural inference could produce ambiguity, particularly across type extensions that share operation names.

**FRANK420 — `useSessionTypes` called without a registered `RoleExtractor`.** Session-type projection has no input without a role extractor; a missing extractor at webhost-config time is detectable statically. The analyzer flags the missing registration and lists the available default implementations.

These rules enforce the Voltron discipline at compile time. AI tools generating Frank code target these rules as part of the analyzer contract surface; composition failures that would otherwise manifest as method-resolution ambiguity or runtime type errors are caught and reported in composition-package vocabulary.

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

Per AD-1, every semantic concern owns its types in its own Core package. Per AD-7, every Core package depends only on FSharp.Core (plus `System.Security.Claims` for `Frank.SessionTypes.Core`), not on ASP.NET Core and not on any other Core. Per AD-13, composition between concerns happens in explicit composition packages that contribute only type extensions. The layout below realizes these commitments.

### 5.1 Dependency Graph

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  LAYER 0: Concern Cores (FSharp.Core + optional System.Security.Claims)      │
│                                                                              │
│  ┌─────────────────────────┐  ┌─────────────────────────┐                    │
│  │ Frank.SessionTypes      │  │ Frank.Statecharts       │                    │
│  │   .Core                 │  │   .Core                 │                    │
│  │  3 algebras; global /   │  │  3 algebras; AST;       │                    │
│  │  local types; generic   │  │  ConfigurationPredicate;│                    │
│  │  hook family;           │  │  Guard; Action          │                    │
│  │  RoleExtractor +        │  │                         │                    │
│  │  ClaimsRoleExtractor    │  │                         │                    │
│  └─────────────────────────┘  └─────────────────────────┘                    │
│                                                                              │
│  ┌─────────────────────────┐  ┌─────────────────────────┐  ┌──────────────┐  │
│  │ Frank.Validation        │  │ Frank.Provenance        │  │ Frank.Linked │  │
│  │   .Core                 │  │   .Core                 │  │   Data.Core  │  │
│  │  2 algebras; SHACL      │  │  1 algebra; PROV-O      │  │ 1 algebra;   │  │
│  │  shape types; type →    │  │  types                  │  │ RDF types    │  │
│  │  SHACL serialization    │  │                         │  │              │  │
│  └─────────────────────────┘  └─────────────────────────┘  └──────────────┘  │
└──────────────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────────────┐
│  LAYER 0.5: Runtime and Phase 0c CE-Driven Codegen                           │
│                                                                              │
│  ┌─────────────────────────┐  ┌─────────────────────────┐                    │
│  │ Frank.Statecharts       │  │ Frank.SessionTypes      │                    │
│  │   .Runtime              │  │   .Codegen              │                    │
│  │  Journal interface,     │  │  Myriad plugin for      │                    │
│  │  SQLite-per-actor       │  │  protocol { } CE →      │                    │
│  │  default, Scheduler,    │  │  specialized RoleHooks, │                    │
│  │  StatechartAgent,       │  │  MailboxProcessor       │                    │
│  │  SCXML algorithm        │  │  actors                 │                    │
│  └─────────────────────────┘  └─────────────────────────┘                    │
│                                                                              │
│                                 ┌─────────────────────────┐                  │
│                                 │ Frank.Statecharts       │                  │
│                                 │   .Codegen              │                  │
│                                 │  Myriad plugin for      │                  │
│                                 │  statechart { } CE →    │                  │
│                                 │  typed state/event DUs  │                  │
│                                 └─────────────────────────┘                  │
└──────────────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────────────┐
│  LAYER 0.75: V1 Composition Packages (Voltron grid)                          │
│                                                                              │
│  Eight composition packages, each contributing type extensions only:         │
│                                                                              │
│  Foundational (Phase 0b):                                                    │
│    Frank.Statecharts.SessionTypes    (statechart ↔ session-type translation) │
│    Frank.SessionTypes.Validation     (refinements on message payloads)       │
│                                                                              │
│  Standard (Phase 4):                                                         │
│    Frank.SessionTypes.Auth           (role-based authorization)              │
│    Frank.Statecharts.Validation      (SHACL bound to configuration preds)    │
│    Frank.Statecharts.Provenance      (journal → PROV-O homomorphism)         │
│    Frank.Statecharts.LinkedData      (ALPS affordances from statecharts)     │
│    Frank.Validation.LinkedData       (SHACL Link headers, shape serving)    │
│    Frank.Provenance.LinkedData       (PROV-O as RDF, audit serving)          │
└──────────────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────────────┐
│  LAYER 1: Frank HTTP Integrations (+ ASP.NET Core)                           │
│                                                                              │
│  ┌─────────────────────┐  ┌─────────────────────┐  ┌─────────────────────┐   │
│  │ Frank.SessionTypes  │  │ Frank.Statecharts   │  │ Frank.Validation    │   │
│  └─────────────────────┘  └─────────────────────┘  └─────────────────────┘   │
│                                                                              │
│  ┌─────────────────────┐  ┌─────────────────────┐  ┌─────────────────────┐   │
│  │ Frank.Provenance    │  │ Frank.LinkedData    │  │ Frank.Discovery     │   │
│  └─────────────────────┘  └─────────────────────┘  └─────────────────────┘   │
│                                                                              │
│  Each contributes type extensions to ResourceBuilder and WebHostBuilder       │
│  per AD-13. Existing Frank.Auth, Frank.OpenApi, Frank.Datastar unchanged.    │
└──────────────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────────────┐
│  LAYER 2: Analyzers                                                          │
│                                                                              │
│  ┌──────────────────────────────────────────────────────────────────────┐    │
│  │ Frank.Analyzers                                                      │    │
│  │  • FRANK001–099: Conventions (existing FRANK001 etc.)                │    │
│  │  • FRANK100–199: Structural (AST shape, bindings)                    │    │
│  │  • FRANK200–299: Semantic (reachability, role completeness)          │    │
│  │  • FRANK300–399: Refinement (SMT-backed, via Microsoft.Z3)           │    │
│  │  • FRANK400–499: Composition (collisions, prerequisites, extractor)  │    │
│  │  • FRANK500–599: Codegen (hook completeness, hand-modification)      │    │
│  └──────────────────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────────────────┘

Optional packages (not v1, user-implementable against interfaces):
  Frank.SessionTypes.ActorTracked   (actor-backed RoleExtractor)
  Frank.SessionTypes.External       (external-service RoleExtractor)
```

### 5.2 Package Descriptions

**Core packages (Layer 0).** Each depends only on FSharp.Core plus the minimal type libraries the concern requires.

|Package                  |Dependencies                       |Responsibility                                                                                                                                                                                                        |
|-------------------------|-----------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
|`Frank.SessionTypes.Core`|FSharp.Core, System.Security.Claims|Global/local session types, refinements, projection; three algebras (`IProtocolBuilder`, `IProjectionAlgebra`, `ILocalTypeAlgebra`); generic hook family; `RoleExtractor` interface with `ClaimsRoleExtractor` default|
|`Frank.Statecharts.Core` |FSharp.Core                        |Statechart AST, transition edges; three algebras (`IStatechartBuilder`, `ITransitionAlgebra`, `IConfigurationPredicate`); `Guard`, `Action`, `ConfigurationPredicate` types                                           |
|`Frank.Validation.Core`  |FSharp.Core                        |SHACL shape types (`NodeShape`), two algebras (`IShapeBuilder`, `IConstraintAlgebra`); runtime type → SHACL emission helper for content-negotiated shape descriptions                                                 |
|`Frank.Provenance.Core`  |FSharp.Core                        |PROV-O types; `IProvenanceBuilder` algebra                                                                                                                                                                            |
|`Frank.LinkedData.Core`  |FSharp.Core, dotNetRdf             |RDF types (Graph, Triple, IRI); `IGraphBuilder` algebra                                                                                                                                                               |

**Runtime and CE-driven codegen packages (Layer 0.5).** These depend on their respective Cores and provide runtime infrastructure and build-time generation.

|Package                     |Dependencies                                    |Responsibility                                                                                                                                                                  |
|----------------------------|------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
|`Frank.Statecharts.Runtime` |Frank.Statecharts.Core, Microsoft.Data.Sqlite   |Journal interface, SQLite-per-actor default implementation, Scheduler interface, `StatechartAgent`, SCXML algorithm transliteration                                             |
|`Frank.SessionTypes.Codegen`|Frank.SessionTypes.Core, Myriad.Core, Myriad.Sdk|Myriad plugin generating specialized `RoleHooks<...>` records, message DUs, `MailboxProcessor` actors, and projection-soundness FsCheck properties from `protocol { }` CE source|
|`Frank.Statecharts.Codegen` |Frank.Statecharts.Core, Myriad.Core, Myriad.Sdk |Myriad plugin generating typed state DUs, typed event DUs, and transition program functions from `statechart { }` CE source                                                     |

**V1 composition packages (Layer 0.75).** Each contributes type extensions only — no new algebras, no new nested CEs. Composition packages do not depend on ASP.NET Core unless their concern composition inherently requires HTTP integration (for instance, `Frank.Validation.LinkedData`’s shape-serving functionality).

|Package                         |Dependencies                                                            |Responsibility                                                                                                                                              |
|--------------------------------|------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------|
|`Frank.Statecharts.SessionTypes`|Frank.Statecharts.Core, Frank.SessionTypes.Core                         |Statechart-to-session-type translation per §2.5; session-type-to-statechart presentation; type extensions for interoperability between the two presentations|
|`Frank.SessionTypes.Validation` |Frank.SessionTypes.Core, Frank.Validation.Core                          |Refinements on message payloads per §2.3; refinement satisfiability obligations for the analyzer (FRANK300–301)                                             |
|`Frank.SessionTypes.Auth`       |Frank.SessionTypes.Core, Frank.Auth                                     |Role-based authorization composing `ClaimsPrincipal` with session-typed role constraints; `authorizeByRole` operation                                       |
|`Frank.Statecharts.Validation`  |Frank.Statecharts.Core, Frank.Validation.Core                           |SHACL shapes bound to configuration predicates; state-dependent validation                                                                                  |
|`Frank.Statecharts.Provenance`  |Frank.Statecharts.Core, Frank.Provenance.Core, Frank.Statecharts.Runtime|Journal-to-PROV-O homomorphism; derives PROV-O graphs from statechart journals                                                                              |
|`Frank.Statecharts.LinkedData`  |Frank.Statecharts.Core, Frank.LinkedData.Core                           |ALPS affordance derivation from statechart plus role projection; semantic descriptions of stateful resources                                                |
|`Frank.Validation.LinkedData`   |Frank.Validation.Core, Frank.LinkedData.Core, ASP.NET Core              |SHACL shape Link headers (`rel="describedby"`); content-negotiated shape serving                                                                            |
|`Frank.Provenance.LinkedData`   |Frank.Provenance.Core, Frank.LinkedData.Core, ASP.NET Core              |PROV-O graphs as RDF; content-negotiated provenance serving                                                                                                 |

**HTTP integration packages (Layer 1).** Each concern has one HTTP-integration package that depends on its Core and on ASP.NET Core, contributing type extensions to `ResourceBuilder` and `WebHostBuilder`.

|Package             |Dependencies                                            |Responsibility                                                                                                                                                                                                              |
|--------------------|--------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
|`Frank.SessionTypes`|Frank.SessionTypes.Core, Frank                          |`roleHandlers { }` CE, `role` operation on `ResourceBuilder`, `useSessionTypes` on `WebHostBuilder`                                                                                                                         |
|`Frank.Statecharts` |Frank.Statecharts.Core, Frank.Statecharts.Runtime, Frank|`statechart { }` and `stateful { }` CEs, `stateful` operation on `ResourceBuilder`, `useStatecharts`/`useRoleProjection`/`useAffordances` on `WebHostBuilder`; persistent journal/scheduler via ASP.NET Core hosted services|
|`Frank.Validation`  |Frank.Validation.Core, Frank                            |`shapes { }` CE, `validate`/`validateWhenIn` operations, `useValidation`                                                                                                                                                    |
|`Frank.Provenance`  |Frank.Provenance.Core, Frank                            |`provenance { }` CE, `track`/`provenance` operations, `useProvenance`                                                                                                                                                       |
|`Frank.LinkedData`  |Frank.LinkedData.Core, Frank                            |`graph { }` CE, `representation`/`graph` operations, `useLinkedData`                                                                                                                                                        |
|`Frank.Discovery`   |Frank                                                   |Static ALPS, Link headers, OPTIONS handlers (existing, unchanged)                                                                                                                                                           |

**Analyzer package (Layer 2).**

|Package          |Dependencies                                                                        |Responsibility                                                                                                      |
|-----------------|------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------|
|`Frank.Analyzers`|Frank.Statecharts.Core, Frank.SessionTypes.Core, Frank.Validation.Core, Microsoft.Z3|All rule categories (FRANK001–599); SMT-backed refinement checks; composition-failure detection; codegen consistency|

**Existing Frank packages (unchanged structurally).** These predate this architecture revision and continue to work as documented in their respective decision records.

|Package         |Status   |Notes                                                                 |
|----------------|---------|----------------------------------------------------------------------|
|`Frank.Auth`    |Unchanged|Produces `ClaimsPrincipal`; `Frank.SessionTypes.Auth` composes with it|
|`Frank.OpenApi` |Unchanged|Standard OpenAPI generation from routes and F# types                  |
|`Frank.Datastar`|Unchanged|SSE delivery; used in handlers, not as a Frank composition            |

**Optional future packages (not v1, user-implementable).**

|Package                          |Notes                                                                                                                                                                                                    |
|---------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
|`Frank.SessionTypes.ActorTracked`|Actor-backed `RoleExtractor` for dynamic in-session role changes; the `frank-fs/tic-tac-toe` pattern. Users implement against the `RoleExtractor` interface in `Frank.SessionTypes.Core`.                |
|`Frank.SessionTypes.External`    |External-service `RoleExtractor` for roles governed by an entitlement API, policy engine, or organizational directory.                                                                                   |
|`Frank.Validation.OpenApi`       |SHACL-to-OpenAPI-schema derivation. Deferred; value largely subsumed by Phase 8 format-driven codegen. Shipped if demand appears.                                                                        |
|`Frank.Validation.Provenance`    |PROV-O records of validation events (which shape applied to which request, with pass/fail and attribution). Deferred; user code against `IProvenanceBuilder` handles the narrow use case in the meantime.|

### 5.3 Out-of-Scope Packages

These pairings were considered and deliberately not shipped. The rationale is recorded so future revisions don’t accidentally revisit settled questions.

- **`Frank.Statecharts.Datastar`** — Datastar composition with role-projected SSE streams. Not planned because Datastar sits downstream of response data as a delivery choice; composition happens naturally in user-written handlers (per the `frank-fs/tic-tac-toe` pattern) without a Frank-provided package.
- **`Frank.Statecharts.OpenApi`** — State-annotated OpenAPI. Not planned because OpenAPI is used in the standard way; users wanting state-aware hypermedia features use ALPS, Link headers, and the affordance machinery Frank already provides. OpenAPI was not designed for runtime state semantics, and bending it to that purpose is the wrong direction.

### 5.4 Adoption Profiles

Five profiles cover the common adoption paths, from simplest to richest.

1. **Validation-only REST.** `Frank.Validation` plus `Frank.Validation.Core`. Request/response SHACL validation on an otherwise stateless HTTP API. Zero statechart or session-type machinery.
1. **Agent-legible REST.** `Frank.Validation` + `Frank.LinkedData` + `Frank.Discovery` + `Frank.Validation.LinkedData`. RDF content negotiation, SHACL shape discovery, static ALPS. No state machine or session types.
1. **Session-typed service.** `Frank.SessionTypes` + `Frank.SessionTypes.Core` + optionally `Frank.SessionTypes.Codegen`. Multi-party protocols with per-role actors, projection soundness, and refinement checking. No statecharts required; session types are authored directly.
1. **Stateful workflow with audit.** `Frank.Statecharts` + `Frank.Provenance` + `Frank.Statecharts.Provenance` + optionally `Frank.Statecharts.Codegen`. Statechart-driven workflows with journal-derived PROV-O audit.
1. **Full semantic stack.** All five Core packages, all runtime/codegen packages, all eight composition packages, plus the HTTP integration packages. Self-describing, self-validating, self-auditing stateful resources that enforce multi-party protocols and negotiate across representations. This is the target for Frank’s semantic-resources vision.

An adopter moves between profiles without refactoring: adding a concern is a dependency change and a set of new operations in the builder CE, not a rewrite. This is the payoff of AD-1 (separate types per concern), AD-7 (Core independence), and AD-13 (three contribution surfaces).

### 5.5 Composition Matrix

The v1 composition grid is complete with respect to the concern pairs Frank ships. A pairing not listed is either in the out-of-scope list (§5.3) or deliberately absent from v1.

|                |SessionTypes|Statecharts |Validation  |Provenance                        |LinkedData                        |
|----------------|------------|------------|------------|----------------------------------|----------------------------------|
|**SessionTypes**|—           |✓ (Phase 0b)|✓ (Phase 0b)|(via Frank.Statecharts.Provenance)|(via Frank.Statecharts.LinkedData)|
|**Statecharts** |✓           |—           |✓           |✓                                 |✓                                 |
|**Validation**  |✓           |✓           |—           |(deferred to v2)                  |✓                                 |
|**Provenance**  |(transitive)|✓           |(none)      |—                                 |✓                                 |
|**LinkedData**  |(transitive)|✓           |✓           |✓                                 |—                                 |

Plus `Frank.SessionTypes.Auth` in the grid as a composition with the existing `Frank.Auth` package.

The Validation + Provenance cell is deferred to v2: a specific use case exists (emitting PROV-O records about validation events themselves — which SHACL shape was applied to which request, whether validation passed or failed, attributed to which user) but the use case is narrow enough that user code against `IProvenanceBuilder` handles it in the meantime. Promoted to a v1 package only if demand appears.

-----

## 6. Type Specifications

### 6.1 Frank.Statecharts.Core — Structure

```fsharp
namespace Frank.Statecharts.Core

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

### 6.2 Frank.Statecharts.Runtime — Journal, Scheduler, Agent

```fsharp
namespace Frank.Statecharts.Runtime

open Frank.Statecharts.Core

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

### 6.3 Frank.Validation.Core

```fsharp
namespace Frank.Validation.Core

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

### 6.4 Frank.Provenance.Core

```fsharp
namespace Frank.Provenance.Core

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

### 6.5 Frank.Statecharts.Multiparty — Role Participation

```fsharp
namespace Frank.Statecharts.Multiparty

open Frank.Statecharts.Core

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

### 6.6 Frank.Statecharts.Provenance — Journal → PROV-O Derivation

This package sits at Layer 0.5 and bridges two otherwise-independent concerns: the statechart journal (a runtime primitive) and PROV-O graphs (a provenance primitive). It depends on `Frank.Statecharts.Core` and `Frank.Provenance.Core`; it does not depend on the top-level `Frank` HTTP framework package, nor on `Frank.Statecharts.Runtime` beyond the journal types, nor on `Frank.Provenance`.

The package exists so that `Frank.Provenance` remains statechart-unaware and the journal-to-PROV-O mapping is reusable from CLI tools, background workers, and test harnesses that replay journals offline.

```fsharp
namespace Frank.Statecharts.Provenance

open Frank.Statecharts.Core
open Frank.Statecharts.Runtime
open Frank.Provenance.Core

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

Attribution to human agents (via `wasAttributedTo`) happens at the Frank.Statecharts layer, where `ProvenanceOptions.AgentExtractor` turns `HttpContext` into an `Agent`. `Frank.Statecharts.Provenance` is HTTP-unaware; the runtime agent is always the software agent unless Frank.Statecharts injects a human agent before calling `deriveAndAppend`.

### 6.7 Frank.Statecharts — Composition Model

```fsharp
namespace Frank.Statecharts

open Frank.Statecharts.Core
open Frank.Statecharts.Runtime
open Frank.Statecharts.Multiparty
open Frank.Validation.Core
open Frank.Provenance.Core

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
namespace Frank.Statecharts.Core

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
namespace Frank.Statecharts.Core.Interpreters

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
namespace Frank.Statecharts.Core

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
namespace Frank.Statecharts.Core.Interpreters

/// Interpret transitions as state updates (used by the runtime).
type StateUpdateAlgebra = ...          // ITransitionAlgebra<AgentState -> AgentState * Action list>

/// Generate F# code for transitions.
type FSharpCodeGenAlgebra = ...        // ITransitionAlgebra<string>

/// Generate trace entries (used for provenance derivation).
type TraceAlgebra = ...                // ITransitionAlgebra<JournalEntryKind list>
```

### 7.3 IConfigurationPredicate<’r> — Predicate Evaluation

```fsharp
namespace Frank.Statecharts.Core

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
namespace Frank.Provenance.Core

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
namespace Frank.Statecharts.Runtime

module internal Semantics =
    open Frank.Statecharts.Core

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
namespace Frank.Statecharts.Runtime.Schedulers

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
namespace Frank.Statecharts.Multiparty

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

The plan is strictly depth-first. Each layer ships complete (against its conformance suite) before the next layer is started. No layer assumes simplifications it intends to revisit. Phase 0 splits into three sub-phases reflecting the denotational layering: 0a establishes concern Cores in parallel; 0b builds the foundational composition packages that the runtime depends on; 0c delivers the CE-driven codegen infrastructure. Phases 1 through 7 are v1; Phase 8 is named explicitly to document the deferred v2 scope.

### Phase 0a: Concern Cores (Weeks 1–4)

Deliverables — all five Core packages in parallel, each with its algebras and types:

- **`Frank.SessionTypes.Core`:** Global and local session types per §2.2; refinement predicates per §2.3; projection function with the homomorphism law; three algebras (`IProtocolBuilder`, `IProjectionAlgebra`, `ILocalTypeAlgebra`); generic hook family; `RoleExtractor` interface with `ClaimsRoleExtractor` default implementation; SRTP-based generic operations.
- **`Frank.Statecharts.Core`:** Statechart AST from §6.1 (presented as sugar over session types); closed `Guard` language; three algebras (`IStatechartBuilder`, `ITransitionAlgebra`, `IConfigurationPredicate`); `ConfigurationPredicate` evaluation and indexing; reflect/reify bridges per AD-4.
- **`Frank.Validation.Core`:** SHACL shape types (`NodeShape`); two algebras (`IShapeBuilder`, `IConstraintAlgebra`); runtime type → SHACL emission helper.
- **`Frank.Provenance.Core`:** PROV-O types; `IProvenanceBuilder` algebra.
- **`Frank.LinkedData.Core`:** RDF types (Graph, Triple, IRI); `IGraphBuilder` algebra; interop with dotNetRdf.

Conformance suite (applies to all five Cores):

- Round-trip property: `reify (reflect doc) ≡ doc` for all generated documents in each concern.
- All algebra interpreters total on all generated inputs (FsCheck).
- Projection soundness: for all well-formed global types in the generated corpus, `αᵣ ∘ π_r = F(π_r) ∘ α` holds at every state-role pair.
- Well-formedness checks have zero false positives on the curated known-good corpus, zero false negatives on the curated intentionally-broken corpus.
- Type → SHACL emission round-trip for the supported F#-type fragment.

Exit gate: all five Cores have at least three interpreters per algebra; projection soundness property holds on the session-type corpus; Core-to-Core dependency check confirms no `Frank.*.Core` depends on any other `Frank.*.Core` (per AD-7).

### Phase 0b: Foundational Composition Packages (Weeks 4–5)

Deliverables — two composition packages that the runtime depends on, with higher formal-verification bar:

- **`Frank.Statecharts.SessionTypes`:** Statechart-to-session-type translation per §2.5; session-type-to-statechart presentation. Mechanically-checked translation correctness: for every well-formed statechart, `translate(s)` denotes the same coalgebra as `s` itself.
- **`Frank.SessionTypes.Validation`:** Refinement predicates on message payloads per §2.3; refinement obligation extraction for the analyzer. Property-based proof that refined message types preserve the projection homomorphism.

Conformance suite:

- Uniform structural conformance: type extensions registered without collisions; no analyzer warnings on positive samples; warnings fire on negative samples.
- Concern-specific property tests are **complete** (not sample):
  - Translation correctness for the full statechart fragment Frank supports.
  - Projection-preservation under refinements for the refinement fragment Frank supports.
  - Round-trip: `present (translate s) ≡ s` up to structural equivalence.

Exit gate: both composition packages pass the full concern-specific property suites; uniform structural conformance met.

### Phase 0c: CE-Driven Codegen (Weeks 5–7)

Deliverables — Myriad plugins for the two concerns where CE-driven codegen is load-bearing:

- **`Frank.SessionTypes.Codegen`:** Myriad plugin reading `protocol { }` CE source via Fantomas AST extraction; generates specialized `RoleHooks<...>` records, message DUs, `MailboxProcessor` actor implementations, and projection-soundness FsCheck properties.
- **`Frank.Statecharts.Codegen`:** Myriad plugin reading `statechart { }` CE source; generates typed state DUs (replacing stringly-typed `StateId`), typed event DUs (replacing stringly-typed `EventId`), and transition program functions.

Conformance suite:

- Generated `MailboxProcessor` actors pass the projection-soundness properties from Phase 0a for every test protocol.
- Generated `RoleHooks<...>` records conform to the generic hook family in `Frank.SessionTypes.Core`.
- Analyzer rules FRANK500–503 (hook completeness) fire correctly on matched and mismatched test cases.
- Hand-modified generated files are detected by FRANK510 on subsequent build.
- Generated output round-trips: writing the same CE source produces identical generated files (deterministic generation).

Exit gate: sample protocols generate working actors that pass all projection-soundness tests; all FRANK500–510 analyzer rules have positive and negative fixture coverage.

### Phase 1: Runtime (Weeks 7–10)

Deliverables:

- `IStatechartJournal` with in-memory and SQLite-per-actor implementations. SQLite implementation includes WAL mode, close-on-idle connection management, and per-actor file provisioning.
- `IStatechartScheduler` with in-memory, file-backed, and SQLite-backed implementations.
- `StatechartAgent` with full SCXML algorithm transliteration (§8.1).
- Per-role `MailboxProcessor` actor runtime wired to `Frank.SessionTypes.Codegen` output.
- Reference coalgebraic interpreter (the adequacy-check target from AD-10).
- Crash recovery: agent restart from journal produces identical state.

Conformance suite:

- W3C SCXML 1.0 IRP test subset (compound, parallel, history, internal events, eventless transitions, send/cancel, datamodel — restricted to features Frank supports).
- **Operational adequacy (AD-10):** for all well-formed protocols in the corpus, the trace produced by the SCXML algorithm is contained in the set of traces the reference interpreter produces (FsCheck).
- Property: agent state after N events equals replayed state from journal of those N events.
- Property: snapshot at position P, then N more events, equals fresh agent fed all P+N events.
- Property: scheduled events fire at the correct time, survive restart.
- SQLite-specific: memory budget stays within documented bounds (~400 KB per resident actor default, ~200 KB tuned); close-on-idle releases handles correctly; reopening works at expected latency.
- Soak test: 100,000 events without memory leak; journal grows linearly; snapshot path stays bounded.

Exit gate: all SCXML IRP tests Frank claims to support pass; operational adequacy property holds on corpus; restart-recovery property holds across all generated event sequences; SQLite memory budget verified.

### Phase 2: Generators to External Formats (Weeks 10–12)

Deliverables — generators only, no parsers. Parsers move to Phase 8 alongside format-driven codegen.

- Generators from statechart AST to SCXML, smcat, mermaid, ALPS.
- Generators from session-type AST to Scribble notation, AsyncAPI notation, textual documentation.
- Generators from SHACL shapes to Turtle, JSON-LD (already partially in `Frank.Validation.Core`; this phase completes).
- Generators from RDF graphs to Turtle, JSON-LD, N-Triples (via dotNetRdf).

Conformance suite:

- For each generator: output validates against the format’s schema.
- Cross-tool interop: SCXML produced by Frank loads in scion and qm-scxml without error; Scribble output parses in Scribble tooling.
- Generator determinism: same input produces identical output bytes.

Exit gate: schema validation passes for all generators on the sample corpus; cross-tool interop verified for SCXML and Scribble.

### Phase 3: Refinement Verification (Weeks 12–14)

Deliverables:

- Microsoft.Z3 integration in `Frank.Analyzers`.
- Refinement obligation extraction: given a typed AST of a protocol with refined branches, produce the SMT formulas that must be satisfiable for well-formedness.
- Analyzer rules FRANK300–301 for sender provability and receiver completeness.
- Unsat-core reporting: when a refinement is rejected, the analyzer surfaces the specific predicates and values that caused rejection.

Conformance suite:

- For every protocol in the curated corpus, refinement verification terminates within 30 seconds per protocol.
- Known-satisfiable refinements are accepted; known-unsatisfiable refinements are rejected with correct unsat-core.
- The verifier is sound: no protocol with refinements violating sender provability or receiver completeness is accepted.
- The verifier is complete on Frank’s decidable `Guard` fragment.

Exit gate: curated corpus passes soundness and completeness checks; Z3 integration reproducible across platforms; unsat-core output human-readable.

### Phase 4: Remaining Composition Packages (Weeks 14–17, parallelizable)

Deliverables — the six composition packages not shipped in Phase 0b:

- `Frank.SessionTypes.Auth` — role-based authorization composing `ClaimsPrincipal` with session-typed role constraints.
- `Frank.Statecharts.Validation` — SHACL shapes bound to configuration predicates.
- `Frank.Statecharts.Provenance` — journal-to-PROV-O homomorphism.
- `Frank.Statecharts.LinkedData` — ALPS affordance derivation from statechart plus role projection.
- `Frank.Validation.LinkedData` — SHACL shape Link headers and content-negotiated shape serving.
- `Frank.Provenance.LinkedData` — PROV-O graphs as RDF, content-negotiated provenance serving.

Conformance suite: uniform structural conformance (type extensions registered, integration tests demonstrating joint behavior, analyzer rules fire appropriately) **plus complete concern-specific test suites** (not samples) per composition:

- `Frank.SessionTypes.Auth`: role-authorization matrix coverage across protocols, roles, and claim configurations.
- `Frank.Statecharts.Validation`: shape-per-predicate binding coverage; state-dependent validation fires for all reachable configurations.
- `Frank.Statecharts.Provenance`: for every curated journal sequence, derive → round-trip through Apache Jena preserves the graph; `derive(entries[0..k]) ∪ derive(entries[k+1..n]) = derive(entries[0..n])`; derivation respects denotational equivalence (bisimilar journals yield isomorphic PROV-O graphs).
- `Frank.Statecharts.LinkedData`: ALPS affordances match the role projection for all role × configuration combinations.
- `Frank.Validation.LinkedData`: shape discovery endpoint returns correct Turtle/JSON-LD; Link headers point to correct shapes for all advertised resources.
- `Frank.Provenance.LinkedData`: PROV-O content negotiation returns correctly-formatted RDF for all requested media types.

Exit gate: all six packages meet structural conformance and have their complete concern-specific test suites passing.

### Phase 5: HTTP Integration (Weeks 17–20)

Deliverables — the HTTP integration packages that extend `ResourceBuilder` and `WebHostBuilder`:

- `Frank.SessionTypes`: `roleHandlers { }` CE, `role` operation, `useSessionTypes` with role-extractor registration.
- `Frank.Statecharts`: `statechart { }` and `stateful { }` CEs, `stateful` operation, `useStatecharts`/`useRoleProjection`/`useAffordances` middleware.
- `Frank.Validation`: `shapes { }` CE, `validate`/`validateWhenIn` operations, `useValidation` middleware.
- `Frank.Provenance`: `provenance { }` CE, `track`/`provenance` operations, `useProvenance` middleware.
- `Frank.LinkedData`: `graph { }` CE, `representation`/`graph` operations, `useLinkedData` middleware.
- `Frank.Discovery`: updates as needed for the new concerns (existing package, not a rewrite).
- Persistent journal/scheduler wiring for ASP.NET Core hosted services.

Conformance suite:

- Order workflow sample: end-to-end test demonstrates each role sees only their projected affordances; CE authoring path produces the expected runtime behavior.
- TicTacToe sample: multi-party game with derived role projections and the user-implemented `RoleExtractor` pattern.
- Provenance sample: PROV-O graph derived from journal matches hand-constructed expected graph.
- Property: across all role × configuration combinations on samples, projected view affordances are exactly the intersection of (binding affordances satisfying predicate) and (role’s triggerable transitions).
- Property: live-run PROV-O equals replay-derived PROV-O (`Frank.Statecharts.Provenance.Derivation.replay` over the final journal).
- End-to-end CE authoring path is exercised by at least three sample applications covering different profile combinations (§5.4).

Exit gate: three samples pass full integration tests; projection consistency holds; live-vs-replay provenance equivalence holds; CE authoring path produces working applications across adoption profiles.

### Phase 6: Analyzer Completion (Weeks 20–22)

Deliverables — `Frank.Analyzers` with complete rule coverage across all six categories from AD-11:

- FRANK001–099: conventions (existing rules preserved).
- FRANK100–199: structural (duplicate handlers, missing handler coverage, unresolved transition references, etc.).
- FRANK200–299: semantic (reachability, deadlock, predicate satisfiability, role completeness, hierarchical role consistency).
- FRANK300–399: refinement (already integrated in Phase 3).
- FRANK400–499: composition (operation-name collisions, missing prerequisites, builder-output mismatches, missing extractor registration).
- FRANK500–599: codegen (hook completeness already integrated in Phase 0c; generated-file hand-modification detection).

Conformance suite:

- Per-rule positive and negative test fixtures.
- All rules fire with actionable messages on negative fixtures.
- No rule fires on positive fixtures.
- Analyzer performance: analyzing a 500-transition protocol completes in under 5 seconds; analyzing a 50-rule session-typed protocol with refinements completes in under 30 seconds.

Exit gate: per-rule fixtures cover all documented detection cases; analyzer runs in IDE and in CI with equivalent results.

### Phase 7: Documentation and Samples (Weeks 22–24)

Deliverables:

- Order Workflow sample (multi-party session types, derived projections, journal-based provenance).
- TicTacToe sample (hierarchical role scoping, actor-backed role tracking in user code).
- API Documentation sample (ALPS, LinkedData content negotiation, SHACL shape serving).
- Session-typed service sample (session types authored directly in `protocol { }` CE without statechart presentation).
- Representation cookbook (short examples of handlers producing Oxpecker views, raw HTML, JSON, ASP.NET Core `IResult`, Datastar SSE — all using the `HttpContext`-exposed Frank context).
- Conformance report documenting which SCXML features Frank supports, which it intentionally omits, and where it deviates from the SCXML algorithm in favor of denotational correctness.

Exit gate: documentation review by an external reader (someone who has not been part of the design process) yields a working sample within an hour; the five adoption profiles in §5.4 each have a working sample.

### Phase 8: External Formats and Format-Driven Codegen (v2, scope finalized post-v1)

Format-driven codegen per AD-12, using the FSharp.GrpcCodeGenerator / FsGrpc.Tools MSBuild-integration template referenced in issue #285. Scope finalized after v1 ships based on CE-driven codegen experience and user demand.

Formats in view (full list recorded for future reference; final v2 scope may be smaller):

- **Statecharts:** WSD parser (one of the original formats inspiring the hypermedia-program direction per Amundsen’s work, where the idea of parsing structured workflow specifications into executable code originated), SCXML parser, smcat parser, mermaid stateDiagram-v2 parser, PlantUML state diagram parser, XState JSON parser. Each parses to `StatechartDocument` AST; codegen produces the same F# artifacts as `Frank.Statecharts.Codegen` from Phase 0c.
- **Session types:** Scribble parser, AsyncAPI parser. Each parses to `GlobalType`; codegen produces `Frank.SessionTypes.Codegen`-equivalent artifacts.
- **Validation:** SHACL file parser (Turtle, JSON-LD, N-Triples serializations), JSON Schema parser. Codegen produces F# record types matching shape structure (the shape → type direction deferred from v1).
- **Linked data:** RDF graph parsers for Turtle, JSON-LD, N-Triples data files (as distinct from the shape-file imports above).
- **Provenance:** PROV-O Turtle/JSON-LD parsers for importing external provenance records.
- **Hypermedia:** ALPS YAML/XML/JSON parsers for importing existing ALPS profiles.
- **OpenAPI:** OpenAPI YAML/JSON parser, for the deferred `Frank.Validation.OpenApi` composition if that path is pursued.

Out-of-scope confirmation: no Datastar codegen (out-of-scope per AD-13); no OpenAPI-from-statechart codegen (out-of-scope per AD-13).

No conformance criteria at this phase; v2 scope is finalized when v1 demonstrates which formats are most needed by users and which CE patterns have stabilized enough to serve as the canonical F# target.

### Calendar Reality

Twenty-four weeks of focused work (Phases 0a through 7). Given competing priorities on Frank’s other v7.3.0 work (semantic resources, spec pipeline) and day-job context, an honest elapsed-time estimate is **10–14 months**. The phase boundaries are firm; the calendar between them is flexible.

The key discipline this plan enforces: no layer is started until the layer beneath it has passed its conformance gate. The trade-off for depth-first: slower arrival at visible HTTP behavior, but that behavior, when it arrives, rests on a foundation that doesn’t require revision. The denotational layering means that when HTTP integration lands in Phase 5, the protocols it runs are well-formed, refinement-verified, projection-sound, and derived from CE authoring that the developer can trust — none of which can be bolted on later without rework.

-----

## 12. Conformance and Falsifiability

Each layer has a published conformance suite. The suite is the architecture’s contract with future maintainers and adopters.

### 12.1 Test Suites by Layer

|Layer                       |Test suite                                                                     |
|----------------------------|-------------------------------------------------------------------------------|
|Frank.Statecharts.Core      |FsCheck properties on AST round-trip, predicate evaluation, guard evaluation   |
|Frank.Statecharts.Runtime   |W3C SCXML IRP subset; restart-recovery property; scheduler under simulated kill|
|Frank.Statecharts.Parsers   |Round-trip on sample corpora; cross-tool load tests                            |
|Frank.Statecharts.Generators|Schema validation; cross-tool load tests                                       |
|Frank.Statecharts.Multiparty|MPST projection rule conformance; well-formedness check corpus                 |
|Frank.Validation.Core       |W3C SHACL test suite (Frank-supported shapes)                                  |
|Frank.Provenance.Core       |PROV-O serialization round-trip via Apache Jena                                |
|Frank.Statecharts.Provenance|Journal → PROV-O derivation purity and append-associativity properties         |
|Frank.LinkedData.Core       |RDF format round-trips                                                         |
|Frank.Statecharts           |Sample-based integration; projection consistency; live-vs-replay provenance    |
|Frank.Statecharts.Analyzers |Per-rule positive/negative fixtures                                            |

### 12.2 Falsifiable Acceptance Criteria

Every deliverable has a falsifiable acceptance criterion. Examples:

- **Frank.Statecharts.Runtime AC-1:** Given any sequence of N events fired against a fresh agent, then snapshotting at position k and replaying events k+1..N from the journal produces identical `AgentState`. Falsifiable by any divergence.
- **Frank.Statecharts.Multiparty AC-1:** Given a global protocol with N roles, projecting each role and re-composing as parallel regions produces a statechart bisimilar to the original. Falsifiable by any role × configuration where the projection cannot reproduce an enabled transition of the original.
- **Frank.Statecharts.Provenance AC-1:** For any split point k in a journal of length N, `derive(entries[0..k]) ∪ derive(entries[k+1..N]) ≡ derive(entries[0..N])` as RDF graphs. Falsifiable by any structural inequivalence.
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

|Date   |Decision                                    |Rationale                                                                   |
|-------|--------------------------------------------|----------------------------------------------------------------------------|
|2026-04|Separate types per concern                  |Orthogonal concerns, independent utility                                    |
|2026-04|Three algebras with disjoint carriers       |Single algebra conflated structure/behavior/queries                         |
|2026-04|Journal-sourced runtime                     |Durability, provenance derivation, snapshot consistency                     |
|2026-04|AST + algebra bridge per algebra            |Parsing produces AST; interpretation uses algebra                           |
|2026-04|Predicate-keyed bindings                    |Single-state keys silently dropped parallel-region cases                    |
|2026-04|Derived role projections                    |Hand-authored projections were unsound and didn’t scale                     |
|2026-04|Independent statecharts package             |Reusability outside HTTP contexts                                           |
|2026-04|Closed guard language                       |Open-string guards required embedded evaluator                              |
|2026-04|Scheduled events first-class                |Half of HTTP workflows have timeouts; can’t defer                           |
|2026-04|Macrostep matches SCXML exactly             |Almost-SCXML is worse than either SCXML or explicit divergence              |
|2026-04|Frank.Statecharts.Provenance as seam package|Keeps Frank.Provenance statechart-unaware; journal → PROV-O reusable offline|
|2026-04|Depth-first build plan                      |Breadth-first accumulates inherited constraints across layers               |

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