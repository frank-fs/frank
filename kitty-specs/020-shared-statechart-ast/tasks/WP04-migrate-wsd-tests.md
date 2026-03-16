---
work_package_id: "WP04"
title: "Migrate WSD Test Files"
phase: "Phase 2 - Parser Migration"
lane: "done"
assignee: ""
agent: ""
shell_pid: ""
review_status: "approved"
reviewed_by: "Ryan Riley"
dependencies: ["WP03"]
requirement_refs:
  - "FR-011"
  - "FR-012"
  - "FR-017"
  - "FR-018"
subtasks:
  - "T017"
  - "T018"
  - "T019"
  - "T020"
  - "T021"
  - "T022"
history:
  - timestamp: "2026-03-15T23:59:08Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP04 -- Migrate WSD Test Files

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

```bash
spec-kitty implement WP04 --base WP03
```

---

## Objectives & Success Criteria

- All 5 WSD test files (`GuardParserTests.fs`, `ParserTests.fs`, `GroupingTests.fs`, `ErrorTests.fs`, `RoundTripTests.fs`) assert against shared AST types
- `dotnet test test/Frank.Statecharts.Tests/` passes with ALL existing tests green
- No test is deleted -- every existing test must have a semantically equivalent replacement
- Test count remains the same (no tests lost or added in this WP)
- The `LexerTests.fs` file is UNCHANGED (lexer types did not migrate)

## Context & Constraints

- **Spec SC-002**: "The migrated WSD parser produces `StatechartDocument` output that, when tested against the existing WSD parser test suite, yields semantically equivalent results"
- **Plan**: Type Migration Map shows `Participant` -> `StateNode`, `Message` -> `TransitionEdge`, `Diagram` -> `StatechartDocument`, etc.
- **Key challenge**: `ArrowStyle`/`Direction` are no longer direct fields on messages -- they are `WsdAnnotation` values in `TransitionEdge.Annotations`. Tests must extract them.
- **Key challenge**: `NotePosition` is now `WsdNotePosition` in annotations. Tests must extract it.
- **Key challenge**: `Diagram.Participants` list no longer exists. Tests must derive participants from `StateDecl` elements.
- **Key challenge**: `Diagram.AutoNumber` no longer exists. Tests must check for `DirectiveElement(AutoNumberDirective _)` in elements.
- **Key challenge**: `ParseFailure.Position` is now `SourcePosition option`. Assertions on `.Position.Line` become `.Position.Value.Line`.

## Subtasks & Detailed Guidance

### Subtask T017 -- Migrate GuardParserTests.fs

**Purpose**: Update guard parser test assertions to use shared AST types (`SourcePosition`, `ParseFailure`, `ParseWarning`).

**Steps**:

1. Open `test/Frank.Statecharts.Tests/Wsd/GuardParserTests.fs`
2. Change `open Frank.Statecharts.Wsd.Types` to:
   ```fsharp
   open Frank.Statecharts.Ast
   open Frank.Statecharts.Wsd.GuardParser
   ```
   Note: `SourcePosition` now comes from `Ast`, `GuardAnnotation` comes from `GuardParser` (locally defined).
3. The `pos` helper function creates `SourcePosition` -- this should continue to work since it's the same shape.
4. Update `ParseFailure` position assertions -- `err.Position` is now `SourcePosition option`:
   - Test "US2-S4: unclosed bracket" asserts `err.Position (pos 3 10)` -- change to `Expect.equal err.Position (Some (pos 3 10))` or extract with `.Value`
5. Update `ParseWarning` position assertions similarly.
6. `GuardAnnotation.Pairs` and `GuardAnnotation.Position` should remain the same since the local type has the same shape.

**Key changes summary**:
- `open Frank.Statecharts.Wsd.Types` -> `open Frank.Statecharts.Ast` + `open Frank.Statecharts.Wsd.GuardParser`
- `err.Position` assertions become `err.Position.Value` or `Expect.equal err.Position (Some ...)` where position is checked
- Guard structure assertions unchanged (local `GuardAnnotation` has same shape)

**Files**: `test/Frank.Statecharts.Tests/Wsd/GuardParserTests.fs`
**Parallel?**: Yes (different file from other test migrations)

### Subtask T018 -- Migrate ParserTests.fs

