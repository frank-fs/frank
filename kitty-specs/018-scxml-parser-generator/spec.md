# Feature Specification: SCXML Parser and Generator

**Feature Branch**: `018-scxml-parser-generator`
**Created**: 2026-03-15
**Status**: Draft
**GitHub Issue**: #98
**Dependencies**: #87 (core runtime -- `StateMachineMetadata` types, complete), shared AST spec (forthcoming -- defines the unified AST all format parsers populate)
**Parallel with**: #97 (ALPS), #100 (smcat), #90 (WSD Parser)
**Location**: Internal to `src/Frank.Statecharts/` (Scxml parser/generator namespace), tests in `test/Frank.Statecharts.Tests/`
**Input**: GitHub issue #98 -- SCXML Parser + Generator: bidirectional W3C SCXML support

## Clarifications

### Session 2026-03-15

- Q: Should the SCXML parser produce its own per-format AST or use a shared AST? -> A: All format parsers (WSD, ALPS, SCXML, smcat) share a single AST. The shared AST is defined in a separate prerequisite spec. Each format parser populates the parts of the shared AST it can represent. The WSD parser's existing per-format AST will be migrated to the shared AST in that prerequisite spec.
- Q: Should `<data>` elements be parsed as simple name/expression pairs or richly typed? -> A: Simple name/expression pairs. Semantic interpretation is left to downstream consumers (LLM or cross-validator).
- Q: Should `<invoke>` and `<history>` be opaque annotations or structured AST nodes? -> A: Structured but non-functional AST nodes -- parsed into typed fields but not mapped to any Frank runtime concept.

## Background

SCXML (State Chart XML) is a W3C Recommendation that provides a well-defined XML format for describing state machines. It models states, transitions, data model, invoke, and history -- providing richer structured context than simpler text-based formats.

