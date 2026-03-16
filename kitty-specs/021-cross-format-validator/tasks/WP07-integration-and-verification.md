---
work_package_id: WP07
title: Integration & Build Verification
lane: planned
dependencies:
- WP05
subtasks:
- T044
- T045
- T046
- T047
- T048
- T049
phase: Phase 3 - Polish
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
requirement_refs: [FR-006, FR-007, FR-008, FR-009, FR-013, FR-014, FR-015, FR-016, FR-017]
---

# Work Package Prompt: WP07 -- Integration & Build Verification

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
spec-kitty implement WP07 --base WP06
```

Depends on WP05 and WP06 (all implementation and tests must exist).

---

## Objectives & Success Criteria

- Verify end-to-end integration across all validation components.
- Verify multi-TFM build (net8.0, net9.0, net10.0) for the library project.
- Verify all existing and new tests pass.
- Write a full pipeline integration test with all 5 format artifacts.
- Write a performance benchmark (SC-003: < 1 second for 20 states, 50 transitions, 5 formats).
- Verify diagnostic output quality (SC-004).
- Final code review for compliance with spec constraints.
- **Success**: `dotnet build` succeeds for all TFMs. `dotnet test` passes all tests. Performance benchmark passes. Code review confirms no mutable state, no CLI concerns, pure data-in/data-out.

---

## Context & Constraints

- **Spec Success Criteria**: SC-001 through SC-007 from `kitty-specs/021-cross-format-validator/spec.md`
- **Constitution**: `.kittify/memory/constitution.md` -- no mutable state, no silent swallowing, no duplicated logic
- **Build Targets**: `net8.0;net9.0;net10.0` (library), `net10.0` (tests)

### Success Criteria Checklist
- [ ] SC-001: Validator identifies all intentionally introduced mismatches (10+ distinct cross-format inconsistencies)
- [ ] SC-002: Zero false positives on fully consistent 5-format artifact set
- [ ] SC-003: Validation completes in < 1 second (20 states, 50 transitions, 5 formats)
- [ ] SC-004: Every failure contains formats, entity type, expected/actual, description
- [ ] SC-005: Missing formats produce skipped checks (not false failures)
- [ ] SC-006: New rule can be registered without modifying validator module
- [ ] SC-007: Library compiles and tests pass across all supported target platforms

---

## Subtasks & Detailed Guidance

### Subtask T044 -- Verify multi-TFM build

**Purpose**: Confirm the library compiles for all three target frameworks (SC-007).

**Steps**:
1. Run: `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj`
2. Verify output shows successful build for `net8.0`, `net9.0`, and `net10.0`.
3. Fix any framework-specific compilation errors (e.g., API differences between .NET versions).
4. If `Ast/Types.fs` was created as a stub in WP01, verify it compiles cleanly on all TFMs.

**Files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
**Parallel?**: No -- do this first to catch build issues.

---

### Subtask T045 -- Verify all tests pass

**Purpose**: Run the full test suite and confirm all tests pass.

**Steps**:
1. Run: `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --verbosity normal`
2. Verify ALL tests pass, including:
   - Existing tests (TypeTests, StoreTests, MiddlewareTests, etc.)
   - New validation tests (TypeTests, ValidatorTests, SelfConsistencyTests, CrossFormatTests)
3. Fix any test failures.
4. Verify no test warnings that indicate issues.

**Files**: `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
**Parallel?**: No -- do after T044.

---

### Subtask T046 -- Full pipeline integration test

**Purpose**: Write an end-to-end integration test with all 5 format artifacts and all rules (SC-001, SC-002).

**Steps**:
1. Add an integration test to `test/Frank.Statecharts.Tests/Validation/ValidatorTests.fs` (or a new file):

2. **Test: Fully consistent 5-format artifact set (SC-002)**:
   - Create a tic-tac-toe state machine with states: ["idle"; "playerX"; "playerO"; "gameOver"]
   - Create transitions: idle->playerX (start), playerX->playerO (move), playerO->playerX (move), playerX->gameOver (win), playerO->gameOver (win)
   - Create `FormatArtifact` for all 5 formats (Wsd, Alps, Scxml, Smcat, XState) with identical documents.
   - Register all rules: `SelfConsistencyRules.rules @ CrossFormatRules.rules`
   - Run `Validator.validate`
   - Assert: zero failures, zero false positives

3. **Test: 10+ distinct cross-format inconsistencies (SC-001)**:
   - Create 5 format artifacts with intentional mismatches:
     - SCXML has extra state "review"
     - XState is missing event "start"
     - smcat has state "Idle" (casing mismatch with "idle")
     - Alps has extra state "archived"
     - Wsd is missing state "gameOver"
   - Register all rules
   - Run `Validator.validate`
   - Assert: at least 10 distinct failures identified
   - Assert: zero false negatives (all intentional mismatches detected)

4. **Test: New rule registration without modifying validator (SC-006)**:
   - Define a custom `ValidationRule` in the test file
   - Register it alongside existing rules
   - Verify it appears in the report without changing any source files

**Files**: `test/Frank.Statecharts.Tests/Validation/ValidatorTests.fs`
**Parallel?**: Yes -- independent from T047, T048.

---

### Subtask T047 -- Performance benchmark test

