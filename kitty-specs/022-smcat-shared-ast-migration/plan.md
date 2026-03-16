# Implementation Plan: smcat Shared AST Migration

**Branch**: `022-smcat-shared-ast-migration` | **Date**: 2026-03-16 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/kitty-specs/022-smcat-shared-ast-migration/spec.md`
**Issue**: #113 | **Parent**: #57

## Summary

Migrate the smcat parser, generator, and test suite to the shared `StatechartDocument` AST (spec 020), following the exact pattern established by the WSD migration. The parser will produce `Ast.ParseResult` directly (eliminating the `Mapper.fs` bridge), a new `Serializer.fs` will convert `StatechartDocument` to smcat text, and `Generator.fs` will be refactored to produce `Result<StatechartDocument, GeneratorError>` from `StateMachineMetadata`. Format-specific `Types.fs` will be reduced to lexer-only types.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank.Statecharts)
**Primary Dependencies**: Frank.Statecharts (project-internal -- shared AST types from spec 020 in `Frank.Statecharts.Ast` namespace)
**Storage**: N/A (stateless parser/serializer/generator library)
**Testing**: Expecto (existing test suite in `test/Frank.Statecharts.Tests/Smcat/`)
**Target Platform**: .NET library (multi-target: net8.0, net9.0, net10.0)
**Project Type**: Single project (library within `src/Frank.Statecharts/`)
**Performance Goals**: N/A (not a hot path; no regression from current implementation)
**Constraints**: Must preserve all existing parser behavior, especially error recovery and best-effort document production
**Scale/Scope**: ~6 source files modified/created, ~6 test files updated, ~1 file deleted

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | N/A | Statechart tooling, not HTTP resource modeling |
| II. Idiomatic F# | PASS | DU pattern matching, pipeline-friendly `serialize` and `generate` signatures, Option types throughout |
| III. Library, Not Framework | PASS | Pure library functions, no framework coupling |
| IV. ASP.NET Core Native | N/A | No ASP.NET Core involvement |
| V. Performance Parity | PASS | No new allocations in hot paths; same algorithmic complexity |
| VI. Resource Disposal Discipline | PASS | No `IDisposable` types created or consumed |
| VII. No Silent Exception Swallowing | PASS | Parser error reporting is explicit via `ParseFailure` records; no exception handling involved |
| VIII. No Duplicated Logic Across Modules | PASS | Eliminating `Mapper.fs` removes duplicated mapping logic; `Serializer.fs` is new (not duplicated from Generator) |

**Post-design re-check**: The serializer's `needsQuoting`/`quoteName` helpers are format-specific and distinct from `Wsd.Serializer.needsQuoting` (smcat allows dots, WSD does not). No duplication concern.

## Project Structure

### Documentation (this feature)

```
kitty-specs/022-smcat-shared-ast-migration/
├── plan.md              # This file
├── research.md          # Phase 0 output (minimal -- no unknowns)
├── data-model.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── api-signatures.md
└── tasks.md             # Phase 2 output (created by /spec-kitty.tasks)
```

### Source Code (repository root)

```
src/Frank.Statecharts/
├── Ast/
│   └── Types.fs              # Shared AST (unchanged -- SmcatMeta already has needed cases)
├── Smcat/
│   ├── Types.fs              # MODIFIED: reduced to lexer types only (TokenKind, Token, TransitionLabel, SmcatAttribute, inferStateType)
│   ├── Lexer.fs              # UNCHANGED: uses smcat-local Token/TokenKind
│   ├── LabelParser.fs        # MODIFIED: update ParseWarning to use Ast.ParseWarning, SourcePosition to Ast.SourcePosition
│   ├── Parser.fs             # MODIFIED: produce Ast.ParseResult with StatechartDocument directly
│   ├── Serializer.fs         # NEW: StatechartDocument -> smcat text
│   ├── Generator.fs          # MODIFIED: produce Result<StatechartDocument, GeneratorError> from metadata
│   └── Mapper.fs             # DELETED
└── Frank.Statecharts.fsproj  # MODIFIED: remove Mapper.fs, add Serializer.fs

