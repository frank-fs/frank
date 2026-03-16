---
work_package_id: WP06
title: Tests -- Data Model, Advanced Parser, Generator, and Roundtrip
lane: planned
dependencies:
- WP03
subtasks:
- T022
- T023
- T026
- T027
- T028
phase: Phase 3 - Testing
assignee: ''
agent: ''
shell_pid: ''
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-16T01:17:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-005, FR-008, FR-009, FR-010, FR-011, FR-016, FR-017, FR-018, FR-019, FR-020, FR-021, FR-022, FR-023]
---

# Work Package Prompt: WP06 -- Tests -- Data Model, Advanced Parser, Generator, and Roundtrip

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

Depends on WP03 and WP04 -- run:
```bash
spec-kitty implement WP06 --base WP04
```

**Note**: WP06 requires both the parser (WP02+WP03) and the generator (WP04). Since WP03 depends on WP02 and WP04 depends on WP01, the implementation branch should be based on whichever is latest. If WP03 and WP04 are on separate branches, merge both before starting WP06. The `--base WP04` assumes WP03 has already been merged into the feature branch.

---

## Objectives & Success Criteria

- Test data model parsing (User Story 3 acceptance scenarios).
- Test parallel, history, and invoke parsing (User Story 4 acceptance scenarios).
- Test SCXML generation (User Story 2 acceptance scenarios).
- Test roundtrip consistency (User Story 5 acceptance scenarios).
- Update test `.fsproj` to include `GeneratorTests.fs` and `RoundTripTests.fs`.
- All tests pass with `dotnet test test/Frank.Statecharts.Tests --filter "Scxml"`.

## Context & Constraints

- **Spec**: User Stories 2-5 acceptance scenarios, SC-001 through SC-005
- **Existing tests**: `test/Frank.Statecharts.Tests/Scxml/ParserTests.fs` (from WP05)
- **Dependencies**: Requires working parser (WP02+WP03) and generator (WP04)
- **Roundtrip note**: `Position` fields will differ between original parse and re-parse of generated output. The comparison strategy must account for this.

## Subtasks & Detailed Guidance

### Subtask T022 -- Add Data Model Parsing Tests (User Story 3)

- **Purpose**: Test `<datamodel>`/`<data>` element parsing against User Story 3 acceptance scenarios.

- **Steps**:
  1. Add tests to `test/Frank.Statecharts.Tests/Scxml/ParserTests.fs` (extend the existing test list or add a new `testList "Scxml.Parser.DataModel"`):

     **Scenario 1**: Data entries with `expr` attribute.
     ```fsharp
     testCase "US3-S1: data entries with expr attribute" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
           <datamodel>
             <data id="count" expr="0"/>
             <data id="name" expr="'default'"/>
           </datamodel>
           <state id="s1"/>
         </scxml>"""
         let result = parseString xml
         let doc = result.Document.Value
         Expect.equal doc.DataEntries.Length 2 "should have 2 data entries"
         Expect.equal doc.DataEntries.[0].Id "count" "first entry id"
         Expect.equal doc.DataEntries.[0].Expression (Some "0") "first entry expr"
         Expect.equal doc.DataEntries.[1].Id "name" "second entry id"
         Expect.equal doc.DataEntries.[1].Expression (Some "'default'") "second entry expr"
     ```

     **Scenario 2**: Data entry with no `expr` attribute.
     ```fsharp
     testCase "US3-S2: data entry with no expr" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
           <datamodel>
             <data id="items"/>
           </datamodel>
           <state id="s1"/>
         </scxml>"""
         let result = parseString xml
         let entry = result.Document.Value.DataEntries.[0]
         Expect.equal entry.Id "items" "entry id"
         Expect.isNone entry.Expression "expression should be None"
     ```

     **Scenario 3**: State-scoped datamodel.
     ```fsharp
     testCase "US3-S3: state-scoped datamodel" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
           <state id="s1">
             <datamodel>
               <data id="localVar" expr="42"/>
             </datamodel>
           </state>
         </scxml>"""
         let result = parseString xml
         let state = result.Document.Value.States.[0]
         Expect.equal state.DataEntries.Length 1 "state should have 1 data entry"
         Expect.equal state.DataEntries.[0].Id "localVar" "entry id"
         Expect.equal state.DataEntries.[0].Expression (Some "42") "entry expr"
     ```

     **Additional**: `<data>` with child text content (alternative to `expr`).
     ```fsharp
     testCase "data with child text content" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
           <datamodel>
             <data id="config">some content</data>
           </datamodel>
           <state id="s1"/>
         </scxml>"""
         let result = parseString xml
         let entry = result.Document.Value.DataEntries.[0]
         Expect.equal entry.Id "config" "entry id"
         Expect.equal entry.Expression (Some "some content") "expression from child content"
     ```

