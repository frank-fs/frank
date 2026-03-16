# Data Model: SCXML Shared AST Migration

**Feature**: 024-scxml-shared-ast-migration
**Date**: 2026-03-16

## Entity Changes

### Modified Entity: ScxmlMeta (in `Ast/Types.fs`)

**Current definition** (3 cases):
```fsharp
type ScxmlMeta =
    | ScxmlInvoke of invokeType: string * src: string option
    | ScxmlHistory of id: string * historyKind: HistoryKind
    | ScxmlNamespace of string
```

**New definition** (8 cases):
```fsharp
type ScxmlMeta =
    | ScxmlInvoke of invokeType: string * src: string option * id: string option
    | ScxmlHistory of id: string * historyKind: HistoryKind * defaultTarget: string option
    | ScxmlNamespace of string
    | ScxmlTransitionType of internal: bool
    | ScxmlMultiTarget of targets: string list
    | ScxmlDatamodelType of datamodel: string
    | ScxmlBinding of binding: string
    | ScxmlInitial of initialId: string
```

**Migration impact on existing consumers**:
- `ScxmlInvoke` gains a third parameter (`id`) -- existing pattern matches `ScxmlInvoke(t, s)` must be updated to `ScxmlInvoke(t, s, _)` or `ScxmlInvoke(t, s, id)`
- `ScxmlHistory` gains a third parameter (`defaultTarget`) -- existing pattern matches `ScxmlHistory(id, kind)` must be updated to `ScxmlHistory(id, kind, _)` or `ScxmlHistory(id, kind, dt)`
- Affected files: `Scxml/Mapper.fs` (being deleted), any tests that pattern-match on these cases

### Deleted Entities (from `Scxml/Types.fs`)

| Type | Replacement |
|---|---|
| `ScxmlDocument` | `Ast.StatechartDocument` |
| `ScxmlState` | `Ast.StateNode` |
| `ScxmlTransition` | `Ast.TransitionEdge` + `ScxmlAnnotation(ScxmlTransitionType/ScxmlMultiTarget)` |
| `ScxmlParseResult` | `Ast.ParseResult` |
| `ScxmlStateKind` | `Ast.StateKind` (Regular/Parallel/Final) |
| `DataEntry` (SCXML) | `Ast.DataEntry` (Name replaces Id) |
| `ParseError` (SCXML) | `Ast.ParseFailure` |
| `ParseWarning` (SCXML) | `Ast.ParseWarning` |

### Retained Entities (in `Scxml/Types.fs`)

| Type | Purpose |
|---|---|
| `SourcePosition` (struct) | Parser-internal use during XML parsing, before conversion to `Ast.SourcePosition` |
| `ScxmlTransitionType` (DU: Internal/External) | Parser-internal: used during XML attribute parsing before converting to `ScxmlMeta.ScxmlTransitionType` annotation |
| `ScxmlHistoryKind` (DU: Shallow/Deep) | Parser-internal: used during XML attribute parsing before converting to `Ast.HistoryKind` |

### Deleted Module

| Module | Replacement |
|---|---|
| `Frank.Statecharts.Scxml.Mapper` | Logic absorbed into Parser.fs (parse direction) and Generator.fs (generate direction) |

## Field Mapping Details

### Parser: SCXML attributes to shared AST

| SCXML attribute/element | Shared AST field | Annotation (if needed) |
|---|---|---|
| `<scxml name="...">` | `StatechartDocument.Title` | -- |
| `<scxml initial="...">` | `StatechartDocument.InitialStateId` | -- |
| `<scxml datamodel="...">` | -- | `ScxmlAnnotation(ScxmlDatamodelType(...))` on document |
| `<scxml binding="...">` | -- | `ScxmlAnnotation(ScxmlBinding(...))` on document |
| `<state id="...">` | `StateNode.Identifier` | -- |
| `<state initial="...">` | -- | `ScxmlAnnotation(ScxmlInitial(...))` on state |
| `<state>` (simple) | `StateNode.Kind = Regular` | -- |
| `<state>` (compound) | `StateNode.Kind = Regular` + children | -- |
| `<parallel>` | `StateNode.Kind = Parallel` | -- |
| `<final>` | `StateNode.Kind = Final` | -- |
| `<transition event="...">` | `TransitionEdge.Event` | -- |
| `<transition cond="...">` | `TransitionEdge.Guard` | -- |
| `<transition target="t1">` | `TransitionEdge.Target = Some "t1"` | -- |
| `<transition target="t1 t2 t3">` | `TransitionEdge.Target = Some "t1"` | `ScxmlAnnotation(ScxmlMultiTarget(["t1";"t2";"t3"]))` |
| `<transition type="internal">` | -- | `ScxmlAnnotation(ScxmlTransitionType(true))` |
| `<transition type="external">` | -- | (no annotation, external is default) |
| `<history id="..." type="deep">` | `StateNode.Kind = DeepHistory`, child of parent | `ScxmlAnnotation(ScxmlHistory("id", Deep, defaultTarget))` |
| `<history id="..." type="shallow">` | `StateNode.Kind = ShallowHistory`, child of parent | `ScxmlAnnotation(ScxmlHistory("id", Shallow, defaultTarget))` |
| `<invoke type="..." src="..." id="...">` | -- | `ScxmlAnnotation(ScxmlInvoke(type, src, id))` on parent state |
| `<data id="..." expr="...">` | `DataEntry.Name`, `DataEntry.Expression` | -- |

### Generator: shared AST to SCXML elements

| Shared AST source | SCXML output |
|---|---|
| `StatechartDocument.Title` | `<scxml name="...">` |
| `StatechartDocument.InitialStateId` | `<scxml initial="...">` |
| `ScxmlAnnotation(ScxmlDatamodelType(...))` | `<scxml datamodel="...">` |
| `ScxmlAnnotation(ScxmlBinding(...))` | `<scxml binding="...">` |
| `StateNode.Kind = Regular` (no children) | `<state>` |
| `StateNode.Kind = Regular` (with children) | `<state>` (compound) |
| `StateNode.Kind = Parallel` | `<parallel>` |
| `StateNode.Kind = Final` | `<final>` |
| `StateNode.Kind = Initial` | `<state>` (fallback) |
| `StateNode.Kind = ShallowHistory/DeepHistory` | `<history type="shallow/deep">` |
| `ScxmlAnnotation(ScxmlHistory(_, _, Some target))` | `<history><transition target="..."/></history>` |
| `ScxmlAnnotation(ScxmlInvoke(type, src, id))` | `<invoke type="..." src="..." id="...">` |
| `ScxmlAnnotation(ScxmlTransitionType(true))` | `<transition type="internal">` |
| `ScxmlAnnotation(ScxmlMultiTarget(targets))` | `<transition target="t1 t2 t3">` |
| `ScxmlAnnotation(ScxmlInitial(id))` | `<state initial="...">` |
| `DataEntry` | `<datamodel><data id="..." expr="..."></datamodel>` |
