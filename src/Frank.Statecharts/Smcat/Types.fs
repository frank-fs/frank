module internal Frank.Statecharts.Smcat.Types

open System
open Frank.Statecharts.Ast

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

// Key-value attribute pair attached to states or transitions.
type SmcatAttribute = { Key: string; Value: string }

// Parsed label components for a transition.
type TransitionLabel =
    { Event: string option
      Guard: string option
      Action: string option }

// Infer state type from name and attributes.
// Priority order: attribute override > naming convention > Regular.
let inferStateType (name: string) (attributes: SmcatAttribute list) : StateKind =
    // 1. Attribute override takes highest priority (R-002).
    let typeAttr =
        attributes
        |> List.tryFind (fun a -> a.Key.Equals("type", StringComparison.OrdinalIgnoreCase))

    match typeAttr with
    | Some attr ->
        match attr.Value.ToLowerInvariant() with
        | "initial" -> StateKind.Initial
        | "final" -> StateKind.Final
        | "history" -> StateKind.ShallowHistory
        | "deep.history" -> StateKind.DeepHistory
        | "choice" -> StateKind.Choice
        | "forkjoin" -> StateKind.ForkJoin
        | "terminate" -> StateKind.Terminate
        | "regular" -> StateKind.Regular
        | _ -> StateKind.Regular
    | None ->
        // 2-8. Naming convention checks in priority order.
        if name.Contains("initial", StringComparison.OrdinalIgnoreCase) then StateKind.Initial
        elif name.Contains("final", StringComparison.OrdinalIgnoreCase) then StateKind.Final
        elif name.Contains("deep.history", StringComparison.OrdinalIgnoreCase) then StateKind.DeepHistory
        elif name.Contains("history", StringComparison.OrdinalIgnoreCase) then StateKind.ShallowHistory
        elif name.StartsWith('^') then StateKind.Choice
        elif name.StartsWith(']') then StateKind.ForkJoin
        elif name.Contains("terminate", StringComparison.OrdinalIgnoreCase) then StateKind.Terminate
        else StateKind.Regular
