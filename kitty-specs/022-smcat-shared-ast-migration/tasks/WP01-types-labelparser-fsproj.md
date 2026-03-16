---
work_package_id: "WP01"
title: "Types, LabelParser, and fsproj Updates"
phase: "Phase 1 - Foundation"
lane: "for_review"
assignee: ""
agent: "claude-opus"
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: []
requirement_refs: ["FR-018", "FR-019", "FR-020"]
subtasks:
  - "T001"
  - "T002"
  - "T003"
history:
  - timestamp: "2026-03-16T19:13:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP01 -- Types, LabelParser, and fsproj Updates

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Objectives & Success Criteria

- `Types.fs` retains ONLY: `TokenKind`, `Token` (using `Ast.SourcePosition`), `TransitionLabel`, `SmcatAttribute`, and `inferStateType` (returning `Ast.StateKind`)
- All smcat-specific semantic types are deleted: `SourcePosition`, `StateType`, `StateActivity`, `SmcatState`, `SmcatTransition`, `SmcatElement`, `SmcatDocument`, `ParseResult`, `ParseFailure`, `ParseWarning`
- `LabelParser.fs` accepts `Ast.SourcePosition` and returns `Ast.ParseWarning list`
- `fsproj` has `Serializer.fs` in place of `Mapper.fs`
- `Lexer.fs` compiles (it uses `Token` and `TokenKind` from Types.fs, and `SourcePosition` via `Token.Position`)

## Context & Constraints

- **Spec**: `kitty-specs/022-smcat-shared-ast-migration/spec.md` (FR-018, FR-019, FR-020)
- **Plan**: `kitty-specs/022-smcat-shared-ast-migration/plan.md` (DD-004, DD-005, DD-007)
- **Shared AST types**: `src/Frank.Statecharts/Ast/Types.fs` -- defines `SourcePosition`, `StateKind`, `StateActivities`, `ParseFailure`, `ParseWarning`, `ParseResult`, `StatechartDocument`, `StateNode`, `TransitionEdge`, `StatechartElement`, `SmcatAnnotation`, `SmcatMeta`
- **WSD reference**: `src/Frank.Statecharts/Wsd/Types.fs` -- shows the pattern (lexer-only types retained)
- **Constraint**: Downstream files (`Parser.fs`, `Generator.fs`) will NOT compile after this WP. That is expected. WP02 and WP03 fix those files.
- **Constraint**: `Lexer.fs` MUST still compile -- it only uses `TokenKind`, `Token`, and position struct

## Implementation Command

```bash
spec-kitty implement WP01
```

## Subtasks & Detailed Guidance

### Subtask T001 -- Modify Types.fs: Delete semantic types, migrate retained types

**Purpose**: Reduce `Types.fs` to lexer-only types. The semantic types are replaced by the shared AST (`Ast/Types.fs`).

**Steps**:

1. Open `src/Frank.Statecharts/Smcat/Types.fs`
2. Add `open Frank.Statecharts.Ast` at the top (after `open System`)
3. **Delete** these types entirely:
   - `SourcePosition` struct (lines 6-7) -- replaced by `Ast.SourcePosition`
   - `StateType` DU (lines 43-51) -- replaced by `Ast.StateKind`
   - `StateActivity` record (lines 54-57) -- replaced by `Ast.StateActivities`
   - `SmcatState` record (lines 69-76) -- replaced by `Ast.StateNode`
   - `SmcatTransition` record (lines 78-83) -- replaced by `Ast.TransitionEdge`
   - `SmcatElement` DU (lines 85-88) -- replaced by `Ast.StatechartElement`
   - `SmcatDocument` record (lines 90-91) -- replaced by `Ast.StatechartDocument`
   - `ParseFailure` record (lines 94-99) -- replaced by `Ast.ParseFailure`
   - `ParseWarning` record (lines 101-104) -- replaced by `Ast.ParseWarning`
   - `ParseResult` record (lines 106-109) -- replaced by `Ast.ParseResult`

4. **Modify `Token`** struct to use `Ast.SourcePosition`:
   ```fsharp
   [<Struct>]
   type Token =
       { Kind: TokenKind
         Position: Ast.SourcePosition }
   ```
   Note: Since we `open Frank.Statecharts.Ast`, we can just use `SourcePosition` unqualified. But to avoid ambiguity, use `Ast.SourcePosition` explicitly, or rely on the fact that the local `SourcePosition` has been deleted.

5. **Modify `inferStateType`** to return `Ast.StateKind` instead of `StateType`:
   ```fsharp
   let inferStateType (name: string) (attributes: SmcatAttribute list) : StateKind =
       match typeAttr with
       | Some attr ->
           match attr.Value.ToLowerInvariant() with
           | "initial" -> StateKind.Initial
           | "final" -> StateKind.Final
           | "history" -> StateKind.ShallowHistory
           | "deep.history" -> StateKind.DeepHistory
           | "choice" -> StateKind.Choice
           | "forkjoin" -> StateKind.ForkJoin
           | "terminate" -> StateKind.Terminate
           | "regular" -> StateKind.Regular
           | _ -> StateKind.Regular
       | None ->
           if name.Contains("initial", StringComparison.OrdinalIgnoreCase) then StateKind.Initial
           elif name.Contains("final", StringComparison.OrdinalIgnoreCase) then StateKind.Final
           elif name.Contains("deep.history", StringComparison.OrdinalIgnoreCase) then StateKind.DeepHistory
           elif name.Contains("history", StringComparison.OrdinalIgnoreCase) then StateKind.ShallowHistory
           elif name.StartsWith('^') then StateKind.Choice
           elif name.StartsWith(']') then StateKind.ForkJoin
           elif name.Contains("terminate", StringComparison.OrdinalIgnoreCase) then StateKind.Terminate
           else StateKind.Regular
   ```

