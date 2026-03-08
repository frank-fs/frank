module internal Frank.Statecharts.Wsd.Types

// T001: SourcePosition — 1-based line and column position in source WSD text.
[<Struct>]
type SourcePosition = { Line: int; Column: int }

// T002: TokenKind DU and Token struct
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
    | SolidArrow // ->
    | DashedArrow // -->
    | SolidDeactivate // ->-
    | DashedDeactivate // -->-
    // Punctuation
    | Colon // :
    | LeftParen // (
    | RightParen // )
    | Comma // ,
    | LeftBracket // [
    | RightBracket // ]
    | Equals // =
    // Content
    | Identifier of string
    | StringLiteral of string
    | TextContent of string
    // Structure
    | Newline
    | Eof

[<Struct>]
type Token =
    { Kind: TokenKind
      Position: SourcePosition }

// T003: ArrowStyle and Direction
type ArrowStyle =
    | Solid
    | Dashed

type Direction =
    | Forward
    | Deactivating

// T004: Participant
type Participant =
    { Name: string
      Alias: string option
      Explicit: bool
      Position: SourcePosition }

// T005: Message
type Message =
    { Sender: string
      Receiver: string
      ArrowStyle: ArrowStyle
      Direction: Direction
      Label: string
      Parameters: string list
      Position: SourcePosition }

// T006: GuardAnnotation
type GuardAnnotation =
    { Pairs: (string * string) list
      Position: SourcePosition }

// T007: NotePosition and Note
type NotePosition =
    | Over
    | LeftOf
    | RightOf

type Note =
    { NotePosition: NotePosition
      Target: string
      Content: string
      Guard: GuardAnnotation option
      Position: SourcePosition }

// T008: GroupKind
type GroupKind =
    | Alt
    | Opt
    | Loop
    | Par
    | Break
    | Critical
    | Ref

// T008 + T009: Mutually recursive types — GroupBranch, Group, DiagramElement
type GroupBranch =
    { Condition: string option
      Elements: DiagramElement list }

and Group =
    { Kind: GroupKind
      Branches: GroupBranch list
      Position: SourcePosition }

// T009: DiagramElement
and DiagramElement =
    | ParticipantDecl of Participant
    | MessageElement of Message
    | NoteElement of Note
    | GroupElement of Group
    | TitleDirective of title: string * position: SourcePosition
    | AutoNumberDirective of position: SourcePosition

// T010: Diagram
type Diagram =
    { Title: string option
      AutoNumber: bool
      Participants: Participant list
      Elements: DiagramElement list }

// T011: ParseFailure, ParseWarning, ParseResult
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
    { Diagram: Diagram
      Errors: ParseFailure list
      Warnings: ParseWarning list }
