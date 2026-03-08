# Data Model: WSD Lexer, Parser, and AST

**Branch**: `007-wsd-lexer-parser-ast` | **Date**: 2026-03-07 | **Spec**: [spec.md](spec.md)

## Source Position

Tracks line and column for every token, used in error reporting.

```fsharp
/// 1-based line and column position in the source WSD text.
[<Struct>]
type SourcePosition = { Line: int; Column: int }
```

## Tokens

The lexer produces a flat list of tokens. Each token carries its source position and the raw text span.

```fsharp
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
    | SolidArrow        // ->
    | DashedArrow       // -->
    | SolidDeactivate   // ->-
    | DashedDeactivate  // -->-
    // Punctuation
    | Colon             // :
    | LeftParen         // (
    | RightParen        // )
    | Comma             // ,
    | LeftBracket       // [
    | RightBracket      // ]
    | Equals            // =
    // Content
    | Identifier of string
    | StringLiteral of string   // Quoted strings: "some text"
    | TextContent of string     // Free-form text (message labels, note content)
    // Structure
    | Newline
    | Eof

[<Struct>]
type Token = {
    Kind: TokenKind
    Position: SourcePosition
}
```

## AST Types

### Arrow Semantics

```fsharp
/// Solid vs. dashed line style.
type ArrowStyle =
    | Solid     // -> or ->-
    | Dashed    // --> or -->-

/// Forward (activating) vs. deactivating direction.
type Direction =
    | Forward       // -> or -->
    | Deactivating  // ->- or -->-
```

### Participant

```fsharp
/// A named participant in the sequence diagram.
type Participant = {
    Name: string
    Alias: string option
    /// True if declared via explicit `participant` line; false if implicitly introduced.
    Explicit: bool
    /// Position of the declaration (explicit) or first appearance (implicit).
    Position: SourcePosition
}
```

### Message

```fsharp
/// A message arrow between two participants.
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

### Guard Annotation

```fsharp
/// A structured guard extracted from `[guard: key=value, ...]` syntax in a note.
type GuardAnnotation = {
    Pairs: (string * string) list
    Position: SourcePosition
}
```

### Note

```fsharp
/// The position of a note relative to a participant.
type NotePosition =
    | Over
    | LeftOf
    | RightOf

/// A note annotation attached to a participant.
type Note = {
    NotePosition: NotePosition
    Target: string
    Content: string
    Guard: GuardAnnotation option
    Position: SourcePosition
}
```

### Grouping Blocks

```fsharp
/// The kind of grouping block.
type GroupKind =
    | Alt
    | Opt
    | Loop
    | Par
    | Break
    | Critical
    | Ref

/// A single branch within a grouping block (the initial block or an `else` branch).
type GroupBranch = {
    Condition: string option
    Elements: DiagramElement list
}

/// A grouping block containing one or more branches.
and Group = {
    Kind: GroupKind
    Branches: GroupBranch list
    Position: SourcePosition
}
```

### Diagram Elements

```fsharp
/// A single element in the diagram, in source order.
and DiagramElement =
    | ParticipantDecl of Participant
    | MessageElement of Message
    | NoteElement of Note
    | GroupElement of Group
    | TitleDirective of title: string * position: SourcePosition
    | AutoNumberDirective of position: SourcePosition
```

### Top-Level Diagram

```fsharp
/// The top-level AST node representing a complete (or partial) WSD diagram.
type Diagram = {
    Title: string option
    AutoNumber: bool
    Participants: Participant list
    Elements: DiagramElement list
}
```

## Parse Diagnostics

### Parse Failure (Errors)

```fsharp
/// A hard parse error. The construct at this position could not be parsed.
type ParseFailure = {
    Position: SourcePosition
    Description: string
    Expected: string
    Found: string
    CorrectiveExample: string
}
```

### Parse Warning

```fsharp
/// A soft diagnostic. The WSD is valid but may not map cleanly to Frank.Statecharts.
type ParseWarning = {
    Position: SourcePosition
    Description: string
    Suggestion: string option
}
```

### Parse Result

```fsharp
/// The result of parsing a WSD string. Always contains a Diagram (possibly partial/empty).
type ParseResult = {
    /// Best-effort diagram. Always present; may be empty or partial if errors occurred.
    Diagram: Diagram
    /// Hard errors: unrecognizable syntax, structural violations.
    Errors: ParseFailure list
    /// Soft warnings: valid WSD that may not map to statechart semantics.
    Warnings: ParseWarning list
}
```

## Top-Level API Signatures

These are the internal function signatures exposed by the WSD parser modules.

```fsharp
/// Wsd/Lexer.fs
module internal Frank.Statecharts.Wsd.Lexer

/// Tokenize a WSD source string into a flat token list.
val tokenize: source: string -> Token list

/// Wsd/GuardParser.fs
module internal Frank.Statecharts.Wsd.GuardParser

/// Attempt to extract a guard annotation from note content text.
/// Returns the guard (if found) and remaining content text.
val tryParseGuard: content: string -> position: SourcePosition -> (GuardAnnotation option * string)

/// Wsd/Parser.fs
module internal Frank.Statecharts.Wsd.Parser

/// Parse a token list into a ParseResult containing the best-effort AST and diagnostics.
val parse: tokens: Token list -> maxErrors: int -> ParseResult

/// Convenience: tokenize and parse a WSD source string in one step.
val parseWsd: source: string -> ParseResult
```

## Entity Relationships

```
ParseResult
├── Diagram
│   ├── Title (string option, from TitleDirective)
│   ├── AutoNumber (bool, from AutoNumberDirective)
│   ├── Participants (Participant list, explicit + implicit)
│   └── Elements (DiagramElement list, in source order)
│       ├── ParticipantDecl -> Participant
│       ├── MessageElement -> Message
│       │                     ├── ArrowStyle (Solid | Dashed)
│       │                     ├── Direction (Forward | Deactivating)
│       │                     └── Parameters (string list)
│       ├── NoteElement -> Note
│       │                  ├── NotePosition (Over | LeftOf | RightOf)
│       │                  └── Guard -> GuardAnnotation option
│       │                              └── Pairs ((string * string) list)
│       ├── GroupElement -> Group
│       │                   ├── GroupKind (Alt | Opt | Loop | Par | Break | Critical | Ref)
│       │                   └── Branches (GroupBranch list)
│       │                       └── Elements (DiagramElement list, recursive)
│       ├── TitleDirective
│       └── AutoNumberDirective
├── Errors (ParseFailure list)
│   └── ParseFailure { Position, Description, Expected, Found, CorrectiveExample }
└── Warnings (ParseWarning list)
    └── ParseWarning { Position, Description, Suggestion }
```
