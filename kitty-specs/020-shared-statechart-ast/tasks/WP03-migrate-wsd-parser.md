---
work_package_id: "WP03"
title: "Migrate WSD Parser & GuardParser"
phase: "Phase 2 - Parser Migration"
lane: "done"
assignee: ""
agent: ""
shell_pid: ""
review_status: "approved"
reviewed_by: "Ryan Riley"
dependencies: ["WP02"]
requirement_refs:
  - "FR-008"
  - "FR-009"
  - "FR-010"
  - "FR-011"
  - "FR-012"
subtasks:
  - "T011"
  - "T012"
  - "T013"
  - "T014"
  - "T015"
  - "T016"
history:
  - timestamp: "2026-03-15T23:59:08Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP03 -- Migrate WSD Parser & GuardParser

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

```bash
spec-kitty implement WP03 --base WP02
```

---

## Objectives & Success Criteria

- `GuardParser.fs` uses shared `ParseFailure`/`ParseWarning` from `Frank.Statecharts.Ast` and defines `GuardAnnotation` locally as a WSD parse helper
- `Parser.fs` produces `ParseResult` containing `StatechartDocument` (shared AST) instead of `Diagram` (WSD-specific)
- All parser behaviors preserved: error recovery, implicit participant warnings, guard parsing, group nesting, error limits
- `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` succeeds
- The `parseWsd` function signature returns the shared `ParseResult` type

## Context & Constraints

- **Plan**: `kitty-specs/020-shared-statechart-ast/plan.md` -- Type Migration Map (critical reference)
- **Data Model**: `kitty-specs/020-shared-statechart-ast/data-model.md` -- exact field definitions
- **Research**: D-002 (ArrowStyle/Direction become WSD annotations), D-007 (StatechartElement preserves order)
- **Planning decisions**: PD-002 (`Participant.Explicit` stays in parser state), PD-003 (ParserState.Participants preserved as internal tracking), PD-004 (`GuardAnnotation` stays as WSD parse helper)
- **Current Parser.fs**: ~800 lines, mutable `ParserState`, recursive descent with error recovery
- **Current GuardParser.fs**: ~160 lines, standalone guard parsing function

## Subtasks & Detailed Guidance

### Subtask T011 -- Migrate GuardParser.fs

**Purpose**: Update the guard parser to use shared `ParseFailure`/`ParseWarning`/`SourcePosition` types from `Ast.Types`. The `GuardAnnotation` type needs to be redefined locally since it was removed from `Wsd/Types.fs`.

**Steps**:

1. Open `src/Frank.Statecharts/Wsd/GuardParser.fs`
2. Change `open Frank.Statecharts.Wsd.Types` to also include `open Frank.Statecharts.Ast`
3. Define `GuardAnnotation` locally at the top of the module (it is a WSD-specific parse helper, NOT a shared AST concept):
   ```fsharp
   type GuardAnnotation =
       { Pairs: (string * string) list
         Position: SourcePosition }
   ```
4. Update `ParseFailure` construction: wrap `Position` in `Some`:
   - Change `{ Position = { Line = ...; Column = ... }; ... }` to `{ Position = Some { Line = ...; Column = ... }; ... }`
   - This applies to the unclosed bracket error (line ~48-55) and the empty key / missing equals errors (lines ~110-130)
5. Update `ParseWarning` construction: wrap `Position` in `Some`:
   - Change `{ Position = { Line = ...; Column = ... }; ... }` to `{ Position = Some { Line = ...; Column = ... }; ... }`
   - This applies to the empty guard warning (line ~70-74) and empty value warning (lines ~138-143)
6. The return type `(GuardAnnotation option * string * ParseFailure list * ParseWarning list)` uses the shared `ParseFailure` and `ParseWarning` types now, and the local `GuardAnnotation` type

**Files**: `src/Frank.Statecharts/Wsd/GuardParser.fs`
**Parallel?**: Yes (modifies a different file from T012-T016)
**Notes**: The `GuardAnnotation` type is NOT part of the shared AST. It is a WSD-specific intermediate type that the guard parser produces. The extracted guard data will eventually populate `TransitionEdge.Guard` or note annotations in the parser.

### Subtask T012 -- Migrate ParserState to shared types

