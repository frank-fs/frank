---
work_package_id: "WP03"
title: "Core parser"
lane: done
dependencies: ["WP02"]
requirement_refs: ["FR-002", "FR-003", "FR-005", "FR-009", "FR-012", "FR-013"]
subtasks: ["T018", "T019", "T020", "T021", "T022", "T023"]
history:
  - timestamp: "2026-03-07T00:00:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# WP03: Core Parser

## Implementation Command

```
spec-kitty implement WP03 --base WP02
```

## Objectives

Implement the core recursive descent parser that transforms a `Token list` into a `ParseResult` containing a best-effort `Diagram` AST. This WP covers the parser infrastructure (token stream cursor, helpers) and parsing of the four primary constructs: participant declarations, messages with arrow styles, directives (title, autonumber), and notes. Grouping blocks (WP05), guard parsing (WP04), and error recovery (WP06) are handled in later WPs; this WP establishes the parser skeleton and core element parsing.

**Output file**: `src/Frank.Statecharts/Wsd/Parser.fs`
**Test file**: `test/Frank.Statecharts.Tests/Wsd/ParserTests.fs`
**Module**: `module internal Frank.Statecharts.Wsd.Parser`

## Success Criteria

- Parser correctly handles participant declarations (explicit and implicit with alias)
- Parser correctly handles all four arrow types with proper ArrowStyle and Direction
- Parser correctly handles message labels and parameter lists
- Parser correctly handles title and autonumber directives
- Parser correctly handles notes in all three positions (over, left of, right of)
- Implicit participants (first appearance in a message) are added to the Participants list
- Empty input produces an empty Diagram with no errors
- The parser skeleton supports extension points for grouping blocks and error recovery
- `parse` and `parseWsd` functions compile and produce correct results

## Context & Constraints

- **Depends on**: WP01 (Types.fs), WP02 (Lexer.fs)
- **Parser style**: Hand-written recursive descent (DD-01 in plan.md)
- **State management**: The parser needs mutable state for the token cursor position, collected elements, participant registry, and diagnostics. Use a parser state record or class with mutable fields.
- **Guard parsing**: Notes are parsed in this WP, but guard annotation extraction (from note content) is deferred to WP04. For now, notes store their raw TextContent as `Content` with `Guard = None`.
- **Grouping blocks**: The main parse loop should recognize group-start keywords (alt, opt, loop, etc.) but defer to a stub or skip them until WP05. A simple approach: emit a warning "grouping blocks not yet supported" if encountered, and skip to end.
- **Error recovery**: Basic skip-to-newline recovery is sufficient for this WP. Full error recovery with corrective examples is WP06.
- **Token consumption pattern**: The parser consumes tokens by advancing an index into the token list. Use `peek`, `advance`, `expect` helper functions. DO NOT use list pattern matching with `head::tail` (performance: rebuilds list on every step).

## Subtasks & Detailed Guidance

### T018: Core Parser Infrastructure

Build the parser's internal machinery: token cursor, helper functions, state management, and the main parse loop.

**Token cursor**:
```fsharp
type ParserState = {
    Tokens: Token array       // Converted from list for O(1) access
    mutable Position: int     // Current index into Tokens
    mutable Participants: Map<string, Participant>  // Registry by name
    mutable Elements: DiagramElement list            // Accumulated (reversed)
    mutable Errors: ParseFailure list                // Accumulated (reversed)
    mutable Warnings: ParseWarning list              // Accumulated (reversed)
    mutable Title: string option
    mutable AutoNumber: bool
    MaxErrors: int
}
```

**Helper functions**:
- `peek (state: ParserState) : Token` — return current token without advancing
- `advance (state: ParserState) : Token` — return current token and increment position
- `expect (state: ParserState) (kind: TokenKind) : Token option` — if current matches kind, advance and return Some; else return None
- `skipNewlines (state: ParserState) : unit` — consume consecutive Newline tokens
- `skipToNewline (state: ParserState) : unit` — consume tokens until Newline or Eof (basic error recovery)
- `addError (state: ParserState) (pos: SourcePosition) (desc: string) (expected: string) (found: string) (example: string) : unit` — append error, check limit
- `addWarning (state: ParserState) (pos: SourcePosition) (desc: string) (suggestion: string option) : unit`
- `registerParticipant (state: ParserState) (name: string) (alias: string option) (explicit: bool) (pos: SourcePosition) : unit` — add to registry if not already present; if already present, skip (duplicate explicit decl is a no-op per spec edge cases)

**Main parse loop**:
```fsharp
let rec parseElements (state: ParserState) : unit =
    skipNewlines state
    let token = peek state
    match token.Kind with
    | Eof -> ()
    | Participant -> parseParticipant state
    | Title -> parseTitleDirective state
    | AutoNumber -> parseAutoNumberDirective state
    | Note -> parseNote state
    | Alt | Opt | Loop | Par | Break | Critical | Ref ->
        // Stub: skip to end or defer to WP05
        parseGroup state
    | Identifier _ ->
        // Could be a message (Sender->Receiver: label)
        parseMessage state
    | _ ->
        // Unrecognized: skip line, record error
        addError state token.Position "Unexpected token" "participant, message, note, or directive" (tokenDescription token) ""
        skipToNewline state
    parseElements state  // Continue until Eof
```

