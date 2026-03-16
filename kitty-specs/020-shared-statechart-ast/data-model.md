# Data Model: Shared Statechart AST (Spec 020)

**Date**: 2026-03-15
**Status**: Complete
**Derived From**: research.md decisions D-001 through D-013

---

## Entity Relationship Overview

```
StatechartDocument (root)
  |-- Title: string option
  |-- InitialStateId: string option
  |-- Elements: StatechartElement list (ordered)
  |-- DataEntries: DataEntry list
  |-- Annotations: Annotation list
  |
  +-- StatechartElement (DU)
       |-- StateDecl of StateNode
       |-- TransitionElement of TransitionEdge
       |-- NoteElement of NoteContent
       |-- GroupElement of GroupBlock
       |-- DirectiveElement of Directive
```

```
StateNode
  |-- Identifier: string (unique within document)
  |-- Label: string option
  |-- Kind: StateKind (DU: 9 cases)
  |-- Children: StateNode list (hierarchy)
  |-- Activities: StateActivities option
  |-- Position: SourcePosition option
  |-- Annotations: Annotation list
```

```
TransitionEdge
  |-- Source: string (state identifier)
  |-- Target: string option (None = internal transition)
  |-- Event: string option
  |-- Guard: string option
  |-- Action: string option
  |-- Parameters: string list
  |-- Position: SourcePosition option
  |-- Annotations: Annotation list
```

---

## Core Entities

### StatechartDocument (FR-001)

The root AST node representing a complete parsed statechart.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Title | `string option` | No | Document title (from WSD `title` directive, SCXML `name` attribute) |
| InitialStateId | `string option` | No | Identifier of the initial state (SCXML `initial`, smcat `initial`, XState `initial`) |
| Elements | `StatechartElement list` | Yes (may be empty) | Ordered list of all elements preserving source order |
| DataEntries | `DataEntry list` | Yes (may be empty) | Top-level data model entries (SCXML `<datamodel>`, XState `context`) |
| Annotations | `Annotation list` | Yes (may be empty) | Document-level format-specific annotations |

**Structural Equality**: Yes (all fields are value types or immutable lists)
**Notes**: An empty document (no states, no transitions) is valid per edge case specification.

---

### StateNode (FR-002)

A state within the statechart, representing participants (WSD), descriptors (ALPS), `<state>` elements (SCXML), state names (smcat), or state nodes (XState).

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Identifier | `string` | Yes | Unique name within the document scope |
| Label | `string option` | No | Display label (WSD alias, smcat label attribute) |
| Kind | `StateKind` | Yes | Type classification (defaults to `Regular`) |
| Children | `StateNode list` | Yes (may be empty) | Child states for hierarchical/compound states |
| Activities | `StateActivities option` | No | Entry/exit/do actions (SCXML `<onentry>`/`<onexit>`, smcat `entry/`/`exit/`) |
| Position | `SourcePosition option` | No | Source location (None if programmatically constructed) |
| Annotations | `Annotation list` | Yes (may be empty) | Format-specific metadata |

**Structural Equality**: Yes
**Relationships**: Self-referential (Children contains StateNode list for hierarchy)

---

### TransitionEdge (FR-003)

A directed edge between states representing messages (WSD), transition descriptors (ALPS), `<transition>` elements (SCXML), arrows (smcat), or event handlers (XState).

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Source | `string` | Yes | Source state identifier |
| Target | `string option` | No | Target state identifier (None = internal/completion transition) |
| Event | `string option` | No | Event name triggering the transition |
| Guard | `string option` | No | Guard condition expression |
| Action | `string option` | No | Action description |
| Parameters | `string list` | Yes (may be empty) | Transition parameters (WSD message params) |
| Position | `SourcePosition option` | No | Source location |
| Annotations | `Annotation list` | Yes (may be empty) | Format-specific metadata (WSD arrow style, ALPS transition type) |

**Structural Equality**: Yes
**Notes**: Self-transition (Source = Target) is valid. Multiple transitions between same source/target with different events are valid.

---

### DataEntry (FR-004)

A data model variable, representing SCXML `<data>` elements, ALPS semantic descriptor values, XState context entries, or WSD message parameters.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Name | `string` | Yes | Variable name/identifier |
| Expression | `string option` | No | Initial value expression (empty for SCXML `<data id="x"/>`) |
| Position | `SourcePosition option` | No | Source location |

