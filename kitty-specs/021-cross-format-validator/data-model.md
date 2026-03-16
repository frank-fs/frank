# Data Model: Cross-Format Statechart Validator (Spec 021)

**Date**: 2026-03-15
**Status**: Complete
**Derived From**: research.md decisions D-001 through D-010

---

## Entity Relationship Overview

```
Validator.validate : ValidationRule list -> FormatArtifact list -> ValidationReport

ValidationRule (record)
  |-- Name: string
  |-- RequiredFormats: FormatTag Set
  |-- Check: FormatArtifact list -> ValidationCheck list

FormatArtifact (record)
  |-- Format: FormatTag (DU: 5 cases)
  |-- Document: StatechartDocument (from spec 020)

ValidationReport (record)
  |-- TotalChecks: int
  |-- TotalSkipped: int
  |-- TotalFailures: int
  |-- Checks: ValidationCheck list
  |-- Failures: ValidationFailure list

ValidationCheck (record)
  |-- Name: string
  |-- Status: CheckStatus (DU: Pass | Fail | Skip)
  |-- Reason: string option

ValidationFailure (record)
  |-- Formats: FormatTag list
  |-- EntityType: string
  |-- Expected: string
  |-- Actual: string
  |-- Description: string
```

---

## Core Entities

### FormatTag (FR-002, FR-014)

A discriminated union identifying which parser produced an artifact.

```fsharp
type FormatTag =
    | Wsd
    | Alps
    | Scxml
    | Smcat
    | XState
```

| Case | Description |
|------|-------------|
| Wsd | WebSequenceDiagram parser (spec 007) |
| Alps | ALPS parser (spec 011) |
| Scxml | SCXML parser (spec 018) |
| Smcat | smcat parser (spec 013) |
| XState | XState JSON parser (forthcoming) |

**Structural Equality**: Yes (DU with no mutable payload)
**Notes**: Used for artifact tagging, rule format requirements, and diagnostic output.

---

### FormatArtifact (FR-002)

A format-tagged wrapper around a parsed shared AST document.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Format | `FormatTag` | Yes | Which parser produced this document |
| Document | `StatechartDocument` | Yes | The parsed shared AST (from spec 020) |

```fsharp
type FormatArtifact =
    { Format: FormatTag
      Document: StatechartDocument }
```

**Structural Equality**: Yes (both fields support structural equality)
**Notes**: One `FormatArtifact` per format per validation run. The same `StatechartDocument` type is used regardless of source format.

---

### CheckStatus (FR-004)

Status of a single validation check.

```fsharp
type CheckStatus =
    | Pass
    | Fail
    | Skip
```

| Case | Description |
|------|-------------|
| Pass | The invariant holds |
| Fail | The invariant is violated |
| Skip | The check could not run (missing format artifacts) |

---

### ValidationCheck (FR-004)

A named invariant result from a validation rule.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Name | `string` | Yes | Human-readable check name (e.g., "SCXML orphan transition targets") |
| Status | `CheckStatus` | Yes | Result of the check |
| Reason | `string option` | No | Reason for Skip status, or brief note for Fail |

```fsharp
type ValidationCheck =
    { Name: string
      Status: CheckStatus
      Reason: string option }
```

**Structural Equality**: Yes
**Notes**: Skip checks include a reason identifying which format is missing. Failed checks may include a brief reason; detailed failure information is in the corresponding `ValidationFailure`.

---

### ValidationFailure (FR-005, FR-015)

Detailed diagnostic information for a single validation failure.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Formats | `FormatTag list` | Yes | Formats involved (1 for self-consistency, 2 for cross-format) |
| EntityType | `string` | Yes | What kind of entity failed (e.g., "state name", "event", "transition target") |
| Expected | `string` | Yes | What was expected (e.g., "state 'review' should exist") |
| Actual | `string` | Yes | What was found (e.g., "state 'review' not found in SCXML") |
| Description | `string` | Yes | Human-readable explanation sufficient to locate and fix the issue |

```fsharp
type ValidationFailure =
    { Formats: FormatTag list
      EntityType: string
      Expected: string
      Actual: string
      Description: string }
```

**Structural Equality**: Yes
**Notes**: Each failure is independent and self-contained (FR-015, acceptance scenario 5.3). `EntityType`, `Expected`, `Actual`, and `Description` are all free-form strings; the validator does not constrain their content. When a casing mismatch is detected (e.g., "Active" vs "active"), the `Description` explicitly calls out the casing difference.

---

### ValidationReport (FR-003, FR-009)

Top-level result of a validation run.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| TotalChecks | `int` | Yes | Number of checks performed (Pass + Fail, excluding Skip) |
| TotalSkipped | `int` | Yes | Number of checks skipped |
| TotalFailures | `int` | Yes | Number of failures (equal to `Failures.Length`) |
| Checks | `ValidationCheck list` | Yes | All check results (pass, fail, skip) |
| Failures | `ValidationFailure list` | Yes | All failure details |

