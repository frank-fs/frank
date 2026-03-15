# Feature Specification: smcat Parser and Generator

**Feature Branch**: `013-smcat-parser-generator`
**Created**: 2026-03-15
**Status**: Draft
**GitHub Issue**: #100
**Dependencies**: Frank.Statecharts (#87) core runtime is complete (merged via PR #96), Shared Statechart AST (spec 020) defines the unified AST all format parsers populate
**Location**: Internal to `src/Frank.Statecharts/` (Smcat parser namespace), tests in `test/Frank.Statecharts.Tests/`
**Input**: GitHub issue #100 -- smcat Parser + Generator: bidirectional state-machine-cat support

## Clarifications

### Session 2026-03-15

- Q: Are the `StateMachineMetadata` types available? -> A: Yes, #87 merged via PR #96. The types `StateMachine`, `StateMachineMetadata`, `StateInfo`, `Guard`, `TransitionResult`, `BlockReason`, and `GuardResult` are all defined in `src/Frank.Statecharts/Types.fs` and `src/Frank.Statecharts/StatefulResourceBuilder.fs`.

## Background

smcat (state-machine-cat) is a lightweight text-based state machine notation with visualization support. It uses a concise, readable syntax where states are declared implicitly or explicitly, transitions use `=>` arrows, and transition labels follow the format `event [guard] / action`. The notation supports pseudo-states (initial, final, history, choice, fork/join), composite/nested states, state activities (entry/exit/do), and attributes on both states and transitions.

This feature provides bidirectional support: parsing smcat text into a typed F# AST (and mapping that AST to `StateMachineMetadata` for cross-validation and LLM-assisted code scaffolding), and generating smcat text from `StateMachineMetadata` (for exporting compiled Frank.Statecharts assemblies back to a human-readable notation).

The parser and generator use the shared statechart AST (defined in spec 020), which all format parsers populate. smcat populates the portions of the shared AST it can represent: states (including pseudo-states), transitions with event/guard/action labels, and composite state hierarchy. Data model and semantic meaning, which smcat does not express, are left unpopulated for other formats to contribute. The implementation follows the same internal-module pattern established by the existing WSD parser in `src/Frank.Statecharts/Wsd/`.

### smcat Syntax Overview

- **States**: Declared implicitly via transitions or explicitly with activities and attributes
- **Transitions**: `source => target: event [guard] / action;`
- **Pseudo-states**: Names containing `initial`, `final`, `history`; names starting with `^` (choice) or `]` (fork/join/junction)
- **Composite states**: States containing nested state machines via `{ ... }` blocks
- **Activities**: `entry/`, `exit/`, and `...` (do) activities on state declarations
- **Attributes**: `[key=value]` on states (`type`, `label`, `color`, `active`, `class`) and transitions (`color`, `width`, `type`, `class`)
- **Comments**: Lines starting with `#`
- **Statement terminators**: Semicolons and commas

## User Scenarios & Testing

### User Story 1 - Parse smcat Text into a Typed AST (Priority: P1)

A developer passes an smcat text string to the parser and receives a typed F# AST representing all diagram elements: states (with types, activities, and attributes), transitions (with optional event/guard/action labels and attributes), pseudo-states, and composite states.

**Why this priority**: This is the core value proposition. Without a correct AST, neither the metadata mapper nor the generator can function. All downstream use cases depend on accurate parsing.

**Independent Test**: Parse a representative smcat example containing states, transitions with event/guard/action labels, pseudo-states (initial, final), and verify every element maps to the expected discriminated union case with correct field values.

**Acceptance Scenarios**:

1. **Given** an smcat string with `initial => home: start;`, **When** parsed, **Then** the AST contains a `Transition` node with source state type `Initial`, target state `home`, and event label `start`.
2. **Given** an smcat string with `on => off: switch flicked [not emergency] / light off;`, **When** parsed, **Then** the `Transition` AST node includes event `switch flicked`, guard `not emergency`, and action `light off`.
3. **Given** an smcat string with explicit state declarations separated by commas (`idle, running, stopped;`), **When** parsed, **Then** the AST contains three `State` declaration nodes with correct names.
4. **Given** an smcat string with state activities (`doing: entry/ start timer exit/ stop timer ...;`), **When** parsed, **Then** the `State` AST node includes the entry, exit, and do activity strings.
5. **Given** an smcat string with state attributes (`on [label="Lamp on" color="#008800"];`), **When** parsed, **Then** the `State` AST node includes the attributes as key-value pairs.

---

### User Story 2 - Map Parsed AST to StateMachineMetadata (Priority: P1)

A developer parses an smcat file and then maps the resulting AST into a `StateMachineMetadata`-compatible representation. This enables the parsed smcat to serve as input for LLM-assisted code scaffolding or cross-validation against compiled Frank.Statecharts assemblies.

**Why this priority**: The AST alone is not useful without a bridge to the Frank.Statecharts type system. The mapper is required for both the CLI `import` command and the roundtrip guarantee.

**Independent Test**: Parse an onboarding-style smcat example, map it to metadata, and verify the state names, transition topology, guard names, and initial state all match the source diagram.

**Acceptance Scenarios**:

1. **Given** a parsed AST with states `initial`, `home`, `WIP`, `customerData`, `final`, **When** mapped, **Then** the metadata contains all five states with correct names and the initial state is set to `home` (the first non-pseudo target of `initial`).
2. **Given** a parsed AST with a transition `WIP => customerData: collectCustomerData [isValid] / logAction;`, **When** mapped, **Then** the metadata records a transition from `WIP` to `customerData` with event name `collectCustomerData` and guard name `isValid`.
3. **Given** a parsed AST with `final` pseudo-state, **When** mapped, **Then** the metadata marks the corresponding state's `IsFinal` flag as true.
4. **Given** a parsed AST with no guard annotations on any transition, **When** mapped, **Then** the metadata guard list is empty.

---

### User Story 3 - Generate smcat Text from StateMachineMetadata (Priority: P2)

A developer has a compiled Frank.Statecharts assembly and wants to export its state machine definition as human-readable smcat text. The generator produces valid smcat notation with `event [guard] / action` labels on transitions.

**Why this priority**: Generation is the reverse direction and is useful for documentation, visualization, and round-trip validation. It depends on the AST types being established first (User Story 1).

**Independent Test**: Build metadata programmatically with known states, transitions, guards, and initial state, generate smcat text, and verify the output is valid smcat that re-parses to an equivalent AST.

**Acceptance Scenarios**:

1. **Given** metadata with states `idle`, `running`, `stopped` and initial state `idle`, **When** generated, **Then** the output contains `initial => idle;` as the first transition.
2. **Given** metadata with a transition from `idle` to `running` with event `start`, guard `isReady`, and action `logStart`, **When** generated, **Then** the output contains `idle => running: start [isReady] / logStart;`.
3. **Given** metadata with a final state `completed`, **When** generated, **Then** the output contains a transition to `final` from the appropriate source state.
4. **Given** metadata with transitions but no guards or actions, **When** generated, **Then** transition labels contain only the event name (no brackets or slashes).

---

### User Story 4 - Roundtrip Consistency (Priority: P2)

A developer parses an smcat file, maps it to metadata, generates smcat text from that metadata, and then re-parses the generated text. The two ASTs are semantically equivalent (state topology, transition labels, and guard names match).

**Why this priority**: Roundtrip consistency is the key quality guarantee that validates both the parser and generator are correct and compatible.

**Independent Test**: Take multiple smcat examples of varying complexity, run the full parse-map-generate-reparse cycle, and compare the resulting ASTs for semantic equivalence.

**Acceptance Scenarios**:

1. **Given** a simple smcat file with 3 states and 3 transitions, **When** roundtripped, **Then** the re-parsed AST has identical state names, transition source/target pairs, and event labels.
2. **Given** an smcat file with guards and actions on transitions, **When** roundtripped, **Then** all guard and action strings survive the cycle.
3. **Given** an smcat file with initial and final pseudo-states, **When** roundtripped, **Then** the pseudo-state semantics are preserved.

---

### User Story 5 - Structured Failure Reports for Invalid smcat (Priority: P3)

A developer submits malformed smcat text. Instead of a generic error, the parser produces a structured failure report with the line/column of the error, what was expected, what was found, and a corrective example.

**Why this priority**: Good error messages reduce iteration time and teach correct smcat syntax. This is a quality-of-life enhancement that can be delivered after core parsing works.

**Independent Test**: Feed the parser deliberately malformed smcat (missing semicolons, invalid arrow syntax, unclosed brackets, unclosed composite state blocks) and verify each failure report contains position, expectation, and a corrective example.

**Acceptance Scenarios**:

1. **Given** `on => off switch flicked;` (missing colon before label), **When** parsed, **Then** the failure report includes line/column, states "expected `:` or `;` after target state", and shows correct syntax as example.
2. **Given** `on ==> off;` (invalid arrow), **When** parsed, **Then** the failure report states "unrecognized arrow syntax, expected `=>`" with a corrective example.
3. **Given** `on => off: start [guard;` (unclosed bracket), **When** parsed, **Then** the failure report indicates the unclosed bracket with line/column position.
4. **Given** multiple errors in the same input, **When** parsed, **Then** all errors are collected (not just the first) up to a configurable maximum.

---

### Edge Cases

- Empty input string produces a valid empty AST (no states, no transitions)
- Whitespace-only input produces a valid empty AST
- Comment-only input (lines starting with `#`) produces a valid empty AST
- States with special characters in quoted names (`"a state"`) are parsed correctly
- Transition labels with only an event (no guard, no action) are parsed correctly
- Transition labels with only a guard (no event, no action) are parsed correctly
- Transition labels with only an action (no event, no guard) are parsed correctly
- Pseudo-state detection by naming convention: `initial`, `final`, `history`, `deep.history`, names starting with `^` (choice), names starting with `]` (fork/join)
- Composite states with nested state machines parse recursively
- Parallel composite states (name contains "parallel") with comma-separated regions
- State activities (`entry/`, `exit/`, `...`) are preserved in the AST
- State and transition attributes (`[key=value]`) are preserved as key-value pairs
- Semicolon and comma as statement terminators are both accepted
- Windows (`\r\n`) and Unix (`\n`) line endings are both handled
- Unicode characters in state names, labels, and comments
- Deeply nested composite states (5+ levels) parse without stack overflow
- Generator output for metadata with no transitions produces only state declarations
- Generator output orders initial-state transition first for readability
- Roundtrip: state ordering may differ but topology is preserved

## Requirements

### Functional Requirements

- **FR-001**: System MUST tokenize all smcat syntax elements into a flat token stream: state identifiers (plain and quoted), transition arrows (`=>`), colons, semicolons, commas, square brackets, forward slashes, curly braces, hash comments, attribute key-value pairs, and newlines
- **FR-002**: System MUST parse the token stream into a typed F# AST using discriminated unions, producing an `SmcatDocument` record containing an ordered list of `SmcatElement` nodes
- **FR-003**: System MUST parse transition labels in the format `event [guard] / action`, where each component (event, guard, action) is optional
- **FR-004**: System MUST recognize pseudo-states by naming convention: states named or containing `initial` are initial states; states named or containing `final` are final states; states containing `history` are history states; states starting with `^` are choice pseudo-states; states starting with `]` are fork/join/junction pseudo-states
- **FR-005**: System MUST parse composite (nested) state declarations containing inner state machines within `{ ... }` blocks, supporting arbitrary nesting depth
- **FR-006**: System MUST parse state activities (`entry/`, `exit/`, and `...` do activities) from explicit state declarations into structured fields on the AST node
- **FR-007**: System MUST parse state and transition attributes in `[key=value key2=value2]` format, preserving them as ordered key-value pairs on the AST node
- **FR-008**: System MUST map a parsed smcat AST into a representation compatible with `StateMachineMetadata`, extracting: state names, initial state, final states, transition topology (source, target, event, guard name), and guard names
- **FR-009**: System MUST generate valid smcat text from `StateMachineMetadata`, producing transitions with `event [guard] / action` labels where applicable
- **FR-010**: System MUST produce structured failure reports for unparseable input, each containing: source position (line and column), description of the error, what was expected, what was found, and a corrective example
- **FR-011**: System MUST collect multiple parse errors (up to a configurable limit, default: 50) rather than aborting on the first error
- **FR-012**: System MUST ignore comment lines (starting with `#`) and blank lines during parsing
- **FR-013**: System MUST handle both Windows (`\r\n`) and Unix (`\n`) line endings
- **FR-014**: System MUST accept both semicolons and commas as statement terminators

### Key Entities

- **Token**: Lexer output -- keyword, arrow, identifier, string literal, punctuation, comment, newline, EOF, with source position
- **SmcatDocument**: Top-level AST node containing an ordered list of `SmcatElement` nodes (state declarations, transitions, comments)
- **SmcatState**: A state declaration with name, optional display label, optional state type (regular, initial, final, history, choice, fork/join, terminate), optional activities (entry, exit, do), optional attributes, and optional nested `SmcatDocument` for composite states
- **SmcatTransition**: Source state, target state, optional event name, optional guard string, optional action string, optional attributes, and source position
- **TransitionLabel**: Parsed components of the `event [guard] / action` label format -- each component is an optional string
- **SmcatElement**: Discriminated union -- `StateDeclaration` of SmcatState, `TransitionElement` of SmcatTransition, `CommentElement` of string
- **StateType**: Discriminated union -- `Regular`, `Initial`, `Final`, `ShallowHistory`, `DeepHistory`, `Choice`, `ForkJoin`, `Terminate`
- **StateActivity**: Record with optional `Entry`, `Exit`, and `Do` string fields
- **ParseFailure**: Source position (line, column), error description, expected/found descriptions, corrective example text
- **ParseResult**: Record containing an `SmcatDocument` (best-effort, possibly partial), a `ParseFailure list` for hard errors, and a `ParseWarning list` for ambiguities

## Success Criteria

### Measurable Outcomes

- **SC-001**: Parser correctly handles representative smcat examples end-to-end, producing an AST where every state, transition, event, guard, and action can be verified by walking the AST
- **SC-002**: All smcat transition label components (event, guard, action) are correctly extracted into separate AST fields for any combination of present/absent components
- **SC-003**: Pseudo-states (initial, final, history, choice, fork/join) are correctly identified by naming convention and assigned the appropriate `StateType` value
- **SC-004**: Composite states with nested state machines parse correctly to at least 5 levels of nesting without error
- **SC-005**: Generator produces valid smcat text that re-parses to a semantically equivalent AST (roundtrip consistency for state topology and transition labels)
- **SC-006**: Invalid input produces failure reports where every report includes a line/column position and a corrective example
- **SC-007**: Library compiles under multi-target (net8.0/net9.0/net10.0) with `dotnet build`
- **SC-008**: Parser handles smcat inputs up to 500 lines without measurable allocation pressure (no intermediate string concatenation in hot paths)
- **SC-009**: Golden file tests pass for at least 3 smcat examples of varying complexity (simple linear, branching with guards, composite states)

## Assumptions

- Guards in smcat are opaque strings -- the parser extracts the text between `[` and `]` but does not interpret guard semantics (per research decision D-008)
- smcat has no concept of context/data -- only state topology and transition labels are modeled
- The parser is internal to `Frank.Statecharts` (no standalone use case exists), following the same pattern as the WSD parser
- Actions in transition labels are informational annotations -- they are preserved in the AST but have no execution semantics in the Frank.Statecharts runtime
- The mapper produces an intermediate representation rather than a live `StateMachineMetadata` with closures, since closures (handlers, store access) cannot be derived from text notation
- State type inference from naming conventions follows the state-machine-cat project's own conventions (e.g., names containing "initial", "final", "history")
