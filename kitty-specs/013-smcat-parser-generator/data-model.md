# Data Model: smcat Parser and Generator

**Feature**: 013-smcat-parser-generator
**Date**: 2026-03-15

## Smcat/Types.fs -- Format-Specific AST Types

All types are in `module internal Frank.Statecharts.Smcat.Types`.

### SourcePosition (struct)

Identical to `Wsd.Types.SourcePosition`. Will be unified by spec 020.

```fsharp
[<Struct>]
type SourcePosition = { Line: int; Column: int }
```

### Token Types

```fsharp
type TokenKind =
    // Arrows
    | TransitionArrow    // =>
    // Punctuation
    | Colon              // :
    | Semicolon          // ;
    | Comma              // ,
    | LeftBracket        // [
    | RightBracket       // ]
    | LeftBrace          // {
    | RightBrace         // }
    | ForwardSlash       // /
    | Equals             // =
    // Content
    | Identifier of string
    | QuotedString of string
    // Pseudo-state prefixes (detected during lexing)
    | Caret              // ^ (choice pseudo-state prefix)
    | CloseBracketPrefix // ] (fork/join pseudo-state prefix -- context-dependent)
    // Activities
    | EntrySlash         // entry/
    | ExitSlash          // exit/
    | Ellipsis           // ... (do activity marker)
    // Structure
    | Newline
    | Eof

[<Struct>]
type Token =
    { Kind: TokenKind
      Position: SourcePosition }
```

### State Types

```fsharp
type StateType =
    | Regular
    | Initial
    | Final
    | ShallowHistory
    | DeepHistory
    | Choice
    | ForkJoin
    | Terminate

type StateActivity =
    { Entry: string option
      Exit: string option
      Do: string option }

type SmcatAttribute = { Key: string; Value: string }

type SmcatState =
    { Name: string
      Label: string option
      StateType: StateType
      Activities: StateActivity option
      Attributes: SmcatAttribute list
      Children: SmcatDocument option   // Composite states contain nested documents
      Position: SourcePosition }
```

### Transition Types

```fsharp
type TransitionLabel =
    { Event: string option
      Guard: string option
      Action: string option }

type SmcatTransition =
    { Source: string
      Target: string
      Label: TransitionLabel option
      Attributes: SmcatAttribute list
      Position: SourcePosition }
```

### Document Types

```fsharp
type SmcatElement =
    | StateDeclaration of SmcatState
    | TransitionElement of SmcatTransition
    | CommentElement of string

type SmcatDocument =
    { Elements: SmcatElement list }
```

Note: `SmcatDocument` and `SmcatState` are mutually recursive (`SmcatState.Children` references `SmcatDocument`). They must be defined with `and` in F#.

### Parse Result Types

```fsharp
type ParseFailure =
    { Position: SourcePosition
      Description: string
      Expected: string
      Found: string
      CorrectiveExample: string }

type ParseWarning =
    { Position: SourcePosition
      Description: string
      Suggestion: string option }

type ParseResult =
    { Document: SmcatDocument
      Errors: ParseFailure list
      Warnings: ParseWarning list }
```

## Entity Relationships

```
SmcatDocument
 └── SmcatElement list
      ├── StateDeclaration of SmcatState
      │    ├── StateType (DU: Regular | Initial | Final | ...)
      │    ├── StateActivity option (entry/exit/do)
      │    ├── SmcatAttribute list (key-value pairs)
      │    └── SmcatDocument option (composite -- recursive)
      ├── TransitionElement of SmcatTransition
      │    ├── TransitionLabel option
      │    │    ├── Event: string option
      │    │    ├── Guard: string option
      │    │    └── Action: string option
      │    └── SmcatAttribute list
      └── CommentElement of string

ParseResult
 ├── Document: SmcatDocument
 ├── Errors: ParseFailure list
 └── Warnings: ParseWarning list
```

## State Type Detection Logic

State type is inferred from the state name using these rules (evaluated in order):

1. Name contains `"initial"` (case-insensitive) -> `Initial`
2. Name contains `"final"` (case-insensitive) -> `Final`
3. Name contains `"deep.history"` (case-insensitive) -> `DeepHistory`
4. Name contains `"history"` (case-insensitive) -> `ShallowHistory`
5. Name starts with `^` -> `Choice`
6. Name starts with `]` -> `ForkJoin`
7. Name contains `"terminate"` (case-insensitive) -> `Terminate`
8. Explicit `[type=...]` attribute overrides naming convention
9. Otherwise -> `Regular`

## Validation Rules

- State names must be non-empty strings (plain identifiers or quoted strings)
- Transition source and target must be non-empty strings
- Transition labels are fully optional (a transition can have no label)
- Each component of a transition label (event, guard, action) is independently optional
- Attributes are ordered key-value pairs; keys need not be unique (last wins for rendering)
- Composite state blocks must have matching `{` and `}` braces
- Nested documents follow the same rules as top-level documents
- Empty input, whitespace-only input, and comment-only input produce valid empty documents

## Generator Output Model

The generator takes `StateMachineMetadata<'State, 'Event, 'Context>` and produces a `string` of valid smcat text. The output model:

- For each state in `StateMetadata`:
  - If the state equals `Initial`, emit `initial => <state>;` as first line
  - If `IsFinal` is true on a state, record it for final pseudo-state transitions
- For each transition implied by the `Transition` function (not directly enumerable -- requires enumeration of state/event pairs from metadata):
  - Emit `source => target: event [guard] / action;`
  - Omit guard/action components if not present
- For final states, emit `<source> => final;` transitions

Note: The generator works from `StateMachineMetadata` which contains `Guard` records with `Name` fields. The event and state names come from the generic type parameters' string representation.
