---
work_package_id: WP04
title: Cross-Format Validation Rules
lane: "planned"
dependencies: [WP02]
base_branch: 021-cross-format-validator-WP02
base_commit: 2ab90f1895bb7a4cfe04bd5469803e2d8c4db322
created_at: '2026-03-16T04:17:12.703868+00:00'
subtasks:
- T022
- T023
- T024
- T025
- T026
- T027
phase: Phase 1 - Core Rules
assignee: ''
agent: "claude-opus-4-6"
shell_pid: "62414"
review_status: "has_feedback"
reviewed_by: "Ryan Riley"
review_feedback_file: "/private/tmp/fix-lane.md"
history:
- timestamp: '2026-03-15T23:59:11Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-012, FR-014, FR-015, FR-017]
---

# Work Package Prompt: WP04 -- Cross-Format Validation Rules

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

**Reviewed by**: Ryan Riley
**Status**: ❌ Changes Requested
**Date**: 2026-03-16
**Feedback file**: `/private/tmp/fix-lane.md`

**Issue**: Manually correcting lane status to done


## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````bash`

---

## Implementation Command

```bash
spec-kitty implement WP04 --base WP02
```

Depends on WP02 (AstHelpers and Validator.validate must exist).
Can be developed in parallel with WP03 (both depend on WP02 only).

---

## Objectives & Success Criteria

- Implement cross-format pairwise validation rules for state name agreement, event name agreement, and transition target agreement.
- Implement casing mismatch detection that explicitly notes casing differences in failure descriptions.
- Generate rules for all 10 pairwise format combinations (5 choose 2).
- Expose a `CrossFormatRules.rules` value aggregating all cross-format rules.
- **Success**: Rules detect mismatches between two format artifacts and pass when artifacts agree. `dotnet build` succeeds.

---

## Context & Constraints

- **Spec**: `kitty-specs/021-cross-format-validator/spec.md` -- User Stories 2-4, FR-012, FR-014, edge cases (casing)
- **Research**: `kitty-specs/021-cross-format-validator/research.md` -- D-009 (rules receive full list, filter internally), D-010 (case-sensitive with casing mismatch detection)
- **Data Model**: `kitty-specs/021-cross-format-validator/data-model.md` -- ValidationRule, FormatTag, FormatArtifact

### Key Constraints
- Cross-format rules require exactly 2 formats in `RequiredFormats`.
- Rules receive the full artifact list and filter by format tag internally (D-009).
- Comparisons are case-sensitive (D-010). When a near-match is detected (same string ignoring case, different case), the failure description MUST explicitly note the casing difference.
- Symmetric checks: if format A has state "foo" missing from B, AND B has state "bar" missing from A, report BOTH as separate failures.
- 10 pairwise combinations: Wsd-Alps, Wsd-Scxml, Wsd-Smcat, Wsd-XState, Alps-Scxml, Alps-Smcat, Alps-XState, Scxml-Smcat, Scxml-XState, Smcat-XState.
- Use generic rule factories to avoid duplicating rule logic 10 times.

### Acceptance Scenarios to Satisfy
1. ALPS and XState with matching events -> all checks pass.
2. XState has event "submitMove" missing from ALPS -> failure identifying formats (ALPS, XState), entity type (event), expected/actual.
3. ALPS transition target "gameOver" not in XState states -> failure identifying missing state.
4. "Active" vs "active" (case difference) -> failure with explicit casing note.
5. Three+ formats: only applicable pairs checked, missing format pairs skipped.

---

## Subtasks & Detailed Guidance

### Subtask T022 -- State name agreement rule

**Purpose**: Check that state identifiers agree between two format artifacts (FR-012, spec scenario 2.3).