**Structural Equality**: Yes

---

### NoteContent

A textual note/comment attached to a participant or state, used by WSD `note` elements and potentially other formats.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Target | `string` | Yes | State/participant the note is attached to |
| Content | `string` | Yes | Note text (after guard extraction) |
| Position | `SourcePosition option` | No | Source location |
| Annotations | `Annotation list` | Yes (may be empty) | Format-specific metadata (WSD note position) |

**Structural Equality**: Yes

---

### GroupBlock (FR-015)

A control flow grouping structure (UML interaction fragments), used by WSD (alt/opt/loop/par/break/critical/ref) and smcat (composite states with conditions).

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Kind | `GroupKind` | Yes | Grouping type |
| Branches | `GroupBranch list` | Yes (at least 1) | Ordered branches, each with optional condition |
| Position | `SourcePosition option` | No | Source location |

**Structural Equality**: Yes

---

### GroupBranch

A branch within a GroupBlock, containing an optional condition and child elements.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Condition | `string option` | No | Branch condition text |
| Elements | `StatechartElement list` | Yes (may be empty) | Child elements within this branch |

**Structural Equality**: Yes

---

### StateActivities

Entry, exit, and do activities for a state.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Entry | `string list` | Yes (may be empty) | Actions on state entry |
| Exit | `string list` | Yes (may be empty) | Actions on state exit |
| Do | `string list` | Yes (may be empty) | Ongoing activities while in state |

**Structural Equality**: Yes

---

## Discriminated Unions

### StateKind (FR-005)

```fsharp
type StateKind =
    | Regular
    | Initial
    | Final
    | Parallel
    | ShallowHistory
    | DeepHistory
    | Choice
    | ForkJoin
    | Terminate
```

| Case | Used By | Description |
|------|---------|-------------|
| Regular | All formats | Default state type |
| Initial | SCXML, smcat, XState | Initial pseudo-state |
| Final | SCXML, smcat, XState | Final/accepting state |
| Parallel | SCXML, smcat, XState | Orthogonal/parallel region |
| ShallowHistory | SCXML, smcat | Shallow history pseudo-state |
| DeepHistory | SCXML, smcat | Deep history pseudo-state |
| Choice | smcat | Choice/decision pseudo-state |
| ForkJoin | smcat | Fork/join pseudo-state |
| Terminate | smcat | Terminate pseudo-state |

---

### HistoryKind (FR-019)

```fsharp
type HistoryKind =
    | Shallow
    | Deep
```

Used in SCXML annotations to specify history type on `<history>` elements.

---

### GroupKind (FR-021)

```fsharp
type GroupKind =
    | Alt
    | Opt
    | Loop
    | Par
    | Break
    | Critical
    | Ref
```

Reused from WSD parser. All 7 cases correspond to UML interaction fragment types.

---

### StatechartElement (FR-016)

```fsharp
type StatechartElement =
    | StateDecl of StateNode
    | TransitionElement of TransitionEdge
    | NoteElement of NoteContent
    | GroupElement of GroupBlock
    | DirectiveElement of Directive
```

Preserves source ordering of elements within the document.

---

### Directive

```fsharp
type Directive =
    | TitleDirective of title: string * position: SourcePosition option
    | AutoNumberDirective of position: SourcePosition option
```

Format-specific directives that affect rendering but are not statechart semantics.

---

### Annotation (FR-006)

```fsharp
type Annotation =
    | WsdAnnotation of WsdMeta
    | AlpsAnnotation of AlpsMeta
    | ScxmlAnnotation of ScxmlMeta
    | SmcatAnnotation of SmcatMeta
    | XStateAnnotation of XStateMeta
```

Each case carries typed data specific to the format.

---

### WsdMeta

```fsharp
type WsdMeta =
    | WsdTransitionStyle of TransitionStyle
    | WsdNotePosition of WsdNotePosition
```

### TransitionStyle (FR-020)

```fsharp
type ArrowStyle = Solid | Dashed
type Direction = Forward | Deactivating

type TransitionStyle =
    { ArrowStyle: ArrowStyle
      Direction: Direction }
```

