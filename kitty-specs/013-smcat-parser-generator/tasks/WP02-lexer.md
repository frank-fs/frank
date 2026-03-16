---
work_package_id: "WP02"
subtasks:
  - "T006"
  - "T007"
  - "T008"
  - "T009"
  - "T010"
  - "T011"
title: "Lexer"
phase: "Phase 0 - Foundation"
lane: "doing"
assignee: ""
agent: "claude-opus-4-6"
shell_pid: "12561"
review_status: ""
reviewed_by: ""
dependencies: ["WP01"]
requirement_refs: ["FR-001", "FR-012", "FR-013", "FR-014"]
history:
  - timestamp: "2026-03-15T23:59:14Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 -- Lexer

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP02 --base WP01
```

Depends on WP01 (Types.fs must exist).

---

## Objectives & Success Criteria

- Implement `src/Frank.Statecharts/Smcat/Lexer.fs` -- a tokenizer that converts raw smcat text into a flat `Token list`
- Handle all smcat syntax elements: identifiers, quoted strings, arrows, punctuation, comments, activities, attributes, composite braces
- Handle both `\r\n` and `\n` line endings (FR-013)
- Create comprehensive lexer tests in `test/Frank.Statecharts.Tests/Smcat/LexerTests.fs`

**Done when**: `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Smcat.Lexer"` passes with all token types covered.

## Context & Constraints

- **Spec**: FR-001 (tokenize all smcat syntax elements), FR-012 (ignore comments), FR-013 (line endings), FR-014 (semicolons and commas)
- **Data Model**: `data-model.md` -- TokenKind DU and Token struct definitions
- **API Signatures**: `contracts/api-signatures.md` -- `val tokenize : source:string -> Token list`
- **WSD Lexer Pattern**: `src/Frank.Statecharts/Wsd/Lexer.fs` -- follow the mutable scanning pattern exactly
- **Performance**: SC-008 -- no intermediate string concatenation in hot paths; use `source.Substring(start, len)` or spans

**Key constraints**:
- Module declaration: `module internal Frank.Statecharts.Smcat.Lexer`
- Mutable state pattern with `pos`, `line`, `col` variables
- `ResizeArray<Token>` for accumulation, convert to list at end
- All tokens carry `SourcePosition` for error reporting in downstream parser

## Subtasks & Detailed Guidance

### Subtask T006 -- Create `src/Frank.Statecharts/Smcat/Lexer.fs` with scanning infrastructure

**Purpose**: Establish the lexer skeleton with mutable state and helper functions, matching the WSD Lexer pattern.

**Steps**:

1. Replace the stub in `src/Frank.Statecharts/Smcat/Lexer.fs` with the full module:
   ```fsharp
   module internal Frank.Statecharts.Smcat.Lexer

   open Frank.Statecharts.Smcat.Types
   ```

2. Define the `tokenize` function with mutable state:
   ```fsharp
   let tokenize (source: string) : Token list =
       let len = source.Length
       let mutable pos = 0
       let mutable line = 1
       let mutable col = 1
       let tokens = ResizeArray<Token>()

       let inline peek () = if pos < len then source[pos] else '\000'
       let inline peekAt i = if i < len then source[i] else '\000'

       let inline advance () =
           pos <- pos + 1
           col <- col + 1

       let inline newline () =
           line <- line + 1
           col <- 1

       let inline makeToken kind l c =
           { Kind = kind; Position = { Line = l; Column = c } }
       // ... main loop ...
       tokens |> Seq.toList
   ```

3. Implement `skipWhitespace` helper (skip spaces and tabs, NOT newlines):
   ```fsharp
   let skipWhitespace () =
       while pos < len && (source[pos] = ' ' || source[pos] = '\t') do
           advance ()
   ```

4. Implement the main scanning `while` loop that dispatches on `peek()` to the appropriate tokenization logic (implemented in T007-T010).

5. At end of input, emit `Eof` token.

**Files**: `src/Frank.Statecharts/Smcat/Lexer.fs` (~250-300 lines total when complete)

---

### Subtask T007 -- Implement identifier and quoted string tokenization

**Purpose**: Handle the two content token types: plain identifiers (including dot-separated like `deep.history`) and quoted strings (like `"a state"`).

**Steps**:

1. **Identifier scanning**: When `peek()` is a letter, underscore, or digit-starting-identifier:
   - Read alphanumeric, underscore, dot, and hyphen characters
   - Stop before arrow sequences: if current char is `=` and next is `>`, stop
   - Trim trailing dots/hyphens if any
   - Emit `Identifier of string`

   ```fsharp
   let isIdentStartChar c = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c = '_'
   let isIdentChar c = isIdentStartChar c || (c >= '0' && c <= '9') || c = '.' || c = '-'
   ```

   **Edge cases**:
   - `deep.history` is a single identifier (dot allowed)
   - `state-name` is a single identifier (hyphen allowed in middle)
   - `a=>b` should tokenize as `Identifier "a"`, `TransitionArrow`, `Identifier "b"` (stop at `=`)

