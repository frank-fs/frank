# Research: Cross-Format Statechart Validator (Spec 021)

**Date**: 2026-03-15
**Status**: Complete
**Researcher**: Claude (spec-kitty.plan)

## Executive Summary

This research investigates the design of a pluggable validation orchestrator for cross-format statechart consistency checking. The validator operates on the shared AST types from spec 020 (`StatechartDocument`, `StateNode`, `TransitionEdge`, etc.) and defines a function-based `ValidationRule` contract. Key decisions cover the rule registration mechanism (functional, no mutable state), the validation type hierarchy, AST traversal helpers, and exception handling strategy.

---

## Research Area 1: Rule Registration Mechanism

### Options Considered

**Option A: Mutable registry (rejected)**
A global `ValidationRegistry` that format modules mutate at startup. This introduces mutable state, ordering dependencies, and makes testing harder. Rejected because it violates the "Library, Not Framework" constitution principle and introduces hidden global state.

**Option B: Functional composition (selected)**
Each format module exposes a `rules: ValidationRule list` value at the module level. The consumer (frank-cli or tests) assembles the full rule list by concatenating lists from the format modules it wants to include, then passes `rules + artifacts` to `Validator.validate`.

```fsharp
// In a format module (e.g., Frank.Statecharts.Scxml.Validation):
let rules: ValidationRule list = [ orphanTargetCheck; requiredFieldsCheck ]

// At the call site:
let allRules = ScxmlValidation.rules @ AlpsValidation.rules @ CrossFormatRules.rules
let report = Validator.validate allRules artifacts
```

Advantages:
- No mutable state
- Fully testable (pass any subset of rules)
- Consumer controls which rules run
- Composable (concatenate rule lists)
- Aligns with "Library, Not Framework" principle

**Decision D-001**: Functional composition. Rules are `ValidationRule list` values. Consumer assembles and passes to `Validator.validate`.

---

## Research Area 2: ValidationRule Contract Design

### Function Signature

The spec requires `ValidationRule` to be "a function signature, not an interface or class." The rule needs to:
1. Declare which formats it requires (so the orchestrator can skip it when formats are missing)
2. Accept the available artifacts
3. Return check results

Two design approaches:

**Option A: Plain function with format requirements as data**
```fsharp
type ValidationRule =
    { Name: string
      RequiredFormats: FormatTag Set
      Check: FormatArtifact list -> ValidationCheck list }
```

The `Name` field enables meaningful diagnostics when a rule throws an exception. The `RequiredFormats` set lets the orchestrator skip the rule without invoking it. The `Check` function receives only the artifacts and returns checks.

**Option B: Single function that returns skip/results (rejected)**
```fsharp
type ValidationRule = FormatArtifact list -> ValidationCheck list
```

This puts the format-checking logic inside every rule, duplicating the skip logic. Rejected because it violates "No Duplicated Logic" (constitution VIII).

**Decision D-002**: Use a record with `Name`, `RequiredFormats`, and `Check` function. The record is not a class -- it is an immutable F# record with a function field. The orchestrator handles skip logic centrally.

---

## Research Area 3: FormatTag Design

### Format Identification

The spec lists five formats: WSD, ALPS, SCXML, smcat, XState. The `FormatTag` identifies which parser produced an artifact.

```fsharp
type FormatTag =
    | Wsd
    | Alps
    | Scxml
    | Smcat
    | XState
```

Simple discriminated union with no payload. Used for:
1. Tagging `FormatArtifact` values
2. Declaring `RequiredFormats` on rules
3. Identifying formats in failure diagnostics

**Decision D-003**: `FormatTag` is a simple 5-case DU with no payload. Case names follow F# naming conventions (PascalCase).

---

## Research Area 4: Orchestrator Behavior

### Rule Execution Strategy

The orchestrator must:
1. For each rule, check if all `RequiredFormats` are present in the artifact list
2. If not, record a `Skip` check with reason (which format is missing)
3. If yes, invoke the rule's `Check` function
4. Catch any exceptions from the `Check` function and report them as failures
5. Aggregate all results into a single `ValidationReport`

### Exception Handling (FR-013)