### WsdNotePosition

```fsharp
type WsdNotePosition = Over | LeftOf | RightOf
```

---

### AlpsMeta (Stub)

```fsharp
type AlpsTransitionKind = Safe | Unsafe | Idempotent

type AlpsMeta =
    | AlpsTransitionType of AlpsTransitionKind
    | AlpsDescriptorHref of string
    | AlpsExtension of name: string * value: string
```

To be fleshed out by ALPS parser spec (#97).

---

### ScxmlMeta (Stub)

```fsharp
type ScxmlMeta =
    | ScxmlInvoke of invokeType: string * src: string option
    | ScxmlHistory of id: string * historyKind: HistoryKind
    | ScxmlNamespace of string
```

To be fleshed out by SCXML parser spec (#98).

---

### SmcatMeta (Stub)

```fsharp
type SmcatMeta =
    | SmcatColor of string
    | SmcatStateLabel of string
    | SmcatActivity of kind: string * body: string
```

To be fleshed out by smcat parser spec (#100).

---

### XStateMeta (Stub)

```fsharp
type XStateMeta =
    | XStateAction of string
    | XStateService of string
```

To be fleshed out by XState parser spec (forthcoming).

---

## Supporting Types

### SourcePosition (FR-007)

```fsharp
[<Struct>]
type SourcePosition = { Line: int; Column: int }
```

1-based line and column. Used as `option` on all AST node types.

---

### ParseFailure (FR-008)

```fsharp
type ParseFailure =
    { Position: SourcePosition option
      Description: string
      Expected: string
      Found: string
      CorrectiveExample: string }
```

**Change from WSD**: `Position` becomes `option` (some formats may not have position info for certain errors, e.g., XML parse errors from a library).

---

### ParseWarning (FR-009)

```fsharp
type ParseWarning =
    { Position: SourcePosition option
      Description: string
      Suggestion: string option }
```

**Change from WSD**: `Position` becomes `option`.

---

### ParseResult (FR-010)

```fsharp
type ParseResult =
    { Document: StatechartDocument
      Errors: ParseFailure list
      Warnings: ParseWarning list }
```

**Change from WSD**: `Diagram` field becomes `Document` of type `StatechartDocument`.

---

## Format Population Summary

| Entity | WSD | ALPS | SCXML | smcat | XState |
|--------|-----|------|-------|-------|--------|
| StatechartDocument.Title | From `title` directive | From doc metadata | From `name` attr | -- | From `id` |
| StatechartDocument.InitialStateId | First participant (implicit) | -- | From `initial` attr | From `initial` state | From `initial` prop |
| StateNode | From `participant` | From descriptor | From `<state>` | From state name | From state node |
| StateNode.Kind | Always `Regular` | Always `Regular` | Full range | Full range | Most cases |
| StateNode.Children | -- (empty) | From nested descriptors | From nested `<state>` | From composite | From nested |
| StateNode.Activities | -- | -- | From `<onentry>`/`<onexit>` | From `entry/`/`exit/` | From `entry`/`exit` |
| TransitionEdge | From message | From transition desc | From `<transition>` | From `=>` arrow | From `on:` handler |
| TransitionEdge.Guard | From `[guard:]` note | From `ext` elements | From `cond` attr | From `[cond]` label | From `cond` prop |
| DataEntry | -- | From descriptor values | From `<data>` | -- | From `context` |
| GroupBlock | From alt/opt/loop/par/... | -- | -- | From composite grouping | -- |
| NoteContent | From `note` element | -- | -- | -- | -- |
| Directive | From `title`/`autonumber` | -- | -- | -- | -- |

---

## File Structure

```
src/Frank.Statecharts/
  Ast/
    Types.fs          -- All shared AST types (SourcePosition, StateKind, StateNode, etc.)
  Wsd/
    Types.fs          -- WSD-specific lexer types only (TokenKind, Token)
    Lexer.fs          -- WSD lexer (imports SourcePosition from Ast.Types)
    GuardParser.fs    -- Guard annotation parser (imports from Ast.Types)
    Parser.fs         -- WSD parser (produces StatechartDocument)
```

The `Frank.Statecharts.fsproj` compile order must list `Ast/Types.fs` before `Wsd/Types.fs`.
