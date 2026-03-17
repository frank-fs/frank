---
work_package_id: "WP06"
title: "Migrate Generator, RoundTrip Tests, and Final Verification"
lane: "done"
dependencies: ["WP03", "WP04", "WP05"]
requirement_refs:
  - "FR-021"
  - "FR-022"
subtasks:
  - "T020"
  - "T021"
  - "T022"
  - "T023"
  - "T024"
assignee: ""
agent: ""
shell_pid: ""
review_status: "approved"
reviewed_by: "claude-opus"
history:
  - timestamp: "2026-03-16T19:13:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP06 -- Migrate Generator, RoundTrip Tests, and Final Verification

## Important: Review Feedback Status

- **Has review feedback?**: Check the `review_status` field above.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Objectives & Success Criteria

- `JsonGeneratorTests.fs` constructs `StatechartDocument` values with `AlpsMeta` annotations (no `AlpsDocument` references).
- `RoundTripTests.fs` compares `StatechartDocument` equality through parse-generate-reparse cycle.
- `MapperTests.Roundtrip` (7 tests) and `MapperTests.EdgeCases` reverse-direction tests (3 tests) are absorbed.
- Cross-format validator compatibility test added (SC-008).
- `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` succeeds for net8.0, net9.0, and net10.0.
- `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` passes with zero failures.
- Zero references to `AlpsDocument`, `Alps.Types`, `Alps.Mapper` remain in `src/` or `test/`.

## Context & Constraints

- **Spec**: `kitty-specs/023-alps-shared-ast-migration/spec.md` (User Stories 3, 5, 6; SC-004, SC-006, SC-007, SC-008)
- **Plan**: `kitty-specs/023-alps-shared-ast-migration/plan.md` (Test Migration Strategy)
- **Prerequisites**: WP03 (generator accepts `StatechartDocument`), WP04 (old types deleted), WP05 (parser tests migrated -- avoids conflicting edits to shared test file).
- **Absorbed from MapperTests**:
  - `MapperTests.Roundtrip` (7 tests): "preserves state ids", "preserves transition events", "preserves rt targets", "onboarding roundtrip", "preserves guard labels", "sets version to 1.0", "preserves title as documentation" -> absorbed into `RoundTripTests.fs`
  - `MapperTests.EdgeCases` reverse-direction tests (3 tests): "empty statechart maps to minimal ALPS document", "fromStatechartDocument defaults to Unsafe", "fromStatechartDocument re-adds # prefix" -> absorbed into `JsonGeneratorTests.fs`

## Implementation Command

```bash
spec-kitty implement WP06 --base WP05
```

## Subtasks & Detailed Guidance

### Subtask T020 -- Rewrite `JsonGeneratorTests.fs`

- **Purpose**: Update all generator tests to construct `StatechartDocument` with `AlpsMeta` annotations instead of `AlpsDocument`.

- **File**: `test/Frank.Statecharts.Tests/Alps/JsonGeneratorTests.fs`

- **Steps**:
  1. **Change module opens**:
     ```fsharp
     module Frank.Statecharts.Tests.Alps.JsonGeneratorTests

     open System.Text.Json
     open Expecto
     open Frank.Statecharts.Ast
     open Frank.Statecharts.Alps.JsonParser
     open Frank.Statecharts.Alps.JsonGenerator
     open Frank.Statecharts.Tests.Alps.GoldenFiles
     ```
     Remove `open Frank.Statecharts.Alps.Types`.

  2. **Update `jsonGeneratorTests` list** ("Alps.JsonGenerator"):
     - "generate from tic-tac-toe AST produces valid reparseable JSON": Parse tic-tac-toe, get `result.Document`, pass to `generateAlpsJson`, re-parse, compare `StatechartDocument` equality.
     - "generate from onboarding AST": Same pattern.
     - "generated JSON has alps root object": Construct minimal `StatechartDocument` with `AlpsAnnotation(AlpsVersion "1.0")` annotation.
     - "generated JSON preserves version": Same -- construct `StatechartDocument` with `AlpsVersion` annotation.

     **Example -- minimal `StatechartDocument` for generator tests**:
     ```fsharp
     let minimalDoc =
         { Title = None
           InitialStateId = None
           Elements = []
           DataEntries = []
           Annotations = [AlpsAnnotation(AlpsVersion "1.0")] }
     ```

  3. **Update `jsonGeneratorStructureTests` list** ("Alps.JsonGenerator structure"):
     - "empty document produces minimal ALPS JSON": Use empty `StatechartDocument` (no annotations). Generator should default to version `"1.0"`.
     - "descriptor type is written as string": Construct `StatechartDocument` with one `StateNode` and one `TransitionEdge` with `AlpsTransitionType Unsafe` annotation.
     - "all four descriptor types": Construct 4 transitions with different `AlpsTransitionType` annotations (note: `Semantic` is not a transition type -- it is the default for states. Only Safe/Unsafe/Idempotent map to transitions).
     - "nested descriptors are written recursively": Construct a state with a transition (nested as child descriptor in output).
     - "ext elements": Construct a transition with `AlpsExtension` annotation and `Guard` field.
     - "ext element with href and no value": Construct a transition with `AlpsExtension("ref", Some "http://example.com/ext", None)` annotation.
     - "output is indented": Generate from minimal doc and check for newlines.
     - "documentation with format": Use `AlpsDocumentation(Some "html", "...")` annotation on the document.
     - "documentation without format": Use `AlpsDocumentation(None, "...")` annotation.
     - "link elements": Use `AlpsLink("self", "...")` annotation on the document.
     - "top-level ext elements": Use `AlpsExtension("custom", None, Some "data")` annotation on the document.
     - "descriptor with href only": This becomes a shared transition test -- a transition with `AlpsDescriptorHref` annotation should produce href-only descriptor in the output.
     - "descriptor with rt field": Construct a transition with `Target = Some "gameState"` and verify the output has `"rt": "#gameState"`.

  4. **Absorb reverse-direction mapper edge case tests** (3 tests from `MapperTests.EdgeCases`):
     - "empty statechart maps to minimal ALPS document": Construct empty `StatechartDocument`, generate, verify minimal JSON with version `"1.0"`.
     - "fromStatechartDocument defaults to Unsafe when no ALPS annotation present": Construct `StatechartDocument` with a transition that has no `AlpsTransitionType` annotation. Generate JSON and verify the transition descriptor has `"type": "unsafe"`.
     - "fromStatechartDocument re-adds # prefix to local rt targets": Construct `StatechartDocument` with a transition targeting `"B"`. Generate JSON and verify the descriptor has `"rt": "#B"`.

