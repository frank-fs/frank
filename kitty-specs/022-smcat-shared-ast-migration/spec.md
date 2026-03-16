# Feature Specification: smcat Shared AST Migration

**Feature Branch**: `022-smcat-shared-ast-migration`
**Created**: 2026-03-16
**Status**: Draft
**Dependencies**: #111 (Shared AST, spec 020 -- complete), #100 (smcat Parser + Generator, spec 013 -- complete)
**Location**: `src/Frank.Statecharts/Smcat/` (parser, generator, types), `src/Frank.Statecharts/Ast/Types.fs` (SmcatMeta). Tests in `test/Frank.Statecharts.Tests/Smcat/`.
**Input**: Issue #113 -- smcat: migrate parser and generator to shared StatechartDocument AST

## Clarifications

### Session 2026-03-16

- Q: Should the smcat migration follow the WSD two-file split (Generator.fs: metadata -> AST, Serializer.fs: AST -> smcat text), or replace Generator entirely with just a Serializer? -> A: Follow the WSD two-file split for all formats. Every format should have the same set of files and use the same shared AST. Generator.fs (metadata -> StatechartDocument) + Serializer.fs (StatechartDocument -> smcat text).

## Background

The smcat parser (spec 013, #100) currently produces format-specific types (`SmcatDocument`, `SmcatState`, `SmcatTransition`, `SmcatElement`) and the generator works directly with `StateMachineMetadata` to produce raw text. A separate `Mapper.fs` (267 lines) bridges between these smcat-specific types and the shared `StatechartDocument` AST (spec 020, #111). This mapper is a workaround -- the target design has both parser and serializer working directly with `StatechartDocument`.

The WSD parser/serializer has already been migrated to the shared AST (spec 020) and serves as the reference pattern. The WSD migration:
1. Had the parser produce `Ast.ParseResult` with `StatechartDocument` directly
2. Split output into `Generator.fs` (metadata -> AST) and `Serializer.fs` (AST -> text)
3. Reduced `Types.fs` to only lexer types (`TokenKind`, `Token`)
4. Stored format-specific data in `WsdAnnotation` annotations
5. Deleted all WSD-specific semantic types (`Diagram`, `Participant`, `Message`, etc.)

The smcat migration follows this same pattern exactly.

### Current Architecture (to be replaced)

```
smcat text --> Parser --> SmcatDocument --> Mapper --> StatechartDocument
StateMachineMetadata --> Generator --> smcat text (bypasses AST entirely)
```

### Target Architecture

```
smcat text --> Parser --> StatechartDocument (with SmcatAnnotation annotations)
StatechartDocument --> Serializer --> smcat text
StateMachineMetadata --> Generator --> StatechartDocument (reuse WSD Generator pattern)
```

### Type Mapping Summary

| smcat Type | Shared AST Type | Notes |
|-----------|----------------|-------|
| `SmcatDocument` | `StatechartDocument` | Delete |
| `SmcatState` | `StateNode` | Delete; color/label attrs become `SmcatAnnotation` |
| `SmcatTransition` | `TransitionEdge` | Delete; attrs become annotations |
| `SmcatElement` | `StatechartElement` | Delete |
| `StateType` | `StateKind` | Delete; 1:1 mapping (Regular/Initial/Final/ShallowHistory/DeepHistory/Choice/ForkJoin/Terminate) |
| `StateActivity` | `StateActivities` | Delete; single option -> list per kind |
| `ParseResult` | `Ast.ParseResult` | Delete; `Document` always present (not best-effort option) |
| `ParseFailure` | `Ast.ParseFailure` | Delete; fields match (Position, Description, Expected, Found, CorrectiveExample) |
| `ParseWarning` | `Ast.ParseWarning` | Delete; same structure |
| `SourcePosition` | `Ast.SourcePosition` | Delete; use shared struct |
| `TokenKind` | (keep) | Lexer-internal, not semantic |
| `Token` | (keep, use `Ast.SourcePosition`) | Lexer-internal |
| `TransitionLabel` | (keep) | Parser-internal label parsing helper |
| `SmcatAttribute` | (keep) | Parser-internal key-value pairs from smcat syntax |
| `inferStateType` | (keep, return `Ast.StateKind`) | Parser-internal state classification |

## User Scenarios & Testing

### User Story 1 - Parser Produces Shared AST Directly (Priority: P1)

A developer parsing smcat text receives a shared `Ast.ParseResult` containing a `StatechartDocument` directly, without needing to call a separate mapper. The parser handles all smcat constructs -- states with types, transitions with labels, composite states with children, activities (entry/exit/do), pseudo-states (initial, final, history, choice, fork/join, terminate), and attributes (color, label) -- and maps them into the shared AST types.

**Why this priority**: This is the core deliverable. Without the parser producing shared AST directly, the mapper workaround remains and the cross-format validator cannot work with smcat artifacts uniformly.

**Independent Test**: Parse smcat text containing states, transitions, composite states, and pseudo-states. Assert the resulting `StatechartDocument` has the correct `StateNode` entries with proper `Kind` values, `TransitionEdge` entries with correct source/target/event/guard/action fields, and `SmcatAnnotation` entries for color and label attributes.

**Acceptance Scenarios**:

1. **Given** smcat text `idle => active: start;`, **When** parsed, **Then** the result is an `Ast.ParseResult` where `Document.Elements` contains `StateDecl` nodes for `idle` and `active`, and a `TransitionElement` edge from `idle` to `active` with `Event = Some "start"`.
2. **Given** smcat text with a state `initial => idle;`, **When** parsed, **Then** the `StateNode` for `initial` has `Kind = Initial` (inferred from name via `inferStateType`).
3. **Given** smcat text with a composite state `playing { xTurn => oTurn: move; }`, **When** parsed, **Then** the `StateNode` for `playing` has `Children` containing `StateNode` entries for `xTurn` and `oTurn`.
4. **Given** smcat text with activities `idle [entry/ logEntry exit/ cleanup ...doing]`, **When** parsed, **Then** the `StateNode` for `idle` has `Activities = Some { Entry = ["logEntry"]; Exit = ["cleanup"]; Do = ["doing"] }`.
5. **Given** smcat text with attributes `idle [color="red" label="Idle State"]`, **When** parsed, **Then** the `StateNode` for `idle` has `Annotations` containing `SmcatAnnotation(SmcatColor "red")` and `SmcatAnnotation(SmcatStateLabel "Idle State")`.
6. **Given** smcat text with a transition label `idle => active: start [isReady] / logStart;`, **When** parsed, **Then** the `TransitionEdge` has `Event = Some "start"`, `Guard = Some "isReady"`, `Action = Some "logStart"`.
7. **Given** malformed smcat text (e.g., missing semicolons, invalid tokens), **When** parsed, **Then** the result contains `ParseFailure` entries (shared AST type) with descriptions and positions.
8. **Given** smcat text with pseudo-states (history, deep.history, choice via `^`, fork/join via `]`, terminate), **When** parsed, **Then** each `StateNode` has the correct `Kind` value from the shared `StateKind` enumeration.

---

### User Story 2 - New Serializer Produces smcat Text from Shared AST (Priority: P1)

A developer passes a `StatechartDocument` to a new `serialize` function and receives valid smcat text output. The serializer reconstructs state declarations (with types, activities, attributes, children), transition arrows with labels (event [guard] / action), and comments. Format-specific data is read from `SmcatAnnotation` entries on AST nodes.

**Why this priority**: The serializer is the output half of the pipeline. Following the WSD pattern (Generator.fs + Serializer.fs), the serializer converts shared AST to format text, completing the bidirectional pipeline.

**Independent Test**: Construct a `StatechartDocument` with states, transitions, annotations, and composite states. Call `serialize`. Assert the output is syntactically valid smcat text that can be re-parsed to produce a structurally equal `StatechartDocument`.

**Acceptance Scenarios**:

1. **Given** a `StatechartDocument` with `StateDecl` nodes and `TransitionElement` edges, **When** `serialize` is called, **Then** valid smcat text is produced with state names and `=>` transition arrows.
2. **Given** a `StateNode` with `Kind = Initial`, **When** serialized, **Then** the state is emitted with the `initial` type marker (or relies on naming convention if the identifier already contains "initial").
3. **Given** a `StateNode` with `Children`, **When** serialized, **Then** the output contains a composite state block with `{` and `}` delimiters enclosing child states and transitions.
4. **Given** a `StateNode` with `Activities`, **When** serialized, **Then** the output includes activity declarations (`entry/ ...`, `exit/ ...`, `... doing`).
5. **Given** a `StateNode` with `SmcatAnnotation(SmcatColor "red")` and `SmcatAnnotation(SmcatStateLabel "Idle")`, **When** serialized, **Then** the output includes `[color="red" label="Idle"]` attribute syntax.
6. **Given** a `TransitionEdge` with `Event`, `Guard`, and `Action`, **When** serialized, **Then** the output is `source => target: event [guard] / action;`.
7. **Given** a `StatechartDocument` with no `SmcatAnnotation` entries (e.g., from a different format parser), **When** serialized, **Then** the output uses sensible defaults: no attributes, no activities, regular state type.

---

### User Story 3 - Generator Produces Shared AST from Runtime Metadata (Priority: P1)

A developer passes `StateMachineMetadata` (Frank runtime metadata) to the smcat `Generator.generate` function and receives a `Result<StatechartDocument, GeneratorError>`. This follows the exact same pattern as `Wsd.Generator.generate` -- validating the boxed Machine type, extracting states from `StateHandlerMap`, ordering them (initial first), creating `StateDecl` and `TransitionElement` entries, and returning a `StatechartDocument`.

**Why this priority**: The generator bridges runtime metadata to the shared AST, enabling `frank statechart generate smcat` CLI command (#94). The current Generator.fs bypasses the AST entirely (metadata -> raw text), which must change.

**Independent Test**: Construct `StateMachineMetadata` with known states and handlers. Call `generate`. Assert the resulting `StatechartDocument` has the correct states and transitions.

**Acceptance Scenarios**:

1. **Given** `StateMachineMetadata` with states `["Idle"; "Active"; "Done"]` and `InitialStateKey = "Idle"`, **When** `generate` is called, **Then** the result contains `StateDecl` nodes in order: `Idle` (initial), then `Active`, `Done` (alphabetical).
2. **Given** `StateMachineMetadata` with `StateHandlerMap` containing `("Idle", [("GET", _); ("POST", _)])`, **When** generated, **Then** self-transition `TransitionElement` entries are created for each HTTP method.
3. **Given** `StateMachineMetadata` with `GuardNames = ["isReady"; "isAdmin"]`, **When** generated, **Then** guard information is stored in the AST (as `NoteElement` with guard data, following WSD pattern).
4. **Given** an invalid boxed `Machine` object, **When** `generate` is called, **Then** the result is `Error(UnrecognizedMachineType ...)`.

---

### User Story 4 - Delete Format-Specific Types and Mapper (Priority: P2)

After the parser and serializer work directly with the shared AST, the format-specific document types and the `Mapper.fs` bridge module are deleted. `Types.fs` is reduced to only lexer types (`TokenKind`, `Token`) and parser-internal helpers (`TransitionLabel`, `SmcatAttribute`, `inferStateType`).

**Why this priority**: Cleanup follows core migration. Keeping dead types creates confusion.

**Independent Test**: After deletion, verify `dotnet build` succeeds across all targets and no remaining code references deleted types.

**Acceptance Scenarios**:

1. **Given** the migration is complete, **When** `SmcatDocument`, `SmcatState`, `SmcatTransition`, `SmcatElement`, `ParseResult`, `ParseFailure`, `ParseWarning`, `StateType`, `StateActivity`, and smcat-specific `SourcePosition` are deleted from `Types.fs`, **Then** no compiler errors occur.
2. **Given** the migration is complete, **When** `Mapper.fs` is deleted from the project, **Then** no compiler errors occur.
3. **Given** the deletions, **When** `dotnet build` is run targeting net8.0, net9.0, and net10.0, **Then** the build succeeds with no errors.

---

### User Story 5 - Test Suite Migration (Priority: P1)

All existing smcat tests (parser, generator, lexer, label parser, round-trip, error reporting) are updated to work with the shared AST types. Tests that previously asserted against `SmcatDocument` fields now assert against `StatechartDocument` fields. Round-trip tests verify `parse -> serialize -> re-parse` produces structurally equal `StatechartDocument` values.

**Why this priority**: Tests validate correctness. Without migrated tests, there is no confidence the migration preserved behavior.

**Acceptance Scenarios**:

1. **Given** parser tests that previously asserted against `SmcatDocument` fields, **When** updated to assert against `StatechartDocument` and `ParseResult` fields, **Then** all parser tests pass.
2. **Given** generator tests that previously verified raw text output, **When** updated to verify `StatechartDocument` output (plus separate serializer tests for text output), **Then** all tests pass.
3. **Given** round-trip tests, **When** updated to verify `parse -> serialize -> re-parse` produces structurally equal `StatechartDocument` values, **Then** all round-trip tests pass.
4. **Given** lexer tests and label parser tests, **When** run unchanged (lexer types are retained), **Then** all pass with no modifications needed.
5. **Given** cross-format validator tests that use smcat artifacts, **When** they construct smcat `FormatArtifact` values, **Then** they work with `StatechartDocument` directly (no mapper call needed).

---

### Edge Cases

- Empty smcat document produces a valid `StatechartDocument` with empty element lists
- State with no transitions produces a `StateDecl` with no `TransitionElement` entries
- Transition with empty label (`idle => active;`) produces a `TransitionEdge` with `Event = None`, `Guard = None`, `Action = None`
- Deeply nested composite states (3+ levels) are handled correctly via recursive `StateNode.Children`
- State with both naming convention and explicit `type` attribute -- attribute takes priority (existing `inferStateType` behavior preserved)
- Unicode characters in state names, labels, and event names
- Comments in smcat text produce `NoteElement` or are discarded (matching current parser behavior)
- `StatechartDocument` with no smcat annotations passed to serializer produces valid minimal smcat text
- State with activities but no children (flat state with `entry/exit/do`)
- Transition with only guard, no event or action (`idle => active: [isReady];`)
- Smcat-specific attributes with non-standard keys (not `color` or `label`) are stored as `SmcatAnnotation(SmcatActivity(key, value))` annotations, preserving round-trip fidelity

## Requirements

### Functional Requirements

#### Parser Migration

- **FR-001**: Parser MUST return `Ast.ParseResult` (with `Document: StatechartDocument`, `Errors: ParseFailure list`, `Warnings: ParseWarning list`) instead of smcat-specific `ParseResult` (with `SmcatDocument`)
- **FR-002**: Parser MUST map `SmcatState` fields to `StateNode` fields: `Name` -> `Identifier`, `Label` -> `Label`, `StateType` -> `Kind` (via `StateKind` mapping), `Activities` -> `Activities` (single option -> `StateActivities` with lists), `Children` -> recursive `StateNode.Children`, `Position` -> `Position` (wrapped in `Some`)
- **FR-003**: Parser MUST map `SmcatTransition` fields to `TransitionEdge` fields: `Source` -> `Source`, `Target` -> `Target` (wrapped in `Some`), `Label.Event` -> `Event`, `Label.Guard` -> `Guard`, `Label.Action` -> `Action`, `Position` -> `Position` (wrapped in `Some`)
- **FR-004**: Parser MUST store smcat color attributes as `SmcatAnnotation(SmcatColor color)` on `StateNode.Annotations`
- **FR-005**: Parser MUST store smcat label attributes as `SmcatAnnotation(SmcatStateLabel label)` on `StateNode.Annotations`
- **FR-006**: Parser MUST map smcat activities (entry/exit/do) to `StateNode.Activities` using the shared `StateActivities` record (Entry, Exit, Do as string lists), converting the smcat single-option-per-kind model to the shared list-per-kind model
- **FR-007**: Parser MUST store non-standard smcat attributes (keys other than `color`, `label`, `type`) as `SmcatAnnotation(SmcatActivity(key, value))` on `StateNode.Annotations`, preserving round-trip fidelity for unknown attributes
- **FR-008**: Parser MUST map smcat `StateType` to shared `StateKind` with this mapping: Regular -> Regular, Initial -> Initial, Final -> Final, ShallowHistory -> ShallowHistory, DeepHistory -> DeepHistory, Choice -> Choice, ForkJoin -> ForkJoin, Terminate -> Terminate
- **FR-009**: Parser MUST map parse errors to `Ast.ParseFailure` records and parse warnings to `Ast.ParseWarning` records
- **FR-010**: Parser MUST always return a populated `Document` in the `ParseResult` (best-effort, even on parse failure), matching the shared AST contract

#### Serializer (New)

- **FR-011**: New `Serializer.fs` MUST provide a `serialize` function with signature `StatechartDocument -> string` that produces valid smcat text
- **FR-012**: Serializer MUST reconstruct state declarations with type markers, activities (from `StateNode.Activities`), attributes (from `SmcatAnnotation` entries), and composite state blocks (from `StateNode.Children`)
- **FR-013**: Serializer MUST reconstruct transition arrows with labels in the format `source => target: event [guard] / action;`
- **FR-014**: Serializer MUST use sensible defaults when `SmcatAnnotation` entries are absent: no attributes, no activities, regular state type

#### Generator Refactoring

- **FR-015**: Generator MUST be refactored to produce `Result<StatechartDocument, GeneratorError>` from `StateMachineMetadata`, following the `Wsd.Generator` pattern
- **FR-016**: Generator MUST validate the boxed `Machine` type, extract states from `StateHandlerMap`, order them (initial first), and create `StateDecl` + `TransitionElement` entries
- **FR-017**: Generator MUST produce a `GeneratorError` (type: `UnrecognizedMachineType of typeName: string`) for invalid boxed Machine objects, matching the WSD generator error type

#### Type Cleanup

- **FR-018**: `SmcatDocument`, `SmcatState`, `SmcatTransition`, `SmcatElement`, smcat-specific `ParseResult`, `ParseFailure`, `ParseWarning`, `StateType`, `StateActivity`, and smcat-specific `SourcePosition` MUST be deleted from `Types.fs`
- **FR-019**: `Mapper.fs` MUST be deleted from the project
- **FR-020**: `Types.fs` MUST retain only: `TokenKind`, `Token` (lexer types using `Ast.SourcePosition`), `TransitionLabel`, `SmcatAttribute`, and `inferStateType` (returning `Ast.StateKind`)

#### Build and Test

- **FR-021**: All existing smcat tests MUST pass after being updated to use shared AST types
- **FR-022**: The project MUST build successfully across net8.0, net9.0, and net10.0 target frameworks
- **FR-023**: The cross-format validator MUST work with smcat-produced `StatechartDocument` artifacts without the mapper
- **FR-024**: Round-trip property MUST hold: parsing smcat text, serializing from the resulting `StatechartDocument`, and re-parsing the serialized text produces a structurally equal `StatechartDocument`

### Key Entities

- **StatechartDocument**: Root AST node (from spec 020). After migration, the smcat parser populates this directly with states, transitions, and smcat-specific annotations. The serializer reads this to produce smcat text.
- **SmcatMeta**: Discriminated union in the shared AST carrying smcat-specific data. Existing cases: `SmcatColor`, `SmcatStateLabel`, `SmcatActivity`. These are sufficient for the migration -- no new cases needed.
- **StateNode**: Represents an smcat state. Replaces `SmcatState`. Carries smcat attributes as annotations, activities in the standard `Activities` field.
- **TransitionEdge**: Represents an smcat transition. Replaces `SmcatTransition`. Label components are split into `Event`, `Guard`, `Action` fields.
- **ParseResult**: Uniform result type (from spec 020). Replaces smcat-specific `ParseResult`. Contains `Document` (always present), `Errors`, `Warnings`.

## Success Criteria

### Measurable Outcomes

- **SC-001**: `parseSmcat` returns `Ast.ParseResult` with `StatechartDocument` -- verified by type signature and compilation
- **SC-002**: New `Serializer.serialize` function exists with signature `StatechartDocument -> string` -- verified by compilation
- **SC-003**: `Generator.generate` returns `Result<StatechartDocument, GeneratorError>` from `StateMachineMetadata` -- verified by type signature
- **SC-004**: Round-trip fidelity: parsing smcat text, serializing, and re-parsing produces structurally equal `StatechartDocument` (ignoring source positions) -- verified by round-trip tests
- **SC-005**: `SmcatDocument`, `SmcatState`, `SmcatTransition`, `SmcatElement` and all other deleted types have zero references in the codebase -- verified by text search
- **SC-006**: `Mapper.fs` does not exist in the project -- verified by file system check
- **SC-007**: All existing smcat test scenarios pass (updated to use shared types) -- verified by `dotnet test`
- **SC-008**: Multi-target build succeeds (net8.0, net9.0, net10.0) -- verified by `dotnet build`
- **SC-009**: Cross-format validator works with smcat artifacts without the mapper -- verified by cross-format validation tests

## Assumptions

- The `SmcatMeta` discriminated union in `Ast/Types.fs` already has the three cases needed (`SmcatColor`, `SmcatStateLabel`, `SmcatActivity`). No new cases are needed for this migration.
- The `inferStateType` function in `Types.fs` will be updated to return `Ast.StateKind` instead of smcat-specific `StateType`. The logic is identical since `StateKind` has the same cases.
- The `TransitionLabel` and `SmcatAttribute` types are parser-internal helpers that remain in `Types.fs`. They are not semantic AST types.
- The existing generator's metadata-to-text logic will be split: the metadata-to-AST conversion moves into a refactored `Generator.fs`, and a new `Serializer.fs` handles AST-to-text.
- Lexer tests and label parser tests require no changes since they operate on lexer types (`Token`, `TokenKind`) which are retained.
- The smcat parser's error recovery behavior (best-effort document on parse failure) will map naturally to the shared AST contract where `ParseResult.Document` is always present.
- Standard smcat activities (entry/exit/do) map to `StateNode.Activities` (the shared `StateActivities` record). The `SmcatAnnotation(SmcatActivity(key, value))` case is used for non-standard smcat attributes that don't map to standard AST fields (preserving round-trip fidelity for unknown attribute keys).
