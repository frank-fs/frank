module internal Frank.Statecharts.Smcat.Types

open System

// Temporary duplicate of Wsd.Types.SourcePosition — will be unified by spec 020.
[<Struct>]
type SourcePosition = { Line: int; Column: int }

// Token types for the smcat lexer.
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

// State classification for pseudo-state detection.
type StateType =
    | Regular
    | Initial
    | Final
    | ShallowHistory
    | DeepHistory
    | Choice
    | ForkJoin
    | Terminate

// Activities that can be declared within a state.
type StateActivity =
    { Entry: string option
      Exit: string option
      Do: string option }

// Key-value attribute pair attached to states or transitions.
type SmcatAttribute = { Key: string; Value: string }

// Parsed label components for a transition.
type TransitionLabel =
    { Event: string option
      Guard: string option
      Action: string option }

// Mutually recursive AST types: SmcatState -> SmcatDocument -> SmcatElement -> SmcatState.
type SmcatState =
    { Name: string
      Label: string option
      StateType: StateType
      Activities: StateActivity option
      Attributes: SmcatAttribute list
      Children: SmcatDocument option
      Position: SourcePosition }

and SmcatTransition =
    { Source: string
      Target: string
      Label: TransitionLabel option
      Attributes: SmcatAttribute list
      Position: SourcePosition }

and SmcatElement =
    | StateDeclaration of SmcatState
    | TransitionElement of SmcatTransition
    | CommentElement of string

and SmcatDocument =
    { Elements: SmcatElement list }

// Parse result types.
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

// Infer state type from name and attributes.
// Priority order: attribute override > naming convention > Regular.
let inferStateType (name: string) (attributes: SmcatAttribute list) : StateType =
    // 1. Attribute override takes highest priority (R-002).
    let typeAttr =
        attributes
        |> List.tryFind (fun a -> a.Key.Equals("type", StringComparison.OrdinalIgnoreCase))

    match typeAttr with
    | Some attr ->
        match attr.Value.ToLowerInvariant() with
        | "initial" -> Initial
        | "final" -> Final
        | "history" -> ShallowHistory
        | "deep.history" -> DeepHistory
        | "choice" -> Choice
        | "forkjoin" -> ForkJoin
        | "terminate" -> Terminate
        | "regular" -> Regular
        | _ -> Regular
    | None ->
        // 2-8. Naming convention checks in priority order.
        if name.Contains("initial", StringComparison.OrdinalIgnoreCase) then Initial
        elif name.Contains("final", StringComparison.OrdinalIgnoreCase) then Final
        elif name.Contains("deep.history", StringComparison.OrdinalIgnoreCase) then DeepHistory
        elif name.Contains("history", StringComparison.OrdinalIgnoreCase) then ShallowHistory
        elif name.StartsWith('^') then Choice
        elif name.StartsWith(']') then ForkJoin
        elif name.Contains("terminate", StringComparison.OrdinalIgnoreCase) then Terminate
        else Regular