**Steps**:
1. Add to `src/Frank.Statecharts/Validation/Validator.fs` a new module (or extend the file):
   ```fsharp
   module CrossFormatRules =

       /// Create a state name agreement rule for a specific format pair.
       let stateNameAgreement (formatA: FormatTag) (formatB: FormatTag) : ValidationRule =
           { Name = sprintf "%A-%A state name agreement" formatA formatB
             RequiredFormats = set [ formatA; formatB ]
             Check = fun artifacts ->
                 let artA = artifacts |> List.find (fun a -> a.Format = formatA)
                 let artB = artifacts |> List.find (fun a -> a.Format = formatB)
                 let statesA = AstHelpers.stateIdentifiers artA.Document
                 let statesB = AstHelpers.stateIdentifiers artB.Document

                 let missingFromB = statesA - statesB
                 let missingFromA = statesB - statesA

                 let failuresFromB =
                     missingFromB
                     |> Set.toList
                     |> List.map (fun stateId ->
                         let casingNote = describeCasingMismatch stateId statesB
                         { Name = sprintf "State '%s' missing from %A" stateId formatB
                           Status = Fail
                           Reason = Some (sprintf "State '%s' exists in %A but not in %A.%s" stateId formatA formatB casingNote) })

                 let failuresFromA =
                     missingFromA
                     |> Set.toList
                     |> List.map (fun stateId ->
                         let casingNote = describeCasingMismatch stateId statesA
                         { Name = sprintf "State '%s' missing from %A" stateId formatA
                           Status = Fail
                           Reason = Some (sprintf "State '%s' exists in %A but not in %A.%s" stateId formatB formatA casingNote) })

                 let allFailures = failuresFromB @ failuresFromA
                 if List.isEmpty allFailures then
                     [ { Name = sprintf "%A-%A state name agreement" formatA formatB
                         Status = Pass
                         Reason = None } ]
                 else
                     allFailures }
   ```

2. The `describeCasingMismatch` helper is defined in T025.

**Files**: `src/Frank.Statecharts/Validation/Validator.fs`
**Parallel?**: Yes -- independent from T023, T024.

---

### Subtask T023 -- Event name agreement rule

**Purpose**: Check that event names agree between two format artifacts (FR-012, spec scenario 2.1-2.2).

**Steps**:
1. Add to `CrossFormatRules` module:
   ```fsharp
   /// Create an event name agreement rule for a specific format pair.
   let eventNameAgreement (formatA: FormatTag) (formatB: FormatTag) : ValidationRule =
       { Name = sprintf "%A-%A event name agreement" formatA formatB
         RequiredFormats = set [ formatA; formatB ]
         Check = fun artifacts ->
             let artA = artifacts |> List.find (fun a -> a.Format = formatA)
             let artB = artifacts |> List.find (fun a -> a.Format = formatB)
             let eventsA = AstHelpers.eventNames artA.Document
             let eventsB = AstHelpers.eventNames artB.Document

             let missingFromB = eventsA - eventsB
             let missingFromA = eventsB - eventsA

             let failuresFromB =
                 missingFromB
                 |> Set.toList
                 |> List.map (fun eventName ->
                     let casingNote = describeCasingMismatch eventName eventsB
                     { Name = sprintf "Event '%s' missing from %A" eventName formatB
                       Status = Fail
                       Reason = Some (sprintf "Event '%s' exists in %A but not in %A.%s" eventName formatA formatB casingNote) })

             let failuresFromA =
                 missingFromA
                 |> Set.toList
                 |> List.map (fun eventName ->
                     let casingNote = describeCasingMismatch eventName eventsA
                     { Name = sprintf "Event '%s' missing from %A" eventName formatA
                       Status = Fail
                       Reason = Some (sprintf "Event '%s' exists in %A but not in %A.%s" eventName formatB formatA casingNote) })

             let allFailures = failuresFromB @ failuresFromA
             if List.isEmpty allFailures then
                 [ { Name = sprintf "%A-%A event name agreement" formatA formatB
                     Status = Pass
                     Reason = None } ]
             else
                 allFailures }
   ```

