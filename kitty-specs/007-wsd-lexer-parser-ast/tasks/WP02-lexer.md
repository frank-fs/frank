---
work_package_id: "WP02"
title: "Lexer (tokenizer)"
lane: "planned"
dependencies: ["WP01"]
requirement_refs: ["FR-001", "FR-003", "FR-010", "FR-011"]
subtasks: ["T012", "T013", "T014", "T015", "T016", "T017"]
history:
  - timestamp: "2026-03-07T00:00:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# WP02: Lexer (Tokenizer)

## Implementation Command

```
spec-kitty implement WP02 --base WP01
```

## Objectives

Implement the WSD lexer that converts a raw WSD source string into a flat list of `Token` values. The lexer handles line ending normalization, comment stripping, keyword recognition, arrow tokenization, punctuation, identifiers, string literals, and free-form text content. Every token carries an accurate `SourcePosition`.

**Output file**: `src/Frank.Statecharts/Wsd/Lexer.fs`
**Test file**: `test/Frank.Statecharts.Tests/Wsd/LexerTests.fs`
**Module**: `module internal Frank.Statecharts.Wsd.Lexer`
**Public API**: `val tokenize: source: string -> Token list`

## Success Criteria

- `tokenize` produces correct token sequences for all WSD constructs listed in research.md
- All four arrow forms are recognized with longest-match semantics (`-->-` before `-->` before `->`)
- Keywords are case-insensitive (`PARTICIPANT`, `Participant`, `participant` all produce `TokenKind.Participant`)
- Multi-word keywords (`left of`, `right of`) are recognized as single tokens
- Comment lines (starting with `#`) produce no tokens (stripped entirely)
- Blank lines produce `Newline` tokens (parser uses newlines as statement terminators)
- Source positions are 1-based and accurate for every token
- Windows (`\r\n`) and Unix (`\n`) line endings both work correctly
- No intermediate string allocations in the hot tokenization loop (use slicing/spans where possible)
- All tests pass under multi-target build

## Context & Constraints

- **Depends on**: WP01 (Types.fs must exist with Token, TokenKind, SourcePosition)
- **Performance target**: SC-007 requires handling 1000-line inputs without allocation pressure. Avoid `string.Substring` in tight loops; prefer index-based scanning.
- **Two-word keywords**: `left of` and `right of` require lookahead after `left`/`right`. If `of` doesn't follow, treat `left`/`right` as identifiers.
- **Text content**: After a colon (`:`) in message or note context, the rest of the line is free-form text. The lexer emits this as a single `TextContent` token. The parser (and guard parser) handle further decomposition.
- **Arrow ambiguity**: `-->-` is a prefix of no other arrow; `-->` is a prefix of `-->-`; `->` is a prefix of `->-`. Longest match resolves: scan the full arrow before deciding which TokenKind to emit.
- **String literals**: Quoted with double quotes. Support escaped quotes (`\"`) inside. Emit `StringLiteral` with quotes stripped.
- **Identifiers**: Unquoted sequences of alphanumeric characters plus underscore and hyphen. Used for participant names and parameter names.

## Subtasks & Detailed Guidance

### T012: Line Ending Normalization + Comment/Blank Line Handling

The lexer's first responsibility is to handle input normalization so the rest of the tokenization logic only deals with `\n`.

**Implementation approach**:
- Normalize `\r\n` to `\n` at the start of tokenization (single pass through the input string, or handle `\r` during character-by-character scanning)
- Alternative: handle `\r` inline during scanning — when encountering `\r`, check if next char is `\n` and consume both, treating as a single newline. This avoids a preprocessing pass.
- Comment detection: when the first non-whitespace character on a line is `#`, skip all characters until the next newline. Do NOT emit any tokens for the comment line. Still emit a `Newline` token at the end.
- Blank lines (only whitespace before newline): emit a `Newline` token. The parser will skip consecutive newlines.
- Track line numbers accurately across both `\r\n` and `\n` sequences.

**Tests to write**:
- Input with `\r\n` line endings produces same tokens as `\n` version
- Comment line `# this is a comment` produces only `Newline`
- Mixed comments and code: tokens from code lines are correct, comment lines produce nothing
- Multiple consecutive blank lines produce multiple `Newline` tokens
- Comment at end of file (no trailing newline) produces no tokens beyond the comment

**Edge cases**:
- `\r` without following `\n` (old Mac line endings) — treat as newline
- File ending without final newline — still produce `Eof` token
- Comment line with only `#` and nothing else

### T013: Keyword Tokenization

Recognize all 17 WSD keywords and emit the corresponding `TokenKind` case.

**Keywords**: `participant`, `title`, `autonumber`, `note`, `over`, `left of`, `right of`, `alt`, `opt`, `loop`, `par`, `break`, `critical`, `ref`, `else`, `end`, `as`

