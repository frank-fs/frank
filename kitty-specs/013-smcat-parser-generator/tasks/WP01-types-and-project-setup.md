---
work_package_id: WP01
title: Types & Project Setup
lane: "done"
dependencies: []
base_branch: master
base_commit: b6ebc1db2fa12d44f6003965cae9fe3f8c3b2c18
created_at: '2026-03-16T04:02:21.612401+00:00'
subtasks:
- T001
- T002
- T003
- T004
- T005
phase: Phase 0 - Foundation
assignee: ''
agent: "claude-opus"
shell_pid: "97851"
review_status: "approved"
reviewed_by: "Ryan Riley"
history:
- timestamp: '2026-03-15T23:59:14Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-004]
---

# Work Package Prompt: WP01 -- Types & Project Setup

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP01
```

No dependencies -- this is the foundation WP.

---

## Objectives & Success Criteria

- Define all smcat-specific AST types in `src/Frank.Statecharts/Smcat/Types.fs`
- Implement `inferStateType` function for pseudo-state detection by naming convention
- Wire Smcat source files into `Frank.Statecharts.fsproj` in correct F# compile order
- Wire Smcat test files into `Frank.Statecharts.Tests.fsproj`
- Multi-target build (`net8.0;net9.0;net10.0`) succeeds with new files included

**Done when**: `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` succeeds and Types.fs contains all types from the data model.

## Context & Constraints

- **Spec**: `kitty-specs/013-smcat-parser-generator/spec.md` (FR-001 through FR-014, key entities)
- **Data Model**: `kitty-specs/013-smcat-parser-generator/data-model.md` (complete type definitions)
- **API Signatures**: `kitty-specs/013-smcat-parser-generator/contracts/api-signatures.md`
- **WSD Pattern Reference**: `src/Frank.Statecharts/Wsd/Types.fs` (follow module declaration and struct patterns)
- **Quickstart**: `kitty-specs/013-smcat-parser-generator/quickstart.md` (file ordering in .fsproj)

**Key constraints**:
- All modules are `module internal Frank.Statecharts.Smcat.*`
- `SourcePosition` is a temporary duplicate of `Wsd.Types.SourcePosition` (spec 020 will unify)
- `SmcatDocument` and `SmcatState` are mutually recursive -- must use F# `and` keyword
- Compile order in .fsproj matters: Smcat files go after Wsd files, before runtime Types.fs

## Subtasks & Detailed Guidance

### Subtask T001 -- Create `src/Frank.Statecharts/Smcat/Types.fs` with all smcat AST types

**Purpose**: Establish the complete type foundation that all other smcat modules depend on. Every downstream WP (Lexer, Parser, LabelParser, Generator) imports these types.

**Steps**:

1. Create directory `src/Frank.Statecharts/Smcat/` if it does not exist.

2. Create `src/Frank.Statecharts/Smcat/Types.fs` with module declaration:
   ```fsharp
   module internal Frank.Statecharts.Smcat.Types
   ```

3. Define `SourcePosition` as a struct (identical to WSD's):
   ```fsharp
   [<Struct>]
   type SourcePosition = { Line: int; Column: int }
   ```

4. Define `TokenKind` discriminated union with all cases from data-model.md:
   - Arrows: `TransitionArrow` (`=>`)
   - Punctuation: `Colon`, `Semicolon`, `Comma`, `LeftBracket`, `RightBracket`, `LeftBrace`, `RightBrace`, `ForwardSlash`, `Equals`
   - Content: `Identifier of string`, `QuotedString of string`
   - Pseudo-state prefixes: `Caret`, `CloseBracketPrefix`
   - Activities: `EntrySlash`, `ExitSlash`, `Ellipsis`
   - Structure: `Newline`, `Eof`

5. Define `Token` as a struct:
   ```fsharp
   [<Struct>]
   type Token = { Kind: TokenKind; Position: SourcePosition }
   ```

6. Define `StateType` DU: `Regular | Initial | Final | ShallowHistory | DeepHistory | Choice | ForkJoin | Terminate`

7. Define `StateActivity` record: `{ Entry: string option; Exit: string option; Do: string option }`

8. Define `SmcatAttribute` record: `{ Key: string; Value: string }`

9. Define `TransitionLabel` record: `{ Event: string option; Guard: string option; Action: string option }`

10. Define mutually recursive types using `and` keyword:
    ```fsharp
    type SmcatState =
        { Name: string
          Label: string option
          StateType: StateType
          Activities: StateActivity option
          Attributes: SmcatAttribute list
          Children: SmcatDocument option
          Position: SourcePosition }

    and SmcatTransition =
        { Source: string
          Target: string
          Label: TransitionLabel option
          Attributes: SmcatAttribute list
          Position: SourcePosition }

    and SmcatElement =
        | StateDeclaration of SmcatState
        | TransitionElement of SmcatTransition
        | CommentElement of string

    and SmcatDocument =
        { Elements: SmcatElement list }
    ```

    **IMPORTANT**: `SmcatState`, `SmcatTransition`, `SmcatElement`, and `SmcatDocument` must all be in the same `type ... and ...` block because `SmcatState.Children` references `SmcatDocument` and `SmcatDocument.Elements` contains `SmcatElement` which contains `SmcatState`. Follow the WSD pattern where `GroupBranch`, `Group`, and `DiagramElement` are defined with `and`.

11. Define `ParseFailure` record:
    ```fsharp
    type ParseFailure =
        { Position: SourcePosition
          Description: string
          Expected: string
          Found: string
          CorrectiveExample: string }
    ```

12. Define `ParseWarning` record:
    ```fsharp
    type ParseWarning =
        { Position: SourcePosition
          Description: string
          Suggestion: string option }
    ```

13. Define `ParseResult` record:
    ```fsharp
    type ParseResult =
        { Document: SmcatDocument
          Errors: ParseFailure list
          Warnings: ParseWarning list }
    ```

**Files**: `src/Frank.Statecharts/Smcat/Types.fs` (new, ~120 lines)

**Notes**: The exact type definitions are specified in `kitty-specs/013-smcat-parser-generator/data-model.md`. Use that as the authoritative reference.

---

### Subtask T002 -- Implement `inferStateType` function in Types.fs

**Purpose**: Provide a reusable function for pseudo-state detection by naming convention, used by both the Parser (AST construction) and the future Mapper (shared AST conversion). Specified in the API signatures contract.

**Steps**:

1. Add the `inferStateType` function to Types.fs:
   ```fsharp
   val inferStateType : name:string -> attributes:SmcatAttribute list -> StateType
   ```

2. Implement the detection rules in priority order (from data-model.md "State Type Detection Logic"):
   1. Check `attributes` for `[type=...]` -- if present, map the value to the corresponding `StateType` (this overrides naming convention)
   2. Name contains `"initial"` (case-insensitive) -> `Initial`
   3. Name contains `"final"` (case-insensitive) -> `Final`
   4. Name contains `"deep.history"` (case-insensitive) -> `DeepHistory`
   5. Name contains `"history"` (case-insensitive) -> `ShallowHistory` (must check after deep.history)
   6. Name starts with `^` -> `Choice`
   7. Name starts with `]` -> `ForkJoin`
   8. Name contains `"terminate"` (case-insensitive) -> `Terminate`
   9. Otherwise -> `Regular`

3. Use `System.String.Contains(value, System.StringComparison.OrdinalIgnoreCase)` for case-insensitive checks.

4. Use `name.StartsWith('^')` and `name.StartsWith(']')` for prefix checks.

5. For the `[type=...]` attribute override, map known values: `"initial"`, `"final"`, `"history"`, `"deep.history"`, `"choice"`, `"forkjoin"`, `"terminate"`, `"regular"`.

**Files**: `src/Frank.Statecharts/Smcat/Types.fs` (append to existing, ~40 lines)

**Notes**: The ordering of checks matters -- `deep.history` must be checked before `history` to avoid false matches. The attribute override takes highest priority per R-002 and data-model.md rule 8.

---

### Subtask T003 -- Update `src/Frank.Statecharts/Frank.Statecharts.fsproj`

**Purpose**: Wire Smcat source files into the project's compile order so they are built as part of the library.

**Steps**:

1. Open `src/Frank.Statecharts/Frank.Statecharts.fsproj`.

2. Add Smcat `<Compile Include>` entries **after** the WSD entries and **before** `<Compile Include="Types.fs" />` (the runtime types). The F# compiler requires dependency order.

3. Add these entries:
   ```xml
   <!-- smcat parser and generator -->
   <Compile Include="Smcat/Types.fs" />
   <Compile Include="Smcat/Lexer.fs" />
   <Compile Include="Smcat/LabelParser.fs" />
   <Compile Include="Smcat/Parser.fs" />
   <Compile Include="Smcat/Generator.fs" />
   ```

4. **For this WP**: Only `Smcat/Types.fs` will have real content. The other files should be created as minimal stubs (just the module declaration) so the build succeeds:
   ```fsharp
   module internal Frank.Statecharts.Smcat.Lexer
   // Implementation in WP02
   ```
   Similarly for LabelParser.fs, Parser.fs, Generator.fs.

**Files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj` (edit), `src/Frank.Statecharts/Smcat/Lexer.fs` (stub), `src/Frank.Statecharts/Smcat/LabelParser.fs` (stub), `src/Frank.Statecharts/Smcat/Parser.fs` (stub), `src/Frank.Statecharts/Smcat/Generator.fs` (stub)