**Files**: `src/Frank.Statecharts/Validation/Validator.fs`
**Parallel?**: Yes.

---

### Subtask T024 -- Transition target agreement rule

**Purpose**: Check that transition targets in one format reference states that exist in another format.

**Steps**:
1. Add to `CrossFormatRules` module:
   ```fsharp
   /// Create a transition target agreement rule for a specific format pair.
   /// Checks that transition targets in format A reference states that exist in format B.
   let transitionTargetAgreement (formatA: FormatTag) (formatB: FormatTag) : ValidationRule =
       { Name = sprintf "%A-%A transition target agreement" formatA formatB
         RequiredFormats = set [ formatA; formatB ]
         Check = fun artifacts ->
             let artA = artifacts |> List.find (fun a -> a.Format = formatA)
             let artB = artifacts |> List.find (fun a -> a.Format = formatB)
             let targetsA = AstHelpers.transitionTargets artA.Document
             let statesB = AstHelpers.stateIdentifiers artB.Document
             let targetsB = AstHelpers.transitionTargets artB.Document
             let statesA = AstHelpers.stateIdentifiers artA.Document

             // Targets in A should reference states in B
             let missingInB = targetsA - statesB
             // Targets in B should reference states in A
             let missingInA = targetsB - statesA

             let failuresAtoB =
                 missingInB
                 |> Set.toList
                 |> List.map (fun target ->
                     let casingNote = describeCasingMismatch target statesB
                     { Name = sprintf "Transition target '%s' from %A missing in %A states" target formatA formatB
                       Status = Fail
                       Reason = Some (sprintf "Transition target '%s' in %A does not correspond to any state in %A.%s" target formatA formatB casingNote) })

             let failuresBtoA =
                 missingInA
                 |> Set.toList
                 |> List.map (fun target ->
                     let casingNote = describeCasingMismatch target statesA
                     { Name = sprintf "Transition target '%s' from %A missing in %A states" target formatB formatA
                       Status = Fail
                       Reason = Some (sprintf "Transition target '%s' in %A does not correspond to any state in %A.%s" target formatB formatA casingNote) })

             let allFailures = failuresAtoB @ failuresBtoA
             if List.isEmpty allFailures then
                 [ { Name = sprintf "%A-%A transition target agreement" formatA formatB
                     Status = Pass
                     Reason = None } ]
             else
                 allFailures }
   ```

**Files**: `src/Frank.Statecharts/Validation/Validator.fs`
**Parallel?**: Yes.

---

### Subtask T025 -- Casing mismatch detection helper

**Purpose**: Detect and describe casing differences between identifiers (D-010, edge case spec).

**Steps**:
1. Add to `CrossFormatRules` module (BEFORE the rule functions that use it):
   ```fsharp
   /// Check if a value has a case-insensitive match in a set but not an exact match.
   /// Returns a descriptive note about the casing difference, or empty string if no near-match.
   let private describeCasingMismatch (value: string) (candidates: string Set) : string =
       let nearMatch =
           candidates
           |> Set.toList
           |> List.tryFind (fun c ->
               System.String.Equals(value, c, System.StringComparison.OrdinalIgnoreCase)
               && value <> c)
       match nearMatch with
       | Some found ->
           sprintf " Note: case-insensitive match found: '%s' vs '%s' (casing differs)" value found
       | None -> ""
   ```

**Files**: `src/Frank.Statecharts/Validation/Validator.fs`
**Notes**: This must be defined before T022-T024 functions since they reference it. The `private` visibility keeps it internal to the module.

---

### Subtask T026 -- `CrossFormatRules` module aggregate

**Purpose**: Expose a `rules` value aggregating all cross-format rules for easy registration.

**Steps**:
1. After all rule factory functions are defined, add:
   ```fsharp
   /// All cross-format rules for all applicable format pairs.
   let rules: ValidationRule list =
       allPairwiseRules  // Defined in T027
   ```