```fsharp
type ValidationReport =
    { TotalChecks: int
      TotalSkipped: int
      TotalFailures: int
      Checks: ValidationCheck list
      Failures: ValidationFailure list }
```

**Structural Equality**: Yes
**Notes**: `TotalChecks` counts only Pass + Fail checks (not skipped). `TotalFailures` equals `Failures.Length` and also equals the count of `Fail` checks in the `Checks` list. An empty artifact set produces `{ TotalChecks = 0; TotalSkipped = N; TotalFailures = 0; Checks = [...skips...]; Failures = [] }` where N is the number of registered rules.

---

### ValidationRule (FR-001, FR-017)

A validation rule defined by a format module.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Name | `string` | Yes | Human-readable rule name for diagnostics and reporting |
| RequiredFormats | `FormatTag Set` | Yes | Set of format tags this rule requires. Empty set = universal rule (runs on any single artifact). |
| Check | `FormatArtifact list -> ValidationCheck list` | Yes | The validation function. Receives all available artifacts; returns check results. |

```fsharp
type ValidationRule =
    { Name: string
      RequiredFormats: FormatTag Set
      Check: FormatArtifact list -> ValidationCheck list }
```

**Structural Equality**: No (function fields do not support structural equality)
**Notes**: The orchestrator checks `RequiredFormats` before invoking `Check`. If any required format is missing from the artifact list, the rule is skipped. Rules with `RequiredFormats = Set.empty` are "universal" rules that run regardless of which formats are present. The `Check` function receives the full artifact list and filters by format tag internally.

---

## Orchestrator Function

### Validator.validate (FR-006, FR-007, FR-008, FR-009, FR-013)

```fsharp
module Validator =
    /// Validate statechart artifacts against registered rules.
    /// Collects all results without aborting on first failure.
    /// Catches exceptions from rules and reports them as failures.
    val validate: rules: ValidationRule list -> artifacts: FormatArtifact list -> ValidationReport
```

**Behavior**:
1. Compute `availableTags = artifacts |> List.map _.Format |> Set.ofList`
2. For each rule in `rules`:
   a. If `rule.RequiredFormats` is not a subset of `availableTags`, emit a `Skip` check with reason listing missing formats
   b. Otherwise, invoke `rule.Check artifacts` inside a `try/with`
   c. If `Check` throws, emit a `Fail` check and a `ValidationFailure` with rule name and exception message
3. Aggregate all checks and failures into a `ValidationReport`
4. Compute `TotalChecks` (count of Pass + Fail), `TotalSkipped` (count of Skip), `TotalFailures` (count of Fail)

---

## AST Helper Functions

### AstHelpers Module

Shared traversal functions for extracting elements from `StatechartDocument` (spec 020). Used by validation rules to avoid duplicating traversal logic.

```fsharp
module AstHelpers =
    /// Extract all StateNode values from a document, recursively including children
    /// and states nested in GroupBlock branches.
    val allStates: doc: StatechartDocument -> StateNode list

    /// Extract all TransitionEdge values from a document, including those
    /// nested in GroupBlock branches.
    val allTransitions: doc: StatechartDocument -> TransitionEdge list

    /// Extract the set of all state identifiers from a document.
    val stateIdentifiers: doc: StatechartDocument -> string Set

    /// Extract the set of all event names from transitions in a document.
    /// Filters out None events.
    val eventNames: doc: StatechartDocument -> string Set

    /// Extract the set of all transition target identifiers from a document.
    /// Filters out None (internal/completion) targets.
    val transitionTargets: doc: StatechartDocument -> string Set
```

---

## File Structure

```
src/Frank.Statecharts/
  Ast/
    Types.fs              -- Shared AST types (spec 020: StatechartDocument, StateNode, etc.)
  Validation/
    Types.fs              -- FormatTag, FormatArtifact, CheckStatus, ValidationCheck,
                             ValidationFailure, ValidationReport, ValidationRule
    Validator.fs           -- AstHelpers module, Validator.validate function
  Wsd/
    Types.fs              -- WSD-specific lexer types (TokenKind, Token)
    Lexer.fs
    GuardParser.fs
    Parser.fs
  Types.fs                -- Runtime state machine types (existing)
  ...
```

The `Frank.Statecharts.fsproj` compile order must list:
1. `Ast/Types.fs` (shared AST -- spec 020 dependency)
2. `Validation/Types.fs` (validation types -- depends on shared AST)
3. `Validation/Validator.fs` (orchestrator -- depends on validation types)
4. `Wsd/Types.fs` (WSD types -- imports SourcePosition from Ast)
5. ... (remaining existing files)
