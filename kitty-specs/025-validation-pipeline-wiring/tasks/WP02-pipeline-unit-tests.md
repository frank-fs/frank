---
work_package_id: "WP02"
subtasks:
  - "T008"
  - "T009"
  - "T010"
  - "T011"
  - "T012"
  - "T013"
  - "T014"
  - "T015"
  - "T016"
title: "Pipeline Unit Tests"
phase: "Phase 1 - Unit Tests"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP01"]
requirement_refs:
  - "FR-008"
  - "FR-010"
  - "FR-011"
  - "FR-012"
  - "FR-014"
history:
  - timestamp: "2026-03-16T19:13:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 -- Pipeline Unit Tests

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

Depends on WP01:

```bash
spec-kitty implement WP02 --base WP01
```

---

## Objectives & Success Criteria

1. Create `PipelineTests.fs` with comprehensive unit tests for the `Pipeline` module.
2. Cover all edge cases: empty input (FR-011), duplicate formats (FR-010), unsupported formats (FR-012).
3. Verify single-format validation runs self-consistency rules and skips cross-format rules (User Story 3).
4. Verify multi-format consistent inputs produce zero failures (User Story 1).
5. Verify parse errors are correctly attributed in `FormatParseResult` (FR-008, User Story 2).
6. Verify custom rules work via `validateSourcesWithRules` (FR-014).
7. `dotnet test --filter "Validation.Pipeline"` passes with all tests green.

## Context & Constraints

- **Spec**: `kitty-specs/025-validation-pipeline-wiring/spec.md`
- **Plan**: `kitty-specs/025-validation-pipeline-wiring/plan.md` -- Test Design section
- **Prerequisite**: WP01 must be complete (Pipeline types and module exist).
- **Test framework**: Expecto 10.2.3 with `testList`, `test`, `Expect.*` assertions.
- **Test file location**: `test/Frank.Statecharts.Tests/Validation/PipelineTests.fs`
- **Naming convention**: Module `Frank.Statecharts.Tests.Validation.PipelineTests`, test list name `"Validation.Pipeline"`.
- **Existing test patterns**: See `test/Frank.Statecharts.Tests/Validation/IntegrationTests.fs` for the established test style with `makeState`, `makeTransition`, `makeDocument`, `makeArtifact` helpers.

**Important**: These are UNIT tests that exercise the pipeline's dispatch logic and edge case handling. They may use simple/minimal source text or rely on well-known parser inputs. The end-to-end integration tests with real tic-tac-toe format text are in WP03.

## Subtasks & Detailed Guidance

### Subtask T008 -- Create PipelineTests.fs with test module and helpers

- **Purpose**: Set up the test file with module declaration, imports, and any shared helpers.
- **Steps**:
  1. Create `test/Frank.Statecharts.Tests/Validation/PipelineTests.fs`.
  2. Declare module: `module Frank.Statecharts.Tests.Validation.PipelineTests`.
  3. Open: `Expecto`, `Frank.Statecharts.Validation`, `Frank.Statecharts.Ast`.
  4. No shared helpers needed beyond what's available from imports -- each test constructs its own input.
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineTests.fs` (NEW)

### Subtask T009 -- Test: empty input returns valid PipelineResult (FR-011)

- **Purpose**: Verify that `Pipeline.validateSources []` returns a valid `PipelineResult` with empty report, empty parse results, and empty errors.
- **Steps**:
  1. Call `Pipeline.validateSources []`.
  2. Assert `result.ParseResults` is empty.
  3. Assert `result.Report.TotalChecks = 0`.
  4. Assert `result.Report.TotalSkipped = 0`.
  5. Assert `result.Report.TotalFailures = 0`.
  6. Assert `result.Errors` is empty.
- **Code example**:
  ```fsharp
  test "Empty input returns valid PipelineResult with empty report" {
      let result = Pipeline.validateSources []
      Expect.isEmpty result.ParseResults "ParseResults should be empty"
      Expect.equal result.Report.TotalChecks 0 "TotalChecks should be 0"
      Expect.equal result.Report.TotalSkipped 0 "TotalSkipped should be 0"
      Expect.equal result.Report.TotalFailures 0 "TotalFailures should be 0"
      Expect.isEmpty result.Errors "Errors should be empty"
  }
  ```
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineTests.fs`

### Subtask T010 -- Test: duplicate format tags return DuplicateFormat error (FR-010)

