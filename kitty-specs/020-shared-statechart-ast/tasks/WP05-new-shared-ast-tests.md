---
work_package_id: "WP05"
title: "New Shared AST Tests"
phase: "Phase 3 - Validation"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP01"]
requirement_refs:
  - "FR-001"
  - "FR-002"
  - "FR-003"
  - "FR-004"
  - "FR-005"
  - "FR-006"
  - "FR-007"
  - "FR-013"
  - "FR-022"
subtasks:
  - "T023"
  - "T024"
  - "T025"
  - "T026"
  - "T027"
  - "T028"
history:
  - timestamp: "2026-03-15T23:59:08Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP05 -- New Shared AST Tests

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

```bash
spec-kitty implement WP05 --base WP01
```

Note: WP05 depends only on WP01 (shared AST types), NOT on WP02-WP04 (parser migration). The tests in this WP construct ASTs programmatically. However, T026 (source position tests) also uses the parser, so full test execution requires WP04 to be complete.

---

## Objectives & Success Criteria

- Two new test files created: `Ast/TypeConstructionTests.fs` and `Ast/PartialPopulationTests.fs`
- Tests cover: programmatic AST construction (US1), partial population by all 5 formats (US3), annotation coexistence (US5), source position tracking (US4), structural equality (SC-006), and edge cases
- `dotnet test test/Frank.Statecharts.Tests/` passes with all new tests green
- Test project `.fsproj` updated with new compile entries

## Context & Constraints

- **Spec**: User Stories 1, 3, 4, 5 and Edge Cases section
- **Data Model**: `kitty-specs/020-shared-statechart-ast/data-model.md` -- Format Population Summary table
- **Success Criteria**: SC-003 (partial population), SC-005 (annotations), SC-006 (structural equality), SC-007 (source positions)
- **Test Framework**: Expecto (matching existing test patterns in `Frank.Statecharts.Tests`)
- **File Location**: `test/Frank.Statecharts.Tests/Ast/` (new directory)

## Subtasks & Detailed Guidance

### Subtask T023 -- TypeConstructionTests.fs

**Purpose**: Verify that `StatechartDocument` can be fully constructed programmatically. Build a tic-tac-toe game model (4 states, multiple transitions with guards, initial state marker). Verify structural equality by constructing two identical ASTs independently.

**Steps**:

1. Create directory `test/Frank.Statecharts.Tests/Ast/` if it does not exist
2. Create `test/Frank.Statecharts.Tests/Ast/TypeConstructionTests.fs`:

```fsharp
module Frank.Statecharts.Tests.Ast.TypeConstructionTests

open Expecto
open Frank.Statecharts.Ast
```

3. **Tic-tac-toe construction test** (US1 independent test):
   Build a `StatechartDocument` representing:
   - 4 states: "idle" (initial), "xTurn", "oTurn", "gameOver" (final)
   - Transitions: idle->xTurn (event "start"), xTurn->oTurn (event "move", guard "validMove"), oTurn->xTurn (event "move", guard "validMove"), xTurn->gameOver (event "win"), oTurn->gameOver (event "win")
   - Initial state: "idle"
   - Title: "Tic-Tac-Toe"

   ```fsharp
   let private buildTicTacToe () =
       let idle = { Identifier = "idle"; Label = None; Kind = Initial; Children = []; Activities = None; Position = None; Annotations = [] }
       let xTurn = { Identifier = "xTurn"; Label = Some "X's Turn"; Kind = Regular; Children = []; Activities = None; Position = None; Annotations = [] }
       let oTurn = { Identifier = "oTurn"; Label = Some "O's Turn"; Kind = Regular; Children = []; Activities = None; Position = None; Annotations = [] }
       let gameOver = { Identifier = "gameOver"; Label = None; Kind = Final; Children = []; Activities = None; Position = None; Annotations = [] }

       let transitions = [
           { Source = "idle"; Target = Some "xTurn"; Event = Some "start"; Guard = None; Action = None; Parameters = []; Position = None; Annotations = [] }
           { Source = "xTurn"; Target = Some "oTurn"; Event = Some "move"; Guard = Some "validMove"; Action = None; Parameters = []; Position = None; Annotations = [] }
           { Source = "oTurn"; Target = Some "xTurn"; Event = Some "move"; Guard = Some "validMove"; Action = None; Parameters = []; Position = None; Annotations = [] }
           { Source = "xTurn"; Target = Some "gameOver"; Event = Some "win"; Guard = None; Action = None; Parameters = []; Position = None; Annotations = [] }
           { Source = "oTurn"; Target = Some "gameOver"; Event = Some "win"; Guard = None; Action = None; Parameters = []; Position = None; Annotations = [] }
       ]

       { Title = Some "Tic-Tac-Toe"
         InitialStateId = Some "idle"
         Elements =
             [ StateDecl idle; StateDecl xTurn; StateDecl oTurn; StateDecl gameOver ]
             @ (transitions |> List.map TransitionElement)
         DataEntries = []
         Annotations = [] }
   ```