- **Files**: `test/Frank.Statecharts.Tests/Scxml/ParserTests.fs` (extend existing)
- **Parallel?**: Yes -- appends to existing file, independent of T023.

### Subtask T023 -- Add Parallel, History, and Invoke Parsing Tests (User Story 4)

- **Purpose**: Test `<parallel>`, `<history>`, and `<invoke>` parsing against User Story 4 acceptance scenarios.

- **Steps**:
  1. Add tests to `test/Frank.Statecharts.Tests/Scxml/ParserTests.fs`:

     **Scenario 1**: Parallel state with child states.
     ```fsharp
     testCase "US4-S1: parallel state with children" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="p1">
           <parallel id="p1">
             <state id="region1"/>
             <state id="region2"/>
           </parallel>
         </scxml>"""
         let result = parseString xml
         let p = result.Document.Value.States.[0]
         Expect.equal p.Kind Parallel "kind is Parallel"
         Expect.equal p.Id (Some "p1") "id is p1"
         Expect.equal p.Children.Length 2 "has 2 child states"
     ```

     **Scenario 2**: History with `type="deep"`.
     ```fsharp
     testCase "US4-S2: history with type deep" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
           <state id="s1">
             <history id="h1" type="deep"/>
             <state id="child1"/>
           </state>
         </scxml>"""
         let result = parseString xml
         let state = result.Document.Value.States.[0]
         Expect.equal state.HistoryNodes.Length 1 "has 1 history node"
         let h = state.HistoryNodes.[0]
         Expect.equal h.Id "h1" "history id"
         Expect.equal h.Kind Deep "history kind is Deep"
     ```

     **Scenario 3**: History defaults to shallow.
     ```fsharp
     testCase "US4-S3: history defaults to shallow" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
           <state id="s1">
             <history id="h2"/>
             <state id="child1"/>
           </state>
         </scxml>"""
         let result = parseString xml
         let h = result.Document.Value.States.[0].HistoryNodes.[0]
         Expect.equal h.Kind Shallow "history kind defaults to Shallow"
     ```

     **Scenario 4**: Invoke with attributes.
     ```fsharp
     testCase "US4-S4: invoke with attributes" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
           <state id="s1">
             <invoke type="http" src="https://example.com/service"/>
           </state>
         </scxml>"""
         let result = parseString xml
         let state = result.Document.Value.States.[0]
         Expect.equal state.InvokeNodes.Length 1 "has 1 invoke node"
         let inv = state.InvokeNodes.[0]
         Expect.equal inv.InvokeType (Some "http") "invoke type"
         Expect.equal inv.Src (Some "https://example.com/service") "invoke src"
     ```

     **Additional**: History with default transition (child `<transition>`).
     ```fsharp
     testCase "history with default transition" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
           <state id="s1">
             <history id="h1" type="shallow">
               <transition target="child1"/>
             </history>
             <state id="child1"/>
           </state>
         </scxml>"""
         let result = parseString xml
         let h = result.Document.Value.States.[0].HistoryNodes.[0]
         Expect.isSome h.DefaultTransition "should have default transition"
         Expect.equal h.DefaultTransition.Value.Targets ["child1"] "default target"
     ```

- **Files**: `test/Frank.Statecharts.Tests/Scxml/ParserTests.fs` (extend existing)
- **Parallel?**: Yes -- appends to existing file, independent of T022.

### Subtask T026 -- Create GeneratorTests.fs with User Story 2 Acceptance Scenarios

- **Purpose**: Test the SCXML generator against User Story 2 acceptance scenarios. Verify generated XML contains expected elements and attributes.

