---
work_package_id: "WP04"
title: "Guard extension parser"
lane: "done"
dependencies: ["WP01"]
requirement_refs: ["FR-004"]
subtasks: ["T024", "T025", "T026", "T027"]
agent: "claude-opus-reviewer"
shell_pid: "41672"
reviewed_by: "Ryan Riley"
review_status: "approved"
history:
  - timestamp: "2026-03-07T00:00:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# WP04: Guard Extension Parser

## Implementation Command

```
spec-kitty implement WP04 --base WP01
```

## Objectives

Implement the guard annotation parser that extracts `[guard: key=value, ...]` syntax from note content strings. This is a standalone module (`GuardParser.fs`) that operates on plain strings (not tokens), making it independently testable and parallelizable with the lexer (WP02).

The guard parser is called from the main parser's note-parsing path (WP03 integration happens when both WPs merge). It takes a note's content string and source position, and returns an optional `GuardAnnotation` plus the remaining non-guard content text.

**Output file**: `src/Frank.Statecharts/Wsd/GuardParser.fs`
**Test file**: `test/Frank.Statecharts.Tests/Wsd/GuardParserTests.fs`
**Module**: `module internal Frank.Statecharts.Wsd.GuardParser`
**Public API**: `val tryParseGuard: content: string -> position: SourcePosition -> (GuardAnnotation option * string * ParseFailure list * ParseWarning list)`

Note: The return type is expanded from data-model.md to include failures and warnings, since guard parsing can produce its own diagnostics.

## Success Criteria

- `[guard: role=PlayerX]` extracts one key-value pair correctly
- `[guard: state=XTurn, role=PlayerX]` extracts two key-value pairs in order
- `[guard: auth=bearer, scope=write]` works with arbitrary key-value names
- Mixed content `[guard: role=admin] Must be admin` extracts guard AND preserves remaining text
- Regular notes without `[guard:` return `None` with full content preserved
- Malformed guard syntax produces appropriate ParseFailure with position information
- Empty guard `[guard: ]` produces a ParseWarning
- Empty value `[guard: key=]` produces a ParseWarning (valid but suspicious)
- All tests pass under multi-target build

## Context & Constraints

- **Depends on**: WP01 (Types.fs for GuardAnnotation, ParseFailure, ParseWarning, SourcePosition)
- **Does NOT depend on**: WP02 (Lexer) or WP03 (Parser). This module operates on plain strings, not tokens.
- **Integration point**: WP03's note parsing path will call `tryParseGuard` when processing `note over` elements. The integration code is written when WP03 and WP04 merge (or in WP06/WP07).
- **Guard scope**: Guards are only semantically meaningful on `note over` elements (per research.md). The GuardParser itself doesn't enforce this — it just parses strings. The calling code in Parser.fs decides when to invoke it.
- **No regex**: Use manual string scanning for consistency with the lexer's approach and to avoid regex compilation overhead.
- **Position tracking**: The `position` parameter is the source position of the note content start (after the colon). Guard error positions should be offsets from this base position.

## Subtasks & Detailed Guidance

### T024: Bracket Detection + Key-Value Pair Extraction

The core parsing logic: detect `[guard:` at the start of content, extract key-value pairs, find the closing `]`.

**Algorithm**:
1. Trim leading whitespace from content
2. Check if content starts with `[guard:` (case-insensitive on `guard`)
3. If not, return `(None, content, [], [])` — no guard present
4. Find the closing `]` bracket
5. Extract the text between `[guard:` and `]`
6. Split on `,` to get individual pairs
7. For each pair, split on `=` to get key and value
8. Trim whitespace from keys and values
9. Build the `GuardAnnotation` record

