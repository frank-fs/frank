# Feature Specification: Shared Statechart AST

**Feature Branch**: `020-shared-statechart-ast`
**Created**: 2026-03-15
**Status**: Draft
**Dependencies**: #87 (core runtime -- `StateMachineMetadata` types, complete). This spec is a prerequisite for #97 (ALPS), #98 (SCXML), #100 (smcat), #91 (WSD Generator), and a forthcoming cross-format validator spec.
**Location**: `src/Frank.Statecharts/Ast/` (shared AST types), with migration of `src/Frank.Statecharts/Wsd/Types.fs` and `src/Frank.Statecharts/Wsd/Parser.fs`. Tests in `test/Frank.Statecharts.Tests/`.
**Input**: Cross-cutting architectural requirement -- unified AST for all statechart format parsers (WSD, ALPS, SCXML, smcat, XState JSON)

## Clarifications

### Session 2026-03-15

- Q: Should the shared AST live in its own namespace/module under `src/Frank.Statecharts/Ast/` or in the root namespace? -> A: Under `Ast/` sub-directory, following the per-format pattern (e.g., `Wsd/`).
- Q: Should the cross-format validator be part of this spec or carved off? -> A: The cross-format validator is NOT part of this spec -- it will be a standalone spec that depends on all format parsers being complete and uses the shared AST to validate consistency across formats.
- Q: Should the WSD parser's format-specific types (Token, TokenKind, etc.) also move to the shared AST? -> A: No. Only the semantic AST types (states, transitions, guards, etc.) are shared. Lexer tokens and parse infrastructure remain per-format.

## Background

