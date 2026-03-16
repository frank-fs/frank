# Data Model: SCXML Parser and Generator

**Feature**: 018-scxml-parser-generator
**Date**: 2026-03-16

## SCXML-Specific Parse Types

These types live in `src/Frank.Statecharts/Scxml/Types.fs` under `module internal Frank.Statecharts.Scxml.Types`. They represent the parsed SCXML document structure independent of the shared AST (spec 020).

### SourcePosition

Reuse or mirror the existing WSD `SourcePosition` struct. If the shared AST (spec 020) defines a common `SourcePosition`, use that. Otherwise, define locally.

```fsharp
[<Struct>]
type SourcePosition = { Line: int; Column: int }
```

### ScxmlTransitionType

```fsharp
type ScxmlTransitionType =
    | Internal
    | External
```

### ScxmlHistoryKind

```fsharp
type ScxmlHistoryKind =
    | Shallow
    | Deep
```

### ScxmlStateKind

```fsharp
type ScxmlStateKind =
    | Simple       // <state> with no child states (atomic)
    | Compound     // <state> with child states
    | Parallel     // <parallel>
    | Final        // <final>
```

### DataEntry

A name/expression pair from `<data id="..." expr="...">` or `<data id="...">child content</data>`.

```fsharp
type DataEntry =
    { Id: string
      Expression: string option   // expr attribute or child text content
      Position: SourcePosition option }
```

### ScxmlTransition

A transition parsed from `<transition event="..." cond="..." target="..." type="...">`.

```fsharp
type ScxmlTransition =
    { Event: string option           // event attribute (may be space-separated event descriptors)
      Guard: string option           // cond attribute
      Targets: string list           // target attribute split on spaces (may be empty for targetless transitions)
      TransitionType: ScxmlTransitionType  // type attribute, defaults to External
      Position: SourcePosition option }
```

**Design note**: `Targets` is a `string list` rather than `string option` because W3C SCXML section 3.5 allows space-separated target IDs. An empty list represents a targetless (internal/completion) transition.

### ScxmlHistory

A history pseudo-state parsed from `<history id="..." type="...">`.

```fsharp
type ScxmlHistory =
    { Id: string
      Kind: ScxmlHistoryKind           // defaults to Shallow when absent
      DefaultTransition: ScxmlTransition option  // child <transition> specifying default target
      Position: SourcePosition option }
```

### ScxmlInvoke

An invocation annotation parsed from `<invoke type="..." src="..." id="...">`.

```fsharp
type ScxmlInvoke =
    { InvokeType: string option    // type attribute
      Src: string option           // src attribute
      Id: string option            // id attribute
      Position: SourcePosition option }
```

### ScxmlState

A state node parsed from `<state>`, `<parallel>`, or `<final>`.

```fsharp
type ScxmlState =
    { Id: string option               // id attribute (optional per W3C but practically always present)
      Kind: ScxmlStateKind            // derived from element name and presence of children
      InitialId: string option        // initial attribute (for compound states)
      Transitions: ScxmlTransition list
      Children: ScxmlState list       // child states (for compound/parallel)
      DataEntries: DataEntry list     // state-scoped <datamodel>/<data> entries
      HistoryNodes: ScxmlHistory list  // <history> pseudo-states
      InvokeNodes: ScxmlInvoke list   // <invoke> annotations
      Position: SourcePosition option }
```

**Design note**: `DataEntries` is stored directly on the state node per PQ2 decision. This mirrors the SCXML document structure where `<datamodel>` can appear inside `<state>`.

### ScxmlDocument

Root type representing a complete parsed SCXML document.

```fsharp
type ScxmlDocument =
    { Name: string option              // name attribute on <scxml>
      InitialId: string option         // initial attribute on <scxml>, or inferred from first child
      DatamodelType: string option     // datamodel attribute ("ecmascript", "xpath", etc.)
      Binding: string option           // binding attribute ("early" or "late")
      States: ScxmlState list          // top-level child states
      DataEntries: DataEntry list      // top-level <datamodel>/<data> entries
      Position: SourcePosition option }
```

### ParseError

Structured error result for malformed XML or invalid SCXML structure.

```fsharp
type ParseError =
    { Description: string
      Position: SourcePosition option }
```

### ParseWarning

Non-fatal issues detected during parsing.

```fsharp
type ParseWarning =
    { Description: string
      Position: SourcePosition option
      Suggestion: string option }
```

### ScxmlParseResult

Result type combining the document with any errors/warnings.

```fsharp
type ScxmlParseResult =
    { Document: ScxmlDocument option   // None only for hard XML parse failures
      Errors: ParseError list
      Warnings: ParseWarning list }
```

## Relationships

```
ScxmlDocument (root)
├── Name, InitialId, DatamodelType, Binding (metadata)
├── DataEntries: DataEntry list (top-level data model)
└── States: ScxmlState list
    ├── Id, Kind, InitialId (identity)
    ├── Transitions: ScxmlTransition list
    │   └── Event, Guard, Targets, TransitionType
    ├── Children: ScxmlState list (recursive for compound/parallel)
    ├── DataEntries: DataEntry list (state-scoped data model)
    ├── HistoryNodes: ScxmlHistory list
    │   └── Id, Kind, DefaultTransition
    └── InvokeNodes: ScxmlInvoke list
        └── InvokeType, Src, Id
```

## Structural Equality

All types are F# records (no mutable fields, no reference equality). This ensures structural equality semantics for comparison-based testing (SC-006) and roundtrip verification (SC-003).

## State Kind Derivation Rules

| XML Element | Has Children? | ScxmlStateKind |
|-------------|--------------|----------------|
| `<state>` | No child `<state>`/`<parallel>`/`<final>` | `Simple` |
| `<state>` | Has child `<state>`/`<parallel>`/`<final>` | `Compound` |
| `<parallel>` | (always) | `Parallel` |
| `<final>` | (always) | `Final` |
