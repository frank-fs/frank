# API Signatures: smcat Parser and Generator

**Feature**: 013-smcat-parser-generator
**Date**: 2026-03-15

All modules are `internal` to `Frank.Statecharts`.

## Smcat.Lexer

```fsharp
module internal Frank.Statecharts.Smcat.Lexer

/// Tokenize an smcat source string into a flat token list.
/// Handles comments (# lines), quoted strings, state identifiers,
/// transition arrows (=>), attributes ([key=value]), activities (entry/, exit/, ...),
/// composite state braces ({ }), and statement terminators (; ,).
/// Handles both \r\n and \n line endings.
val tokenize : source:string -> Token list
```

## Smcat.LabelParser

```fsharp
module internal Frank.Statecharts.Smcat.LabelParser

/// Parse a transition label string in the format "event [guard] / action".
/// Each component is optional. Returns a TransitionLabel record.
/// Also returns any parse warnings (e.g., unclosed brackets).
val parseLabel : label:string -> position:SourcePosition -> TransitionLabel * ParseWarning list
```

## Smcat.Parser

```fsharp
module internal Frank.Statecharts.Smcat.Parser

/// Parse a token list into an SmcatDocument, collecting errors and warnings.
/// maxErrors controls the maximum number of ParseFailure entries before
/// the parser stops attempting further recovery (default: 50).
val parse : tokens:Token list -> maxErrors:int -> ParseResult

/// Convenience: tokenize + parse in one call.
val parseSmcat : source:string -> ParseResult
```

## Smcat.Generator

```fsharp
module internal Frank.Statecharts.Smcat.Generator

/// Generate valid smcat text from StateMachineMetadata.
/// The output is a single string with newline-separated statements.
/// - Initial state transition is emitted first
/// - Final state transitions are emitted last
/// - Transition labels include event [guard] / action as applicable
/// - State names use the string representation of the generic 'State type
val generate<'State, 'Event, 'Context when 'State : equality and 'State : comparison> :
    metadata:StateMachineMetadata<'State, 'Event, 'Context> ->
    stateNames:('State -> string) ->
    eventNames:('Event -> string) ->
    transitions:('State * 'Event * 'State) list ->
    string
```

**Note on Generator API**: Because `StateMachineMetadata` stores its `Transition` function as a closure (not a declarative transition table), the generator cannot enumerate all transitions automatically. The caller must provide:
- `stateNames`: Function to convert a state DU case to its smcat name
- `eventNames`: Function to convert an event DU case to its smcat name
- `transitions`: Explicit list of (source, event, target) triples to render

This is consistent with the spec's assumption that "the mapper produces an intermediate representation rather than a live StateMachineMetadata with closures."

## Smcat.Mapper (Blocked on Spec 020)

```fsharp
module internal Frank.Statecharts.Smcat.Mapper

/// Map a parsed SmcatDocument to the shared StatechartDocument AST.
/// Converts SmcatState -> StateNode, SmcatTransition -> TransitionEdge,
/// attaches SmcatAnnotation values for format-specific metadata.
/// Returns a ParseResult<StatechartDocument>.
val mapToSharedAst : smcatResult:Smcat.Types.ParseResult -> (* StatechartDocument ParseResult -- type from spec 020 *)
```

**This module is not implementable until spec 020 defines `StatechartDocument`, `StateNode`, `TransitionEdge`, `StateKind`, `SmcatAnnotation`, and the shared `ParseResult<'T>` type.**

## Pseudo-State Inference

```fsharp
/// Infer StateType from a state name using smcat naming conventions.
/// This is used by both the Parser (during AST construction) and the
/// Mapper (during shared AST conversion).
/// Exposed as a module-level function in Types.fs for testability.
val inferStateType : name:string -> attributes:SmcatAttribute list -> StateType
```