Constitution principle VII says "No Silent Exception Swallowing in Request Paths." However, the validator is not in a request path -- it is a build-time library tool. The spec (FR-013) explicitly requires catching exceptions and reporting them as failures. This is not silent swallowing: the exception details are preserved in the `ValidationFailure` record with the rule name and full error message.

```fsharp
// Pseudocode for orchestrator:
let executeRule (rule: ValidationRule) (artifacts: FormatArtifact list) =
    let availableTags = artifacts |> List.map (fun a -> a.Format) |> Set.ofList
    let missingFormats = rule.RequiredFormats - availableTags
    if not (Set.isEmpty missingFormats) then
        let reason = sprintf "Missing formats: %s" (missingFormats |> Set.toList |> List.map string |> String.concat ", ")
        [ { Name = rule.Name; Status = Skip; Reason = Some reason } ]
    else
        try
            rule.Check artifacts
        with ex ->
            [ { Name = rule.Name; Status = Fail; Reason = None }
              // Plus a ValidationFailure with the exception details ]
```

**Decision D-004**: Exceptions from rules are caught and reported as `Fail` checks with a corresponding `ValidationFailure` containing the rule name and `exn.Message`. This satisfies FR-013 without violating constitution VII (the error is surfaced, not swallowed).

### Empty Artifact Set

When no artifacts are provided, all rules are skipped (no formats present). The report has zero checks performed, zero failures, and all rules in Skip status. This matches the edge case spec.

**Decision D-005**: Empty artifact set produces a valid report with all rules skipped. No special-casing needed -- the general skip logic handles it.

---

## Research Area 5: AST Traversal Helpers

### Extracting States and Transitions from StatechartDocument

The shared AST (spec 020) stores elements in an ordered `StatechartElement list`. Validation rules need to extract states, transitions, and other elements efficiently. Rather than having each rule write its own extraction logic, shared helper functions should be provided.

```fsharp
module AstHelpers =
    /// Extract all StateNode values from a StatechartDocument, including nested children.
    let allStates (doc: StatechartDocument) : StateNode list = ...

    /// Extract all TransitionEdge values from a StatechartDocument.
    let allTransitions (doc: StatechartDocument) : TransitionEdge list = ...

    /// Extract all state identifiers from a StatechartDocument.
    let stateIdentifiers (doc: StatechartDocument) : string Set = ...

    /// Extract all event names from transitions in a StatechartDocument.
    let eventNames (doc: StatechartDocument) : string Set = ...
```

These helpers walk the `Elements` list and recursively descend into `GroupBlock` branches and `StateNode.Children`. They are pure functions with no side effects.

**Decision D-006**: Provide module-level helper functions in a `Validation.AstHelpers` module for extracting states, transitions, identifiers, and event names from `StatechartDocument`. Rules use these helpers instead of duplicating traversal logic (constitution VIII).

---

## Research Area 6: Validation Check and Failure Modeling

### Check Status

The spec defines three statuses: pass, fail, skip. Model as a DU:

```fsharp
type CheckStatus =
    | Pass
    | Fail
    | Skip
```

### ValidationCheck

A named invariant with a status and optional reason:

```fsharp
type ValidationCheck =
    { Name: string
      Status: CheckStatus
      Reason: string option }
```

`Reason` is populated for `Skip` (why skipped) and optionally for `Fail` (brief note). Detailed failure information is in the separate `ValidationFailure` list.

### ValidationFailure

Detailed diagnostic information:

```fsharp
type ValidationFailure =
    { Formats: FormatTag list
      EntityType: string
      Expected: string
      Actual: string
      Description: string }
```

All fields are strings for maximum flexibility. The `Formats` list has one element for self-consistency checks and two for cross-format checks.

### ValidationReport

Top-level aggregation:

```fsharp
type ValidationReport =
    { TotalChecks: int
      TotalSkipped: int
      TotalFailures: int
      Checks: ValidationCheck list
      Failures: ValidationFailure list }
```