**Top-level functions**:
```fsharp
let parse (tokens: Token list) (maxErrors: int) : ParseResult = ...
let parseWsd (source: string) : ParseResult =
    let tokens = Lexer.tokenize source
    parse tokens 50
```

**Key design decisions**:
- Convert token list to array once at entry for O(1) indexed access
- Accumulate elements in reverse (prepend), reverse at the end for correct source order
- The main loop is tail-recursive (or iterative with a while loop)
- Eof token is always the last token (Lexer guarantees this)

**Tests**: Basic infrastructure tests — parse empty input returns empty diagram, parse single newline returns empty diagram.

### T019: Participant Declarations

> **Implementation note:** The parser maintains a mutable participant registry during parsing. When an explicit `participant X` declaration is encountered and X already exists as an implicit participant (from a prior message reference), the registry entry is updated to set `Explicit = true`. The final `Diagram.Participants` list contains one entry per unique participant name, with `Explicit` reflecting whether an explicit declaration was seen.

Parse `participant <name>` and `participant <name> as <alias>` lines.

**Token sequence**: `Participant` `Identifier(name)` [optional: `As` `Identifier(alias)` or `As` `StringLiteral(alias)`] `Newline`

**Implementation**:
```fsharp
let parseParticipant (state: ParserState) =
    let startToken = advance state  // consume Participant keyword
    match peek state with
    | { Kind = Identifier name } | { Kind = StringLiteral name } ->
        advance state |> ignore
        let alias =
            match peek state with
            | { Kind = As } ->
                advance state |> ignore
                match peek state with
                | { Kind = Identifier a } | { Kind = StringLiteral a } ->
                    advance state |> ignore
                    Some a
                | t ->
                    addError state t.Position "Expected alias name" "identifier or string" (tokenDescription t) "participant API as \"REST API\""
                    None
            | _ -> None
        let participant = { Name = name; Alias = alias; Explicit = true; Position = startToken.Position }
        registerParticipant state name alias true startToken.Position
        state.Elements <- ParticipantDecl participant :: state.Elements
    | t ->
        addError state t.Position "Expected participant name" "identifier" (tokenDescription t) "participant Client"
    skipToNewline state  // consume rest of line
```

**Implicit participants**: handled in T020 (message parsing). When a message references a sender or receiver not in the registry, register it as implicit.

**Tests**:
- `participant Client` — explicit participant, no alias
- `participant API as "REST API"` — explicit with string literal alias
- `participant X as Y` — explicit with identifier alias
- Duplicate `participant Client` — second is a no-op, no error
- `participant` with no name — error with corrective example

### T020: Message Parsing

Parse message lines: `<sender><arrow><receiver>: <label>(<params>)`

**Token sequence**: `Identifier(sender)` `Arrow` `Identifier(receiver)` `Colon` `TextContent(label_and_params)` `Newline`

**Implementation approach**:
- When the main loop encounters an `Identifier`, peek ahead for an arrow token. If found, this is a message.
- Consume sender identifier, arrow token, receiver identifier.
- Map the arrow token to `(ArrowStyle, Direction)`:
  - `SolidArrow` → `(Solid, Forward)`
  - `DashedArrow` → `(Dashed, Forward)`
  - `SolidDeactivate` → `(Solid, Deactivating)`
  - `DashedDeactivate` → `(Dashed, Deactivating)`
- After the receiver, expect a `Colon` followed by `TextContent`.
- Parse the label and parameters from the TextContent string:
  - If the text contains `(...)`, extract the parenthesized portion as parameters
  - Split parameters on `,` and trim each
  - The label is the text before the `(`
  - If no parentheses, the entire text is the label, parameters is empty
  - Empty parens `()` produce an empty parameter list

**Implicit participant registration**: Before creating the Message, check if sender and receiver are in the participant registry. If not, register them as implicit (Explicit = false) and emit a `ParseWarning` noting the implicit declaration.

**Tests**:
- `Client->Server: hello` — solid forward message, no params
- `Client-->Server: getData` — dashed forward
- `Server->-Client: 200 OK` — solid deactivating
- `Server-->-Client: result` — dashed deactivating
- `Client->API: createUser(name, email)` — message with 2 params
- `Client->API: getStatus()` — message with empty parens, 0 params
- `Client->API: simple` — message with no parens, 0 params
- Message with implicit participants (no prior declaration) — produces warnings
- Missing colon after receiver — error
- Missing receiver after arrow — error

### T021: Directive Parsing

Parse `title <text>` and `autonumber` directives.

**Title**: `Title` `TextContent(text)` `Newline`
(Or: `Title` `Colon` `TextContent(text)` — handle both with and without colon after `title`)