2. **Quoted string scanning**: When `peek()` is `"`:
   - Advance past opening quote
   - Read all characters until closing `"`
   - Handle escaped quotes `\"` inside strings
   - Emit `QuotedString of string` (content without quotes)
   - If EOF before closing quote, emit the partial string (parser will report the error)

   **Edge cases**:
   - Empty quoted string `""` -> `QuotedString ""`
   - Unicode characters inside quotes are preserved as-is
   - Newlines inside quoted strings: preserve them (smcat allows multiline quoted names)

**Files**: `src/Frank.Statecharts/Smcat/Lexer.fs`

---

### Subtask T008 -- Implement punctuation and arrow tokenization

**Purpose**: Handle all single-character and multi-character punctuation tokens.

**Steps**:

1. **Transition arrow** `=>`: When `peek()` is `=` and `peekAt(pos+1)` is `>`:
   - Record position, advance twice, emit `TransitionArrow`

2. **Equals sign** `=`: When `peek()` is `=` but next is NOT `>`:
   - Emit `Equals` (used in attributes: `key=value`)

3. **Single-character punctuation** -- dispatch table:
   | Character | TokenKind |
   |-----------|-----------|
   | `:` | `Colon` |
   | `;` | `Semicolon` |
   | `,` | `Comma` |
   | `[` | `LeftBracket` |
   | `{` | `LeftBrace` |
   | `}` | `RightBrace` |

4. **Right bracket** `]`: Context-dependent handling:
   - If at statement start (the only tokens before this on the current logical statement are `Newline` or this is the first token, or the previous non-whitespace token is `;`, `,`, `{`, `}`, or `Newline`): Emit `CloseBracketPrefix` (fork/join pseudo-state prefix)
   - Otherwise: Emit `RightBracket` (attribute closing bracket)
   - **Simplification for initial implementation**: Always emit `RightBracket`. The parser can handle disambiguation based on syntactic context. Only emit `CloseBracketPrefix` if the `]` appears at the very start of input or immediately after a statement terminator.

5. **Caret** `^`: Emit `Caret` (choice pseudo-state prefix)

6. **Forward slash** `/`: Emit `ForwardSlash` (used in transition labels for action separator)
   - **But**: If preceded by `entry` or `exit` identifier, those are handled in T010 as combined tokens.

**Files**: `src/Frank.Statecharts/Smcat/Lexer.fs`

---

### Subtask T009 -- Implement comment/whitespace/newline handling

**Purpose**: Handle comment lines, whitespace, and line endings per FR-012 and FR-013.

**Steps**:

1. **Comment lines**: When `peek()` is `#`:
   - Skip all characters until newline or EOF
   - Do NOT emit a token (comments are discarded at the lexer level)
   - When the newline is reached, process it as a newline (emit `Newline` and update line/col)

2. **Newline handling**:
   - `\r\n` (Windows): Advance past both characters, call `newline()`, emit `Newline`
   - `\n` (Unix): Advance past `\n`, call `newline()`, emit `Newline`
   - `\r` alone (old Mac): Treat as newline for robustness

3. **Whitespace**: Handled by `skipWhitespace()` at the top of the main loop -- spaces and tabs are consumed without emitting tokens.

4. **Consecutive newlines**: Each newline emits its own `Newline` token. The parser will skip consecutive newlines.

**Files**: `src/Frank.Statecharts/Smcat/Lexer.fs`

**Notes**: Comments starting with `#` should be fully consumed (not just skipped line-by-line). A `#` mid-line is NOT a comment start (smcat comments must start at the beginning of a line or after whitespace).

---

### Subtask T010 -- Implement activity keyword detection and prefix tokens

**Purpose**: Handle the special `entry/`, `exit/`, and `...` tokens used in state activity declarations.

**Steps**:

1. **Activity keywords**: After reading an identifier, check if it matches `"entry"` or `"exit"` AND the next character is `/`:
   - If `"entry"` and next is `/`: Instead of emitting `Identifier "entry"` + `ForwardSlash`, emit single `EntrySlash` token. Advance past the `/`.
   - If `"exit"` and next is `/`: Emit `ExitSlash`. Advance past the `/`.
   - Otherwise: Emit the identifier normally.

   **Implementation approach**: In the identifier scanning code (T007), after reading the identifier text, check:
   ```fsharp
   let identText = source.Substring(start, pos - start)
   if (identText = "entry" || identText = "exit") && pos < len && source[pos] = '/' then
       advance () // consume the '/'
       if identText = "entry" then tokens.Add(makeToken EntrySlash startLine startCol)
       else tokens.Add(makeToken ExitSlash startLine startCol)
   else
       tokens.Add(makeToken (Identifier identText) startLine startCol)
   ```

