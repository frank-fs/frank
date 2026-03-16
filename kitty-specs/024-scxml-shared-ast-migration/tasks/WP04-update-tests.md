---
work_package_id: WP04
title: Update All Test Files
lane: planned
dependencies:
- WP02
subtasks:
- T022
- T023
- T024
- T025
- T026
phase: Phase 2 - Test Migration
assignee: ''
agent: ''
shell_pid: ''
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-16T19:26:17Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-029]
---

# Work Package Prompt: WP04 -- Update All Test Files

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: Update `review_status: acknowledged` when you begin addressing feedback.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````bash`

---

## Implementation Command

This WP depends on both WP02 and WP03. Use the later WP as the base (or whichever was merged last):
```bash
spec-kitty implement WP04 --base WP03
```

If WP02 was merged after WP03, use `--base WP02` instead. The key requirement is that both parser and generator migrations are complete before starting this WP.

---

## Objectives & Success Criteria

- Update all 5 SCXML test files to use shared AST types.
- `dotnet test test/Frank.Statecharts.Tests/` passes with zero failures for all SCXML test modules.
- No references to `ScxmlDocument`, `ScxmlState`, `ScxmlTransition`, `ScxmlParseResult`, or `ScxmlStateKind` remain in test files.

## Context & Constraints

- **Spec**: `kitty-specs/024-scxml-shared-ast-migration/spec.md` (FR-029)
- **Test Files**: All under `test/Frank.Statecharts.Tests/Scxml/`
- **Key type mappings** (old -> new):
  - `ScxmlParseResult` -> `Ast.ParseResult`
  - `ScxmlDocument` -> `StatechartDocument`
  - `ScxmlDocument.Name` -> `StatechartDocument.Title`
  - `ScxmlDocument.InitialId` -> `StatechartDocument.InitialStateId`
  - `ScxmlDocument.States` -> extract `StateDecl` entries from `StatechartDocument.Elements`
  - `ScxmlState` -> `StateNode`
  - `ScxmlState.Id` (string option) -> `StateNode.Identifier` (string, empty for no id)
  - `ScxmlState.Kind` (Simple/Compound/Parallel/Final) -> `StateNode.Kind` (Regular/Parallel/Final)
  - `ScxmlState.Transitions` -> extract from `StatechartDocument.Elements` via `TransitionElement`
  - `ScxmlState.DataEntries` -> `StatechartDocument.DataEntries` (flattened)
  - `ScxmlState.HistoryNodes` -> child `StateNode` entries with `Kind = ShallowHistory/DeepHistory`
  - `ScxmlState.InvokeNodes` -> `ScxmlAnnotation(ScxmlInvoke(...))` on `StateNode`
  - `ScxmlTransition` -> `TransitionEdge`
  - `ScxmlTransition.Targets` (string list) -> `TransitionEdge.Target` (string option) + `ScxmlAnnotation(ScxmlMultiTarget(...))`
  - `ScxmlTransition.TransitionType` -> `ScxmlAnnotation(ScxmlTransitionType(...))`
  - `DataEntry.Id` -> `Ast.DataEntry.Name`
  - `ParseError` -> `Ast.ParseFailure`
  - `ParseWarning` -> `Ast.ParseWarning`
  - `result.Document` (option) -> `result.Document` (always present, check for empty)

---

## Subtasks & Detailed Guidance

### Subtask T022 -- Update `ParserTests.fs`

- **Purpose**: Migrate all parser test assertions from SCXML-specific types to shared AST types.
- **Steps**:
  1. Change module opens:
     ```fsharp
     open Frank.Statecharts.Ast
     open Frank.Statecharts.Scxml.Parser
     // Remove: open Frank.Statecharts.Scxml.Types
     ```
  2. Add helper functions at the top of the test module for common extraction patterns:
     ```fsharp
     /// Extract StateDecl entries from a StatechartDocument's Elements.
     let private stateDecls (doc: StatechartDocument) =
         doc.Elements |> List.choose (function StateDecl s -> Some s | _ -> None)

     /// Extract TransitionEdge entries for a given source state from a StatechartDocument.
     let private transitionsFrom (source: string) (doc: StatechartDocument) =
         doc.Elements
         |> List.choose (function TransitionElement t when t.Source = source -> Some t | _ -> None)

     /// Extract history child nodes from a StateNode.
     let private historyChildren (state: StateNode) =
         state.Children
         |> List.filter (fun c -> match c.Kind with ShallowHistory | DeepHistory -> true | _ -> false)

     /// Extract non-history children from a StateNode.
     let private regularChildren (state: StateNode) =
         state.Children
         |> List.filter (fun c -> match c.Kind with ShallowHistory | DeepHistory -> false | _ -> true)
     ```
  3. Update test assertions systematically. Key patterns:
     - **`result.Document.Value`** -> **`result.Document`** (no longer `option`)
     - **`doc.States.[i]`** -> **`(stateDecls doc).[i]`**
     - **`state.Id (Some "idle")`** -> **`state.Identifier "idle"`** (string, not option)
     - **`state.Kind Simple`** -> **`state.Kind Regular`** (Simple/Compound both become Regular)
     - **`state.Kind Compound`** -> **`state.Kind Regular`**
     - **`state.Transitions.[0]`** -> **`(transitionsFrom state.Identifier doc).[0]`**
     - **`t.Targets ["active"]`** -> **`t.Target (Some "active")`** (single target)
     - **`t.Targets ["s1"; "s2"; "s3"]`** -> Check `ScxmlMultiTarget` annotation
     - **`t.TransitionType Internal`** -> Check `ScxmlAnnotation(ScxmlTransitionType(true))`
     - **`doc.DataEntries.[0].Id`** -> **`doc.DataEntries.[0].Name`**
     - **`state.HistoryNodes.[0]`** -> **`(historyChildren state).[0]`**
     - **`h.Kind Deep`** -> **`h.Kind DeepHistory`**
     - **`h.DefaultTransition.Value.Targets`** -> Extract from `ScxmlHistory` annotation's `defaultTarget`
     - **`state.InvokeNodes.[0]`** -> Extract from `ScxmlAnnotation(ScxmlInvoke(...))` on state
  4. For the `dataModelTests` section (US3-S3: state-scoped datamodel): state-scoped data is now flattened into `doc.DataEntries`. Update the test to check `doc.DataEntries` instead of `state.DataEntries`.
  5. For the `advancedParserTests` section: history and invoke are now checked via annotations and child nodes, not via `.HistoryNodes` and `.InvokeNodes` fields.
- **Files**: `test/Frank.Statecharts.Tests/Scxml/ParserTests.fs`
- **Notes**: This is the largest test file update (~35 test cases). Work through methodically. Run `dotnet build` after updating each test list to catch issues early.

### Subtask T023 -- Update `GeneratorTests.fs`

- **Purpose**: Migrate all generator test cases from constructing `ScxmlDocument`/`ScxmlState`/`ScxmlTransition` to constructing `StatechartDocument`/`StateNode`/`TransitionEdge`.
- **Steps**:
  1. Change module opens:
     ```fsharp
     open Frank.Statecharts.Ast
     open Frank.Statecharts.Scxml.Generator
     open Frank.Statecharts.Scxml.Parser
     // Remove: open Frank.Statecharts.Scxml.Types
     ```
  2. Add the same helper functions as in T022 (or reference a shared test utilities module if one exists).
  3. Rewrite each test case's `ScxmlDocument` construction to `StatechartDocument` construction.

     **Example transformation** (US2-S1: generate basic states):

     Old:
     ```fsharp
     let doc =
         { Name = None; InitialId = Some "idle"; DatamodelType = None; Binding = None
           States =
             [ { Id = Some "idle"; Kind = Simple; InitialId = None; Transitions = []
                 Children = []; DataEntries = []; HistoryNodes = []; InvokeNodes = []; Position = None }
               { Id = Some "done"; Kind = Final; ... } ]
           DataEntries = []; Position = None }
     ```

     New:
     ```fsharp
     let doc =
         { Title = None; InitialStateId = Some "idle"
           Elements =
             [ StateDecl { Identifier = "idle"; Label = None; Kind = Regular
                           Children = []; Activities = None; Position = None; Annotations = [] }
               StateDecl { Identifier = "active"; Label = None; Kind = Regular
                           Children = []; Activities = None; Position = None; Annotations = [] }
               StateDecl { Identifier = "done"; Label = None; Kind = Final
                           Children = []; Activities = None; Position = None; Annotations = [] } ]
           DataEntries = []; Annotations = [] }
     ```

  4. Tests that constructed `ScxmlTransition` values must now construct `TransitionEdge` values as `TransitionElement` entries in `Elements`:
     ```fsharp
     Elements =
         [ StateDecl { Identifier = "active"; ... Annotations = []; ... }
           StateDecl { Identifier = "submitted"; ... }
           TransitionElement
               { Source = "active"; Target = Some "submitted"
                 Event = Some "submit"; Guard = Some "isValid"
                 Action = None; Parameters = []; Position = None; Annotations = [] } ]
     ```
  5. For tests with transitions that have internal type, add annotation:
     ```fsharp
     Annotations = [ ScxmlAnnotation(ScxmlTransitionType(true)) ]
     ```
  6. For tests with multi-target transitions, add annotation:
     ```fsharp
     Annotations = [ ScxmlAnnotation(ScxmlMultiTarget(["s2"; "s3"])) ]
     ```
  7. For tests with history nodes: history becomes a child `StateNode` with `Kind = DeepHistory/ShallowHistory` and `ScxmlAnnotation(ScxmlHistory(...))`.
  8. For tests with invoke nodes: invoke becomes `ScxmlAnnotation(ScxmlInvoke(...))` on the parent `StateNode`.
  9. For tests with data entries: use `Ast.DataEntry` with `.Name` instead of `.Id`.
  10. Update assertions on re-parsed output (many tests do `generate doc |> parseString`): use the same helper functions from T022.
- **Files**: `test/Frank.Statecharts.Tests/Scxml/GeneratorTests.fs`
- **Notes**: This file has ~14 test cases, each constructing a full document. The transformation is mechanical but verbose.

### Subtask T024 -- Update `RoundTripTests.fs`

- **Purpose**: Migrate round-trip tests to work with shared AST types.
- **Steps**:
  1. Change module opens:
     ```fsharp
     open Frank.Statecharts.Ast
     open Frank.Statecharts.Scxml.Parser
     open Frank.Statecharts.Scxml.Generator
     // Remove: open Frank.Statecharts.Scxml.Types
     ```
  2. Rewrite `stripPositions` helper to walk `StateNode`:
     ```fsharp
     let rec private stripStatePositions (state: StateNode) : StateNode =
         { state with
             Position = None
             Children = state.Children |> List.map stripStatePositions }
     ```
  3. Rewrite `stripDocPositions` to walk `StatechartDocument`:
     ```fsharp
     let private stripDocPositions (doc: StatechartDocument) : StatechartDocument =
         { doc with
             Elements =
                 doc.Elements
                 |> List.map (fun el ->
                     match el with
                     | StateDecl s -> StateDecl (stripStatePositions s)
                     | TransitionElement t -> TransitionElement { t with Position = None }
                     | NoteElement n -> NoteElement { n with Position = None }
                     | other -> other)
             DataEntries =
                 doc.DataEntries
                 |> List.map (fun d -> { d with Position = None }) }
     ```
  4. Update test assertions:
     - **`result1.Document.Value`** -> **`result1.Document`**
     - **`Expect.isSome result2.Document`** -> Not needed (always present)
     - Comparison: `Expect.equal doc1 doc2` stays the same but operates on `StatechartDocument` now.
  5. The round-trip test for `"roundtrip state-scoped datamodel"` needs attention: after migration, state-scoped data entries are flattened into `StatechartDocument.DataEntries`. The generated SCXML will put them at document level. Re-parsing will also put them at document level. So the round-trip should still pass, but the SCXML output structure changes (data moves from state level to document level). Verify this is acceptable per the spec (it matches existing Mapper behavior).
- **Files**: `test/Frank.Statecharts.Tests/Scxml/RoundTripTests.fs`
- **Notes**: The round-trip tests are the strongest validation of the migration. If these pass, the migration is correct.

### Subtask T025 -- Update `ErrorTests.fs`

- **Purpose**: Migrate error/warning test assertions from `ScxmlParseResult` to `Ast.ParseResult`.
- **Steps**:
  1. Change module opens:
     ```fsharp
     open Frank.Statecharts.Ast
     open Frank.Statecharts.Scxml.Parser
     // Remove: open Frank.Statecharts.Scxml.Types
     ```
  2. Key assertion changes:
     - **`Expect.isNone result.Document`** -> **`Expect.isEmpty (stateDecls result.Document)`** or check `result.Document.Elements` is empty.
       The `Ast.ParseResult.Document` is always present (even on error). On error, it is an empty `StatechartDocument` with no elements.
     - **`result.Errors.[0].Description`** -> Same field name, works as-is.
     - **`result.Errors.[0].Position`** -> Same field name, works as-is.
     - Error type is now `Ast.ParseFailure` (has `Expected`, `Found`, `CorrectiveExample` fields, all empty strings for SCXML errors).
  3. Warning assertions should work mostly as-is since `Ast.ParseWarning` has the same fields.
  4. For the test `"valid document has no errors and no warnings"`:
     - **`Expect.isSome result.Document`** -> Remove (always true) or replace with `Expect.isNonEmpty (stateDecls result.Document)`.
  5. For tests checking `result.Document` is `Some` on warning cases:
     - **`Expect.isSome result.Document "should still parse successfully"`** -> Replace with assertion that document has elements.
- **Files**: `test/Frank.Statecharts.Tests/Scxml/ErrorTests.fs`
- **Notes**: Add the `stateDecls` helper or use inline extraction.

### Subtask T026 -- Update `TypeTests.fs`

- **Purpose**: Remove tests for deleted types, keep tests for retained types, add tests for new `ScxmlMeta` cases.
- **Steps**:
  1. Change module opens:
     ```fsharp
     open Frank.Statecharts.Ast
     open Frank.Statecharts.Scxml.Types
     // Keep Scxml.Types for ScxmlTransitionType, ScxmlHistoryKind
     ```
  2. **Remove** test cases that reference deleted types:
     - `"ScxmlStateKind has all four cases"` -- `ScxmlStateKind` is deleted
     - `"DataEntry construction"` -- `Scxml.Types.DataEntry` is deleted
     - `"ScxmlTransition construction"` -- `ScxmlTransition` is deleted
     - `"ScxmlState construction with nested children"` -- `ScxmlState` is deleted
     - `"ScxmlDocument construction"` -- `ScxmlDocument` is deleted
     - `"ScxmlDocument structural equality"` -- deleted
     - `"ScxmlDocument inequality on different InitialId"` -- deleted
  3. **Keep** test cases for retained types:
     - `"SourcePosition construction"` -- keep (SourcePosition is retained in Scxml.Types)
     - `"ScxmlTransitionType has Internal and External"` -- keep
     - `"ScxmlHistoryKind has Shallow and Deep"` -- keep
  4. **Add** test cases for new `ScxmlMeta` cases:
     ```fsharp
     testCase "ScxmlMeta new cases construction"
     <| fun _ ->
         let cases =
             [ ScxmlTransitionType(true)
               ScxmlMultiTarget(["s1"; "s2"])
               ScxmlDatamodelType("ecmascript")
               ScxmlBinding("early")
               ScxmlInitial("child1") ]
         Expect.hasLength cases 5 "five new ScxmlMeta cases"

     testCase "ScxmlMeta extended cases construction"
     <| fun _ ->
         let inv = ScxmlInvoke("http", Some "https://example.com", Some "inv1")
         let hist = ScxmlHistory("h1", Deep, Some "child1")
         // Verify construction succeeds with new fields
         match inv with
         | ScxmlInvoke(t, s, id) ->
             Expect.equal t "http" "invoke type"
             Expect.equal s (Some "https://example.com") "invoke src"
             Expect.equal id (Some "inv1") "invoke id"
         | _ -> failtest "unexpected"
         match hist with
         | ScxmlHistory(id, kind, dt) ->
             Expect.equal id "h1" "history id"
             Expect.equal kind Deep "history kind"
             Expect.equal dt (Some "child1") "default target"
         | _ -> failtest "unexpected"
     ```
- **Files**: `test/Frank.Statecharts.Tests/Scxml/TypeTests.fs`
- **Notes**: The retained `SourcePosition` test uses `Scxml.Types.SourcePosition`, which may conflict with `Ast.SourcePosition`. Use explicit qualification if needed.

---

## Risks & Mitigations

- **Test count**: 40+ test cases across 5 files. Risk of missing an assertion. Mitigation: run `dotnet test` after updating each file, not just at the end.
- **State-scoped data round-trip**: The "roundtrip state-scoped datamodel" test may need adjustment since state-scoped data is now flattened to document level. The generated SCXML will have `<datamodel>` at the root level instead of inside the state. This changes the round-trip behavior. Mitigation: accept the flattened behavior (it matches the Mapper) or adjust the test expectations.
- **Helper function duplication**: Multiple test files need `stateDecls` and `transitionsFrom` helpers. Consider adding a shared test utility module, or duplicate in each file (simpler for this migration).

---

## Review Guidance

- Verify zero references to `ScxmlDocument`, `ScxmlState`, `ScxmlTransition`, `ScxmlParseResult`, `ScxmlStateKind` in test files.
- Verify `dotnet test` passes with zero failures.
- Verify round-trip tests cover at least 8 documents (spec SC-003 requires 8+).
- Count the round-trip test cases -- current count is 8 (US5-S1 through roundtrip traffic light). Verify all still exist.
- Verify error tests correctly handle the "Document is always present" change.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-16T19:26:17Z -- system -- lane=planned -- Prompt created.

---

### Updating Lane Status

To change a work package's lane, either:

1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP04 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