**Purpose**: Update the `ParserState` record in `Parser.fs` to use shared AST types for elements, errors, and warnings.

**Steps**:

1. Open `src/Frank.Statecharts/Wsd/Parser.fs`
2. Change `open Frank.Statecharts.Wsd.Types` to also include `open Frank.Statecharts.Ast`
3. Import the local `GuardAnnotation` from `GuardParser`:
   - The parser already references `GuardParser.tryParseGuard` -- the return type automatically uses the local `GuardAnnotation`
4. Update `ParserState` record:
   ```fsharp
   type ParserState =
       { Tokens: Token array
         mutable Position: int
         mutable Participants: Map<string, {| Identifier: string; Label: string option; Explicit: bool; Position: SourcePosition |}>
         mutable Elements: StatechartElement list
         mutable Errors: ParseFailure list
         mutable Warnings: ParseWarning list
         mutable Title: string option
         mutable AutoNumber: bool
         mutable ErrorLimitReached: bool
         mutable ImplicitWarned: Set<string>
         MaxErrors: int }
   ```

   **ALTERNATIVE approach** (simpler): Keep `Participants` as a `Map<string, Participant>` by defining a local `Participant` type:
   ```fsharp
   /// Internal parser tracking type -- NOT part of shared AST
   type internal Participant =
       { Name: string
         Alias: string option
         Explicit: bool
         Position: SourcePosition }
   ```
   This avoids anonymous records and keeps the parser's internal tracking clean. The `Participant` is converted to `StateNode` when emitted as a `StateDecl`.

5. Update `Elements` type from `DiagramElement list` to `StatechartElement list`
6. Update `Errors` to use shared `ParseFailure list` and `Warnings` to use shared `ParseWarning list`

**Files**: `src/Frank.Statecharts/Wsd/Parser.fs`
**Parallel?**: No (T013-T016 depend on this)
**Notes**: The internal `Participant` type keeps the parser's behavior unchanged -- it still tracks explicit/implicit status. The conversion to `StateNode` happens at the emit point (T013).

### Subtask T013 -- Migrate parseParticipant to produce StateDecl

**Purpose**: Change the participant parsing logic to emit `StateDecl of StateNode` instead of `ParticipantDecl of Participant`.

**Steps**:

1. In `parseParticipant`, after building the internal `Participant` record, create a `StateNode`:
   ```fsharp
   let stateNode =
       { Identifier = name
         Label = alias
         Kind = StateKind.Regular
         Children = []
         Activities = None
         Position = Some startToken.Position
         Annotations = [] }
   ```
2. Change the element emission from:
   ```fsharp
   state.Elements <- ParticipantDecl participant :: state.Elements
   ```
   to:
   ```fsharp
   state.Elements <- StateDecl stateNode :: state.Elements
   ```
3. The `registerParticipant` function continues to use the internal `Participant` type for tracking explicit/implicit status. No change needed there.
4. The `ensureParticipant` function also uses the internal `Participant` type. No change needed.

**IMPORTANT**: The internal `Participants` map is used for:
- Tracking whether a participant was explicitly declared (for implicit warnings)
- Preventing duplicate warnings
- Building the final participants list (which is now derived from `StateDecl` elements)

The internal `Participant` type serves all these purposes. Only the emitted element type changes.

**Files**: `src/Frank.Statecharts/Wsd/Parser.fs`
**Parallel?**: No (sequential within Parser.fs)

### Subtask T014 -- Migrate parseMessage to produce TransitionElement

**Purpose**: Change message parsing to emit `TransitionElement of TransitionEdge` with WSD arrow style as an annotation.

**Steps**:

1. In `parseMessage`, after parsing sender, arrow, receiver, label, and parameters, create a `TransitionEdge`:
   ```fsharp
   let transitionStyle =
       { ArrowStyle =
             (match arrowStyle with
              | Solid -> ArrowStyle.Solid
              | Dashed -> ArrowStyle.Dashed)
         Direction =
             (match direction with
              | Forward -> Direction.Forward
              | Deactivating -> Direction.Deactivating) }
   ```

   **WAIT** -- `ArrowStyle` and `Direction` are now in `Frank.Statecharts.Ast`, not as `Solid`/`Dashed` from `Wsd.Types`. The `mapArrow` function returns `(ArrowStyle * Direction)` from `TokenKind` matching. Since the WSD `ArrowStyle`/`Direction` types were removed, `mapArrow` must now return the shared AST types directly.