**Implementation sketch**:
```fsharp
let tryParseGuard (content: string) (position: SourcePosition) =
    let trimmed = content.TrimStart()
    let offset = content.Length - trimmed.Length  // leading whitespace chars

    if not (trimmed.StartsWith("[guard:", StringComparison.OrdinalIgnoreCase)) then
        (None, content, [], [])
    else
        let guardStart = 7  // length of "[guard:"
        match trimmed.IndexOf(']', guardStart) with
        | -1 ->
            // Unclosed bracket — error
            let errorPos = { Line = position.Line; Column = position.Column + offset }
            let failure = {
                Position = errorPos
                Description = "Unclosed guard annotation bracket"
                Expected = "closing ']'"
                Found = "end of line"
                CorrectiveExample = "[guard: role=PlayerX]"
            }
            (None, content, [failure], [])
        | closingIdx ->
            let guardText = trimmed.Substring(guardStart, closingIdx - guardStart).Trim()
            let remaining = trimmed.Substring(closingIdx + 1).Trim()
            // Parse key-value pairs from guardText
            ...
```

**Key-value extraction**:
- Split `guardText` on `,`
- For each segment:
  - Trim whitespace
  - Skip empty segments (trailing comma)
  - Find `=` character
  - If no `=`: produce ParseFailure "missing '=' in guard pair"
  - If `=` at start (empty key): produce ParseFailure "empty key in guard pair"
  - If `=` at end (empty value): produce ParseWarning "empty value in guard pair" but still include the pair
  - Otherwise: extract `(key.Trim(), value.Trim())`

**Tests**:
- `[guard: role=PlayerX]` → `Some { Pairs = [("role", "PlayerX")] }`, remaining = `""`
- `[guard: state=XTurn, role=PlayerX]` → pairs in order, remaining = `""`
- `[guard: a=1, b=2, c=3]` → three pairs in order
- `This is just a note` → `None`, remaining = `"This is just a note"`
- `[not a guard]` → `None`, remaining = `"[not a guard]"`

### T025: Mixed Content Handling

Handle notes that contain both a guard annotation and descriptive text.

**Pattern**: `[guard: key=value] Additional descriptive text here`

**Implementation**: After extracting the guard and closing bracket, the remaining text (after `]`) is the note's descriptive content. Trim leading/trailing whitespace from the remaining text.

**Examples**:
- Input: `[guard: role=admin] Must be authenticated admin`
  - Guard: `Some { Pairs = [("role", "admin")] }`
  - Remaining: `"Must be authenticated admin"`
- Input: `[guard: state=XTurn] `
  - Guard: `Some { Pairs = [("state", "XTurn")] }`
  - Remaining: `""` (empty after trim)
- Input: `  [guard: role=PlayerX]  extra text  `
  - Guard extracted (leading whitespace ignored)
  - Remaining: `"extra text"` (trimmed)

**Edge case**: Guard annotation NOT at the start of content:
- Input: `Some text [guard: role=admin]`
- This should NOT be recognized as a guard. Guards must be at the start (after optional whitespace) of the note content. Return `None` with full content preserved.
- Rationale: requiring the guard at the start avoids ambiguity with square brackets used in normal text.

**Tests**:
- Guard at start with trailing text
- Guard at start with only whitespace after
- Guard not at start (embedded in text) — not recognized
- Multiple `[guard:]` in content — only first is extracted (subsequent are part of remaining text)

### T026: Error Cases

Handle all malformed guard syntax gracefully, producing structured diagnostics.

**Error cases from research.md**:

1. **Unclosed bracket**: `[guard: malformed` — no closing `]`
   - Produce `ParseFailure` with description "Unclosed guard annotation bracket"
   - Expected: "closing ']'"
   - Found: "end of line"
   - Corrective example: `[guard: role=PlayerX]`
   - Return `(None, content, [failure], [])` — do not extract a partial guard

2. **Empty key**: `[guard: =value]` — `=` at start of pair
   - Produce `ParseFailure` with description "Empty key in guard annotation"
   - Expected: "key name before '='"
   - Found: "'=value'"
   - Corrective example: `[guard: role=PlayerX]`

3. **Missing equals**: `[guard: key]` — pair without `=`
   - Produce `ParseFailure` with description "Missing '=' in guard pair"
   - Expected: "key=value"
   - Found: "'key'"
   - Corrective example: `[guard: role=PlayerX]`

4. **Empty value**: `[guard: key=]` — `=` at end
   - Produce `ParseWarning` (not error — structurally valid, just suspicious)
   - Description: "Empty value in guard annotation for key 'key'"
   - Suggestion: "Provide a value: [guard: key=someValue]"
   - Still include the pair `("key", "")` in the guard annotation

