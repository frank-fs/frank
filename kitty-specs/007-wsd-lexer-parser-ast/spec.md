# Feature Specification: WSD Lexer, Parser, and AST

**Feature Branch**: `007-wsd-lexer-parser-ast`
**Created**: 2026-03-07
**Status**: Draft
**GitHub Issue**: #90
**Dependencies**: Frank.Statecharts (#87) core runtime is complete
**Location**: Internal to `src/Frank.Statecharts/` (WSD parser namespace), tests in `test/Frank.Statecharts.Tests/`
**Input**: P0 foundation for the WSD-to-spec pipeline (#57). The existing wsd-gen F# fork is an HTTP client, not a parser. This parser is built from scratch.

## Clarifications

### Session 2026-03-07

- Q: Should the parser return strict all-or-nothing or partial AST with warnings? → A: Partial — return best-effort AST plus warnings/errors list. Valid WSD may not fit Frank.Statecharts goals; partial results allow best effort with warnings for ambiguities while still supporting hard fail-fast on true errors.
- Q: Should the WSD parser be a separate library or part of Frank.Statecharts? → A: Internal to Frank.Statecharts. The parser is only consumed by the statecharts pipeline — no standalone use case exists.

## Background

Web Sequence Diagrams (WSD) is a text-based notation for describing interactions between participants. No formal grammar specification exists; the syntax is defined implicitly by the websequencediagrams.com renderer. This feature produces a from-scratch F# lexer, parser, and typed AST that covers the full WSD syntax, extended with guard annotation support for the Amundsen API design workflow.

When WSD syntax is ambiguous, Amundsen's API design approach is the canonical interpretation guide. Arrows carry semantic meaning: solid arrows represent synchronous/unsafe operations, dashed arrows represent asynchronous/optional operations, and deactivating arrows represent returns.

## User Scenarios & Testing

### User Story 1 - Parse a WSD File into a Typed AST (Priority: P1)

A developer passes a WSD text string to the parser and receives a typed F# AST representing all diagram elements: participants, messages with arrow styles, notes, activations, and metadata directives.

**Why this priority**: This is the core value proposition -- without a correct AST, nothing downstream (spec generation, statechart extraction, validation) can function.

**Independent Test**: Parse Amundsen's onboarding WSD example end-to-end, walk the resulting AST, and verify every element maps to the expected discriminated union case with correct field values.

**Acceptance Scenarios**:

1. **Given** a WSD string with `participant` declarations and `->` messages, **When** parsed, **Then** the AST contains `Participant` nodes with correct names and `Message` nodes with `Solid` style, `Forward` direction, and correct sender/receiver.
2. **Given** a WSD string with `title` and `autonumber` directives, **When** parsed, **Then** the AST contains `Title` and `AutoNumber` directive nodes in source order.
3. **Given** a WSD string with message parameters like `makeMove(position)`, **When** parsed, **Then** the `Message` AST node includes a parameters list containing `"position"`.
4. **Given** a WSD string with mixed arrow types (`->`, `-->`, `->-`, `-->-`), **When** parsed, **Then** each message node has the correct `ArrowStyle` and `Direction` discriminated union values.

---

### User Story 2 - Parse Guard Annotations from Notes (Priority: P2)

A developer uses `note over X: [guard: role=PlayerX]` syntax in their WSD diagram. The parser extracts the guard annotation into structured data in the AST, enabling downstream tools to generate guard conditions for Frank.Statecharts.

**Why this priority**: Guard annotations are the bridge between WSD diagrams and Frank.Statecharts guard evaluation. Without structured guard data, the WSD-to-spec pipeline cannot generate access control rules.

**Independent Test**: Parse the tic-tac-toe WSD example containing guard annotations, verify each `Note` AST node with guard syntax produces a `Guard` record with parsed key-value pairs.

**Acceptance Scenarios**:

1. **Given** `note over Player: [guard: role=PlayerX]`, **When** parsed, **Then** the AST contains a `Note` node with a `Guard` annotation where key is `"role"` and value is `"PlayerX"`.
2. **Given** `note over Player: [guard: state=XTurn, role=PlayerX]`, **When** parsed, **Then** the guard annotation contains both key-value pairs.
3. **Given** `note over Player: This is a regular note`, **When** parsed, **Then** the `Note` node has no guard annotation (plain text content only).
4. **Given** `note over Player: [guard: malformed`, **When** parsed, **Then** a structured failure is produced indicating the unclosed bracket with line/column position.

---

### User Story 3 - Parse Grouping Blocks with Nesting (Priority: P2)

A developer uses `alt`, `opt`, `loop`, `par`, `break`, `critical`, and `ref` grouping blocks in their WSD diagram. The parser correctly nests these blocks to arbitrary depth, preserving conditions and `else` branches.

**Why this priority**: Grouping blocks represent control flow (conditionals, loops, parallelism) that maps directly to statechart semantics. Without correct nesting, complex interaction patterns cannot be modeled.

**Independent Test**: Parse a WSD with `alt` containing a nested `loop` containing an `opt`, verify the AST tree structure reflects the correct nesting with each block's condition text preserved.

**Acceptance Scenarios**:

1. **Given** `alt condition text` ... `else other condition` ... `end`, **When** parsed, **Then** the AST contains a `Group` node of kind `Alt` with two branches, each with its condition text and child elements.
2. **Given** `loop 3 times` ... `opt if available` ... `end` ... `end`, **When** parsed, **Then** the `Loop` group contains a nested `Opt` group, each with correct condition text.
3. **Given** `par` ... `else` ... `else` ... `end`, **When** parsed, **Then** the `Par` group has three parallel branches.
4. **Given** `alt unclosed block` with no matching `end`, **When** parsed, **Then** a structured failure is produced indicating the unclosed group with the opening line number.

---

### User Story 4 - Structured Failure Reports for Invalid WSD (Priority: P3)

A developer submits WSD text that cannot be parsed. Instead of a generic error, the parser produces a structured failure report that includes the line/column of the error, what was expected, what was found, and a corrective example following Amundsen's API design conventions.

**Why this priority**: Actionable error messages dramatically reduce iteration time when authoring WSD diagrams. Corrective examples teach Amundsen conventions.

**Independent Test**: Feed the parser deliberately malformed WSD (unknown arrow syntax, unclosed groups, invalid participant references) and verify each failure report contains position, expectation, and a corrective example.

**Acceptance Scenarios**:

1. **Given** a line with an unrecognized arrow like `->->`, **When** parsed, **Then** the failure report includes line/column, states "unrecognized arrow syntax", and shows the four valid arrow forms as corrective examples.
2. **Given** a message referencing a participant never declared or implicitly introduced, **When** parsed, **Then** the failure report notes the undeclared participant and suggests adding a `participant` declaration.
3. **Given** a completely empty input string, **When** parsed, **Then** the result is an empty diagram AST (not a failure).
4. **Given** multiple errors in the same input, **When** parsed, **Then** all errors are collected (not just the first) up to a configurable maximum.

---

### Edge Cases

- Deeply nested grouping blocks (5+ levels) parse correctly without stack overflow
- Mixed arrow styles within the same group block
- Malformed guard syntax: unclosed brackets, missing equals sign, empty key or value
- Unicode characters in participant names, message text, and note content
- Empty diagrams (zero elements after directives) produce a valid empty AST
- Comment lines (lines starting with `#`) are ignored by the lexer
- Whitespace-only lines between elements are ignored
- Duplicate `participant` declarations: the second is a no-op (participant already exists)
- Participants implicitly declared by first appearance in a message (no explicit `participant` line)
- `title` containing special characters (colons, brackets)
- Messages with no parameters vs. empty parentheses vs. multiple parameters
- Tabs vs. spaces for indentation (both accepted, not significant)
- Windows (`\r\n`) and Unix (`\n`) line endings
- Corrective examples: Each error type produces a corrective example following Amundsen's API design conventions. The error-to-example mappings are: (1) unrecognized arrow syntax — show the four valid forms: `->`, `-->`, `->-`, `-->-`; (2) undeclared participant — suggest adding a `participant X` declaration; (3) unclosed group block — show the matching `end` keyword; (4) malformed guard annotation — show correct `[guard: key=value]` syntax. (The full catalogue is also documented in data-model.md.)

## Requirements

### Functional Requirements

- **FR-001**: System MUST tokenize all WSD syntax elements into a flat token stream: keywords (`participant`, `title`, `autonumber`, `note`, `over`, `left of`, `right of`, `alt`, `opt`, `loop`, `par`, `break`, `critical`, `ref`, `else`, `end`, `as`), arrows (`->`, `-->`, `->-`, `-->-`), identifiers, string literals, colons, parentheses, commas, left bracket, right bracket, equals sign, and newlines. Note: Multi-word keywords (`left of`, `right of`) require lookahead during tokenization.
- **FR-002**: System MUST parse the token stream into a typed F# AST using discriminated unions, producing a `Diagram` record containing an ordered list of `DiagramElement` nodes
- **FR-003**: System MUST support all four arrow types with correct semantic interpretation per Amundsen's approach:
  - `->` (solid forward): synchronous call, activates target
  - `-->` (dashed forward): asynchronous or optional call
  - `->-` (solid deactivating): return from activation
  - `-->-` (dashed deactivating): asynchronous return
- **FR-004**: System MUST parse guard extension syntax from `note` annotations, extracting `[guard: key=value, ...]` into structured key-value pairs on the AST node
- **FR-005**: System MUST parse message parameters from parenthesized argument lists (e.g., `makeMove(position, value)`) into an ordered list of parameter names on the `Message` AST node
- **FR-006**: System MUST parse grouping blocks (`alt`, `opt`, `loop`, `par`, `break`, `critical`, `ref`) with `else` branches and `end` terminators, supporting arbitrary nesting depth
- **FR-007**: System MUST produce structured failure reports for unparseable input, each containing: source position (line and column), description of the error, what was expected, what was found, and a corrective example following Amundsen conventions
- **FR-008**: System MUST collect multiple parse errors and warnings (up to a configurable limit, default: 50) rather than aborting on the first error. Errors represent hard failures; warnings represent valid WSD that may not map cleanly to Frank.Statecharts semantics. The limit is configurable via the `maxErrors` parameter on the `parse` function; the `parseWsd` convenience function applies the default.
- **FR-008a**: System MUST return a best-effort partial AST alongside collected warnings, allowing consumers to use successfully parsed elements even when some constructs produced warnings
- **FR-009**: System MUST handle implicit participant declarations (participants introduced by first appearance in a message without an explicit `participant` line)
- **FR-010**: System MUST ignore comment lines (starting with `#`) and blank lines
- **FR-011**: System MUST handle both Windows (`\r\n`) and Unix (`\n`) line endings
- **FR-012**: System MUST parse `title` and `autonumber` directives into corresponding AST nodes
- **FR-013**: System MUST parse `note` elements in all positions: `note over`, `note left of`, `note right of`, with multi-word content after the colon

### Key Entities

- **Token**: Lexer output -- keyword, arrow, identifier, string literal, colon, parentheses, comma, newline, EOF, with source position
- **Diagram**: Top-level AST node containing title (optional), autonumber flag, participant list, and ordered element list
- **DiagramElement**: Discriminated union -- `ParticipantDecl`, `Message`, `Note`, `Group`, `Directive`
- **Message**: Sender, receiver, arrow style (`Solid`/`Dashed`), direction (`Forward`/`Deactivating`), label text, and parameter list
- **Note**: Position (`Over`/`LeftOf`/`RightOf`), target participant, content text, and optional guard annotation
- **GuardAnnotation**: List of key-value string pairs extracted from `[guard: ...]` syntax
- **Group**: Kind (`Alt`/`Opt`/`Loop`/`Par`/`Break`/`Critical`/`Ref`), condition text, ordered list of branches (each branch has optional condition and child elements)
- **ParseFailure**: Source position (line, column), error description, expected/found descriptions (human-readable strings), corrective example text
- **ParseResult**: Record containing a `Diagram` (always present, possibly partial -- never optional), a `ParseFailure list` for hard errors, and a `ParseWarning list` for ambiguities or WSD constructs that are valid but not a fit for Frank.Statecharts. Consumers check errors for fail-fast; warnings allow graceful degradation.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Parser correctly handles Amundsen's onboarding WSD example end-to-end, producing an AST where every element can be verified by walking the AST and confirming every element's field values match the original input's participants, messages, arrow styles, directions, labels, parameters, notes, guards, and grouping blocks
- **SC-002**: Parser correctly handles the tic-tac-toe WSD example with guard extensions, producing `GuardAnnotation` values on all annotated notes
- **SC-003**: All four WSD arrow types produce correct `Message` AST nodes with the expected `ArrowStyle` and `Direction` values
- **SC-004**: Grouping blocks nest correctly to at least 5 levels deep without error or performance degradation
- **SC-005**: Invalid input produces failure reports where every report includes a line/column position and a corrective example
- **SC-006**: Library compiles under multi-target (net8.0/net9.0/net10.0) with `dotnet build`
- **SC-007**: Parser handles WSD inputs up to 1000 lines without measurable allocation pressure (no intermediate string concatenation in hot paths)
