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
