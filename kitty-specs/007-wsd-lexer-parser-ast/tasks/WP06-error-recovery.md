---
work_package_id: WP06
title: Error recovery + failure reports
lane: "doing"
dependencies: [WP03, WP04]
subtasks: [T032, T033, T034, T035, T036, T037, T038]
agent: "claude-opus-reviewer"
shell_pid: "43003"
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-004
- FR-007
- FR-008
- FR-008a
- FR-009
---

# WP06: Error Recovery + Failure Reports

## Implementation Command

```
spec-kitty implement WP06 --base WP03
```

## Objectives

Implement comprehensive error recovery and structured failure reporting in the parser. The parser must continue past errors (collecting diagnostics) rather than aborting on the first failure, and every error must include a corrective example following Amundsen's API design conventions. This WP also integrates the guard parser (WP04) into the note-parsing path.

**Modified files**:
- `src/Frank.Statecharts/Wsd/Parser.fs` — enhance error recovery, integrate GuardParser
- **Test file**: `test/Frank.Statecharts.Tests/Wsd/ErrorTests.fs`

## Success Criteria

- Parser continues past errors, collecting multiple diagnostics (FR-008)
- Partial AST is returned alongside errors (FR-008a)
- Every ParseFailure includes line/column, description, expected/found, and corrective example (FR-007)
- Corrective examples follow Amundsen conventions (valid arrow forms, participant patterns)
- Error limit (configurable, default 50) stops parsing after N errors (FR-008)
- Implicit participants produce warnings (not errors)
- Unsupported WSD constructs (activate/deactivate/destroy/styling) produce warnings and are skipped
- GuardParser is integrated: `note over` elements have their content passed through `tryParseGuard`
- Guard parse errors/warnings are merged into the main ParseResult diagnostics

## Context & Constraints

- **Depends on**: WP03 (Parser.fs core), WP04 (GuardParser.fs)
- **Error recovery philosophy**: Best-effort partial AST (DD-02 in plan.md). Three recovery strategies from research.md: skip-to-newline, skip-to-end, unclosed block recovery.
- **Corrective examples**: Must teach Amundsen conventions. For arrow errors, show all four valid forms. For participant errors, show `participant Name`. For guard errors, show `[guard: key=value]`.
- **Integration**: WP03 created stubs for guard parsing in note handling and basic error recovery. This WP replaces those stubs with full implementations.
- **Out-of-scope constructs**: `activate`, `deactivate`, `destroy`, `box`, `theme`, `skin` — recognized by name, emit warning, skip line.

## Subtasks & Detailed Guidance

### T032: Skip-to-Newline Recovery

Enhance the parser's line-level error recovery to produce structured failure reports and continue parsing.

**Current state (from WP03)**: The parser has a basic `skipToNewline` helper and calls `addError`. This subtask ensures every call site produces a complete `ParseFailure` with all five fields populated.

**Implementation**:
- Review every `addError` call site in Parser.fs
- Ensure each provides:
  - `Position`: accurate line/column of the error
  - `Description`: clear English description of what went wrong
  - `Expected`: what the parser expected to find
  - `Found`: what was actually found (use a `tokenDescription` helper)
  - `CorrectiveExample`: a valid WSD snippet showing the correct syntax

**`tokenDescription` helper**:
```fsharp
let tokenDescription (token: Token) =
    match token.Kind with
    | Identifier s -> sprintf "identifier '%s'" s
    | StringLiteral s -> sprintf "string \"%s\"" s
    | TextContent s -> sprintf "text '%s'" (if s.Length > 20 then s.[..19] + "..." else s)
    | SolidArrow -> "arrow '->'"
    | DashedArrow -> "arrow '-->'"
    | SolidDeactivate -> "arrow '->-'"
    | DashedDeactivate -> "arrow '-->-'"
    | Newline -> "end of line"
    | Eof -> "end of file"
    | kind -> sprintf "'%A'" kind
```

**Error sites to review and enhance**:
1. Unknown token at line start → "Unexpected token at start of line"
2. Missing participant name after `participant` → "Expected participant name"
3. Missing receiver after arrow → "Expected receiver participant name"
4. Missing colon after receiver in message → "Expected ':' after receiver"
5. Unknown note position → "Expected 'over', 'left of', or 'right of'"
6. Missing participant after note position → "Expected participant name"
7. Unrecognized arrow syntax → "Unrecognized arrow syntax" with corrective showing all four forms

**After recording the error**, call `skipToNewline` to advance past the problematic line and continue parsing from the next line.

**Tests**:
- Unrecognized first token: `!!!` → error with position, continue parsing next line
- Two errors on consecutive lines: both errors collected
- Error followed by valid content: error recorded, valid content still parsed into AST

