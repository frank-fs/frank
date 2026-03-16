---
work_package_id: WP03
title: End-to-End Integration Tests
lane: planned
dependencies:
- WP01
subtasks:
- T017
- T018
- T019
- T020
- T021
- T022
- T023
- T024
- T025
- T026
- T027
phase: Phase 2 - Integration Tests
assignee: ''
agent: ''
shell_pid: ''
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-16T19:13:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-004
- FR-005
- FR-006
- FR-007
- FR-008
- FR-009
---

# Work Package Prompt: WP03 -- End-to-End Integration Tests

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

Depends on WP01 and WP02:

```bash
spec-kitty implement WP03 --base WP02
```

---

## Objectives & Success Criteria

1. Create `PipelineIntegrationTests.fs` with end-to-end tests that parse REAL format source text (not hand-constructed AST) through the pipeline.
2. Define tic-tac-toe state machine source text constants in all four supported formats: WSD, smcat, SCXML, ALPS.
3. Verify zero validation failures for consistent inputs across all four formats (SC-001, User Story 5 scenario 1).
4. Verify correct mismatch detection when one format has intentional differences (SC-002, User Story 5 scenarios 2-3).
5. Verify parse error handling with real malformed input (User Story 2).
6. Verify performance: 4 formats parsed and validated in under 2 seconds (SC-004).
7. `dotnet test --filter "Validation.PipelineIntegration"` passes with all tests green.

## Context & Constraints

- **Spec**: `kitty-specs/025-validation-pipeline-wiring/spec.md` -- User Stories 2 and 5, SC-001 through SC-006
- **Plan**: `kitty-specs/025-validation-pipeline-wiring/plan.md` -- Test Design, PipelineIntegrationTests section
- **Quickstart**: `kitty-specs/025-validation-pipeline-wiring/quickstart.md` -- WSD and smcat source examples
- **Prerequisites**: WP01 (Pipeline module) and WP02 (unit tests passing).
- **Test framework**: Expecto 10.2.3.
- **Test file location**: `test/Frank.Statecharts.Tests/Validation/PipelineIntegrationTests.fs`

**Critical difference from existing `IntegrationTests.fs`**: The existing integration tests construct `StatechartDocument` by hand and call `Validator.validate` directly. THIS WP tests the full pipeline from raw format text to `PipelineResult`, exercising real parsers.

**Tic-tac-toe state machine** (shared across all formats):
- **States**: `idle`, `playerX`, `playerO`, `gameOver`
- **Events**: `start`, `move`, `win`
- **Transitions**:
  - `idle` --start--> `playerX`
  - `playerX` --move--> `playerO`
  - `playerO` --move--> `playerX`
  - `playerX` --win--> `gameOver`
  - `playerO` --win--> `gameOver`

## Subtasks & Detailed Guidance

### Subtask T017 -- Create PipelineIntegrationTests.fs with module structure

- **Purpose**: Set up the test file with module declaration, imports, and test list structure.
- **Steps**:
  1. Create `test/Frank.Statecharts.Tests/Validation/PipelineIntegrationTests.fs`.
  2. Declare module: `module Frank.Statecharts.Tests.Validation.PipelineIntegrationTests`.
  3. Open: `Expecto`, `Frank.Statecharts.Validation`, `Frank.Statecharts.Ast`.
  4. Create top-level `[<Tests>]` test lists for organizing tests.
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineIntegrationTests.fs` (NEW)

### Subtask T018 -- Define tic-tac-toe WSD source text constant

- **Purpose**: Provide real WSD format text that the WSD parser can parse into the tic-tac-toe state machine.
- **Steps**:
  1. Define a `let` binding with the WSD source as a multi-line string.
  2. Use WSD syntax: `participant <state>` for state declarations, `<source> -> <target>: <event>` for transitions.
- **Code**:
  ```fsharp
  let wsdTicTacToe = """
  participant idle
  participant playerX
  participant playerO
  participant gameOver
  idle -> playerX: start
  playerX -> playerO: move
  playerO -> playerX: move
  playerX -> gameOver: win
  playerO -> gameOver: win
  """
  ```
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineIntegrationTests.fs`
- **Validation**: Verify this source parses correctly by checking existing WSD parser tests or by running a quick parse in a test. The `participant` keyword declares states and `->` declares transitions with event labels after `:`.
- **Notes**: Check `test/Frank.Statecharts.Tests/Wsd/ParserTests.fs` for confirmed working WSD syntax examples. Adjust if `participant` is not the correct keyword (it might be a different keyword in this WSD dialect).

### Subtask T019 -- Define tic-tac-toe smcat source text constant