**Implementation approach**:
- After scanning an alphabetic word (identifier scan), check if the word matches a keyword (case-insensitive comparison)
- Use a lookup table or match expression for keyword recognition. A `Map<string, TokenKind>` with lowercased keys is clean and fast enough for 17 entries.
- **Multi-word keywords** (`left of`, `right of`): after matching `left` or `right`, look ahead for whitespace followed by `of`. If found, consume all three parts and emit `LeftOf`/`RightOf`. If `of` is NOT found, emit `Identifier "left"` or `Identifier "right"`.
- `autonumber` is a single keyword (no space variant).

**Tests to write**:
- Each keyword in lowercase produces the correct TokenKind
- Each keyword in mixed case (e.g., `Participant`, `TITLE`) produces the correct TokenKind
- `left of` produces `LeftOf`; bare `left` (not followed by `of`) produces `Identifier "left"`
- `right of` produces `RightOf`; bare `right` produces `Identifier "right"`
- Keywords followed by identifiers: `participant Client` produces `[Participant; Identifier "Client"]`

**Edge cases**:
- `leftof` (no space) is an identifier, not `LeftOf`
- `left  of` (multiple spaces) — decide: treat as `LeftOf` (normalize whitespace) or not. Recommendation: require exactly `left` + whitespace + `of` with any amount of whitespace between.
- A word that starts like a keyword but is longer: `participants` is `Identifier "participants"`, not `Participant`

### T014: Arrow Tokenization

Recognize all four WSD arrow forms using longest-match semantics.

**Arrows**: `->` (SolidArrow), `-->` (DashedArrow), `->-` (SolidDeactivate), `-->-` (DashedDeactivate)

**Implementation approach**:
- When encountering `-`, look ahead:
  - `-` then `>` then `-` → `SolidDeactivate` (`->-`), consume 3 chars
  - `-` then `>` → `SolidArrow` (`->`), consume 2 chars... BUT first check for `->-` (3 chars)
  - `-` then `-` then `>` then `-` → `DashedDeactivate` (`-->-`), consume 4 chars
  - `-` then `-` then `>` → `DashedArrow` (`-->`), consume 3 chars... BUT first check for `-->-` (4 chars)
- **Longest match algorithm**: when you see `-`, scan ahead greedily:
  1. If next is `-`: could be `-->` or `-->-`. Check for `->` at position+1: `>` at pos+2. Then check pos+3 for `-`.
  2. If next is `>`: could be `->` or `->-`. Check pos+2 for `-`.
- Simplest correct approach: match on the next 4 characters (padding with EOF):
  - `-->-` → DashedDeactivate (4 chars)
  - `-->`  → DashedArrow (3 chars)
  - `->-`  → SolidDeactivate (3 chars)
  - `->`   → SolidArrow (2 chars)
  - `-` followed by anything else → part of an identifier or error

**Tests to write**:
- Each arrow form in isolation: `->`, `-->`, `->-`, `-->-`
- Arrows embedded in messages: `Client->Server`, `Client-->Server`, `Client->-Server`, `Client-->-Server`
- Arrow at end of line (no receiver yet — lexer just emits the token, parser validates context)
- Unrecognized sequences starting with `-`: e.g., `-x` should produce `Identifier "-x"` or an error token

**Edge cases**:
- `--->` (three dashes) — should tokenize as `DashedArrow` (`-->`) + `SolidArrow` start? No — this is likely an error. Longest match: `-->` then `-` then `>` doesn't form an arrow. Emit `DashedArrow` then handle remaining `-` and `>` as separate tokens or identifier chars.
- Hyphenated identifiers (e.g., `my-service`) — the `-` starts an arrow scan, but `m` doesn't match. Need to handle `-` inside identifiers. Recommendation: `-` at the start of a scan position triggers arrow detection; `-` preceded by alphanumeric is part of an identifier.

### T015: Punctuation, Identifiers, String Literals, Text Content

Handle all remaining token types.

**Punctuation**: Single-character tokens: `:` (Colon), `(` (LeftParen), `)` (RightParen), `,` (Comma), `[` (LeftBracket), `]` (RightBracket), `=` (Equals).

**Identifiers**: Sequences of `[a-zA-Z0-9_]` characters. Also allow hyphens (`-`) WHEN not at the start of the token and not forming an arrow sequence. Participant names like `my-service` are valid identifiers.