**Purpose**: Update parser test assertions from WSD-specific types to shared AST types. This is the largest test file migration.

**Steps**:

1. Open `test/Frank.Statecharts.Tests/Wsd/ParserTests.fs`
2. Change imports:
   ```fsharp
   open Frank.Statecharts.Ast
   open Frank.Statecharts.Wsd.Parser
   ```
3. **Create shared helper functions** at the top of the module for extracting typed data from the shared AST:

   ```fsharp
   /// Extract TransitionEdge elements from parse result
   let private transitions (result: ParseResult) =
       result.Document.Elements
       |> List.choose (function
           | TransitionElement t -> Some t
           | _ -> None)

   /// Extract NoteContent elements from parse result
   let private notes (result: ParseResult) =
       result.Document.Elements
       |> List.choose (function
           | NoteElement n -> Some n
           | _ -> None)

   /// Extract StateNode declarations from parse result
   let private stateDecls (result: ParseResult) =
       result.Document.Elements
       |> List.choose (function
           | StateDecl s -> Some s
           | _ -> None)

   /// Extract WSD transition style from a TransitionEdge's annotations
   let private transitionStyle (edge: TransitionEdge) =
       edge.Annotations
       |> List.tryPick (function
           | WsdAnnotation(WsdTransitionStyle ts) -> Some ts
           | _ -> None)

   /// Check if document has AutoNumber directive
   let private hasAutoNumber (result: ParseResult) =
       result.Document.Elements
       |> List.exists (function
           | DirectiveElement(AutoNumberDirective _) -> true
           | _ -> false)
   ```

4. **Update all `result.Diagram.*` references to `result.Document.*`**:
   - `result.Diagram.Title` -> `result.Document.Title`
   - `result.Diagram.AutoNumber` -> `hasAutoNumber result`
   - `result.Diagram.Participants` -> `stateDecls result` (for explicit participant checks)
   - `result.Diagram.Elements` -> `result.Document.Elements`

5. **Update participant assertions**:
   - Old: `result.Diagram.Participants.[0]` with `.Name`, `.Alias`, `.Explicit`
   - New: `stateDecls result` gives `StateNode` list with `.Identifier`, `.Label`, no `.Explicit`
   - For `.Explicit` checks: check warnings instead (implicit participants generate warnings)
   - For participant count: `(stateDecls result).Length`
   - For participant name: `(stateDecls result).[0].Identifier`
   - For participant alias: `(stateDecls result).[0].Label`
   - For participant order: `stateDecls result |> List.map (fun s -> s.Identifier)`

   **IMPORTANT**: `Participant.Explicit` has no direct equivalent in `StateNode`. Tests that check `Explicit` need to:
   - Check for absence/presence of implicit participant warnings
   - OR accept that explicit/implicit tracking is parser-internal and not externally observable through AST types (except via warnings)

6. **Update message assertions**:
   - Old: `msgs.[0].Sender`, `msgs.[0].Receiver`, `msgs.[0].ArrowStyle`, `msgs.[0].Direction`, `msgs.[0].Label`, `msgs.[0].Parameters`
   - New: `let edges = transitions result` then:
     - `edges.[0].Source` (was `Sender`)
     - `edges.[0].Target` (was `Receiver`, now `string option` -- use `.Value` or `Expect.equal edges.[0].Target (Some "Server")`)
     - Arrow style: `(transitionStyle edges.[0]).Value.ArrowStyle` or pattern match
     - Direction: `(transitionStyle edges.[0]).Value.Direction`
     - Label: `edges.[0].Event` (was `Label`, now `string option` -- `Expect.equal edges.[0].Event (Some "hello")`)
     - `edges.[0].Parameters` (unchanged shape)

7. **Update note assertions**:
   - `ns.[0].NotePosition` -> extract from annotations: `WsdAnnotation(WsdNotePosition pos)` -> `pos`
   - `ns.[0].Guard` -> if guard data is stored as WSD annotation, extract it; if not available on `NoteContent`, adjust test
   - `ns.[0].Target` -> `ns.[0].Target` (unchanged)
   - `ns.[0].Content` -> `ns.[0].Content` (unchanged)

8. **Update directive assertions**:
   - `result.Diagram.AutoNumber` -> `hasAutoNumber result`
   - Title: `result.Document.Title`