test/Frank.Statecharts.Tests/Smcat/
├── LexerTests.fs             # UNCHANGED
├── LabelParserTests.fs       # MINOR: update type references if needed
├── ParserTests.fs            # MODIFIED: assert against Ast types
├── GeneratorTests.fs         # MODIFIED: assert against StatechartDocument
├── RoundTripTests.fs         # MODIFIED: use Serializer.serialize, assert against StatechartDocument
└── ErrorTests.fs             # MODIFIED: update type references to Ast types
```

**Structure Decision**: Existing `src/Frank.Statecharts/Smcat/` directory. New `Serializer.fs` is added; `Mapper.fs` is deleted. No new projects or directories.

## Complexity Tracking

No constitution violations. No complexity justifications needed.

---

## Design Decisions

### DD-001: Parser produces `Ast.ParseResult` directly

The parser's `parseSmcat` function will return `Ast.ParseResult` (containing `StatechartDocument`) directly, eliminating the need for `Mapper.fs`. The mapping logic currently in `Mapper.toStatechartDocument` will be inlined into the parser's element construction functions.

**Key changes to `Parser.fs`**:
- `ParserState.Elements` becomes `Ast.StatechartElement list` (was `SmcatElement list`)
- `ParserState.Errors` becomes `Ast.ParseFailure list` (was local `ParseFailure list`)
- `ParserState.Warnings` becomes `Ast.ParseWarning list` (was local `ParseWarning list`)
- State declarations construct `Ast.StateNode` directly with `SmcatAnnotation` for color/label attributes
- Transitions construct `Ast.TransitionEdge` directly with label components split into `Event`/`Guard`/`Action` fields
- `parseDocument` returns `StatechartDocument` (was `SmcatDocument`)
- `inferStateType` returns `Ast.StateKind` (was smcat-local `StateType`)

### DD-002: New `Serializer.fs` follows WSD pattern

A new `Serializer.fs` with signature `serialize: StatechartDocument -> string` produces valid smcat text. It reads `SmcatAnnotation` entries from `StateNode.Annotations` to reconstruct format-specific attributes (color, label). It handles composite states recursively via `StateNode.Children`.

**Key behaviors**:
- State declarations with type markers (from `StateNode.Kind`)
- Activity declarations (from `StateNode.Activities`)
- Attribute syntax `[color="red" label="Idle"]` (from `SmcatAnnotation` entries)
- Transition arrows `source => target: event [guard] / action;` (from `TransitionEdge` fields)
- Composite state blocks with `{ }` delimiters (from `StateNode.Children`)
- Sensible defaults when `SmcatAnnotation` entries are absent

### DD-003: Generator refactored to metadata-to-AST

`Generator.fs` is refactored to produce `Result<StatechartDocument, GeneratorError>` from `StateMachineMetadata`, following the exact `Wsd.Generator` pattern:
- Validate boxed `Machine` type
- Extract states from `StateHandlerMap`
- Order states (initial first, others alphabetically)
- Create `StateDecl` and `TransitionElement` entries
- Return `StatechartDocument`

The existing `formatLabel`, `formatTransition`, `generateTo` functions are deleted (their logic moves to `Serializer.fs`). The `GenerateOptions` type is retained.

### DD-004: `Types.fs` reduced to lexer types

After migration, `Types.fs` retains only:
- `TokenKind` (lexer token classification)
- `Token` (lexer token, updated to use `Ast.SourcePosition`)
- `TransitionLabel` (parser-internal label parsing helper)
- `SmcatAttribute` (parser-internal key-value pairs from smcat syntax)
- `inferStateType` (parser-internal state classification, updated to return `Ast.StateKind`)

Deleted types: `SmcatDocument`, `SmcatState`, `SmcatTransition`, `SmcatElement`, `StateType`, `StateActivity`, `ParseResult`, `ParseFailure`, `ParseWarning`, `SourcePosition`.

### DD-005: LabelParser update

`LabelParser.fs` currently references smcat-local `Types.SourcePosition` and `Types.ParseWarning`. After migration:
- `parseLabel` signature changes to accept `Ast.SourcePosition` and return `TransitionLabel * Ast.ParseWarning list`
- The `TransitionLabel` type itself remains in `Types.fs` (it is parser-internal)

### DD-006: Test migration strategy

Tests are updated mechanically:
- Replace `SmcatDocument`/`SmcatState`/`SmcatTransition` assertions with `StatechartDocument`/`StateNode`/`TransitionEdge` assertions
- Replace `StateDeclaration s` pattern matches with `Ast.StateDecl s`
- Replace `Types.TransitionElement t` pattern matches with `Ast.TransitionElement t`
- Replace `s.Name` with `s.Identifier`, `s.StateType` with `s.Kind`
- Replace `t.Label.Value.Event` with `t.Event` (label components are now directly on `TransitionEdge`)
- Round-trip tests use `Serializer.serialize` instead of the test-only `generateFromDocument` helper
- Generator tests assert against `StatechartDocument` instead of raw text output

### DD-007: fsproj file ordering

The `Frank.Statecharts.fsproj` file ordering becomes:
```xml
<Compile Include="Smcat/Types.fs" />
<Compile Include="Smcat/Lexer.fs" />
<Compile Include="Smcat/LabelParser.fs" />
<Compile Include="Smcat/Parser.fs" />
<Compile Include="Smcat/Serializer.fs" />   <!-- NEW: replaces Mapper.fs position -->
...
<Compile Include="Smcat/Generator.fs" />     <!-- unchanged position -->
```

`Mapper.fs` line is removed. `Serializer.fs` is inserted after `Parser.fs` (it depends on `Ast` types but not on `Parser`; placing it here mirrors the WSD file ordering pattern).

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Parser behavior change | Low | Medium | All existing test scenarios are preserved; type changes are mechanical |
| `inferStateType` return type mismatch | Low | Low | `StateKind` has identical cases to `StateType` (1:1 mapping) |
| Test compilation errors from type changes | High (expected) | Low | Mechanical find-and-replace; caught by compiler |
| Round-trip fidelity regression | Low | Medium | Serializer output validated by parse-serialize-reparse tests |
| Cross-format validator breakage | Low | Low | Validator uses `StatechartDocument` (the target type) |
