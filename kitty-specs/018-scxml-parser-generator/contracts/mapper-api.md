# AST Mapper API Contract

**Module**: `Frank.Statecharts.Scxml.AstMapper` (internal)
**Blocked on**: Spec 020 (shared statechart AST) landing

## Public API Surface (preliminary)

```fsharp
module internal Frank.Statecharts.Scxml.AstMapper

open Frank.Statecharts.Scxml.Types
// open Frank.Statecharts.Ast.Types  // from spec 020

/// Convert an SCXML-specific document to the shared AST.
/// Maps ScxmlState -> StateNode, ScxmlTransition -> TransitionEdge, etc.
/// History and Invoke nodes become ScxmlAnnotation values on the shared AST.
val toSharedAst : ScxmlDocument -> StatechartDocument

/// Convert a shared AST back to SCXML-specific types.
/// Extracts ScxmlAnnotation values from shared AST annotations.
/// Non-SCXML annotations are ignored.
val fromSharedAst : StatechartDocument -> ScxmlDocument
```

## Mapping Rules (preliminary)

| SCXML Type | Shared AST Type | Notes |
|-----------|----------------|-------|
| `ScxmlDocument` | `StatechartDocument` | `Name` -> `Title`, `InitialId` -> `InitialStateId` |
| `ScxmlState` (Simple) | `StateNode` (Regular) | Direct mapping |
| `ScxmlState` (Compound) | `StateNode` (Regular) with children | Children populated |
| `ScxmlState` (Parallel) | `StateNode` (Parallel) | Kind mapped |
| `ScxmlState` (Final) | `StateNode` (Final) | Kind mapped |
| `ScxmlTransition` | `TransitionEdge` | One edge per target (multi-target expansion) |
| `DataEntry` | `DataEntry` | Direct mapping |
| `ScxmlHistory` | `StateNode` (ShallowHistory/DeepHistory) | As child of parent + ScxmlAnnotation |
| `ScxmlInvoke` | ScxmlAnnotation on parent `StateNode` | Non-functional annotation |

## Notes

- This module cannot be implemented until spec 020 defines the shared AST types
- The mapping rules above are preliminary and will be finalized once spec 020 lands
- Multi-target transitions produce one `TransitionEdge` per target (per spec FR-007)
- History and invoke are preserved as `ScxmlAnnotation` DU cases on the shared AST
