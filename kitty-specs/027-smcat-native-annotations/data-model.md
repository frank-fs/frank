# Data Model: smcat Native Annotations and Generator Fidelity

**Feature**: 027-smcat-native-annotations
**Date**: 2026-03-18

## New Types (added to `Ast/Types.fs`)

### SmcatTypeOrigin

```
SmcatTypeOrigin
├── Explicit    — state type declared via [type="..."] attribute in source
└── Inferred    — state type determined by naming convention or default (Regular)
```

**Relationships**: Used as a field in `SmcatStateType` case of `SmcatMeta`.

### SmcatTransitionKind

```
SmcatTransitionKind
├── InitialTransition    — initial => firstState (pseudo-state entry)
├── FinalTransition      — state => final (pseudo-state exit)
├── SelfTransition       — state => state (HTTP method capability)
├── ExternalTransition   — state => otherState (cross-state transition)
└── InternalTransition   — within composite, no exit/re-entry
```

**Relationships**: Wrapped by `SmcatTransition` case of `SmcatMeta`.

### SmcatMeta (expanded)

```
SmcatMeta (before)              SmcatMeta (after)
├── SmcatColor of string        ├── SmcatColor of string
├── SmcatStateLabel of string   ├── SmcatStateLabel of string
└── SmcatActivity of kind*body  ├── SmcatCustomAttribute of key*value  ← renamed
                                ├── SmcatStateType of StateKind * SmcatTypeOrigin  ← new
                                └── SmcatTransition of SmcatTransitionKind  ← new
```

## Annotation Placement Rules

### On StateNode

| State scenario | Kind field | SmcatStateType annotation | SmcatColor/Label/Custom |
|---------------|------------|---------------------------|------------------------|
| `idle;` (regular, no attr) | Regular | Absent (default) | As parsed |
| `initial;` (naming convention) | Initial | `(Initial, Inferred)` | As parsed |
| `myState [type="initial"];` | Initial | `(Initial, Explicit)` | As parsed |
| `myState [type="regular"];` | Regular | `(Regular, Explicit)` | As parsed |
| Generator initial pseudo-state | Initial | `(Initial, Explicit)` | None |
| Generator regular state | Regular | Absent (default) | None |

### On TransitionEdge

| Transition scenario | SmcatTransition annotation |
|--------------------|---------------------------|
| `initial => firstState;` | `InitialTransition` |
| `state => state: GET;` | `SelfTransition` |
| `state => final;` | `FinalTransition` |
| `state => otherState: event;` | `ExternalTransition` |
| Transition inside `{ }` block | `InternalTransition` |

## Serializer Annotation Consumption

### State Type Attribute Emission

```
Has SmcatStateType annotation?
├── Yes, origin = Explicit → emit [type="<kind>"] attribute
├── Yes, origin = Inferred → do NOT emit type attribute
└── No annotation → fallback to StateNode.Kind:
    ├── Kind = Regular → no type attribute
    └── Kind ≠ Regular → emit [type="<kind>"] attribute (cross-format fallback)
```

### StateKind → smcat type string mapping

| StateKind | smcat type value |
|-----------|-----------------|
| Regular | `"regular"` |
| Initial | `"initial"` |
| Final | `"final"` |
| Parallel | `"parallel"` |
| ShallowHistory | `"history"` |
| DeepHistory | `"deep.history"` |
| Choice | `"choice"` |
| ForkJoin | `"forkjoin"` |
| Terminate | `"terminate"` |

## Validation Rules

- `SmcatStateType` MUST NOT appear more than once per `StateNode.Annotations`
- `SmcatTransition` MUST NOT appear more than once per `TransitionEdge.Annotations`
- When `SmcatStateType` is present, its `kind` field MUST equal `StateNode.Kind` (consistency invariant)
- `SmcatTypeOrigin.Explicit` on a generator-produced state means the generator intentionally typed it (not that source text had an attribute)