- **Purpose**: Provide real smcat format text for the same tic-tac-toe state machine.
- **Steps**:
  1. Define a `let` binding with the smcat source.
  2. Use smcat syntax: `source => target: event;` for transitions (states are inferred from transitions, or can be declared explicitly).
- **Code**:
  ```fsharp
  let smcatTicTacToe = """
  idle => playerX: start;
  playerX => playerO: move;
  playerO => playerX: move;
  playerX => gameOver: win;
  playerO => gameOver: win;
  """
  ```
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineIntegrationTests.fs`
- **Notes**: Check `test/Frank.Statecharts.Tests/Smcat/ParserTests.fs` for confirmed working smcat syntax. States may need explicit declaration syntax. Some smcat dialects use `initial` and `final` keywords.

### Subtask T020 -- Define tic-tac-toe SCXML source text constant

- **Purpose**: Provide real SCXML format text for the same tic-tac-toe state machine.
- **Steps**:
  1. Define a `let` binding with valid SCXML XML source.
  2. Use SCXML XML structure with `<scxml>`, `<state>`, and `<transition>` elements.
- **Code**:
  ```fsharp
  let scxmlTicTacToe = """<?xml version="1.0" encoding="UTF-8"?>
  <scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
    <state id="idle">
      <transition event="start" target="playerX"/>
    </state>
    <state id="playerX">
      <transition event="move" target="playerO"/>
      <transition event="win" target="gameOver"/>
    </state>
    <state id="playerO">
      <transition event="move" target="playerX"/>
      <transition event="win" target="gameOver"/>
    </state>
    <final id="gameOver"/>
  </scxml>
  """
  ```
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineIntegrationTests.fs`
- **Notes**:
  - Check `test/Frank.Statecharts.Tests/Scxml/ParserTests.fs` for confirmed working SCXML syntax.
  - `gameOver` is `<final>` rather than `<state>` since it has no outgoing transitions. Some parsers may handle this differently. Alternatively, use `<state id="gameOver"/>` if the parser does not support `<final>`.
  - The `initial` attribute on `<scxml>` specifies the initial state.
  - IMPORTANT: Verify that the SCXML parser through the mapper produces state identifiers matching `idle`, `playerX`, `playerO`, `gameOver` exactly. The SCXML `id` attribute values become state identifiers.

### Subtask T021 -- Define tic-tac-toe ALPS source text constant

- **Purpose**: Provide real ALPS JSON format text for the same tic-tac-toe state machine.
- **Steps**:
  1. Define a `let` binding with valid ALPS JSON source.
  2. Use ALPS JSON structure. ALPS represents states as descriptors with `type: "semantic"` and transitions as links between descriptors.
- **Notes**:
  - **This is the trickiest format.** ALPS has a fundamentally different conceptual model -- it uses descriptors, not states/transitions.
  - Check `test/Frank.Statecharts.Tests/Alps/JsonParserTests.fs` and `test/Frank.Statecharts.Tests/Alps/MapperTests.fs` for how ALPS JSON is structured and how the mapper converts it to `StatechartDocument`.
  - The ALPS mapper (`Alps.Mapper.toStatechartDocument`) must produce states matching `idle`, `playerX`, `playerO`, `gameOver` and events matching `start`, `move`, `win`.
  - Read the existing ALPS test golden files or fixtures to understand the JSON structure that maps to states and transitions.
  - If the ALPS mapper cannot produce states/transitions from ALPS JSON in a way that matches the other formats, you may need to: (a) study the mapper logic carefully, or (b) use a simpler state machine for the ALPS portion, or (c) adjust the ALPS source to match what the mapper expects.
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineIntegrationTests.fs`
- **Research needed**: Read `src/Frank.Statecharts/Alps/Mapper.fs` to understand how ALPS descriptors map to `StatechartDocument` states and transitions. Read `test/Frank.Statecharts.Tests/Alps/MapperTests.fs` for working examples of ALPS JSON that produces known AST outputs.

### Subtask T022 -- Test: consistent 4-format sources produce zero failures (SC-001)

- **Purpose**: The primary acceptance test for the pipeline. Parse all four formats and verify zero validation failures.
- **Steps**:
  1. Call `Pipeline.validateSources [(Wsd, wsdTicTacToe); (Smcat, smcatTicTacToe); (Scxml, scxmlTicTacToe); (Alps, alpsTicTacToe)]`.
  2. Assert `result.Errors` is empty (no pipeline-level errors).
  3. Assert `result.ParseResults` has 4 entries, all with `Succeeded = true`.
  4. Assert `result.Report.TotalFailures = 0`.
  5. Assert `result.Report.TotalSkipped = 0` (all pairwise cross-format rules should execute with 4 formats present -- though only 4 of the 5 tags are present, so rules involving `XState` will be skipped).
  6. Actually, assert `result.Report.TotalSkipped >= 0` -- some XState-involving rules will be skipped since XState is not in the input.
- **Code example**:
  ```fsharp
  test "Consistent tic-tac-toe in 4 formats produces zero validation failures" {
      let result = Pipeline.validateSources [
          (Wsd, wsdTicTacToe)
          (Smcat, smcatTicTacToe)
          (Scxml, scxmlTicTacToe)
          (Alps, alpsTicTacToe)
      ]
      Expect.isEmpty result.Errors "No pipeline errors expected"
      Expect.equal (List.length result.ParseResults) 4 "Should have 4 parse results"
      for pr in result.ParseResults do
          Expect.isTrue pr.Succeeded (sprintf "%A should parse successfully" pr.Format)
      Expect.equal result.Report.TotalFailures 0
          (sprintf "Expected zero failures but got %d: %A" result.Report.TotalFailures result.Report.Failures)
  }
  ```
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineIntegrationTests.fs`
- **Notes**: If this test fails, the most likely cause is that the format source text constants don't produce identical state/event/transition sets. Debug by printing each format's parsed states and events.