- **Steps**:
  1. Create `test/Frank.Statecharts.Tests/Scxml/GeneratorTests.fs`.
  2. Module declaration:
     ```fsharp
     module Frank.Statecharts.Tests.Scxml.GeneratorTests

     open Expecto
     open Frank.Statecharts.Scxml.Types
     open Frank.Statecharts.Scxml.Generator
     open Frank.Statecharts.Scxml.Parser
     ```
  3. Test strategy: construct `ScxmlDocument` values programmatically, generate XML, then re-parse the XML to verify structure. This leverages the parser to validate generator output.

     **Scenario 1**: Basic states with initial and final.
     ```fsharp
     testCase "US2-S1: generate basic states" <| fun _ ->
         let doc =
             { Name = None; InitialId = Some "idle"
               DatamodelType = None; Binding = None
               States =
                 [ { Id = Some "idle"; Kind = Simple; InitialId = None
                     Transitions = []; Children = []; DataEntries = []
                     HistoryNodes = []; InvokeNodes = []; Position = None }
                   { Id = Some "active"; Kind = Simple; InitialId = None
                     Transitions = []; Children = []; DataEntries = []
                     HistoryNodes = []; InvokeNodes = []; Position = None }
                   { Id = Some "done"; Kind = Final; InitialId = None
                     Transitions = []; Children = []; DataEntries = []
                     HistoryNodes = []; InvokeNodes = []; Position = None } ]
               DataEntries = []; Position = None }
         let xml = generate doc
         let reparsed = parseString xml
         Expect.isSome reparsed.Document "generated XML should reparse"
         let rdoc = reparsed.Document.Value
         Expect.equal rdoc.InitialId (Some "idle") "initial state preserved"
         Expect.equal rdoc.States.Length 3 "3 states"
         Expect.equal rdoc.States.[2].Kind Final "third state is final"
     ```

     **Scenario 2**: Guarded transition.
     ```fsharp
     testCase "US2-S2: generate guarded transition" <| fun _ ->
         let doc =
             { Name = None; InitialId = Some "active"
               DatamodelType = None; Binding = None
               States =
                 [ { Id = Some "active"; Kind = Simple; InitialId = None
                     Transitions =
                       [ { Event = Some "submit"; Guard = Some "isValid"
                           Targets = ["submitted"]; TransitionType = External
                           Position = None } ]
                     Children = []; DataEntries = []
                     HistoryNodes = []; InvokeNodes = []; Position = None }
                   { Id = Some "submitted"; Kind = Simple; InitialId = None
                     Transitions = []; Children = []; DataEntries = []
                     HistoryNodes = []; InvokeNodes = []; Position = None } ]
               DataEntries = []; Position = None }
         let xml = generate doc
         let t = (parseString xml).Document.Value.States.[0].Transitions.[0]
         Expect.equal t.Event (Some "submit") "event preserved"
         Expect.equal t.Guard (Some "isValid") "guard preserved"
         Expect.equal t.Targets ["submitted"] "target preserved"
     ```

     **Scenario 3**: Compound (hierarchical) states.
     ```fsharp
     testCase "US2-S3: generate compound states" <| fun _ ->
         let doc =
             { Name = None; InitialId = Some "parent"
               DatamodelType = None; Binding = None
               States =
                 [ { Id = Some "parent"; Kind = Compound; InitialId = Some "child1"
                     Transitions = []
                     Children =
                       [ { Id = Some "child1"; Kind = Simple; InitialId = None
                           Transitions = []; Children = []; DataEntries = []
                           HistoryNodes = []; InvokeNodes = []; Position = None }
                         { Id = Some "child2"; Kind = Simple; InitialId = None
                           Transitions = []; Children = []; DataEntries = []
                           HistoryNodes = []; InvokeNodes = []; Position = None } ]
                     DataEntries = []
                     HistoryNodes = []; InvokeNodes = []; Position = None } ]
               DataEntries = []; Position = None }
         let xml = generate doc
         let parent = (parseString xml).Document.Value.States.[0]
         Expect.equal parent.Kind Compound "parent is compound"
         Expect.equal parent.Children.Length 2 "2 children"
     ```

     **Scenario 4**: Data model.
     ```fsharp
     testCase "US2-S4: generate datamodel" <| fun _ ->
         let doc =
             { Name = None; InitialId = Some "s1"
               DatamodelType = None; Binding = None
               States =
                 [ { Id = Some "s1"; Kind = Simple; InitialId = None
                     Transitions = []; Children = []; DataEntries = []
                     HistoryNodes = []; InvokeNodes = []; Position = None } ]
               DataEntries =
                 [ { Id = "count"; Expression = Some "0"; Position = None }
                   { Id = "name"; Expression = Some "'default'"; Position = None } ]
               Position = None }
         let xml = generate doc
         let rdoc = (parseString xml).Document.Value
         Expect.equal rdoc.DataEntries.Length 2 "2 data entries"
         Expect.equal rdoc.DataEntries.[0].Id "count" "first entry"
         Expect.equal rdoc.DataEntries.[0].Expression (Some "0") "first expr"
     ```

- **Files**: `test/Frank.Statecharts.Tests/Scxml/GeneratorTests.fs` (new file, ~150-180 lines)
- **Parallel?**: Yes -- independent file.
- **Notes**: The re-parse validation strategy tests both generator correctness and parser-generator compatibility simultaneously.