2. Update `mapArrow` to return shared AST types:
   ```fsharp
   let private mapArrow (kind: TokenKind) : (ArrowStyle * Direction) option =
       match kind with
       | SolidArrow -> Some(ArrowStyle.Solid, Direction.Forward)
       | DashedArrow -> Some(ArrowStyle.Dashed, Direction.Forward)
       | SolidDeactivate -> Some(ArrowStyle.Solid, Direction.Deactivating)
       | DashedDeactivate -> Some(ArrowStyle.Dashed, Direction.Deactivating)
       | _ -> None
   ```

   **NOTE**: Since `open Frank.Statecharts.Ast` brings `ArrowStyle` and `Direction` into scope, and `TokenKind` cases like `SolidArrow` come from `open Frank.Statecharts.Wsd.Types`, there could be ambiguity with the `ArrowStyle.Solid` etc. If needed, fully qualify: `Ast.ArrowStyle.Solid` or use module aliases.

3. Create the `TransitionEdge`:
   ```fsharp
   let transitionEdge =
       { Source = senderName
         Target = Some receiverName
         Event = if label.Length > 0 then Some label else None
         Guard = None
         Action = None
         Parameters = parameters
         Position = Some senderToken.Position
         Annotations =
             [ WsdAnnotation(
                   WsdTransitionStyle
                       { ArrowStyle = arrowStyle
                         Direction = direction })  ] }
   ```

4. Change emission from:
   ```fsharp
   state.Elements <- MessageElement message :: state.Elements
   ```
   to:
   ```fsharp
   state.Elements <- TransitionElement transitionEdge :: state.Elements
   ```

**Files**: `src/Frank.Statecharts/Wsd/Parser.fs`
**Parallel?**: No
**Notes**: The WSD `Message.Label` maps to `TransitionEdge.Event`. An empty label becomes `None`. The `ArrowStyle`/`Direction` become a `WsdAnnotation` in the annotations list.

### Subtask T015 -- Migrate remaining parse functions

**Purpose**: Update `parseNote`, `parseGroup`, directive parsing, and the top-level `parse`/`parseWsd` functions.

**Steps**:

