# Research: smcat Native Annotations and Generator Fidelity

**Feature**: 027-smcat-native-annotations
**Date**: 2026-03-18

## R-001: SmcatMeta DU Expansion Design

**Decision**: Expand `SmcatMeta` with 2 new cases and 2 new supporting DUs, rename 1 existing case.

**New types in `Ast/Types.fs`:**

```fsharp
/// Tracks whether a state's type was declared via [type="..."] attribute
/// or inferred from naming convention.
type SmcatTypeOrigin = Explicit | Inferred

/// Semantic role of a transition in smcat format.
type SmcatTransitionKind =
    | InitialTransition   // initial => firstState
    | FinalTransition     // state => final
    | SelfTransition      // state => state (capability/HTTP method)
    | ExternalTransition  // state => otherState
    | InternalTransition  // within composite, no exit/re-entry

/// Expanded SmcatMeta DU:
type SmcatMeta =
    | SmcatColor of string
    | SmcatStateLabel of string
    | SmcatCustomAttribute of key: string * value: string  // renamed from SmcatActivity
    | SmcatStateType of kind: StateKind * origin: SmcatTypeOrigin
    | SmcatTransition of SmcatTransitionKind
```

**Rationale**: Leverages the existing `StateKind` shared DU rather than duplicating state type enumerations. `SmcatTypeOrigin` as a 2-case DU (not a bool) for self-documenting pattern matches. `SmcatTransition` case named differently from `SmcatTransitionKind` payload type for consistency with `SmcatStateType of StateKind * SmcatTypeOrigin` pattern.

**Alternatives considered**:
- `SmcatStateType of StateKind * explicit: bool` — rejected: bool is opaque, DU is self-documenting
- `SmcatTransitionKind of SmcatTransitionKind` — rejected: case name = type name creates reader ambiguity
- Separate `SmcatExplicitStateType` / `SmcatInferredStateType` cases — rejected: two cases for one concept, can't prevent both on same node

## R-002: Annotation Density Rule

**Decision**: Omit `SmcatStateType` for `Regular, Inferred` states. Present for all other combinations.

**Rule**: Annotation present when it carries information beyond what `Kind = Regular` already conveys:
- `Initial, Inferred` — present (serializer must NOT emit `[type="initial"]`)
- `Initial, Explicit` — present (serializer MUST emit `[type="initial"]`)
- `Regular, Explicit` — present (user wrote `[type="regular"]`, preserve it)
- `Regular, Inferred` — absent (universal default, zero information added)
- All other kinds — present regardless of origin

**Consumer pattern**: `annotations |> List.tryPick (function SmcatAnnotation(SmcatStateType(k, o)) -> Some(k, o) | _ -> None) |> Option.defaultValue (Regular, Inferred)`

**Rationale**: Annotation lists should signal exceptions to the default, not confirm it. Reduces noise for large documents with many regular states.

**Alternatives considered**:
- Always annotate every state — rejected: adds zero information for Regular/Inferred, pollutes annotation queries

## R-003: Initial/Final Pseudo-State Identity

**Decision**: Retain `Identifier = Some "initial"` / `Some "final"` as smcat pseudo-state names. Fix `Kind` and add annotations.

**Rationale**: `TransitionEdge.Source: string` requires identifiers for referential integrity. The names `"initial"` and `"final"` are smcat format conventions, not the semantic markers. The problem was `Kind = Regular` hiding the state's nature, not the identifier strings.

**Alternatives considered**:
- `Identifier = None` with serializer synthesis — rejected: breaks `TransitionEdge.Source: string` referential integrity
- Use real state name (e.g., `"Idle"`) with Kind=Initial — rejected: conflates domain state with format pseudo-state

## R-004: Impact Analysis

**SmcatActivity consumers (rename to SmcatCustomAttribute):**

| File | Location | Change |
|------|----------|--------|
| `Ast/Types.fs:106` | Type definition | Rename case |
| `Smcat/Parser.fs:377` | Construction site | Rename constructor |
| `Smcat/Serializer.fs:46` | Pattern match | Rename pattern |
| `TypeConstructionTests.fs:318` | Test assertion | Update annotation |
| `Smcat/ParserTests.fs:230,333` | Test assertions | Update pattern matches |

**Cross-module impact**: None. No files outside `Smcat/` and `Ast/` reference `SmcatActivity`, `SmcatColor`, `SmcatStateLabel`, or any `SmcatMeta` case directly. The cross-format validator (spec 021) and pipeline (spec 025) work at the `StatechartDocument` level and are annotation-agnostic.

## R-005: Generator Transition Kind Inference

**Decision**: The generator infers `SmcatTransitionKind` from structural analysis of each transition it creates:

| Generator creates | SmcatTransitionKind |
|------------------|---------------------|
| `initial => firstState` | `InitialTransition` |
| `state => state: HTTP_METHOD` | `SelfTransition` |
| `state => final` | `FinalTransition` |
| `state => otherState` (if added) | `ExternalTransition` |

`InternalTransition` is not produced by the current generator (no composite state support in generator). It will be used by the parser for transitions within composite states.

## R-006: Parser SmcatTransitionKind Inference

**Decision**: The parser infers `SmcatTransitionKind` from structural analysis of parsed transitions:

| Parsed transition | SmcatTransitionKind |
|------------------|---------------------|
| Source = "initial" | `InitialTransition` |
| Source = Target (self-loop) | `SelfTransition` |
| Target = Some "final" | `FinalTransition` |
| Inside composite state block | `InternalTransition` |
| All other transitions | `ExternalTransition` |

The `inferStateType` function in `Types.fs` already handles naming conventions. The new logic adds annotation storage alongside the existing Kind inference.
