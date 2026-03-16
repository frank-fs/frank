# Feature Specification: Cross-Format Statechart Validator

**Feature Branch**: `021-cross-format-validator`
**Created**: 2026-03-15
**Status**: Draft
**Dependencies**: Shared Statechart AST (spec 020, #87) must be complete. Format parser/generator specs: ALPS (spec 011, #97), smcat (spec 013, #100), WSD Generator (spec 017, #91), SCXML (spec 018, #98). Carved out of spec 017.
**Consumed by**: frank-cli validate (#94) -- this spec does not provide a CLI interface
**Parent issue**: #57 (statechart spec pipeline)
**Location**: Internal to `src/Frank.Statecharts/Validation/`, tests in `test/Frank.Statecharts.Tests/`
**Input**: Cross-format validation of statechart spec artifacts (WSD, ALPS, SCXML, smcat, XState JSON)

## Background

The Frank.Statecharts spec pipeline (#57) supports five statechart notation formats: WSD, ALPS, SCXML, smcat, and XState JSON. Each format has its own parser and generator (defined in separate specs). All parsers populate a shared AST (defined in spec 020) that represents statechart concepts in a format-agnostic way: states, transitions, guards, events, actions, data/context, hierarchy, and annotations.

Different formats express different aspects of a statechart. When multiple format artifacts exist for the same resource, inconsistencies between them indicate specification errors. For example, an XState event name that does not appear as an ALPS transition descriptor suggests a mismatch between the behavioral model and the semantic profile.

This feature provides a pluggable validation orchestrator that checks consistency across statechart artifacts in any combination of formats. The validator defines a `ValidationRule` contract (a function signature) that each format module implements and registers in its own code. The validator orchestrates registered rules, collects all failures without aborting early, and returns a structured `ValidationReport` data structure. Presentation and formatting of the report is entirely the CLI's concern (#94).

The validator performs two categories of checks:

1. **Single-format self-consistency**: When only one format artifact is available, the validator checks structural validity (e.g., all transition targets reference states that exist within the same artifact) and AST conformance (e.g., required shared AST fields are populated correctly by the parser).

2. **Cross-format invariant checks**: When multiple format artifacts are available, the validator checks that corresponding elements agree across formats (e.g., SCXML state IDs match XState state names, ALPS transition descriptors match XState events).

The pluggable architecture follows Frank's design philosophy of distributed ownership: each format module knows best what invariants apply to its format and registers those checks with the validator. The validator itself has no knowledge of specific formats.

## User Scenarios & Testing

### User Story 1 - Validate Single-Format Self-Consistency (Priority: P1)

A developer has a single format artifact (e.g., an SCXML file parsed into the shared AST). They run validation and receive a report confirming that the artifact is internally consistent: all transition targets reference states that exist, all required AST fields are populated, and no structural anomalies are present.

**Why this priority**: Single-format validation is the minimum useful scope. It works even when only one format parser is available, providing immediate value before the full multi-format pipeline is complete.

**Independent Test**: Parse an SCXML file into the shared AST, register the SCXML self-consistency rule, run the validator, and verify the report shows all checks passed. Then introduce a transition targeting a nonexistent state, re-validate, and verify the report contains a failure identifying the orphan target.

**Acceptance Scenarios**:

1. **Given** a single SCXML artifact with all transition targets referencing valid states, **When** the validator runs with the SCXML self-consistency rule registered, **Then** the report shows all checks passed with zero failures.
2. **Given** a single SCXML artifact with a transition targeting state "review" which does not exist, **When** the validator runs, **Then** the report contains a failure identifying the orphan target "review", the format involved (SCXML), and the entity type (transition target).
3. **Given** a single smcat artifact where a state has no incoming or outgoing transitions, **When** the validator runs, **Then** the report contains a warning (not a failure) about the isolated state, since isolated states may be intentional.
4. **Given** a single artifact from any format where a required AST field (e.g., state identifier) is empty, **When** the validator runs, **Then** the report contains a failure identifying the missing field and the format that produced it.

---

### User Story 2 - Validate Cross-Format Consistency Between Two Formats (Priority: P1)

A developer has artifacts from two formats (e.g., ALPS and XState JSON) for the same resource. They run validation and receive a report confirming that the two formats agree on shared concepts, or identifying specific mismatches such as an XState event name that does not appear as an ALPS transition descriptor.

**Why this priority**: Cross-format validation between two formats is the core value proposition of this feature. It catches specification drift between format artifacts that are supposed to describe the same state machine.

**Independent Test**: Create shared AST instances representing ALPS and XState artifacts for the same tic-tac-toe state machine. Register the ALPS-vs-XState cross-format rule. Run the validator and verify all checks pass. Then remove an event from the XState artifact, re-validate, and verify the failure report identifies the missing event by name.

**Acceptance Scenarios**:

1. **Given** ALPS and XState artifacts where every XState event name exists as an ALPS transition descriptor, **When** the validator runs with the ALPS-vs-XState rule registered, **Then** the report shows all cross-format checks passed.
2. **Given** ALPS and XState artifacts where XState has an event "submitMove" that has no corresponding ALPS transition descriptor, **When** the validator runs, **Then** the report contains a failure identifying: formats involved (ALPS, XState), entity type (event/transition descriptor), expected value ("submitMove" should exist in ALPS), and the actual state (missing from ALPS).
3. **Given** ALPS and XState artifacts where ALPS has a transition target "gameOver" that does not exist as an XState state, **When** the validator runs, **Then** the report contains a failure identifying the missing state in XState.

---

### User Story 3 - Validate Across All Available Formats (Priority: P1)

A developer has artifacts from three or more formats for the same resource. They run validation and receive a comprehensive report covering all pairwise and collective checks. The validator runs every registered rule that applies to the available format combination and skips rules that require formats not present.

**Why this priority**: Full multi-format validation is the end-state goal. A developer maintaining a complete spec pipeline (WSD + ALPS + SCXML + smcat + XState) needs confidence that all artifacts are consistent.

**Independent Test**: Create shared AST instances for all five formats representing the same state machine. Register all cross-format rules. Run the validator and verify the report includes checks for every applicable format pair, with skipped checks clearly marked for pairs that were not checked.

**Acceptance Scenarios**:

1. **Given** artifacts from all five formats that are fully consistent, **When** the validator runs with all rules registered, **Then** the report shows all checks passed and no checks were skipped (since all formats are present).
2. **Given** artifacts from SCXML, XState, and smcat (but not ALPS or WSD), **When** the validator runs, **Then** checks requiring ALPS or WSD are marked as skipped (not failed), and all applicable checks between SCXML, XState, and smcat are executed.
3. **Given** artifacts where SCXML state IDs match XState state names but smcat states include an extra state "maintenance" not in SCXML, **When** the validator runs, **Then** the report contains a failure for the smcat-vs-SCXML check and passes for the SCXML-vs-XState check.

---

### User Story 4 - Register Format-Specific Validation Rules (Priority: P1)

A format module developer (e.g., working on the SCXML parser spec) defines a validation rule as a function matching the `ValidationRule` contract. They register it with the validator. The rule is automatically included in future validation runs when the appropriate format artifacts are present.

**Why this priority**: The pluggable architecture is what makes this validator sustainable. Without a clear registration mechanism, adding new format checks would require modifying the validator module directly, violating distributed ownership.

**Independent Test**: Define a custom validation rule that checks a trivial invariant (e.g., "all states have non-empty identifiers"), register it, run the validator, and verify the rule's checks appear in the report.

**Acceptance Scenarios**:

1. **Given** a validation rule defined as a function matching the `ValidationRule` contract, **When** registered with the validator, **Then** the rule is invoked during validation when its required format artifacts are present.
2. **Given** a validation rule that requires both SCXML and XState artifacts, **When** only SCXML is present, **Then** the rule is skipped and the report records it as skipped with a reason (missing XState artifact).
3. **Given** multiple rules registered from different format modules, **When** the validator runs, **Then** all applicable rules execute and their results are aggregated into a single report.

---

### User Story 5 - Receive Actionable Diagnostic Information (Priority: P2)

A developer receives a validation failure and can immediately understand what went wrong: which formats disagree, what type of entity is mismatched, what values were expected versus found, and where in the source to look. The diagnostic is sufficient to fix the issue without additional investigation.

**Why this priority**: Actionable diagnostics are what make validation results useful in practice. Without them, developers know something is wrong but not what or where.

**Independent Test**: Trigger a cross-format failure (e.g., SCXML state "waiting" vs XState state "pending") and verify the failure record contains the format names, entity type, expected value, actual value, and a human-readable description.

**Acceptance Scenarios**:

1. **Given** a validation failure where SCXML has state "waiting" but XState has state "pending", **When** the failure is inspected, **Then** it contains: formats involved (["SCXML", "XState"]), entity type ("state name"), expected value ("waiting"), actual value ("pending"), and a description explaining the mismatch.
2. **Given** a validation failure from a single-format self-consistency check, **When** the failure is inspected, **Then** it identifies the single format involved, the structural issue found, and the specific AST node or field that failed.
3. **Given** multiple validation failures in a single report, **When** the failures are enumerated, **Then** each failure is independent and self-contained (no failure references another failure for context).

---

### Edge Cases

- Empty artifact set (no formats provided) produces a valid report with zero checks performed, zero failures, and a note that no artifacts were supplied
- Artifact with an empty shared AST (no states, no transitions) passes structural validation but produces a warning about the empty state machine
- Two formats that agree on state names but use different casing (e.g., "Active" vs "active") are treated as a mismatch (case-sensitive comparison) and the failure message calls attention to the casing difference
- A validation rule that throws an exception during execution does not crash the validator; the exception is caught and reported as a validation failure with the rule name and error details
- Duplicate state identifiers within a single format artifact are reported as a self-consistency failure
- A format artifact with circular transitions (A->B->C->A) is valid and does not cause infinite loops in validation
- When all registered rules are skipped (none apply to the available formats), the report reflects this with all checks in "skipped" status
- Unicode characters in state names, event names, and guard conditions are handled correctly in comparisons and diagnostic output

## Requirements

### Functional Requirements

- **FR-001**: System MUST define a `ValidationRule` contract as a function signature that accepts a list of format-tagged artifacts and returns a list of validation checks (pass/fail/skip with optional failure details)
- **FR-002**: System MUST define a `FormatArtifact` type that wraps a parsed shared AST (`StatechartDocument` from spec 020) with a format tag identifying which parser produced it (WSD, ALPS, SCXML, smcat, XState)
- **FR-003**: System MUST define a `ValidationReport` type containing: total checks performed, total checks skipped, total failures, a list of `ValidationCheck` results, and a list of `ValidationFailure` details
- **FR-004**: System MUST define a `ValidationCheck` type representing a named invariant with status (pass, fail, skip) and an optional reason for skip status
- **FR-005**: System MUST define a `ValidationFailure` type containing: formats involved (list of format names), entity type (e.g., "state name", "event", "transition target"), expected value, actual value, and a human-readable description
- **FR-006**: System MUST accept any combination of format artifacts (zero to five) and execute only the validation rules whose required formats are present
- **FR-007**: System MUST skip validation rules gracefully when required format artifacts are not available, recording each skipped rule in the report with the reason (which format is missing)
- **FR-008**: System MUST collect all validation failures across all rules without aborting on the first failure
- **FR-009**: System MUST aggregate results from all executed rules into a single `ValidationReport`
- **FR-010**: System MUST support single-format self-consistency checks that validate structural validity (e.g., all transition targets reference existing states within the same artifact)
- **FR-011**: System MUST support single-format AST conformance checks that validate required shared AST fields are populated correctly by the parser
- **FR-012**: System MUST support cross-format invariant checks between any pair of formats when both artifacts are available
- **FR-013**: System MUST catch exceptions thrown by validation rules during execution and report them as failures with the rule name and error details, without crashing the validator
- **FR-014**: System MUST use case-sensitive comparison for state names, event names, and other identifiers when checking cross-format consistency
- **FR-015**: System MUST produce diagnostic information on each failure that identifies: the formats involved, the entity type, the expected value, the actual value, and a human-readable description sufficient to locate and fix the issue
- **FR-016**: System MUST validate against the shared AST types from spec 020 (`StatechartDocument`, `StateNode`, `TransitionEdge`, etc.)
- **FR-017**: System MUST allow validation rules to be registered from external modules (each format parser/generator registers its own rules) without modifying the validator module

### Key Entities

- **ValidationReport**: Top-level result containing the total number of checks performed, checks skipped, and failures. Holds a list of `ValidationCheck` results and a list of `ValidationFailure` details. Returned as a data structure; presentation is the CLI's concern.
- **ValidationFailure**: A single cross-format or intra-format mismatch. Contains the formats involved (one for self-consistency, two for cross-format), the entity type (state name, event, transition target, etc.), expected and actual values, and a human-readable description.
- **ValidationCheck**: A named invariant with a status of pass, fail, or skip. Skip status includes a reason (e.g., "ALPS artifact not available"). Failed checks reference their corresponding `ValidationFailure` entries.
- **FormatArtifact**: A format-tagged wrapper around a parsed `StatechartDocument` (from spec 020). The format tag is a discriminated union identifying the source format (WSD, ALPS, SCXML, smcat, XState).
- **ValidationRule**: A function that accepts a list of `FormatArtifact` values and returns a list of `ValidationCheck` results. Each rule declares which formats it requires. Rules are defined by format modules and registered with the validator.
- **FormatTag**: A discriminated union with cases for each supported format: WSD, ALPS, SCXML, Smcat, XState. Used to tag artifacts and declare rule requirements.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Validator correctly identifies all intentionally introduced mismatches in a test suite containing at least 10 distinct cross-format inconsistencies across different format pairs, with zero false negatives
- **SC-002**: Validator produces zero false positives when run against a fully consistent set of artifacts from all five formats representing the tic-tac-toe state machine
- **SC-003**: Validator completes validation of a state machine with 20 states, 50 transitions, and artifacts from all five formats in under 1 second
- **SC-004**: Every validation failure in the report contains sufficient diagnostic information (formats, entity type, expected/actual values) for a developer to locate and fix the issue without additional investigation
- **SC-005**: Validator gracefully handles missing formats by skipping inapplicable checks, producing a valid report with appropriate skip reasons, and never producing false failures for missing formats
- **SC-006**: A new format-specific validation rule can be registered from an external module and appears in the validation report without any changes to the validator module itself
- **SC-007**: Library compiles and all tests pass across all supported target platforms (net8.0/net9.0/net10.0)

## Assumptions

- The shared statechart AST (spec 020) is complete and stable before this validator is implemented. The validator operates on `StatechartDocument` and related types from spec 020.
- Each format parser/generator spec (011 ALPS, 013 smcat, 017 WSD Generator, 018 SCXML) will define and register its own validation rules in its own module. This spec defines the orchestration framework and contract, not the individual format-specific rules.
- The validator is a library component. It does not provide a CLI interface; `frank-cli validate` (#94) consumes the `ValidationReport` and handles presentation.
- Format artifacts are produced by `frank-cli compile` (#94) at build time. The validator receives pre-parsed `StatechartDocument` instances, not raw format text.
- The `ValidationRule` contract is a simple function signature, not an interface or class hierarchy. This keeps the design idiomatic to F# and minimizes ceremony for rule authors.
- Cross-format checks are defined per format pair. The 10 pairwise combinations from 5 formats (5 choose 2) are: Wsd-Alps, Wsd-Scxml, Wsd-Smcat, Wsd-XState, Alps-Scxml, Alps-Smcat, Alps-XState, Scxml-Smcat, Scxml-XState, Smcat-XState. There are no three-way or higher-order checks; complex invariants decompose into pairwise checks.
- State name, event name, and identifier comparisons are case-sensitive. Different casing between formats is treated as a mismatch.
- The validator does not modify artifacts. It is a pure read-only operation that produces a report.
