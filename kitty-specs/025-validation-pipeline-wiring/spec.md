# Feature Specification: Validation Pipeline End-to-End Wiring

**Feature Branch**: `025-validation-pipeline-wiring`
**Created**: 2026-03-16
**Status**: Draft
**Dependencies**: #113 (smcat shared AST migration), #114 (SCXML shared AST migration), #115 (ALPS shared AST migration). All three must be complete before implementation begins -- each parser must return `ParseResult` directly. Also depends on spec 021 (cross-format validator engine, #112) which is already implemented and passing.
**Consumed by**: frank-cli `validate` command (#94) -- the CLI delegates to the pipeline rather than orchestrating parse+validate itself
**Parent issue**: #57 (statechart spec pipeline)
**GitHub issue**: #117
**Location**: `Pipeline.fs` added to `src/Frank.Statecharts/Validation/`, end-to-end tests in `test/Frank.Statecharts.Tests/Validation/`
**Input**: User description: "Cross-format validation pipeline: end-to-end parse-then-validate wiring"

## Background

The cross-format validator engine (spec 021, #112) works correctly at the `StatechartDocument` level -- given `FormatArtifact` records, it detects state name mismatches, event mismatches, casing issues, orphan targets, and other inconsistencies. However, there is no end-to-end wiring that takes format source text, parses each format, and feeds the results into the validator.

Currently, all integration tests construct `StatechartDocument` by hand. No test parses actual format text through the full pipeline. There is no orchestration function that ties parsing to validation.

This feature provides a library-level orchestration module that accepts raw format source text, dispatches to the correct parser, wraps parse results as format-tagged artifacts, runs the validator with both self-consistency and cross-format rules, and returns a unified `ValidationReport`. It also provides end-to-end integration tests that parse real format text (the same tic-tac-toe state machine in multiple formats) to verify zero failures for consistent inputs and correct failure detection for intentional mismatches.

The pipeline is designed for the post-migration parser interface where all format parsers return `Ast.ParseResult` directly (no mapper step). The uniform function signature is `string -> ParseResult` for each format (WSD, smcat, SCXML, ALPS).

## User Scenarios & Testing

### User Story 1 - Parse and Validate Multiple Format Sources in One Call (Priority: P1)

A developer has the same state machine described in multiple format source texts (e.g., WSD text and smcat text). They call a single pipeline function with a list of `(FormatTag * string)` pairs. The pipeline parses each source, runs cross-format validation, and returns a `PipelineResult` without the developer needing to know how to call each individual parser or construct `FormatArtifact` records.

**Why this priority**: This is the core value of the pipeline -- eliminating the manual orchestration that currently requires calling each parser separately, constructing artifacts, assembling rules, and invoking the validator. Without this, the cross-format validator is usable only by code that understands the internal architecture.

**Independent Test**: Provide WSD and smcat source text describing the same tic-tac-toe state machine. Call the pipeline function. Verify the returned report has zero failures and all applicable cross-format checks passed.

**Acceptance Scenarios**:

1. **Given** WSD source text and smcat source text that describe the same state machine with identical states and transitions, **When** the pipeline is called with `[(Wsd, wsdSource); (Smcat, smcatSource)]`, **Then** the returned result has zero validation failures and all cross-format checks for the Wsd-Smcat pair passed.
2. **Given** WSD source text with states `idle`, `playing`, `gameOver` and smcat source text with states `idle`, `playing`, `finished` (mismatch on the third state), **When** the pipeline is called, **Then** the result contains failures identifying the state name disagreement between WSD and smcat, including the missing state names.
3. **Given** source text for all four currently supported formats (WSD, smcat, SCXML, ALPS) describing the same state machine consistently, **When** the pipeline is called, **Then** the report has zero failures, zero skipped checks, and all pairwise cross-format rules executed.

---

### User Story 2 - Handle Parse Errors Gracefully in the Pipeline (Priority: P1)

A developer provides format source text that contains a syntax error (e.g., malformed SCXML). The pipeline should still return a useful result: the parse errors are surfaced, and validation runs on whatever partial document the parser produced (since `ParseResult.Document` is always populated, even on parse failure, per the shared AST contract).

**Why this priority**: Real-world usage will frequently involve source text with errors. The pipeline must not crash or silently discard formats when parsing fails; it must provide actionable diagnostics.

**Independent Test**: Provide valid WSD source and syntactically invalid SCXML source. Call the pipeline. Verify the result includes parse errors for the SCXML source and validation still runs on the best-effort document.

**Acceptance Scenarios**:

1. **Given** valid WSD source text and SCXML source text with a missing closing tag, **When** the pipeline is called, **Then** the result includes parse errors attributed to the SCXML format, and the WSD artifact is still validated (self-consistency checks run).
2. **Given** source text that is completely unparseable for its format (e.g., random text tagged as SCXML), **When** the pipeline is called, **Then** parse errors are reported and validation runs on the empty best-effort document, producing no false failures from the empty artifact.
3. **Given** a single source with parse warnings (not errors), **When** the pipeline is called, **Then** the warnings are included in the result and validation runs normally on the fully parsed document.

---

### User Story 3 - Validate a Single Format Source End-to-End (Priority: P2)

A developer has only one format source text (e.g., only a WSD file). They call the pipeline with a single `(FormatTag * string)` pair. The pipeline parses it, runs self-consistency validation (orphan targets, duplicate states, required fields), and returns a result. Cross-format rules that require other formats are skipped.

**Why this priority**: Single-format validation is the simplest use case and provides immediate value even when only one parser is available or only one format artifact exists for a resource.

**Independent Test**: Provide a single WSD source text with an intentional orphan transition target. Call the pipeline. Verify the report contains the orphan target failure and that cross-format checks are skipped.

**Acceptance Scenarios**:

1. **Given** a single WSD source text with all transition targets referencing valid states, **When** the pipeline is called with `[(Wsd, wsdSource)]`, **Then** the report has zero failures and all self-consistency checks passed, with cross-format checks skipped.
2. **Given** a single WSD source text where a transition targets state "review" which does not exist, **When** the pipeline is called, **Then** the report contains an orphan target failure identifying "review" and the WSD format.
3. **Given** only ALPS source text, **When** the pipeline is called, **Then** self-consistency rules run against the ALPS artifact and all cross-format rules requiring other formats are skipped with appropriate reasons.

---

### User Story 4 - CLI Delegation to the Pipeline (Priority: P2)

The frank-cli `validate` command (#94) can call the pipeline function to validate statechart format sources without implementing its own parse-and-validate logic. The CLI reads format files, determines their format tags, and passes `(FormatTag * string)` pairs to the pipeline. The CLI is responsible for presentation of the returned result.

**Why this priority**: This closes the loop between the library-level pipeline and the developer-facing CLI tool. Without this integration point, the CLI would need to duplicate the orchestration logic.

**Independent Test**: Verify that the pipeline's public API is callable from outside the `Frank.Statecharts` assembly (public module and types), and that the return type (`PipelineResult`) contains all the information the CLI needs to render output (parse errors, parse warnings, and the `ValidationReport`).

**Acceptance Scenarios**:

1. **Given** the pipeline module exists with a public `validateSources` function, **When** called from external code (e.g., frank-cli), **Then** it returns a `PipelineResult` containing parse diagnostics per format and the unified `ValidationReport`.
2. **Given** the CLI has read WSD and SCXML files from disk, **When** it constructs `[(Wsd, wsdContent); (Scxml, scxmlContent)]` and calls the pipeline, **Then** it receives a result it can format as text or JSON output without needing to understand parser internals.

---

### User Story 5 - End-to-End Test with Real Format Text (Priority: P1)

Integration tests parse the same tic-tac-toe state machine expressed in real WSD, smcat, SCXML, and ALPS format text (not hand-constructed AST). The tests verify that the pipeline produces zero validation failures for consistent inputs and detects specific failures for intentionally mismatched inputs.

**Why this priority**: This is the acceptance test that proves the entire pipeline works from text to report. It exercises real parsers, real AST conversion, and real validation rules in combination. Without these tests, the pipeline could pass unit tests while failing on actual format text due to parser quirks or AST mapping differences.

**Independent Test**: Embed tic-tac-toe state machine source text in each format as test constants. Parse all four through the pipeline. Assert zero failures. Then modify one format's source to introduce a mismatch and assert the correct failure is detected.

**Acceptance Scenarios**:

1. **Given** real WSD, smcat, SCXML, and ALPS source text all describing the same tic-tac-toe state machine (states: `idle`, `playerX`, `playerO`, `gameOver`; events: `start`, `move`, `win`), **When** the pipeline is called with all four, **Then** the report has zero failures.
2. **Given** the same consistent sources but with the smcat source modified to rename state `gameOver` to `finished`, **When** the pipeline is called, **Then** the report contains cross-format failures identifying `gameOver` missing from smcat and `finished` missing from all other formats.
3. **Given** the same consistent sources but with the ALPS source modified to remove the `start` event, **When** the pipeline is called, **Then** the report contains cross-format failures for the missing event between ALPS and each other format.

---

### Edge Cases

- Empty source list (no formats provided) produces a valid `PipelineResult` with an empty `ValidationReport` (zero checks, zero failures)
- Duplicate format tags in the input (e.g., two WSD sources) produces an error result indicating that each format may appear at most once. Note: Duplicate format checking is performed first (input validation), before any parsing begins. Once duplicates are rejected, unsupported format checking occurs for the remaining unique tags.
- Unsupported `FormatTag` value (e.g., `XState`) that has no registered parser produces a parse error for that format rather than crashing
- A format source that is an empty string produces a parse result with appropriate errors or warnings (parser-dependent behavior), and validation runs on the resulting empty document
- Very large source text (thousands of lines) does not cause excessive memory allocation or timeout -- the pipeline delegates to existing parsers that already handle large inputs
- When all sources fail to parse, the pipeline still returns a valid result with parse errors and a validation report against the best-effort empty documents

## Requirements

### Functional Requirements

- **FR-001**: System MUST provide a `Pipeline` module in the `Frank.Statecharts.Validation` namespace with a public `validateSources` function
- **FR-002**: The `validateSources` function MUST accept a list of `(FormatTag * string)` pairs where `FormatTag` identifies the format and `string` is the raw source text
- **FR-003**: The `validateSources` function MUST return a `PipelineResult` record containing: per-format parse diagnostics (errors and warnings) and the unified `ValidationReport`
- **FR-004**: System MUST dispatch to the correct parser based on the `FormatTag`: `Wsd` dispatches to the WSD parser, `Smcat` to the smcat parser, `Scxml` to the SCXML parser, `Alps` to the ALPS parser
- **FR-005**: System MUST assume all parsers return `Ast.ParseResult` directly (post-migration interface: `string -> ParseResult`), with no mapper step
- **FR-006**: System MUST wrap each successfully parsed `ParseResult.Document` as a `FormatArtifact` with the corresponding `FormatTag`
- **FR-007**: System MUST run both `SelfConsistencyRules.rules` and `CrossFormatRules.rules` against all produced artifacts via `Validator.validate`
- **FR-008**: System MUST include parse errors and warnings from each format parser in the `PipelineResult`, attributed to the format that produced them
- **FR-009**: System MUST handle parse failures gracefully -- when a parser returns errors, the best-effort `ParseResult.Document` is still used for validation (per the shared AST contract that `Document` is always populated)
- **FR-010**: System MUST reject duplicate `FormatTag` values in the input list and return an error indicating which format was duplicated
- **FR-011**: System MUST return a valid result (not throw an exception) for an empty input list, producing a `ValidationReport` with zero checks
- **FR-012**: System MUST report an error for `FormatTag` values that have no registered parser (currently `XState` has no parser), rather than crashing. When an unsupported format is encountered, the pipeline adds `UnsupportedFormat` to errors and continues processing remaining formats rather than aborting the entire pipeline.
- **FR-013**: The `Pipeline` module MUST be public (not `internal`) so that external consumers (e.g., frank-cli) can call it
- **FR-014**: System MUST allow callers to optionally provide additional custom `ValidationRule` values beyond the built-in self-consistency and cross-format rules

### Key Entities

- **PipelineResult**: Top-level result from the pipeline containing: a list of `FormatParseResult` records (one per input format) with parse errors and warnings attributed to each format, and the unified `ValidationReport` from running all applicable validation rules. Also contains an optional `PipelineError` when input validation fails (e.g., duplicate formats).
- **FormatParseResult**: Per-format parse outcome containing: the `FormatTag`, parse errors (`ParseFailure list`), parse warnings (`ParseWarning list`), and whether parsing succeeded (had zero errors).
- **PipelineError**: Discriminated union for pipeline-level errors (not parse errors): `DuplicateFormat` when the same `FormatTag` appears twice in the input, `UnsupportedFormat` when no parser exists for a `FormatTag`.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Pipeline correctly parses and validates consistent tic-tac-toe source text in all four formats (WSD, smcat, SCXML, ALPS) with zero validation failures and zero false positives
- **SC-002**: Pipeline correctly detects all intentionally introduced mismatches in end-to-end tests using real format source text, with zero false negatives
- **SC-003**: Pipeline returns actionable parse error diagnostics when given malformed source text, attributed to the correct format, without crashing
- **SC-004**: Pipeline completes parse-and-validate of four format sources (each ~50 lines) in under 2 seconds
- **SC-005**: Pipeline's public API is callable from outside `Frank.Statecharts` (no `internal` visibility barrier) and returns all information needed by frank-cli to render results
- **SC-006**: End-to-end integration tests exercise real parsers (not hand-constructed AST) and pass across all supported target platforms
- **SC-007**: Pipeline handles edge cases (empty input, duplicate formats, unsupported formats, empty source strings) without throwing exceptions

## Assumptions

- All format parsers will return `Ast.ParseResult` directly by the time this feature is implemented. This depends on #113 (smcat), #114 (SCXML), and #115 (ALPS) completing their shared AST migrations. The WSD parser already returns `Ast.ParseResult` directly.
- The `ParseResult.Document` field is always populated (never null/empty by accident), even when parse errors occur. This is the established contract from spec 020.
- The `XState` format tag exists in the `FormatTag` discriminated union but has no parser implementation. The pipeline treats it as an unsupported format and reports an error rather than crashing.
- The pipeline does not read files from disk. It receives source text as strings. File I/O is the caller's responsibility (e.g., frank-cli reads files and passes content to the pipeline).
- The pipeline does not format or present results. It returns structured data (`PipelineResult`). Presentation is the CLI's concern (#94).
- The pipeline uses the existing `SelfConsistencyRules.rules` and `CrossFormatRules.rules` from the validator engine (spec 021). It does not define new validation rules.
- The tic-tac-toe state machine used in end-to-end tests has states `idle`, `playerX`, `playerO`, `gameOver` and events `start`, `move`, `win` -- matching the existing test fixtures in the validator integration tests.
- The pipeline module is added to the existing `Frank.Statecharts` project and compiled after the `Validation/Validator.fs` file in the `.fsproj` file ordering.