5. **Empty guard**: `[guard: ]` — no pairs at all
   - Produce `ParseWarning`
   - Description: "Empty guard annotation"
   - Suggestion: "Add key-value pairs: [guard: role=PlayerX]"
   - Return `Some { Pairs = [] }` (structurally valid empty guard)

6. **Multiple errors**: `[guard: =bad, key, good=ok]`
   - Collect all errors from all pairs
   - Still extract successfully parsed pairs (`good=ok`)
   - Return partial guard with collected diagnostics

**Implementation**: The key-value parsing loop should collect errors as it goes, not abort on first error. Build a list of successfully parsed pairs alongside a list of failures/warnings.

**Tests**: One test per error case above, plus a test with multiple errors in one guard annotation.

### T027: Guard Parser Tests

Comprehensive test suite for the guard parser. Use Expecto.

**Test file**: `test/Frank.Statecharts.Tests/Wsd/GuardParserTests.fs`

**Test categories**:

1. **No guard present**: plain text, text with unrelated brackets, empty string
2. **Simple guards**: single pair, multiple pairs, various key-value names
3. **Mixed content**: guard + text, guard + whitespace only, text + guard (not recognized)
4. **Whitespace handling**: spaces around keys/values, leading/trailing whitespace in content
5. **Error cases**: unclosed bracket, empty key, missing equals, empty value, empty guard
6. **Multiple errors**: guard with several malformed pairs
7. **Case insensitivity**: `[Guard:`, `[GUARD:`, `[guard:` all recognized
8. **Acceptance scenarios from spec.md US2**:
   - US2-S1: `[guard: role=PlayerX]` → single pair
   - US2-S2: `[guard: state=XTurn, role=PlayerX]` → two pairs
   - US2-S3: `This is a regular note` → no guard
   - US2-S4: `[guard: malformed` → failure with position

**Example test**:
```fsharp
testCase "US2-S1: single guard pair" <| fun _ ->
    let pos = { Line = 5; Column = 20 }
    let (guard, remaining, errors, warnings) =
        GuardParser.tryParseGuard "[guard: role=PlayerX]" pos
    Expect.isSome guard "guard found"
    Expect.equal guard.Value.Pairs [("role", "PlayerX")] "one pair"
    Expect.equal remaining "" "no remaining text"
    Expect.isEmpty errors "no errors"
    Expect.isEmpty warnings "no warnings"
```

Write at least 20 test cases covering all categories.

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Position offset calculation incorrect for nested content | Use the base `position` parameter plus character offset. Test with multi-column inputs. |
| Guard syntax conflicts with regular bracket usage in notes | Guard must start with `[guard:` — the specific prefix avoids conflicts with general brackets |
| Integration with Parser.fs may require signature changes | Keep the return type rich (guard + remaining + errors + warnings) so the parser can simply destructure |
| Empty string edge case | Test explicitly: `tryParseGuard "" pos` should return `(None, "", [], [])` |

## Review Guidance

- Verify guard extraction handles ALL error cases from research.md
- Verify mixed content returns correct remaining text (trimmed)
- Verify case-insensitive matching of `[guard:`
- Verify position offsets in error reports are accurate
- Verify the `.fsproj` includes `Wsd/GuardParser.fs` after `Wsd/Types.fs` and before `Wsd/Parser.fs`
- Run `dotnet build` and `dotnet test`

## Activity Log

| Timestamp | Agent | Action |
|-----------|-------|--------|
| 2026-03-07T00:00:00Z | system | Prompt generated via /spec-kitty.tasks |
- 2026-03-15T19:20:55Z – unknown – lane=for_review – Moved to for_review
- 2026-03-15T19:43:38Z – claude-opus-reviewer – shell_pid=41672 – lane=doing – Started review via workflow command
- 2026-03-15T19:44:21Z – claude-opus-reviewer – shell_pid=41672 – lane=done – Review passed: All 4 subtasks (T024-T027) implemented. Guard parser handles bracket detection, key-value extraction, mixed content, case-insensitive prefix. Error cases: unclosed bracket, missing =, empty key, empty guard. 21 tests covering all scenarios. Uses ResizeArray (constitution V fix). Builds clean, 105 tests pass.