4. **Test cases**:
   - "tic-tac-toe document has correct title": `Expect.equal doc.Title (Some "Tic-Tac-Toe")`
   - "tic-tac-toe document has 4 states": count `StateDecl` elements = 4
   - "tic-tac-toe document has 5 transitions": count `TransitionElement` elements = 5
   - "tic-tac-toe document has initial state": `Expect.equal doc.InitialStateId (Some "idle")`
   - "guard on xTurn->oTurn transition": find transition with source "xTurn", target "oTurn", check `Guard = Some "validMove"`
   - "all fields can be populated": verify no field is accidentally `None` that should have a value

5. **Structural equality test** (SC-006):
   ```fsharp
   test "structural equality: identical ASTs are equal" {
       let doc1 = buildTicTacToe ()
       let doc2 = buildTicTacToe ()
       Expect.equal doc1 doc2 "identical ASTs must be equal"
   }

   test "structural equality: different ASTs are not equal" {
       let doc1 = buildTicTacToe ()
       let doc2 = { doc1 with Title = Some "Different" }
       Expect.notEqual doc1 doc2 "different titles means not equal"
   }
   ```

6. **Empty document test** (edge case):
   ```fsharp
   test "empty document is valid" {
       let doc = { Title = None; InitialStateId = None; Elements = []; DataEntries = []; Annotations = [] }
       Expect.isNone doc.Title "no title"
       Expect.isEmpty doc.Elements "no elements"
   }
   ```

**Files**: `test/Frank.Statecharts.Tests/Ast/TypeConstructionTests.fs` (new)
**Parallel?**: Yes

### Subtask T024 -- PartialPopulationTests.fs

**Purpose**: Verify that `StatechartDocument` can be constructed with any subset of fields populated, simulating each of the 5 format parsers.

**Steps**:

1. Create `test/Frank.Statecharts.Tests/Ast/PartialPopulationTests.fs`:

```fsharp
module Frank.Statecharts.Tests.Ast.PartialPopulationTests

open Expecto
open Frank.Statecharts.Ast
```

2. **WSD-like population** (US3-S4): states, transitions, transition style annotations, guards, groups. NO hierarchy, NO final state.
   ```fsharp
   test "WSD parser: states, transitions, groups -- no hierarchy, no final state" {
       let client = { Identifier = "Client"; Label = None; Kind = Regular; Children = []; Activities = None; Position = Some { Line = 1; Column = 1 }; Annotations = [] }
       let server = { Identifier = "Server"; Label = None; Kind = Regular; Children = []; Activities = None; Position = Some { Line = 2; Column = 1 }; Annotations = [] }
       let transition = { Source = "Client"; Target = Some "Server"; Event = Some "request"; Guard = None; Action = None; Parameters = []; Position = Some { Line = 3; Column = 1 }; Annotations = [ WsdAnnotation(WsdTransitionStyle { ArrowStyle = Solid; Direction = Forward }) ] }
       let doc = { Title = Some "WSD Example"; InitialStateId = None; Elements = [ StateDecl client; StateDecl server; TransitionElement transition ]; DataEntries = []; Annotations = [] }

       Expect.isEmpty doc.DataEntries "WSD has no data model"
       Expect.isEmpty client.Children "WSD has no hierarchy"
       Expect.isNone doc.InitialStateId "WSD has no explicit initial state"
       Expect.equal client.Kind Regular "WSD states are Regular"
   }
   ```