### Subtask T023 -- Test: state name mismatch detected (SC-002, User Story 5 scenario 2)

- **Purpose**: Verify the pipeline detects cross-format state name disagreement when one format renames a state.
- **Steps**:
  1. Create a modified smcat source where `gameOver` is renamed to `finished`.
  2. Call the pipeline with the original WSD + modified smcat sources.
  3. Assert `result.Report.TotalFailures > 0`.
  4. Assert at least one failure mentions `gameOver` or `finished` in its description.
  5. Assert the failure's `Formats` list includes `Smcat`.
- **Code example**:
  ```fsharp
  test "State name mismatch detected: gameOver vs finished" {
      let smcatMismatch = smcatTicTacToe.Replace("gameOver", "finished")
      let result = Pipeline.validateSources [
          (Wsd, wsdTicTacToe)
          (Smcat, smcatMismatch)
      ]
      Expect.isGreaterThan result.Report.TotalFailures 0 "Should detect state name mismatch"
      let relevantFailures =
          result.Report.Failures
          |> List.filter (fun f ->
              f.Description.Contains("gameOver") || f.Description.Contains("finished"))
      Expect.isNonEmpty relevantFailures "Should have failures mentioning gameOver or finished"
  }
  ```
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineIntegrationTests.fs`

### Subtask T024 -- Test: missing event detected (User Story 5 scenario 3)

- **Purpose**: Verify the pipeline detects when one format is missing an event present in other formats.
- **Steps**:
  1. Create a modified version of one format source (e.g., WSD) that removes the `start` event (replace `start` with `move` or remove the transition entirely).
  2. Call the pipeline with the original smcat + modified WSD sources.
  3. Assert `result.Report.TotalFailures > 0`.
  4. Assert at least one failure is related to the missing `start` event.
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineIntegrationTests.fs`
- **Notes**: Removing a transition entirely is cleaner than changing the event name, because changing the event name would also introduce a new event that other formats don't have.

### Subtask T025 -- Test: parse error in one format still validates others (User Story 2)