**Decision D-007**: Use simple records with string fields for maximum flexibility. No rich types for `EntityType` or `Expected`/`Actual` -- these are diagnostic strings meant for human consumption. The CLI layer (spec #94) handles formatting.

---

## Research Area 7: Self-Consistency Check Patterns

### Common Single-Format Checks

These checks apply when only one format artifact is available:

1. **Orphan transition targets**: All `TransitionEdge.Target` values (when `Some`) must reference a state identifier that exists in the same document.
2. **Duplicate state identifiers**: All `StateNode.Identifier` values within a single document must be unique.
3. **Required AST fields**: `StateNode.Identifier` must be non-empty. `TransitionEdge.Source` must be non-empty.
4. **Isolated states**: States with no incoming or outgoing transitions (warning, not failure per spec scenario 3).

These checks are generic -- they apply to any format's parsed `StatechartDocument`. They can be defined once and registered as rules with `RequiredFormats = Set.empty` (any single artifact suffices) or with a specific format tag.

**Decision D-008**: Generic self-consistency checks (orphan targets, duplicates, required fields) are defined in the `Validation` module itself as "universal rules." They require no specific format and run on every artifact individually. Format-specific self-consistency rules are defined by each format module.

---

## Research Area 8: Cross-Format Check Patterns

### Pairwise Invariant Checks

Cross-format checks compare two artifacts:

1. **State name agreement**: All state identifiers in artifact A should exist in artifact B, and vice versa.
2. **Event name agreement**: All events in artifact A should exist in artifact B.
3. **Transition target agreement**: Transition targets in one format should correspond to states in another.

These checks require two specific format tags (e.g., `RequiredFormats = set [Scxml; XState]`).

The orchestrator filters artifacts by format tag and passes the relevant pair to the rule's `Check` function. Rules receive the full artifact list but can filter by tag:

```fsharp
let scxmlXStateStateCheck: ValidationRule =
    { Name = "SCXML-XState state name agreement"
      RequiredFormats = set [ Scxml; XState ]
      Check = fun artifacts ->
          let scxml = artifacts |> List.find (fun a -> a.Format = Scxml)
          let xstate = artifacts |> List.find (fun a -> a.Format = XState)
          // Compare state identifiers...
    }
```

**Decision D-009**: Cross-format rules receive the full artifact list and filter by format tag internally. The orchestrator only checks that required formats are present; it does not pre-filter. This allows rules to access additional artifacts for context if needed (future extensibility).

---

## Research Area 9: Case Sensitivity

### Spec Requirement

The spec is explicit: "State name, event name, and identifier comparisons are case-sensitive. Different casing between formats is treated as a mismatch" (Assumptions section). The edge cases section adds: "the failure message calls attention to the casing difference."

**Decision D-010**: All identifier comparisons use ordinal (case-sensitive) string comparison. When a near-match is detected (same string, different casing), the failure description explicitly notes the casing difference to help the developer. Detection logic: if `String.Equals(a, b, StringComparison.OrdinalIgnoreCase)` is true but `a <> b`, note the casing mismatch in the description.

---

## Decision Register

| ID | Decision | Rationale |
|----|----------|-----------|
| D-001 | Functional rule composition -- consumer assembles rule lists | No mutable state; "Library, Not Framework" (constitution III) |
| D-002 | `ValidationRule` is a record with `Name`, `RequiredFormats`, `Check` | Enables skip logic and exception reporting without duplicating logic (constitution VIII) |
| D-003 | `FormatTag` is a simple 5-case DU | Minimal, type-safe format identification |
| D-004 | Exceptions from rules are caught and reported as failures | FR-013; not silent swallowing since details are preserved (constitution VII) |
| D-005 | Empty artifact set produces a valid report with all rules skipped | General skip logic handles it; matches edge case spec |
| D-006 | Shared AST traversal helpers in `Validation.AstHelpers` module | Avoids duplicated traversal logic across rules (constitution VIII) |
| D-007 | Simple string fields in `ValidationFailure` for diagnostics | Presentation is CLI's concern; maximum flexibility |
| D-008 | Generic self-consistency checks are "universal rules" in validator module | Apply to any format; avoid duplicating structural checks |
| D-009 | Cross-format rules receive full artifact list, filter internally | Orchestrator checks presence; rules control selection |
| D-010 | Case-sensitive comparison with explicit casing-mismatch diagnostics | Spec requirement; failure messages note casing differences |