3. **smcat-like population** (US3-S1): states, transitions, guards, events, actions, hierarchy, initial/final. NO data model.
   ```fsharp
   test "smcat parser: states with hierarchy -- no data model" {
       let child1 = { Identifier = "sub1"; Label = None; Kind = Regular; Children = []; Activities = None; Position = None; Annotations = [] }
       let child2 = { Identifier = "sub2"; Label = None; Kind = Regular; Children = []; Activities = None; Position = None; Annotations = [] }
       let parent = { Identifier = "parent"; Label = None; Kind = Regular; Children = [ child1; child2 ]; Activities = None; Position = None; Annotations = [] }
       let initial = { Identifier = "start"; Label = None; Kind = Initial; Children = []; Activities = None; Position = None; Annotations = [] }
       let final = { Identifier = "end"; Label = None; Kind = Final; Children = []; Activities = None; Position = None; Annotations = [] }
       let doc = { Title = None; InitialStateId = Some "start"; Elements = [ StateDecl initial; StateDecl parent; StateDecl final ]; DataEntries = []; Annotations = [] }

       Expect.isEmpty doc.DataEntries "smcat has no data model"
       Expect.hasLength parent.Children 2 "hierarchy preserved"
       Expect.equal initial.Kind Initial "initial state"
       Expect.equal final.Kind Final "final state"
   }
   ```

4. **ALPS-like population** (US3-S2): states (descriptors), transitions, transition type annotations. NO initial state, NO ordering.
   ```fsharp
   test "ALPS parser: states and transitions with transition type -- no initial state" {
       let desc1 = { Identifier = "user"; Label = Some "User Descriptor"; Kind = Regular; Children = []; Activities = None; Position = None; Annotations = [] }
       let transition = { Source = "user"; Target = Some "profile"; Event = None; Guard = None; Action = None; Parameters = []; Position = None; Annotations = [ AlpsAnnotation(AlpsTransitionType Idempotent) ] }
       let doc = { Title = None; InitialStateId = None; Elements = [ StateDecl desc1; TransitionElement transition ]; DataEntries = []; Annotations = [] }

       Expect.isNone doc.InitialStateId "ALPS has no initial state concept"
       Expect.isNone doc.Title "ALPS may have no title"
   }
   ```

5. **SCXML-like population** (US3-S3): FULL population -- all fields.
   ```fsharp
   test "SCXML parser: full population (most expressive format)" {
       let state = { Identifier = "s1"; Label = None; Kind = Regular; Children = []; Activities = Some { Entry = ["action1"]; Exit = ["action2"]; Do = [] }; Position = Some { Line = 5; Column = 3 }; Annotations = [ ScxmlAnnotation(ScxmlNamespace "http://www.w3.org/2005/07/scxml") ] }
       let finalState = { Identifier = "done"; Label = None; Kind = Final; Children = []; Activities = None; Position = None; Annotations = [] }
       let data = { Name = "counter"; Expression = Some "0"; Position = None }
       let transition = { Source = "s1"; Target = Some "done"; Event = Some "finish"; Guard = Some "counter > 3"; Action = Some "logDone"; Parameters = []; Position = None; Annotations = [] }
       let doc = { Title = Some "SCXML Machine"; InitialStateId = Some "s1"; Elements = [ StateDecl state; StateDecl finalState; TransitionElement transition ]; DataEntries = [ data ]; Annotations = [] }

       Expect.isSome doc.InitialStateId "SCXML has initial state"
       Expect.isNonEmpty doc.DataEntries "SCXML has data model"
       Expect.isSome state.Activities "SCXML has state activities"
       Expect.isSome doc.Title "SCXML has title"
   }
   ```

