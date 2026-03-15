---
work_package_id: WP01
title: AST types + token definitions
lane: "doing"
dependencies: []
base_branch: master
base_commit: 17eb0dbfb272896d5ae71c74b9a65ce93d4ddd26
created_at: '2026-03-08T17:30:54.800354+00:00'
subtasks: [T001, T002, T003, T004, T005, T006, T007, T008, T009, T010, T011]
shell_pid: "39810"
agent: "claude-opus-reviewer"
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-001
- FR-002
- FR-003
- FR-004
- FR-007
- FR-008a
---

# WP01: AST Types + Token Definitions

## Implementation Command

```
spec-kitty implement WP01
```

## Objectives

Define all F# discriminated unions, records, and struct types that form the WSD parser's type system. This includes:

1. The lexer's token types (source positions, token kinds, token records)
2. The AST types (arrows, participants, messages, notes, guards, groups, diagram elements, diagram)
3. The parse result types (failures, warnings, parse result record)

All types live in a single file: `src/Frank.Statecharts/Wsd/Types.fs`.

## Success Criteria

- All types from `data-model.md` are faithfully implemented as internal F# types
- `SourcePosition` and `Token` are `[<Struct>]` value types (performance: avoid heap allocation per token)
- All types compile under multi-target (net8.0/net9.0/net10.0)
- Types are `internal` — no public visibility
- `DiagramElement` and `Group`/`GroupBranch` form a mutually recursive type group (using `and`)
- A basic compilation test exists to verify the types are usable from the test project
- The `Wsd/` directory is created under `src/Frank.Statecharts/`
- The `.fsproj` file includes `Wsd/Types.fs` in the `<Compile>` items

## Context & Constraints

- **Location**: `src/Frank.Statecharts/Wsd/Types.fs`
- **Module declaration**: `module internal Frank.Statecharts.Wsd.Types`
- **Visibility**: All types are internal to the Frank.Statecharts assembly. Use `internal` on the module declaration. The test project will need `InternalsVisibleTo` if not already configured.
- **No dependencies**: This file depends on nothing except FSharp.Core. No ASP.NET Core, no NuGet packages.
- **F# compile order**: This file must appear before Lexer.fs, GuardParser.fs, and Parser.fs in the `.fsproj`.
- **Mutually recursive types**: `DiagramElement`, `GroupBranch`, and `Group` reference each other. Use `type ... and ...` syntax.
- **Performance**: `SourcePosition` and `Token` must be `[<Struct>]` to avoid per-token heap allocations. The lexer will produce hundreds of tokens for a typical WSD file.
- **Data model reference**: See `kitty-specs/007-wsd-lexer-parser-ast/data-model.md` for the canonical type definitions.

## Subtasks & Detailed Guidance

### T001: SourcePosition Struct Type

Create the `SourcePosition` struct record. This is the foundation for all error reporting — every token and every AST node carries a source position.

```fsharp
[<Struct>]
type SourcePosition = { Line: int; Column: int }
```

**Key decisions**:
- 1-based line and column numbers (matches editor conventions and user expectations)
- `[<Struct>]` attribute is mandatory — this type is allocated per token, and tokens number in the hundreds for typical WSD inputs
- Do NOT add comparison or formatting members yet — keep the type minimal

**Validation**: Instantiate a `SourcePosition` in a test, verify `Line` and `Column` are accessible.

### T002: TokenKind DU and Token Struct

Define `TokenKind` as a discriminated union covering all WSD syntax elements, and `Token` as a struct record pairing a `TokenKind` with a `SourcePosition`.

```fsharp
type TokenKind =
    // Keywords (17 total)
    | Participant | Title | AutoNumber | Note | Over | LeftOf | RightOf
    | Alt | Opt | Loop | Par | Break | Critical | Ref | Else | End | As
    // Arrows (4 total)
    | SolidArrow | DashedArrow | SolidDeactivate | DashedDeactivate
    // Punctuation (6 total)
    | Colon | LeftParen | RightParen | Comma | LeftBracket | RightBracket | Equals
    // Content (3 total — carry string payloads)
    | Identifier of string | StringLiteral of string | TextContent of string
    // Structure
    | Newline | Eof
```

