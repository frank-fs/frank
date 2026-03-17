---
work_package_id: "WP02"
title: "Parser Migration to Shared AST"
phase: "Phase 1 - Foundation"
lane: "done"
assignee: ""
agent: ""
shell_pid: ""
review_status: "approved"
reviewed_by: "claude-opus"
dependencies: ["WP01"]
requirement_refs: ["FR-001", "FR-002", "FR-003", "FR-004", "FR-005", "FR-006", "FR-007", "FR-008", "FR-009", "FR-010"]
subtasks:
  - "T004"
  - "T005"
  - "T006"
  - "T007"
  - "T008"
  - "T009"
  - "T010"
history:
  - timestamp: "2026-03-16T19:13:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 -- Parser Migration to Shared AST

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Objectives & Success Criteria

- `Parser.fs` produces `Ast.ParseResult` (containing `StatechartDocument`) directly
- No references to deleted smcat-specific types (`SmcatDocument`, `SmcatState`, `SmcatTransition`, `SmcatElement`, `ParseResult`, `ParseFailure`, `ParseWarning`)
- State declarations construct `Ast.StateNode` with `SmcatAnnotation` for color/label attributes
- Transitions construct `Ast.TransitionEdge` with label components split into `Event`/`Guard`/`Action`
- `parseSmcat` and `parse` return `Ast.ParseResult`
- Composite states use `StateNode.Children: StateNode list`

## Context & Constraints

- **Spec**: FR-001 through FR-010 (parser requirements)
- **Plan**: DD-001 (parser produces `Ast.ParseResult` directly)
- **Reference**: `src/Frank.Statecharts/Smcat/Mapper.fs` -- contains the exact mapping logic to inline
- **Shared AST**: `src/Frank.Statecharts/Ast/Types.fs`
- **Prerequisite**: WP01 must be complete (Types.fs cleaned, LabelParser updated)
- **Key insight**: The Mapper.fs functions `toAstElements`, `toChildStateNodes`, `toAnnotation`, `toStateKind`, `toStateActivities`, `toAstPosition`, `toAstFailure`, `toAstWarning` contain the exact logic that needs to be inlined into the parser

## Implementation Command

```bash
spec-kitty implement WP02 --base WP01
```

## Subtasks & Detailed Guidance

### Subtask T004 -- Update ParserState to use Ast types

**Purpose**: The parser's internal state must hold shared AST types instead of smcat-specific types.

**Steps**:

1. Open `src/Frank.Statecharts/Smcat/Parser.fs`
2. Replace `open Frank.Statecharts.Smcat.Types` imports section. Add:
   ```fsharp
   open Frank.Statecharts.Ast
   open Frank.Statecharts.Smcat.Types
   open Frank.Statecharts.Smcat.LabelParser
   open Frank.Statecharts.Smcat.Lexer
   ```
   Note: `Ast` must be opened BEFORE `Types` so that when both have `SourcePosition`, the smcat-local one is gone (deleted in WP01) and Ast's wins. Actually, since local `SourcePosition` is deleted, there is no conflict.

3. Update `ParserState` record:
   ```fsharp
   type ParserState =
       { Tokens: Token array
         mutable Position: int
         mutable Elements: StatechartElement list      // was SmcatElement list
         mutable Errors: ParseFailure list             // was smcat ParseFailure list (now Ast)
         mutable Warnings: ParseWarning list           // was smcat ParseWarning list (now Ast)
         mutable ErrorLimitReached: bool
         mutable DeclaredStates: Set<string>
         MaxErrors: int }
   ```

4. Update `eofToken` to use `Ast.SourcePosition`:
   ```fsharp
   let private eofToken =
       { Kind = Eof
         Position = { Line = 0; Column = 0 } }
   ```
   This should work unchanged since `SourcePosition` is now from Ast (both are the same struct shape).

**Files**: `src/Frank.Statecharts/Smcat/Parser.fs`

**Validation**:
- [ ] `ParserState.Elements` is `StatechartElement list`
- [ ] `ParserState.Errors` is `ParseFailure list` (Ast)
- [ ] `ParserState.Warnings` is `ParseWarning list` (Ast)