- **Notes**: Constructor patterns for generator tests are more verbose than before because `StatechartDocument` + annotations is more complex than `AlpsDocument`. Consider adding local helper functions to reduce boilerplate (e.g., `makeState`, `makeTransition`).

### Subtask T021 -- Rewrite `RoundTripTests.fs`

- **Purpose**: Roundtrip tests now verify `StatechartDocument` equality through the parse-generate-reparse cycle.

- **File**: `test/Frank.Statecharts.Tests/Alps/RoundTripTests.fs`

- **Steps**:
  1. **Change module opens**:
     ```fsharp
     module Frank.Statecharts.Tests.Alps.RoundTripTests

     open Expecto
     open Frank.Statecharts.Ast
     open Frank.Statecharts.Alps.JsonParser
     open Frank.Statecharts.Alps.JsonGenerator
     open Frank.Statecharts.Tests.Alps.GoldenFiles
     ```
     Remove `open Frank.Statecharts.Alps.Types`.

  2. **Add helpers**:
     ```fsharp
     /// Extract all StateNodes from a StatechartDocument.
     let private getStates (doc: StatechartDocument) =
         doc.Elements |> List.choose (fun el ->
             match el with StateDecl s -> Some s | _ -> None)

     /// Extract all TransitionEdges from a StatechartDocument.
     let private getTransitions (doc: StatechartDocument) =
         doc.Elements |> List.choose (fun el ->
             match el with TransitionElement t -> Some t | _ -> None)
     ```

  3. **Update `roundTripTests` list** ("Alps.RoundTrip"):
     - "tic-tac-toe JSON roundtrip preserves all information":
       ```fsharp
       let original = (parseAlpsJson ticTacToeAlpsJson).Document
       let generated = generateAlpsJson original
       let roundTripped = (parseAlpsJson generated).Document
       Expect.equal roundTripped original "roundtrip preserves all information"
       ```
     - "onboarding JSON roundtrip": Same pattern.
     - "roundtrip preserves descriptor ids and types": Compare state identifiers.
     - "roundtrip preserves ext elements": Compare `AlpsExtension` annotations (requires collecting from all nodes).
     - "roundtrip preserves links": Compare `AlpsLink` annotations from `doc.Annotations`.
     - "roundtrip preserves version": Compare `AlpsVersion` annotation.
     - "roundtrip preserves documentation": Compare `AlpsDocumentation` annotation.
     - "roundtrip preserves nested descriptor hierarchy": Compare transition counts per state.
     - "empty document roundtrips": Construct empty `StatechartDocument`, generate, re-parse, compare.
     - "document with only version roundtrips": Construct `StatechartDocument` with `AlpsVersion "1.0"` annotation.

  4. **Absorb `MapperTests.Roundtrip` (7 tests)**:
     - "preserves state ids": Parse tic-tac-toe, generate, re-parse. Compare state identifiers.
     - "preserves transition events": Compare transition event names through roundtrip.
     - "preserves rt targets": Compare transition targets through roundtrip.
     - "onboarding roundtrip preserves state ids": Same for onboarding golden file.
     - "preserves guard labels": Compare guards through roundtrip.
     - "sets version to 1.0": Verify `AlpsVersion` annotation present after roundtrip.
     - "preserves title as documentation": Verify `AlpsDocumentation` or `Title` preserved.

     These tests follow the same pattern: parse -> generate -> re-parse, then compare specific fields. The key change is removing the `toStatechartDocument` / `fromStatechartDocument` calls and working with `StatechartDocument` directly.

