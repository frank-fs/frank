---
work_package_id: "WP02"
title: "Migrate WSD Types & Lexer"
phase: "Phase 1 - Foundation"
lane: "done"
assignee: ""
agent: ""
shell_pid: ""
review_status: "approved"
reviewed_by: "Ryan Riley"
dependencies: ["WP01"]
requirement_refs:
  - "FR-014"
  - "FR-018"
subtasks:
  - "T007"
  - "T008"
  - "T009"
  - "T010"
history:
  - timestamp: "2026-03-15T23:59:08Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 -- Migrate WSD Types & Lexer

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

```bash
spec-kitty implement WP02 --base WP01
```

---

## Objectives & Success Criteria

- `Wsd/Types.fs` contains ONLY `TokenKind` and `Token` (lexer-specific types)
- All WSD semantic types (`Participant`, `Message`, `Note`, `Diagram`, `ParseFailure`, `ParseWarning`, `ParseResult`, `SourcePosition`, `ArrowStyle`, `Direction`, `GroupKind`, `GroupBranch`, `Group`, `DiagramElement`, `GuardAnnotation`, `NotePosition`) are removed from `Wsd/Types.fs`
- `Token.Position` uses `SourcePosition` from `Frank.Statecharts.Ast`
- `Wsd/Lexer.fs` imports `SourcePosition` from the shared AST namespace
- **NOTE**: Build will NOT succeed after this WP alone -- `GuardParser.fs` and `Parser.fs` still reference removed types. They are fixed in WP03. This WP and WP03 should be implemented in the same branch/session.

## Context & Constraints

- **Plan**: `kitty-specs/020-shared-statechart-ast/plan.md` -- Type Migration Map shows exactly which types move/stay
- **Research**: Decision D-001 (`SourcePosition` moves to shared namespace), D-002 (`ArrowStyle`/`Direction` become WSD annotation), D-009 (`Token` uses shared `SourcePosition`)
- **Current file**: `src/Frank.Statecharts/Wsd/Types.fs` -- 150 lines, defines 16+ types
- **After migration**: `src/Frank.Statecharts/Wsd/Types.fs` -- ~50 lines, defines only `TokenKind` and `Token`

## Subtasks & Detailed Guidance

### Subtask T007 -- Strip WSD Types.fs to lexer-only types

**Purpose**: Remove all semantic AST types from `Wsd/Types.fs` that now live in the shared `Ast/Types.fs`. Only lexer infrastructure (`TokenKind`, `Token`) remains.

**Steps**:

1. Open `src/Frank.Statecharts/Wsd/Types.fs`
2. **KEEP** the following (lines 1-51 in current file):
   - Module declaration: `module internal Frank.Statecharts.Wsd.Types`
   - `TokenKind` DU (all cases unchanged)
   - `Token` struct (but `Position` type will change in T008)
3. **REMOVE** all of the following:
   - `SourcePosition` struct (line 4-5) -- now in `Ast.Types`
   - `ArrowStyle` DU (line 53-55) -- now `ArrowStyle` in `Ast.Types`
   - `Direction` DU (line 57-59) -- now `Direction` in `Ast.Types`
   - `Participant` record (line 62-66) -- replaced by `StateNode` in `Ast.Types`
   - `Message` record (line 69-76) -- replaced by `TransitionEdge` in `Ast.Types`
   - `GuardAnnotation` record (line 79-81) -- WSD parse helper, will be redefined locally in `GuardParser.fs` or `Parser.fs` (see WP03)
   - `NotePosition` DU (line 84-87) -- now `WsdNotePosition` in `Ast.Types`
   - `Note` record (line 89-94) -- replaced by `NoteContent` in `Ast.Types`
   - `GroupKind` DU (line 97-104) -- now in `Ast.Types`
   - `GroupBranch` record (line 107-109) -- now in `Ast.Types`
   - `Group` record (line 111-114) -- now `GroupBlock` in `Ast.Types`
   - `DiagramElement` DU (line 117-123) -- now `StatechartElement` in `Ast.Types`
   - `Diagram` record (line 126-130) -- now `StatechartDocument` in `Ast.Types`
   - `ParseFailure` record (line 133-138) -- now in `Ast.Types`
   - `ParseWarning` record (line 140-143) -- now in `Ast.Types`
   - `ParseResult` record (line 145-148) -- now in `Ast.Types`

**Files**: `src/Frank.Statecharts/Wsd/Types.fs`
**Parallel?**: No

### Subtask T008 -- Add shared AST import to WSD Types.fs

**Purpose**: Make the `Token` struct reference the shared `SourcePosition` type instead of the removed WSD-local one.