Implementation: start scanning when the current character is alphabetic or `_`. Continue while the next character is alphanumeric, `_`, or `-` (but for `-`, peek further to ensure it's not an arrow — if `-` is followed by `>`, stop the identifier before the `-`).

**String literals**: Delimited by double quotes (`"`). Support `\"` escape inside. Strip the outer quotes in the `StringLiteral` payload. Track position as the opening quote.

Implementation: when encountering `"`, scan forward until the matching unescaped `"`. If EOF is reached before closing quote, emit an error token or handle gracefully.

**Text content**: After a colon (`:`) in certain contexts (message labels, note content, title text), the rest of the line is free-form text. The lexer emits this as `TextContent`.

Implementation approach: The lexer does NOT need to know the semantic context. Instead, after emitting a `Colon` token, scan forward past optional whitespace and emit the rest of the line (up to newline or EOF) as a single `TextContent` token. This keeps the lexer context-free — the parser interprets TextContent appropriately.

**Tests to write**:
- Each punctuation character produces the correct token
- Identifiers: `Client`, `my_service`, `API2`, `my-service`
- String literals: `"hello"`, `"with \"escape\""`, `""`
- Text content after colon: `": hello world"` produces `[Colon; TextContent "hello world"]`
- Text content with special characters: `": 201 Created (ok)"`
- Unclosed string literal at end of line/file

### T016: Source Position Tracking

Every token must carry an accurate `SourcePosition` with 1-based line and column.

**Implementation approach**:
- Maintain `mutable line = 1` and `mutable col = 1` as the lexer scans
- Increment `col` for each character consumed
- On newline (`\n` or normalized `\r\n`): increment `line`, reset `col` to 1
- Each token's `Position` is set to `{ Line = line; Column = col }` at the START of the token
- For multi-character tokens (arrows, keywords, identifiers), position is the first character

**Tests to write**:
- Single-line input: verify column numbers increment correctly
- Multi-line input: verify line numbers increment, column resets
- Token after comment line: verify line number accounts for the skipped comment
- Token after blank lines: verify line number accounts for blank lines
- Tabs count as 1 column each (tab is one character advance)
- Windows line endings: `\r\n` is one line advance, not two

**Edge cases**:
- Very long lines (1000+ chars) — columns should still be accurate
- First token on first line has position `{Line=1; Column=1}`
- `Eof` token position is after the last character

### T017: Lexer Tests

Comprehensive test suite for the lexer. Use Expecto test framework (matching existing Frank test patterns).

**Test organization**: `test/Frank.Statecharts.Tests/Wsd/LexerTests.fs`

**Test categories**:
1. **Keyword tests**: every keyword recognized, case insensitivity, multi-word keywords
2. **Arrow tests**: all four forms, longest match, arrows in context
3. **Punctuation tests**: each single-char punctuation
4. **Identifier tests**: simple names, hyphenated, underscored, numeric suffixes
5. **String literal tests**: basic, escaped quotes, empty string, unclosed
6. **Text content tests**: after colon, with special chars, multiword
7. **Comment tests**: full-line comments, comment-only input
8. **Whitespace tests**: blank lines, tabs, mixed whitespace
9. **Line ending tests**: Unix, Windows, mixed
10. **Position tests**: accurate line/column on multi-line inputs
11. **Integration tests**: full WSD snippets tokenized end-to-end

**Example test patterns**:
```fsharp
let tokenKinds source =
    Lexer.tokenize source |> List.map (fun t -> t.Kind)

testCase "solid arrow" <| fun _ ->
    let kinds = tokenKinds "Client->Server: hello"
    Expect.equal kinds
        [Identifier "Client"; SolidArrow; Identifier "Server"; Colon; TextContent "hello"; Newline; Eof]
        "solid arrow message tokens"
```

Write at least 30 test cases covering the categories above. Focus on correctness and edge cases identified in T012-T016.

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Ambiguity between hyphens in identifiers and arrow starts | Peek-ahead logic: `-` followed by `>` or `-` starts arrow scan; `-` preceded by alphanumeric and not followed by `>` continues identifier |
| TextContent swallowing tokens the parser needs | TextContent only emitted after Colon. The parser handles decomposition of guard syntax from TextContent via GuardParser. |
| Performance: string allocations in tight loops | Use index-based scanning on the input string. Create substrings only when emitting token payloads (Identifier, StringLiteral, TextContent). |
| Multi-word keyword lookahead complexity | Keep it simple: after matching `left` or `right`, skip whitespace, check for `of`. If not found, backtrack the position to after `left`/`right`. |

## Review Guidance

- Verify longest-match arrow tokenization with a test for each pair of overlapping arrows
- Verify `left of` / `right of` multi-word handling including edge cases (`leftof`, `left  of`, `left`)
- Verify position accuracy on a multi-line WSD with comments and blank lines
- Verify no `string.Substring` or `string.Concat` in tight scanning loops (check for span/index usage)
- Verify the `.fsproj` includes `Wsd/Lexer.fs` after `Wsd/Types.fs`
- Run `dotnet build` and `dotnet test` across all targets

## Activity Log

| Timestamp | Agent | Action |
|-----------|-------|--------|
| 2026-03-07T00:00:00Z | system | Prompt generated via /spec-kitty.tasks |