- **Purpose**: Verify that providing the same `FormatTag` twice results in a `DuplicateFormat` pipeline error.
- **Steps**:
  1. Call `Pipeline.validateSources [(Wsd, "source1"); (Wsd, "source2")]`.
  2. Assert `result.Errors` contains `DuplicateFormat Wsd`.
  3. Assert `result.ParseResults` is empty (parsing should not occur when input is invalid).
  4. Assert `result.Report.TotalChecks = 0`.
- **Code example**:
  ```fsharp
  test "Duplicate format tags return DuplicateFormat pipeline error" {
      let result = Pipeline.validateSources [(Wsd, "a"); (Wsd, "b")]
      Expect.isNonEmpty result.Errors "Should have pipeline errors"
      let hasDuplicate =
          result.Errors |> List.exists (fun e ->
              match e with DuplicateFormat Wsd -> true | _ -> false)
      Expect.isTrue hasDuplicate "Should contain DuplicateFormat Wsd"
      Expect.isEmpty result.ParseResults "No parsing should occur on invalid input"
  }
  ```
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineTests.fs`
- **Edge case**: Also test with other format tags (e.g., `Smcat`) to ensure it's not hardcoded to `Wsd`.

### Subtask T011 -- Test: unsupported format tag returns UnsupportedFormat error (FR-012)

- **Purpose**: Verify that `XState` (which has no parser) produces an `UnsupportedFormat` error without crashing.
- **Steps**:
  1. Call `Pipeline.validateSources [(XState, "some source")]`.
  2. Assert `result.Errors` contains `UnsupportedFormat XState`.
  3. Assert `result.ParseResults` is empty (no parser to produce results).
  4. Assert no exception was thrown (the test completing is sufficient proof).
- **Code example**:
  ```fsharp
  test "Unsupported format tag XState returns UnsupportedFormat error" {
      let result = Pipeline.validateSources [(XState, "anything")]
      Expect.isNonEmpty result.Errors "Should have pipeline errors"
      let hasUnsupported =
          result.Errors |> List.exists (fun e ->
              match e with UnsupportedFormat XState -> true | _ -> false)
      Expect.isTrue hasUnsupported "Should contain UnsupportedFormat XState"
  }
  ```
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineTests.fs`
- **Notes**: Also verify that a mix of supported and unsupported formats (e.g., `[(Wsd, wsdSrc); (XState, "x")]`) produces an `UnsupportedFormat` for XState while still parsing and validating the WSD source.

### Subtask T012 -- Test: single format runs self-consistency, cross-format skipped (User Story 3)

- **Purpose**: Verify that a single-format input runs self-consistency rules and skips cross-format rules.
- **Steps**:
  1. Prepare simple valid WSD source text (e.g., `"participant A\nparticipant B\nA -> B: go\n"`).
  2. Call `Pipeline.validateSources [(Wsd, wsdSource)]`.
  3. Assert `result.Report.TotalSkipped > 0` (cross-format rules are skipped because only one format is present).
  4. Assert `result.Report.TotalFailures = 0` (valid source should have no self-consistency failures).
  5. Assert `result.ParseResults` has exactly 1 entry for `Wsd`.
  6. Assert `result.Errors` is empty.
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineTests.fs`
- **Notes**: The exact WSD source syntax: each `participant X` declares a state, and `X -> Y: event` declares a transition. Keep it minimal -- 2 states, 1 transition.

### Subtask T013 -- Test: two consistent formats produce zero failures (User Story 1, scenario 1)

- **Purpose**: Verify that two format sources describing the same state machine produce zero validation failures.
- **Steps**:
  1. Prepare WSD source with states A, B and transition A -> B: go.
  2. Prepare smcat source with equivalent states and transitions: `"A => B: go;\n"`.
  3. Call `Pipeline.validateSources [(Wsd, wsdSrc); (Smcat, smcatSrc)]`.
  4. Assert `result.Report.TotalFailures = 0`.
  5. Assert `result.ParseResults` has 2 entries, both with `Succeeded = true`.
  6. Assert `result.Errors` is empty.
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineTests.fs`
- **Notes**: Keep the state machine simple to avoid parser-specific issues. The point is to verify pipeline wiring, not parser correctness.

### Subtask T014 -- Test: parse errors included in FormatParseResult (FR-008, User Story 2)