**Notes**: See quickstart.md for the exact .fsproj ordering reference.

---

### Subtask T004 -- Create test directory and update test .fsproj

**Purpose**: Set up the test project structure so downstream WPs can add test files without .fsproj modifications.

**Steps**:

1. Create directory `test/Frank.Statecharts.Tests/Smcat/` if it does not exist.

2. Open `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`.

3. Add Smcat test file `<Compile Include>` entries **after** the existing WSD test entries and **before** `<Compile Include="StatechartETagProviderTests.fs" />`:
   ```xml
   <!-- smcat tests -->
   <Compile Include="Smcat/LexerTests.fs" />
   <Compile Include="Smcat/LabelParserTests.fs" />
   <Compile Include="Smcat/ParserTests.fs" />
   <Compile Include="Smcat/ErrorTests.fs" />
   <Compile Include="Smcat/GeneratorTests.fs" />
   <Compile Include="Smcat/RoundTripTests.fs" />
   ```

4. Create minimal stub test files for each so the build succeeds:
   ```fsharp
   module Smcat.LexerTests

   open Expecto

   [<Tests>]
   let tests = testList "Smcat.Lexer" []
   ```
   Repeat for LabelParserTests, ParserTests, ErrorTests, GeneratorTests, RoundTripTests (adjusting module name and test list name).