1. **parseNote**: Change to produce `NoteElement of NoteContent`:
   ```fsharp
   let noteContent =
       { Target = target
         Content = finalContent
         Position = Some startToken.Position
         Annotations =
             [ WsdAnnotation(WsdNotePosition(
                   match position with
                   | NotePosition.Over -> WsdNotePosition.Over
                   | NotePosition.LeftOf -> WsdNotePosition.LeftOf
                   | NotePosition.RightOf -> WsdNotePosition.RightOf)) ] }
   ```

   **WAIT** -- `NotePosition` DU was removed from `Wsd.Types`. The local variable `notePos` in `parseNote` is of type `NotePosition option`. Since `NotePosition` no longer exists, use the shared `WsdNotePosition` directly:
   - Change local `notePos` to be of type `WsdNotePosition option`
   - Change the match arms from `Some NotePosition.Over` to `Some WsdNotePosition.Over` etc.

   Also handle the guard: if a guard was parsed, attach it as additional context. The `NoteContent` does not have a `Guard` field -- the guard data should be stored in the note's `Content` or as an annotation. The simplest approach: if a guard was parsed, keep the guard annotation as part of the note content or add the guard pairs as note annotations. For now, the guard data from notes is available through the parser's guard extraction -- downstream consumers handle association with transitions. Keep the `Content` field as the remaining text after guard extraction, and the guard data is not directly on `NoteContent`.

   **PRACTICAL APPROACH**: Define a helper to handle guard data. Since `NoteContent` doesn't have a `Guard` field, and guards extracted from notes are a WSD-specific concept, the parser should:
   - Keep the note content (remaining text after guard extraction)
   - The guard annotation data is available but not stored on `NoteContent` (it would be associated with transitions by a higher-level pass)
   - For now, if there's a guard, store a textual representation in the content or drop it (the tests check for guards on notes, so we need a solution)

   **BEST APPROACH**: Add the guard information as part of the note's annotations or content. Since the existing tests check `ns.[0].Guard.Value.Pairs`, we need the `NoteContent` to carry this data. Options:
   - (a) Encode guard as a WSD annotation on the note
   - (b) Keep a reconstructed `[guard: ...]` text in `Content`
   - (c) Extend `NoteContent` with an additional field

   Looking at the spec more carefully: "the `NoteContent` record carries the remaining text after guard extraction" and "guards extracted from notes are associated with transitions/states separately." So guards should NOT be on NoteContent. The tests will need to be updated in WP04 to check guards differently. For this WP, simply drop the guard data from `NoteContent` and let WP04 handle the test assertions.

   **SIMPLEST CORRECT APPROACH**: Since the guard annotation is WSD-specific parse information, store it as a WSD annotation on the note:
   ```fsharp
   // If guard was parsed, add it as a WSD annotation
   let guardAnnotations =
       match guard with
       | Some g ->
           [ WsdAnnotation(WsdGuardData(g.Pairs)) ]
       | None -> []
   ```
   BUT `WsdGuardData` doesn't exist in the shared AST. We could:
   - Add it to `WsdMeta` as `WsdGuardData of (string * string) list`
   - OR store guard pairs in a format the tests can check

   **FINAL DECISION**: Add a `WsdGuardData` case to `WsdMeta` in `Ast/Types.fs` (minor addition to WP01 types, or do it here if WP01 is already done). This keeps guard data accessible through the annotation system.

   If adding to `WsdMeta` is not desired, the alternative is to encode the guard in the note content as text. But this loses structured data.

   **PRACTICAL RECOMMENDATION**: For this migration, define a module-level helper type in `Parser.fs` to carry guard data alongside notes, and handle the mapping in WP04 tests. The cleanest approach:

   Add `WsdGuardData of pairs: (string * string) list` to `WsdMeta` in `Ast/Types.fs`. This is a one-line addition and keeps the data structured.

2. **parseTitleDirective**: Change from:
   ```fsharp
   state.Elements <- TitleDirective(titleText, startToken.Position) :: state.Elements
   ```
   to:
   ```fsharp
   state.Elements <- DirectiveElement(TitleDirective(titleText, Some startToken.Position)) :: state.Elements
   ```
   Note: The shared `Directive.TitleDirective` takes `position: SourcePosition option`.

3. **parseAutoNumberDirective**: Change from:
   ```fsharp
   state.Elements <- AutoNumberDirective startToken.Position :: state.Elements
   ```
   to:
   ```fsharp
   state.Elements <- DirectiveElement(AutoNumberDirective(Some startToken.Position)) :: state.Elements
   ```

4. **parseGroup / parseBranchBody / parseBranchElements**:
   - `GroupElement of Group` -> `GroupElement of GroupBlock`
   - The `Group` record becomes `GroupBlock`:
     ```fsharp
     let groupBlock =
         { Kind = groupKind
           Branches = branches |> Seq.toList
           Position = Some startToken.Position }
     ```
   - `parseBranchBody` returns `StatechartElement list` instead of `DiagramElement list`
   - `parseBranchElements` pattern matching uses `StatechartElement` cases

5. **Top-level `parse` function**: Change the return value construction:
   ```fsharp
   { Document =
       { Title = state.Title
         InitialStateId = None  // WSD has no explicit initial state concept
         Elements = List.rev state.Elements
         DataEntries = []  // WSD has no data model
         Annotations = [] }
     Errors = List.rev state.Errors
     Warnings = List.rev state.Warnings }
   ```

   **IMPORTANT**: The old `Diagram` had `AutoNumber: bool` and `Participants: Participant list`. These are gone:
   - `AutoNumber` is already captured as a `DirectiveElement(AutoNumberDirective _)` in the elements list
   - `Participants` were derived from the parser state. In the shared AST, participants are `StateDecl` elements in the elements list. The old code built a sorted participants list -- this is no longer needed.

   Remove the participants list construction at the end of `parse`:
   ```fsharp
   // REMOVE this block:
   let participants =
       state.Participants
       |> Map.toList
       |> List.map snd
       |> List.sortBy (fun p -> p.Position.Line, p.Position.Column)
   ```

