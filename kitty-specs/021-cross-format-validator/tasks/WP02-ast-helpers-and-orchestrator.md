---
work_package_id: WP02
title: AST Helpers & Validator Orchestrator
lane: "doing"
dependencies: [WP01]
base_branch: 021-cross-format-validator-WP01
base_commit: 2ab90f1895bb7a4cfe04bd5469803e2d8c4db322
created_at: '2026-03-16T04:02:24.037386+00:00'
subtasks:
- T009
- T010
- T011
- T012
- T013
- T014
- T015
phase: Phase 0 - Foundation
assignee: ''
agent: "claude-opus"
shell_pid: "97995"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-15T23:59:11Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-006, FR-007, FR-008, FR-009, FR-013, FR-016]
---

# Work Package Prompt: WP02 -- AST Helpers & Validator Orchestrator

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
spec-kitty implement WP02 --base WP01
```

Depends on WP01 (validation domain types must exist).

---

## Objectives & Success Criteria

- Implement `AstHelpers` module with 5 shared AST traversal functions.
- Implement `Validator.validate` orchestrator function with skip logic, exception handling, and report aggregation.
- All functions match the contract signatures in `contracts/validation-api.fsi`.
- **Success**: `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` succeeds. The orchestrator can be called with rules and artifacts to produce a `ValidationReport`.

---

## Context & Constraints

- **Spec**: `kitty-specs/021-cross-format-validator/spec.md` -- FR-006 through FR-009, FR-013
- **Plan**: `kitty-specs/021-cross-format-validator/plan.md` -- orchestrator behavior, structure
- **Data Model**: `kitty-specs/021-cross-format-validator/data-model.md` -- `Validator.validate` behavior, `AstHelpers` signatures
- **Contract**: `kitty-specs/021-cross-format-validator/contracts/validation-api.fsi` -- exact function signatures
- **Research**: `kitty-specs/021-cross-format-validator/research.md` -- D-004 (exception handling), D-005 (empty artifacts), D-006 (AST helpers), D-009 (rules receive full list)

### Key Constraints
- Namespace: `Frank.Statecharts.Validation`
- `AstHelpers` must recursively traverse `StatechartElement` tree, including `GroupBlock.Branches` and `StateNode.Children`.
- `Validator.validate` is a pure function: no mutable state, no side effects.
- Exception handling (FR-013): catch exceptions from rule `Check` functions, report as `Fail` check + `ValidationFailure` with rule name and `exn.Message`. This is NOT silent swallowing -- the error is surfaced.
- `TotalChecks` excludes Skip checks. `TotalFailures` = count of Fail.

### AST Structure Reference (from spec 020 data model)

The `StatechartDocument.Elements` is a `StatechartElement list` where:
- `StateDecl of StateNode` -- states (may have `Children: StateNode list`)
- `TransitionElement of TransitionEdge` -- transitions
- `NoteElement of NoteContent` -- notes
- `GroupElement of GroupBlock` -- groups with `Branches: GroupBranch list`, each branch has `Elements: StatechartElement list`
- `DirectiveElement of Directive` -- directives

Recursion strategy: walk `Elements`, for each `StateDecl` recurse into `Children`, for each `GroupElement` recurse into each branch's `Elements`.

---

## Subtasks & Detailed Guidance

### Subtask T009 -- Implement `AstHelpers.allStates`

**Purpose**: Extract all `StateNode` values from a `StatechartDocument`, recursively including children and states nested in `GroupBlock` branches (D-006).

**Steps**:
1. Create file `src/Frank.Statecharts/Validation/Validator.fs`.
2. Add namespace and module:
   ```fsharp
   namespace Frank.Statecharts.Validation

   open Frank.Statecharts.Ast

   module AstHelpers =
   ```
3. Implement `allStates`:
   ```fsharp
   /// Extract all StateNode values from a document, recursively including
   /// children and states nested in GroupBlock branches.
   let allStates (doc: StatechartDocument) : StateNode list =
       let rec collectFromElements (elements: StatechartElement list) =
           elements
           |> List.collect (fun elem ->
               match elem with
               | StateDecl state ->
                   state :: collectFromChildren state
               | GroupElement group ->
                   group.Branches
                   |> List.collect (fun branch -> collectFromElements branch.Elements)
               | _ -> [])
       and collectFromChildren (state: StateNode) =
           state.Children
           |> List.collect (fun child -> child :: collectFromChildren child)
       collectFromElements doc.Elements
   ```

**Files**: `src/Frank.Statecharts/Validation/Validator.fs` (new file)
**Parallel?**: Can be developed alongside T010-T013 (all in same file).
**Notes**: Must handle deeply nested states (children of children). Must handle states inside `GroupBlock` branches.

---

### Subtask T010 -- Implement `AstHelpers.allTransitions`

**Purpose**: Extract all `TransitionEdge` values from a document, including those nested in `GroupBlock` branches.

**Steps**:
1. Add to `AstHelpers` module in `Validator.fs`:
   ```fsharp
   /// Extract all TransitionEdge values from a document, including those
   /// nested in GroupBlock branches.
   let allTransitions (doc: StatechartDocument) : TransitionEdge list =
       let rec collectFromElements (elements: StatechartElement list) =
           elements
           |> List.collect (fun elem ->
               match elem with
               | TransitionElement edge -> [ edge ]
               | GroupElement group ->
                   group.Branches
                   |> List.collect (fun branch -> collectFromElements branch.Elements)
               | _ -> [])
       collectFromElements doc.Elements
   ```

**Files**: `src/Frank.Statecharts/Validation/Validator.fs`
**Notes**: Transitions do not nest inside states (they are top-level elements or inside groups). No need to recurse into `StateNode.Children` for transitions.

---

### Subtask T011 -- Implement `AstHelpers.stateIdentifiers`

**Purpose**: Extract the set of all state identifiers from a document, for use in cross-reference checks.

**Steps**:
1. Add to `AstHelpers` module:
   ```fsharp
   /// Extract the set of all state identifiers from a document.
   let stateIdentifiers (doc: StatechartDocument) : string Set =
       allStates doc
       |> List.map (fun s -> s.Identifier)
       |> Set.ofList
   ```

**Files**: `src/Frank.Statecharts/Validation/Validator.fs`
**Notes**: Depends on `allStates` (T009). Simple projection.

---

### Subtask T012 -- Implement `AstHelpers.eventNames`

**Purpose**: Extract the set of all event names from transitions, filtering out `None` events.

**Steps**:
1. Add to `AstHelpers` module:
   ```fsharp
   /// Extract the set of all event names from transitions in a document.
   /// Filters out None events.
   let eventNames (doc: StatechartDocument) : string Set =
       allTransitions doc
       |> List.choose (fun t -> t.Event)
       |> Set.ofList
   ```

**Files**: `src/Frank.Statecharts/Validation/Validator.fs`
**Notes**: Depends on `allTransitions` (T010). Uses `List.choose` to filter `None`.

---

### Subtask T013 -- Implement `AstHelpers.transitionTargets`

**Purpose**: Extract the set of all transition target identifiers, filtering out `None` (internal/completion) targets.

**Steps**:
1. Add to `AstHelpers` module:
   ```fsharp
   /// Extract the set of all transition target identifiers from a document.
   /// Filters out None (internal/completion) targets.
   let transitionTargets (doc: StatechartDocument) : string Set =
       allTransitions doc
       |> List.choose (fun t -> t.Target)
       |> Set.ofList
   ```

**Files**: `src/Frank.Statecharts/Validation/Validator.fs`
**Notes**: Depends on `allTransitions` (T010). Parallel structure to `eventNames`.

---

### Subtask T014 -- Implement `Validator.validate` orchestrator function

**Purpose**: The core orchestration function that runs validation rules against artifacts, handling skips, exceptions, and report aggregation (FR-006, FR-007, FR-008, FR-009, FR-013, D-004, D-005).

**Steps**:
1. Add a new module after `AstHelpers` in `Validator.fs`:
   ```fsharp
   module Validator =

       /// Validate statechart artifacts against registered rules.
       /// Collects all results without aborting on first failure.
       /// Catches exceptions from rules and reports them as failures.
       let validate (rules: ValidationRule list) (artifacts: FormatArtifact list) : ValidationReport =
           let availableTags =
               artifacts |> List.map (fun a -> a.Format) |> Set.ofList

           let executeRule (rule: ValidationRule) =
               let missingFormats = rule.RequiredFormats - availableTags
               if not (Set.isEmpty missingFormats) then
                   let missingStr =
                       missingFormats
                       |> Set.toList
                       |> List.map (sprintf "%A")
                       |> String.concat ", "
                   let skipCheck =
                       { Name = rule.Name
                         Status = Skip
                         Reason = Some (sprintf "Missing formats: %s" missingStr) }
                   [ skipCheck ], []
               else
                   try
                       let checks = rule.Check artifacts
                       let failures =
                           checks
                           |> List.choose (fun c ->
                               // Failures are tracked separately by the rule
                               // The orchestrator does not create failures from checks
                               None)
                       checks, []
                   with ex ->
                       let failCheck =
                           { Name = rule.Name
                             Status = Fail
                             Reason = Some (sprintf "Rule threw exception: %s" ex.Message) }
                       let failure =
                           { Formats = []
                             EntityType = "rule execution"
                             Expected = sprintf "Rule '%s' to execute without error" rule.Name
                             Actual = sprintf "Exception: %s" ex.Message
                             Description = sprintf "Validation rule '%s' threw an exception during execution: %s" rule.Name ex.Message }
                       [ failCheck ], [ failure ]

           let allResults =
               rules |> List.map executeRule

           let allChecks =
               allResults |> List.collect fst

           let orchestratorFailures =
               allResults |> List.collect snd

           // Collect failures from check results that have Fail status
           // Rules should return failures paired with their checks, but the orchestrator
           // also needs to count Fail checks for TotalFailures
           let totalChecks =
               allChecks
               |> List.filter (fun c -> c.Status <> Skip)
               |> List.length

           let totalSkipped =
               allChecks
               |> List.filter (fun c -> c.Status = Skip)
               |> List.length

           let totalFailures =
               allChecks
               |> List.filter (fun c -> c.Status = Fail)
               |> List.length

           { TotalChecks = totalChecks
             TotalSkipped = totalSkipped
             TotalFailures = totalFailures
             Checks = allChecks
             Failures = orchestratorFailures }
   ```

**IMPORTANT DESIGN NOTE**: The orchestrator collects `ValidationFailure` records from two sources:
1. **Exception failures**: Created by the orchestrator when a rule throws (shown above).
2. **Rule-produced failures**: Rules that detect mismatches should return their own `ValidationFailure` records. However, the current `ValidationRule.Check` signature returns `ValidationCheck list`, not failures directly.

**Resolution**: Rules need a way to communicate failures. The recommended approach is:
- Rules return `ValidationCheck` with `Status = Fail` for each detected issue.
- The orchestrator's `Failures` list in the report contains only exception-generated failures.
- **OR**: Enhance the approach so rules can produce failures alongside checks. A practical solution is for rules to store failure details in the check's `Reason` field, or to have the orchestrator create `ValidationFailure` entries from `Fail` checks.

**Recommended implementation**: Have the orchestrator extract failure information from `Fail` checks. Rules encode diagnostic info in a structured way that the orchestrator can parse, OR (simpler) rules return both checks and failures via a wrapper. Since the `ValidationRule.Check` signature returns `ValidationCheck list`, the simplest approach is to have rules also append `ValidationFailure` records to a shared accumulator. However, this introduces mutable state.

**Cleanest approach**: Make rules responsible for returning failures alongside checks. Since `Check: FormatArtifact list -> ValidationCheck list` only returns checks, we have two options:
1. Keep the contract as-is. Rules put all diagnostic info in `ValidationCheck.Reason`. The orchestrator does not populate `ValidationReport.Failures` from rules -- only from exceptions. **This means `Failures` only contains exception failures, and consumers read check details from `Reason`.**
2. Change the approach to have rules store failures in a module-level convention.

**Follow the contract exactly as written**: The contract says `Check: FormatArtifact list -> ValidationCheck list`. Rules return checks. The orchestrator creates `ValidationFailure` only for exceptions. For rule-detected issues, the diagnostic info goes in `ValidationCheck.Reason`. The `Failures` list in the report may also be populated by rule-produced failures -- see the note below.

**ACTUALLY**: Looking at the data model more carefully, `ValidationReport.Failures` should contain ALL failures including rule-detected ones. The pragmatic approach: have rules produce BOTH checks and failures, and accumulate them. Since the type signature only returns checks, we need a convention. The best F# idiom: **let rules return checks, and have a separate function to extract failures from the rule's domain**. But the simplest solution that matches the contract: have the orchestrator accept that `Failures` in the report is populated ONLY by:
1. Exception-caused failures (from `try/with`).
2. A post-processing step that the CALLER can optionally add.

**FINAL RECOMMENDATION**: Keep it simple. Rules return `ValidationCheck list`. Failed checks have `Reason = Some "diagnostic details"`. The orchestrator creates `ValidationFailure` records ONLY for rule exceptions. If the downstream consumer (frank-cli) needs richer `ValidationFailure` records, rules can be enhanced later. For now, the report's `Failures` list contains exception failures only, and `TotalFailures` counts all `Fail` status checks.

**Wait -- re-reading the data model**: The data model says `TotalFailures` equals `Failures.Length`. This means every `Fail` check should have a corresponding `ValidationFailure`. Let me revise: rules should produce `ValidationFailure` records alongside their checks.

**REVISED APPROACH**: Since the contract `Check: FormatArtifact list -> ValidationCheck list` cannot carry failures, introduce a module-level helper that rules use. The cleanest F# approach: **have each rule's `Check` function return checks, and have a separate module-level list that accumulates failures**. But that requires mutable state.

**PRAGMATIC FINAL APPROACH**: Implement as follows:
- The orchestrator calls `rule.Check artifacts` to get checks.
- For each `Fail` check, the orchestrator creates a `ValidationFailure` from the check's `Reason` field.
- For exception cases, the orchestrator creates both a `Fail` check and a `ValidationFailure`.
- This way, every `Fail` check has a corresponding `ValidationFailure`, and `TotalFailures = Failures.Length`.

This requires rules to encode enough info in `Reason` for the orchestrator to create meaningful failures, or the orchestrator creates generic failures from `Fail` checks.

**FILES**: `src/Frank.Statecharts/Validation/Validator.fs`
**Notes**: This is the most complex subtask. Test thoroughly in WP05.

---

### Subtask T015 -- Update `Frank.Statecharts.fsproj` for `Validator.fs`

**Purpose**: Add `Validation/Validator.fs` to the project compile order, after `Validation/Types.fs`.

**Steps**:
1. Open `src/Frank.Statecharts/Frank.Statecharts.fsproj`.
2. Add `<Compile Include="Validation/Validator.fs" />` immediately after `<Compile Include="Validation/Types.fs" />`.
3. The updated compile order should be:
   ```xml
   <Compile Include="Ast/Types.fs" />         <!-- if created in WP01 -->
   <Compile Include="Wsd/Types.fs" />
   <Compile Include="Wsd/Lexer.fs" />
   <Compile Include="Wsd/GuardParser.fs" />
   <Compile Include="Wsd/Parser.fs" />
   <Compile Include="Validation/Types.fs" />
   <Compile Include="Validation/Validator.fs" />  <!-- NEW -->
   <Compile Include="Types.fs" />
   <!-- ... remaining files ... -->
   ```
4. Verify: `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj`

**Files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`

---

## Risks & Mitigations

- **AST traversal correctness**: `allStates` must handle nested children AND groups. Test with deeply nested structures.
- **Exception handling balance**: FR-013 requires catching exceptions but not silently swallowing. The failure includes rule name and error message -- this is surfacing, not swallowing.
- **Report aggregation arithmetic**: `TotalChecks` must exclude Skip. `TotalFailures` must equal `Failures.Length`. Verify with tests.
- **Failure propagation from rules**: The `ValidationRule.Check` signature only returns `ValidationCheck list`. The orchestrator must derive `ValidationFailure` records from `Fail` checks. See detailed guidance in T014.

---

## Review Guidance

- Verify `AstHelpers` functions handle: empty documents, nested states (children), groups with branches, mixed element types.
- Verify `Validator.validate` handles: empty rule list, empty artifact list, rules with empty RequiredFormats (universal), rules that throw, mix of pass/fail/skip.
- Verify report math: `TotalChecks = count(Pass) + count(Fail)`, `TotalSkipped = count(Skip)`, `TotalFailures = Failures.Length`.
- Verify `.fsproj` compile order is correct and `dotnet build` succeeds.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:11Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T04:02:24Z – claude-opus – shell_pid=97995 – lane=doing – Assigned agent via workflow command