**Key decisions**:
- `TokenKind` is NOT a struct (it has `of string` cases, which prevent struct DU in F#)
- `Token` IS a struct record: `[<Struct>] type Token = { Kind: TokenKind; Position: SourcePosition }`
- `Identifier` holds unquoted names (participant names, message labels that are single words)
- `StringLiteral` holds quoted strings (text between double quotes, quotes stripped)
- `TextContent` holds free-form text (message labels after colon, note content after colon — everything to end of line)
- Keywords are case-insensitive during lexing but represented as specific DU cases
- `LeftOf` and `RightOf` are two-word keywords (`left of`, `right of`) — the lexer handles multi-word recognition

**Validation**: Create tokens of various kinds, pattern match on them in tests.

### T003: ArrowStyle + Direction DUs

```fsharp
type ArrowStyle = Solid | Dashed
type Direction = Forward | Deactivating
```

These are the semantic dimensions of WSD arrows per Amundsen's approach:
- `Solid` + `Forward` = `->` (synchronous call, state transition trigger)
- `Dashed` + `Forward` = `-->` (asynchronous/optional call, query)
- `Solid` + `Deactivating` = `->-` (return from unsafe operation)
- `Dashed` + `Deactivating` = `-->-` (return from safe operation)

The parser assigns these; it does NOT interpret HTTP semantics (that is downstream).

**Validation**: Verify all four combinations can be constructed.

### T004: Participant Record Type

```fsharp
type Participant = {
    Name: string
    Alias: string option
    Explicit: bool
    Position: SourcePosition
}
```

- `Name` is the canonical identifier used in messages
- `Alias` is the display name from `participant X as "Display Name"` (None if no alias)
- `Explicit` is true for `participant X` declarations, false for implicit first-appearance
- `Position` is the declaration site (explicit) or first-appearance site (implicit)

**Validation**: Create explicit and implicit participants, verify field values.

### T005: Message Record Type

```fsharp
type Message = {
    Sender: string
    Receiver: string
    ArrowStyle: ArrowStyle
    Direction: Direction
    Label: string
    Parameters: string list
    Position: SourcePosition
}
```

- `Sender` and `Receiver` are participant names (must match `Participant.Name`)
- `Label` is the text after the colon (e.g., `createUser` from `Client->API: createUser(name, email)`)
- `Parameters` is extracted from parenthesized list (e.g., `["name"; "email"]`); empty list if no parens
- Parameters are trimmed strings, no quotes

**Validation**: Create messages with and without parameters, verify all fields.

### T006: GuardAnnotation Record Type

```fsharp
type GuardAnnotation = {
    Pairs: (string * string) list
    Position: SourcePosition
}
```

- `Pairs` are key-value tuples extracted from `[guard: key=value, ...]` syntax
- Keys and values are trimmed strings
- Position points to the opening `[` of the guard syntax

**Validation**: Create a guard with multiple pairs, verify tuple access.

### T007: Note + NotePosition Types

```fsharp
type NotePosition = Over | LeftOf | RightOf

type Note = {
    NotePosition: NotePosition
    Target: string
    Content: string
    Guard: GuardAnnotation option
    Position: SourcePosition
}
```

- `Guard` is `Some` only for `note over` with `[guard: ...]` syntax; `None` otherwise
- Per research.md: guards are only recognized in `note over`, not `note left of` or `note right of`
- `Content` contains the note text with guard syntax removed (if guard was extracted)

**Validation**: Create notes in all three positions, with and without guards.

### T008: GroupKind, GroupBranch, Group Types

These types are mutually recursive with `DiagramElement` (T009). Define them together using `and`.

```fsharp
type GroupKind = Alt | Opt | Loop | Par | Break | Critical | Ref

type GroupBranch = {
    Condition: string option
    Elements: DiagramElement list
}

and Group = {
    Kind: GroupKind
    Branches: GroupBranch list
    Position: SourcePosition
}
```

- `GroupBranch.Condition` is `None` for branches with no condition text (e.g., bare `else`)
- `Branches` always has at least one entry (the initial block); `else` adds more
- `Elements` is recursive — groups can contain other groups (nesting)

### T009: DiagramElement DU

```fsharp
and DiagramElement =
    | ParticipantDecl of Participant
    | MessageElement of Message
    | NoteElement of Note
    | GroupElement of Group
    | TitleDirective of title: string * position: SourcePosition
    | AutoNumberDirective of position: SourcePosition
```

- Named fields on `TitleDirective` and `AutoNumberDirective` for clarity
- This is in the same `and` group as `GroupBranch` and `Group` (mutual recursion)

### T010: Diagram Record Type

```fsharp
type Diagram = {
    Title: string option
    AutoNumber: bool
    Participants: Participant list
    Elements: DiagramElement list
}
```

- `Title` is extracted from the first `TitleDirective` encountered (later ones produce warnings)
- `Participants` includes both explicit and implicit declarations, in order of first appearance
- `Elements` is the full ordered list of all diagram elements (including participant declarations)

### T011: ParseFailure, ParseWarning, ParseResult Types

```fsharp
type ParseFailure = {
    Position: SourcePosition
    Description: string
    Expected: string
    Found: string
    CorrectiveExample: string
}

type ParseWarning = {
    Position: SourcePosition
    Description: string
    Suggestion: string option
}

type ParseResult = {
    Diagram: Diagram
    Errors: ParseFailure list
    Warnings: ParseWarning list
}
```

- `ParseResult.Diagram` is ALWAYS present (never option). Even on total failure, an empty diagram is returned.
- `Errors` = hard failures (unrecognizable syntax). `Warnings` = valid WSD with caveats.
- `CorrectiveExample` on failures teaches Amundsen conventions (e.g., shows valid arrow forms).

**Validation**: Create a ParseResult with errors and warnings, verify all fields accessible.

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Frank.Statecharts project may not exist yet (#87) | Create the `Wsd/` directory and `Types.fs` as standalone. If the parent project doesn't exist, create a minimal project shell or document what's needed. |
| Mutual recursion between DiagramElement/Group/GroupBranch | Use `type ... and ... and ...` syntax. Test that all patterns can be constructed and matched. |
| InternalsVisibleTo not configured for test project | Check if it exists; add `[<assembly: InternalsVisibleTo("Frank.Statecharts.Tests")>]` if needed. |
| Struct limitations (no `of` cases in struct DUs) | Only `SourcePosition` and `Token` are structs. `TokenKind` with `of string` cases must be a regular DU. |

## Review Guidance

- Verify every type from `data-model.md` is present and matches the specification exactly
- Verify `[<Struct>]` on `SourcePosition` and `Token` (not on `TokenKind` or AST DUs)
- Verify `internal` visibility on the module declaration
- Verify mutual recursion compiles (`DiagramElement`, `GroupBranch`, `Group` in one `and` chain)
- Verify `.fsproj` includes `Wsd/Types.fs` in the correct position (before other Wsd/ files)
- Run `dotnet build` across all target frameworks
- Verify test project can access the internal types

## Activity Log

| Timestamp | Agent | Action |
|-----------|-------|--------|
| 2026-03-07T00:00:00Z | system | Prompt generated via /spec-kitty.tasks |
- 2026-03-08T17:30:54Z – claude-opus – shell_pid=98012 – lane=doing – Assigned agent via workflow command
- 2026-03-08T17:36:47Z – claude-opus – shell_pid=98012 – lane=for_review – All 11 subtasks (T001-T011) implemented: SourcePosition, TokenKind, ArrowStyle/Direction, Participant, Message, GuardAnnotation, Note/NotePosition, GroupKind/GroupBranch/Group, DiagramElement, Diagram, ParseFailure/ParseWarning/ParseResult. Builds clean on net8.0/net9.0/net10.0.
- 2026-03-15T19:20:52Z – claude-opus – shell_pid=98012 – lane=for_review – Moved to for_review
- 2026-03-15T19:39:45Z – claude-opus-reviewer – shell_pid=39810 – lane=doing – Started review via workflow command
