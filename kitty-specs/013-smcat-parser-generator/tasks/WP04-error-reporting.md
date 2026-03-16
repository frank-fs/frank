---
work_package_id: "WP04"
subtasks:
  - "T020"
  - "T021"
  - "T022"
  - "T023"
  - "T024"
title: "Structured Error Reporting"
phase: "Phase 2 - Robustness"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP03"]
requirement_refs: ["FR-010", "FR-011"]
history:
  - timestamp: "2026-03-15T23:59:14Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP04 -- Structured Error Reporting

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
spec-kitty implement WP04 --base WP03
```

Depends on WP03 (Parser must exist with basic parsing before error recovery is layered in).

---

## Objectives & Success Criteria

- Enhance `Parser.fs` with error recovery: on parse failure, record error, skip to next statement, continue parsing
- Generate structured `ParseFailure` records with position, expected/found, and corrective examples for common error patterns
- Implement configurable error limit (maxErrors, default 50) per FR-011
- Generate `ParseWarning` records for ambiguous constructs
- Create comprehensive error tests covering all User Story 5 acceptance scenarios

**Done when**: `dotnet test --filter "Smcat.Error"` passes. Malformed inputs produce `ParseFailure` records with line/column, expected/found descriptions, and corrective example text. Multiple errors in one input are all collected (up to limit).

## Context & Constraints

- **Spec**: User Story 5 (structured failure reports), FR-010 (failure report fields), FR-011 (error collection up to limit)
- **Research**: R-001 (hand-written parser enables precise error recovery)
- **Data Model**: `data-model.md` -- `ParseFailure` and `ParseWarning` record definitions
- **SC-006**: Every failure report includes line/column position and corrective example
- **WSD Pattern**: `src/Frank.Statecharts/Wsd/Parser.fs` error handling (follow recovery patterns)

**Key constraints**:
- `ParseResult.Document` should contain successfully parsed elements (best-effort partial result)
- Error recovery must not cause infinite loops (always advance at least one token)
- Corrective examples must show valid smcat syntax

## Subtasks & Detailed Guidance

### Subtask T020 -- Enhance Parser.fs with error recovery

**Purpose**: Make the parser resilient to malformed input by recording errors and continuing to parse subsequent statements, rather than aborting on the first error.

**Steps**:

1. **Add recovery function** to Parser.fs:
   ```fsharp
   let skipToNextStatement (state: ParserState) =
       // Advance past tokens until we reach a clean synchronization point
       let mutable found = false
       while not found && (peek state).Kind <> Eof do
           match (peek state).Kind with
           | Semicolon | Comma ->
               advance state  // consume the terminator
               found <- true
           | Newline ->
               advance state  // consume newline
               found <- true
           | RightBrace ->
               found <- true  // don't consume -- let composite state handler deal with it
           | _ ->
               advance state  // skip unknown token
   ```

2. **Wrap parse operations** with error recovery:
   - In `parseElement`, wrap the dispatch logic in a try-with or use Result-returning helpers
   - On failure: call `addError` with descriptive message, then `skipToNextStatement`
   - Continue the main parsing loop after recovery

3. **Ensure forward progress**: After error recovery, always verify that `state.Position` has advanced. If not, force-advance by one token to prevent infinite loops.

4. **Best-effort document**: Even after errors, successfully parsed elements should remain in the `Elements` list. Only the current statement being parsed is discarded on error.

**Files**: `src/Frank.Statecharts/Smcat/Parser.fs` (modify existing)

**Notes**: The WSD parser uses a similar pattern -- study `src/Frank.Statecharts/Wsd/Parser.fs` for the error recovery approach used there.

---

### Subtask T021 -- Implement corrective example generation

**Purpose**: For each common error pattern, provide a corrective example showing correct smcat syntax. This fulfills FR-010 and SC-006.

**Steps**:

1. **Define error patterns and their corrective examples** as constants or helper functions:

   | Error Pattern | Expected | Found | Corrective Example |
   |---------------|----------|-------|--------------------|
   | Missing colon before label | `":"` or `";"` after target state | text without colon | `"source => target: event;"` |
   | Invalid arrow syntax | `"=>"` | `"==>"` or other | `"source => target;"` |
   | Unclosed bracket | `"]"` to close attribute/guard bracket | EOF or `;` | `"source => target: event [guard];"` |
   | Unclosed composite block | `"}"` to close composite state | EOF | `"parent { child1 => child2; };"` |
   | Missing target state | identifier after `"=>"` | `;` or EOF | `"source => target;"` |
   | Unexpected token | identifier or `"=>"` | the unexpected token | `"idle => running: start;"` |
   | Empty state name | non-empty identifier | empty or whitespace | `"stateName;"` |

2. **Integrate examples into error recording**: When `addError` is called, pass the appropriate corrective example based on the error type:
   ```fsharp
   addError state pos
       "Missing colon before transition label"
       "':' or ';' after target state"
       (sprintf "found '%s'" (tokenToString (peek state)))
       "source => target: event;"
   ```

3. **Make examples context-aware** where practical: If the source and target state names are known, include them in the corrective example:
   ```fsharp
   let example = sprintf "%s => %s: eventName;" sourceName targetName
   ```

**Files**: `src/Frank.Statecharts/Smcat/Parser.fs` (modify existing)

---

### Subtask T022 -- Implement configurable error limit

**Purpose**: Allow the caller to control the maximum number of errors collected, preventing runaway error accumulation on very malformed input.

**Steps**:

1. **Verify `MaxErrors` is used** in the `addError` function:
   ```fsharp
   let addError (state: ParserState) ... =
       if not state.ErrorLimitReached then
           state.Errors <- state.Errors @ [{ ... }]
           if state.Errors.Length >= state.MaxErrors then
               state.ErrorLimitReached <- true
   ```

2. **Stop parsing when limit reached**: In the main parsing loop, check `ErrorLimitReached`:
   ```fsharp
   while not (atEnd state) && not state.ErrorLimitReached do
       parseElement state ...
   ```

3. **Default value**: `parse` function uses `maxErrors` parameter (caller-specified). `parseSmcat` convenience function passes `50` as the default.

4. **Test with configurable limit**:
   - Parse input with 100 errors using `maxErrors = 5` -> exactly 5 errors recorded
   - Parse input with 3 errors using `maxErrors = 50` -> exactly 3 errors recorded
   - Parse input with 0 errors -> empty error list

**Files**: `src/Frank.Statecharts/Smcat/Parser.fs` (modify existing)

---

### Subtask T023 -- Add ParseWarning generation

**Purpose**: Generate warnings for ambiguous or suspicious constructs that are not errors but may indicate user mistakes.

**Steps**:

1. **Warning cases to implement**:

   a. **State name matches pseudo-state convention but has explicit `[type=...]` attribute**:
      - Warning: "State name 'initialPhase' matches naming convention for Initial state type, but explicit attribute overrides to Regular"
      - Suggestion: "Consider renaming the state or removing the explicit type attribute"

   b. **Duplicate state declarations**:
      - Warning: "State 'idle' declared multiple times"
      - Suggestion: "Combine state attributes into a single declaration"

   c. **Unclosed bracket in transition label** (already handled in LabelParser):
      - Propagate LabelParser warnings into Parser state

2. **Propagate LabelParser warnings**: When calling `LabelParser.parseLabel`, merge returned warnings into `state.Warnings`:
   ```fsharp
   let (label, labelWarnings) = LabelParser.parseLabel labelText position
   state.Warnings <- state.Warnings @ labelWarnings
   ```

3. **Record warnings** using the `ParseWarning` type:
   ```fsharp
   state.Warnings <- state.Warnings @ [{ Position = pos; Description = desc; Suggestion = Some suggestion }]
   ```

**Files**: `src/Frank.Statecharts/Smcat/Parser.fs` (modify existing)

**Notes**: Warnings do not cause error recovery or statement skipping. They are informational.

---

### Subtask T024 -- Create `test/Frank.Statecharts.Tests/Smcat/ErrorTests.fs`

**Purpose**: Comprehensive tests for error recovery and structured failure reports, covering all User Story 5 acceptance scenarios.

**Steps**:

1. Replace the stub with test cases:

2. **Acceptance scenario tests from spec**:

   a. Missing colon: `"on => off switch flicked;"`
      - Verify: error includes line/column
      - Verify: expected says `:` or `;` after target state
      - Verify: corrective example shows correct syntax

   b. Invalid arrow: `"on ==> off;"`
      - Verify: error says "unrecognized arrow syntax, expected `=>`"
      - Verify: corrective example present

   c. Unclosed bracket: `"on => off: start [guard;"`
      - Verify: error indicates unclosed bracket with position

   d. Multiple errors: Input with 3+ distinct errors
      - Verify: all errors collected (not just first)
      - Verify: each error has position, expected, found, corrective example

3. **Error recovery tests**:
   - Input with error followed by valid statement -> valid statement is still parsed
   - `"a => ; b => c: event;"` -> first transition has error (missing target), second parses correctly
   - Verify `ParseResult.Document.Elements` contains the successfully parsed elements

4. **Error limit tests**:
   - Create input with many errors, use `parse tokens 3` -> exactly 3 errors
   - Verify parsing stops cleanly after limit

5. **Warning tests**:
   - State name `"initialPhase"` with `[type=regular]` attribute -> warning about naming convention mismatch
   - Unclosed bracket in label -> warning from LabelParser propagated

6. **Edge cases**:
   - Completely garbage input `"@#$%^&*"` -> errors but no crash
   - Very long invalid input (500 characters of junk) -> errors collected, no stack overflow
   - Single invalid token followed by EOF -> one error

7. Use Expecto pattern:
   ```fsharp
   module Smcat.ErrorTests

   open Expecto
   open Frank.Statecharts.Smcat.Types
   open Frank.Statecharts.Smcat.Parser

   [<Tests>]
   let errorTests = testList "Smcat.Error" [ ... ]
   ```

**Files**: `test/Frank.Statecharts.Tests/Smcat/ErrorTests.fs` (~200-250 lines)

**Parallel?**: Yes -- can be written alongside T020-T023 implementation.

---

## Test Strategy

Run error tests:
```bash
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Smcat.Error"
```

Run all smcat tests to verify no regressions:
```bash
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Smcat"
```

## Risks & Mitigations

- **Infinite loop in error recovery**: Always advance at least one token after error recovery. Add a safety check in the main loop.
- **Cascading false-positive errors**: After skipping to next statement, the parser may be in a confused state. The synchronization point (`;`, `,`, newline) helps, but some false positives are acceptable.
- **Corrective examples becoming stale**: Keep examples as string literals close to the error detection logic so they stay synchronized with grammar changes.
- **Parser test regressions**: Error recovery changes may affect how valid input is parsed. Run all Smcat.Parser tests after changes.

## Review Guidance

- Verify all 4 acceptance scenarios from User Story 5 have corresponding tests
- Verify every `ParseFailure` includes all 5 fields (Position, Description, Expected, Found, CorrectiveExample)
- Verify error recovery allows subsequent valid statements to be parsed
- Verify error limit works correctly (test with small limit value)
- Verify no infinite loops on malformed input (check for force-advance on recovery)
- Run `dotnet test --filter "Smcat"` to verify no regressions in Parser/Lexer tests

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:14Z -- system -- lane=planned -- Prompt created.

---

### Updating Lane Status

To change a work package's lane, either:
1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP04 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