6. **XState-like population**: states, transitions, guards, actions, context, hierarchy, initial, final, parallel. NO grouping blocks.
   ```fsharp
   test "XState parser: states with context and actions -- no grouping blocks" {
       let state = { Identifier = "active"; Label = None; Kind = Regular; Children = []; Activities = Some { Entry = ["startTimer"]; Exit = ["stopTimer"]; Do = [] }; Position = None; Annotations = [ XStateAnnotation(XStateAction "logEntry") ] }
       let data = { Name = "retries"; Expression = Some "0"; Position = None }
       let doc = { Title = None; InitialStateId = Some "active"; Elements = [ StateDecl state ]; DataEntries = [ data ]; Annotations = [] }

       let hasGrouping = doc.Elements |> List.exists (function GroupElement _ -> true | _ -> false)
       Expect.isFalse hasGrouping "XState has no grouping blocks"
       Expect.isNonEmpty doc.DataEntries "XState has context data"
   }
   ```

**Files**: `test/Frank.Statecharts.Tests/Ast/PartialPopulationTests.fs` (new)
**Parallel?**: Yes

### Subtask T025 -- Annotation coexistence tests

**Purpose**: Verify that multiple annotation types can coexist on the same AST node (US5 scenarios).

**Steps**:

1. Add tests to `TypeConstructionTests.fs` (or a separate section):

   ```fsharp
   test "WSD and SCXML annotations coexist on transition" {
       let edge = { Source = "s1"; Target = Some "s2"; Event = Some "go"; Guard = None; Action = None; Parameters = []; Position = None; Annotations = [ WsdAnnotation(WsdTransitionStyle { ArrowStyle = Dashed; Direction = Forward }); ScxmlAnnotation(ScxmlNamespace "http://example.com") ] }

       let wsdAnns = edge.Annotations |> List.choose (function WsdAnnotation w -> Some w | _ -> None)
       let scxmlAnns = edge.Annotations |> List.choose (function ScxmlAnnotation s -> Some s | _ -> None)
       Expect.hasLength wsdAnns 1 "one WSD annotation"
       Expect.hasLength scxmlAnns 1 "one SCXML annotation"
   }

   test "WSD and SCXML annotations coexist on state" {
       let state = { Identifier = "s1"; Label = None; Kind = Regular; Children = []; Activities = None; Position = None; Annotations = [ WsdAnnotation(WsdNotePosition Over); ScxmlAnnotation(ScxmlHistory("h1", Deep)) ] }

       let wsdAnns = state.Annotations |> List.choose (function WsdAnnotation w -> Some w | _ -> None)
       let scxmlAnns = state.Annotations |> List.choose (function ScxmlAnnotation s -> Some s | _ -> None)
       Expect.hasLength wsdAnns 1 "one WSD annotation"
       Expect.hasLength scxmlAnns 1 "one SCXML annotation"
   }

   test "annotations from all 5 formats on same node" {
       let annotations = [
           WsdAnnotation(WsdTransitionStyle { ArrowStyle = Solid; Direction = Forward })
           AlpsAnnotation(AlpsTransitionType Safe)
           ScxmlAnnotation(ScxmlNamespace "ns")
           SmcatAnnotation(SmcatColor "red")
           XStateAnnotation(XStateAction "log")
       ]
       let edge = { Source = "s1"; Target = Some "s2"; Event = None; Guard = None; Action = None; Parameters = []; Position = None; Annotations = annotations }
       Expect.hasLength edge.Annotations 5 "all 5 format annotations present"
   }
   ```

**Files**: `test/Frank.Statecharts.Tests/Ast/TypeConstructionTests.fs`
**Parallel?**: Yes

### Subtask T026 -- Source position tests

**Purpose**: Verify source position tracking behavior (US4 scenarios).

**Steps**:

1. Add tests to `TypeConstructionTests.fs`:

   ```fsharp
   test "programmatic construction has None position" {
       let state = { Identifier = "s1"; Label = None; Kind = Regular; Children = []; Activities = None; Position = None; Annotations = [] }
       Expect.isNone state.Position "programmatic = no position"
   }

   test "parser output has Some position" {
       let state = { Identifier = "Client"; Label = None; Kind = Regular; Children = []; Activities = None; Position = Some { Line = 3; Column = 1 }; Annotations = [] }
       Expect.isSome state.Position "parser output = has position"
       Expect.equal state.Position.Value.Line 3 "line 3"
       Expect.equal state.Position.Value.Column 1 "column 1"
   }

   test "SourcePosition is a struct" {
       let pos : SourcePosition = { Line = 1; Column = 1 }
       // Struct types are value types -- verify by checking it's not null-able in the usual F# way
       Expect.equal pos.Line 1 "line"
       Expect.equal pos.Column 1 "column"
   }
   ```

2. **Parser integration test** (requires WP04 to be complete for full run):
   ```fsharp
   test "WSD parser output carries source positions" {
       let result = Frank.Statecharts.Wsd.Parser.parseWsd "participant Client\nClient->Client: self\n"
       let states = result.Document.Elements |> List.choose (function StateDecl s -> Some s | _ -> None)
       Expect.isTrue (states |> List.forall (fun s -> s.Position.IsSome)) "all states have positions"
       let transitions = result.Document.Elements |> List.choose (function TransitionElement t -> Some t | _ -> None)
       Expect.isTrue (transitions |> List.forall (fun t -> t.Position.IsSome)) "all transitions have positions"
   }
   ```

**Files**: `test/Frank.Statecharts.Tests/Ast/TypeConstructionTests.fs`
**Parallel?**: Yes

### Subtask T027 -- Edge case tests

**Purpose**: Verify all edge cases listed in the spec.

**Steps**:

1. Add edge case tests to `TypeConstructionTests.fs`:

   ```fsharp
   testList "Edge Cases" [
       test "state with no transitions is valid" {
           let state = { Identifier = "sink"; Label = None; Kind = Regular; Children = []; Activities = None; Position = None; Annotations = [] }
           let doc = { Title = None; InitialStateId = None; Elements = [ StateDecl state ]; DataEntries = []; Annotations = [] }
           Expect.hasLength doc.Elements 1 "one element"
       }

       test "transition with no event is valid (completion transition)" {
           let edge = { Source = "s1"; Target = Some "s2"; Event = None; Guard = None; Action = None; Parameters = []; Position = None; Annotations = [] }
           Expect.isNone edge.Event "no event = completion transition"
       }

       test "transition with no target is valid (internal transition)" {
           let edge = { Source = "s1"; Target = None; Event = Some "tick"; Guard = None; Action = None; Parameters = []; Position = None; Annotations = [] }
           Expect.isNone edge.Target "no target = internal transition"
       }

       test "self-transition is valid" {
           let edge = { Source = "s1"; Target = Some "s1"; Event = Some "retry"; Guard = None; Action = None; Parameters = []; Position = None; Annotations = [] }
           Expect.equal edge.Source "s1" "source"
           Expect.equal edge.Target (Some "s1") "target = source"
       }

       test "multiple transitions between same states with different events" {
           let e1 = { Source = "s1"; Target = Some "s2"; Event = Some "eventA"; Guard = None; Action = None; Parameters = []; Position = None; Annotations = [] }
           let e2 = { Source = "s1"; Target = Some "s2"; Event = Some "eventB"; Guard = None; Action = None; Parameters = []; Position = None; Annotations = [] }
           let doc = { Title = None; InitialStateId = None; Elements = [ TransitionElement e1; TransitionElement e2 ]; DataEntries = []; Annotations = [] }
           let transitions = doc.Elements |> List.choose (function TransitionElement t -> Some t | _ -> None)
           Expect.hasLength transitions 2 "two transitions"
       }

       test "deeply nested hierarchy (5+ levels)" {
           let leaf = { Identifier = "leaf"; Label = None; Kind = Regular; Children = []; Activities = None; Position = None; Annotations = [] }
           let l4 = { Identifier = "l4"; Label = None; Kind = Regular; Children = [ leaf ]; Activities = None; Position = None; Annotations = [] }
           let l3 = { Identifier = "l3"; Label = None; Kind = Regular; Children = [ l4 ]; Activities = None; Position = None; Annotations = [] }
           let l2 = { Identifier = "l2"; Label = None; Kind = Regular; Children = [ l3 ]; Activities = None; Position = None; Annotations = [] }
           let l1 = { Identifier = "l1"; Label = None; Kind = Regular; Children = [ l2 ]; Activities = None; Position = None; Annotations = [] }
           Expect.hasLength l1.Children 1 "l1 has child"
           Expect.hasLength l1.Children.[0].Children 1 "l2 has child"
           Expect.hasLength l1.Children.[0].Children.[0].Children 1 "l3 has child"
           Expect.hasLength l1.Children.[0].Children.[0].Children.[0].Children 1 "l4 has child"
           Expect.isEmpty l1.Children.[0].Children.[0].Children.[0].Children.[0].Children "leaf has no children"
       }

       test "data entry with empty expression is valid" {
           let data = { Name = "x"; Expression = None; Position = None }
           Expect.isNone data.Expression "empty expression"
       }

       test "empty annotations list is valid" {
           let state = { Identifier = "s1"; Label = None; Kind = Regular; Children = []; Activities = None; Position = None; Annotations = [] }
           Expect.isEmpty state.Annotations "empty annotations"
       }

       test "unicode characters in identifiers and events" {
           let state = { Identifier = "Utilisateur"; Label = Some "Benutzer"; Kind = Regular; Children = []; Activities = None; Position = None; Annotations = [] }
           let edge = { Source = "Utilisateur"; Target = Some "Serveur"; Event = Some "requete"; Guard = Some "estPret"; Action = None; Parameters = []; Position = None; Annotations = [] }
           Expect.equal state.Identifier "Utilisateur" "unicode identifier"
           Expect.equal edge.Event (Some "requete") "unicode event"
       }
   ]
   ```

**Files**: `test/Frank.Statecharts.Tests/Ast/TypeConstructionTests.fs`
**Parallel?**: Yes

### Subtask T028 -- Update test .fsproj for new test files

**Purpose**: Add new test files to the test project compile order.

**Steps**:

1. Open `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
2. Add new compile entries BEFORE the existing `Wsd/` entries (Ast types should compile first):
   ```xml
   <Compile Include="Ast/TypeConstructionTests.fs" />
   <Compile Include="Ast/PartialPopulationTests.fs" />
   <Compile Include="Wsd/GuardParserTests.fs" />
   <!-- ... rest unchanged ... -->
   ```
3. Run `dotnet test test/Frank.Statecharts.Tests/` to verify all tests pass

**Files**: `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
**Parallel?**: Yes (can be done alongside other subtasks)

## Risks & Mitigations

- **Parser dependency for T026**: One test in T026 uses the parser to verify source positions. This test will fail if WP03/WP04 are not complete. Mitigation: Wrap parser-dependent tests in a conditional or mark them for later verification.
- **Test file organization**: Adding files to `Ast/` directory mirrors the source structure. The `.fsproj` compile order matters in F#. Mitigation: Place Ast test files before Wsd test files in the compile order.

## Review Guidance

- Verify all spec edge cases are covered in T027
- Verify tic-tac-toe model has 4 states and 5 transitions as specified in US1
- Verify partial population tests cover all 5 formats per the Format Population Summary table
- Verify structural equality tests use independently constructed ASTs (not `let doc2 = doc1`)
- Verify annotation coexistence tests use multiple format annotation types on the same node
- Run `dotnet test` and verify all new tests pass

## Activity Log

- 2026-03-15T23:59:08Z -- system -- lane=planned -- Prompt created.