- **Purpose**: Verify that when one format has a parse error, the pipeline still validates the other format's artifact.
- **Steps**:
  1. Prepare valid WSD source (the tic-tac-toe constant).
  2. Prepare intentionally malformed SCXML source (e.g., `"<scxml><state id='a'><transition event='go' target='b'/></state>"` -- missing closing `</scxml>` tag or other XML error).
  3. Call `Pipeline.validateSources [(Wsd, wsdTicTacToe); (Scxml, malformedScxml)]`.
  4. Assert `result.ParseResults` has 2 entries.
  5. Find the SCXML parse result: assert `Succeeded = false` and `Errors` is non-empty.
  6. Find the WSD parse result: assert `Succeeded = true`.
  7. Assert `result.Report` exists and has some checks (self-consistency at minimum for the WSD artifact).
  8. Assert no exception was thrown.
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineIntegrationTests.fs`
- **Notes**: The exact malformed SCXML that triggers parse errors depends on the SCXML parser's error handling. Check `test/Frank.Statecharts.Tests/Scxml/ErrorTests.fs` for examples of SCXML input that produces parse errors.

### Subtask T026 -- Test: performance -- 4 formats under 2 seconds (SC-004)

- **Purpose**: Verify the pipeline completes within the performance budget.
- **Steps**:
  1. Use the consistent tic-tac-toe sources (same as T022).
  2. Wrap the pipeline call in a `System.Diagnostics.Stopwatch`.
  3. Assert elapsed time is under 2 seconds.
- **Code example**:
  ```fsharp
  test "Performance: 4 formats parsed and validated in under 2 seconds" {
      let sw = System.Diagnostics.Stopwatch.StartNew()
      let _result = Pipeline.validateSources [
          (Wsd, wsdTicTacToe)
          (Smcat, smcatTicTacToe)
          (Scxml, scxmlTicTacToe)
          (Alps, alpsTicTacToe)
      ]
      sw.Stop()
      Expect.isLessThan sw.Elapsed.TotalSeconds 2.0
          (sprintf "Pipeline took %.3f seconds, expected < 2.0" sw.Elapsed.TotalSeconds)
  }
  ```
- **Files**: `test/Frank.Statecharts.Tests/Validation/PipelineIntegrationTests.fs`
- **Notes**: This should easily pass -- parsers are fast for ~10-line inputs. The budget is generous.

### Subtask T027 -- Add PipelineIntegrationTests.fs to test fsproj compile order

- **Purpose**: Ensure the test file is compiled by the test project.
- **Steps**:
  1. Open `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`.
  2. Add `<Compile Include="Validation/PipelineIntegrationTests.fs" />` after `Validation/PipelineTests.fs` (added in WP02) and before `Program.fs`.
- **Files**: `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
- **Validation**: Run `dotnet test test/Frank.Statecharts.Tests/` and verify ALL tests pass (existing + WP02 + WP03).

## Test Strategy

All tests follow the Expecto pattern:

```fsharp
[<Tests>]
let pipelineIntegrationTests =
    testList "Validation.PipelineIntegration" [
        test "test name" { ... }
    ]
```

Run integration tests only:
```bash
dotnet test test/Frank.Statecharts.Tests/ --filter "Validation.PipelineIntegration"
```

Run all validation tests:
```bash
dotnet test test/Frank.Statecharts.Tests/ --filter "Validation"
```

Run everything:
```bash
dotnet test test/Frank.Statecharts.Tests/
```

## Risks & Mitigations

1. **ALPS source text mapping**: ALPS has the most different conceptual model. The ALPS JSON must be structured so that `Alps.JsonParser.parseAlpsJson` succeeds AND `Alps.Mapper.toStatechartDocument` produces states and transitions matching the other formats. **Mitigation**: Read `src/Frank.Statecharts/Alps/Mapper.fs` lines 110-215 to understand exactly how ALPS descriptors map to `StatechartDocument` states and transitions. Read existing ALPS test fixtures for working examples.

2. **SCXML `<final>` vs `<state>`**: The SCXML parser may treat `<final id="gameOver"/>` differently from `<state id="gameOver"/>`. If `<final>` produces a different state kind that affects cross-format comparisons, use `<state>` instead. **Mitigation**: Check `src/Frank.Statecharts/Scxml/Mapper.fs` to see how `<final>` maps to `StateKind`.

3. **Parser error for malformed SCXML (T025)**: The SCXML parser uses `System.Xml.Linq.XDocument.Parse` which throws `XmlException` on malformed XML. The pipeline's `parserFor` adapter must handle this. If the SCXML parser does not catch XML exceptions internally, the pipeline may need a try/catch. **Mitigation**: Check `src/Frank.Statecharts/Scxml/Parser.fs` `tryParseWith` function to see if it catches `XmlException`.

4. **State identity across formats**: Different parsers may normalize state identifiers differently (e.g., trimming whitespace, case changes). The tic-tac-toe state names are simple lowercase identifiers (`idle`, `playerX`, `playerO`, `gameOver`) which should survive all parsers unchanged. If cross-format failures appear, debug by printing each format's extracted state identifiers.

## Review Guidance

- Verify all four format source text constants are syntactically valid for their respective parsers.
- Verify the consistent test (T022) produces zero failures -- if it doesn't, the source texts are inconsistent.
- Verify the mismatch tests (T023, T024) detect the correct failures.
- Verify the parse error test (T025) actually triggers parse errors in the SCXML parser.
- Verify the performance test (T026) has a reasonable budget (2 seconds is generous for ~40 lines of total source text).
- Verify the test file is correctly added to the fsproj.
- Run `dotnet test` and verify ALL tests pass (existing + new).

## Activity Log

- 2026-03-16T19:13:00Z -- system -- lane=planned -- Prompt created.
