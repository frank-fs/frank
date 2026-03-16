# Data Model: WSD Generator

**Feature**: 017-wsd-generator-cross-validator
**Date**: 2026-03-15

## Overview

The WSD generator introduces two new modules (`Wsd/Generator.fs` and `Wsd/Serializer.fs`) and one new error type. All other types are reused from existing modules: `Wsd.Types` (AST) and `StateMachineMetadata` (runtime metadata).

## New Types

### GeneratorError (in Wsd/Generator.fs)

```fsharp
/// Error cases for WSD generation from StateMachineMetadata.
type GeneratorError =
    | UnrecognizedMachineType of typeName: string
    | NoStatesFound of resourceName: string
```

- `UnrecognizedMachineType`: The boxed `Machine: obj` could not be identified as a `StateMachine<_,_,_>` record. Includes the runtime type name for diagnostics.
- `NoStatesFound`: The `StateHandlerMap` is empty and the boxed machine yielded no state metadata. Includes the resource name (from the title/context) for diagnostics.

### GenerateOptions (in Wsd/Generator.fs)

```fsharp
/// Options controlling WSD generation behavior.
type GenerateOptions =
    { ResourceName: string }
```

- `ResourceName`: The name used in the `title` directive of the generated WSD. Typically the route template or a user-provided label.

## Reused Types (from Wsd/Types.fs, spec #90)

The generator constructs instances of these existing types:

- `Diagram = { Title: string option; AutoNumber: bool; Participants: Participant list; Elements: DiagramElement list }`
- `Participant = { Name: string; Alias: string option; Explicit: bool; Position: SourcePosition }`
- `Message = { Sender: string; Receiver: string; ArrowStyle: ArrowStyle; Direction: Direction; Label: string; Parameters: string list; Position: SourcePosition }`
- `Note = { NotePosition: NotePosition; Target: string; Content: string; Guard: GuardAnnotation option; Position: SourcePosition }`
- `GuardAnnotation = { Pairs: (string * string) list; Position: SourcePosition }`
- `DiagramElement = ParticipantDecl of Participant | MessageElement of Message | NoteElement of Note | ...`
- `ArrowStyle = Solid | Dashed`
- `Direction = Forward | Deactivating`
- `NotePosition = Over | LeftOf | RightOf`
- `SourcePosition = { Line: int; Column: int }` (struct)

## Reused Types (from StatefulResourceBuilder.fs)

The generator reads from this existing type:

- `StateMachineMetadata = { Machine: obj; StateHandlerMap: Map<string, (string * RequestDelegate) list>; InitialStateKey: string; ... }`
- `StateMachine<'S,'E,'C> = { Initial: 'S; InitialContext: 'C; Transition: ...; Guards: Guard<'S,'E,'C> list; StateMetadata: Map<'S, StateInfo> }`
- `Guard<'S,'E,'C> = { Name: string; Predicate: ... }`
- `StateInfo = { AllowedMethods: string list; IsFinal: bool; Description: string option }`

## Function Signatures

### Wsd/Generator.fs

```fsharp
module internal Frank.Statecharts.Wsd.Generator

/// Generate a WSD Diagram AST from StateMachineMetadata.
/// Returns Ok(Diagram) on success or Error(GeneratorError) on failure.
val generate : options: GenerateOptions -> metadata: StateMachineMetadata -> Result<Diagram, GeneratorError>
```

### Wsd/Serializer.fs

```fsharp
module internal Frank.Statecharts.Wsd.Serializer

/// Serialize a Diagram AST to WSD text with Unix line endings.
val serialize : diagram: Diagram -> string

/// Check if a participant name requires quoting in WSD output.
val needsQuoting : name: string -> bool

/// Quote a participant name if it contains non-identifier characters.
val quoteName : name: string -> string
```

## Data Flow

```
StateMachineMetadata
    |
    | Generator.generate (extracts states, transitions, guards)
    v
Diagram (Wsd.Types AST)
    |
    | Serializer.serialize (formats to text)
    v
string (WSD text)
    |
    | Parser.parseWsd (roundtrip verification)
    v
ParseResult (confirms roundtrip fidelity)
```

## Field Mapping: StateMachineMetadata -> Diagram

| Source (StateMachineMetadata) | Target (Diagram) | Notes |
|-------------------------------|-------------------|-------|
| `InitialStateKey` | First `Participant` in list | FR-004: initial state is first participant |
| `StateHandlerMap` keys | `Participant` names | FR-003: one participant per state |
| `StateHandlerMap` values (method names) | `Message` labels | FR-005: event = HTTP method name |
| Resource name (from `GenerateOptions`) | `Diagram.Title` | FR-009: title directive |
| Boxed `Machine` -> `Guards` -> `Name` | `GuardAnnotation.Pairs` | FR-007: guard annotations |
| All transitions | `ArrowStyle.Solid`, `Direction.Forward` | FR-006: default arrow style |

## SourcePosition Convention for Generated AST

Since the generator constructs AST nodes programmatically (not from parsed source text), all `SourcePosition` fields use a synthetic position: `{ Line = 0; Column = 0 }`. This convention:
- Distinguishes generated nodes from parsed nodes
- Is consistent (all generated positions are identical)
- Does not conflict with real positions (which start at line 1, column 1)
