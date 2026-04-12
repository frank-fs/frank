---
source: kitty-specs/003-statecharts-feasibility-research
status: complete
type: spec
---

# Research Specification: Isomorphic Statechart-to-Code Feasibility for Frank

**Feature Branch**: `003-statecharts-feasibility-research`
**Created**: 2026-03-06
**Status**: Complete
**Research Type**: Case Study | Empirical Study

## Research Question & Scope

**Primary Research Question**: Can Frank support isomorphic round-tripping between statechart specifications (WSD, ALPS, XState, SCXML) and runtime code, such that a `statefulResource` computation expression auto-generates allowed HTTP methods/responses per state -- and the runtime resource definition can be projected back to equivalent specifications?

**Sub-Questions**:
1. **Feasibility**: Is spec-to-code-to-spec round-tripping achievable within an ASP.NET Core web framework, or do fundamental impedances prevent it?
2. **Spec Selection**: Which specification formats are necessary -- a single canonical format, a fixed combination, or any subset? What is the minimal set that preserves full statechart semantics (states, transitions, guards, affordances)?
3. **CLI Requirements**: What additional `frank-cli` commands (if any) are needed to support extraction from compiled Frank assemblies back to specification formats?
4. **API Design**: What is the right abstraction for encoding state machines into Frank resources -- specifically, how should `statefulResource` CE, `StateMachine<'State, 'Event, 'Context>`, guards, and `IStateMachineStore` work together?
5. **Minimum Viable Scope**: What is the smallest statechart runtime that unblocks #76 (Validation) and #77 (Provenance) in the v7.3.0 milestone?
6. **Complexity Ceiling**: What level of statechart complexity is reasonable to achieve? Assess feasibility for simple (tic-tac-toe), moderate (onboarding), and complex (e-commerce API) state machines.

**Scope**:
- **In Scope**:
  - Analysis of the tic-tac-toe prior art (informal pattern) and what formalization requires
  - Evaluation of WSD, ALPS, XState JSON, and SCXML as candidate specification formats
  - Feasibility of round-trip projection: spec -> F# types -> runtime -> spec
  - Design of `statefulResource` CE that auto-generates allowed methods/responses per state
  - Guard model mapping to HTTP status codes and filtered affordances
  - `IStateMachineStore` abstraction with `MailboxProcessor` as default implementation
  - Transition event hooks for Provenance observability
  - Identification of required `frank-cli` commands for reverse projection
  - Project plan for implementation
- **Out of Scope**:
  - Orleans virtual actors or other distributed state backends (future direction only)
  - Spec Kit validation / tic-tac-toe regeneration (separate validation exercise)
  - Actual implementation of the library (follows as software-dev feature)
  - Performance benchmarking beyond MailboxProcessor baseline characteristics

**Expected Outcomes**:
- A feasibility determination (go/no-go) with evidence for isomorphic round-tripping
- If no-go: documentation of what *can* be achieved as a viable alternative
- Recommended specification format(s) with rationale
- Proposed API surface for `Frank.Statecharts` (types, CE operations, guard signatures)
- Identification of impedance mismatches between spec formats and Frank's resource model
- Inventory of required `frank-cli` additions
- Implementation project plan (phased, with dependency ordering)
- Complexity ceiling assessment with feasibility notes for real-world APIs

## Clarifications

### Session 2026-03-06