2. **Ellipsis** `...`: When `peek()` is `.` and `peekAt(pos+1)` is `.` and `peekAt(pos+2)` is `.`:
   - Record position, advance three times, emit `Ellipsis`
   - If only one or two dots, treat as part of an identifier (dots are valid in identifiers like `deep.history`)

**Files**: `src/Frank.Statecharts/Smcat/Lexer.fs`

**Notes**: The activity detection must happen during identifier scanning, not as a post-processing step, to avoid splitting `entry/` into two tokens that the parser would need to recombine.

---

### Subtask T011 -- Create `test/Frank.Statecharts.Tests/Smcat/LexerTests.fs`

**Purpose**: Comprehensive tests for the lexer covering all token types, edge cases, and line ending handling.

**Steps**:

1. Replace the stub in `test/Frank.Statecharts.Tests/Smcat/LexerTests.fs`.

2. Create a helper function to extract token kinds (stripping positions):
   ```fsharp
   let tokenKinds (source: string) =
       Lexer.tokenize source
       |> List.map (fun t -> t.Kind)

   let tokenKindsNoEof (source: string) =
       tokenKinds source |> List.filter (fun k -> k <> Eof)
   ```

3. Write test cases for each token type category:

   **Basic tokens**:
   - `"a => b"` -> `[Identifier "a"; TransitionArrow; Identifier "b"; Eof]`
   - `"state1, state2;"` -> `[Identifier "state1"; Comma; Identifier "state2"; Semicolon; Eof]`
   - `"a => b: event;"` -> `[Identifier "a"; TransitionArrow; Identifier "b"; Colon; Identifier "event"; Semicolon; Eof]`

   **Quoted strings**:
   - `"\"hello world\""` -> contains `QuotedString "hello world"`
   - Empty quoted string `"\"\""` -> contains `QuotedString ""`

   **Activities**:
   - `"entry/ start"` -> contains `EntrySlash`
   - `"exit/ stop"` -> contains `ExitSlash`
   - `"..."` -> contains `Ellipsis`

   **Comments**:
   - `"# comment\na => b"` -> should NOT contain any comment token; first meaningful token is `Identifier "a"`

   **Attributes**:
   - `"[color=\"red\"]"` -> contains `LeftBracket`, `Identifier "color"`, `Equals`, `QuotedString "red"`, `RightBracket`

   **Composite states**:
   - `"a { b => c; }"` -> contains `LeftBrace` and `RightBrace`

   **Line endings**:
   - Verify `\r\n` and `\n` both produce `Newline` tokens
   - Verify line/column tracking is correct after newlines

   **Pseudo-state prefixes**:
   - `"^choice"` -> contains `Caret`, `Identifier "choice"`

   **Position tracking**:
   - Verify that tokens on line 2 have `Position.Line = 2`
   - Verify column positions are correct

   **Edge cases**:
   - Empty input `""` -> `[Eof]`
   - Whitespace only `"   "` -> `[Eof]`
   - `"deep.history"` -> single `Identifier "deep.history"`

4. Use Expecto `testList` and `testCase` pattern (matching WSD test style).

**Files**: `test/Frank.Statecharts.Tests/Smcat/LexerTests.fs` (~200-250 lines)

**Parallel?**: Yes -- can be written in parallel with T006-T010 if Token type contract is stable.

---

## Test Strategy

Run lexer tests:
```bash
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Smcat.Lexer"
```

Verify all existing tests still pass:
```bash
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj
```

## Risks & Mitigations

- **Context-dependent `]`**: Simplified to always emit `RightBracket` except at statement start. If this causes parser issues, the parser can handle disambiguation.
- **`entry/` vs `entry` + `/`**: The lookahead in identifier scanning must be precise. Test with `"entry/"`, `"entry /` (space before slash), and `"entry"` (no slash).
- **Arrow in identifier**: `a=>b` must not scan `a=` as an identifier. Stop identifier scanning when `=` followed by `>` is detected.
- **Performance**: Use `source.Substring(start, pos - start)` for identifier extraction, not string concatenation in a loop.

## Review Guidance

- Verify all TokenKind cases from Types.fs are produced by the lexer
- Verify `entry/` and `exit/` are detected as single tokens
- Verify `\r\n` produces one Newline (not two)
- Verify position tracking: line increments on newline, column resets to 1
- Verify comments are fully consumed (not partially tokenized)
- Check that `dotnet test --filter "Smcat.Lexer"` passes

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:14Z -- system -- lane=planned -- Prompt created.

---

### Updating Lane Status

To change a work package's lane, either:
1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP02 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
- 2026-03-16T04:28:05Z – unknown – lane=for_review – Ready for review: smcat lexer with 52 passing tests, all token types covered
- 2026-03-16T04:28:45Z – claude-opus-4-6 – shell_pid=12561 – lane=doing – Started review via workflow command