### T033: Skip-to-End Recovery for Grouping Blocks

Enhance group parsing to recover from errors inside group bodies.

**When an error occurs inside a group body**:
1. Record the error with full details
2. Skip tokens until a matching `end` keyword is found
3. Track nesting depth: if another group keyword is encountered during skip, increment depth; when `end` is found, decrement depth. Only stop when depth reaches 0.
4. Return the partially-parsed branch elements collected before the error

**Implementation sketch**:
```fsharp
let skipToMatchingEnd (state: ParserState) =
    let mutable depth = 1
    while depth > 0 && (peek state).Kind <> Eof do
        let token = advance state
        match token.Kind with
        | Alt | Opt | Loop | Par | Break | Critical | Ref -> depth <- depth + 1
        | End -> depth <- depth - 1
        | _ -> ()
```

**Integration point**: In `parseBranchBody`, when `parseOneElement` encounters an error that skip-to-newline can't recover from (e.g., a structurally broken element that confuses the parser), the branch parser catches the failure and calls `skipToMatchingEnd` to jump to the group's `end`.

**Tests**:
- Error inside a group body: error recorded, group still closes properly
- Nested groups with error in inner group: outer group unaffected
- Error in first branch, valid else branch: both branches present in AST

### T034: Unclosed Block Recovery

Handle the case where a grouping block reaches EOF without a matching `end`.

**Implementation**: Already partially addressed in WP05 (the branch collection loop checks for Eof). This subtask ensures:
1. The error message references the opening line number of the unclosed block
2. All open blocks are implicitly closed (if there are nested unclosed blocks, each gets its own error)
3. The partial group structure is included in the AST (not discarded)

**Error format**:
```fsharp
{
    Position = openingPosition  // Line where alt/opt/loop/etc. appeared
    Description = sprintf "Unclosed '%s' block starting at line %d" (groupKindKeyword kind) openingPosition.Line
    Expected = "'end' keyword to close the block"
    Found = "end of file"
    CorrectiveExample = sprintf "%s condition\n    Client->Server: request\nend" (groupKindKeyword kind)
}
```

**Tests**:
- Single unclosed block: error references opening line
- Nested unclosed blocks: each produces its own error with correct opening line
- Unclosed block with some valid content: content preserved in partial AST

### T035: Implicit Participant Warnings

When a message references a participant not previously declared, the parser:
1. Registers the participant as implicit (`Explicit = false`)
2. Emits a `ParseWarning` (not an error — implicit declaration is valid WSD)

**Warning format**:
```fsharp
{
    Position = messagePosition
    Description = sprintf "Participant '%s' not explicitly declared" name
    Suggestion = Some (sprintf "Add: participant %s" name)
}
```

**Implementation**: In the message parsing logic (T020), after extracting sender and receiver names, check `state.Participants` for each. If not found, call `registerParticipant` with `explicit = false` and add a warning.

**Special cases**:
- Both sender and receiver are undeclared: two warnings, two implicit registrations
- Second message with same implicit participant: no additional warning (already registered)
- Implicit participant later gets explicit `participant` declaration: update `Explicit` to `true`, no warning (the explicit declaration "claims" the implicit one)

**Tests**:
- Message with undeclared sender: warning produced, participant registered
- Message with undeclared receiver: warning produced
- Both undeclared: two warnings
- Implicit then explicit: no error, participant marked explicit after the declaration
- All participants explicit: no warnings

### T036: Error Limit Configuration

Implement configurable maximum error count. When the limit is reached, stop parsing and return the partial AST.

**Implementation**:
- `parse` already accepts `maxErrors: int` parameter (from WP03 signature)
- In `addError`, after appending the error, check if `state.Errors.Length >= state.MaxErrors`
- If limit reached, add a final error: "Error limit reached (N errors). Parsing stopped."
- Set a flag on state: `mutable ErrorLimitReached: bool`
- In the main parse loop, check this flag and stop if true

**Default**: 50 errors (in `parseWsd` convenience function)

**Tests**:
- Input with 60 errors, limit 50: exactly 51 errors collected (50 + limit message)
- Input with 3 errors, limit 50: all 3 collected, parsing continues normally
- Limit of 1: stops after first error
- Partial AST contains elements parsed before the limit was reached

### T037: Corrective Example Generation

Ensure every error type has a meaningful corrective example following Amundsen conventions.

**Corrective example catalog**:

