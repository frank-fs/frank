---
work_package_id: "WP05"
title: "Migrate Parser and Mapper Tests"
lane: "done"
dependencies: ["WP02", "WP04"]
requirement_refs:
  - "FR-020"
subtasks:
  - "T016"
  - "T017"
  - "T018"
  - "T019"
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

# Work Package Prompt: WP05 -- Migrate Parser and Mapper Tests

## Important: Review Feedback Status

- **Has review feedback?**: Check the `review_status` field above.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Objectives & Success Criteria

- `JsonParserTests.fs` asserts against `ParseResult`/`StatechartDocument` instead of `Result<AlpsDocument, AlpsParseError list>`.
- All 39 existing parser tests (16 core + 15 edge case + 8 error) pass with updated assertions.
- All 33 mapper test assertions are absorbed into `JsonParserTests.fs` as new test lists and pass.
- `TypeTests.fs` and `MapperTests.fs` are deleted.
- The test project file no longer references deleted test files.
- `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Alps"` passes with zero failures.

## Context & Constraints

- **Spec**: `kitty-specs/023-alps-shared-ast-migration/spec.md` (User Story 5, FR-020)
- **Plan**: `kitty-specs/023-alps-shared-ast-migration/plan.md` (Test Migration Strategy)
- **Prerequisites**: WP02 (parser returns `ParseResult`), WP04 (old types deleted -- tests cannot reference them).
- **Test migration mapping** (from plan.md):
  - `TypeTests.fs` (10 tests) -> deleted entirely (types no longer exist)
  - `MapperTests.StateExtraction` (5 tests) -> `JsonParserTests.StateExtraction` test list
  - `MapperTests.TransitionMapping` (5 tests) -> `JsonParserTests.TransitionMapping` test list
  - `MapperTests.GuardExtraction` (4 tests) -> `JsonParserTests.GuardExtraction` test list
  - `MapperTests.HttpMethodHints` (3 tests) -> `JsonParserTests.HttpMethodHints` test list
  - `MapperTests.EdgeCases` (8 tests) -> `JsonParserTests.EdgeCases` test list
  - `MapperTests.Roundtrip` (7 tests) -> absorbed into `RoundTripTests.fs` (handled in WP06)
- **Important pattern change**: Parser tests that previously called `parseAlpsJson json` and got `Result<AlpsDocument, AlpsParseError list>` now get `ParseResult`. Key differences:
  - Success: `result.Document` gives `StatechartDocument`, `result.Errors` is empty.
  - Error: `result.Errors` is non-empty. There is always a `Document` (best-effort, may be empty).
  - No more `Expect.wantOk`, `Expect.isError`, `Result.defaultWith` patterns.

## Implementation Command

```bash
spec-kitty implement WP05 --base WP04
```

## Subtasks & Detailed Guidance

### Subtask T016 -- Rewrite `JsonParserTests.fs`

- **Purpose**: Update all existing parser tests to assert against `ParseResult`/`StatechartDocument` instead of `AlpsDocument`.

- **File**: `test/Frank.Statecharts.Tests/Alps/JsonParserTests.fs`