**Files**: `src/Frank.Statecharts/Validation/Validator.fs`

---

### Subtask T027 -- Rule generation for all 10 pairwise combinations

**Purpose**: Generate validation rules for all 10 pairwise format combinations (5 choose 2), avoiding code duplication.

**Steps**:
1. Define all format pairs:
   ```fsharp
   /// All unique pairs of format tags.
   let private formatPairs : (FormatTag * FormatTag) list =
       let tags = [ Wsd; Alps; Scxml; Smcat; XState ]
       [ for i in 0 .. tags.Length - 2 do
           for j in i + 1 .. tags.Length - 1 do
               yield (tags.[i], tags.[j]) ]
   ```

2. Generate rules for all pairs:
   ```fsharp
   /// All cross-format rules generated from pairwise combinations.
   let private allPairwiseRules : ValidationRule list =
       formatPairs
       |> List.collect (fun (a, b) ->
           [ stateNameAgreement a b
             eventNameAgreement a b
             transitionTargetAgreement a b ])
   ```

3. This produces 30 rules total (10 pairs x 3 check types). Each rule has a unique name based on the format pair and check type.

**Files**: `src/Frank.Statecharts/Validation/Validator.fs`
**Notes**:
- 10 pairs: Wsd-Alps, Wsd-Scxml, Wsd-Smcat, Wsd-XState, Alps-Scxml, Alps-Smcat, Alps-XState, Scxml-Smcat, Scxml-XState, Smcat-XState.
- Each pair gets 3 rules: state name, event name, transition target agreement.
- Rules for pair (A, B) check symmetrically: both A-missing-from-B and B-missing-from-A.
- No need for separate (B, A) rules since each rule checks both directions.

---

## Risks & Mitigations

- **Combinatorial explosion**: 30 rules (10 pairs x 3 types) is manageable. Each rule is lightweight (set operations).
- **Casing detection edge cases**: Unicode case folding could differ from `OrdinalIgnoreCase`. Stick with `OrdinalIgnoreCase` for consistency.
- **Rule naming collisions**: Each rule has a unique name derived from the format pair and check type. No collisions possible.
- **`List.find` may throw**: If a rule's required formats are present but the artifact list somehow doesn't contain both, `List.find` would throw. The orchestrator's exception handling (FR-013) catches this, but it should not happen because the orchestrator checks `RequiredFormats` before invoking `Check`.

---

## Review Guidance

- Verify `describeCasingMismatch` is defined BEFORE rule factory functions that use it.
- Verify all 10 pairwise format combinations are generated.
- Verify symmetric checks: each rule checks both directions (A missing from B AND B missing from A).
- Verify casing mismatch detection: "Active" vs "active" produces a failure with explicit casing note.
- Verify rules with `RequiredFormats = set [formatA; formatB]` for exactly 2 formats.
- Verify `CrossFormatRules.rules` contains all 30 generated rules.
- Verify `dotnet build` succeeds.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:11Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T04:17:12Z – claude-opus – shell_pid=4668 – lane=doing – Assigned agent via workflow command
- 2026-03-16T04:17:51Z – claude-opus – shell_pid=4668 – lane=planned – Moved to planned
- 2026-03-16T04:32:23Z – claude-opus – shell_pid=15686 – lane=doing – Started implementation via workflow command
- 2026-03-16T11:50:00Z – claude-opus – shell_pid=15686 – lane=for_review – Ready for review: All 6 subtasks (T022-T027) implemented. Cross-format rules for state name, event name, and transition target agreement across all 10 pairwise format combinations (30 rules total). Casing mismatch detection included. Build succeeds on net8.0/net9.0/net10.0 with 0 warnings.
- 2026-03-16T11:51:44Z – claude-opus-4-6 – shell_pid=62414 – lane=doing – Started review via workflow command
- 2026-03-16T14:34:45Z – claude-opus-4-6 – shell_pid=62414 – lane=planned – Moved to planned
