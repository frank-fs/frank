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

/// Options controlling smcat generation behavior.
type GenerateOptions = { ResourceName: string }

/// Generate valid smcat text from StateMachineMetadata.
/// Uses precomputed GuardNames and StateMetadataMap — no reflection needed.
/// The output is a single string with newline-separated statements.
/// - Initial state transition is emitted first
/// - Self-messages for each (state, HTTP method) handler pair
/// - Final state transitions are emitted last
val generate : options:GenerateOptions -> metadata:StateMachineMetadata -> string

/// Generate valid smcat text and write directly to a TextWriter.
/// The caller owns the writer lifecycle.
val generateTo : writer:System.IO.TextWriter -> options:GenerateOptions -> metadata:StateMachineMetadata -> unit

/// Format a transition label from optional components.
/// Format: "event [guard] / action" with absent components omitted.
val internal formatLabel : eventName:string option -> guardName:string option -> actionName:string option -> string option

/// Format a single transition line: "source => target: label;" or "source => target;"
val internal formatTransition : source:string -> target:string -> label:string option -> string
```

**Note**: The generator takes `StateMachineMetadata` (the untyped endpoint metadata record) rather than the generic `StateMachine<'S,'E,'C>`. Guard names and state metadata are precomputed at registration time via the `GuardNames` and `StateMetadataMap` fields — no runtime reflection is used.

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
