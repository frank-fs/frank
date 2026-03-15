# Implementation Plan: Cross-Format Statechart Validator

**Branch**: `021-cross-format-validator` | **Date**: 2026-03-15 | **Spec**: [kitty-specs/021-cross-format-validator/spec.md](spec.md)
**Input**: Feature specification from `/kitty-specs/021-cross-format-validator/spec.md`

## Summary

This feature provides a pluggable validation orchestrator for statechart artifacts within the Frank.Statecharts library. The validator defines a `ValidationRule` function contract and a set of data types (`FormatTag`, `FormatArtifact`, `ValidationCheck`, `ValidationFailure`, `ValidationReport`) that enable format modules to register self-consistency and cross-format invariant checks. The validator orchestrates registered rules against available artifacts, collects all failures without aborting early, catches exceptions from rules, and returns a structured `ValidationReport`. Rule registration follows a purely functional approach: each format module exposes a `rules: ValidationRule list` value, and the consumer (frank-cli) passes rules + artifacts to `Validator.validate`.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank.Statecharts)
**Primary Dependencies**: Frank.Statecharts (project-internal -- shared AST types from spec 020 in `Frank.Statecharts.Ast` namespace)
**Storage**: N/A (stateless validation -- pure functions, no persistence)
**Testing**: Expecto 10.2.3, YoloDev.Expecto.TestSdk 0.14.3, targeting net10.0 (matching existing Frank.Statecharts.Tests)
**Target Platform**: .NET 8.0/9.0/10.0 (multi-target library)
**Project Type**: Library module within existing Frank.Statecharts project
**Performance Goals**: Validation of 20 states, 50 transitions, 5 format artifacts completes in < 1 second (SC-003)
**Constraints**: No mutable state. No CLI/presentation concerns. Pure data-in/data-out. No external NuGet dependencies.
**Scale/Scope**: 5 format types (WSD, ALPS, SCXML, smcat, XState), up to 10 pairwise cross-format checks

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | PASS | Validator supports the statechart spec pipeline which feeds resource state machines. Does not introduce route-centric thinking. |
| II. Idiomatic F# | PASS | All types are discriminated unions and records. `ValidationRule` is a function type alias. No classes, no interfaces. Pipeline-friendly `validate` function signature. |
| III. Library, Not Framework | PASS | Validator is a pure library component. Consumer (frank-cli) assembles rules and calls `validate`. No global state, no registration ceremony, no framework inversion. |
| IV. ASP.NET Core Native | N/A | Validator has no HTTP or ASP.NET Core dependency. It operates on parsed AST types. |
| V. Performance Parity | PASS | Simple list iteration over rules and artifacts. No allocation-heavy patterns. Performance goal (< 1s for 20 states, 50 transitions, 5 formats) is achievable with straightforward implementation. |
| VI. Resource Disposal Discipline | N/A | No IDisposable values in scope. All types are immutable records and DUs. |
| VII. No Silent Exception Swallowing | PASS | FR-013 requires catching rule exceptions but reporting them as failures with rule name and error details -- not silently swallowing. The exception is caught, wrapped in a `ValidationFailure`, and included in the report. |
| VIII. No Duplicated Logic | PASS | Shared helper functions for AST traversal (extracting states, transitions, events from `StatechartDocument`) will be defined once in the Validation module. Format modules reference these helpers. |

## Project Structure

### Documentation (this feature)

```
kitty-specs/021-cross-format-validator/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── contracts/           # Phase 1 output (F# module signatures)
└── tasks.md             # Phase 2 output (created by /spec-kitty.tasks)
```

### Source Code (repository root)

```
src/Frank.Statecharts/
├── Ast/
│   └── Types.fs              # Shared AST types (spec 020 -- already exists or will be created by spec 020)
├── Validation/
│   ├── Types.fs              # FormatTag, FormatArtifact, ValidationCheck, ValidationFailure, ValidationReport, ValidationRule
│   └── Validator.fs           # validate function, rule orchestration, exception handling
├── Wsd/
│   ├── Types.fs              # WSD-specific lexer types (existing)
│   ├── Lexer.fs              # (existing)
│   ├── GuardParser.fs        # (existing)
│   └── Parser.fs             # (existing)
├── Types.fs                  # Runtime state machine types (existing)
├── ...                       # Other existing files
└── Frank.Statecharts.fsproj  # Updated compile order to include Validation/ files

test/Frank.Statecharts.Tests/
├── Validation/
│   ├── TypeTests.fs           # Tests for validation data types
│   ├── ValidatorTests.fs      # Tests for orchestrator (rule execution, skipping, exception handling, aggregation)
│   ├── SelfConsistencyTests.fs # Tests for single-format structural checks
│   └── CrossFormatTests.fs    # Tests for cross-format invariant checks
├── Wsd/                      # (existing test files)
├── ...                       # Other existing test files
└── Frank.Statecharts.Tests.fsproj  # Updated compile order
```

**Structure Decision**: New validation types and orchestrator are added as a `Validation/` subdirectory within the existing `Frank.Statecharts` project, following the established pattern of `Wsd/` subdirectory. This keeps the validator co-located with the AST types it validates and avoids creating a separate project (which would violate the "Library, Not Framework" principle by adding unnecessary assembly boundaries). Tests follow the same subdirectory pattern under `test/Frank.Statecharts.Tests/Validation/`.

## Complexity Tracking

No constitution violations. No complexity justification needed.
