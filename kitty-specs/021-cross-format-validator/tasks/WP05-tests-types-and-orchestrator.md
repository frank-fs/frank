---
work_package_id: WP05
title: Validation Tests - Types & Orchestrator
lane: "doing"
dependencies:
- WP01
base_branch: 021-cross-format-validator-WP01
base_commit: 2ab90f1895bb7a4cfe04bd5469803e2d8c4db322
created_at: '2026-03-16T11:46:03.721226+00:00'
subtasks:
- T028
- T029
- T030
- T031
- T032
- T033
- T034
phase: Phase 2 - Testing
assignee: ''
agent: "claude-opus"
shell_pid: "59373"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-15T23:59:11Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-002, FR-003, FR-004, FR-005, FR-006, FR-007, FR-008, FR-009, FR-013]
---

# Work Package Prompt: WP05 -- Validation Tests - Types & Orchestrator

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
spec-kitty implement WP05 --base WP02
```

Depends on WP01 and WP02 (types and orchestrator must exist to test).
Can start in parallel with WP03/WP04 since it tests infrastructure, not rules.

---

## Objectives & Success Criteria

- Write Expecto tests for validation domain types (construction, structural equality).
- Write Expecto tests for orchestrator behavior (rule execution, skipping, exception handling, aggregation).
- Write edge case tests (empty artifact set, all rules skipped, empty statechart, Unicode identifiers).
- Update test project file with new test files.
- **Success**: `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` passes all new tests.

---

## Context & Constraints

- **Test Framework**: Expecto 10.2.3, YoloDev.Expecto.TestSdk 0.14.3 (matches existing project)
- **Test Pattern**: Use `testList` and `testCase` patterns from existing test files (e.g., `test/Frank.Statecharts.Tests/TypeTests.fs`)
- **Spec Edge Cases**: `kitty-specs/021-cross-format-validator/spec.md` -- edge cases section
- **Data Model**: `kitty-specs/021-cross-format-validator/data-model.md` -- report field semantics

### Key Constraints
- Tests target `net10.0` only (matching existing test project).
- Use Expecto `Expect.equal`, `Expect.isTrue`, etc. for assertions.
- Build test helper functions for creating mock `StatechartDocument`, `FormatArtifact`, and `ValidationRule` values.
- Test files go in `test/Frank.Statecharts.Tests/Validation/` directory.

---

## Subtasks & Detailed Guidance

### Subtask T028 -- Create `Validation/` test directory

**Purpose**: Establish directory structure for validation test files.

**Steps**:
1. Create directory `test/Frank.Statecharts.Tests/Validation/`.
2. Verify the directory exists.

**Files**: `test/Frank.Statecharts.Tests/Validation/` (new directory)
**Parallel?**: No -- must be done first.

---

### Subtask T029 -- Type construction and equality tests

**Purpose**: Verify that all validation types can be constructed and that structural equality works correctly.

**Steps**:
1. Create `test/Frank.Statecharts.Tests/Validation/TypeTests.fs`:
   ```fsharp
   module Frank.Statecharts.Tests.Validation.TypeTests

   open Expecto
   open Frank.Statecharts.Validation
   open Frank.Statecharts.Ast

   // Test helper: create a minimal StatechartDocument
   let emptyDocument : StatechartDocument =
       { Title = None
         InitialStateId = None
         Elements = []
         DataEntries = []
         Annotations = [] }

   [<Tests>]
   let typeTests = testList "Validation.Types" [

       testCase "FormatTag cases are distinct" <| fun () ->
           Expect.notEqual Wsd Alps "Wsd <> Alps"
           Expect.notEqual Scxml Smcat "Scxml <> Smcat"
           Expect.notEqual XState Wsd "XState <> Wsd"

       testCase "FormatTag structural equality" <| fun () ->
           Expect.equal Wsd Wsd "Same tag should be equal"
           Expect.equal XState XState "Same tag should be equal"

       testCase "FormatArtifact construction" <| fun () ->
           let artifact = { Format = Scxml; Document = emptyDocument }
           Expect.equal artifact.Format Scxml "Format should be Scxml"

       testCase "CheckStatus cases" <| fun () ->
           Expect.notEqual Pass Fail "Pass <> Fail"
           Expect.notEqual Fail Skip "Fail <> Skip"
           Expect.notEqual Pass Skip "Pass <> Skip"

       testCase "ValidationCheck construction" <| fun () ->
           let check = { Name = "test check"; Status = Pass; Reason = None }
           Expect.equal check.Status Pass "Status should be Pass"
           Expect.isNone check.Reason "Reason should be None"

       testCase "ValidationCheck with reason" <| fun () ->
           let check = { Name = "skipped check"; Status = Skip; Reason = Some "Missing SCXML" }
           Expect.equal check.Status Skip "Status should be Skip"
           Expect.isSome check.Reason "Reason should be Some"

       testCase "ValidationFailure construction" <| fun () ->
           let failure =
               { Formats = [ Scxml; XState ]
                 EntityType = "state name"
                 Expected = "waiting"
                 Actual = "pending"
                 Description = "State name mismatch" }
           Expect.equal failure.Formats [ Scxml; XState ] "Formats should match"
           Expect.equal failure.EntityType "state name" "EntityType should match"

       testCase "ValidationReport construction" <| fun () ->
           let report =
               { TotalChecks = 5
                 TotalSkipped = 2
                 TotalFailures = 1
                 Checks = []
                 Failures = [] }
           Expect.equal report.TotalChecks 5 "TotalChecks should be 5"

       testCase "ValidationRule construction" <| fun () ->
           let rule =
               { Name = "test rule"
                 RequiredFormats = set [ Scxml; XState ]
                 Check = fun _ -> [] }
           Expect.equal rule.Name "test rule" "Name should match"
           Expect.equal rule.RequiredFormats (set [ Scxml; XState ]) "RequiredFormats should match"
   ]
   ```

**Files**: `test/Frank.Statecharts.Tests/Validation/TypeTests.fs` (new file)
**Parallel?**: Yes -- independent from T030-T033.

---

### Subtask T030 -- Orchestrator execution tests

**Purpose**: Test that `Validator.validate` correctly executes rules and aggregates results.

**Steps**:
1. Create `test/Frank.Statecharts.Tests/Validation/ValidatorTests.fs`:
   ```fsharp
   module Frank.Statecharts.Tests.Validation.ValidatorTests

   open Expecto
   open Frank.Statecharts.Validation
   open Frank.Statecharts.Ast
   ```

2. Add test helper for creating test documents and artifacts (shared across tests in this file).

3. Add tests:
   - **Single passing rule**: One rule that returns `[{ Name = "test"; Status = Pass; Reason = None }]`. Verify report has `TotalChecks = 1`, `TotalFailures = 0`.
   - **Single failing rule**: One rule that returns a `Fail` check. Verify `TotalChecks = 1`, `TotalFailures = 1`.
   - **Multiple rules**: Two rules, one passes, one fails. Verify aggregation: `TotalChecks = 2`, `TotalFailures = 1`.
   - **Rule returning multiple checks**: One rule returns 3 checks (2 pass, 1 fail). Verify counts.
   - **Empty rule list**: No rules, some artifacts. Verify report has all zeros.

**Files**: `test/Frank.Statecharts.Tests/Validation/ValidatorTests.fs` (new file)
**Parallel?**: Yes -- independent from T029.

---

### Subtask T031 -- Orchestrator skip tests

**Purpose**: Test that rules are correctly skipped when required format artifacts are missing (FR-007).

**Steps**:
1. Add to `ValidatorTests.fs`:
   - **Rule with missing format**: Rule requires `[Scxml; XState]`, only `Scxml` artifact provided. Verify: `TotalSkipped = 1`, skip check has reason mentioning "XState".
   - **Rule with all formats present**: Same rule, both artifacts provided. Verify: rule executes, `TotalSkipped = 0`.
   - **Universal rule (empty RequiredFormats)**: Rule with `Set.empty`. Verify: always executes regardless of artifacts.
   - **Multiple rules, some skipped**: Mix of applicable and non-applicable rules. Verify correct skip counts.

**Files**: `test/Frank.Statecharts.Tests/Validation/ValidatorTests.fs`
**Parallel?**: Yes.

---

### Subtask T032 -- Exception handling tests (FR-013)

**Purpose**: Verify that exceptions from validation rules are caught and reported as failures without crashing the validator.

**Steps**:
1. Add to `ValidatorTests.fs`:
   - **Rule that throws**: Create a rule whose `Check` throws `System.InvalidOperationException("test error")`. Verify: report contains a `Fail` check with the rule name, `Failures` list contains a `ValidationFailure` with the exception message.
   - **Rule that throws does not prevent other rules**: Two rules, first throws, second passes. Verify both are in the report.
   - **Different exception types**: Test with `System.ArgumentException`, `System.NullReferenceException` to ensure all are caught.

**Files**: `test/Frank.Statecharts.Tests/Validation/ValidatorTests.fs`
**Parallel?**: Yes.
**Notes**: Verify the failure's `Description` contains the rule name AND the exception message. Verify it does NOT crash the test runner.

---

### Subtask T033 -- Edge case tests

**Purpose**: Cover edge cases from the spec: empty artifact set, all rules skipped, empty statechart, Unicode identifiers.

**Steps**:
1. Add to `ValidatorTests.fs`:
   - **Empty artifact set**: Rules registered but no artifacts. Verify all rules skipped (if they have RequiredFormats) or executed with empty list (if universal).
   - **No artifacts, no rules**: Both empty. Verify: `TotalChecks = 0`, `TotalSkipped = 0`, `TotalFailures = 0`, `Checks = []`, `Failures = []`.
   - **All rules skipped**: All rules require formats not in the artifact set. Verify: `TotalChecks = 0`, `TotalSkipped > 0`.
   - **Unicode identifiers**: Create artifacts with Unicode state names (e.g., "\u00e9tat", "\u72b6\u6001"). Verify validation handles them correctly in comparisons and does not throw.
   - **Report field consistency**: Verify `TotalChecks` = count of Pass + Fail in `Checks`. Verify `TotalSkipped` = count of Skip in `Checks`. Verify `TotalFailures = Failures.Length`.

**Files**: `test/Frank.Statecharts.Tests/Validation/ValidatorTests.fs`
**Parallel?**: Yes.

---

### Subtask T034 -- Update test `.fsproj` for new files

**Purpose**: Add new test files to the test project compile order.

**Steps**:
1. Open `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`.
2. Add compile entries for the new test files. Insert BEFORE `Program.fs` (which must be last for Expecto):
   ```xml
   <Compile Include="Validation/TypeTests.fs" />
   <Compile Include="Validation/ValidatorTests.fs" />
   ```
3. Verify: `dotnet build test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
4. Run: `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`

**Files**: `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`

---

## Risks & Mitigations

- **Mock AST complexity**: Building test `StatechartDocument` values requires many fields. Mitigation: Create a shared helper module/function that builds minimal documents with sensible defaults.
- **Test isolation**: Each test should be independent. Do not share mutable state between tests.
- **Compile order**: F# test files must be in dependency order in the `.fsproj`. `TypeTests.fs` does not depend on `ValidatorTests.fs`, but both depend on the source project.

---

## Review Guidance

- Verify test helper functions produce valid `StatechartDocument` instances.
- Verify exception handling tests actually trigger exceptions (not just test happy paths).
- Verify report field consistency checks (TotalChecks = Pass + Fail count, etc.).
- Verify edge cases cover all items from spec edge cases section.
- Verify `dotnet test` passes.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:11Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T11:46:04Z – claude-opus – shell_pid=59373 – lane=doing – Assigned agent via workflow command
