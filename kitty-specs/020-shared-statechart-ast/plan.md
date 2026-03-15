# Implementation Plan: Shared Statechart AST

**Branch**: `020-shared-statechart-ast` | **Date**: 2026-03-15 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/kitty-specs/020-shared-statechart-ast/spec.md`

## Summary

Define a unified, format-agnostic AST for all statechart format parsers (WSD, ALPS, SCXML, smcat, XState JSON) and migrate the existing WSD parser to produce it. The shared AST (`StatechartDocument`) lives in `src/Frank.Statecharts/Ast/Types.fs` and represents the superset of statechart concepts across all five formats: states (9-case `StateKind`), transitions, guards, data model entries, hierarchical nesting, grouping blocks, and format-specific annotations (5-case `Annotation` DU). The WSD parser's semantic output types (`Diagram`, `Participant`, `Message`, etc.) are replaced by shared AST types; lexer/token infrastructure remains WSD-specific. All shared AST types are `public` (consumed by frank-cli, cross-format validator, and external tooling).

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank.Statecharts)
**Primary Dependencies**: None beyond .NET BCL (no new NuGet packages)
**Storage**: N/A (compile-time type definitions, no persistence)
**Testing**: Expecto (existing test framework for Frank.Statecharts.Tests)
**Target Platform**: Multi-target library (net8.0;net9.0;net10.0)
**Project Type**: Single project (types added to existing `Frank.Statecharts` project)
**Performance Goals**: Structural equality on all AST types; no mutable fields; zero-allocation construction where possible (`[<Struct>]` on `SourcePosition`)
**Constraints**: Must not break the existing `Frank.Statecharts` public API surface (runtime types in `StatefulResourceBuilder.fs` and `Types.fs` are untouched). No dependency from shared AST on runtime types (`StateMachine<'S,'E,'C>`, `StateMachineMetadata`).
**Scale/Scope**: ~300 LOC new types, ~400 LOC parser migration, ~600 LOC test migration

## Planning Decisions

| # | Question | Decision |
|---|----------|----------|
| PQ-1 | Shared AST type visibility | `public` -- consumed by frank-cli (separate project), cross-format validator, and potential external tooling. Avoids binary-breaking visibility change later. |

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | PASS | AST types are internal representations for the spec pipeline. They do not affect the resource CE API or push users toward route-centric thinking. |
| II. Idiomatic F# | PASS | Discriminated unions for `StateKind`, `GroupKind`, `Annotation`, `StatechartElement`. Option types instead of nulls. Immutable records with structural equality. Empty lists for unpopulated collections. |
| III. Library, Not Framework | PASS | No new opinions imposed. AST types are a data structure library consumed by parsers/generators. No framework coupling. |
| IV. ASP.NET Core Native | PASS | No ASP.NET Core integration surface. AST types are in a separate namespace (`Frank.Statecharts.Ast`) with no dependency on `HttpContext` or middleware. |
| V. Performance Parity | PASS | `SourcePosition` is `[<Struct>]`. All record types use structural equality (no reference equality). No hot path impact -- AST construction is parse-time only. |
| VI. Resource Disposal Discipline | PASS | No `IDisposable` types introduced. AST types are pure immutable data. |
| VII. No Silent Exception Swallowing | PASS | Parser error handling preserves existing patterns (structured `ParseFailure` with position, description, expected, found, corrective example). No catch-all handlers. |
| VIII. No Duplicated Logic Across Modules | PASS | `SourcePosition` consolidated from WSD-specific to shared namespace (eliminates duplication). `GroupKind`, `GroupBranch`, `ParseFailure`, `ParseWarning` consolidated similarly. Helper functions for AST navigation defined once in shared module. |

**Post-design re-check**: All principles satisfied. The migration eliminates type duplication (Principle VIII) by moving shared concepts to `Frank.Statecharts.Ast`.

## Project Structure

### Documentation (this feature)

```
kitty-specs/020-shared-statechart-ast/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Research findings (13 decisions, complete)
├── data-model.md        # Data model (complete)
├── checklists/
│   └── requirements.md  # Requirements checklist
├── research/
│   ├── evidence-log.csv
│   └── source-register.csv
└── tasks/
    └── README.md
```

### Source Code (repository root)

```
src/Frank.Statecharts/
├── Ast/
│   └── Types.fs              # NEW: All shared AST types (public)
├── Wsd/
│   ├── Types.fs              # MODIFIED: Lexer-only types (TokenKind, Token); semantic types removed
│   ├── Lexer.fs              # MODIFIED: Import SourcePosition from Ast.Types
│   ├── GuardParser.fs        # MODIFIED: Import types from Ast.Types, produce shared GuardAnnotation
│   └── Parser.fs             # MODIFIED: Produce StatechartDocument instead of Diagram
├── Types.fs                  # UNCHANGED: Runtime state machine types
├── Store.fs                  # UNCHANGED
├── StatefulResourceBuilder.fs # UNCHANGED
├── Middleware.fs              # UNCHANGED
├── StatechartETagProvider.fs  # UNCHANGED
├── ResourceBuilderExtensions.fs # UNCHANGED
├── WebHostBuilderExtensions.fs  # UNCHANGED
└── Frank.Statecharts.fsproj  # MODIFIED: Add Ast/Types.fs to compile order (before Wsd/Types.fs)

test/Frank.Statecharts.Tests/
├── Wsd/
│   ├── LexerTests.fs         # UNCHANGED (lexer types stay WSD-specific)
│   ├── GuardParserTests.fs   # MODIFIED: Import from Ast.Types
│   ├── ParserTests.fs        # MODIFIED: Assert against shared AST types
│   ├── GroupingTests.fs      # MODIFIED: Assert against shared AST types
│   ├── ErrorTests.fs         # MODIFIED: Assert against shared AST types
│   └── RoundTripTests.fs     # MODIFIED: Assert against shared AST types
├── Ast/
│   ├── TypeConstructionTests.fs  # NEW: Shared AST type construction + structural equality
│   └── PartialPopulationTests.fs # NEW: Format-specific partial population scenarios
└── Program.fs                # UNCHANGED
```