**Steps**:

1. Add `open Frank.Statecharts.Ast` after the module declaration in `Wsd/Types.fs`
2. The `Token` struct's `Position: SourcePosition` field now resolves to the shared type
3. The resulting file should look like:

```fsharp
module internal Frank.Statecharts.Wsd.Types

open Frank.Statecharts.Ast

type TokenKind =
    // Keywords
    | Participant
    | Title
    | AutoNumber
    | Note
    | Over
    | LeftOf
    | RightOf
    | Alt
    | Opt
    | Loop
    | Par
    | Break
    | Critical
    | Ref
    | Else
    | End
    | As
    // Arrows
    | SolidArrow
    | DashedArrow
    | SolidDeactivate
    | DashedDeactivate
    // Punctuation
    | Colon
    | LeftParen
    | RightParen
    | Comma
    | LeftBracket
    | RightBracket
    | Equals
    // Content
    | Identifier of string
    | StringLiteral of string
    | TextContent of string
    // Structure
    | Newline
    | Eof

[<Struct>]
type Token =
    { Kind: TokenKind
      Position: SourcePosition }
```

**Files**: `src/Frank.Statecharts/Wsd/Types.fs`
**Parallel?**: No (depends on T007)

### Subtask T009 -- Update Lexer.fs to import shared SourcePosition

**Purpose**: The lexer creates `Token` values with `SourcePosition` values. Since `SourcePosition` is now in `Frank.Statecharts.Ast`, the lexer needs access to it (inherited through `Token` type, but `open` may be needed for direct construction).

**Steps**:

1. Open `src/Frank.Statecharts/Wsd/Lexer.fs`
2. The existing `open Frank.Statecharts.Wsd.Types` imports `Token` and `TokenKind`
3. Add `open Frank.Statecharts.Ast` to get direct access to `SourcePosition` for the `makeToken` inline function which constructs `{ Line = l; Column = c }` position records
4. The lexer creates positions like `{ Line = l; Column = c }` in the `makeToken` helper. This record literal syntax should resolve to the shared `SourcePosition` since it is `open`-ed.
5. Verify no other changes needed -- the lexer only produces `Token` values with `SourcePosition`, it does not reference any other removed types.

**Files**: `src/Frank.Statecharts/Wsd/Lexer.fs`
**Parallel?**: No (depends on T008)
**Notes**: The lexer does NOT reference `Participant`, `Message`, `Diagram`, or any other semantic types. It only produces `Token list`. Changes should be minimal.

### Subtask T010 -- Verify build state

**Purpose**: Confirm the expected build state after type removal.

**Steps**:

1. Run `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` -- this is expected to FAIL because `GuardParser.fs` and `Parser.fs` still reference removed types
2. Verify that the only compilation errors are in `GuardParser.fs` and `Parser.fs` (not in `Lexer.fs` or `Ast/Types.fs`)
3. This confirms T007-T009 are correct and WP03 is the only remaining migration work
4. Document the error count and error types for WP03 reference

**IMPORTANT**: This WP intentionally creates a non-building state. It is designed to be followed immediately by WP03 in the same implementation session. If the build is required to pass at this checkpoint, you may combine WP02 and WP03 into a single implementation session.

**Files**: N/A (verification)
**Parallel?**: No

## Risks & Mitigations

- **Intermediate broken build**: Between WP02 and WP03, the project will not compile. This is acceptable for a multi-WP migration. Mitigation: Implement WP02 and WP03 in the same session.
- **GuardAnnotation orphan**: The `GuardAnnotation` type is removed from `Wsd/Types.fs` but is still needed by `GuardParser.fs`. It will be redefined locally in `GuardParser.fs` in WP03 (it's a WSD-specific parse helper, not a shared AST concept).
- **Token struct backward compatibility**: The `Token` struct field type changes from a WSD-local `SourcePosition` to a shared `SourcePosition`. Since both have identical shape (`{ Line: int; Column: int }`), this is a source-compatible change.

## Review Guidance

- Verify `Wsd/Types.fs` contains ONLY `TokenKind` and `Token`
- Verify `open Frank.Statecharts.Ast` is present in `Wsd/Types.fs`
- Verify `Lexer.fs` has `open Frank.Statecharts.Ast`
- Check that no types were accidentally left behind in `Wsd/Types.fs`
- Understand that the build will fail after this WP -- WP03 completes the migration

## Activity Log

- 2026-03-15T23:59:08Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T04:30:30Z – unknown – lane=done – Moved to done
- 2026-03-16T14:33:09Z – unknown – lane=done – Moved to done
