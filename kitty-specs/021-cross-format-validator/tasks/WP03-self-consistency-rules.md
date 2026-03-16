---
work_package_id: WP03
title: Self-Consistency Validation Rules
lane: "done"
dependencies: [WP02]
base_branch: 021-cross-format-validator-WP02
base_commit: 2ab90f1895bb7a4cfe04bd5469803e2d8c4db322
created_at: '2026-03-16T04:31:44.311364+00:00'
subtasks:
- T016
- T017
- T018
- T019
- T020
- T021
phase: Phase 1 - Core Rules
assignee: ''
agent: claude-opus
shell_pid: '14982'
review_status: "has_feedback"
reviewed_by: "Ryan Riley"
review_feedback_file: "/private/tmp/fix-lane.md"
history:
- timestamp: '2026-03-15T23:59:11Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-010, FR-011, FR-014, FR-017]
---

# Work Package Prompt: WP03 -- Self-Consistency Validation Rules

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
spec-kitty implement WP03 --base WP02
```

Depends on WP02 (AstHelpers and Validator.validate must exist).

---

## Objectives & Success Criteria

- Implement universal self-consistency rules that validate structural integrity of any single-format artifact.
- Rules cover: orphan transition targets, duplicate state identifiers, required AST fields, isolated state warnings, empty statechart warnings.
- All rules are registered as `ValidationRule` values with `RequiredFormats = Set.empty`.
- Expose a `SelfConsistencyRules.rules` value that aggregates all universal rules.
- **Success**: Rules produce correct pass/fail results when tested against well-formed and malformed artifacts. `dotnet build` succeeds.

---

## Context & Constraints

- **Spec**: `kitty-specs/021-cross-format-validator/spec.md` -- User Story 1 (single-format self-consistency), FR-010, FR-011, FR-014, edge cases
- **Research**: `kitty-specs/021-cross-format-validator/research.md` -- D-008 (universal rules), D-010 (case-sensitive)
- **Data Model**: `kitty-specs/021-cross-format-validator/data-model.md` -- ValidationRule, ValidationCheck, ValidationFailure field definitions

### Key Constraints
- All rules use `RequiredFormats = Set.empty` (universal -- run on every artifact individually).
- Each rule iterates over EACH artifact independently (not pairwise between artifacts).
- Isolated states produce warnings (`Pass` status with descriptive reason), NOT failures (spec scenario 1.3).
- Empty statechart produces a warning about the empty state machine (edge case spec).
- Case-sensitive comparisons (D-010).
- Rules use `AstHelpers` for traversal -- do NOT duplicate traversal logic.

### Acceptance Scenarios to Satisfy
1. SCXML artifact with all valid transitions -> all checks pass.
2. SCXML artifact with orphan target "review" -> failure identifying the orphan target.
3. smcat artifact with isolated state -> warning (not failure) about isolated state.
4. Artifact with empty state identifier -> failure for missing required field.
5. Artifact with duplicate state identifiers -> failure for duplicates.

---

## Subtasks & Detailed Guidance

### Subtask T016 -- Orphan transition target rule

**Purpose**: Validate that all transition targets reference states that exist within the same artifact (FR-010, spec scenario 1.1-1.2).

**Steps**:
1. Add to `src/Frank.Statecharts/Validation/Validator.fs` (after the `Validator` module, or in a new `SelfConsistencyRules` module within the same file):
   ```fsharp
   module SelfConsistencyRules =

       /// Check that all transition targets reference existing states within each artifact.
       let orphanTransitionTargets: ValidationRule =
           { Name = "Orphan transition targets"
             RequiredFormats = Set.empty
             Check = fun artifacts ->
                 artifacts
                 |> List.collect (fun artifact ->
                     let stateIds = AstHelpers.stateIdentifiers artifact.Document
                     let targets = AstHelpers.transitionTargets artifact.Document
                     let orphans = targets - stateIds
                     if Set.isEmpty orphans then
                         [ { Name = sprintf "Orphan transition targets (%A)" artifact.Format
                             Status = Pass
                             Reason = None } ]
                     else
                         orphans
                         |> Set.toList
                         |> List.map (fun orphan ->
                             { Name = sprintf "Orphan transition target '%s' (%A)" orphan artifact.Format
                               Status = Fail
                               Reason = Some (sprintf "Transition target '%s' does not reference any state in %A artifact" orphan artifact.Format) })) }
   ```

2. When a `Fail` check is produced, also produce a corresponding `ValidationFailure`:
   - `Formats = [ artifact.Format ]`
   - `EntityType = "transition target"`
   - `Expected = sprintf "Target '%s' should reference an existing state" orphan`
   - `Actual = sprintf "State '%s' not found in %A artifact" orphan artifact.Format`
   - `Description = sprintf "Transition targets state '%s' which does not exist in the %A artifact. Available states: %s" orphan artifact.Format (stateIds |> Set.toList |> String.concat ", ")`

**IMPORTANT**: Since `Check` returns `ValidationCheck list`, not failures, the orchestrator needs a way to get failures. See WP02 T014 notes on failure propagation. The recommended approach for this WP: have the orchestrator derive `ValidationFailure` from `Fail` checks using the `Reason` field content. Rules should encode all diagnostic info in `Reason`.

**Files**: `src/Frank.Statecharts/Validation/Validator.fs`
**Parallel?**: Yes -- independent from T017-T020.

---

### Subtask T017 -- Duplicate state identifier rule

**Purpose**: Detect duplicate state identifiers within a single artifact (edge case spec).

**Steps**:
1. Add to `SelfConsistencyRules` module:
   ```fsharp
   /// Check that all state identifiers are unique within each artifact.
   let duplicateStateIdentifiers: ValidationRule =
       { Name = "Duplicate state identifiers"
         RequiredFormats = Set.empty
         Check = fun artifacts ->
             artifacts
             |> List.collect (fun artifact ->
                 let allStates = AstHelpers.allStates artifact.Document
                 let ids = allStates |> List.map (fun s -> s.Identifier)
                 let duplicates =
                     ids
                     |> List.groupBy id
                     |> List.filter (fun (_, group) -> group.Length > 1)
                     |> List.map fst
                 if List.isEmpty duplicates then
                     [ { Name = sprintf "Duplicate state identifiers (%A)" artifact.Format
                         Status = Pass
                         Reason = None } ]
                 else
                     duplicates
                     |> List.map (fun dup ->
                         { Name = sprintf "Duplicate state identifier '%s' (%A)" dup artifact.Format
                           Status = Fail
                           Reason = Some (sprintf "State identifier '%s' appears multiple times in %A artifact" dup artifact.Format) })) }
   ```

**Files**: `src/Frank.Statecharts/Validation/Validator.fs`
**Parallel?**: Yes.

---

### Subtask T018 -- Required AST fields rule

**Purpose**: Validate that required fields are populated: state identifiers must be non-empty, transition sources must be non-empty (FR-011, spec scenario 1.4).

**Steps**:
1. Add to `SelfConsistencyRules` module:
   ```fsharp
   /// Check that required AST fields are populated in each artifact.
   let requiredAstFields: ValidationRule =
       { Name = "Required AST fields"
         RequiredFormats = Set.empty
         Check = fun artifacts ->
             artifacts
             |> List.collect (fun artifact ->
                 let states = AstHelpers.allStates artifact.Document
                 let transitions = AstHelpers.allTransitions artifact.Document

                 let emptyStateIds =
                     states
                     |> List.filter (fun s -> System.String.IsNullOrWhiteSpace s.Identifier)
                     |> List.mapi (fun i _ ->
                         { Name = sprintf "Empty state identifier #%d (%A)" (i + 1) artifact.Format
                           Status = Fail
                           Reason = Some (sprintf "State at index %d has empty identifier in %A artifact" i artifact.Format) })

                 let emptyTransitionSources =
                     transitions
                     |> List.filter (fun t -> System.String.IsNullOrWhiteSpace t.Source)
                     |> List.mapi (fun i _ ->
                         { Name = sprintf "Empty transition source #%d (%A)" (i + 1) artifact.Format
                           Status = Fail
                           Reason = Some (sprintf "Transition at index %d has empty source in %A artifact" i artifact.Format) })

                 let allIssues = emptyStateIds @ emptyTransitionSources
                 if List.isEmpty allIssues then
                     [ { Name = sprintf "Required AST fields (%A)" artifact.Format
                         Status = Pass
                         Reason = None } ]
                 else
                     allIssues) }
   ```

**Files**: `src/Frank.Statecharts/Validation/Validator.fs`
**Parallel?**: Yes.

---

### Subtask T019 -- Isolated state warning rule

**Purpose**: Detect states with no incoming or outgoing transitions and report as warnings, not failures (spec scenario 1.3).

**Steps**:
1. Add to `SelfConsistencyRules` module:
   ```fsharp
   /// Warn about states with no incoming or outgoing transitions.
   /// Isolated states may be intentional, so this is a warning (Pass with reason), not a failure.
   let isolatedStates: ValidationRule =
       { Name = "Isolated states"
         RequiredFormats = Set.empty
         Check = fun artifacts ->
             artifacts
             |> List.collect (fun artifact ->
                 let stateIds = AstHelpers.stateIdentifiers artifact.Document
                 let transitions = AstHelpers.allTransitions artifact.Document
                 let sources = transitions |> List.map (fun t -> t.Source) |> Set.ofList
                 let targets = transitions |> List.choose (fun t -> t.Target) |> Set.ofList
                 let connected = Set.union sources targets
                 let isolated = stateIds - connected

                 if Set.isEmpty isolated then
                     [ { Name = sprintf "Isolated states (%A)" artifact.Format
                         Status = Pass
                         Reason = None } ]
                 else
                     isolated
                     |> Set.toList
                     |> List.map (fun stateId ->
                         { Name = sprintf "Isolated state '%s' (%A)" stateId artifact.Format
                           Status = Pass  // WARNING, not failure
                           Reason = Some (sprintf "State '%s' has no incoming or outgoing transitions in %A artifact (may be intentional)" stateId artifact.Format) })) }
   ```

**CRITICAL**: This rule uses `Status = Pass` with a `Reason`, NOT `Status = Fail`. Per spec scenario 1.3: "the report contains a warning (not a failure) about the isolated state, since isolated states may be intentional."

**Files**: `src/Frank.Statecharts/Validation/Validator.fs`
**Parallel?**: Yes.

---

### Subtask T020 -- Empty statechart warning rule

**Purpose**: Warn when an artifact has an empty state machine (no states, no transitions) per edge case spec.

**Steps**:
1. Add to `SelfConsistencyRules` module:
   ```fsharp
   /// Warn about artifacts with empty state machines (no states, no transitions).
   let emptyStatechart: ValidationRule =
       { Name = "Empty statechart"
         RequiredFormats = Set.empty
         Check = fun artifacts ->
             artifacts
             |> List.collect (fun artifact ->
                 let states = AstHelpers.allStates artifact.Document
                 let transitions = AstHelpers.allTransitions artifact.Document
                 if List.isEmpty states && List.isEmpty transitions then
                     [ { Name = sprintf "Empty statechart (%A)" artifact.Format
                         Status = Pass  // WARNING, not failure
                         Reason = Some (sprintf "%A artifact contains no states and no transitions" artifact.Format) } ]
                 else
                     [ { Name = sprintf "Empty statechart (%A)" artifact.Format
                         Status = Pass
                         Reason = None } ]) }
   ```

**Files**: `src/Frank.Statecharts/Validation/Validator.fs`
**Parallel?**: Yes.
**Notes**: Like isolated states, this is a warning (Pass with Reason), not a failure. The artifact passes structural validation but the warning alerts the developer.

---

### Subtask T021 -- Create `SelfConsistencyRules` module aggregate

**Purpose**: Expose a `rules` value aggregating all universal self-consistency rules for easy registration.

**Steps**:
1. Add to the `SelfConsistencyRules` module (at the bottom):
   ```fsharp
   /// All universal self-consistency rules.
   let rules: ValidationRule list =
       [ orphanTransitionTargets
         duplicateStateIdentifiers
         requiredAstFields
         isolatedStates
         emptyStatechart ]
   ```

**Files**: `src/Frank.Statecharts/Validation/Validator.fs`
**Notes**: Consumer can use `SelfConsistencyRules.rules` to register all universal rules at once, or pick individual rules.

---

## Risks & Mitigations

- **Isolated state vs failure confusion**: The spec is explicit -- isolated states are warnings (Pass), not failures. Verify this in tests.
- **Empty string detection**: Use `System.String.IsNullOrWhiteSpace` for empty/whitespace-only identifiers.
- **Circular transitions**: A state machine with A->B->C->A is valid and should not cause infinite loops. The traversal functions in `AstHelpers` do not follow transitions, so this is not a concern for traversal. Validation rules should not follow transition chains either.
- **Performance**: Rules iterate all artifacts and all states/transitions. For the target scale (20 states, 50 transitions, 5 formats), this is well within the 1-second performance goal.

---

## Review Guidance

- Verify isolated state rule uses `Pass` status (not `Fail`) per spec scenario 1.3.
- Verify empty statechart rule uses `Pass` status with warning reason.
- Verify orphan target rule correctly computes `stateIds - transitionTargets` (not the reverse).
- Verify duplicate detection handles the case where a state ID appears 3+ times.
- Verify required fields check covers both empty state identifiers and empty transition sources.
- Verify all rules use `RequiredFormats = Set.empty` (universal).
- Verify `SelfConsistencyRules.rules` contains all 5 rules.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:11Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T04:31:44Z – claude-opus – shell_pid=14982 – lane=doing – Started implementation via workflow command
- 2026-03-16T14:34:44Z – claude-opus – shell_pid=14982 – lane=planned – Moved to planned