### Subtask T027 -- Add Generator Tests for History, Invoke, and Edge Cases

- **Purpose**: Test generator output for history, invoke, and edge cases not covered in the main acceptance scenarios.

- **Steps**:
  1. Add to `test/Frank.Statecharts.Tests/Scxml/GeneratorTests.fs`:

     **History generation**:
     ```fsharp
     testCase "generate history nodes" <| fun _ ->
         let doc =
             { Name = None; InitialId = Some "s1"
               DatamodelType = None; Binding = None
               States =
                 [ { Id = Some "s1"; Kind = Compound; InitialId = Some "child1"
                     Transitions = []
                     Children =
                       [ { Id = Some "child1"; Kind = Simple; InitialId = None
                           Transitions = []; Children = []; DataEntries = []
                           HistoryNodes = []; InvokeNodes = []; Position = None } ]
                     DataEntries = []
                     HistoryNodes =
                       [ { Id = "h1"; Kind = Deep
                           DefaultTransition = None; Position = None } ]
                     InvokeNodes = []; Position = None } ]
               DataEntries = []; Position = None }
         let xml = generate doc
         let state = (parseString xml).Document.Value.States.[0]
         Expect.equal state.HistoryNodes.Length 1 "has history node"
         Expect.equal state.HistoryNodes.[0].Kind Deep "deep history"
     ```

     **Invoke generation**:
     ```fsharp
     testCase "generate invoke nodes" <| fun _ ->
         // Similar pattern: construct doc with InvokeNodes, generate, reparse, verify
     ```

     **Edge cases**:
     - **Empty document**: `ScxmlDocument` with no states -> generates valid `<scxml>` with no children.
     - **Optional attributes**: `Name = Some "myMachine"` -> `<scxml name="myMachine" ...>`.
     - **Internal transition type**: `TransitionType = Internal` -> `type="internal"` attribute present in output.
     - **Multi-target transition**: `Targets = ["s1"; "s2"]` -> `target="s1 s2"` in output.
     - **Data entry with no expression**: `Expression = None` -> `<data id="x"/>` (no `expr` attribute).

- **Files**: `test/Frank.Statecharts.Tests/Scxml/GeneratorTests.fs` (extend)
- **Parallel?**: Yes -- same file as T026 but can be developed together.

### Subtask T028 -- Create RoundTripTests.fs with Roundtrip Consistency Tests

- **Purpose**: Test that parse -> generate -> parse produces structurally equal ASTs (User Story 5).

- **Steps**:
  1. Create `test/Frank.Statecharts.Tests/Scxml/RoundTripTests.fs`.
  2. Module declaration:
     ```fsharp
     module Frank.Statecharts.Tests.Scxml.RoundTripTests

     open Expecto
     open Frank.Statecharts.Scxml.Types
     open Frank.Statecharts.Scxml.Parser
     open Frank.Statecharts.Scxml.Generator
     ```

  3. Create a helper to strip `Position` fields for comparison (positions will differ between original parse and re-parse of generated output):
     ```fsharp
     let rec private stripPositions (state: ScxmlState) : ScxmlState =
         { state with
             Position = None
             Transitions = state.Transitions |> List.map (fun t -> { t with Position = None })
             Children = state.Children |> List.map stripPositions
             DataEntries = state.DataEntries |> List.map (fun d -> { d with Position = None })
             HistoryNodes = state.HistoryNodes |> List.map (fun h ->
                 { h with Position = None
                          DefaultTransition = h.DefaultTransition |> Option.map (fun t -> { t with Position = None }) })
             InvokeNodes = state.InvokeNodes |> List.map (fun i -> { i with Position = None }) }

     let private stripDocPositions (doc: ScxmlDocument) : ScxmlDocument =
         { doc with
             Position = None
             States = doc.States |> List.map stripPositions
             DataEntries = doc.DataEntries |> List.map (fun d -> { d with Position = None }) }
     ```

  4. Write roundtrip tests:

     **Scenario 1**: Reference document with states, transitions, guards, data model.
     ```fsharp
     testCase "US5-S1: roundtrip reference document" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
           <datamodel>
             <data id="count" expr="0"/>
           </datamodel>
           <state id="idle">
             <transition event="start" target="active"/>
           </state>
           <state id="active">
             <transition event="submit" cond="isValid" target="done"/>
             <transition event="cancel" target="idle"/>
           </state>
           <final id="done"/>
         </scxml>"""
         let result1 = parseString xml
         let generated = generate result1.Document.Value
         let result2 = parseString generated
         Expect.isSome result2.Document "re-parsed successfully"
         let doc1 = stripDocPositions result1.Document.Value
         let doc2 = stripDocPositions result2.Document.Value
         Expect.equal doc1 doc2 "ASTs should be structurally equal"
     ```

     **Scenario 2**: Minimal document.
     ```fsharp
     testCase "US5-S2: roundtrip minimal document" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
           <state id="s1"/>
         </scxml>"""
         let result1 = parseString xml
         let generated = generate result1.Document.Value
         let result2 = parseString generated
         let doc1 = stripDocPositions result1.Document.Value
         let doc2 = stripDocPositions result2.Document.Value
         Expect.equal doc1 doc2 "minimal doc roundtrips"
     ```

     **Scenario 3**: Document with parallel, history, invoke.
     ```fsharp
     testCase "US5-S3: roundtrip parallel, history, invoke" <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="p1">
           <parallel id="p1">
             <state id="r1">
               <history id="h1" type="deep"/>
             </state>
             <state id="r2">
               <invoke type="http" src="https://example.com"/>
             </state>
           </parallel>
         </scxml>"""
         let result1 = parseString xml
         let generated = generate result1.Document.Value
         let result2 = parseString generated
         let doc1 = stripDocPositions result1.Document.Value
         let doc2 = stripDocPositions result2.Document.Value
         Expect.equal doc1 doc2 "parallel/history/invoke roundtrip"
     ```

     **Additional**: Roundtrip a complex document (traffic light or tic-tac-toe state machine, ~10+ states).

  5. Update test `.fsproj` to include new files if not already done by WP05:
     ```xml
     <Compile Include="Scxml/GeneratorTests.fs" />
     <Compile Include="Scxml/RoundTripTests.fs" />
     ```

