# Data Model: smcat Shared AST Migration

**Feature**: 022-smcat-shared-ast-migration
**Date**: 2026-03-16

## Overview

This migration does not introduce new data models. It replaces smcat-specific types with the shared AST types from spec 020. This document maps the before/after type landscape.

## Types Deleted (from `Smcat/Types.fs`)

| Type | Replacement | Notes |
|------|------------|-------|
| `SourcePosition` (struct) | `Ast.SourcePosition` | Identical struct; `{ Line: int; Column: int }` |
| `StateType` (DU) | `Ast.StateKind` | 1:1 mapping; `StateKind` adds `Parallel` (unused by smcat) |
| `StateActivity` (record) | `Ast.StateActivities` | Single option per kind -> list per kind |
| `SmcatState` (record) | `Ast.StateNode` | Fields map directly; attributes become `SmcatAnnotation` annotations |
| `SmcatTransition` (record) | `Ast.TransitionEdge` | Label components split into separate fields |
| `SmcatElement` (DU) | `Ast.StatechartElement` | `StateDeclaration` -> `StateDecl`, `TransitionElement` -> `TransitionElement`, `CommentElement` -> dropped |
| `SmcatDocument` (record) | `Ast.StatechartDocument` | Adds `Title`, `InitialStateId`, `DataEntries`, `Annotations` fields |
| `ParseResult` (record) | `Ast.ParseResult` | Same structure; `Document` is always present |
| `ParseFailure` (record) | `Ast.ParseFailure` | `Position` becomes `SourcePosition option` (was non-option) |
| `ParseWarning` (record) | `Ast.ParseWarning` | `Position` becomes `SourcePosition option` (was non-option) |

## Types Retained (in `Smcat/Types.fs`)

| Type | Purpose | Changes |
|------|---------|---------|
| `TokenKind` (DU) | Lexer token classification | None |
| `Token` (struct) | Lexer token with position | `Position` type changes from local `SourcePosition` to `Ast.SourcePosition` |
| `TransitionLabel` (record) | Parser-internal label parsing helper | None |
| `SmcatAttribute` (record) | Parser-internal key-value pairs | None |
| `inferStateType` (function) | Parser-internal state classification | Return type changes from `StateType` to `Ast.StateKind` |

## Shared AST Types Used (from `Ast/Types.fs`, unchanged)

| Type | Usage in smcat |
|------|---------------|
| `StatechartDocument` | Root AST node produced by parser |
| `StateNode` | Represents smcat states (flat and composite) |
| `TransitionEdge` | Represents smcat transitions |
| `StatechartElement` | Union wrapping `StateDecl`, `TransitionElement`, etc. |
| `StateActivities` | Entry/exit/do activities for a state |
| `StateKind` | State type classification |
| `SourcePosition` | 1-based line/column position |
| `ParseResult` | Parser output container |
| `ParseFailure` | Parser error record |
| `ParseWarning` | Parser warning record |
| `Annotation` | Union wrapping format-specific metadata |
| `SmcatAnnotation` | Case of `Annotation` carrying `SmcatMeta` |
| `SmcatMeta` | Union: `SmcatColor`, `SmcatStateLabel`, `SmcatActivity` |

## New Types Introduced

### `GeneratorError` (in `Smcat/Generator.fs`)

```fsharp
type GeneratorError =
    | UnrecognizedMachineType of typeName: string
```

Matches the `Wsd.Generator.GeneratorError` type exactly. Used as the `Error` case in `Result<StatechartDocument, GeneratorError>`.

## Field Mapping Details

### SmcatState -> StateNode

| SmcatState field | StateNode field | Conversion |
|-----------------|----------------|------------|
| `Name: string` | `Identifier: string` | Direct |
| `Label: string option` | `Label: string option` | Direct |
| `StateType: StateType` | `Kind: StateKind` | 1:1 DU mapping |
| `Activities: StateActivity option` | `Activities: StateActivities option` | Single option -> list per kind |
| `Attributes: SmcatAttribute list` | `Annotations: Annotation list` | color -> `SmcatColor`, label -> `SmcatStateLabel`, other -> `SmcatActivity(key, value)` |
| `Children: SmcatDocument option` | `Children: StateNode list` | `None` -> `[]`, `Some doc` -> extract state nodes |
| `Position: SourcePosition` | `Position: SourcePosition option` | Wrap in `Some` |

### SmcatTransition -> TransitionEdge

| SmcatTransition field | TransitionEdge field | Conversion |
|----------------------|---------------------|------------|
| `Source: string` | `Source: string` | Direct |
| `Target: string` | `Target: string option` | Wrap in `Some` |
| `Label: TransitionLabel option` -> `.Event` | `Event: string option` | Extract from label or `None` |
| `Label: TransitionLabel option` -> `.Guard` | `Guard: string option` | Extract from label or `None` |
| `Label: TransitionLabel option` -> `.Action` | `Action: string option` | Extract from label or `None` |
| (not present) | `Parameters: string list` | Always `[]` |
| `Attributes: SmcatAttribute list` | `Annotations: Annotation list` | Same mapping as state attributes |
| `Position: SourcePosition` | `Position: SourcePosition option` | Wrap in `Some` |

### StateActivity -> StateActivities

| StateActivity field | StateActivities field | Conversion |
|--------------------|----------------------|------------|
| `Entry: string option` | `Entry: string list` | `None` -> `[]`, `Some s` -> `[s]` |
| `Exit: string option` | `Exit: string list` | `None` -> `[]`, `Some s` -> `[s]` |
| `Do: string option` | `Do: string list` | `None` -> `[]`, `Some s` -> `[s]` |
