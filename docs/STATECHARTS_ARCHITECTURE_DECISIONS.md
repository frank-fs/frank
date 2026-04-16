# Frank Statecharts Architecture Decision Record

**Status**: Approved  
**Date**: April 2026  
**Authors**: Ryan Riley, with architectural consultation  
**Supersedes**: Previous unified AST approach in Frank.Statecharts.Core / Frank.Resources.Model

-----

## Executive Summary

This document records the architectural decisions for Frank’s statechart-based stateful resources system. The architecture combines:

1. **Tagless-final algebras** for compositional structure and multiple interpretations
1. **MailboxProcessor agents** for Harel-faithful runtime semantics
1. **HMBS-style composition model** for binding statecharts to HTTP resources
1. **Orthogonal concern packages** with explicit composition rather than unified AST

The key insight: **separate types per concern, unified through a composition model (the extended HMBS tuple), not a unified AST**.

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
1. [Role Projections](#10-role-projections)
1. [Build Plan](#11-build-plan)
1. [Migration Guide](#12-migration-guide)
1. [References](#13-references)
1. [Appendices](#14-appendices)

-----

## 1. Problem Statement

### 1.1 Goals

Frank aims to provide:

1. **Stateful HTTP resources** — Resources whose behavior changes based on application state
1. **Self-describing APIs** — Agents and humans can discover capabilities at runtime
1. **Protocol enforcement** — Invalid state transitions are structurally impossible
1. **Multi-party protocols** — Different roles see different projections of the same resource
1. **Audit and provenance** — Full traceability of state changes via PROV-O
1. **Semantic richness** — RDF/Linked Data representations, ALPS profiles, SHACL validation

### 1.2 Constraints

1. **Harel fidelity** — Statechart semantics must match Harel’s formal specification
1. **F# idioms** — Computation expressions, immutability, type safety
1. **ASP.NET Core integration** — Must work with existing middleware patterns
1. **Independent utility** — Each capability should work without requiring all others
1. **Quorum reuse** — Statechart runtime must be reusable outside Frank (e.g., workflow orchestration)

### 1.3 Prior Attempts and Why They Failed

|Attempt                                                            |Problem                                                                             |
|-------------------------------------------------------------------|------------------------------------------------------------------------------------|
|Unified AST with all concerns embedded                             |Created circular dependencies; couldn’t use validation without importing statecharts|
|Duplicate types in Frank.Resources.Model and Frank.Statecharts.Core|Maintenance burden; unclear which was canonical                                     |
|Ad-hoc runtime interpretation of AST                               |No formal model of how concerns interact; hard to reason about combinations         |
|Pattern-matching interpreters over AST                             |Verbose; adding new interpretations required modifying visitor functions            |

-----

## 2. Theoretical Foundations

### 2.1 Harel Statecharts (1987)

David Harel’s statecharts extend finite-state machines with:

|Feature          |Description                                       |Frank Relevance                    |
|-----------------|--------------------------------------------------|-----------------------------------|
|**Hierarchy**    |States contain substates (AND/OR decomposition)   |Resource nesting, visibility levels|
|**Orthogonality**|Parallel regions execute concurrently             |Multi-party protocols              |
|**Broadcast**    |Actions generate events propagating to all regions|Event-driven coordination          |
|**History**      |Remember and restore sub-configurations           |Session state, caching             |
|**Guards**       |Conditions enabling/disabling transitions         |Authorization                      |

**Critical semantic properties**:

1. **Configuration** — Maximal orthogonal set of active states (global property)
1. **Macrostep** — Process one external event to quiescence
1. **Microstep** — Single transition within a macrostep
1. **Broadcast semantics** — Generated events process in same macrostep

**Why this matters**: These semantics are NOT compositional. You cannot compute them by folding over a tree. This justifies the MailboxProcessor runtime.

Reference: Harel, D. (1987). Statecharts: A Visual Formalism for Complex Systems.

### 2.2 HMBS — Hypermedia Model Based on Statecharts (2001)

De Oliveira, Turine, and Masiero’s HMBS model formally connects statecharts to hypermedia:

```
Hip = ⟨ST, P, m, ae, N⟩
```

|Component       |Definition                        |Frank Analog           |
|----------------|----------------------------------|-----------------------|
|**ST**          |Statechart structure              |`StatechartDocument`   |
|**P**           |Set of pages (content units)      |HTTP handlers          |
|**m : Sₛ → P**  |State-to-page mapping             |`Map<StateId, Handler>`|
|**ae : Anc → E**|Anchor-to-event mapping           |Affordance → Transition|
|**N**           |Visibility level (hierarchy depth)|Projection depth       |

**Key HMBS concepts adopted**:

1. AND-states for concurrent presentation (parallel regions)
1. Visibility level for hierarchy projection
1. Reachability tree for navigation analysis
1. Legal configuration = maximal orthogonal set

Reference: de Oliveira, M.C.F., Turine, M.A.S., & Masiero, P.C. (2001). A Statechart-Based Model for Hypermedia Applications. ACM TOIS.

### 2.3 Tagless-Final Encoding (2009)

Carette, Kiselyov, and Shan’s tagless-final approach:

```fsharp
// Instead of:
type Exp = Lit of int | Add of Exp * Exp
let rec eval = function Lit n -> n | Add(a,b) -> eval a + eval b

// We write:
type IExpAlgebra<'r> =
    abstract lit : int -> 'r
    abstract add : 'r -> 'r -> 'r

// Program is polymorphic:
let program (alg: IExpAlgebra<'r>) = alg.add (alg.lit 1) (alg.lit 2)

// Multiple interpretations:
let value = program EvalAlgebra      // 3
let text = program PrintAlgebra      // "1 + 2"
let tree = program ASTAlgebra        // Add(Lit 1, Lit 2)
```

**Benefits for Frank**:

1. “Code as Model” — Same program interpreted for execution, analysis, generation
1. Extensibility — New interpreters without modifying programs
1. Type safety — Compiler ensures exhaustive handling
1. Composition — Interpreters can be combined

**Limitations**:

1. Parsing into polymorphic programs is awkward (requires rank-2 types or interface-passing)
1. Inspection requires reification
1. F# lacks type classes (use interfaces or SRTPs)

**Resolution**: Use AST for parsing/serialization, tagless-final for interpretation. Bridge via `reflect`/`reify`.

Reference: Carette, J., Kiselyov, O., & Shan, C. (2009). Finally Tagless, Partially Evaluated. JFP.

### 2.4 John Azariah’s “Code as Model” (2025)

Azariah’s elevator verification example demonstrates:

> “Your Code IS the Model. When we wrote `let controller = elevator { move_up; open_doors }`, we didn’t write ‘code’ in the traditional sense. We wrote a description of intent.”

**Key insight**: The same abstract program can be interpreted for:

- Production execution
- Verification (state space exploration)
- Visualization (graph building)
- Auditing (trace generation)

**Application to Frank**: Statechart programs should be interpretable as:

- SCXML export
- ALPS profiles
- Reachability analysis
- Runtime HTTP handlers
- PROV-O audit trails

Reference: Azariah, J. (2025). Tagless Final in F# - Part 6: The Power of Tagless-Final: Code as Model.

-----

## 3. Architectural Decisions

### AD-1: Separate Types Per Concern

**Decision**: Each semantic concern (statecharts, validation, provenance, linked data) has its own type definitions in its own package.

**Rationale**:

- Concerns are orthogonal — each has independent value
- Avoids circular dependencies
- Users pay only for what they use
- Aligns with SOLID principles

**Consequences**:

- More packages to manage
- Composition model needed to relate them
- Clear dependency graph

### AD-2: Tagless-Final Algebras for Structural Interpretation

**Decision**: Use tagless-final algebras (`IStatechartAlgebra<'r>`, `IShapeAlgebra<'r>`, etc.) for operations that are compositional over structure.

**Rationale**:

- Multiple interpretations without code duplication
- Type-safe exhaustiveness
- Extensible without modifying existing code

**Applies to**:

- SCXML/ALPS/smcat generation
- Pretty printing
- Reachability analysis
- Affordance projection

**Does NOT apply to**:

- Step execution (requires global state)
- Configuration management (non-compositional)
- Broadcast propagation (requires event queue)

### AD-3: MailboxProcessor for Runtime Semantics

**Decision**: Harel’s operational semantics are implemented via `MailboxProcessor<StatechartMessage>`, not via algebras.

**Rationale**:

- Harel semantics require serialized event processing
- Configuration is a global property, not compositional
- Broadcast requires event queue and iteration to quiescence
- MailboxProcessor naturally models reactive systems

**Consequences**:

- Runtime is not extensible via interpreters
- Single correct implementation, heavily tested
- Clear boundary between compositional (algebra) and non-compositional (agent) operations

### AD-4: AST + Tagless-Final Bridge (Option 3)

**Decision**: Support both AST (initial encoding) and tagless-final (final encoding) with explicit conversion functions.

```fsharp
// Both representations available
type StatechartDocument = { ... }  // AST for parsing, serialization, inspection
type Statechart = { Run: IStatechartAlgebra<'a> -> 'a }  // Polymorphic

// Bidirectional conversion
let reflect : StatechartDocument -> Statechart
let reify : Statechart -> StatechartDocument
```

**Rationale**:

- Parsing naturally produces AST
- Serialization requires inspectable structure
- Analyzers need pattern matching
- Interpretation benefits from tagless-final

**Consequences**:

- Two representations to understand
- Conversion overhead (usually negligible)
- Maximum flexibility

### AD-5: Composition Model, Not Unified AST

**Decision**: Relate concerns via an explicit binding type (extended HMBS tuple), not by embedding all concerns in one AST.

```fsharp
// NOT this (unified AST):
type ResourceDocument = {
    States: StateNode list
    Shapes: NodeShape list     // Embedded
    Affordances: Affordance list  // Embedded
}

// THIS (composition model):
type StatefulResourceBinding = {
    Statechart: StatechartDocument           // References external type
    Validation: Map<StateId, NodeShape>      // Maps to external type
    Affordances: Map<StateId, Affordance list>
    // ...
}
```

**Rationale**:

- Each concern maintains its own types
- Binding describes relationships explicitly
- Adding new concerns doesn’t modify existing types
- Clear ownership boundaries

### AD-6: Role Projection is Filtering, Not Interpretation

**Decision**: Multi-party role projections filter data before interpretation; interpreters remain role-agnostic.

```fsharp
// Filter step
let view = Projection.project role config binding

// Then interpret (algebras unchanged)
let headers = view.Affordances |> List.map (LinkHeaderAlgebra().affordance)
```

**Rationale**:

- Keeps algebras simple and focused
- Avoids combinatorial explosion (role × visibility × version × …)
- Filters compose naturally
- Single responsibility principle

### AD-7: Statecharts Package is Independent

**Decision**: `Statecharts.Core` and `Statecharts.Runtime` have no dependencies on Frank, ASP.NET Core, or HTTP concepts.

**Rationale**:

- Reusable for Quorum workflow orchestration
- Testable without HTTP infrastructure
- Potential F# ecosystem contribution
- Clean separation of concerns

**Consequences**:

- Frank.Statecharts is the HTTP integration layer
- Integration concepts (handlers, affordances, middleware) live in Frank.Statecharts

-----

## 4. Rejected Alternatives

### 4.1 Unified AST (Rejected)

```fsharp
// REJECTED: All concerns in one type
type FrankDocument = {
    States: StateNode list
    Transitions: TransitionEdge list
    Shapes: NodeShape list
    Affordances: Affordance list
    Provenance: ProvenanceConfig
    Roles: RoleDefinition list
}
```

**Why rejected**:

- Creates dependency on all concerns when using any
- Adding new concern requires modifying core type
- Carries dead fields when using subset
- Violates single responsibility

### 4.2 Pure Tagless-Final Without AST (Rejected)

```fsharp
// REJECTED: No AST, only polymorphic programs
let parseWSD (input: string) : IStatechartAlgebra<'a> -> 'a = ...
```

**Why rejected**:

- Parsing into polymorphic functions is awkward in F#
- No way to serialize/checkpoint
- Analyzers need pattern matching
- Round-tripping impossible

### 4.3 Denotational Semantics for Runtime (Rejected)

```fsharp
// REJECTED: Statechart = function from event traces to config traces
type Denotation = EventTrace -> ConfigurationTrace
```

**Why rejected**:

- Denotational semantics for statecharts are complex
- History and broadcast notoriously hard to model
- Loses operational intuition
- Academic elegance over practical utility

### 4.4 Role-Aware Interpreters (Rejected)

```fsharp
// REJECTED: Every algebra needs role parameter
type IRoleAwareAffordanceAlgebra<'r> =
    inherit IAffordanceAlgebra<'r>
    abstract filterForRole : RoleId -> 'r -> 'r
```

**Why rejected**:

- Combinatorial explosion with other filters
- Violates single responsibility
- Projection is a separate concern from formatting

### 4.5 Coupled Statecharts + HTTP (Rejected for Core)

```fsharp
// REJECTED: Statechart types depend on HTTP
type StatechartDocument = {
    // ...
    Handlers: Map<StateId, HttpContext -> Task>  // HTTP leak
}
```

**Why rejected**:

- Can’t reuse for non-HTTP (Quorum workflows)
- Testing requires HTTP infrastructure
- Violates dependency direction (stable → volatile)

-----

## 5. Package Structure

### 5.1 Dependency Graph

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  LAYER 0: Pure Models (FSharp.Core only)                                    │
│                                                                             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐              │
│  │ Statecharts     │  │ Validation      │  │ Provenance      │              │
│  │ .Core           │  │ .Core           │  │ .Core           │              │
│  └────────┬────────┘  └────────┬────────┘  └────────┬────────┘              │
│           │                    │                    │                       │
│  ┌────────┴────────┐           │                    │                       │
│  │ Statecharts     │           │                    │                       │
│  │ .Runtime        │           │                    │                       │
│  └────────┬────────┘           │                    │                       │
│           │                    │                    │                       │
│  ┌────────┴────────┐           │                    │                       │
│  │ Statecharts     │           │                    │                       │
│  │ .Parsers        │           │                    │                       │
│  └────────┬────────┘           │                    │                       │
│           │                    │                    │                       │
│  ┌────────┴────────┐           │                    │                       │
│  │ Statecharts     │           │                    │                       │
│  │ .Generators     │           │                    │                       │
│  └─────────────────┘           │                    │                       │
└────────────────────────────────┼────────────────────┼───────────────────────┘
                                 │                    │
┌────────────────────────────────┼────────────────────┼───────────────────────┐
│  LAYER 1: Frank HTTP Integration (+ ASP.NET Core)                           │
│                                                                             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐              │
│  │ Frank           │  │ Frank           │  │ Frank           │              │
│  │ .LinkedData     │  │ .Validation     │  │ .Provenance     │              │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘              │
│                                                                             │
│  ┌─────────────────┐                                                        │
│  │ Frank           │                                                        │
│  │ .Discovery      │  (Static ALPS, Link headers, OPTIONS)                  │
│  └─────────────────┘                                                        │
└────────────────────────────────────────────────────────────────────────────┘
                                 │
┌────────────────────────────────┼────────────────────────────────────────────┐
│  LAYER 2: Statechart Integration                                            │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ Frank.Statecharts                                                   │    │
│  │                                                                     │    │
│  │ • StatefulResourceBinding (composition model)                       │    │
│  │ • StatefulResourceBuilder CE                                        │    │
│  │ • State-dependent affordances, validation, provenance               │    │
│  │ • Role projections                                                  │    │
│  │ • Middleware (useAffordances, useStatecharts)                       │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ Frank.Statecharts.Analyzers                                         │    │
│  │                                                                     │    │
│  │ • FRANK101-108: Structural validation                               │    │
│  │ • FRANK201-208: Semantic validation (reachability, deadlock)        │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 5.2 Package Descriptions

|Package                      |Dependencies          |Responsibility                    |
|-----------------------------|----------------------|----------------------------------|
|`Statecharts.Core`           |FSharp.Core           |AST types, algebras, parse results|
|`Statecharts.Runtime`        |Statecharts.Core      |StatechartAgent, Harel semantics  |
|`Statecharts.Parsers`        |Statecharts.Core      |WSD, SCXML, smcat parsers         |
|`Statecharts.Generators`     |Statecharts.Core      |SCXML, XState, smcat generators   |
|`Validation.Core`            |FSharp.Core           |SHACL types, shape algebra        |
|`Provenance.Core`            |FSharp.Core           |PROV-O types, provenance algebra  |
|`LinkedData.Core`            |FSharp.Core           |RDF types, graph algebra          |
|`Frank.LinkedData`           |LinkedData.Core, Frank|Content negotiation middleware    |
|`Frank.Validation`           |Validation.Core, Frank|Request/response validation       |
|`Frank.Provenance`           |Provenance.Core, Frank|Request audit middleware          |
|`Frank.Discovery`            |Frank                 |Static ALPS, Link headers         |
|`Frank.Statecharts`          |All above             |Composition model, CE, middleware |
|`Frank.Statecharts.Analyzers`|Statecharts.Core      |Compile-time validation           |

-----

## 6. Type Specifications

### 6.1 Statecharts.Core

```fsharp
namespace Statecharts.Core

/// State decomposition type (Harel's ψ function)
[<RequireQualifiedAccess>]
type StateType =
    | Basic      // Leaf state, no children
    | Compound   // OR-decomposition: exclusive children
    | Parallel   // AND-decomposition: concurrent children

/// History marker type
[<RequireQualifiedAccess>]
type HistoryKind =
    | None
    | Shallow    // Remember immediate child only
    | Deep       // Remember full sub-configuration

/// A state in the hierarchy (Harel's S with ρ structure)
type StateNode = {
    /// Unique identifier within the statechart
    Id: StateId
    
    /// Decomposition type
    Type: StateType
    
    /// Child states (ρ function)
    Children: StateNode list
    
    /// Initial child state for compound states (δ function)
    Initial: StateId option
    
    /// History marker
    History: HistoryKind
    
    /// Entry/exit actions (optional)
    OnEntry: Action list
    OnExit: Action list
}

and StateId = StateId of string

and Action = 
    | Raise of EventId           // Generate event for broadcast
    | Assign of string * string  // Variable assignment
    | Custom of string           // Extension point

/// A transition between states
type TransitionEdge = {
    /// Unique identifier
    Id: TransitionId
    
    /// Source state(s) — can be multiple for join transitions
    Sources: StateId list
    
    /// Triggering event
    Event: EventId
    
    /// Guard condition (optional)
    Guard: Guard option
    
    /// Target state(s) — can be multiple for fork transitions
    Targets: StateId list
    
    /// Actions to execute on transition
    Actions: Action list
}

and TransitionId = TransitionId of string
and EventId = EventId of string

and Guard = 
    | Expression of string
    | InState of StateId
    | And of Guard * Guard
    | Or of Guard * Guard
    | Not of Guard

/// The complete statechart document (Harel's ST tuple)
type StatechartDocument = {
    /// Document identifier
    Id: string
    
    /// Root state (contains full hierarchy)
    Root: StateNode
    
    /// All transitions
    Transitions: TransitionEdge list
    
    /// Initial configuration (default states)
    Initial: StateId list
}

/// Configuration: set of active states (maximal orthogonal set)
type Configuration = Set<StateId>

/// Result of parsing
type ParseResult<'T> =
    | Success of 'T
    | Failure of ParseError list

and ParseError = {
    Message: string
    Location: SourceLocation option
}

and SourceLocation = {
    Line: int
    Column: int
    Source: string option
}
```

### 6.2 Statecharts.Runtime

```fsharp
namespace Statecharts.Runtime

open Statecharts.Core

/// Messages processed by the statechart agent
type StatechartMessage<'TEvent> =
    | Fire of event: 'TEvent * reply: AsyncReplyChannel<FireResult>
    | Query of query: StateQuery * reply: AsyncReplyChannel<QueryResult>
    | Snapshot of reply: AsyncReplyChannel<AgentState<'TEvent>>
    | Restore of state: AgentState<'TEvent>

/// Result of firing an event
and FireResult =
    | Transitioned of TransitionResult
    | Blocked of BlockReason
    | NoTransition

and TransitionResult = {
    FromConfiguration: Configuration
    ToConfiguration: Configuration
    TransitionsFired: TransitionId list
    ActionsExecuted: Action list
    EventsGenerated: EventId list
}

and BlockReason =
    | GuardFailed of TransitionId * Guard
    | NotInSourceState of required: StateId * actual: Configuration
    | Conflict of TransitionId list
    | InvalidTarget of StateId

/// Queries against current state
and StateQuery =
    | IsActive of StateId
    | EnabledTransitions of EventId
    | CurrentConfiguration
    | HistoryOf of StateId
    | VariableValue of string

and QueryResult =
    | Bool of bool
    | Transitions of TransitionId list
    | Config of Configuration
    | Value of obj option

/// Internal agent state
type AgentState<'TEvent> = {
    /// Current configuration
    Configuration: Configuration
    
    /// History memory
    History: Map<StateId, Configuration>
    
    /// Extended state variables
    Variables: Map<string, obj>
    
    /// Pending events (for broadcast within macrostep)
    PendingEvents: EventId list
    
    /// Execution trace (for provenance)
    Trace: TraceEntry list
}

and TraceEntry = {
    Timestamp: DateTimeOffset
    Event: EventId
    FromConfiguration: Configuration
    ToConfiguration: Configuration
    TransitionId: TransitionId option
}

/// The statechart agent interface
type IStatechartAgent<'TEvent> =
    /// Fire an event and wait for result
    abstract Fire : 'TEvent -> Async<FireResult>
    
    /// Query current state
    abstract Query : StateQuery -> Async<QueryResult>
    
    /// Get full state snapshot
    abstract Snapshot : unit -> Async<AgentState<'TEvent>>
    
    /// Restore from snapshot
    abstract Restore : AgentState<'TEvent> -> unit

/// Factory for creating agents
module StatechartAgent =
    
    /// Create agent from document
    val fromDocument : 
        document: StatechartDocument -> 
        eventMapper: ('TEvent -> EventId) ->
        IStatechartAgent<'TEvent>
    
    /// Create agent from polymorphic statechart
    val fromStatechart : 
        statechart: Statechart -> 
        eventMapper: ('TEvent -> EventId) ->
        IStatechartAgent<'TEvent>
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

/// SHACL property shape
type PropertyShape = {
    Path: PropertyPath
    Name: string option
    Description: string option
    Constraints: Constraint list
}

/// SHACL node shape
type NodeShape = {
    Id: ShapeId
    TargetClass: string option
    TargetNode: string option
    Properties: PropertyShape list
    Closed: bool
    IgnoredProperties: PropertyPath list
}

and ShapeId = ShapeId of string

/// Validation result
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

/// PROV-O Entity
type Entity = {
    Id: EntityId
    GeneratedAtTime: DateTimeOffset option
    InvalidatedAtTime: DateTimeOffset option
    Value: obj option
}

and EntityId = EntityId of string

/// PROV-O Activity
type Activity = {
    Id: ActivityId
    StartedAtTime: DateTimeOffset
    EndedAtTime: DateTimeOffset option
    Type: string option
}

and ActivityId = ActivityId of string

/// PROV-O Agent
type Agent = {
    Id: AgentId
    Name: string option
    Type: AgentType
}

and AgentId = AgentId of string
and AgentType = Person | Organization | SoftwareAgent

/// PROV-O Relations
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
type Delegation = { Delegate: AgentId; Responsible: AgentId; Activity: ActivityId option }

/// Complete provenance graph
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

### 6.5 Frank.Statecharts — Composition Model

```fsharp
namespace Frank.Statecharts

open Statecharts.Core
open Statecharts.Runtime
open Validation.Core
open Provenance.Core

/// Affordance definition
type Affordance = {
    /// Link relation (e.g., "submit", "cancel", "self")
    Rel: string
    
    /// Target href (can include templates)
    Href: string
    
    /// HTTP method (if applicable)
    Method: HttpMethod option
    
    /// Content types accepted
    Accepts: string list
    
    /// Human-readable title
    Title: string option
}

/// Role projection definition
type RoleProjection = {
    /// Identifier for this role
    RoleId: RoleId
    
    /// States visible to this role
    VisibleStates: StateId list
    
    /// Transitions this role can trigger
    AllowedTransitions: TransitionId list
    
    /// State-specific overrides
    StateOverrides: Map<StateId, StateRoleOverride>
}

and RoleId = RoleId of string

and StateRoleOverride = {
    HiddenAffordances: string list
    ExtraAffordances: Affordance list
}

/// Extended HMBS tuple — the composition model
type StatefulResourceBinding<'TEvent> = {
    // ═══════════════════════════════════════════════════════════════════════
    // HMBS Core Components
    // ═══════════════════════════════════════════════════════════════════════
    
    /// ST: The statechart structure
    Statechart: StatechartDocument
    
    /// P + m: State → Handler mapping
    Handlers: Map<StateId, 'TEvent -> HttpContext -> Task<unit>>
    
    /// ae: Anchor → Event mapping (via affordances)
    Affordances: Map<StateId, Affordance list>
    
    /// N: Visibility level for hierarchy projection
    VisibilityLevel: int
    
    // ═══════════════════════════════════════════════════════════════════════
    // Frank Extensions
    // ═══════════════════════════════════════════════════════════════════════
    
    /// State-dependent validation shapes
    Validation: Map<StateId, NodeShape> option
    
    /// Provenance tracking configuration
    Provenance: ProvenanceOptions option
    
    /// Role projections for multi-party protocols
    Roles: Map<RoleId, RoleProjection> option
    
    /// Event mapping function
    EventMapper: 'TEvent -> EventId
    
    // ═══════════════════════════════════════════════════════════════════════
    // Metadata
    // ═══════════════════════════════════════════════════════════════════════
    
    /// Resource path template
    Path: string
    
    /// Resource name for discovery
    Name: string
}

and ProvenanceOptions = {
    /// How to extract agent from request
    AgentExtractor: HttpContext -> Agent
    
    /// Whether to track read operations
    TrackReads: bool
    
    /// Provenance store
    Store: IProvenanceStore
}

/// Result of projecting for a role
type ProjectedView = {
    /// Affordances visible to this role in current state
    Affordances: Affordance list
    
    /// Validation shapes applicable to this role
    Validation: NodeShape list
    
    /// States visible to this role
    VisibleConfiguration: Configuration
    
    /// Transitions this role can trigger
    EnabledTransitions: TransitionId list
}
```

-----

## 7. Algebra Specifications

### 7.1 IStatechartAlgebra

```fsharp
namespace Statecharts.Core

/// Tagless-final algebra for statechart construction
type IStatechartAlgebra<'repr> =
    /// Empty/unit element
    abstract empty : 'repr
    
    /// Basic state
    abstract basicState : id: string -> 'repr
    
    /// Compound state (OR-decomposition)
    abstract compoundState : id: string -> initial: string -> children: 'repr list -> 'repr
    
    /// Parallel state (AND-decomposition)
    abstract parallelState : id: string -> regions: 'repr list -> 'repr
    
    /// History state
    abstract historyState : id: string -> kind: HistoryKind -> 'repr
    
    /// Transition
    abstract transition : source: string -> event: string -> target: string -> 'repr
    
    /// Guarded transition
    abstract guardedTransition : source: string -> event: string -> guard: string -> target: string -> 'repr
    
    /// Transition with actions
    abstract transitionWithActions : source: string -> event: string -> target: string -> actions: string list -> 'repr
    
    /// Complete document
    abstract document : id: string -> root: 'repr -> transitions: 'repr list -> 'repr
    
    /// Combine two elements
    abstract combine : 'repr -> 'repr -> 'repr

/// Polymorphic statechart (can be interpreted multiple ways)
[<Struct>]
type Statechart = {
    Run: IStatechartAlgebra<'a> -> 'a
}
```

### 7.2 Standard Interpreters

```fsharp
namespace Statecharts.Core.Interpreters

/// Reify to AST (the "initial" encoding)
type ReifyAlgebra() =
    interface IStatechartAlgebra<StatechartNode> with
        // ... produces AST nodes

/// Pretty-print
type PrettyPrintAlgebra() =
    interface IStatechartAlgebra<string> with
        // ... produces formatted string

/// Collect all state IDs
type StateCollectorAlgebra() =
    interface IStatechartAlgebra<Set<StateId>> with
        // ... collects state identifiers

/// Collect all transitions
type TransitionCollectorAlgebra() =
    interface IStatechartAlgebra<TransitionEdge list> with
        // ... collects transitions

/// Compute reachability (states, edges)
type ReachabilityAlgebra() =
    interface IStatechartAlgebra<Set<StateId> * Set<StateId * StateId>> with
        // ... computes reachable states and edges
```

### 7.3 IAffordanceAlgebra

```fsharp
namespace Frank.Statecharts

/// Algebra for affordance rendering
type IAffordanceAlgebra<'repr> =
    /// Single affordance
    abstract affordance : rel: string -> href: string -> method: HttpMethod option -> 'repr
    
    /// Link (subset of affordance)
    abstract link : href: string -> rel: string -> 'repr
    
    /// Combine multiple affordances
    abstract combine : 'repr list -> 'repr
    
    /// Empty
    abstract empty : 'repr

/// Link header interpreter
type LinkHeaderAlgebra() =
    interface IAffordanceAlgebra<string list> with
        member _.affordance rel href method =
            let methodAttr = method |> Option.map (sprintf "; method=\"%O\"") |> Option.defaultValue ""
            [sprintf "<%s>; rel=\"%s\"%s" href rel methodAttr]
        member _.link href rel = [sprintf "<%s>; rel=\"%s\"" href rel]
        member _.combine items = List.concat items
        member _.empty = []

/// Allow header interpreter
type AllowHeaderAlgebra() =
    interface IAffordanceAlgebra<Set<HttpMethod>> with
        member _.affordance _ _ method = 
            method |> Option.map Set.singleton |> Option.defaultValue Set.empty
        member _.link _ _ = Set.empty
        member _.combine items = Set.unionMany items
        member _.empty = Set.empty

/// ALPS descriptor interpreter
type ALPSAlgebra() =
    interface IAffordanceAlgebra<ALPSDescriptor list> with
        // ... produces ALPS descriptors
```

### 7.4 IProvOAlgebra

```fsharp
namespace Provenance.Core

/// Algebra for PROV-O construction
type IProvOAlgebra<'repr> =
    abstract entity : id: string -> 'repr
    abstract activity : id: string -> startedAt: DateTimeOffset -> 'repr
    abstract agent : id: string -> agentType: AgentType -> 'repr
    
    abstract wasGeneratedBy : entity: 'repr -> activity: 'repr -> 'repr
    abstract used : activity: 'repr -> entity: 'repr -> 'repr
    abstract wasDerivedFrom : generated: 'repr -> used: 'repr -> 'repr
    abstract wasAttributedTo : entity: 'repr -> agent: 'repr -> 'repr
    abstract wasAssociatedWith : activity: 'repr -> agent: 'repr -> 'repr
    abstract actedOnBehalfOf : delegate: 'repr -> responsible: 'repr -> 'repr
    
    abstract combine : 'repr list -> 'repr

/// Build ProvenanceGraph
type GraphBuildingAlgebra() =
    interface IProvOAlgebra<ProvenanceGraph -> ProvenanceGraph> with
        // ... accumulates into graph

/// Render as Turtle
type TurtleAlgebra(baseUri: string) =
    interface IProvOAlgebra<string> with
        // ... produces Turtle RDF
```

-----

## 8. Runtime Specifications

### 8.1 Agent Implementation

The `StatechartAgent` implements Harel’s operational semantics:

```fsharp
namespace Statecharts.Runtime

module internal Semantics =
    
    /// Compute the Least Common Ancestor of a set of states
    let computeLCA (doc: StatechartDocument) (states: StateId list) : StateId =
        // ... tree traversal
    
    /// Compute states to exit when firing a transition
    let computeExitSet (doc: StatechartDocument) (config: Configuration) (transition: TransitionEdge) : StateId list =
        // Exit states within scope(transition) that are currently active
        // Bottom-up order (children before parents)
        // ...
    
    /// Compute states to enter when firing a transition
    let computeEntrySet (doc: StatechartDocument) (config: Configuration) (transition: TransitionEdge) : StateId list =
        // Enter states from LCA to target, plus defaults for compound states
        // Handle history: restore from history map if entering H state
        // Top-down order (parents before children)
        // ...
    
    /// Find enabled transitions for an event in current configuration
    let findEnabledTransitions (doc: StatechartDocument) (config: Configuration) (event: EventId) (variables: Map<string, obj>) : TransitionEdge list =
        // 1. Find transitions where source ⊆ config
        // 2. Filter by event match
        // 3. Evaluate guards
        // 4. Apply priority (inner > outer, specificity)
        // ...
    
    /// Detect conflicts between transitions
    let detectConflicts (transitions: TransitionEdge list) : TransitionEdge list list =
        // Transitions conflict if their exit sets overlap
        // ...
    
    /// Execute one macrostep (external event to quiescence)
    let rec macrostep (doc: StatechartDocument) (event: EventId) (state: AgentState<'E>) : AgentState<'E> * FireResult =
        let enabled = findEnabledTransitions doc state.Configuration event state.Variables
        
        match enabled with
        | [] -> 
            (state, NoTransition)
        
        | [t] ->
            let exitSet = computeExitSet doc state.Configuration t
            let entrySet = computeEntrySet doc state.Configuration t
            
            // Record history before exiting
            let history = recordHistory doc exitSet state.Configuration state.History
            
            // Update configuration
            let newConfig = 
                state.Configuration
                |> Set.filter (fun s -> not (List.contains s exitSet))
                |> Set.union (Set.ofList entrySet)
            
            // Execute actions, collect generated events
            let variables, generatedEvents = executeActions t.Actions state.Variables
            
            // Update state
            let newState = {
                state with
                    Configuration = newConfig
                    History = history
                    Variables = variables
                    PendingEvents = state.PendingEvents @ generatedEvents
                    Trace = {
                        Timestamp = DateTimeOffset.UtcNow
                        Event = event
                        FromConfiguration = state.Configuration
                        ToConfiguration = newConfig
                        TransitionId = Some t.Id
                    } :: state.Trace
            }
            
            // Process broadcast events (chain reaction)
            let finalState = processBroadcast doc newState
            
            (finalState, Transitioned { ... })
        
        | conflicts ->
            (state, Blocked (Conflict (conflicts |> List.map _.Id)))
    
    and processBroadcast (doc: StatechartDocument) (state: AgentState<'E>) : AgentState<'E> =
        match state.PendingEvents with
        | [] -> state  // Quiescence
        | event :: rest ->
            let state = { state with PendingEvents = rest }
            let state, _ = macrostep doc event state
            processBroadcast doc state
```

### 8.2 Agent Creation

```fsharp
module StatechartAgent =
    
    /// Create an agent from a statechart document
    let fromDocument<'TEvent> 
        (document: StatechartDocument) 
        (eventMapper: 'TEvent -> EventId) 
        : IStatechartAgent<'TEvent> =
        
        let initialConfig = computeInitialConfiguration document
        
        let agent = MailboxProcessor.Start(fun inbox ->
            let rec loop (state: AgentState<'TEvent>) = async {
                let! msg = inbox.Receive()
                
                match msg with
                | Fire(event, reply) ->
                    let eventId = eventMapper event
                    let newState, result = Semantics.macrostep document eventId state
                    reply.Reply(result)
                    return! loop newState
                
                | Query(query, reply) ->
                    let result = handleQuery state query
                    reply.Reply(result)
                    return! loop state
                
                | Snapshot(reply) ->
                    reply.Reply(state)
                    return! loop state
                
                | Restore(snapshot) ->
                    return! loop snapshot
            }
            
            loop {
                Configuration = initialConfig
                History = Map.empty
                Variables = Map.empty
                PendingEvents = []
                Trace = []
            }
        )
        
        { new IStatechartAgent<'TEvent> with
            member _.Fire(event) = agent.PostAndAsyncReply(fun ch -> Fire(event, ch))
            member _.Query(query) = agent.PostAndAsyncReply(fun ch -> Query(query, ch))
            member _.Snapshot() = agent.PostAndAsyncReply(Snapshot)
            member _.Restore(state) = agent.Post(Restore state) }
```

-----

## 9. Composition Model

### 9.1 Building a StatefulResourceBinding

```fsharp
namespace Frank.Statecharts

type StatefulResourceBuilder(path: string) =
    
    member val State = {
        Statechart = None
        Handlers = Map.empty
        Affordances = Map.empty
        Validation = None
        Provenance = None
        Roles = None
        VisibilityLevel = 0
        Path = path
        Name = ""
    } with get, set
    
    [<CustomOperation("name")>]
    member this.Name(state, name) =
        { state with Name = name }
    
    [<CustomOperation("statechart")>]
    member this.Statechart(state, sc: Statechart) =
        { state with Statechart = Some (Reify.toDocument sc) }
    
    [<CustomOperation("statechartDoc")>]
    member this.StatechartDoc(state, doc: StatechartDocument) =
        { state with Statechart = Some doc }
    
    [<CustomOperation("inState")>]
    member this.InState(state, stateId: string, handlers: StateHandlers<'E>) =
        let sid = StateId stateId
        { state with 
            Handlers = state.Handlers |> Map.add sid handlers.Handler
            Affordances = state.Affordances |> Map.add sid handlers.Affordances }
    
    [<CustomOperation("whenIn")>]
    member this.WhenIn(state, stateId: string, affordances: Affordance list) =
        let sid = StateId stateId
        let existing = state.Affordances |> Map.tryFind sid |> Option.defaultValue []
        { state with Affordances = state.Affordances |> Map.add sid (existing @ affordances) }
    
    [<CustomOperation("validateInState")>]
    member this.ValidateInState(state, stateId: string, shape: NodeShape) =
        let sid = StateId stateId
        let validation = state.Validation |> Option.defaultValue Map.empty
        { state with Validation = Some (validation |> Map.add sid shape) }
    
    [<CustomOperation("trackTransitions")>]
    member this.TrackTransitions(state, options: ProvenanceOptions) =
        { state with Provenance = Some options }
    
    [<CustomOperation("forRole")>]
    member this.ForRole(state, roleId: string, projection: RoleProjection) =
        let rid = RoleId roleId
        let roles = state.Roles |> Option.defaultValue Map.empty
        { state with Roles = Some (roles |> Map.add rid projection) }
    
    [<CustomOperation("visibilityLevel")>]
    member this.VisibilityLevel(state, level: int) =
        { state with VisibilityLevel = level }
    
    member this.Run(state) : StatefulResourceBinding<'E> =
        match state.Statechart with
        | None -> failwith "Statechart is required"
        | Some sc -> { state with Statechart = sc }

let statefulResource path = StatefulResourceBuilder(path)
```

### 9.2 Example Usage

```fsharp
let orderResource = statefulResource "/orders/{id}" {
    name "Order"
    
    statechart orderStatechart
    
    trackTransitions {
        AgentExtractor = extractUserAgent
        TrackReads = false
        Store = provenanceStore
    }
    
    // State-dependent affordances
    whenIn "Draft" [
        { Rel = "submit"; Href = "./submit"; Method = Some POST; Accepts = []; Title = Some "Submit order" }
        { Rel = "edit"; Href = "."; Method = Some PUT; Accepts = ["application/json"]; Title = Some "Edit order" }
        { Rel = "cancel"; Href = "./cancel"; Method = Some POST; Accepts = []; Title = Some "Cancel order" }
    ]
    whenIn "Submitted" [
        { Rel = "approve"; Href = "./approve"; Method = Some POST; Accepts = []; Title = Some "Approve order" }
        { Rel = "reject"; Href = "./reject"; Method = Some POST; Accepts = []; Title = Some "Reject order" }
    ]
    whenIn "Approved" [
        { Rel = "ship"; Href = "./ship"; Method = Some POST; Accepts = []; Title = Some "Ship order" }
    ]
    
    // State-dependent validation
    validateInState "Draft" draftOrderShape
    validateInState "Submitted" submittedOrderShape
    
    // Role projections
    forRole "Customer" {
        RoleId = RoleId "Customer"
        VisibleStates = [StateId "Draft"; StateId "Submitted"; StateId "Approved"; StateId "Shipped"]
        AllowedTransitions = [TransitionId "submit"; TransitionId "cancel"]
        StateOverrides = Map.empty
    }
    forRole "Approver" {
        RoleId = RoleId "Approver"
        VisibleStates = [StateId "Submitted"; StateId "Approved"; StateId "Rejected"]
        AllowedTransitions = [TransitionId "approve"; TransitionId "reject"]
        StateOverrides = Map.empty
    }
    forRole "Warehouse" {
        RoleId = RoleId "Warehouse"
        VisibleStates = [StateId "Approved"; StateId "Shipped"]
        AllowedTransitions = [TransitionId "ship"]
        StateOverrides = Map.empty
    }
    
    // State-dependent handlers
    inState "Draft" {
        get (fun ctx -> getDraftOrder ctx)
        put (fun ctx -> updateDraft ctx)
        fires "edit"
    }
    inState "Submitted" {
        get (fun ctx -> getSubmittedOrder ctx)
    }
    inState "Approved" {
        get (fun ctx -> getApprovedOrder ctx)
    }
}
```

-----

## 10. Role Projections

### 10.1 Projection Function

```fsharp
namespace Frank.Statecharts.Roles

module Projection =
    
    /// Project a binding for a specific role and current configuration
    let project 
        (role: RoleId) 
        (config: Configuration) 
        (binding: StatefulResourceBinding<'E>) 
        : ProjectedView =
        
        match binding.Roles |> Option.bind (Map.tryFind role) with
        | None ->
            // No projection = full visibility
            {
                Affordances = collectAffordances config binding.Affordances
                Validation = collectValidation config binding.Validation
                VisibleConfiguration = config
                EnabledTransitions = getAllTransitions binding.Statechart
            }
        
        | Some projection ->
            let visibleConfig = 
                config 
                |> Set.filter (fun s -> List.contains s projection.VisibleStates)
            
            let affordances =
                visibleConfig
                |> Set.toList
                |> List.collect (fun stateId ->
                    binding.Affordances
                    |> Map.tryFind stateId
                    |> Option.defaultValue []
                    |> List.filter (fun a -> 
                        projection.AllowedTransitions 
                        |> List.exists (fun (TransitionId tid) -> tid = a.Rel))
                    |> applyOverrides stateId projection.StateOverrides)
            
            let validation =
                visibleConfig
                |> Set.toList
                |> List.choose (fun s ->
                    binding.Validation |> Option.bind (Map.tryFind s))
            
            let enabledTransitions =
                projection.AllowedTransitions
                |> List.filter (fun tid ->
                    binding.Statechart.Transitions
                    |> List.exists (fun t -> 
                        t.Id = tid && 
                        t.Sources |> List.forall (fun s -> Set.contains s config)))
            
            {
                Affordances = affordances
                Validation = validation
                VisibleConfiguration = visibleConfig
                EnabledTransitions = enabledTransitions
            }
```

### 10.2 Integration with HTTP Pipeline

```fsharp
namespace Frank.Statecharts.Middleware

module RoleProjection =
    
    /// Middleware that projects affordances for the authenticated role
    let useRoleProjection<'E> 
        (binding: StatefulResourceBinding<'E>) 
        (agent: IStatechartAgent<'E>)
        (roleExtractor: HttpContext -> RoleId option)
        : HttpHandler =
        
        fun next ctx -> task {
            // Get current configuration
            let! queryResult = agent.Query(CurrentConfiguration)
            let config = 
                match queryResult with
                | Config c -> c
                | _ -> Set.empty
            
            // Get role (default to anonymous/full access if not authenticated)
            let role = roleExtractor ctx |> Option.defaultValue (RoleId "anonymous")
            
            // Project
            let view = Projection.project role config binding
            
            // Set headers
            let linkHeaders = 
                view.Affordances 
                |> List.map (LinkHeaderAlgebra().affordance)
                |> List.concat
            
            for link in linkHeaders do
                ctx.Response.Headers.Append("Link", link)
            
            let allowMethods =
                view.Affordances
                |> List.choose _.Method
                |> List.distinct
                |> List.map string
                |> String.concat ", "
            
            if not (String.IsNullOrEmpty allowMethods) then
                ctx.Response.Headers.Add("Allow", allowMethods)
            
            // Store projected view for handler access
            ctx.Items.["ProjectedView"] <- view
            
            return! next ctx
        }
```

-----

## 11. Build Plan

### Phase 1: Core Models (Weeks 1-2)

**Parallel workstreams:**

|Package           |Deliverables                                                       |Tests                          |
|------------------|-------------------------------------------------------------------|-------------------------------|
|`Statecharts.Core`|AST types, IStatechartAlgebra, standard interpreters, reflect/reify|Unit tests for each interpreter|
|`Validation.Core` |SHACL types, IShapeAlgebra, JSON Schema interop                    |Shape construction tests       |
|`Provenance.Core` |PROV-O types, IProvOAlgebra, Turtle serializer                     |Graph building tests           |
|`LinkedData.Core` |RDF types, IRdfAlgebra, serializers (Turtle, JSON-LD)              |Serialization round-trip       |

**Exit criteria:**

- All types compile with no external dependencies beyond FSharp.Core
- Algebras have at least one working interpreter each
- Reflect/reify round-trips preserve structure

### Phase 2: Statechart Runtime (Weeks 2-3)

|Package              |Deliverables                                      |Tests                                    |
|---------------------|--------------------------------------------------|-----------------------------------------|
|`Statecharts.Runtime`|StatechartAgent, Harel semantics, snapshot/restore|Semantics tests from Harel paper examples|

**Critical test cases:**

- Basic transition firing
- Compound state entry (default child activation)
- Parallel state entry (all regions activate)
- Exit actions fire before entry actions
- History (shallow and deep)
- Broadcast (events generated in same macrostep)
- Priority (inner transitions override outer)
- Guard evaluation
- Conflict detection

**Exit criteria:**

- All Harel semantic tests pass
- Agent handles 10,000 events without memory leak
- Snapshot/restore produces identical behavior

### Phase 3: Parsers and Generators (Weeks 3-4)

**Parallel with Phase 2:**

|Package                 |Deliverables                                      |Tests                                |
|------------------------|--------------------------------------------------|-------------------------------------|
|`Statecharts.Parsers`   |WSD parser, SCXML parser, smcat parser            |Parse → AST → generate round-trip    |
|`Statecharts.Generators`|SCXML generator, XState generator, smcat generator|Generate valid output for each format|

**Exit criteria:**

- Parse all sample files without error
- Generated output validates against format schemas
- Round-trip preserves semantics (parse → generate → parse = original AST)

### Phase 4: Frank HTTP Integration (Weeks 4-5)

**Parallel workstreams:**

|Package           |Deliverables                                     |Tests                        |
|------------------|-------------------------------------------------|-----------------------------|
|`Frank.LinkedData`|Content negotiation, useLinkedData, RDF endpoints|Accept header handling       |
|`Frank.Validation`|Request validation, useValidation                |400 response on invalid input|
|`Frank.Provenance`|Request tracking, useProvenance                  |Provenance records created   |
|`Frank.Discovery` |Static ALPS, Link headers, OPTIONS               |Header presence tests        |

**Exit criteria:**

- Each package works independently (no statecharts required)
- Integration tests with ASP.NET Core TestServer
- Content negotiation selects correct format

### Phase 5: Statechart Integration (Weeks 5-7)

|Package            |Deliverables                                             |Tests                 |
|-------------------|---------------------------------------------------------|----------------------|
|`Frank.Statecharts`|StatefulResourceBinding, CE, middleware, role projections|Full integration tests|

**Deliverables:**

- StatefulResourceBuilder CE
- useStatecharts middleware
- useRoleProjection middleware
- State-dependent affordances
- State-dependent validation
- Transition provenance
- Role projections

**Exit criteria:**

- TicTacToe sample works end-to-end
- Order workflow sample demonstrates multi-party protocol
- Role projections correctly filter affordances
- Provenance tracks all state transitions

### Phase 6: Analyzers (Week 7-8)

|Package                      |Deliverables                                      |Tests                 |
|-----------------------------|--------------------------------------------------|----------------------|
|`Frank.Statecharts.Analyzers`|FRANK101-108 (structural), FRANK201-208 (semantic)|Analyzer test projects|

**Analyzer rules:**

- FRANK101: Duplicate handler detection
- FRANK102: Missing state handler
- FRANK103: Unreferenced state in affordances
- FRANK104: Invalid transition target
- FRANK201: Unreachable state
- FRANK202: Deadlock detection
- FRANK203: Livelock detection (SCC analysis)
- FRANK204: Missing role projection for state

**Exit criteria:**

- Analyzers run during build
- Clear error messages with fix suggestions
- No false positives on sample projects

### Phase 7: Documentation and Samples (Week 8)

- TicTacToe sample (basic stateful resource)
- Order Workflow sample (multi-party, provenance)
- API Documentation sample (ALPS, LinkedData)
- Migration guide from previous Frank versions

-----

## 12. Migration Guide

### 12.1 From Previous Unified AST

**Before:**

```fsharp
// Frank.Statecharts.Core
type StatechartDocument = {
    States: StateNode list
    Transitions: TransitionEdge list
    Affordances: Map<StateId, Affordance list>  // Embedded
    Shapes: Map<StateId, NodeShape>              // Embedded
}
```

**After:**

```fsharp
// Statecharts.Core (structure only)
type StatechartDocument = {
    Root: StateNode
    Transitions: TransitionEdge list
}

// Frank.Statecharts (composition)
type StatefulResourceBinding = {
    Statechart: StatechartDocument              // Reference
    Affordances: Map<StateId, Affordance list>  // Separate
    Validation: Map<StateId, NodeShape> option  // Separate
}
```

### 12.2 From Pattern-Matching Interpreters

**Before:**

```fsharp
let rec toSCXML (node: StateNode) =
    match node.Type with
    | Basic -> XElement("state", XAttribute("id", node.Id))
    | Compound -> 
        let children = node.Children |> List.map toSCXML
        XElement("state", XAttribute("id", node.Id), children)
    // ...
```

**After:**

```fsharp
type SCXMLAlgebra() =
    interface IStatechartAlgebra<XElement> with
        member _.basicState id = XElement("state", XAttribute("id", id))
        member _.compoundState id initial children =
            XElement("state", 
                XAttribute("id", id),
                XAttribute("initial", initial),
                children)
        // ...

// Usage
let scxml = myStatechart |> Statechart.interpret (SCXMLAlgebra())
```

### 12.3 From Ad-Hoc Runtime

**Before:**

```fsharp
let mutable currentState = initialState

let handleEvent event =
    match currentState, event with
    | "Draft", "submit" -> currentState <- "Submitted"
    | "Submitted", "approve" -> currentState <- "Approved"
    | _ -> ()
```

**After:**

```fsharp
let agent = StatechartAgent.fromDocument orderStatechart eventMapper

let handleEvent event = async {
    let! result = agent.Fire(event)
    match result with
    | Transitioned t -> 
        log $"Transitioned from {t.FromConfiguration} to {t.ToConfiguration}"
    | Blocked reason ->
        log $"Blocked: {reason}"
    | NoTransition ->
        log "No applicable transition"
}
```

-----

## 13. References

### 13.1 Academic Papers

1. **Harel, D. (1987).** Statecharts: A Visual Formalism for Complex Systems. *Science of Computer Programming*, 8(3), 231-274.
- Original statechart formalism
- Defines hierarchy, orthogonality, broadcast, history
1. **Harel, D., & Naamad, A. (1996).** The STATEMATE Semantics of Statecharts. *ACM TOSEM*, 5(4), 293-333.
- Operational semantics
- Step semantics, priority rules
1. **de Oliveira, M.C.F., Turine, M.A.S., & Masiero, P.C. (2001).** A Statechart-Based Model for Hypermedia Applications. *ACM TOIS*, 19(1), 28-52.
- HMBS model: Hip = ⟨ST, P, m, ae, N⟩
- Visibility levels, reachability analysis
1. **Carette, J., Kiselyov, O., & Shan, C. (2009).** Finally Tagless, Partially Evaluated: Tagless Staged Interpreters for Simpler Typed Languages. *JFP*, 19(5), 509-543.
- Tagless-final encoding
- Initial/final duality, reflect/reify
1. **Deniélou, P.M., & Yoshida, N. (2012).** Multiparty Session Types Meet Communicating Automata. *ESOP 2012*, LNCS 7211, 194-213.
- Multi-party session types
- Role projections

### 13.2 Online Resources

1. **Kiselyov, O.** Typed Tagless Final Interpreters. SSGIP 2010 Lecture Notes.
- https://okmij.org/ftp/tagless-final/course/lecture.pdf
1. **Kiselyov, O.** Tagless-Final Style.
- https://okmij.org/ftp/tagless-final/index.html
1. **Azariah, J. (2025).** Tagless Final in F# (6-part series). FsAdvent 2025.
- https://johnazariah.github.io/2025/12/12/tagless-final-06-model-verification.html
- “Code as Model” insight
1. **Serokell.** Introduction to Tagless Final.
- https://serokell.io/blog/introduction-tagless-final

### 13.3 Standards

1. **W3C.** State Chart XML (SCXML): State Machine Notation for Control Abstraction.
- https://www.w3.org/TR/scxml/
1. **ALPS.** Application-Level Profile Semantics.
- http://alps.io/spec/
1. **W3C.** PROV-O: The PROV Ontology.
- https://www.w3.org/TR/prov-o/
1. **W3C.** SHACL: Shapes Constraint Language.
- https://www.w3.org/TR/shacl/

-----

## 14. Appendices

### Appendix A: Glossary

|Term             |Definition                                                  |
|-----------------|------------------------------------------------------------|
|**Configuration**|Maximal orthogonal set of active states                     |
|**Macrostep**    |Processing one external event to quiescence                 |
|**Microstep**    |One transition firing within a macrostep                    |
|**Broadcast**    |Events generated by actions, processed in same macrostep    |
|**History**      |Remembering and restoring sub-configurations                |
|**LCA**          |Least Common Ancestor of a set of states                    |
|**Orthogonal**   |States that can be simultaneously active (AND-decomposition)|
|**Tagless-final**|Encoding programs as polymorphic functions over algebras    |
|**Reflect**      |Convert AST to polymorphic program                          |
|**Reify**        |Convert polymorphic program to AST                          |
|**Projection**   |Filtering a binding for a specific role                     |

### Appendix B: Decision Log

|Date   |Decision                       |Rationale                                    |
|-------|-------------------------------|---------------------------------------------|
|2026-04|Separate types per concern     |Orthogonal concerns, independent utility     |
|2026-04|Tagless-final for structure    |Multiple interpretations, extensibility      |
|2026-04|MailboxProcessor for runtime   |Harel semantics require serialized processing|
|2026-04|AST + TF bridge                |Parsing needs AST, interpretation needs TF   |
|2026-04|Composition model              |Unified view without unified type            |
|2026-04|Projection is filtering        |Keeps algebras simple, filters compose       |
|2026-04|Independent statecharts package|Quorum reuse, testability                    |

### Appendix C: Open Questions

1. **Event typing**: Should events be stringly-typed or use discriminated unions?
- Current: `EventId of string`
- Alternative: Generated DUs from SCXML
1. **Guard evaluation**: How to safely evaluate guard expressions?
- Current: String-based
- Alternative: F# quotations, compiled expressions
1. **Timeout events**: SCXML supports `<send delay="5s">`. Implementation strategy?
- Option: Timer-based event injection
- Option: Special timeout transition type
1. **Invoke/spawn**: SCXML supports invoking child state machines. Scope?
- Current: Out of scope for v1
- Future: Actor hierarchy
1. **Testing infrastructure**: Property-based testing for Harel semantics?
- FsCheck generators for valid statecharts
- Shrinking strategies

-----

## Document History

|Version|Date      |Author    |Changes        |
|-------|----------|----------|---------------|
|1.0    |2026-04-16|Ryan Riley|Initial version|

-----

*This document should be updated as architectural decisions evolve. Major changes require team review.*