- **Steps**:
  1. **Change module opens**:
     ```fsharp
     module Frank.Statecharts.Tests.Alps.JsonParserTests

     open Expecto
     open Frank.Statecharts.Ast
     open Frank.Statecharts.Alps.JsonParser
     open Frank.Statecharts.Tests.Alps.GoldenFiles
     ```
     Remove `open Frank.Statecharts.Alps.Types`.

  2. **Add helper functions** (port from `MapperTests.fs` since they work with `StatechartDocument`):
     ```fsharp
     /// Extract all StateNodes from a StatechartDocument's elements.
     let private getStates (doc: StatechartDocument) =
         doc.Elements |> List.choose (fun el ->
             match el with StateDecl s -> Some s | _ -> None)

     /// Extract all TransitionEdges from a StatechartDocument's elements.
     let private getTransitions (doc: StatechartDocument) =
         doc.Elements |> List.choose (fun el ->
             match el with TransitionElement t -> Some t | _ -> None)
     ```

  3. **Update `jsonParserTests` list** ("Alps.JsonParser"):
     - Replace `Expect.wantOk result "..."` with `result.Document` (and optionally assert `result.Errors |> Expect.isEmpty "should have no errors"`).
     - Replace `Result.defaultWith (fun _ -> failwith "parse failed")` with direct `result.Document` access.
     - Change assertions from `AlpsDocument` fields to `StatechartDocument` fields:

     **Example migration** -- "parse tic-tac-toe golden file succeeds":
     ```fsharp
     // BEFORE:
     let result = parseAlpsJson ticTacToeAlpsJson
     let doc = Expect.wantOk result "should parse successfully"
     Expect.equal doc.Version (Some "1.0") "version"

     // AFTER:
     let result = parseAlpsJson ticTacToeAlpsJson
     Expect.isEmpty result.Errors "should parse successfully"
     let doc = result.Document
     // Version is now in annotations:
     let version = doc.Annotations |> List.tryPick (fun a ->
         match a with AlpsAnnotation(AlpsVersion v) -> Some v | _ -> None)
     Expect.equal version (Some "1.0") "version"
     ```

     **Key assertion changes**:
     - `doc.Version` -> extract `AlpsVersion` from `doc.Annotations`
     - `doc.Documentation` -> extract `AlpsDocumentation` from `doc.Annotations` or check `doc.Title`
     - `doc.Descriptors.Length` -> replaced by state count + data descriptor count assertions
     - `doc.Descriptors |> List.choose (fun d -> d.Id)` -> `getStates doc |> List.map (fun s -> s.Identifier)`
     - `doc.Links` -> extract `AlpsLink` from `doc.Annotations`
     - Descriptor child navigation (e.g., `xTurn.Descriptors |> List.find ...`) -> use `getTransitions` and filter by `Source`

  4. **Update `jsonParserEdgeCaseTests` list** ("Alps.JsonParser edge cases"):
     - Same pattern: replace `Result.defaultWith` with `result.Document`.
     - Tests like "empty ALPS document (no descriptors)" should check `getStates doc |> Expect.isEmpty` and `getTransitions doc |> Expect.isEmpty`.
     - Tests like "descriptor without type defaults to Semantic" need different assertion since type is not on descriptors anymore -- check that a semantic-only descriptor is classified as a data descriptor (in annotations) or not a state.
     - Tests about specific JSON property handling (like "ext element with href and no value") need rethinking -- they were testing raw descriptor parsing, but now the parser produces `StatechartDocument`. These tests should verify the annotations carry the correct values.

  5. **Update `jsonParserErrorTests` list** ("Alps.JsonParser errors"):
     - Replace `Expect.isError result` with `Expect.isNonEmpty result.Errors "should have errors"`.
     - Replace `match result with Error errors -> ...` with `let errors = result.Errors`.
     - Error assertions (`errors.[0].Description`) still work but use `ParseFailure.Description` instead of `AlpsParseError.Description`.
     - `errors.[0].Position` now uses `SourcePosition option` instead of `AlpsSourcePosition option` (same fields, different type).

     **Example**:
     ```fsharp
     // BEFORE:
     let result = parseAlpsJson "not valid json"
     Expect.isError result "should be error"

     // AFTER:
     let result = parseAlpsJson "not valid json"
     Expect.isNonEmpty result.Errors "should have errors"
     ```

- **Notes**: Some edge case tests may need significant rethinking because they tested raw descriptor properties that no longer exist as public types. The key question for each test is: "What user-visible behavior does this test verify?" and then assert that behavior through the `StatechartDocument` API. Additionally, ensure a test case is included for "descriptor with both `id` and `href` attributes" (an unusual but valid ALPS construct listed in edge cases). This test should verify that parsing such a descriptor produces the correct `StateNode` or annotation with both the identifier and href reference preserved.

