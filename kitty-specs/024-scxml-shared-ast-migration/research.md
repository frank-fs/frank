# Research: SCXML Shared AST Migration

**Feature**: 024-scxml-shared-ast-migration
**Date**: 2026-03-16

## Research Summary

Minimal research required. This migration follows the established WSD shared AST migration pattern (spec 020). All decisions are grounded in existing code patterns and the spec requirements.

## R1: WSD Migration Pattern (Precedent Analysis)

**Decision**: Follow the WSD migration pattern exactly.

**Evidence**: The WSD parser (`src/Frank.Statecharts/Wsd/Parser.fs`) directly constructs `StatechartDocument`, `StateNode`, `TransitionEdge` from shared AST types. It opens `Frank.Statecharts.Ast` and attaches `WsdAnnotation(...)` for format-specific data. The serializer (`src/Frank.Statecharts/Wsd/Serializer.fs`) reads `StatechartDocument` and extracts `WsdAnnotation(...)` entries. No mapper exists for WSD.

**Rationale**: Proven pattern within the same codebase. The WSD migration was spec 020 and has been shipping successfully.

**Alternatives considered**: None -- the spec explicitly calls for following this pattern.

## R2: ScxmlMeta Case Design

**Decision**: Extend `ScxmlMeta` in `Ast/Types.fs` with 6 new/modified cases.

**Evidence from Mapper.fs analysis**:

| SCXML-specific data | Currently in | ScxmlMeta case |
|---|---|---|
| Transition type (internal/external) | `ScxmlTransition.TransitionType` | `ScxmlTransitionType of internal: bool` |
| Multi-target transitions | `ScxmlTransition.Targets` (list) | `ScxmlMultiTarget of targets: string list` |
| Document datamodel attribute | `ScxmlDocument.DatamodelType` | `ScxmlDatamodelType of datamodel: string` |
| Document binding attribute | `ScxmlDocument.Binding` | `ScxmlBinding of binding: string` |
| State initial attribute | `ScxmlState.InitialId` | `ScxmlInitial of initialId: string` |
| Invoke id attribute | `ScxmlInvoke.Id` | Extended `ScxmlInvoke` (add `id` parameter) |
| History default transition | `ScxmlHistory.DefaultTransition` | Extended `ScxmlHistory` (add `defaultTarget`) |

**Rationale**: Each case maps 1:1 from a SCXML-specific attribute that has no shared AST equivalent. The existing `ScxmlInvoke` and `ScxmlHistory` cases are extended rather than adding separate small cases.

**Alternatives considered**: Using a generic `ScxmlAttribute of name: string * value: string` catch-all -- rejected because it loses type safety and makes the generator's pattern matching fragile.

## R3: Breaking Change Analysis

**Decision**: This is NOT a breaking change for library consumers.

**Evidence**: All SCXML modules are `module internal` (verified by grep). The `Frank.Statecharts.Scxml.Types`, `Frank.Statecharts.Scxml.Parser`, `Frank.Statecharts.Scxml.Generator`, and `Frank.Statecharts.Scxml.Mapper` are all internal. Only `Frank.Statecharts.Tests` has `InternalsVisibleTo` access.

**Rationale**: Internal module refactoring does not affect the public API surface.

## R4: History Default Transition Preservation

**Decision**: Store default transition target in `ScxmlHistory` annotation payload.

**Evidence**: Current `ScxmlHistory` type has `DefaultTransition: ScxmlTransition option`. The mapper does NOT currently preserve history default transitions in the shared AST (the `toStateNodeAndTransitions` function in `Mapper.fs` doesn't extract default transitions from history nodes). This is a fidelity improvement.

**Rationale**: History default transitions are semantically part of the history pseudo-state, not standalone transitions. Storing them as `TransitionElement` entries would require a synthetic source identifier and complicate the generator's reconstruction.

## R5: Cross-Format Validator Compatibility

**Decision**: No changes needed to the cross-format validator.

**Evidence**: `CrossFormatTests.fs` constructs `StatechartDocument` values directly and passes them as `FormatArtifact` records. The validator works with the shared AST types exclusively. The SCXML parser's output type change from `ScxmlParseResult` to `Ast.ParseResult` is transparent to the validator -- it already receives `StatechartDocument` values (currently via the mapper, after migration via direct parser output).

**Rationale**: The validator has no dependency on SCXML-specific types.