**Files**: `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` (edit), `test/Frank.Statecharts.Tests/Smcat/*.fs` (6 stub files)

**Notes**: Follow the Expecto pattern from WSD tests (e.g., `Wsd/GuardParserTests.fs`). Each file needs `module Smcat.XxxTests`, `open Expecto`, and a `[<Tests>]` let binding.

---

### Subtask T005 -- Verify multi-target build succeeds

**Purpose**: Confirm that all new files compile successfully under all three target frameworks.

**Steps**:

1. Run: `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj`
   - This must succeed for net8.0, net9.0, and net10.0.

2. Run: `dotnet build test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
   - This must succeed for net10.0.

3. Run: `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
   - All existing tests must still pass (no regressions).
   - New stub tests should pass (empty test lists).

4. Fix any compilation errors from type definitions, module declarations, or .fsproj ordering issues.

**Files**: No new files -- validation step only.

**Notes**: Common issues: F# compile order errors (file A references type from file B but B appears later in .fsproj), missing `and` keyword for mutually recursive types, struct attribute on wrong type.

---

## Risks & Mitigations

- **Mutually recursive types**: `SmcatState`, `SmcatTransition`, `SmcatElement`, and `SmcatDocument` must be in one `type ... and ... and ... and ...` block. If the compiler complains about forward references, ensure all four are connected with `and`.
- **Compile order**: Smcat/Types.fs must appear before Smcat/Lexer.fs in the .fsproj. The runtime Types.fs must appear after all Smcat files.
- **.fsproj merge conflicts**: Other specs may modify the same .fsproj. Keep changes minimal and well-commented.

## Review Guidance

- Verify all types match `data-model.md` exactly (field names, types, optionality)
- Verify `inferStateType` implements all 9 detection rules in correct priority order
- Verify .fsproj compile order: Wsd/* -> Smcat/* -> Types.fs -> Store.fs -> ...
- Verify `dotnet build` and `dotnet test` both pass
- Check that mutually recursive types use `and` keyword correctly

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:14Z -- system -- lane=planned -- Prompt created.

---

### Updating Lane Status

To change a work package's lane, either:
1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP01 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
- 2026-03-16T04:02:21Z – claude-opus – shell_pid=97851 – lane=doing – Assigned agent via workflow command
- 2026-03-16T04:15:11Z – claude-opus – shell_pid=97851 – lane=for_review – Moved to for_review
- 2026-03-16T04:18:56Z – claude-opus – shell_pid=97851 – lane=done – Moved to done
- 2026-03-16T14:33:10Z – claude-opus – shell_pid=97851 – lane=done – Moved to done