| Error Type | Corrective Example |
|-----------|-------------------|
| Unrecognized arrow | `"Valid arrows: -> (sync call), --> (async call), ->- (sync return), -->- (async return)"` |
| Missing participant name | `"participant Client"` |
| Missing receiver after arrow | `"Client->Server: requestLabel"` |
| Missing colon in message | `"Client->Server: requestLabel(param1, param2)"` |
| Unknown note position | `"note over Client: description text"` |
| Missing note participant | `"note over Client: description text"` |
| Unclosed group block | `"alt condition\n    Client->Server: request\nend"` |
| Unclosed guard bracket | `"[guard: role=PlayerX]"` |
| Missing = in guard | `"[guard: role=PlayerX]"` |
| Empty guard key | `"[guard: role=PlayerX]"` |
| Unrecognized top-level construct | `"participant Name, Title->Receiver: label, note over Name: text, or alt/opt/loop block"` |
| Unsupported construct (activate, etc.) | `"Use arrow deactivation: Server->-Client: response"` |

**Implementation**: Create a helper module or set of functions that generate corrective examples. These can be simple string constants or parameterized templates.

**Tests**: Verify each error type's corrective example is non-empty and contains valid WSD syntax.

### T038: Error/Warning Tests

Comprehensive test suite for error recovery and failure reports. Use Expecto.

**Test file**: `test/Frank.Statecharts.Tests/Wsd/ErrorTests.fs`

**Test categories**:

1. **Skip-to-newline recovery**: error on one line, valid content on next line
2. **Multiple errors**: input with 3+ errors, all collected
3. **Error limit**: input exceeding the limit, verify truncation
4. **Structured failure format**: every error has all five fields populated
5. **Corrective examples**: spot-check examples for key error types
6. **Implicit participant warnings**: undeclared participants produce warnings
7. **Unsupported constructs**: `activate`, `deactivate` produce warnings
8. **Partial AST**: errors present but diagram still has successfully parsed elements
9. **Guard integration**: note over with guard syntax produces GuardAnnotation on the Note
10. **Guard errors**: malformed guard in note produces errors in the ParseResult
11. **Acceptance scenarios from spec.md US4**:
    - US4-S1: unrecognized arrow `->->` produces error with corrective showing four valid forms
    - US4-S2: undeclared participant produces warning suggesting `participant` declaration
    - US4-S3: empty input produces empty diagram (not a failure)
    - US4-S4: multiple errors collected, not just the first

**Example tests**:
```fsharp
testCase "US4-S1: unrecognized arrow syntax" <| fun _ ->
    let result = parseWsd "Client->->Server: bad arrow"
    Expect.isNonEmpty result.Errors "has errors"
    let err = result.Errors.[0]
    Expect.stringContains err.Description "arrow" "mentions arrow"
    Expect.stringContains err.CorrectiveExample "->" "shows valid arrow"

testCase "US4-S4: multiple errors collected" <| fun _ ->
    let result = parseWsd """
!!!badline1
???badline2
$$$badline3
"""
    Expect.isGreaterThanOrEqual result.Errors.Length 3 "at least 3 errors"

testCase "partial AST with errors" <| fun _ ->
    let result = parseWsd """
participant Client
!!!error
participant Server
Client->Server: hello
"""
    Expect.isNonEmpty result.Errors "has errors"
    Expect.equal result.Diagram.Participants.Length 2 "both participants parsed"
    let msgs = result.Diagram.Elements |> List.choose (function MessageElement m -> Some m | _ -> None)
    Expect.equal msgs.Length 1 "message parsed despite error"
```

Write at least 25 test cases covering all categories.

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Error recovery enters infinite loop (skip-to-newline doesn't advance past problematic token) | Ensure `skipToNewline` always advances at least one token. Add a guard: if position doesn't change after recovery, force advance. |
| Guard parser integration changes Parser.fs signatures | GuardParser returns a tuple; destructure it in the note parsing path. Minimal signature change. |
| Corrective examples become stale as syntax evolves | Keep examples in named constants or a helper module so they're easy to update. |
| Error cascading: one real error causes many follow-on errors | Skip-to-newline recovery minimizes cascading. Most WSD constructs are single-line. |

## Review Guidance

- Verify every `addError` call site provides all five ParseFailure fields
- Verify corrective examples contain valid WSD syntax (not just placeholder text)
- Verify error limit stops parsing and includes the limit-reached message
- Verify implicit participants produce warnings (not errors)
- Verify guard parser integration: `note over X: [guard: role=admin]` produces a Note with `Guard = Some ...`
- Verify partial AST: valid elements before/after errors are present in the Diagram
- Run `dotnet build` and `dotnet test`

## Activity Log

| Timestamp | Agent | Action |
|-----------|-------|--------|
| 2026-03-07T00:00:00Z | system | Prompt generated via /spec-kitty.tasks |
- 2026-03-15T19:34:22Z – unknown – lane=for_review – Moved to for_review
- 2026-03-15T19:46:31Z – claude-opus-reviewer – shell_pid=43003 – lane=doing – Started review via workflow command
