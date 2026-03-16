# API Signatures: smcat Shared AST Migration

**Feature**: 022-smcat-shared-ast-migration
**Date**: 2026-03-16

## Module: `Frank.Statecharts.Smcat.Parser`

### Before (current)

```fsharp
module internal Frank.Statecharts.Smcat.Parser

/// Parse a token list into an SmcatDocument.
val parse : tokens:Token list -> maxErrors:int -> ParseResult

/// Convenience: tokenize + parse in one call.
val parseSmcat : source:string -> ParseResult
```

Where `ParseResult` is the smcat-local type containing `SmcatDocument`.

### After (migrated)

```fsharp
module internal Frank.Statecharts.Smcat.Parser

open Frank.Statecharts.Ast

/// Parse a token list into a StatechartDocument.
val parse : tokens:Token list -> maxErrors:int -> ParseResult

/// Convenience: tokenize + parse in one call.
val parseSmcat : source:string -> ParseResult
```

Where `ParseResult` is `Ast.ParseResult` containing `StatechartDocument`.

---

## Module: `Frank.Statecharts.Smcat.Serializer` (NEW)

```fsharp
module internal Frank.Statecharts.Smcat.Serializer

open Frank.Statecharts.Ast

/// Serialize a StatechartDocument AST to smcat text.
val serialize : document:StatechartDocument -> string
```

Internal helpers (not exposed):
- `needsQuoting : name:string -> bool`
- `quoteName : name:string -> string`
- `stateKindToTypeString : kind:StateKind -> string option`
- `serializeActivities : sb:StringBuilder -> activities:StateActivities -> unit`
- `serializeAttributes : sb:StringBuilder -> annotations:Annotation list -> unit`
- `serializeState : sb:StringBuilder -> indent:string -> node:StateNode -> unit`
- `serializeTransition : sb:StringBuilder -> edge:TransitionEdge -> unit`

---

## Module: `Frank.Statecharts.Smcat.Generator`

### Before (current)

```fsharp
module internal Frank.Statecharts.Smcat.Generator

type GenerateOptions = { ResourceName: string }

/// Format a transition label from optional components.
val internal formatLabel : eventName:string option -> guardName:string option -> actionName:string option -> string option

/// Format a single transition line.
val internal formatTransition : source:string -> target:string -> label:string option -> string

/// Generate valid smcat text from StateMachineMetadata.
val generate : options:GenerateOptions -> metadata:StateMachineMetadata -> string

/// Generate and write to a TextWriter.
val generateTo : writer:TextWriter -> options:GenerateOptions -> metadata:StateMachineMetadata -> unit
```

### After (migrated)

```fsharp
module internal Frank.Statecharts.Smcat.Generator

open Frank.Statecharts.Ast

type GenerateOptions = { ResourceName: string }

type GeneratorError =
    | UnrecognizedMachineType of typeName: string

/// Generate a StatechartDocument AST from StateMachineMetadata.
val generate : options:GenerateOptions -> metadata:StateMachineMetadata -> Result<StatechartDocument, GeneratorError>
```

Deleted functions: `formatLabel`, `formatTransition`, `generateTo` (text formatting moves to `Serializer.fs`).

---

## Module: `Frank.Statecharts.Smcat.Types`

### Before (current)

```fsharp
module internal Frank.Statecharts.Smcat.Types

[<Struct>] type SourcePosition = { Line: int; Column: int }
type TokenKind = ...
[<Struct>] type Token = { Kind: TokenKind; Position: SourcePosition }
type StateType = Regular | Initial | Final | ShallowHistory | DeepHistory | Choice | ForkJoin | Terminate
type StateActivity = { Entry: string option; Exit: string option; Do: string option }
type SmcatAttribute = { Key: string; Value: string }
type TransitionLabel = { Event: string option; Guard: string option; Action: string option }
type SmcatState = { ... }
and SmcatTransition = { ... }
and SmcatElement = StateDeclaration of SmcatState | TransitionElement of SmcatTransition | CommentElement of string
and SmcatDocument = { Elements: SmcatElement list }
type ParseFailure = { ... }
type ParseWarning = { ... }
type ParseResult = { Document: SmcatDocument; Errors: ParseFailure list; Warnings: ParseWarning list }
val inferStateType : name:string -> attributes:SmcatAttribute list -> StateType
```

### After (migrated)

```fsharp
module internal Frank.Statecharts.Smcat.Types

open Frank.Statecharts.Ast

type TokenKind = ...  // unchanged
[<Struct>] type Token = { Kind: TokenKind; Position: SourcePosition }  // uses Ast.SourcePosition
type SmcatAttribute = { Key: string; Value: string }  // unchanged
type TransitionLabel = { Event: string option; Guard: string option; Action: string option }  // unchanged
val inferStateType : name:string -> attributes:SmcatAttribute list -> StateKind  // returns Ast.StateKind
```

Deleted: `SourcePosition`, `StateType`, `StateActivity`, `SmcatState`, `SmcatTransition`, `SmcatElement`, `SmcatDocument`, `ParseFailure`, `ParseWarning`, `ParseResult`.

---

## Module: `Frank.Statecharts.Smcat.LabelParser`

### Before (current)

```fsharp
module internal Frank.Statecharts.Smcat.LabelParser

val parseLabel : label:string -> position:SourcePosition -> TransitionLabel * ParseWarning list
```

Where `SourcePosition` and `ParseWarning` are from smcat-local `Types`.

### After (migrated)

```fsharp
module internal Frank.Statecharts.Smcat.LabelParser

open Frank.Statecharts.Ast

val parseLabel : label:string -> position:SourcePosition -> TransitionLabel * ParseWarning list
```

Where `SourcePosition` and `ParseWarning` are from `Ast`. The `ParseWarning.Position` changes from `SourcePosition` to `SourcePosition option` (wrapped in `Some`).