9. **Rename helper from `messages` to `transitions`** throughout all assertions.

**Files**: `test/Frank.Statecharts.Tests/Wsd/ParserTests.fs`
**Parallel?**: No (large migration, do first to establish patterns)
**Notes**: This is the largest single-file migration. Establish the helper function patterns here, then reuse them in T019-T021.

### Subtask T019 -- Migrate GroupingTests.fs

**Purpose**: Update grouping test assertions to use shared AST types.

**Steps**:

1. Open `test/Frank.Statecharts.Tests/Wsd/GroupingTests.fs`
2. Change imports to `open Frank.Statecharts.Ast` and `open Frank.Statecharts.Wsd.Parser`
3. Update helper functions:
   - `groups` helper: `GroupElement g` stays the same (same DU case name in shared AST)
   - `messages` helper: rename to use `TransitionElement t` -> `Some t`
   - `branchGroups`: `GroupElement g` -> same
   - `branchMessages`: `MessageElement m` -> `TransitionElement t` -> `Some t`
   - `branchNotes`: `NoteElement n` -> same

4. Update assertions:
   - `result.Diagram.Elements` -> `result.Document.Elements`
   - `GroupKind.Alt` -> same (shared `GroupKind.Alt`)
   - `GroupBranch` type -> same (shared `GroupBranch`)
   - `ParticipantDecl _` -> `StateDecl _` (for filtering non-decl elements)
   - `MessageElement m` -> `TransitionElement t` with `t.Event` instead of `m.Label`
   - `m.Label` -> `t.Event.Value` (or `Expect.equal t.Event (Some "label")`)

5. The grouping structure (branches, conditions, nesting) should be identical since `GroupBlock` has the same shape as `Group`.

**Files**: `test/Frank.Statecharts.Tests/Wsd/GroupingTests.fs`
**Parallel?**: Can proceed after T018 establishes patterns

### Subtask T020 -- Migrate ErrorTests.fs

**Purpose**: Update error test assertions to use shared AST types.

**Steps**:

1. Open `test/Frank.Statecharts.Tests/Wsd/ErrorTests.fs`
2. Change imports to `open Frank.Statecharts.Ast`, `open Frank.Statecharts.Wsd.Lexer`, `open Frank.Statecharts.Wsd.Parser`
3. Update helper functions:
   - `messages` -> use `TransitionElement t`
   - `notes` -> same `NoteElement n`
   - `participantDecls` -> use `StateDecl s`

4. **Critical**: `ParseFailure.Position` is now `SourcePosition option`. Update assertions:
   - `err.Position.Line` -> `err.Position.Value.Line` (or match on `Some pos`)
   - `err.Position.Column` -> `err.Position.Value.Column`
   - Test "structured failure fields": `err.Position.Line > 0` -> `err.Position.Value.Line > 0`
   - Test "error position line and column": `err.Position.Line = 2` -> `err.Position.Value.Line = 2`

5. Update element references:
   - `result.Diagram.Elements` -> `result.Document.Elements`
   - `ParticipantDecl p` -> `StateDecl s` with `s.Identifier`
   - `MessageElement m` -> `TransitionElement t` with `t.Event`
   - `DiagramElement` pattern -> `StatechartElement` pattern

6. Guard integration tests:
   - `ns.[0].Guard` assertions depend on how guard data is stored in WP03
   - If guard data is in `NoteContent.Annotations` as `WsdAnnotation(WsdGuardData pairs)`, extract accordingly
   - If guard data is not on `NoteContent`, these assertions need to be adjusted

**Files**: `test/Frank.Statecharts.Tests/Wsd/ErrorTests.fs`
**Parallel?**: Can proceed after T018

### Subtask T021 -- Migrate RoundTripTests.fs

**Purpose**: Update the comprehensive round-trip tests (Onboarding Flow, Tic-Tac-Toe, Edge Cases) to use shared AST types.

**Steps**:

1. Open `test/Frank.Statecharts.Tests/Wsd/RoundTripTests.fs`
2. Change imports to `open Frank.Statecharts.Ast` and `open Frank.Statecharts.Wsd.Parser`
3. Update helper functions:
   - `participants r` -> derive from `StateDecl` elements:
     ```fsharp
     let private stateDecls (r: ParseResult) =
         r.Document.Elements
         |> List.choose (function StateDecl s -> Some s | _ -> None)
     ```
   - `messages r` -> `transitions r` using `TransitionElement`
   - `notes r` -> same pattern
   - `groups r` -> same pattern

