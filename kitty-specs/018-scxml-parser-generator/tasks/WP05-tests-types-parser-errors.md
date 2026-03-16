---
work_package_id: WP05
title: Tests -- Types, Parser Core, Errors, and Test Project Setup
lane: "done"
dependencies:
- WP02
subtasks:
- T020
- T021
- T024
- T025
- T029
phase: Phase 3 - Testing
assignee: ''
agent: ''
shell_pid: ''
review_status: "approved"
reviewed_by: "Ryan Riley"
history:
- timestamp: '2026-03-16T01:17:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-002, FR-003, FR-004, FR-006, FR-007, FR-012, FR-013, FR-014, FR-015]
---

# Work Package Prompt: WP05 -- Tests -- Types, Parser Core, Errors, and Test Project Setup

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

Depends on WP02 and WP03 -- run:
```bash
spec-kitty implement WP05 --base WP03
```

(WP03 already includes WP02 changes since WP03 depends on WP02.)

---

## Objectives & Success Criteria

- Create test files for SCXML type construction, parser core scenarios, edge cases, and error handling.
- Update the test project file to include all SCXML test compile entries.
- All tests pass with `dotnet test test/Frank.Statecharts.Tests --filter "Scxml"`.
- Tests cover User Story 1 acceptance scenarios, spec edge cases, and FR-015 (structured errors).

## Context & Constraints

- **Spec**: User Story 1 (acceptance scenarios 1-5), Edge Cases section, FR-015
- **Existing test pattern**: `test/Frank.Statecharts.Tests/Wsd/ParserTests.fs` -- Expecto `testList`/`testCase` pattern
- **Test framework**: Expecto 10.2.3, YoloDev.Expecto.TestSdk 0.14.3, Microsoft.NET.Test.Sdk 17.14.1
- **Test project**: `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` -- targets net10.0 only
- **Dependencies**: Requires working parser from WP02 + WP03

## Subtasks & Detailed Guidance

### Subtask T020 -- Create TypeTests.fs with Type Construction and Equality Tests

- **Purpose**: Verify that all SCXML types can be constructed and that F# structural equality works as expected (needed for roundtrip comparison in WP06).

- **Steps**:
  1. Create `test/Frank.Statecharts.Tests/Scxml/` directory if it does not exist.
  2. Create `test/Frank.Statecharts.Tests/Scxml/TypeTests.fs`.
  3. Module declaration:
     ```fsharp
     module Frank.Statecharts.Tests.Scxml.TypeTests

     open Expecto
     open Frank.Statecharts.Scxml.Types
     ```
  4. Write tests covering:
     - **SourcePosition construction**: `{ Line = 1; Column = 5 }` creates correctly.
     - **ScxmlStateKind values**: All four DU cases exist (`Simple`, `Compound`, `Parallel`, `Final`).
     - **ScxmlTransitionType values**: `Internal` and `External` exist.
     - **ScxmlHistoryKind values**: `Shallow` and `Deep` exist.
     - **DataEntry construction**: record with `Id`, `Expression = Some "0"`, `Position = None`.
     - **ScxmlTransition construction**: record with all fields populated.
     - **ScxmlState construction**: record with `Children` containing nested states (verifies recursive type works).
     - **ScxmlDocument construction**: full document with states, data entries.
     - **Structural equality**: Two identical `ScxmlDocument` records compare as equal; two differing records compare as not equal.

  Example test:
  ```fsharp
  [<Tests>]
  let typeTests =
      testList "Scxml.Types" [
          testCase "ScxmlDocument structural equality" <| fun _ ->
              let doc1 = { Name = Some "test"; InitialId = Some "s1"
                           DatamodelType = None; Binding = None
                           States = []; DataEntries = []; Position = None }
              let doc2 = { Name = Some "test"; InitialId = Some "s1"
                           DatamodelType = None; Binding = None
                           States = []; DataEntries = []; Position = None }
              Expect.equal doc1 doc2 "identical documents should be equal"

          testCase "ScxmlDocument inequality on different InitialId" <| fun _ ->
              let doc1 = { Name = None; InitialId = Some "s1"
                           DatamodelType = None; Binding = None
                           States = []; DataEntries = []; Position = None }
              let doc2 = { Name = None; InitialId = Some "s2"
                           DatamodelType = None; Binding = None
                           States = []; DataEntries = []; Position = None }
              Expect.notEqual doc1 doc2 "different InitialId should not be equal"
      ]
  ```

- **Files**: `test/Frank.Statecharts.Tests/Scxml/TypeTests.fs` (new file, ~80-100 lines)
- **Parallel?**: Yes -- independent of other test files.

### Subtask T021 -- Create ParserTests.fs with User Story 1 Acceptance Scenarios

- **Purpose**: Test the core parser against User Story 1's acceptance scenarios (basic states, transitions, events, guards, final states, compound states).