**Files**: `src/Frank.Statecharts/Smcat/Types.fs`
**Parallel?**: Can proceed alongside T002 once started.

**Validation**:
- [ ] Types.fs contains only: `TokenKind`, `Token`, `SmcatAttribute`, `TransitionLabel`, `inferStateType`
- [ ] `Token.Position` is of type `Ast.SourcePosition` (or unqualified `SourcePosition` from `Ast`)
- [ ] `inferStateType` returns `StateKind` (from `Ast`)
- [ ] No references to deleted types remain in `Types.fs`

### Subtask T002 -- Modify LabelParser.fs: Use shared AST types

**Purpose**: `LabelParser.fs` references `Types.SourcePosition` and `Types.ParseWarning`. After deleting those from Types.fs, it must use `Ast.SourcePosition` and `Ast.ParseWarning`.

**Steps**:

1. Open `src/Frank.Statecharts/Smcat/LabelParser.fs`
2. Add `open Frank.Statecharts.Ast` after the existing opens
3. Update the `parseLabel` function signature:
   - Parameter type: `position: SourcePosition` (now `Ast.SourcePosition` since smcat-local one is deleted)
   - Return type: `TransitionLabel * ParseWarning list` (now `Ast.ParseWarning` since smcat-local one is deleted)
4. Update warning construction inside `parseLabel`:
   - Current:
     ```fsharp
     warnings.Add(
         { Position =
             { Line = position.Line
               Column = position.Column + bracketStart }
           Description = "Unclosed bracket in transition label"
           Suggestion = Some "Add closing ']' bracket" }
     )
     ```
   - New (Ast.ParseWarning has `Position: SourcePosition option`):
     ```fsharp
     warnings.Add(
         { Position =
             Some { Line = position.Line
                    Column = position.Column + bracketStart }
           Description = "Unclosed bracket in transition label"
           Suggestion = Some "Add closing ']' bracket" }
     )
     ```
   Note the key difference: `Position` is now `SourcePosition option` in the shared AST, so wrap in `Some`.

**Files**: `src/Frank.Statecharts/Smcat/LabelParser.fs`
**Parallel?**: Yes, can proceed alongside T001.

**Validation**:
- [ ] `parseLabel` accepts `SourcePosition` (from `Ast`) and returns `TransitionLabel * ParseWarning list` (from `Ast`)
- [ ] Warning position is wrapped in `Some`
- [ ] `TransitionLabel` still comes from local `Types.fs` (retained type)

### Subtask T003 -- Modify fsproj: Remove Mapper.fs, add Serializer.fs

**Purpose**: Update the project file to reflect the new file structure.

**Steps**:

1. Open `src/Frank.Statecharts/Frank.Statecharts.fsproj`
2. Find the line: `<Compile Include="Smcat/Mapper.fs" />` (line 30)
3. Replace it with: `<Compile Include="Smcat/Serializer.fs" />`
4. Verify `Smcat/Generator.fs` remains at its current position (line 47)

The resulting order should be:
```xml
<Compile Include="Smcat/Types.fs" />
<Compile Include="Smcat/Lexer.fs" />
<Compile Include="Smcat/LabelParser.fs" />
<Compile Include="Smcat/Parser.fs" />
<Compile Include="Smcat/Serializer.fs" />   <!-- was Mapper.fs -->
...
<Compile Include="Smcat/Generator.fs" />     <!-- unchanged position -->
```

**Files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
**Parallel?**: Yes, independent of T001 and T002.

**Validation**:
- [ ] No `Mapper.fs` compile entry exists
- [ ] `Serializer.fs` compile entry exists after `Parser.fs`
- [ ] `Generator.fs` compile entry is unchanged

## Risks & Mitigations

- **Risk**: `Lexer.fs` may fail to compile if it references the deleted `SourcePosition` type directly. **Mitigation**: `Lexer.fs` only uses `Token` and `TokenKind` from `Types.fs`, and constructs `Token` with `Position` field. Since `Token.Position` is now `Ast.SourcePosition`, the Lexer needs `open Frank.Statecharts.Ast` added. Check this.
- **Risk**: Other files in the Smcat folder won't compile after this WP. **Mitigation**: This is expected and will be resolved in WP02 (Parser) and WP03 (Serializer/Generator).

## Review Guidance

- Verify that `Types.fs` is genuinely reduced to lexer-only types
- Verify `inferStateType` return type is `StateKind` from `Ast`
- Verify `LabelParser.parseLabel` signature matches the new types
- Verify `Lexer.fs` still compiles (add `open Frank.Statecharts.Ast` if needed)
- Verify `fsproj` ordering matches DD-007 from the plan

## Activity Log

- 2026-03-16T19:13:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T22:56:11Z – claude-opus – lane=for_review – Ready for review: Types.fs reduced to lexer-only types (TokenKind, Token, SmcatAttribute, TransitionLabel, inferStateType). LabelParser.fs updated to use Ast.SourcePosition and Ast.ParseWarning. Lexer.fs updated with open Ast. fsproj updated Mapper->Serializer. Stub Serializer.fs created. All build errors are in downstream Parser.fs/Generator.fs as expected per WP scope.