### Subtask T017 -- Absorb `MapperTests.fs` test assertions into `JsonParserTests.fs`

- **Purpose**: Since the parser now performs the classification that the mapper used to do, the mapper test assertions belong in the parser test file.

- **File**: `test/Frank.Statecharts.Tests/Alps/JsonParserTests.fs` (add new test lists)

- **Steps**:
  1. **Add `stateExtractionTests`** (absorb `Alps.Mapper.StateExtraction`, 5 tests):
     ```fsharp
     [<Tests>]
     let stateExtractionTests =
         testList "Alps.JsonParser.StateExtraction" [
             testCase "tic-tac-toe states are extracted" <| fun _ ->
                 let result = parseAlpsJson ticTacToeAlpsJson
                 let stateIds = getStates result.Document |> List.map (fun s -> s.Identifier) |> Set.ofList
                 Expect.containsAll stateIds
                     (Set.ofList ["XTurn"; "OTurn"; "Won"; "Draw"])
                     "all game states extracted"
             // ... port remaining 4 tests
         ]
     ```
     Key change: remove `toStatechartDocument` call -- parser output IS the `StatechartDocument`.

  2. **Add `transitionMappingTests`** (absorb `Alps.Mapper.TransitionMapping`, 5 tests):
     - Remove `toStatechartDocument` calls.
     - Assertions on `getTransitions` remain the same (same `TransitionEdge` type).

  3. **Add `guardExtractionTests`** (absorb `Alps.Mapper.GuardExtraction`, 4 tests):
     - Remove `toStatechartDocument` calls.
     - Guard assertions (`t.Guard`) remain the same.

  4. **Add `httpMethodHintTests`** (absorb `Alps.Mapper.HttpMethodHints`, 3 tests):
     - Remove `toStatechartDocument` calls.
     - Annotation assertions (`AlpsAnnotation(AlpsTransitionType ...)`) remain the same.
     - The "idempotent descriptor" test constructs an inline `AlpsDocument` -- this must be replaced with inline ALPS JSON string parsed through the parser.

  5. **Add `edgeCaseMapperTests`** (absorb `Alps.Mapper.EdgeCases`, 8 tests):
     - Tests that construct `AlpsDocument` directly must be converted to construct ALPS JSON strings and parse them.
     - Tests that call `fromStatechartDocument` (testing the reverse direction) should be moved to `JsonGeneratorTests.fs` (WP06) since the generator now handles the reverse direction.
     - Tests that only verify `toStatechartDocument` behavior can be ported directly.

     **Specifically**:
     - "empty ALPS document maps to empty statechart" -> construct JSON, parse, check empty.
     - "empty statechart maps to minimal ALPS document" -> moved to WP06 (generator test).
     - "descriptor with external URL in rt" -> construct JSON, parse, check target.
     - "semantic descriptor with no transition children is still a state when referenced as rt target" -> construct JSON, parse, check states.
     - "missing workflow ordering leaves InitialStateId as None" -> parse golden file, check.
     - "fromStatechartDocument defaults to Unsafe when no ALPS annotation present" -> moved to WP06 (generator test).
     - "multiple ext elements with id=guard uses first one" -> construct JSON, parse, check guard.
     - "fromStatechartDocument re-adds # prefix to local rt targets" -> moved to WP06 (generator test).

- **Notes**: Tests that previously called `toStatechartDocument` just need the call removed. Tests that constructed `AlpsDocument` inline need to be converted to JSON strings and parsed. Tests that called `fromStatechartDocument` should be moved to WP06 generator tests.

### Subtask T018 -- Delete `TypeTests.fs` and `MapperTests.fs`

- **Purpose**: These test files test types and a module that no longer exist.