- **Steps**:
  1. Create `test/Frank.Statecharts.Tests/Scxml/ParserTests.fs`.
  2. Module declaration:
     ```fsharp
     module Frank.Statecharts.Tests.Scxml.ParserTests

     open Expecto
     open Frank.Statecharts.Scxml.Types
     open Frank.Statecharts.Scxml.Parser
     ```
  3. Write tests for each acceptance scenario:

     **Scenario 1**: `<scxml initial="idle">` with two states.
     ```fsharp
     testCase "US1-S1: initial state and basic states" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
           <state id="idle"/>
           <state id="active"/>
         </scxml>"""
         let result = parseString xml
         Expect.isSome result.Document "should parse successfully"
         let doc = result.Document.Value
         Expect.equal doc.InitialId (Some "idle") "initial state should be idle"
         Expect.equal doc.States.Length 2 "should have 2 states"
         Expect.equal doc.States.[0].Id (Some "idle") "first state is idle"
         Expect.equal doc.States.[1].Id (Some "active") "second state is active"
     ```

     **Scenario 2**: Transition with event and target.
     ```fsharp
     testCase "US1-S2: transition with event and target" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
           <state id="idle">
             <transition event="start" target="active"/>
           </state>
           <state id="active"/>
         </scxml>"""
         let result = parseString xml
         let doc = result.Document.Value
         let idleState = doc.States.[0]
         Expect.equal idleState.Transitions.Length 1 "idle has one transition"
         let t = idleState.Transitions.[0]
         Expect.equal t.Event (Some "start") "event is start"
         Expect.equal t.Targets ["active"] "target is active"
     ```

     **Scenario 3**: Guarded transition.
     ```fsharp
     testCase "US1-S3: guarded transition" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
           <state id="idle">
             <transition event="submit" cond="isValid" target="submitted"/>
           </state>
           <state id="submitted"/>
         </scxml>"""
         let result = parseString xml
         let t = result.Document.Value.States.[0].Transitions.[0]
         Expect.equal t.Event (Some "submit") "event is submit"
         Expect.equal t.Guard (Some "isValid") "guard is isValid"
         Expect.equal t.Targets ["submitted"] "target is submitted"
     ```

     **Scenario 4**: Final state.
     ```fsharp
     testCase "US1-S4: final state" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
           <state id="idle"/>
           <final id="done"/>
         </scxml>"""
         let result = parseString xml
         let finalState = result.Document.Value.States.[1]
         Expect.equal finalState.Id (Some "done") "final state id"
         Expect.equal finalState.Kind Final "kind is Final"
     ```

     **Scenario 5**: Compound (nested) states.
     ```fsharp
     testCase "US1-S5: compound states" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="parent">
           <state id="parent">
             <state id="child1"/>
             <state id="child2"/>
           </state>
         </scxml>"""
         let result = parseString xml
         let parent = result.Document.Value.States.[0]
         Expect.equal parent.Kind Compound "parent is Compound"
         Expect.equal parent.Children.Length 2 "parent has 2 children"
         Expect.equal parent.Children.[0].Id (Some "child1") "first child"
         Expect.equal parent.Children.[1].Id (Some "child2") "second child"
     ```

- **Files**: `test/Frank.Statecharts.Tests/Scxml/ParserTests.fs` (new file, ~120-150 lines)
- **Parallel?**: Yes -- independent of other test files.
- **Notes**: Use the canonical SCXML namespace (`xmlns="http://www.w3.org/2005/07/scxml"`) in all test inputs. Edge cases for no-namespace docs are in T024.

### Subtask T024 -- Add Parser Edge Case Tests

- **Purpose**: Test the parser against all edge cases from the spec's "Edge Cases" section.

- **Steps**:
  1. Add edge case tests to `test/Frank.Statecharts.Tests/Scxml/ParserTests.fs` (in the same `testList` or a separate `testList "Scxml.Parser.EdgeCases"`):

     - **Empty SCXML**: `<scxml xmlns="..."/>` with no child states -> valid but empty AST, not an error.
     - **No initial attribute**: `<scxml xmlns="..."><state id="first"/></scxml>` -> `InitialId = Some "first"` (inferred from first child).
     - **Self-transition**: `<transition event="retry" target="s1"/>` inside `<state id="s1">` -> target is the containing state.
     - **Eventless transition**: `<transition target="next"/>` with no event attribute -> `Event = None`.
     - **Targetless transition**: `<transition event="check" cond="isReady"/>` with no target -> `Targets = []`.
     - **Multiple transitions same event different guards**: Two `<transition event="submit" cond="isValid" target="ok"/>` and `<transition event="submit" cond="isInvalid" target="error"/>` -> two transitions in order.
     - **Deeply nested states**: 5 levels of nested `<state>` elements -> all parsed correctly with correct parent-child relationships.
     - **Prefixed namespace**: `<sc:scxml xmlns:sc="http://www.w3.org/2005/07/scxml">` with `<sc:state>` -> parsed correctly.
     - **Multiple space-separated targets**: `<transition target="s1 s2 s3"/>` -> `Targets = ["s1"; "s2"; "s3"]`.
     - **Unicode in IDs**: `<state id="zustand_bereit"/>` -> ID preserved correctly.
     - **XML comments**: SCXML with `<!-- comment -->` between elements -> comments ignored, states parsed correctly.