This feature builds a bidirectional SCXML capability for the Frank.Statecharts spec pipeline (#57): a parser that reads W3C SCXML documents and populates the shared AST, and a generator that produces W3C SCXML XML from the shared AST. Together with the WSD, ALPS, and smcat parsers/generators, SCXML enables a design-first development workflow where developers can start from any supported format, use LLM-assisted tooling to scaffold F# code, and then extract specifications from the running application for verification.

The shared AST is defined in a separate prerequisite spec. This SCXML spec depends on that shared AST and populates the portions of it that SCXML can represent: states (simple, compound, parallel, final), transitions with events and guard conditions, data model variables, history pseudo-states, and invocation annotations.

## User Scenarios & Testing

### User Story 1 - Parse an SCXML Document into the Shared AST (Priority: P1)

A developer passes an SCXML XML string or file to the parser and receives a populated shared AST representing the state machine: states, transitions, events, guards, initial state, and final states. The AST can then be used as structured context for LLM-assisted F# code generation.

**Why this priority**: This is the core value proposition -- without correct parsing, the SCXML input path of the spec pipeline does not function. SCXML is a W3C standard with a well-defined schema, making it one of the most reliable formats to parse.

**Independent Test**: Parse a canonical SCXML document (e.g., a tic-tac-toe game or traffic light state machine) end-to-end, walk the resulting shared AST, and verify every state, transition, event, and guard maps to the expected shared AST nodes with correct field values.

**Acceptance Scenarios**:

1. **Given** an SCXML document with `<scxml initial="idle">` containing `<state id="idle">` and `<state id="active">` elements, **When** parsed, **Then** the shared AST contains state nodes for "idle" and "active", with "idle" marked as the initial state.
2. **Given** an SCXML document with `<transition event="start" target="active"/>` inside a state, **When** parsed, **Then** the shared AST contains a transition edge from the containing state to "active" triggered by event "start".
3. **Given** an SCXML document with `<transition event="submit" cond="isValid" target="submitted"/>`, **When** parsed, **Then** the shared AST contains a transition with event "submit", guard name "isValid", and target "submitted".
4. **Given** an SCXML document with `<final id="done"/>`, **When** parsed, **Then** the shared AST contains a terminal state node for "done".
5. **Given** an SCXML document with nested `<state>` elements (compound states), **When** parsed, **Then** the shared AST represents the parent-child state hierarchy correctly.

---

### User Story 2 - Generate SCXML XML from the Shared AST (Priority: P1)

A developer has a populated shared AST (from any format parser or from a compiled Frank.Statecharts assembly via `StateMachineMetadata`) and generates a valid W3C SCXML XML document from it. The generated document can be consumed by SCXML-compatible tools or used for design verification.

**Why this priority**: The generator completes the bidirectional capability. Without it, SCXML cannot serve as an output format for the verification loop (extract spec from running app, compare against original design).

**Independent Test**: Populate a shared AST programmatically with known states, transitions, and guards, generate SCXML XML, and verify the output is valid XML containing the expected `<scxml>`, `<state>`, `<transition>`, and `<final>` elements with correct attributes.

**Acceptance Scenarios**:

1. **Given** a shared AST with states "idle", "active", and "done" where "idle" is initial and "done" is final, **When** SCXML is generated, **Then** the output contains `<scxml initial="idle">` with `<state id="idle">`, `<state id="active">`, and `<final id="done">`.
2. **Given** a shared AST with a guarded transition from "active" to "submitted" on event "submit" with guard "isValid", **When** SCXML is generated, **Then** the output contains `<transition event="submit" cond="isValid" target="submitted"/>` inside the "active" state.
3. **Given** a shared AST with hierarchical (compound) states, **When** SCXML is generated, **Then** the output contains nested `<state>` elements preserving the hierarchy.
4. **Given** a shared AST with data model entries, **When** SCXML is generated, **Then** the output contains a `<datamodel>` element with `<data>` children.

---

### User Story 3 - Parse SCXML Data Model Elements (Priority: P2)

A developer's SCXML document includes `<datamodel>` with `<data id="..." expr="...">` elements describing context variables. The parser extracts these as name/expression pairs in the shared AST, providing context schema information for LLM-assisted code generation.

**Why this priority**: Data model elements provide valuable context for code generation (what variables the state machine operates on), but the mapping is best-effort since `expr` attributes use ECMAScript expressions that require interpretation.

**Independent Test**: Parse an SCXML document with a `<datamodel>` containing multiple `<data>` elements, verify each produces a name/expression pair in the shared AST.

**Acceptance Scenarios**:

1. **Given** `<datamodel><data id="count" expr="0"/><data id="name" expr="'default'"/></datamodel>`, **When** parsed, **Then** the shared AST contains data entries `("count", "0")` and `("name", "'default'")`.
2. **Given** `<data id="items"/>` with no `expr` attribute, **When** parsed, **Then** the data entry has the name "items" and an empty/absent expression value.
3. **Given** a `<datamodel>` nested inside a `<state>` rather than at the top level, **When** parsed, **Then** the data entries are associated with that state in the shared AST.

---

### User Story 4 - Parse SCXML Parallel, History, and Invoke Elements (Priority: P3)

A developer's SCXML document includes `<parallel>`, `<history>`, and `<invoke>` elements. The parser captures these as structured AST nodes. `<parallel>` maps to a compound state concept; `<history>` and `<invoke>` are non-functional annotations preserved for LLM context and roundtripping.

**Why this priority**: These elements have no direct Frank.Statecharts runtime equivalent, but preserving them as structured data supports richer LLM prompts and faithful roundtripping.

**Independent Test**: Parse an SCXML document containing `<parallel>`, `<history>`, and `<invoke>` elements, verify each produces a correctly typed node in the shared AST with all attributes preserved.

**Acceptance Scenarios**:

1. **Given** `<parallel id="p1">` containing two child `<state>` elements, **When** parsed, **Then** the shared AST contains a parallel state node "p1" with two child state nodes.
2. **Given** `<history id="h1" type="deep"/>` inside a state, **When** parsed, **Then** the shared AST contains a history node with id "h1" and type "deep".
3. **Given** `<history id="h2"/>` with no type attribute, **When** parsed, **Then** the history node defaults to type "shallow".
4. **Given** `<invoke type="http" src="https://example.com/service"/>` inside a state, **When** parsed, **Then** the shared AST contains an invoke node with type "http" and src attribute preserved.

---

### User Story 5 - Roundtrip Consistency (Priority: P2)

A developer parses an SCXML document into the shared AST, then generates SCXML back from that AST. The generated document is structurally equivalent to the original: same states, same transitions, same guards, same initial/final markers. Surface differences (attribute ordering, whitespace, comments) are acceptable.

**Why this priority**: Roundtrip consistency validates that the parser and generator are complementary and that no information is lost in the shared AST representation.

**Independent Test**: Parse a reference SCXML document, generate SCXML from the resulting AST, parse the generated SCXML again, and compare the two ASTs for structural equality.

**Acceptance Scenarios**:

1. **Given** a reference SCXML document with states, transitions, guards, and data model, **When** parsed then generated then parsed again, **Then** the two shared ASTs are structurally equal (same states, transitions, events, guards, data entries).
2. **Given** a minimal SCXML document with only a single state and no transitions, **When** roundtripped, **Then** the output is structurally equivalent.
3. **Given** an SCXML document with `<parallel>`, `<history>`, and `<invoke>` elements, **When** roundtripped, **Then** these elements are preserved in the generated output.

---

### Edge Cases

- Empty `<scxml>` document with no child states produces a valid but empty AST (not a parse failure)
- `<scxml>` with no `initial` attribute: parser infers the first child state as initial (per W3C spec section 3.2)
- Self-transitions: `<transition event="retry" target="same-state"/>` where target equals the containing state
- Eventless transitions (automatic/completion transitions): `<transition target="next"/>` with no `event` attribute
- Conditional transitions with no target (internal transitions): `<transition event="check" cond="isReady"/>`
- Multiple transitions from the same state with the same event but different guards (transition ordering matters per W3C spec)
- `<state>` with both `id` and child `<state>` elements (compound state)
- Deeply nested state hierarchies (5+ levels) parse correctly
- SCXML namespace handling: documents may use the default namespace `http://www.w3.org/2005/07/scxml` or a prefixed namespace
- Unicode characters in state IDs, event names, and guard conditions
- Malformed XML input produces a structured error (not an unhandled exception)
- SCXML documents with XML comments interspersed between elements
- `<transition>` with multiple space-separated targets (per W3C spec, targets can be a space-separated list)
- `<data>` elements with child content instead of `expr` attribute (alternative W3C-allowed form)
- Large SCXML documents (100+ states) parse without performance degradation

## Requirements

### Functional Requirements

- **FR-001**: System MUST parse SCXML XML documents using `System.Xml.Linq`, accepting input as a string, `TextReader`, or `Stream`
- **FR-002**: System MUST map `<scxml initial="...">` to the shared AST's initial state marker
- **FR-003**: System MUST map `<state id="...">` elements to state nodes in the shared AST, preserving the state ID
- **FR-004**: System MUST map `<final id="...">` elements to terminal state nodes in the shared AST
- **FR-005**: System MUST map `<parallel id="...">` elements to parallel/compound state nodes in the shared AST, preserving child state structure
- **FR-006**: System MUST map `<transition event="..." cond="..." target="...">` elements to transition edges in the shared AST, preserving event name, guard condition name, and target state reference
- **FR-007**: System MUST handle transitions with multiple space-separated targets (per W3C SCXML section 3.5) by creating one transition edge per target
- **FR-008**: System MUST map `<datamodel>` / `<data id="..." expr="...">` elements to name/expression pairs in the shared AST
- **FR-009**: System MUST handle `<data>` elements with child content (alternative to `expr` attribute) by capturing the text content as the expression value
- **FR-010**: System MUST map `<history id="..." type="...">` elements to structured history nodes in the shared AST, defaulting type to "shallow" when the attribute is absent
- **FR-011**: System MUST map `<invoke type="..." src="...">` elements to structured invoke nodes in the shared AST, preserving type, src, and id attributes
- **FR-012**: System MUST handle compound states (states containing child states) by representing the parent-child hierarchy in the shared AST
- **FR-013**: System MUST infer the initial state from the first child `<state>` element when the `initial` attribute is absent on `<scxml>` (per W3C spec section 3.2)
- **FR-014**: System MUST handle the SCXML namespace (`http://www.w3.org/2005/07/scxml`) in both default and prefixed forms
- **FR-015**: System MUST produce structured error results for malformed XML input, including a description of the error and the position (if available from the XML parser)
- **FR-016**: System MUST generate valid W3C SCXML XML from the shared AST using `System.Xml.Linq`, producing well-formed XML with the SCXML namespace
- **FR-017**: System MUST generate `<scxml initial="...">` with the initial state from the shared AST
- **FR-018**: System MUST generate `<state>`, `<final>`, and `<parallel>` elements from the corresponding shared AST nodes
- **FR-019**: System MUST generate `<transition>` elements with `event`, `cond`, and `target` attributes from shared AST transition edges
- **FR-020**: System MUST generate `<datamodel>` and `<data>` elements from shared AST data entries
- **FR-021**: System MUST generate `<history>` and `<invoke>` elements from shared AST annotation nodes, preserving all attributes
- **FR-022**: System MUST generate nested `<state>` elements for compound state hierarchies
- **FR-023**: System MUST produce SCXML output with the W3C SCXML namespace declaration (`xmlns="http://www.w3.org/2005/07/scxml"`) and version attribute (`version="1.0"`)
- **FR-024**: System MUST compile under multi-target (net8.0/net9.0/net10.0) with `dotnet build`

### Key Entities

- **ScxmlState**: A state node parsed from `<state>`, `<final>`, or `<parallel>` -- maps to the shared AST's state representation. Attributes: id (required), type (simple/compound/parallel/final), child states (for compound/parallel), transitions, data model entries, history nodes, invoke nodes
- **ScxmlTransition**: A transition edge parsed from `<transition>` -- maps to the shared AST's transition representation. Attributes: event (optional), cond/guard (optional), target (optional, may be multi-valued), type (internal/external)
- **ScxmlDataEntry**: A data variable parsed from `<data>` -- a name/expression pair. Attributes: id (name), expr or child content (expression value, optional)
- **ScxmlHistory**: A history pseudo-state parsed from `<history>` -- structured but non-functional. Attributes: id, type (shallow/deep, default shallow)
- **ScxmlInvoke**: An invocation annotation parsed from `<invoke>` -- structured but non-functional. Attributes: type, src, id
- **ParseError**: Structured error from malformed input -- description, position (if available)

## Assumptions

- The shared AST prerequisite spec will provide state, transition, guard, data entry, and annotation node types sufficient to represent all SCXML elements described here. If the shared AST does not yet include types for history or invoke annotations, this spec will propose additions to the shared AST as part of implementation.
- `System.Xml.Linq` is available in all target frameworks (net8.0/net9.0/net10.0) without additional NuGet dependencies.
- The SCXML parser does not execute the state machine -- it only parses the declarative structure. Executable content (`<script>`, `<assign>`, `<send>`, etc.) is outside scope.
- The generator produces canonical/normalized SCXML output -- it does not attempt to reproduce the exact formatting of a previously parsed document.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Parser correctly handles a canonical SCXML document (traffic light or turn-based game) end-to-end, producing a shared AST where every state, transition, event, and guard can be verified against the original document
- **SC-002**: Generator produces valid W3C SCXML XML that can be re-parsed by the same parser without errors
- **SC-003**: Roundtrip test (parse -> generate -> parse) produces structurally equal shared ASTs for all reference SCXML documents
- **SC-004**: All nine SCXML element types in scope (`<scxml>`, `<state>`, `<final>`, `<parallel>`, `<transition>`, `<datamodel>`, `<data>`, `<history>`, `<invoke>`) are correctly parsed and generated
- **SC-005**: Malformed XML input produces structured error results (not unhandled exceptions) with descriptive messages
- **SC-006**: Library compiles under multi-target (net8.0/net9.0/net10.0) with `dotnet build`
- **SC-007**: Parser handles SCXML documents with 100+ states without measurable performance degradation