**Implementation**:
- Consume `Title` keyword
- If next token is `Colon`, consume it and then consume `TextContent`
- If next token is `TextContent` directly (some WSD dialects allow `title My Title` without colon), consume it
- Set `state.Title` to the text value
- If `state.Title` was already set, emit a warning "duplicate title directive"
- Add `TitleDirective` to elements

**AutoNumber**: `AutoNumber` `Newline`

**Implementation**:
- Consume `AutoNumber` keyword
- Set `state.AutoNumber <- true`
- Add `AutoNumberDirective` to elements

**Tests**:
- `title My Diagram` — title extracted
- `title: My Diagram` — title with colon
- `autonumber` — auto number enabled
- Duplicate title — warning on second occurrence
- `title` with no text — error or empty title (decide: emit warning, use empty string)

### T022: Note Parsing

Parse note elements: `note over|left of|right of <participant>: <content>`

**Token sequence**: `Note` `Over|LeftOf|RightOf` `Identifier(target)` `Colon` `TextContent(content)` `Newline`

**Implementation**:
- Consume `Note` keyword
- Match next token for position:
  - `Over` → `NotePosition.Over`
  - `LeftOf` → `NotePosition.LeftOf`
  - `RightOf` → `NotePosition.RightOf`
  - Other → error "expected 'over', 'left of', or 'right of'"
- Consume participant identifier
- Expect `Colon`, then `TextContent`
- Create `Note` with `Guard = None` (guard extraction deferred to WP04)
- Add `NoteElement` to elements

**Guard handling (stub for WP04)**:
- For now, the note's `Content` field holds the raw TextContent including any `[guard: ...]` syntax
- `Guard` is `None` for all notes
- WP04 will add a call to `GuardParser.tryParseGuard` to extract guard annotations from notes where `NotePosition = Over`

**Tests**:
- `note over Client: This is a note` — note over, plain content
- `note left of Server: Internal detail` — note left of
- `note right of Server: External API` — note right of
- `note over Client: [guard: role=admin]` — content includes guard text (guard extraction is WP04)
- `note` with missing position keyword — error
- `note over` with missing participant — error
- `note over Client` with missing colon — error

### T023: Core Parser Tests

Comprehensive test suite for the core parser. Use Expecto.

**Test file**: `test/Frank.Statecharts.Tests/Wsd/ParserTests.fs`

**Test categories**:

1. **Empty/minimal inputs**: empty string, whitespace only, comments only — all produce empty diagrams
2. **Participant tests**: explicit declarations, aliases, duplicates, order preservation
3. **Message tests**: all four arrow types, labels, parameters, implicit participant registration
4. **Directive tests**: title, autonumber, duplicate title warning
5. **Note tests**: all three positions, content preservation
6. **Mixed inputs**: WSD with multiple element types interleaved
7. **Acceptance scenarios from spec.md**:
   - US1-S1: participants + `->` messages produce correct AST
   - US1-S2: title + autonumber directives
   - US1-S3: message parameters
   - US1-S4: mixed arrow types

**Example end-to-end test**:
```fsharp
testCase "US1-S1: participants and solid messages" <| fun _ ->
    let result = parseWsd """
participant Client
participant Server
Client->Server: request
Server->-Client: response
"""
    Expect.isEmpty result.Errors "no errors"
    Expect.equal result.Diagram.Participants.Length 2 "two participants"
    let msgs = result.Diagram.Elements |> List.choose (function MessageElement m -> Some m | _ -> None)
    Expect.equal msgs.Length 2 "two messages"
    Expect.equal msgs.[0].ArrowStyle Solid "first is solid"
    Expect.equal msgs.[0].Direction Forward "first is forward"
    Expect.equal msgs.[1].Direction Deactivating "second is deactivating"
```

Write at least 25 test cases covering the categories above.

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Parser state management complexity | Keep state in a single record with mutable fields. All mutations go through helper functions. |
| Identifier vs. message ambiguity | When an Identifier is encountered in the main loop, peek ahead. If next token is an arrow, parse as message. Otherwise, emit an error (bare identifiers are not valid top-level elements). |
| Title syntax variation (with/without colon) | Handle both forms. The colon-less form is a TextContent right after Title keyword. |
| Guard stub may need refactoring in WP04 | Keep note parsing modular: extract a `parseNoteContent` helper that WP04 can modify to call GuardParser. |

## Review Guidance

- Verify all four arrow types map to the correct (ArrowStyle, Direction) pair
- Verify implicit participants are registered with `Explicit = false`
- Verify the parser handles the acceptance scenarios from spec.md US1
- Verify empty input produces `{ Diagram = { Title = None; AutoNumber = false; Participants = []; Elements = [] }; Errors = []; Warnings = [] }`
- Verify `.fsproj` includes `Wsd/Parser.fs` after `Wsd/Lexer.fs` and `Wsd/GuardParser.fs`
- Run `dotnet build` and `dotnet test`

## Activity Log

| Timestamp | Agent | Action |
|-----------|-------|--------|
| 2026-03-07T00:00:00Z | system | Prompt generated via /spec-kitty.tasks |
