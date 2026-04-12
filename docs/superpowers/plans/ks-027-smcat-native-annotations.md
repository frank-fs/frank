---
source: kitty-specs/027-smcat-native-annotations
type: plan
---

# Implementation Plan: smcat Native Annotations and Generator Fidelity

**Branch**: `027-smcat-native-annotations` | **Date**: 2026-03-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/kitty-specs/027-smcat-native-annotations/spec.md`
**Closes**: #113

## Summary

Replace the WSD-ported smcat generator, serializer, and parser with smcat-native implementations that leverage the `SmcatMeta` discriminated union for full-fidelity round-trip support. Expand `SmcatMeta` with `SmcatStateType`, `SmcatTransition`, `SmcatTypeOrigin`, and `SmcatTransitionKind` types. Rename `SmcatActivity` to `SmcatCustomAttribute`. The generator produces typed AST nodes with annotations instead of stringly-typed conventions. The serializer consumes annotations to decide when to emit explicit `[type="..."]` attributes. The parser stores type origin (Explicit vs Inferred) for round-trip fidelity.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0, 9.0, and 10.0 (multi-targeting)
**Primary Dependencies**: Frank.Statecharts (project-internal), ASP.NET Core (framework reference)
**Storage**: N/A (stateless type system and parser/generator library)
**Testing**: Expecto (test framework), dotnet test, existing test infrastructure in `test/Frank.Statecharts.Tests/`
**Target Platform**: .NET multi-target library (net8.0;net9.0;net10.0)
**Project Type**: Single library project with test project
**Performance Goals**: N/A — startup/CLI-time code, not request hot path
**Constraints**: All modules are `internal`; breaking changes are acceptable (unreleased)
**Scale/Scope**: 5 source files modified, 3 test files modified, 0 new files

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Resource-Oriented Design**: N/A — Internal type system changes, no public API impact
- **II. Idiomatic F#**: PASS — Discriminated unions for state types (`SmcatTypeOrigin`, `SmcatTransitionKind`), exhaustive pattern matching, pipeline-friendly. This change makes the code *more* idiomatic by replacing stringly-typed conventions with typed DUs.
- **III. Library, Not Framework**: PASS — Internal changes only, no user-facing API changes
- **IV. ASP.NET Core Native**: N/A — No ASP.NET Core integration changes
- **V. Performance Parity**: PASS — All changes are in startup/CLI-time code paths. DU tag checks are zero-cost compared to the string comparisons they replace. Confirmed by performance analysis during discovery.
- **VI. Resource Disposal Discipline**: N/A — No IDisposable types involved
- **VII. No Silent Exception Swallowing**: N/A — No exception handling changes
- **VIII. No Duplicated Logic Across Modules**: PASS — Reuses shared `StateKind` DU from `Ast/Types.fs` instead of duplicating state type enumerations. `SmcatTypeOrigin` and `SmcatTransitionKind` are defined once in `Ast/Types.fs` and consumed by parser, generator, and serializer.

**Result**: All gates pass. No violations to justify.

## Project Structure

### Documentation (this feature)

```
kitty-specs/027-smcat-native-annotations/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks/               # Phase 2 output (created by /spec-kitty.tasks)
```

### Source Code (files modified)

```
src/Frank.Statecharts/
├── Ast/
│   └── Types.fs                 # SmcatMeta expansion, SmcatTypeOrigin, SmcatTransitionKind
└── Smcat/
    ├── Generator.fs             # Typed StateKind + SmcatAnnotation on all nodes
    ├── Serializer.fs            # Consume annotations for type attribute emission
    ├── Parser.fs                # Store SmcatStateType with Explicit/Inferred origin
    └── Types.fs                 # No changes (lexer types only)

test/Frank.Statecharts.Tests/
├── Ast/
│   └── TypeConstructionTests.fs # Update SmcatAnnotation usage
└── Smcat/
    ├── GeneratorTests.fs        # Verify annotations on generated nodes
    ├── ParserTests.fs           # Verify SmcatStateType annotations
    └── RoundTripTests.fs        # Add structural comparison + new golden files
```

**Structure Decision**: No new files or projects. All changes are modifications to existing files within `Frank.Statecharts` and `Frank.Statecharts.Tests`.

## Dependency Graph

```
WP01: Ast/Types.fs (DU definitions)
  ↓
  ├── WP02: Generator.fs + GeneratorTests.fs
  ├── WP03: Serializer.fs
  └── WP04: Parser.fs + ParserTests.fs + TypeConstructionTests.fs
       ↓
       WP05: RoundTripTests.fs (depends on all above)
```

WP02, WP03, and WP04 can be executed in parallel after WP01. WP05 depends on all preceding WPs.

## Key Design Decisions

### D-001: SmcatMeta DU Case Naming

The `SmcatMeta` case wrapping `SmcatTransitionKind` is named `SmcatTransition` (not `SmcatTransitionKind`) to follow the established pattern where case name ≠ payload type name. Consistent with `SmcatStateType of StateKind * SmcatTypeOrigin`.

Match site reads: `SmcatAnnotation(SmcatTransition InitialTransition)` — prose-like, unambiguous.

### D-002: Regular/Inferred Annotation Omission

`SmcatStateType` annotation is omitted for `Regular, Inferred` states (the universal default). Present for all other kind/origin combinations. Consumers use `Option.defaultValue (Regular, Inferred)` fallback. This keeps annotation lists clean — presence signals something noteworthy.

### D-003: Initial/Final Pseudo-State Identifiers

Generator retains `Identifier = Some "initial"` / `Some "final"` — these are correct smcat pseudo-state names. The fix is `Kind = Initial`/`Final` with `SmcatAnnotation(SmcatStateType(..., Explicit))`, not renaming the identifiers. `TransitionEdge.Source: string` requires identifiers for referential integrity.

### D-004: Atomic Breaking Changes

All DU changes (new types + `SmcatActivity` → `SmcatCustomAttribute` rename) are in a single atomic work package. Breaking changes are acceptable because the code is unreleased and all smcat modules are `internal`.

### D-005: Test Strategy

Extend existing `RoundTripTests.fs` with annotation-aware `assertStructuralEquivalence` alongside existing `assertSemanticEquivalence`. Add new golden files exercising explicit/inferred types, colors, and custom attributes. No new test files.

## Files Impacted

| File | Change Type | FRs Addressed |
|------|-------------|---------------|
| `src/Frank.Statecharts/Ast/Types.fs` | Modify | FR-001, FR-002, FR-003, FR-004 |
| `src/Frank.Statecharts/Smcat/Generator.fs` | Modify | FR-005, FR-006, FR-007, FR-008 |
| `src/Frank.Statecharts/Smcat/Serializer.fs` | Modify | FR-009, FR-010, FR-011 |
| `src/Frank.Statecharts/Smcat/Parser.fs` | Modify | FR-012, FR-013, FR-014 |
| `test/Frank.Statecharts.Tests/Ast/TypeConstructionTests.fs` | Modify | FR-016, FR-017 |
| `test/Frank.Statecharts.Tests/Smcat/GeneratorTests.fs` | Modify | FR-005, FR-007, FR-008, FR-017 |
| `test/Frank.Statecharts.Tests/Smcat/ParserTests.fs` | Modify | FR-012, FR-013, FR-017 |
| `test/Frank.Statecharts.Tests/Smcat/RoundTripTests.fs` | Modify | FR-015, FR-017 |

## Complexity Tracking

No constitution violations to justify. All changes are within existing project boundaries, use existing patterns, and add no new dependencies.
