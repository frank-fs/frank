---
work_package_id: WP06
title: Validation Tests - Rules
lane: planned
dependencies:
- WP03
- WP05
subtasks:
- T035
- T036
- T037
- T038
- T039
- T040
- T041
- T042
- T043
phase: Phase 2 - Testing
assignee: ''
agent: ''
shell_pid: ''
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-15T23:59:11Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-010, FR-011, FR-012, FR-014, FR-015]
---

# Work Package Prompt: WP06 -- Validation Tests - Rules

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````bash`

---

## Implementation Command

```bash
spec-kitty implement WP06 --base WP05
```

Depends on WP03 (self-consistency rules), WP04 (cross-format rules), and WP05 (test infrastructure).

---

## Objectives & Success Criteria

- Write comprehensive Expecto tests for all self-consistency validation rules.
- Write comprehensive Expecto tests for all cross-format validation rules.
- Cover all acceptance scenarios from the spec.
- **Success**: `dotnet test` passes all rule-specific tests. Every acceptance scenario from spec user stories 1-5 is covered.

---

## Context & Constraints

- **Spec Acceptance Scenarios**: `kitty-specs/021-cross-format-validator/spec.md` -- all scenarios from User Stories 1-5 and Edge Cases
- **Test Framework**: Expecto 10.2.3 (same as WP05)
- **Test Helpers**: Reuse helpers created in WP05 (mock `StatechartDocument` builders)

### Key Test Scenarios (from spec)

**Self-Consistency (User Story 1)**:
1. Valid SCXML artifact -> all checks pass
2. Orphan target "review" -> failure report
3. Isolated state in smcat -> warning, not failure
4. Empty state identifier -> required field failure

**Cross-Format (User Stories 2-3)**:
5. ALPS and XState with matching events -> pass
6. XState has "submitMove" missing from ALPS -> failure
7. ALPS target "gameOver" not in XState -> failure
8. All 5 formats consistent -> all pass
9. 3 of 5 formats present -> applicable checks run, others skipped
10. smcat extra state "maintenance" not in SCXML -> failure for smcat-SCXML, pass for SCXML-XState

**Edge Cases**:
11. "Active" vs "active" -> casing mismatch failure with note
12. Circular transitions (A->B->C->A) -> valid, no infinite loop
13. Unicode state names handled correctly
14. Duplicate state identifiers within artifact -> failure

---

## Subtasks & Detailed Guidance

### Subtask T035 -- Self-consistency tests: orphan transition targets

**Purpose**: Test the orphan transition target rule (spec scenarios 1.1, 1.2).

**Steps**:
1. Create `test/Frank.Statecharts.Tests/Validation/SelfConsistencyTests.fs`:
   ```fsharp
   module Frank.Statecharts.Tests.Validation.SelfConsistencyTests

   open Expecto
   open Frank.Statecharts.Validation
   open Frank.Statecharts.Ast
   ```

2. Create test helper functions for building documents:
   ```fsharp
   let makeState id = { Identifier = id; Label = None; Kind = Regular; Children = []; Activities = None; Position = None; Annotations = [] }
   let makeTransition source target event = { Source = source; Target = target; Event = event; Guard = None; Action = None; Parameters = []; Position = None; Annotations = [] }
   let makeDocument states transitions =
       { Title = None; InitialStateId = None
         Elements =
           (states |> List.map (fun s -> StateDecl (makeState s)))
           @ (transitions |> List.map (fun (s, t, e) -> TransitionElement (makeTransition s t e)))
         DataEntries = []; Annotations = [] }
   ```

3. Write tests:
   - **All targets valid**: Document with states ["idle"; "active"; "done"], transitions [("idle", Some "active", Some "start"); ("active", Some "done", Some "finish")]. Run `SelfConsistencyRules.orphanTransitionTargets`. Verify all checks pass.
   - **Orphan target "review"**: Document with states ["idle"; "active"], transition ("active", Some "review", Some "submit"). Verify failure identifying "review" as orphan.
   - **Internal transition (None target)**: Transition with `Target = None`. Verify no orphan failure (None targets are filtered out by `transitionTargets`).
   - **Multiple orphan targets**: Two transitions targeting nonexistent states. Verify both are reported.

**Files**: `test/Frank.Statecharts.Tests/Validation/SelfConsistencyTests.fs` (new file)
**Parallel?**: Yes -- independent from T036-T038.

---

### Subtask T036 -- Self-consistency tests: duplicate state identifiers

**Purpose**: Test the duplicate state identifier rule (edge case spec).

**Steps**:
1. Add to `SelfConsistencyTests.fs`:
   - **No duplicates**: Document with unique state IDs. Verify pass.
   - **One duplicate**: Document with states ["idle"; "active"; "idle"]. Verify failure identifying "idle" as duplicate.
   - **Multiple duplicates**: Document with ["a"; "b"; "a"; "b"; "c"]. Verify both "a" and "b" reported.
   - **Triple duplicate**: State "x" appears 3 times. Verify reported once (not multiple times).

**Files**: `test/Frank.Statecharts.Tests/Validation/SelfConsistencyTests.fs`
**Parallel?**: Yes.

---

### Subtask T037 -- Self-consistency tests: required AST fields

**Purpose**: Test the required AST fields rule (spec scenario 1.4).

**Steps**:
1. Add to `SelfConsistencyTests.fs`:
   - **All fields populated**: Normal document. Verify pass.
   - **Empty state identifier**: State with `Identifier = ""`. Verify failure.
   - **Whitespace-only state identifier**: State with `Identifier = "  "`. Verify failure.
   - **Empty transition source**: Transition with `Source = ""`. Verify failure.
   - **Multiple missing fields**: Both empty state ID and empty transition source. Verify both reported.

**Files**: `test/Frank.Statecharts.Tests/Validation/SelfConsistencyTests.fs`
**Parallel?**: Yes.

---

### Subtask T038 -- Self-consistency tests: isolated states and empty statechart

**Purpose**: Test isolated state warnings and empty statechart warnings (spec scenario 1.3, edge cases).

**Steps**:
1. Add to `SelfConsistencyTests.fs`:
   - **Isolated state**: Document with states ["idle"; "active"; "orphan"], transitions only between idle and active. Verify "orphan" gets a `Pass` check with reason (warning), NOT a `Fail`.
   - **No isolated states**: All states connected by transitions. Verify no warning.
   - **Empty statechart**: Document with no states and no transitions. Verify `Pass` check with warning reason about empty state machine.
   - **Circular transitions**: States ["A"; "B"; "C"] with transitions A->B, B->C, C->A. Verify all states are connected (no isolated state warning). Verify no infinite loop.

**Files**: `test/Frank.Statecharts.Tests/Validation/SelfConsistencyTests.fs`
**Parallel?**: Yes.
**Notes**: Critical -- verify warnings use `Pass` status, not `Fail`. This is the most common mistake.

---

### Subtask T039 -- Cross-format tests: state name agreement

**Purpose**: Test state name agreement between format pairs (spec scenario 2.3).

**Steps**:
1. Create `test/Frank.Statecharts.Tests/Validation/CrossFormatTests.fs`:
   ```fsharp
   module Frank.Statecharts.Tests.Validation.CrossFormatTests

   open Expecto
   open Frank.Statecharts.Validation
   open Frank.Statecharts.Ast
   ```

2. Write tests:
   - **Matching state names**: SCXML and XState artifacts with same states ["idle"; "active"; "done"]. Verify pass.
   - **Missing state in format B**: SCXML has ["idle"; "active"; "gameOver"], XState has ["idle"; "active"]. Verify failure: "gameOver" missing from XState.
   - **Missing state in format A**: XState has extra state "maintenance" not in SCXML. Verify failure: "maintenance" missing from SCXML.
   - **Symmetric reporting**: Both formats have unique states. Verify both directions reported.
   - **Failure contains correct formats**: Verify `Reason` identifies SCXML and XState by name.

**Files**: `test/Frank.Statecharts.Tests/Validation/CrossFormatTests.fs` (new file)
**Parallel?**: Yes -- independent from T040-T042.

---

### Subtask T040 -- Cross-format tests: event name agreement

**Purpose**: Test event name agreement between format pairs (spec scenarios 2.1, 2.2).

**Steps**:
1. Add to `CrossFormatTests.fs`:
   - **Matching events**: ALPS and XState artifacts with same events ["submitMove"; "reset"]. Verify pass.
   - **Missing event "submitMove"**: XState has "submitMove" but ALPS does not. Verify failure identifying formats (ALPS, XState), entity type (event), and the missing event name.
   - **Empty events in one format**: One artifact has no transitions (no events). Verify: if other has events, report them as missing.
   - **Both have no events**: Both artifacts have empty event sets. Verify pass (nothing to compare).

**Files**: `test/Frank.Statecharts.Tests/Validation/CrossFormatTests.fs`
**Parallel?**: Yes.

---

### Subtask T041 -- Cross-format tests: casing mismatch detection

**Purpose**: Test that casing differences are detected and explicitly noted (D-010, edge case spec).

**Steps**:
1. Add to `CrossFormatTests.fs`:
   - **Casing mismatch "Active" vs "active"**: SCXML has state "Active", XState has state "active". Verify: failure is reported (case-sensitive comparison means these don't match), AND the failure reason/description explicitly mentions the casing difference.
   - **Exact match not flagged**: SCXML and XState both have "Active". Verify pass (no casing note).
   - **Multiple casing mismatches**: "Active"/"active" AND "Done"/"done". Verify both flagged with casing notes.
   - **Event casing mismatch**: Event "Submit" vs "submit". Same behavior as state name casing.

**Files**: `test/Frank.Statecharts.Tests/Validation/CrossFormatTests.fs`
**Parallel?**: Yes.
**Notes**: This is critical for the edge case spec requirement. Verify the failure description contains a casing-specific message.

---

### Subtask T042 -- Cross-format tests: multi-format validation

**Purpose**: Test validation with 3+ formats, including skip behavior for missing formats (User Story 3).

**Steps**:
1. Add to `CrossFormatTests.fs`:
   - **All 5 formats consistent**: Create artifacts for all 5 formats with identical states and events. Register all cross-format rules. Verify: all checks pass, zero skipped (all formats present).
   - **3 of 5 formats (SCXML, XState, smcat)**: Register all rules. Verify: SCXML-XState, SCXML-Smcat, Smcat-XState rules execute. Rules requiring ALPS or WSD are skipped with reason.
   - **Partial mismatch**: SCXML-XState agree, but smcat has extra state "maintenance". Verify: SCXML-XState passes, smcat-SCXML and smcat-XState fail for "maintenance".
   - **No artifacts**: Register rules, provide no artifacts. Verify all rules skipped.
   - **Single artifact**: Only SCXML provided. All cross-format rules skipped (need 2 formats). Self-consistency rules still run.

**Files**: `test/Frank.Statecharts.Tests/Validation/CrossFormatTests.fs`
**Parallel?**: Yes.

---

### Subtask T043 -- Update test `.fsproj` for rule test files

**Purpose**: Add self-consistency and cross-format test files to the test project.

**Steps**:
1. Open `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`.
2. Add compile entries (after the entries added in WP05 T034, before `Program.fs`):
   ```xml
   <Compile Include="Validation/SelfConsistencyTests.fs" />
   <Compile Include="Validation/CrossFormatTests.fs" />
   ```
3. Verify: `dotnet build test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
4. Run: `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`

**Files**: `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`

---

## Risks & Mitigations

- **Test data complexity**: Building 5-format test data sets is verbose. Mitigation: Create a tic-tac-toe state machine builder that generates consistent documents for any format tag.
- **Casing test sensitivity**: Ensure casing tests use platform-independent comparisons (OrdinalIgnoreCase). F# string comparison is ordinal by default.
- **Test ordering**: Expecto tests should be independent. Do not rely on test execution order.

---

## Review Guidance

- Verify all spec acceptance scenarios (1.1-1.4, 2.1-2.3, 3.1-3.3) are covered by tests.
- Verify isolated state tests assert `Pass` status (not `Fail`).
- Verify casing mismatch tests assert the failure description explicitly mentions casing.
- Verify multi-format tests correctly identify which rules are skipped and which execute.
- Verify `dotnet test` passes all tests.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:11Z -- system -- lane=planned -- Prompt created.