- Q: What constitutes "semantic loss" for round-tripping feasibility? -> A: Lossy-but-documented is acceptable. Code-to-spec generation must be comprehensive (emit all formats: WSD, ALPS, XState, SCXML, smcat). Spec-to-code generation is best-effort -- use what the format can express, document gaps. No behavioral information may be lost in the runtime code itself.
- Q: Should the research validate against a second case study beyond tic-tac-toe? -> A: Yes. Use Amundsen's onboarding example (already sketched in #57) as the second case study, plus a simple existing Frank resource as an overly simple baseline. Note FoxyCart API (api.foxycart.com/docs) and Stripe payment lifecycle as future validation candidates. Include a feasibility assessment of statechart complexity limits.
- Q: What constitutes a "no-go" result? -> A: No-go if the `statefulResource` CE can't express tic-tac-toe's full behavior (guards, filtered affordances, per-user discrimination). However, even in a no-go scenario, research must document what can be achieved as a viable alternative.
- Q: Should code-to-spec generation be build-time, runtime, or both? -> A: Both. Build-time via frank-cli (MSBuild task, alongside existing OWL/SHACL generation from #79). Runtime via middleware/endpoints in Frank.Statecharts (like OpenAPI serves /openapi/v1.json). Recommendation: integrate into frank-cli rather than a separate CLI tool, since frank-cli already has assembly analysis, type extraction, and MSBuild integration infrastructure.

## Research Methodology Outline

### Research Approach
- **Method**: Case Study (tic-tac-toe decomposition + onboarding example) + Empirical Study (prototype round-trip mappings)
- **Data Sources**:
  - `../tic-tac-toe` codebase -- working informal implementation (primary case study)
  - Amundsen's onboarding example from #57 analysis -- second case study
  - An existing simple Frank sample resource -- baseline case study
  - Issue #57 analysis -- proposed architecture and mapping rules
  - Frank core (`src/Frank/Builder.fs`) -- ResourceBuilder/ResourceSpec model
  - Frank.LinkedData -- existing semantic metadata projection patterns
  - ALPS specification (alps.io), XState documentation, SCXML W3C spec, WSD format reference
  - Mike Amundsen's WSD-to-API methodology (RESTFest 2018)
  - `wsd-gen` F# fork (github.com/panesofglass/wsd-gen/tree/fsharp) -- existing parser work
- **Analysis Approach**:
  - Map tic-tac-toe's informal state machine to each candidate spec format
  - Map onboarding example to each candidate spec format
  - Identify round-trip information loss at each transformation boundary
  - Prototype the critical mapping: `statefulResource` CE -> endpoint metadata -> spec extraction
  - Evaluate guard expressiveness across formats
  - Assess complexity ceiling using FoxyCart API and Stripe payment lifecycle as reference points

### Feasibility Threshold

- **Go**: `statefulResource` CE can express tic-tac-toe's full behavior (guards, filtered affordances, per-user discrimination) and code-to-spec generation produces valid WSD, ALPS, XState, SCXML, and smcat output
- **No-go**: `statefulResource` CE cannot express tic-tac-toe's full behavior -- but research must document what can be achieved as a viable alternative
- **Round-trip fidelity**: Code-to-spec is comprehensive (all formats). Spec-to-code is best-effort with documented gaps. No behavioral information may be lost in the runtime code itself.

### Generation Architecture

- **Build-time**: frank-cli generates spec artifacts (WSD, ALPS, XState, SCXML, smcat) from Frank source definitions, extending existing OWL/SHACL generation infrastructure from #79. Prefer integrating into frank-cli over a separate CLI tool.
- **Runtime**: Frank.Statecharts middleware/endpoints serve live spec representations from the running application (analogous to OpenAPI endpoint serving).

### Future Validation Candidates

The following APIs should be noted as candidates for future complexity validation beyond this research scope. The research should include a brief feasibility assessment of the statechart complexity these represent:

- **FoxyCart API** (api.foxycart.com/docs): Complex e-commerce workflow with multi-entity state coordination
- **Stripe payment lifecycle**: `pending -> processing -> succeeded/failed -> refunded/disputed` with guards (amount limits, fraud checks) and external triggers

### Success Criteria
- SC-001: Produce concrete mappings between at least 3 spec formats (WSD, ALPS, XState) and Frank's resource model, documenting information preserved and lost at each boundary
- SC-002: Demonstrate (on paper or prototype) that tic-tac-toe's state machine can be expressed as a `statefulResource` CE and projected back to at least one spec format without behavioral loss
- SC-003: Define the complete public API surface for `Frank.Statecharts` with type signatures
- SC-004: Identify all `frank-cli` commands needed for reverse projection (code -> spec), including smcat and SCXML output
- SC-005: Produce a phased implementation plan with clear dependency ordering and milestone alignment
- SC-006: Include a complexity ceiling assessment noting feasibility for simple, moderate, and complex statecharts with reference to FoxyCart and Stripe as benchmarks

## Research Requirements

### Data Collection Requirements
- **DR-001**: Research MUST analyze the complete tic-tac-toe state machine (states, transitions, guards, per-user discrimination) as the primary case study
- **DR-002**: Research MUST map Amundsen's onboarding example to candidate spec formats as a second case study
- **DR-003**: Research MUST evaluate WSD, ALPS, XState JSON, SCXML, and smcat as candidate output formats, documenting each format's strengths and gaps relative to Frank's needs
- **DR-004**: Research MUST examine Frank's existing extension patterns (LinkedData, Auth, OpenApi, Datastar) to ensure the proposed `statefulResource` CE follows established conventions
- **DR-005**: All sources MUST be documented in `research/source-register.csv`

### Analysis Requirements
- **AR-001**: Findings MUST include a transformation matrix showing what information each spec format captures vs. what Frank's runtime needs
- **AR-002**: The proposed API surface MUST be concrete enough to write against (type signatures, CE operations, usage examples)
- **AR-003**: Impedance mismatches MUST be explicitly catalogued with proposed mitigations
- **AR-004**: The relationship between `frank-cli`'s existing OWL/SHACL generation (#79) and statechart spec generation MUST be analyzed
- **AR-005**: Research MUST assess the complexity ceiling for statechart support, using FoxyCart API and Stripe payment lifecycle as reference benchmarks

### Quality Requirements
- **QR-001**: All feasibility claims MUST be supported by concrete mappings or prototype evidence, not theoretical arguments alone
- **QR-002**: Confidence levels MUST be assigned to findings in `research/evidence-log.csv`
- **QR-003**: Alternative API designs MUST be considered and compared (at minimum: deep CE integration vs. library-level composition)

## Key Concepts & Terminology

- **Isomorphic round-tripping**: The ability to transform a statechart specification into runtime code and back to an equivalent specification without behavioral loss. Code-to-spec is comprehensive; spec-to-code is best-effort with documented gaps.
- **Filtered affordances**: The REST/Amundsen approach of omitting unavailable transitions from representations rather than returning error codes for blocked states
- **Guard**: A predicate evaluated at transition time that determines whether a state transition is allowed, potentially incorporating user identity (`ClaimsPrincipal`), current state, and the requested event
- **statefulResource CE**: Proposed computation expression that wraps Frank's existing `resource` CE, adding state-aware handler generation -- different HTTP methods/responses become available depending on current resource state
- **IStateMachineStore**: Abstraction for state persistence, with `MailboxProcessor` as the default (in-memory, single-node) implementation
- **WSD (Web Sequence Diagram)**: Models workflow topology -- states, transitions, ordering, parameters. Source-of-truth in the proposed pipeline
- **ALPS (Application-Level Profile Semantics)**: Models vocabulary -- semantic descriptors, transition types (safe/unsafe/idempotent), return types. Intentionally omits workflow ordering
- **XState JSON**: Models executable state machines -- states, transitions, guards, actions, context. Runtime validation and visual editing via Stately Studio
- **SCXML (State Chart XML)**: W3C standard for state machine notation. Mature but XML-heavy
- **smcat (state machine cat)**: Lightweight text-based state machine notation with visualization support

## Evidence Tracking Guidance

- Log every reviewed source in `research/source-register.csv` with citation, URL, relevance, and status.
- Capture each key finding in `research/evidence-log.csv`, including confidence level and notes.
- Reference evidence row IDs within this specification when making claims.


## Research

# Research: Isomorphic Statechart-to-Code Feasibility for Frank

**Feature**: 003-statecharts-feasibility-research
**Date**: 2026-03-06
**Status**: Complete

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
**Rationale**: frank-cli already has assembly analysis, type extraction, MSBuild integration, and semantic artifact generation. A separate tool would duplicate this infrastructure. Note: the `wsd-gen` F# fork is NOT a WSD parser (see D-009) -- a parser must be written from scratch.
**Evidence**: [E-006], [E-015]

### D-006: ALPS Limitations Acknowledged

**Decision**: ALPS is useful for semantic vocabulary but has significant limitations as a statechart format.
**Rationale**: Research confirmed ALPS is an expired IETF draft (never became RFC). Key limitations:
- Cannot distinguish PUT from DELETE (both `type="idempotent"`)
- `rt` (return type) is single-valued -- cannot express conditional return states (e.g., XTurn -> OTurn OR Won OR Draw)
- No concept of initial state
- No native guards/preconditions (by design, per FAQ A.2)
- No normative HTTP method mapping document (convention only)
ALPS remains valuable for semantic descriptor vocabulary (what states and transitions *mean*) but cannot serve as a standalone statechart format.
**Evidence**: [E-009]

### D-007: XState v5 SCXML Removal Impact

**Decision**: XState JSON and SCXML are treated as independent output formats, not interconvertible.
**Rationale**: XState v5 removed the `@xstate/scxml` import/export package. The formats are no longer interoperable through XState tooling. However:
- XState v5 has a formal JSON schema (`machine.schema.json`, JSON Schema draft-07)
- SCXML is a W3C Recommendation with full statechart semantics
- smcat can read/write SCXML (core constructs) and has its own AST JSON schema
- frank-cli will generate each format independently from the F# type model
**Evidence**: [E-010], [E-011]

### D-008: smcat as Human-Friendly Notation

**Decision**: smcat is the recommended human-authoring format for spec-to-code direction.
**Rationale**: smcat v14.0.6 has excellent human-readable notation, supports guards (`[condition]`), hierarchical states, parallel states, and has a formal AST JSON schema. It can round-trip through SCXML for core constructs. JavaScript-only limitation means frank-cli would either shell out to smcat CLI or implement a custom F# parser (smcat grammar is PEG-based, feasible to port). Transition labels follow `event [guard] / action` format, parsed by regex into separate `event`, `cond`, and `action` AST fields. Guards are opaque strings -- smcat preserves guard *names* but cannot express `BlockReason` semantics. The smcat AST supports 12 state types (initial, final, parallel, history, choice, fork, join, junction, etc.).
**Evidence**: [E-012], [E-019]

### D-009: wsd-gen F# Fork Is Not a Parser

**Decision**: The existing `wsd-gen` F# fork provides no foundation for local WSD parsing.
**Rationale**: Review of the `fsharp` branch reveals it is a thin HTTP client that posts raw WSD text to the `websequencediagrams.com` API and downloads rendered images. There is no local lexer, parser, AST, or data model. Targets `netstandard2.0`/`netcoreapp2.1` (outdated). Only dependency is `Newtonsoft.Json` for API response parsing. A WSD parser for #57 must be written from scratch.
**Impact**: The #57 effort estimate for "WSD Parser -- 1-2 weeks" is likely optimistic. This does not affect #87 (core runtime library).
**Evidence**: [E-015]

### D-010: XState Guard Model Validates DD-03

**Decision**: XState v5's guard evaluation semantics confirm the Frank.Statecharts guard design.
**Rationale**: XState v5 evaluates multiple guarded transitions in registration order -- "the first transition whose guard evaluates to true will be taken." This matches DD-03 (registration order, first `Blocked` short-circuits). XState's named guards via `setup()` parallel our `Guard.Name` pattern. XState also offers guard combinators (`and`/`or`/`not`) which are a potential future enhancement but not needed for v7.3.0 MVP.
**Evidence**: [E-016]

### D-011: SCXML Guard Semantics Diverge Intentionally

**Decision**: Frank's `BlockReason` model is intentionally richer than SCXML's `cond` attribute.
**Rationale**: SCXML `cond` is a boolean expression that silently evaluates to `false` on error. Frank's `GuardResult.Blocked(reason)` carries HTTP-mappable information (`NotAllowed`→403, `NotYourTurn`→409, etc.) that SCXML cannot express. This means code→SCXML export is lossy for guard *reasons* (only guard *names* survive as `cond` attribute values). This is acceptable per D-001 (lossy-but-documented).
**Evidence**: [E-018]

## Case Study Analysis

### Prior Art: F# Advent 2018 Blog Post

**Source**: [State Transitions through Sequence Diagrams](https://wizardsofsmart.wordpress.com/2018/12/05/state-transitions-through-sequence-diagrams/)

An earlier exploration of the WSD-to-state-machine concept. Defined `Transition<'State,'Message>` and an `Agent<'State,'Message>` type backed by `MailboxProcessor` with:
- `Agent.Get`: returns `(currentState, allowedTransitions)` -- only transitions valid from the current state
- `Agent.Post`: validates the message against the current state's allowed transitions before accepting
- String-typed states and messages (not DUs)
- No guards, no HTTP integration, no per-user discrimination

The `Agent.Get` returning filtered transitions is an illustrative example of the "filtered affordances" concept that Frank.Statecharts formalizes with typed DUs, named guards, and HTTP method mapping. The `Agent.Post` validation pattern is analogous to the middleware's method filtering (405) behavior.

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

### Key Finding: Format-Specific Limitations

- **ALPS**: Cannot express conditional return types (`rt` is single-valued). A transition like `makeMove` that can lead to OTurn, Won, or Draw requires three separate ALPS descriptors (one per target state) rather than one transition with conditional targets. Cannot distinguish PUT from DELETE.
- **XState v5**: SCXML import/export removed. JSON schema exists but describes compiled/internal form, not user-facing shorthand. Guard implementations are not serializable in JSON (only guard names/types). Guard evaluation order (first match wins) matches DD-03. Guard combinators (`and`/`or`/`not`) are a potential future enhancement.
- **SCXML**: No HTTP domain model. `cond` attribute treats errors as `false` (silent failure), unlike Frank's explicit `BlockReason`. Full statechart semantics (parallel, history, invoke, datamodel). Actions execute in strict order: onexit → transition content → onentry. No quality .NET libraries -- frank-cli must generate SCXML directly via XML APIs.
- **smcat**: JavaScript-only parser. No .NET equivalent. Guards are opaque strings (parsed but not interpreted via `event [guard] / action` label format). No data model or execution semantics. Supports 12 state types. SCXML round-trip is lossy: no datamodel, no executable content, no invoke.

### Key Finding: smcat as Bridge Format

smcat can read/write SCXML (core constructs: states, transitions, hierarchy, parallel). This means:
- `F# code -> frank-cli -> smcat` for human-readable output
- `smcat -> SCXML` via smcat tooling for W3C standard interchange
- `smcat AST JSON` as a programmatic interchange format with formal schema

This three-way bridge (smcat <-> SCXML <-> XState JSON via shared semantics) provides practical interoperability even though XState v5 dropped direct SCXML support.

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

### Complex (e.g., Multi-Entity E-Commerce)

**Feasibility**: Medium (60%)
- Multi-entity state coordination (cart, order, shipment, payment -- each with own state machine)
- Hierarchical states (order contains sub-states for fulfillment)
- Parallel states (payment processing concurrent with inventory reservation)
- Round-trip partially achievable; hierarchical/parallel states supported by XState and SCXML but not WSD or smcat
- Would require composing multiple `statefulResource` instances with cross-resource transition coordination
- Note: FoxyCart API was evaluated as a potential case study but its documentation does not expose state machine semantics explicitly enough for detailed analysis. A Stripe-like payment lifecycle would be a better hypothetical example for this tier.

### Assessment

The `statefulResource` CE should target simple-to-moderate complexity as the primary use case. Complex multi-entity coordination can be built by composing multiple state machines, but this is an advanced pattern that may not need first-class CE support in v7.3.0.

## Open Questions

1. **Parallel state composition**: How should multiple `statefulResource` instances coordinate? (e.g., multi-entity e-commerce). Defer to post-v7.3.0?
2. **History states**: XState and SCXML support history states (return to previous sub-state). Is this needed for Frank's use cases?
3. **Timeout transitions**: Some state machines have time-based transitions (e.g., session expiry). Should `statefulResource` support timer-based events, or is this left to external scheduling?
4. ~~**Existing `wsd-gen` fork status**~~: **Resolved (D-009)** -- the F# fork is not a parser. A WSD parser must be written from scratch for #57.
5. **smcat parser portability**: Should frank-cli shell out to the Node.js smcat CLI for smcat parsing, or should we port the PEG grammar to F#? Shelling out adds a Node.js dependency; porting is more work but keeps the toolchain pure .NET.
6. **ALPS conditional return types**: The `rt` single-value limitation means each conditional transition becomes multiple ALPS descriptors. Is this acceptable, or should we define an ALPS `ext` convention for multi-target transitions?
7. **XState JSON schema version**: The existing schema describes the internal/compiled form, not user-facing config. Should frank-cli generate the compiled form (for Stately Studio compatibility) or the shorthand form (for human readability)?
8. **Guard combinators**: XState v5 provides `and`/`or`/`not` guard combinators. Should Frank.Statecharts support these in a future version? (Not needed for v7.3.0 MVP.)

## References

See `research/source-register.csv` for full citations.
