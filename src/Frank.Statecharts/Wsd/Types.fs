module internal Frank.Statecharts.Wsd.Types

open Frank.Statecharts.Ast

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