### Subtask T005 -- Update parseStateDeclaration to construct Ast.StateNode

**Purpose**: Instead of constructing `SmcatState` and wrapping in `StateDeclaration`, construct `StateNode` and wrap in `StateDecl`.

**Steps**:

1. In `parseStateDeclaration`, the current code builds:
   ```fsharp
   let smcatState =
       { Name = name; Label = label; StateType = stateType
         Activities = activities; Attributes = attributes
         Children = children; Position = startPos }
   elements.Add(StateDeclaration smcatState)
   ```

2. Replace with `StateNode` construction. Key mappings:
   - `Name` -> `Identifier`
   - `Label` -> `Label` (unchanged)
   - `StateType` -> `Kind` (already returns `StateKind` after WP01)
   - `Activities: StateActivity option` -> `Activities: StateActivities option` (conversion needed)
   - `Attributes: SmcatAttribute list` -> `Annotations: Annotation list` (conversion needed)
   - `Children: SmcatDocument option` -> `Children: StateNode list` (extraction needed)
   - `Position: SourcePosition` -> `Position: SourcePosition option` (wrap in `Some`)

3. **Activity conversion** (inline from Mapper.toStateActivities):
   ```fsharp
   let astActivities =
       activities
       |> Option.map (fun a ->
           { Entry = a.Entry |> Option.map List.singleton |> Option.defaultValue []
             Exit = a.Exit |> Option.map List.singleton |> Option.defaultValue []
             Do = a.Do |> Option.map List.singleton |> Option.defaultValue [] })
   ```
   Note: `parseActivities` still returns the local record `{ Entry: string option; Exit: string option; Do: string option }` since `StateActivity` is deleted but the parser builds it inline. You need to either:
   - Keep using a local anonymous record or tuple for the intermediate activity parsing result, OR
   - Change `parseActivities` to return `StateActivities` directly (preferred -- build `StateActivities` inline in the activity parser)

   **Preferred approach**: Update `parseActivities` to return `StateActivities`:
   ```fsharp
   let private parseActivities (state: ParserState) : StateActivities =
       // ... same collection logic ...
       { Entry = entry |> Option.map List.singleton |> Option.defaultValue []
         Exit = exit |> Option.map List.singleton |> Option.defaultValue []
         Do = doActivity |> Option.map List.singleton |> Option.defaultValue [] }
   ```

4. **Attribute-to-annotation conversion** (inline from Mapper.toAnnotation):
   ```fsharp
   let annotations =
       attributes
       |> List.map (fun attr ->
           match attr.Key.ToLowerInvariant() with
           | "color" -> SmcatAnnotation(SmcatColor attr.Value)
           | "label" -> SmcatAnnotation(SmcatStateLabel attr.Value)
           | kind -> SmcatAnnotation(SmcatActivity(kind, attr.Value)))
   ```
   Note: Filter out `type` attribute from annotations (it is consumed by `inferStateType`, not stored as annotation).

5. **Children extraction**: See T010 for details on composite state handling.

6. Construct the `StateNode`:
   ```fsharp
   let stateNode : StateNode =
       { Identifier = name
         Label = label
         Kind = stateType
         Children = children   // StateNode list (see T010)
         Activities = astActivities
         Position = Some startPos
         Annotations = annotations }
   elements.Add(StateDecl stateNode)
   ```

**Files**: `src/Frank.Statecharts/Smcat/Parser.fs`

**Edge Cases**:
- Attributes with key `type` should NOT become annotations (they are consumed by `inferStateType`)
- Label is extracted from attributes AND set on the node -- the `label` attribute should still become an annotation too for round-trip fidelity

### Subtask T006 -- Update parseTransition to construct Ast.TransitionEdge

**Purpose**: Instead of constructing `SmcatTransition` and wrapping in `TransitionElement`, construct `TransitionEdge` and wrap in `Ast.TransitionElement`.

**Steps**:

1. In `parseTransition`, the current code builds:
   ```fsharp
   let transition =
       { Source = sourceName; Target = targetName
         Label = label; Attributes = attributes
         Position = startPos }
   elements.Add(TransitionElement transition)
   ```

2. Replace with `TransitionEdge` construction. The label components are split out:
   ```fsharp
   let (ev, gd, ac) =
       match label with
       | Some l -> (l.Event, l.Guard, l.Action)
       | None -> (None, None, None)

   let annotations =
       attributes
       |> List.map (fun attr ->
           match attr.Key.ToLowerInvariant() with
           | "color" -> SmcatAnnotation(SmcatColor attr.Value)
           | "label" -> SmcatAnnotation(SmcatStateLabel attr.Value)
           | kind -> SmcatAnnotation(SmcatActivity(kind, attr.Value)))

   let edge : TransitionEdge =
       { Source = sourceName
         Target = Some targetName     // Note: wrapped in Some
         Event = ev
         Guard = gd
         Action = ac
         Parameters = []
         Position = Some startPos     // Note: wrapped in Some
         Annotations = annotations }

   elements.Add(TransitionElement edge)
   ```

3. There are multiple places where transitions are constructed in `parseTransition`:
   - The normal case (with full label parsing)
   - The "missing colon" error recovery case (line ~509-516)
   - The `tryHandleInvalidArrow` function (line ~688-693)

   All three must be updated to construct `TransitionEdge`.

4. For the error-recovery transition constructions (missing colon, invalid arrow), use the same pattern but with `Event = None`, `Guard = None`, `Action = None`.

**Files**: `src/Frank.Statecharts/Smcat/Parser.fs`

**Validation**:
- [ ] All `TransitionElement` constructions use `TransitionEdge` (not `SmcatTransition`)
- [ ] `Target` is wrapped in `Some`
- [ ] `Position` is wrapped in `Some`
- [ ] `Parameters` is `[]`

### Subtask T007 -- Update parseDocument return type

**Purpose**: `parseDocument` currently returns `SmcatDocument`. It must return `StatechartDocument`.

**Steps**:

1. Change the return type of `parseDocument`:
   ```fsharp
   let rec private parseDocument (state: ParserState) (depth: int) : StatechartDocument =
   ```

2. Update the return value at the end of `parseDocument`:
   ```fsharp
   { Title = None
     InitialStateId = None
     Elements = elements |> Seq.toList
     DataEntries = []
     Annotations = [] }
   ```
   (Was: `{ Elements = elements |> Seq.toList }`)

3. The `elements` ResizeArray already holds `StatechartElement` values (updated in T004/T005/T006), so this is straightforward.

**Files**: `src/Frank.Statecharts/Smcat/Parser.fs`

### Subtask T008 -- Update addError/addWarning for Ast types

**Purpose**: Error and warning construction must use `Ast.ParseFailure` and `Ast.ParseWarning`, which have `Position: SourcePosition option` (not plain `SourcePosition`).

**Steps**:

1. Update `addError`:
   ```fsharp
   let private addError
       (state: ParserState)
       (pos: SourcePosition)
       (desc: string)
       (expected: string)
       (found: string)
       (example: string)
       : unit =
       if not state.ErrorLimitReached then
           let failure : ParseFailure =
               { Position = Some pos          // Wrapped in Some
                 Description = desc
                 Expected = expected
                 Found = found
                 CorrectiveExample = example }
           state.Errors <- state.Errors @ [ failure ]
           if state.Errors.Length >= state.MaxErrors then
               state.ErrorLimitReached <- true
   ```

2. Update `addWarning`:
   ```fsharp
   let private addWarning (state: ParserState) (pos: SourcePosition) (desc: string) (suggestion: string option) : unit =
       let warning : ParseWarning =
           { Position = Some pos              // Wrapped in Some
             Description = desc
             Suggestion = suggestion }
       state.Warnings <- state.Warnings @ [ warning ]
   ```

**Files**: `src/Frank.Statecharts/Smcat/Parser.fs`

### Subtask T009 -- Update public API (parse, parseSmcat)