The Frank.Statecharts spec pipeline (#57) supports five statechart notation formats: WSD, ALPS, SCXML, smcat, and XState JSON. Each format has its own parser and generator (defined in separate specs). Currently, the WSD parser (spec 007, #90) defines its own per-format AST types in `src/Frank.Statecharts/Wsd/Types.fs` -- types like `Participant`, `Message`, `Diagram`, `Group`, and `GuardAnnotation` that represent WSD-specific concepts.

The decision was made that ALL format parsers should populate a single shared AST rather than each maintaining its own semantic types. The shared AST represents statechart concepts in a format-agnostic way: states, transitions, guards, events, actions, data/context, hierarchy, initial/final markers, and annotations. Each format parser populates the parts of the shared AST it can represent (e.g., smcat has no data model concept, ALPS has no workflow ordering).

The shared AST serves three roles:
1. **Intermediate representation**: Parsers populate it from format-specific text/XML/JSON
2. **Generator input**: Generators serialize from it to format-specific output
3. **Cross-format bridge**: The forthcoming cross-format validator compares shared ASTs from different format parsers to detect inconsistencies

This spec extends what the WSD parser originally built. The WSD AST types are the starting point, but the shared AST generalizes them to accommodate all five formats. The existing WSD parser must be migrated to use the shared types.

### Format Capabilities Matrix

The shared AST must represent the superset of capabilities across all formats:

- **States**: WSD participants, ALPS state descriptors, SCXML `<state>`/`<final>`, smcat state names, XState state nodes
- **Transitions**: WSD messages/arrows, ALPS transition descriptors, SCXML `<transition>`, smcat `=>` arrows, XState event handlers
- **Guards**: WSD `note over` extension, ALPS `ext` elements, SCXML `cond` attribute, smcat `[guard]` labels, XState guard functions
- **Events and Actions**: WSD message labels, ALPS safe/unsafe/idempotent, SCXML `event` attribute, smcat `event / action` labels, XState event names
- **Data and Context**: WSD parameters, ALPS semantic descriptors, SCXML `<datamodel>`/`<data>`, XState context (smcat has no data model)
- **Hierarchy**: ALPS nested descriptors, SCXML `<parallel>`/`<history>`, smcat composite states, XState nested states (WSD has no hierarchy)
- **Initial state**: WSD first participant, SCXML `initial` attribute, smcat `initial =>`, XState `initial` property (ALPS has no initial state concept)
- **Final state**: SCXML `<final>`, smcat `final` keyword, XState final states (WSD and ALPS have no final state concept)
- **Transition semantics**: WSD solid/dashed/activate/deactivate arrow types, ALPS safe/unsafe/idempotent type annotations (other formats have no transition style concept)

## User Scenarios & Testing

### User Story 1 - Define Format-Agnostic Statechart AST Types (Priority: P1)

A developer working on any format parser (WSD, ALPS, SCXML, smcat, XState JSON) imports the shared AST types and uses them to represent parsed statechart structures. The types cover the full superset of statechart concepts that any supported format can express: states (with type classification), transitions (with optional events, guards, and actions), data model entries, hierarchical nesting, and format-specific annotations.

**Why this priority**: This is the foundational deliverable. Without a correct and complete set of shared types, no format parser can populate the shared AST, and the entire multi-format pipeline stalls.

**Independent Test**: Construct a `StatechartDocument` programmatically representing a tic-tac-toe game (4 states, multiple transitions with guards, an initial state marker), verify that every field can be populated, and confirm that the AST round-trips through F# serialization (structural equality check).

**Acceptance Scenarios**:

1. **Given** a need to represent a simple statechart with states "idle", "active", and "done", **When** the developer constructs shared AST nodes, **Then** each state is represented as a `StateNode` with a unique identifier and optional type classification (regular, initial, final, parallel, history).
2. **Given** a transition from "idle" to "active" triggered by event "start" with guard "isReady" and action "logStart", **When** modeled in the shared AST, **Then** the `TransitionEdge` record captures source state, target state, event name, guard name, and action name as separate optional fields.
3. **Given** a statechart with a data model containing variables "count" (expression: "0") and "player" (expression: "'X'"), **When** modeled in the shared AST, **Then** `DataEntry` records capture each variable as a name/expression pair.
4. **Given** a statechart with hierarchical states (parent "playing" containing children "xTurn" and "oTurn"), **When** modeled in the shared AST, **Then** the parent `StateNode` contains child `StateNode` records preserving the nesting relationship.
5. **Given** a statechart with format-specific annotations (e.g., WSD arrow style "dashed", ALPS descriptor type "idempotent"), **When** modeled in the shared AST, **Then** annotations are stored as typed discriminated union values on the relevant AST node without polluting the core state/transition model.

---

### User Story 2 - Migrate WSD Parser to Shared AST (Priority: P1)

A developer who previously used the WSD parser's per-format AST (`Frank.Statecharts.Wsd.Types.Diagram`, `Message`, `Participant`, etc.) now works with the shared AST types. The WSD parser's output is a `StatechartDocument` (the shared AST root type) instead of a WSD-specific `Diagram`. The WSD-specific lexer tokens and parse infrastructure remain unchanged; only the semantic output types change.

**Why this priority**: The WSD parser is the only format parser already implemented. Migrating it validates that the shared AST design actually works for a real parser and surfaces any design issues before other parsers begin implementation.

**Independent Test**: Run the existing WSD parser test suite against the migrated parser. All tests must pass with equivalent assertions (field names may change, but the semantic content must match). Parse Amundsen's onboarding WSD example and verify every element maps to the correct shared AST node.

**Acceptance Scenarios**:

1. **Given** the WSD parser parsing `participant Client`, **When** the parser produces output, **Then** the result contains a `StateNode` with identifier "Client" (replacing the previous `Participant` record).
2. **Given** the WSD parser parsing `Client->Server: authenticate(token)`, **When** the parser produces output, **Then** the result contains a `TransitionEdge` with source "Client", target "Server", event "authenticate", and parameters ["token"] (replacing the previous `Message` record).
3. **Given** the WSD parser parsing `note over Client: [guard: role=admin]`, **When** the parser produces output, **Then** the result contains a guard annotation associated with the appropriate state/transition in the shared AST, with key-value pair ("role", "admin").
4. **Given** the WSD parser parsing a diagram with `alt`/`else`/`end` grouping blocks, **When** the parser produces output, **Then** the result contains hierarchical branch structures in the shared AST preserving group kind, conditions, and nesting.
5. **Given** the full existing WSD parser test suite, **When** run against the migrated parser, **Then** all tests produce semantically equivalent results (same states, transitions, guards, groups extracted from the same input text).

---

### User Story 3 - Support Partial Population by Format Parsers (Priority: P1)

A format parser that cannot express certain statechart concepts (e.g., smcat has no data model, ALPS has no initial state concept) leaves those portions of the shared AST empty (using `option` types or empty lists) without producing errors. Downstream consumers can inspect which portions were populated and reason about format capabilities.

**Why this priority**: Partial population is essential for the multi-format pipeline. Each format covers different aspects of a statechart. If the AST required all fields to be populated, most format parsers could not use it.

**Independent Test**: Construct a shared AST as if populated by each of the five format parsers (using only the fields that format can express), verify that the construction succeeds without errors, and confirm that unpopulated fields are correctly represented as `None` or `[]`.

**Acceptance Scenarios**:

1. **Given** a simulated smcat parser populating states, transitions, guards, events, and actions but NOT data model entries, **When** the shared AST is constructed, **Then** the `DataEntries` field is an empty list and the document is valid.
2. **Given** a simulated ALPS parser populating states, transitions, and transition type annotations but NOT initial state or workflow ordering, **When** the shared AST is constructed, **Then** the `InitialStateId` field is `None` and no ordering is implied beyond the list order.
3. **Given** a simulated SCXML parser populating states, transitions, guards, data model, hierarchy, initial state, and final states, **When** the shared AST is constructed, **Then** all fields are populated (SCXML is the most expressive format) and the document represents the full statechart.
4. **Given** a simulated WSD parser populating states, transitions, transition style annotations, and guards, **When** the shared AST is constructed, **Then** hierarchy fields are empty (WSD has no hierarchy concept) and the document is valid.

---

### User Story 4 - Preserve Source Position Information (Priority: P2)

Each AST node carries optional source position information (line and column from the original input text) so that error messages, warnings, and debugging tools can point back to the exact location in the source that produced the node. Source positions are set by parsers during construction and ignored by generators.

**Why this priority**: Source position information is critical for actionable error messages. Without it, users cannot locate problems in their source diagrams. This is a quality-of-life enhancement built on top of the core types.

**Independent Test**: Parse a WSD text through the migrated parser, verify that every AST node in the result carries a source position with line and column values matching the original input.

**Acceptance Scenarios**:

1. **Given** a WSD parser processing `participant Client` on line 3, column 1, **When** the shared AST node is created, **Then** the `SourcePosition` field contains line 3, column 1.
2. **Given** a generator constructing a shared AST programmatically (not from parsed text), **When** AST nodes are created, **Then** the `SourcePosition` field is `None` (generators do not produce source positions).
3. **Given** a shared AST node with a source position, **When** an error is reported against that node, **Then** the error message can include the line and column from the source position.

---

### User Story 5 - Support Format-Specific Annotations (Priority: P2)

Each format has concepts that do not generalize across all formats (e.g., WSD arrow styles, ALPS descriptor types, SCXML invoke/history elements). The shared AST provides a typed annotation mechanism that lets each format attach format-specific metadata to AST nodes without affecting the core state/transition model. Annotations use discriminated unions (not stringly-typed dictionaries) for type safety.

**Why this priority**: Without annotations, format-specific information would be lost during parsing or would pollute the core types with format-specific fields. Annotations enable roundtripping for each format.

**Independent Test**: Construct a shared AST with WSD-specific annotations (arrow style on transitions) and SCXML-specific annotations (invoke and history on states), verify both annotation types coexist on the same AST without conflict.

**Acceptance Scenarios**:

1. **Given** a WSD parser encountering a `-->` (dashed arrow), **When** the transition is represented in the shared AST, **Then** the `TransitionEdge` carries an annotation of type `WsdArrowStyle.Dashed` without affecting the core transition fields.
2. **Given** an SCXML parser encountering `<history id="h1" type="deep"/>`, **When** the history element is represented in the shared AST, **Then** the parent state carries an annotation of type `ScxmlHistory` with id "h1" and type "deep".
3. **Given** an ALPS parser encountering `type="idempotent"` on a descriptor, **When** the transition is represented in the shared AST, **Then** the `TransitionEdge` carries an annotation of type `AlpsTransitionType.Idempotent`.
4. **Given** a shared AST node with annotations from multiple formats (hypothetical merge scenario), **When** annotations are inspected, **Then** each format's annotations can be retrieved independently by type.

---

### User Story 6 - Parse Result Consistency (Priority: P2)

All format parsers return a consistent parse result type that wraps the shared AST with a list of errors and warnings. This ensures downstream consumers (cross-format validator, CLI tools) can process results from any parser uniformly without format-specific handling.

**Why this priority**: A consistent parse result type simplifies downstream tooling. Without it, each consumer would need format-specific error handling.

**Independent Test**: Construct parse results as if from the WSD parser (with warnings about implicit participants) and from the SCXML parser (with errors about malformed XML), verify both conform to the same `ParseResult` type with errors and warnings accessible through the same fields.

**Acceptance Scenarios**:

1. **Given** a WSD parser returning a successful parse with two warnings, **When** the result is accessed through the shared `ParseResult` type, **Then** the `Document` field contains the shared AST and the `Warnings` field contains two entries with position, description, and optional suggestion.
2. **Given** an SCXML parser returning a failed parse with one error, **When** the result is accessed through the shared `ParseResult` type, **Then** the `Errors` field contains one entry with position (if available from XML parser), description, expected value, found value, and corrective example.
3. **Given** any format parser, **When** it returns a parse result, **Then** the result type is identical (`ParseResult<StatechartDocument>`) regardless of which format parser produced it.

---

### Edge Cases

- Empty document (no states, no transitions) produces a valid `StatechartDocument` with empty lists (not a parse error)
- State with no transitions is valid (terminal/sink state)
- Transition with no event name is valid (automatic/completion transition, used by SCXML)
- Transition with no target is valid (internal transition, used by SCXML)
- Self-transition (source equals target) is valid
- Multiple transitions between the same source and target with different events are valid
- State with both `IsInitial` and `IsFinal` set produces a warning but is valid (degenerate single-state machine)
- Deeply nested state hierarchy (5+ levels) is valid (tested for SCXML and smcat composite states)
- Data entry with empty expression value is valid (SCXML `<data id="x"/>` with no expr)
- Annotations list on a node may be empty (format parser chose not to add format-specific metadata)
- Source position fields are `None` when AST is constructed programmatically (by generators or tests)
- Unicode characters in state identifiers, event names, guard conditions, and data expressions
- Guard annotation with empty key-value pairs list is valid (produces a warning)
- Multiple guard annotations on the same transition are collected into a single list
- Transition with multiple targets (SCXML space-separated targets) produces one `TransitionEdge` per target

## Requirements

### Functional Requirements

- **FR-001**: System MUST define a `StatechartDocument` record type as the root of the shared AST, containing: an optional document title, an optional initial state identifier, a list of `StateNode` records, a list of `TransitionEdge` records, a list of `DataEntry` records, and a list of format-specific `Annotation` values
- **FR-002**: System MUST define a `StateNode` record type representing a state, containing: a unique string identifier, an optional display label, a `StateKind` discriminated union (Regular, Initial, Final, Parallel, History of HistoryKind, Choice, ForkJoin, Terminate), optional child `StateNode` records (for hierarchy), optional state activities (entry, exit, do actions), an optional source position, and a list of format-specific annotations
- **FR-003**: System MUST define a `TransitionEdge` record type representing a transition, containing: source state identifier, target state identifier (optional, for internal transitions), optional event name, optional guard name, optional action description, optional parameters list, an optional source position, and a list of format-specific annotations
- **FR-004**: System MUST define a `DataEntry` record type representing a data model variable, containing: a name/identifier, an optional expression value, and an optional source position
- **FR-005**: System MUST define a `StateKind` discriminated union with cases: `Regular`, `Initial`, `Final`, `Parallel`, `ShallowHistory`, `DeepHistory`, `Choice`, `ForkJoin`, `Terminate` -- covering the superset of state types across all formats
- **FR-006**: System MUST define an `Annotation` discriminated union with cases for format-specific metadata: `WsdAnnotation` (arrow style, direction), `AlpsAnnotation` (descriptor type, href, extensions), `ScxmlAnnotation` (invoke, namespace), `SmcatAnnotation` (attributes, activities), and `XStateAnnotation` (context, actions) -- each case carrying typed data rather than stringly-typed values
- **FR-007**: System MUST define a `SourcePosition` struct type with `Line` (int) and `Column` (int) fields, used as an optional field on all AST node types
- **FR-008**: System MUST define a `ParseFailure` record type containing: source position (optional), description, expected value, found value, and corrective example -- consistent across all format parsers
- **FR-009**: System MUST define a `ParseWarning` record type containing: source position (optional), description, and optional suggestion -- consistent across all format parsers
- **FR-010**: System MUST define a `ParseResult` record type containing: a `StatechartDocument` (best-effort, always present), a `ParseFailure list`, and a `ParseWarning list` -- used as the return type for all format parsers
- **FR-011**: System MUST migrate the WSD parser (`src/Frank.Statecharts/Wsd/Parser.fs`) to produce `StatechartDocument` (shared AST) instead of `Diagram` (WSD-specific AST), while keeping the WSD lexer and token types unchanged
- **FR-012**: System MUST preserve the WSD parser's existing behavior: partial parsing with error recovery, configurable error limits, implicit participant warnings, guard annotation extraction, and grouping block nesting
- **FR-013**: System MUST use F# `option` types for fields that some formats cannot populate, ensuring partial population is type-safe (not null-based)
- **FR-014**: System MUST place the shared AST types in a namespace/module accessible to all format parsers within `Frank.Statecharts` without requiring cross-project references
- **FR-015**: System MUST define a `GroupBlock` record type representing control flow grouping (alt, opt, loop, par, break, critical, ref) with kind, branches (each with optional condition and child elements), and source position -- covering WSD and smcat grouping constructs
- **FR-016**: System MUST define a `StatechartElement` discriminated union with cases: `StateDecl` of StateNode, `TransitionElement` of TransitionEdge, `NoteElement` of NoteContent, `GroupElement` of GroupBlock, `DirectiveElement` of Directive -- used as the ordered element list in `StatechartDocument`
- **FR-017**: System MUST compile under multi-target (net8.0/net9.0/net10.0) with `dotnet build`
- **FR-018**: System MUST remove or deprecate the WSD-specific AST types (`Frank.Statecharts.Wsd.Types.Diagram`, `Participant`, `Message`, `Note`, `Group`, etc.) after migration, preventing duplicate type definitions
- **FR-019**: System MUST define a `HistoryKind` discriminated union with cases `Shallow` and `Deep`, used by the `StateKind.ShallowHistory` and `StateKind.DeepHistory` cases (covering SCXML `<history type="shallow|deep">`)
- **FR-020**: System MUST define a `TransitionStyle` record type with `ArrowStyle` (Solid, Dashed) and `Direction` (Forward, Deactivating) fields, used by WSD annotations to preserve arrow type information during roundtripping
- **FR-021**: System MUST define a `GroupKind` discriminated union with cases: `Alt`, `Opt`, `Loop`, `Par`, `Break`, `Critical`, `Ref` -- reused from the WSD parser's existing `GroupKind` type
- **FR-022**: System MUST preserve structural equality semantics on all shared AST record types (no mutable fields, no reference equality) to enable comparison-based testing

### Key Entities

- **StatechartDocument**: Root AST node containing an optional title, optional initial state identifier, ordered list of `StatechartElement` nodes, a list of top-level `DataEntry` records, and a list of document-level annotations. Represents the complete parsed statechart regardless of source format.
- **StateNode**: A state with identifier, optional label, kind (Regular/Initial/Final/Parallel/History/Choice/ForkJoin/Terminate), optional child states (for compound/parallel states), optional activities (entry/exit/do), optional source position, and annotations list.
- **TransitionEdge**: A directed edge between states with optional event name, optional guard name, optional action description, optional parameters list, optional source position, and annotations list. Source is required; target is optional (for internal/completion transitions).
- **DataEntry**: A data model variable with name, optional expression value, and optional source position. Used by SCXML `<datamodel>/<data>` and ALPS semantic descriptors.
- **GroupBlock**: A control flow grouping with kind (Alt/Opt/Loop/Par/Break/Critical/Ref), ordered list of branches (each with optional condition and child `StatechartElement` list), and source position.
- **Annotation**: A discriminated union of format-specific metadata. Each case carries typed data (e.g., `WsdAnnotation of TransitionStyle` for arrow types, `ScxmlAnnotation of ScxmlMeta` for invoke/history).
- **ParseResult**: Uniform result type for all format parsers, containing a best-effort `StatechartDocument`, a list of `ParseFailure` errors, and a list of `ParseWarning` warnings.
- **SourcePosition**: A struct with 1-based `Line` and `Column` fields, used optionally on all AST nodes for error reporting.

## Success Criteria

### Measurable Outcomes

- **SC-001**: All shared AST types compile under multi-target (net8.0/net9.0/net10.0) with `dotnet build` and are accessible from the `Frank.Statecharts` namespace
- **SC-002**: The migrated WSD parser produces `StatechartDocument` output that, when tested against the existing WSD parser test suite, yields semantically equivalent results for all test cases (same states, transitions, guards, groups)
- **SC-003**: A `StatechartDocument` can be constructed with any subset of fields populated (demonstrating partial population for each format), and construction succeeds without runtime errors
- **SC-004**: Amundsen's onboarding WSD example and the tic-tac-toe WSD example both parse through the migrated parser and produce shared AST documents where every state, transition, guard, and group can be verified by walking the AST
- **SC-005**: Format-specific annotations can be attached to and retrieved from AST nodes without affecting the core state/transition model (demonstrated by unit tests with WSD and SCXML annotation types)
- **SC-006**: All AST record types support structural equality (verified by creating two identical ASTs independently and asserting equality)
- **SC-007**: Source position information from the WSD parser is preserved on all AST nodes in the migrated output (verified by checking positions match the original input)

## Assumptions

- The WSD parser's lexer and token types (`TokenKind`, `Token`) remain WSD-specific and do not move to the shared AST. Only the semantic output types (states, transitions, guards) are shared.
- Each format parser will import the shared AST types and produce `ParseResult<StatechartDocument>` as its output. The shared AST does not dictate parser internals.
- The `Annotation` discriminated union will be extended by future specs (011 ALPS, 013 smcat, 018 SCXML) as each format parser is implemented. This spec defines the union structure and the WSD annotation cases; other cases are defined as stubs with documented intent.
- The shared AST is an intermediate representation for the spec pipeline. It does not replace `StateMachineMetadata` (the runtime type from #87) -- a separate mapping step converts between the two.
- The existing WSD parser tests will be updated to assert against shared AST types rather than WSD-specific types. The test logic (what is checked) remains the same; only the type names and field accessors change.
- Format parsers that are not yet implemented (ALPS, SCXML, smcat, XState) will implement their parsers against the shared AST as defined here. If they discover the AST is missing types they need, those additions will be proposed as amendments to this spec.
- The cross-format validator (currently in spec 017) will be carved off into its own standalone spec that depends on all format parsers and the shared AST.