6. **`parseWsd` function**: Remains the same (calls `Lexer.tokenize` then `parse`).

**Files**: `src/Frank.Statecharts/Wsd/Parser.fs`, possibly `src/Frank.Statecharts/Ast/Types.fs` (if adding `WsdGuardData`)
**Parallel?**: No (sequential changes across the parser)
**Notes**: The `mapGroupKind` function maps `TokenKind.Alt` -> `GroupKind.Alt` etc. Since `GroupKind` is now in `Frank.Statecharts.Ast`, the shared type is used directly. No ambiguity since the WSD `GroupKind` is removed.

### Subtask T016 -- Update addError/addWarning to wrap Position in option

**Purpose**: The shared `ParseFailure.Position` and `ParseWarning.Position` are now `SourcePosition option`. All construction sites in the parser must wrap positions in `Some`.

**Steps**:

1. Update `addError` function parameter and body:
   ```fsharp
   let private addError
       (state: ParserState)
       (pos: SourcePosition)
       (desc: string)
       (expected: string)
       (found: string)
       (example: string)
       : unit =
       if state.Errors.Length < state.MaxErrors then
           let failure =
               { Position = Some pos  // <-- wrap in Some
                 Description = desc
                 Expected = expected
                 Found = found
                 CorrectiveExample = example }
           state.Errors <- failure :: state.Errors
           if state.Errors.Length >= state.MaxErrors then
               let limitFailure =
                   { Position = Some pos  // <-- wrap in Some
                     Description = "Error limit reached; further errors suppressed"
                     Expected = ""
                     Found = ""
                     CorrectiveExample = "" }
               state.Errors <- limitFailure :: state.Errors
               state.ErrorLimitReached <- true
   ```

2. Update `addWarning` function:
   ```fsharp
   let private addWarning (state: ParserState) (pos: SourcePosition) (desc: string) (suggestion: string option) : unit =
       let warning =
           { Position = Some pos  // <-- wrap in Some
             Description = desc
             Suggestion = suggestion }
       state.Warnings <- warning :: state.Warnings
   ```

3. These are the ONLY two functions that construct `ParseFailure` and `ParseWarning` in `Parser.fs`. The `GuardParser.fs` has its own construction sites handled in T011.

**Files**: `src/Frank.Statecharts/Wsd/Parser.fs`
**Parallel?**: No

## Risks & Mitigations

- **Behavioral regression**: The parser's error recovery and implicit participant tracking are complex. Mitigation: The existing test suite (migrated in WP04) validates all behaviors. Run tests after WP04 to catch regressions.
- **Guard data on notes**: The `NoteContent` type does not have a `Guard` field. Guard data extracted from notes needs to be preserved somewhere (annotation or content). Mitigation: Add `WsdGuardData` to `WsdMeta` or encode guards as annotations.
- **Namespace ambiguity**: Both `Frank.Statecharts.Ast` and local types may have similarly-named types (e.g., `ArrowStyle`). Mitigation: Use fully-qualified names where ambiguous, or rely on the `open` order.
- **Pattern matching exhaustiveness**: The `parseBranchElements` and `parseElements` functions match on `DiagramElement` cases (`ParticipantDecl`, `MessageElement`, etc.). These DU cases are renamed. Mitigation: The compiler will flag all unmatched cases.

## Review Guidance

- Verify `parseWsd` returns shared `ParseResult` with `Document: StatechartDocument`
- Verify `TransitionEdge` carries WSD arrow style as `WsdAnnotation(WsdTransitionStyle ...)`
- Verify `StateNode` is emitted for participant declarations with `Kind = Regular`
- Verify `NoteContent` preserves note text and has WSD note position as annotation
- Verify `GroupBlock` replaces `Group` with same branching behavior
- Verify `DirectiveElement` wraps `TitleDirective` and `AutoNumberDirective`
- Verify `addError`/`addWarning` wrap positions in `Some`
- Verify `GuardAnnotation` is locally defined in `GuardParser.fs` module
- Run `dotnet build` to confirm compilation success

## Activity Log

- 2026-03-15T23:59:08Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T14:33:09Z – unknown – lane=done – Moved to done