**Purpose**: Verify validation completes within performance target (SC-003: < 1 second for 20 states, 50 transitions, 5 formats).

**Steps**:
1. Add a performance test:
   ```fsharp
   testCase "Performance: 20 states, 50 transitions, 5 formats under 1 second" <| fun () ->
       // Generate a document with 20 states
       let states = [ for i in 1..20 -> makeState (sprintf "state%d" i) ]
       // Generate 50 transitions between random states
       let transitions =
           [ for i in 1..50 ->
               let src = sprintf "state%d" ((i % 20) + 1)
               let tgt = sprintf "state%d" (((i + 7) % 20) + 1)
               makeTransition src (Some tgt) (Some (sprintf "event%d" i)) ]
       let doc =
           { Title = None; InitialStateId = None
             Elements =
               (states |> List.map StateDecl) @ (transitions |> List.map TransitionElement)
             DataEntries = []; Annotations = [] }
       // Create 5 artifacts with the same document
       let artifacts =
           [ Wsd; Alps; Scxml; Smcat; XState ]
           |> List.map (fun tag -> { Format = tag; Document = doc })
       let allRules = SelfConsistencyRules.rules @ CrossFormatRules.rules
       let sw = System.Diagnostics.Stopwatch.StartNew()
       let _report = Validator.validate allRules artifacts
       sw.Stop()
       Expect.isLessThan sw.Elapsed.TotalSeconds 1.0
           (sprintf "Validation took %.3f seconds, expected < 1.0" sw.Elapsed.TotalSeconds)
   ```

2. Run the test and verify it passes.

**Files**: `test/Frank.Statecharts.Tests/Validation/ValidatorTests.fs`
**Parallel?**: Yes.
**Notes**: The benchmark should easily pass given the simplicity of set operations. If it fails, investigate which rules are slow.

---

### Subtask T048 -- Diagnostic output quality verification

**Purpose**: Verify every validation failure contains sufficient diagnostic information (SC-004).

**Steps**:
1. Add a test that creates artifacts with intentional mismatches, runs validation, and checks every `ValidationFailure` record:
   ```fsharp
   testCase "Diagnostic quality: all failures have complete information" <| fun () ->
       // Create artifacts with mismatches to generate failures
       // ...
       let report = Validator.validate allRules artifacts
       Expect.isGreaterThan report.TotalFailures 0 "Should have some failures to check"
       for failure in report.Failures do
           Expect.isNonEmpty failure.EntityType "EntityType should not be empty"
           Expect.isNonEmpty failure.Expected "Expected should not be empty"
           Expect.isNonEmpty failure.Actual "Actual should not be empty"
           Expect.isNonEmpty failure.Description "Description should not be empty"
           Expect.isNonEmpty failure.Formats "Formats should not be empty"
   ```

2. Also verify that `Fail` checks have meaningful `Reason` values:
   ```fsharp
   for check in report.Checks do
       if check.Status = Fail then
           Expect.isSome check.Reason "Failed checks should have a Reason"
   ```

**Files**: `test/Frank.Statecharts.Tests/Validation/ValidatorTests.fs`
**Parallel?**: Yes.

---

### Subtask T049 -- Final code review

**Purpose**: Manual review to verify the implementation adheres to all spec constraints and constitution principles.

**Steps**:
1. Review `src/Frank.Statecharts/Validation/Types.fs`:
   - All types match the contract in `contracts/validation-api.fsi`.
   - No mutable fields.
   - Correct namespace.

2. Review `src/Frank.Statecharts/Validation/Validator.fs`:
   - No mutable state (no `mutable`, no `ref`, no `ResizeArray`).
   - No CLI/presentation concerns (no `printfn`, no `Console`, no formatting for display).
   - Pure data-in/data-out (`validate` takes rules + artifacts, returns report).
   - Exception handling does NOT silently swallow -- failures include error details.
   - No duplicated traversal logic -- all rules use `AstHelpers`.

3. Review `.fsproj` files:
   - Compile order is correct.
   - No unnecessary dependencies added.

4. Verify `InternalsVisibleTo` is set correctly (already exists for `Frank.Statecharts.Tests`).

5. Document any issues found and fix them.

**Files**: All validation source and test files
**Parallel?**: No -- do this last as a final quality gate.

---

## Risks & Mitigations

- **Multi-TFM build failures**: .NET 8/9/10 APIs may differ. Mitigation: Use only APIs available across all three TFMs (which is all we use -- Set, List, string operations).
- **Performance regression**: Performance test sets a hard 1-second limit. Mitigation: The implementation uses simple set operations which should complete in milliseconds. If slow, check for accidental O(n^2) patterns.
- **Existing test breakage**: Adding new files to the project may cause compile order issues with existing tests. Mitigation: Verify all existing tests still pass after changes.

---

## Review Guidance

- Verify all 7 success criteria (SC-001 through SC-007) are addressed.
- Verify performance test uses a realistic workload (20 states, 50 transitions, 5 formats).
- Verify diagnostic quality test checks ALL failure fields, not just a subset.
- Verify the final code review checks for constitution compliance.
- Verify `dotnet build` and `dotnet test` both succeed.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:11Z -- system -- lane=planned -- Prompt created.