- **Files**: `test/Frank.Statecharts.Tests/Scxml/ParserTests.fs` (extend existing)
- **Parallel?**: Yes -- independent of other test files.
- **Notes**: Each edge case should be a separate `testCase` for clear failure reporting.

### Subtask T025 -- Create ErrorTests.fs with Malformed XML and Structural Error Tests

- **Purpose**: Test that malformed XML input produces structured `ParseError` results and that structural issues produce warnings.

- **Steps**:
  1. Create `test/Frank.Statecharts.Tests/Scxml/ErrorTests.fs`.
  2. Module declaration:
     ```fsharp
     module Frank.Statecharts.Tests.Scxml.ErrorTests

     open Expecto
     open Frank.Statecharts.Scxml.Types
     open Frank.Statecharts.Scxml.Parser
     ```
  3. Write tests:

     **Malformed XML tests**:
     - Unclosed tag: `<scxml xmlns="..."><state id="s1">` -> `Document = None`, `Errors` has one `ParseError` with description and position.
     - Invalid XML characters: `<scxml xmlns="...">&invalid;</scxml>` -> structured error.
     - Empty string: `""` -> structured error (XmlException on empty input).
     - Missing closing tag: `<scxml xmlns="..."><state id="s1"/>`  (no `</scxml>`) -> structured error.

     ```fsharp
     testCase "malformed XML: unclosed tag" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml"><state id="s1">"""
         let result = parseString xml
         Expect.isNone result.Document "document should be None"
         Expect.isNonEmpty result.Errors "should have errors"
         let err = result.Errors.[0]
         Expect.isTrue (err.Description.Length > 0) "error should have description"
         Expect.isSome err.Position "error should have position"
     ```

     **Structural validation tests** (if implemented):
     - Non-scxml root element: `<notscxml/>` -> error.

     **Warning tests**:
     - Unknown element inside `<state>`: `<state id="s1"><unknown/></state>` -> warning emitted.

- **Files**: `test/Frank.Statecharts.Tests/Scxml/ErrorTests.fs` (new file, ~80-100 lines)
- **Parallel?**: Yes -- independent of other test files.

### Subtask T029 -- Update Test `.fsproj` with Scxml Test Compile Entries

- **Purpose**: Wire all SCXML test files into the test project compilation.

- **Steps**:
  1. Open `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`.
  2. Add compile entries for all SCXML test files before `Program.fs`. The order should be:
     ```xml
     <Compile Include="Scxml/TypeTests.fs" />
     <Compile Include="Scxml/ParserTests.fs" />
     <Compile Include="Scxml/ErrorTests.fs" />
     <Compile Include="Scxml/GeneratorTests.fs" />
     <Compile Include="Scxml/RoundTripTests.fs" />
     ```

  3. Place these entries after the existing `Wsd/RoundTripTests.fs` entry and before `StatechartETagProviderTests.fs` or `Program.fs`.

  **Note**: `GeneratorTests.fs` and `RoundTripTests.fs` will be created by WP06. Including them in the `.fsproj` now is fine -- F# will error if the files don't exist when you build, but since WP05 and WP06 will both be merged before tests run, the files will be present. Alternatively, only include the files that exist and let WP06 add its own entries. The safer approach is to include only files from this WP:
     ```xml
     <Compile Include="Scxml/TypeTests.fs" />
     <Compile Include="Scxml/ParserTests.fs" />
     <Compile Include="Scxml/ErrorTests.fs" />
     ```
  And let WP06 add `GeneratorTests.fs` and `RoundTripTests.fs`.

- **Files**: `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` (edit existing)
- **Parallel?**: No -- must be done after test files are created.

## Test Strategy

Run all SCXML tests:
```bash
dotnet test test/Frank.Statecharts.Tests --filter "Scxml"
```

Expected: All type, parser, and error tests pass. No failures.

The filter `"Scxml"` matches test names containing "Scxml" (Expecto test list names).

## Risks & Mitigations

- **Risk**: Test compile order wrong, causing F# forward reference errors.
  - **Mitigation**: TypeTests -> ParserTests -> ErrorTests order in `.fsproj`.

- **Risk**: Parser behavior differs from expected test assertions (e.g., initial state inference edge cases).
  - **Mitigation**: Tests are derived directly from the spec acceptance scenarios. If a test fails, the parser needs fixing, not the test.

- **Risk**: Namespace handling tests fail on prefixed namespace documents.
  - **Mitigation**: This validates FR-014. If it fails, the parser needs to handle both namespace forms.

## Review Guidance

- Verify all 5 User Story 1 acceptance scenarios are tested.
- Verify edge cases cover the spec's "Edge Cases" section comprehensively.
- Verify error tests confirm `Document = None` for malformed XML.
- Verify error tests confirm `Position` is populated from `XmlException`.
- Verify test naming follows `Scxml.` prefix pattern for filter compatibility.
- Verify `.fsproj` compile entries are in correct order.
- Verify `dotnet test test/Frank.Statecharts.Tests --filter "Scxml"` passes.

## Activity Log

- 2026-03-16T01:17:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T14:33:11Z – unknown – lane=done – Moved to done