- **Files to delete**:
  - `test/Frank.Statecharts.Tests/Alps/TypeTests.fs` (10 tests -- all test deleted types)
  - `test/Frank.Statecharts.Tests/Alps/MapperTests.fs` (27 tests -- all absorbed into parser/generator/roundtrip tests)

- **Steps**:
  1. Verify all mapper test assertions have been absorbed into `JsonParserTests.fs` (T017) or are planned for WP06.
  2. Delete both files:
     ```bash
     rm test/Frank.Statecharts.Tests/Alps/TypeTests.fs
     rm test/Frank.Statecharts.Tests/Alps/MapperTests.fs
     ```

### Subtask T019 -- Update test project file

- **Purpose**: Remove deleted test files from the F# compilation list.

- **File**: `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`

- **Steps**:
  1. Locate the ALPS tests section (currently lines 41-46):
     ```xml
     <!-- ALPS tests -->
     <Compile Include="Alps/GoldenFiles.fs" />
     <Compile Include="Alps/TypeTests.fs" />
     <Compile Include="Alps/JsonParserTests.fs" />
     <Compile Include="Alps/JsonGeneratorTests.fs" />
     <Compile Include="Alps/RoundTripTests.fs" />
     <Compile Include="Alps/MapperTests.fs" />
     ```
  2. Remove the deleted files:
     ```xml
     <!-- ALPS tests -->
     <Compile Include="Alps/GoldenFiles.fs" />
     <Compile Include="Alps/JsonParserTests.fs" />
     <Compile Include="Alps/JsonGeneratorTests.fs" />
     <Compile Include="Alps/RoundTripTests.fs" />
     ```
  3. Verify the test build succeeds:
     ```bash
     dotnet build test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj
     ```
  4. Run the ALPS-filtered tests:
     ```bash
     dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Alps"
     ```
     All parser tests (original + absorbed mapper tests) should pass.

## Risks & Mitigations

- **Risk**: Test count drops below expected after migration.
  - **Mitigation**: Track test counts. Before: 39 parser + 33 mapper + 10 type = 82. After: 39 rewritten parser + ~23 absorbed mapper (minus 7 roundtrip tests moved to WP06, minus 3 `fromStatechartDocument` tests moved to WP06) = ~55 tests in this WP, plus 10 moved to WP06. TypeTests (10) are deleted by design.
- **Risk**: Edge case tests that constructed `AlpsDocument` inline are hard to convert.
  - **Mitigation**: Convert to JSON strings. The JSON is the canonical input format, and the parser is the public API.
- **Risk**: `ParseResult` error tests have different assertion patterns.
  - **Mitigation**: The pattern change is mechanical: `Expect.isError` -> `Expect.isNonEmpty result.Errors`. All error tests follow this same pattern.

## Review Guidance

- Verify no test references `AlpsDocument`, `Descriptor`, `Alps.Types`, or `Alps.Mapper`.
- Verify all absorbed mapper tests preserve their semantic assertions (same state IDs, same transition source/target pairs, same guards).
- Verify error tests check `result.Errors` instead of `Expect.isError`.
- Verify `dotnet test --filter "Alps"` passes all tests.
- Spot check: the "tic-tac-toe states are extracted" test should produce `["XTurn"; "OTurn"; "Won"; "Draw"; "gameState"]` (5 states).

## Activity Log

- 2026-03-16T19:13:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-17T12:30:00Z -- claude-opus -- lane=done -- Review approved. All objectives met: JsonParserTests.fs migrated to ParseResult/StatechartDocument, 23 mapper tests absorbed, TypeTests.fs and MapperTests.fs deleted, fsproj updated, 84 ALPS tests pass. No critical or important issues. Two suggestions: (1) test list naming uses Alps.Parser.* vs suggested Alps.JsonParser.* (cosmetic, acceptable), (2) helper function duplication between JsonParserTests.fs and RoundTripTests.fs could be consolidated in future. Commit 6b31629 also migrated JsonGeneratorTests.fs and RoundTripTests.fs (WP06 scope) due to build dependency -- acceptable.