- **Purpose**: Verify that when a parser encounters errors, they appear in the `FormatParseResult.Errors` list with `Succeeded = false`.
- **Steps**:
  1. Prepare intentionally malformed WSD source (e.g., text that the WSD parser cannot parse -- try something like `"not valid wsd syntax !@#$"`).
  2. Call `Pipeline.validateSources [(Wsd, malformedSource)]`.
  3. If the parser produces errors: assert `result.ParseResults[0].Succeeded = false` and `result.ParseResults[0].Errors` is non-empty.
  4. If the parser is lenient (produces warnings instead of errors for some malformed input), adjust the test input to trigger actual errors.
  5. Assert no exception was thrown -- the pipeline should handle parse failures gracefully.
  6. Assert `result.Errors` is empty (parse failures are NOT pipeline errors).
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineTests.fs`
- **Notes**: The exact malformed input depends on what the WSD parser rejects. Check `test/Frank.Statecharts.Tests/Wsd/ErrorTests.fs` for examples of WSD syntax that produce errors. An empty string or gibberish text is a good starting point.

### Subtask T015 -- Test: custom rules via `validateSourcesWithRules` (FR-014)

- **Purpose**: Verify that `validateSourcesWithRules` includes custom rules alongside built-in rules.
- **Steps**:
  1. Define a custom `ValidationRule` that always produces a check with a distinctive name (e.g., `"Custom: test rule"`).
  2. Prepare simple valid WSD source text.
  3. Call `Pipeline.validateSourcesWithRules [customRule] [(Wsd, wsdSource)]`.
  4. Assert `result.Report.Checks` contains a check with name matching the custom rule.
- **Code example**:
  ```fsharp
  test "validateSourcesWithRules includes custom rules" {
      let customRule : ValidationRule =
          { Name = "Custom: always pass"
            RequiredFormats = Set.empty
            Check = fun _ ->
                ([ { Name = "Custom: always pass"
                     Status = Pass
                     Reason = Some "custom rule executed" } ], []) }

      let wsdSource = "participant A\nparticipant B\nA -> B: go\n"
      let result = Pipeline.validateSourcesWithRules [customRule] [(Wsd, wsdSource)]

      let customChecks =
          result.Report.Checks
          |> List.filter (fun c -> c.Name = "Custom: always pass")
      Expect.isNonEmpty customChecks "Custom rule should appear in checks"
  }
  ```
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineTests.fs`

### Subtask T016 -- Add PipelineTests.fs to test fsproj compile order

- **Purpose**: Ensure the test file is compiled by the test project.
- **Steps**:
  1. Open `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`.
  2. Find the `<!-- Validation tests -->` section (contains `Validation/TypeTests.fs` through `Validation/IntegrationTests.fs`).
  3. Add `<Compile Include="Validation/PipelineTests.fs" />` after `Validation/IntegrationTests.fs`.
- **Files**: `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
- **Validation**: Run `dotnet test test/Frank.Statecharts.Tests/` and verify all tests pass (both new and existing).

## Test Strategy

All tests in this WP follow the Expecto pattern:

```fsharp
[<Tests>]
let pipelineTests =
    testList "Validation.Pipeline" [
        test "test name" {
            // Arrange, Act, Assert
        }
    ]
```

Run with:
```bash
dotnet test test/Frank.Statecharts.Tests/ --filter "Validation.Pipeline"
```

To run all tests (including existing):
```bash
dotnet test test/Frank.Statecharts.Tests/
```

## Risks & Mitigations

1. **WSD parser syntax**: The exact WSD syntax needed for a valid minimal state machine may differ from expectations. Check existing WSD parser tests in `test/Frank.Statecharts.Tests/Wsd/ParserTests.fs` for working examples. The `participant` keyword declares states, and `A -> B: event` declares transitions.

2. **smcat parser syntax**: smcat uses `=>` for transitions and `;` as statement terminators. Check `test/Frank.Statecharts.Tests/Smcat/ParserTests.fs` for syntax examples.

3. **Parser error behavior**: Some parsers may be lenient with certain malformed inputs (returning warnings instead of errors, or producing a partial document). The test for T014 may need adjustments based on actual parser behavior. Test empirically.

## Review Guidance

- Verify all 7 test cases cover distinct acceptance scenarios from the spec.
- Verify test names are descriptive and match the FR/SC references.
- Verify no test modifies shared mutable state.
- Verify the test file is correctly added to the fsproj (after existing validation tests, before `Program.fs`).
- Run `dotnet test` and verify all tests pass (new + existing).
- Check that the test for parse errors (T014) actually triggers errors, not just warnings.

## Activity Log

- 2026-03-16T19:13:00Z -- system -- lane=planned -- Prompt created.