4. **Onboarding Flow tests**:
   - `result.Diagram.Title` -> `result.Document.Title`
   - `result.Diagram.AutoNumber` -> check for `AutoNumberDirective` in elements
   - `participants result` -> `stateDecls result`
   - `ps.[0].Name` -> `ps.[0].Identifier`
   - `p.Explicit` -> check that no implicit warnings exist for that participant
   - `m.Sender`/`m.Receiver` -> `t.Source`/`t.Target`
   - `m.ArrowStyle ArrowStyle.Solid` -> extract from annotations
   - `m.Direction Direction.Forward` -> extract from annotations
   - `m.Label` -> `t.Event`
   - `n.Guard` -> extract from annotations (WsdGuardData or similar)
   - `m.Direction = Direction.Deactivating` filter -> extract and filter by annotation

5. **Tic-Tac-Toe tests**:
   - Same patterns as Onboarding
   - Guard assertions on notes need annotation extraction
   - `GroupKind.Alt` -> same

6. **Edge Case tests**:
   - `Diagram.Elements` -> `Document.Elements`
   - `Diagram.Title` -> `Document.Title`
   - `Diagram.AutoNumber` -> check elements for directive
   - `Diagram.Participants` -> `stateDecls result`
   - `p.Explicit` -> check warnings
   - All arrow style/direction assertions -> extract from annotations
   - `NotePosition.Over/LeftOf/RightOf` -> extract `WsdNotePosition` from note annotations

7. **Determinism test**: `result1.Diagram.Title = result2.Diagram.Title` -> `result1.Document.Title = result2.Document.Title`

**Files**: `test/Frank.Statecharts.Tests/Wsd/RoundTripTests.fs`
**Parallel?**: Can proceed after T018
**Notes**: This is the second-largest test migration. The Onboarding and Tic-Tac-Toe tests are comprehensive acceptance tests that validate end-to-end behavior.

### Subtask T022 -- Update test project compile order

**Purpose**: Ensure the test project `.fsproj` has correct compile entries for any new or moved files.

**Steps**:

1. Open `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
2. The existing compile order for WSD test files should be unchanged (no files moved or renamed):
   ```xml
   <Compile Include="Wsd/GuardParserTests.fs" />
   <Compile Include="Wsd/ParserTests.fs" />
   <Compile Include="Wsd/GroupingTests.fs" />
   <Compile Include="Wsd/ErrorTests.fs" />
   <Compile Include="Wsd/RoundTripTests.fs" />
   ```
3. No new test files are added in this WP (new tests are WP05)
4. Verify `dotnet test test/Frank.Statecharts.Tests/` runs all WSD tests

**Files**: `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
**Parallel?**: No

## Risks & Mitigations

- **Test count regression**: Must verify same number of tests pass before and after migration. Mitigation: Run `dotnet test` before migration to get baseline count, then compare after.
- **Arrow style extraction verbosity**: Tests previously checked `msg.ArrowStyle = Solid` (one field access). Now need to extract from annotations list. Mitigation: Create helper functions like `transitionStyle` to keep test assertions clean.
- **Guard data access**: How guard data is stored on `NoteContent` determines assertion patterns. Mitigation: Align with WP03's decision on guard annotation storage.
- **Participant.Explicit**: No direct equivalent in `StateNode`. Tests checking `Explicit` must use alternative approaches (warning presence/absence). Mitigation: Document the alternative approach clearly.

## Review Guidance

- Run `dotnet test` and verify ALL tests pass (zero failures)
- Compare test count before and after migration (must be identical)
- Check that helper functions (`transitions`, `stateDecls`, `transitionStyle`) are clean and reusable
- Verify no test was silently weakened (e.g., removing an assertion instead of updating it)
- Spot-check that arrow style assertions actually test the right thing through annotations

## Activity Log

- 2026-03-15T23:59:08Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T11:45:28Z – unknown – lane=done – Moved to done
- 2026-03-16T14:33:09Z – unknown – lane=done – Moved to done
