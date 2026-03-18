# Feature Specification: Cross-Format Validation Pipeline and AST Merge

**Feature Branch**: `030-cross-format-validation-pipeline`
**Created**: 2026-03-18
**Status**: Draft
**Input**: Create missing test cases and additional support for multiple formats contributing to a single StatechartDocument. Pipeline orchestration for validate-then-merge workflow.
**Closes**: #117

## Clarifications

### Session 2026-03-18

- Q: Should merge use exact or fuzzy event/state name matching? → A: Merge function uses exact match only (pure, deterministic). Validator enhanced with near-match detection (fuzzy warnings with string distance metrics). Pipeline supports a reconciliation step between validate and merge where the caller (Claude Code, CLI) can resolve near-misses before merging. Fuzzy warnings included in this spec scope.

## Background

The cross-format validator (#112) works correctly at the `StatechartDocument` level, detecting state name mismatches, event mismatches, casing issues, and orphan targets. The validation pipeline (`Pipeline.validateSources`) parses format sources and runs validation rules. However, the original purpose of cross-format validation was to support **merging** multiple format ASTs into one unified `StatechartDocument` — not just comparing them. Each format contributes complementary data:

- **SCXML**: Authoritative for structure — hierarchy, parallelism, history, executable content, data model
- **smcat**: Authoritative for topology — states, transitions, composite nesting, visual attributes
- **WSD**: Authoritative for workflow — transition ordering, participant sequence, guard extensions
- **ALPS**: Authoritative for vocabulary — semantic descriptors, transition types, documentation (contributes only annotations, never structure)

The pipeline currently validates but cannot merge. Additionally, end-to-end integration tests that parse real format text (rather than hand-constructed ASTs) are missing. The ALPS XML parser is not yet wired into the pipeline's format dispatch.

## User Scenarios & Testing

### User Story 1 - Merge Multiple Format ASTs (Priority: P1)

A developer parses the same state machine in multiple formats (e.g., WSD for topology + ALPS for vocabulary) and merges them into one unified `StatechartDocument` that carries both the structural information and the semantic annotations.

**Why this priority**: Merging is the reason the cross-format validation exists. Without merge, the pipeline validates but produces no unified output.

**Independent Test**: Parse a state machine in WSD and ALPS, merge the ASTs, verify the merged document has WSD's states/transitions AND ALPS's semantic annotations on matching nodes.

**Acceptance Scenarios**:

1. **Given** a WSD document and an ALPS document describing the same state machine, **When** merged, **Then** the unified `StatechartDocument` contains `StateNode` entries from WSD with `AlpsAnnotation` entries accumulated from ALPS.
2. **Given** an SCXML document and a WSD document, **When** merged, **Then** SCXML's `StateKind` (e.g., `Parallel`) takes precedence over WSD's for conflicting structural fields.
3. **Given** two formats with annotations on the same state, **When** merged, **Then** annotations from both formats coexist in the state's `Annotations` list (DU discriminates by format).

---

### User Story 2 - Format Priority for Structural Conflicts (Priority: P1)

When multiple formats contribute conflicting structural data (e.g., `StateKind`, `Children`, `Activities`), the merge resolves conflicts using a defined priority ordering: SCXML > smcat > WSD. ALPS never contributes structure.

**Why this priority**: Without clear precedence, the merge would be ambiguous for conflicting structural fields.

**Independent Test**: Parse a state as `Regular` in WSD and `Parallel` in SCXML, merge, verify the merged state has `Kind = Parallel`.

**Acceptance Scenarios**:

1. **Given** SCXML defines a state as `Parallel` and WSD defines the same state as `Regular`, **When** merged, **Then** the merged state has `Kind = Parallel` (SCXML wins).
2. **Given** smcat defines composite children and WSD does not, **When** merged, **Then** the merged state has the smcat children (smcat wins over WSD for structure).
3. **Given** ALPS defines a state with `AlpsDocumentation`, **When** merged with WSD, **Then** the ALPS annotation is added but no structural fields are overridden.

---

### User Story 3 - Validate Before Merge (Priority: P1)

A developer runs validation first, checks the report for consistency issues, and only merges if validation passes. The functions are separate and composable.

**Why this priority**: Merging inconsistent documents silently would produce corrupt unified ASTs. Validate-then-merge is the safety pattern.

**Independent Test**: Parse intentionally mismatched formats (different state names), validate, verify validation fails, verify merge is not attempted.

**Acceptance Scenarios**:

1. **Given** consistent formats, **When** validated then merged, **Then** validation passes and merge produces a unified document.
2. **Given** inconsistent formats (state name mismatch), **When** validated, **Then** validation report contains failures and the developer can decide whether to proceed.
3. **Given** the merge function called directly (without validation), **Then** it produces a best-effort merge (no validation gate enforced by the function itself — the caller is responsible).

---

### User Story 4 - ALPS XML in Pipeline (Priority: P2)

A developer provides ALPS profiles in XML format (not just JSON). The pipeline dispatches to the XML parser correctly.

**Why this priority**: ALPS XML support was added in spec 029 but isn't wired into the pipeline's `FormatTag` dispatch.

**Independent Test**: Call `validateSources` with an ALPS XML source, verify it parses correctly.

**Acceptance Scenarios**:

1. **Given** a source tagged as ALPS XML, **When** passed to the pipeline, **Then** it dispatches to `Alps.XmlParser.parseAlpsXml` and produces the same AST as the JSON parser for equivalent input.

---

### User Story 5 - End-to-End Integration Tests (Priority: P1)

Integration tests parse real format text in multiple formats, run cross-format validation, and verify correct behavior for both consistent and inconsistent inputs.

**Why this priority**: Existing validator tests construct ASTs by hand. End-to-end tests prove the full pipeline works with real parsers.

**Independent Test**: Parse the same tic-tac-toe state machine in WSD + smcat + SCXML + ALPS, validate, verify zero failures. Then parse intentionally mismatched formats and verify correct failure detection.

**Acceptance Scenarios**:

1. **Given** the same state machine described in WSD, smcat, SCXML, and ALPS JSON, **When** all four are validated together, **Then** zero validation failures (states, events, and transitions are consistent).
2. **Given** a WSD document with state "Idle" and an smcat document with state "idle" (casing mismatch), **When** validated, **Then** the validator detects and reports the casing issue.
3. **Given** a WSD document with a transition event "start" absent from the ALPS document, **When** validated, **Then** the validator reports the event mismatch.

---

### User Story 6 - Near-Match Detection in Validator (Priority: P1)

A developer validates formats where state or event names are similar but not identical (e.g., "start" vs "startOnboarding"). The validator reports these as warnings with similarity scores and suggestions, enabling the caller (Claude Code, CLI) to auto-correct or prompt the user before merging.

**Why this priority**: Without near-match detection, the validator only reports exact mismatches. Near-matches are the most common real-world issue when different team members author different format sources.

**Independent Test**: Validate two formats with "start" vs "startOnboarding" — verify a near-match warning is produced with both names and a similarity score.

**Acceptance Scenarios**:

1. **Given** WSD with event "startOnboarding" and smcat with event "start", **When** validated, **Then** a near-match warning is reported with both names, the format pair, and a similarity score.
2. **Given** formats with identical names, **When** validated, **Then** no near-match warnings are produced (only exact-match checks).
3. **Given** formats with completely different names (e.g., "login" vs "shutdown"), **When** validated, **Then** no near-match warning is produced (similarity below threshold).

---

### Edge Cases

- What happens when merging a single format source? The merge returns the document as-is (no-op merge).
- What happens when merging formats with no overlapping states? The merge produces a document with the union of all states and transitions.
- What happens when ALPS contributes a state identifier not present in any structural format? It's added as a `StateNode` with `Kind = Regular` and the ALPS annotations.
- What happens when the same transition appears in multiple formats with different guards? Guards from the highest-priority structural format win; guard-related annotations from all formats accumulate.
- What happens when `FormatTag.Alps` is used with XML content? The pipeline should detect this (JSON parse fails) and report a clear error suggesting `AlpsXml` tag.

## Requirements

### Functional Requirements

- **FR-001**: A new `Pipeline.mergeSources` function MUST accept a list of `(FormatTag * string)` pairs and return a merged `StatechartDocument`.
- **FR-002**: The merge MUST follow priority ordering for structural fields: SCXML > smcat > WSD. ALPS MUST NOT override structural fields.
- **FR-003**: The merge MUST accumulate annotations from all contributing formats on matching nodes (matched by identifier for states, by source+target+event for transitions).
- **FR-004**: States present in one format but not others MUST be included in the merged document.
- **FR-005**: Transitions present in one format but not others MUST be included in the merged document.
- **FR-006**: `Pipeline.validateSources` and `Pipeline.mergeSources` MUST be separate, independently callable functions.
- **FR-007**: The pipeline MUST support ALPS XML as a format source (new `FormatTag` case or extended dispatch).
- **FR-008**: End-to-end integration tests MUST parse real format text (not hand-constructed ASTs) through the full pipeline.
- **FR-009**: Integration tests MUST cover consistent multi-format input (zero validation failures expected).
- **FR-010**: Integration tests MUST cover intentionally inconsistent input (correct failure detection expected).
- **FR-011**: The merge function MUST use exact string matching for state identifiers and transition (source, target, event) triples. No fuzzy matching in the merge itself.
- **FR-012**: The validator MUST detect near-matches between state names and event names across formats using string distance metrics, reporting them as warnings with suggestions (e.g., "'start' in smcat is a near-match for 'startOnboarding' in WSD").
- **FR-013**: Near-match warnings MUST include the format pair, the mismatched identifiers, and a similarity score so the caller (CLI, Claude Code) can offer corrections.
- **FR-014**: All changes MUST compile across net8.0, net9.0, and net10.0 target frameworks.
- **FR-015**: All existing validation tests MUST continue to pass.

### Key Entities

- **Pipeline.mergeSources**: New function merging multiple parsed `StatechartDocument` values into one unified document.
- **FormatTag (extended)**: Add `AlpsXml` case for ALPS XML dispatch, or extend `Alps` to support both representations.
- **MergePriority**: The structural precedence ordering: SCXML > XState > smcat > WSD > ALPS (annotations only).

## Success Criteria

### Measurable Outcomes

- **SC-001**: Merging WSD + ALPS sources produces a unified document with both topology and semantic annotations — zero annotation loss.
- **SC-002**: Merging SCXML + WSD where structural fields conflict always uses the SCXML value.
- **SC-003**: End-to-end test with 4 consistent format sources produces zero validation failures.
- **SC-004**: End-to-end test with intentional mismatches produces correct failure count and descriptions.
- **SC-005**: `dotnet build` and `dotnet test` pass across all target frameworks with zero regressions.

## Assumptions

- The existing `Pipeline.validateSources` function is unchanged — merge is additive, not a modification.
- The merge function does NOT enforce validation — the caller is responsible for validating first. `mergeSources` produces a best-effort merge regardless.
- For end-to-end tests, equivalent representations of the same state machine in each format will be maintained as test fixtures (inline strings or golden files).
- XState is wired into the pipeline via `XState.Deserializer.deserialize`. Its merge priority is between SCXML and smcat (SCXML > XState > smcat) per Harel's assessment — XState is an executable format like SCXML, not a descriptive format like smcat/WSD.
- State matching is by `Identifier` string (exact match) in the merge function. The validator separately checks for near-matches using string distance metrics.
- Transition matching is by `(Source, Target, Event)` triple (exact match) in the merge function.
- String distance for near-match detection uses a standard metric (e.g., Jaro-Winkler or Levenshtein). The threshold for reporting near-matches is configurable but defaults to a reasonable value (e.g., 0.7 similarity).