- **Notes**: The `collectAllExts` helper needs rewriting since it currently operates on `AlpsDocument`. The new version should collect `AlpsExtension` annotations from all `StateNode.Annotations`, `TransitionEdge.Annotations`, and `StatechartDocument.Annotations`.

### Subtask T022 -- Add cross-format validator compatibility test (SC-008)

- **Purpose**: Verify that ALPS artifacts can be used in cross-format validation without a mapper step.

- **File**: `test/Frank.Statecharts.Tests/Validation/CrossFormatTests.fs` (add to existing test file)

- **Steps**:
  1. First, examine the existing cross-format test patterns:
     ```bash
     cat test/Frank.Statecharts.Tests/Validation/CrossFormatTests.fs
     ```
     Identify how `FormatArtifact` values are constructed and how the validator is called.

  2. Add a new test case (or test list) that:
     - Parses the tic-tac-toe ALPS JSON using `parseAlpsJson`.
     - Constructs a `FormatArtifact` directly from `result.Document` (no mapper call).
     - Constructs a matching WSD `FormatArtifact` (or reuses an existing one from other tests).
     - Runs the cross-format validator.
     - Verifies state name agreement and event name agreement rules pass.

  3. If the validator types or calling pattern are unclear from existing tests, check:
     - `src/Frank.Statecharts/Validation/Types.fs` for `FormatArtifact` definition.
     - `src/Frank.Statecharts/Validation/Validator.fs` for the validation API.

- **Notes**: This test validates SC-008: "Cross-format validator works with ALPS artifacts without a mapper step." The test proves the architectural goal of the migration -- ALPS artifacts are first-class citizens in the cross-format validation system.

### Subtask T023 -- Verify multi-target build

- **Purpose**: Confirm the build succeeds for all three target frameworks.

- **Steps**:
  1. Build each target individually:
     ```bash
     dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj -f net8.0
     dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj -f net9.0
     dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj -f net10.0
     ```
  2. All three must succeed with zero errors and zero warnings related to Alps/ALPS.

### Subtask T024 -- Run full test suite

- **Purpose**: Final verification that all tests pass, not just ALPS tests.

- **Steps**:
  1. Run the complete test suite:
     ```bash
     dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj
     ```
  2. Verify zero failures.
  3. Verify no references to deleted types remain:
     ```bash
     grep -r "AlpsDocument\|Alps\.Types\|Alps\.Mapper\|DescriptorType\|AlpsParseError" src/ test/
     ```
     Expected: zero matches.
  4. Report the total test count and any changes from the pre-migration count.

## Risks & Mitigations

- **Risk**: Structural equality for `StatechartDocument` roundtrip fails due to annotation ordering.
  - **Mitigation**: The parser and generator both follow the deterministic annotation ordering convention from data-model.md. If roundtrip fails, compare annotations field by field to identify the ordering mismatch.
- **Risk**: Cross-format test requires understanding of validator API that may have changed.
  - **Mitigation**: Read existing cross-format tests first to understand the pattern. The validator works with `StatechartDocument` which is unchanged.
- **Risk**: Generator tests are verbose due to `StatechartDocument` construction.
  - **Mitigation**: Create helper functions (e.g., `makeState`, `makeTransition`) to reduce boilerplate in generator tests.

## Review Guidance

- Verify no test references `AlpsDocument`, `Descriptor`, `Alps.Types`, or `Alps.Mapper`.
- Verify roundtrip tests use `parseAlpsJson -> .Document -> generateAlpsJson -> parseAlpsJson -> .Document` pattern.
- Verify absorbed mapper roundtrip tests preserve semantic assertions.
- Verify cross-format test constructs `FormatArtifact` directly from parser output (no mapper).
- Verify multi-target build passes.
- Verify full test suite passes with zero failures.
- Final check: `grep -r "AlpsDocument\|Alps\.Types\|Alps\.Mapper" src/ test/` returns zero matches.

## Activity Log

- 2026-03-16T19:13:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-17T12:30:00Z -- claude-opus -- lane=done -- Review approved. All objectives met: 812 tests pass (93 ALPS), zero deleted-type references, multi-target build clean, 7 mapper roundtrip + 3 mapper edge-case tests absorbed, SC-008 cross-format validator compatibility verified. Commit 17bd654.