- **Files**: `test/Frank.Statecharts.Tests/Scxml/RoundTripTests.fs` (new file, ~120-150 lines)
- **Parallel?**: Yes -- independent file.
- **Notes**:
  - The `stripPositions` helper is critical -- positions will differ because the generator produces different XML formatting than the original input. Only structural content matters for roundtrip verification.
  - Roundtrip tests validate SC-003 (structurally equal ASTs after roundtrip).
  - The generated XML uses `expr` attribute form for data entries, even if the original used child text content. This is expected behavior (canonical output).

## Test Strategy

Run all SCXML tests:
```bash
dotnet test test/Frank.Statecharts.Tests --filter "Scxml"
```

Expected: All tests pass including new data model, advanced parser, generator, and roundtrip tests.

## Risks & Mitigations

- **Risk**: Roundtrip fails because generator produces `expr` attribute but parser originally read child text content.
  - **Mitigation**: The `stripPositions` helper handles position differences. For data entry form differences, roundtrip comparison should still work because the parser normalizes both forms to the same `DataEntry` record (same `Id` and `Expression` values regardless of input form).

- **Risk**: Generator output ordering differs from parser expectations (e.g., `<datamodel>` appears after `<state>` elements in generated output but parser expects any order).
  - **Mitigation**: The parser should handle elements in any order within their parent. If roundtrip fails due to ordering, fix the generator to emit elements in canonical order (datamodel, transitions, history, invoke, child states).

- **Risk**: Test file compile order issues when merging WP05 and WP06 branches.
  - **Mitigation**: Coordinate `.fsproj` entries. WP05 adds TypeTests, ParserTests, ErrorTests. WP06 adds GeneratorTests, RoundTripTests. The final order should be: TypeTests -> ParserTests -> ErrorTests -> GeneratorTests -> RoundTripTests.

## Review Guidance

- Verify all 3 User Story 3 acceptance scenarios are tested (T022).
- Verify all 4 User Story 4 acceptance scenarios are tested (T023).
- Verify all 4 User Story 2 acceptance scenarios are tested (T026).
- Verify all 3 User Story 5 acceptance scenarios are tested (T028).
- Verify `stripPositions` helper correctly nullifies all `Position` fields recursively.
- Verify roundtrip tests parse -> generate -> parse -> compare (not just string comparison).
- Verify generator tests use re-parse strategy (construct -> generate -> parse -> verify), not string matching.
- Verify `.fsproj` includes GeneratorTests.fs and RoundTripTests.fs in correct order.
- Verify `dotnet test test/Frank.Statecharts.Tests --filter "Scxml"` passes.

## Activity Log

- 2026-03-16T01:17:00Z -- system -- lane=planned -- Prompt created.