**Purpose**: The public API functions must return `Ast.ParseResult`.

**Steps**:

1. Update `parse`:
   ```fsharp
   let parse (tokens: Token list) (maxErrors: int) : ParseResult =
       let state = createState tokens maxErrors
       let doc = parseDocument state 0
       { Document = doc
         Errors = state.Errors
         Warnings = state.Warnings }
   ```
   Note: `ParseResult` is now `Ast.ParseResult` (which has `Document: StatechartDocument`). The `doc` from `parseDocument` is already `StatechartDocument` (updated in T007). So this should work with minimal changes.

2. Update `parseSmcat`:
   ```fsharp
   let parseSmcat (source: string) : ParseResult =
       let tokens = tokenize source
       parse tokens 50
   ```
   Same return type change; the body is unchanged.

**Files**: `src/Frank.Statecharts/Smcat/Parser.fs`

### Subtask T010 -- Update composite state Children handling

**Purpose**: `StateNode.Children` is `StateNode list`, not `SmcatDocument option`. The parser must extract child states from the parsed composite block and store them as `StateNode list`. Transitions within the composite block should also be included in the parent's elements.

**Steps**:

1. In `parseStateDeclaration`, the current code for composite children:
   ```fsharp
   let children =
       match (peek state).Kind with
       | LeftBrace ->
           advance state |> ignore
           let childDoc = parseDocument state (depth + 1)
           // Consume closing brace...
           Some childDoc
       | None -> None
   ```

2. After WP02 changes, `parseDocument` returns `StatechartDocument`. Extract child `StateNode` values from its `Elements`:
   ```fsharp
   let (childStateNodes, childOtherElements) =
       match (peek state).Kind with
       | LeftBrace ->
           advance state |> ignore
           let childDoc = parseDocument state (depth + 1)
           // Consume closing brace...
           let childStates =
               childDoc.Elements
               |> List.choose (fun el ->
                   match el with
                   | StateDecl node -> Some node
                   | _ -> None)
           let otherElements =
               childDoc.Elements
               |> List.filter (fun el ->
                   match el with
                   | StateDecl _ -> false
                   | _ -> true)
           (childStates, otherElements)
       | _ -> ([], [])
   ```