**Structure Decision**: New `Ast/` sub-directory under `src/Frank.Statecharts/` following the per-format pattern (like `Wsd/`). Single `Types.fs` file for all shared AST types -- the type count (~20 types) fits comfortably in one file and avoids circular dependency concerns with mutually recursive types (`StatechartElement` <-> `GroupBranch` <-> `GroupBlock` <-> `StateNode`).

## Type Migration Map

This table maps each existing WSD type to its shared AST replacement, documenting what happens during migration.

| WSD Type (current) | Shared AST Type (target) | Migration Action |
|---------------------|--------------------------|------------------|
| `SourcePosition` (struct) | `SourcePosition` (struct, same shape) | Move to `Ast.Types`; WSD `Token` imports from there |
| `Participant` (record) | `StateNode` (record) | Replace; `Name` -> `Identifier`, `Alias` -> `Label`, `Explicit` -> WSD-internal tracking |
| `Message` (record) | `TransitionEdge` (record) | Replace; `Sender`/`Receiver` -> `Source`/`Target`, `ArrowStyle`/`Direction` -> `WsdAnnotation` |
| `GuardAnnotation` (record) | `GuardAnnotation` stays in WSD as parse helper; guard data flows to `TransitionEdge.Guard` or `NoteContent` annotations | Guard pairs become annotations or direct field values |
| `NotePosition` (DU) | `WsdNotePosition` (DU, under `WsdMeta`) | Move to WSD annotation payload |
| `Note` (record) | `NoteContent` (record) | Replace; `NotePosition` -> WSD annotation, `Guard` -> separate annotation |
| `GroupKind` (DU, 7 cases) | `GroupKind` (DU, same 7 cases) | Move to `Ast.Types` (shared concept) |
| `GroupBranch` (record) | `GroupBranch` (record, same shape) | Move to `Ast.Types` |
| `Group` (record) | `GroupBlock` (record) | Rename; same structure |
| `DiagramElement` (DU, 6 cases) | `StatechartElement` (DU, 5 cases) | Replace; `ParticipantDecl` -> `StateDecl`, `MessageElement` -> `TransitionElement` |
| `Diagram` (record) | `StatechartDocument` (record) | Replace; `AutoNumber` -> `DirectiveElement`, `Participants` -> derived from elements |
| `ArrowStyle` (DU) | `ArrowStyle` (DU, under WSD annotation types) | Move to WSD-specific annotation data |
| `Direction` (DU) | `Direction` (DU, under WSD annotation types) | Move to WSD-specific annotation data |
| `ParseFailure` (record) | `ParseFailure` (record, `Position` becomes `option`) | Move to `Ast.Types`; generalize position to `option` |
| `ParseWarning` (record) | `ParseWarning` (record, `Position` becomes `option`) | Move to `Ast.Types`; generalize position to `option` |
| `ParseResult` (record) | `ParseResult` (record, `Diagram` -> `Document`) | Move to `Ast.Types` |
| `TokenKind` (DU) | STAYS in `Wsd.Types` | No change (lexer-specific) |
| `Token` (struct) | STAYS in `Wsd.Types` (imports `SourcePosition` from `Ast.Types`) | Minor: import `SourcePosition` from new location |

## Compile Order

The `Frank.Statecharts.fsproj` `<Compile>` items must be ordered:

```xml
<Compile Include="Ast/Types.fs" />       <!-- NEW: shared AST types first -->
<Compile Include="Wsd/Types.fs" />       <!-- lexer types, imports SourcePosition from Ast -->
<Compile Include="Wsd/Lexer.fs" />
<Compile Include="Wsd/GuardParser.fs" />
<Compile Include="Wsd/Parser.fs" />
<Compile Include="Types.fs" />           <!-- runtime state machine types -->
<!-- ... remaining files unchanged ... -->
```

## Key Design Decisions

All decisions from `research.md` (D-001 through D-013) are adopted. Additional planning decisions:

| ID | Decision | Rationale |
|----|----------|-----------|
| PD-001 | Shared AST types are `public` | Consumed by frank-cli, cross-format validator, external tooling |
| PD-002 | WSD `Participant.Explicit` tracking stays in parser state, not in `StateNode` | `Explicit` is a WSD parse concept (implicit participant warnings), not a statechart concept |
| PD-003 | Parser's internal `ParserState.Participants` map is preserved | The WSD parser needs to track participants for implicit warnings; this is parser state, not AST state |
| PD-004 | `GuardAnnotation` stays as WSD parse helper type | Guard extraction from notes is a WSD-specific parsing concern; the extracted guard data populates `TransitionEdge.Guard` or becomes an annotation |
| PD-005 | `ParseFailure.Position` and `ParseWarning.Position` become `option` | Generalizes for formats where XML/JSON libraries may not provide position info |
| PD-006 | Mutually recursive types (`StatechartElement`, `GroupBranch`, `GroupBlock`, `StateNode`) use F# `and` keyword | Required by F# compiler for types that reference each other; matches existing WSD pattern |

## Complexity Tracking

No constitution violations. No complexity justifications needed.
