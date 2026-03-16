# Research: smcat Shared AST Migration

**Feature**: 022-smcat-shared-ast-migration
**Date**: 2026-03-16
**Status**: Complete (no unknowns)

## Summary

No research was required for this feature. The migration follows the established WSD pattern (spec 020) with no technical unknowns. All decisions are derived from the existing reference implementation.

## Decisions

### D-001: Migration pattern

**Decision**: Follow the exact WSD two-file split pattern (Generator.fs: metadata -> AST, Serializer.fs: AST -> text).
**Rationale**: The WSD migration (spec 020) was completed successfully and serves as the reference implementation. Consistency across all format modules reduces cognitive load and enables cross-format tooling (validator, CLI commands).
**Alternatives considered**: Single-file approach (combine serializer into Generator.fs) -- rejected because the spec explicitly requires the two-file split for all formats.

### D-002: SmcatMeta annotation cases

**Decision**: Use existing `SmcatMeta` cases (`SmcatColor`, `SmcatStateLabel`, `SmcatActivity`) without adding new cases.
**Rationale**: The existing three cases cover all smcat-specific attributes (color, label, and arbitrary key-value pairs via `SmcatActivity`). The spec confirms "no new cases needed."
**Alternatives considered**: Adding new cases for each attribute type -- rejected because `SmcatActivity(key, value)` already handles arbitrary attributes generically.

### D-003: `inferStateType` return type

**Decision**: Update `inferStateType` to return `Ast.StateKind` instead of smcat-local `StateType`.
**Rationale**: `StateKind` has identical cases to `StateType` (Regular, Initial, Final, ShallowHistory, DeepHistory, Choice, ForkJoin, Terminate) plus `Parallel` (unused by smcat). The mapping is 1:1 and the logic is identical.
**Alternatives considered**: Keeping `StateType` and mapping at call sites -- rejected because it would retain a type that should be deleted.

### D-004: Token `SourcePosition` reference

**Decision**: Update `Token` struct to use `Ast.SourcePosition` instead of smcat-local `SourcePosition`.
**Rationale**: Both types are identical `[<Struct>] type SourcePosition = { Line: int; Column: int }`. Using the shared type eliminates the local duplicate. The lexer, label parser, and parser all use `Token.Position` which will seamlessly reference the Ast type.
**Alternatives considered**: Keeping a local `SourcePosition` type alias -- rejected because it adds unnecessary indirection.

### D-005: `LabelParser` warning type

**Decision**: Update `LabelParser.parseLabel` to return `Ast.ParseWarning list` instead of smcat-local `ParseWarning list`.
**Rationale**: The shared `Ast.ParseWarning` has `Position: SourcePosition option` (option) vs the local type's `Position: SourcePosition` (non-option). The label parser always has a position, so it wraps in `Some`. This matches the parser's existing conversion in `Mapper.toAstWarning`.
**Alternatives considered**: None -- this is the only viable approach given the type deletion.

### D-006: Composite state children representation

**Decision**: Parser constructs `StateNode.Children` as `StateNode list` directly (not `SmcatDocument option`).
**Rationale**: The shared AST uses `StateNode.Children: StateNode list` (empty list means no children). The smcat format uses `SmcatState.Children: SmcatDocument option`. The conversion is: `None` -> `[]`, `Some doc` -> extract `StateDecl` nodes from `doc.Elements`. This is already implemented in `Mapper.toChildStateNodes`.
**Alternatives considered**: None -- the shared AST design is fixed.