3. Store `childStateNodes` as `Children` on the `StateNode`. The `childOtherElements` (transitions within the composite) should be added to the parent's `elements` list after the state declaration, preserving them in the document.

   **Important design decision**: Looking at how the WSD model and Mapper.fs handle this -- in `Mapper.toChildStateNodes`, only `StateDeclaration` elements become children; transitions are dropped. But looking at the spec more carefully, the `StatechartDocument` model preserves all elements including transitions at each level. For smcat composite states, the transitions within `{ }` are logically part of that scope.

   **The simplest correct approach**: Keep child transitions as part of the composite state's scope. Since `StateNode` only has `Children: StateNode list` (no element list), we need to decide where transitions go.

   **Recommended approach** (matching current Mapper.fs behavior): Only state declarations become `Children`. Transitions within composites are **not** promoted to the parent level -- they are dropped from the `StateNode` representation. This matches `Mapper.toChildStateNodes` which only extracts `StateDeclaration` elements.

   However, for **round-trip fidelity** the serializer will need to track these transitions. The approach: Store nested transitions as part of the parent document's elements or accept that composite-internal transitions are only tracked via the children's state names (the serializer can reconstruct transitions from the parent document's element list that reference child state names).

   **Pragmatic approach**: For now, follow the Mapper.fs pattern exactly. Child state nodes go into `Children`. Transitions within composites remain in the `StatechartDocument.Elements` list at the correct nesting level. Since `parseDocument` returns a `StatechartDocument` with all elements, and the parent function only extracts `StateDecl` nodes for `Children`, the transitions are accessible from the `StatechartDocument` returned by `parseDocument`.

   **Wait** -- re-reading the structure: `parseDocument` at a nested level returns a `StatechartDocument`. The parent `parseStateDeclaration` takes the child doc's `StateDecl` elements as `Children`. But what about the child doc's `TransitionElement` entries? They need to be stored somewhere for the serializer to reconstruct them.

   **Solution**: Store the full child `StatechartDocument` in the `Annotations` of the `StateNode` as a custom approach, OR (better) just add all child elements (both states and transitions) to the parent's elements list right after the state decl. This way the serializer can find them.

   **Actually, the cleanest solution**: Don't separate states from transitions. Since we're building a `StatechartDocument` anyway, and the serializer will receive a `StatechartDocument`, we can store composite internal transitions by making the `StateNode`'s `Children` ONLY contain `StateNode` values, and separately emit the composite's `TransitionElement` entries into the parent-level elements list. The serializer then knows: if a `StateNode` has children AND there are transitions in the same document whose source/target match child state names, those transitions belong inside the `{ }` block.

   **Simplest correct solution (matching Mapper.fs exactly)**:
   - `Children` = state nodes only
   - Transitions within composites are added to the current element list
   - The serializer will need to group elements by composite scope

   ```fsharp
   let childStateNodes =
       childDoc.Elements
       |> List.choose (fun el ->
           match el with
           | StateDecl node -> Some node
           | _ -> None)

   // Add non-state elements (transitions) from the child doc to the current elements list
   for el in childDoc.Elements do
       match el with
       | StateDecl _ -> ()  // handled via Children
       | other -> elements.Add(other)
   ```

   **Note**: This changes how composite transitions are represented vs. the current model. In the current model, transitions inside `{ }` are part of the `SmcatDocument.Elements` of the child. In the new model, they'll be siblings in the parent `StatechartDocument.Elements`. This affects the serializer design (WP03) but is consistent with how the shared AST works.

   **ACTUALLY -- REVISED APPROACH**: After re-reading more carefully, the simplest approach that preserves the structure is: DON'T hoist child transitions to the parent. Instead, just extract child state nodes for `Children` and let the child transitions be lost. The serializer (WP03) will work from the `StateNode.Children` to know which states are nested, and from the parent-level transitions to know the edges. For composite states, the parser should add BOTH the `StateDecl` (with children) AND the transitions to the parent elements list. This way the transitions remain at the correct level.

   **FINAL APPROACH (keeping it simple)**:
   - `parseDocument` returns a `StatechartDocument` with all elements (states + transitions)
   - In `parseStateDeclaration`, extract child state nodes for `Children`
   - Add child transitions to the parent `elements` list (they are logically at composite scope)
   - The `StateNode` gets `Children = childStateNodes`
   - This matches how tests currently verify composite state content

**Files**: `src/Frank.Statecharts/Smcat/Parser.fs`

**Edge Cases**:
- Empty composite state `parent { }` produces `Children = []`
- Nested composites: `parseDocument` is recursive, so nesting works automatically
- Composite with only transitions and no state declarations: `Children = []` with transitions in parent

## Risks & Mitigations

- **Risk**: Composite state handling is the most complex transformation. **Mitigation**: Follow the Mapper.fs pattern exactly for child state extraction. Test with the composite state golden files.
- **Risk**: Missing an internal `SmcatElement` pattern match somewhere. **Mitigation**: The F# compiler will catch any remaining references to deleted types.
- **Risk**: `parseActivities` intermediate representation. **Mitigation**: Change it to return `StateActivities` directly rather than introducing a temporary type.

## Review Guidance

- Verify that `parseSmcat` returns `Ast.ParseResult` (check type signature)
- Verify `SmcatAnnotation` construction for color/label attributes
- Verify `StateActivities` conversion (single option -> list)
- Verify composite state `Children` handling matches the documented approach
- Verify all `Position` fields are wrapped in `Some`
- Check that `TransitionEdge.Target` is always `Some targetName`
- Verify `Parameters = []` on all constructed `TransitionEdge` values

## Activity Log

- 2026-03-16T19:13:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T21:00:00Z -- claude-opus -- lane=done -- Review approved. All 7 subtasks (T004-T010) pass. Parser produces Ast.ParseResult with StatechartDocument directly. No references to deleted smcat-specific types. Commit ddc5d67.
