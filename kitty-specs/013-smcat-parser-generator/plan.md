# Implementation Plan: smcat Parser and Generator
*Path: kitty-specs/013-smcat-parser-generator/plan.md*

**Branch**: `013-smcat-parser-generator` | **Date**: 2026-03-15 | **Spec**: [spec.md](spec.md)
**Input**: GitHub issue #100 -- smcat Parser + Generator: bidirectional state-machine-cat support

## Summary

Bidirectional smcat (state-machine-cat) support for Frank.Statecharts: a lexer/parser that converts smcat text into a typed F# AST, a mapper that bridges to `StateMachineMetadata`, a generator that produces smcat text from metadata, and roundtrip consistency tests. Follows the established WSD parser pattern (`src/Frank.Statecharts/Wsd/`), placing all smcat modules under `src/Frank.Statecharts/Smcat/`. The parser and generator use smcat-specific format types; a separate mapper module converts to the shared statechart AST (spec 020) when that dependency lands.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank.Statecharts)
**Primary Dependencies**: Frank.Statecharts (project reference -- same project, internal modules)
**Storage**: N/A (stateless text parsing)
**Testing**: Expecto 10.2.3 + YoloDev.Expecto.TestSdk 0.14.3, in `test/Frank.Statecharts.Tests/`
**Target Platform**: .NET 8.0/9.0/10.0 (multi-target via `<TargetFrameworks>`)
**Project Type**: Library (internal modules within existing `Frank.Statecharts` project)
**Performance Goals**: Handle smcat inputs up to 500 lines without measurable allocation pressure (SC-008); no intermediate string concatenation in hot paths
**Constraints**: All modules are `internal` (accessed via `InternalsVisibleTo` from test project); follows WSD parser module pattern exactly
**Scale/Scope**: ~6 source files (Types, Lexer, Parser, LabelParser, Generator, Mapper), ~6 test files, 3+ golden file examples

### Dependency on Spec 020 (Shared Statechart AST)

The smcat parser defines its own format-specific types for lexing and parsing (`SmcatDocument`, `SmcatState`, `SmcatTransition`, etc.). A separate mapper module converts the smcat AST to the shared `StatechartDocument` from spec 020. The parser, generator, and all format-specific tests can be built independently. Only the mapper work package is blocked on spec 020 landing.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Resource-Oriented Design | N/A | Parser/generator is a dev tool, not a runtime resource API |
| II | Idiomatic F# | PASS | DUs for AST nodes, option types for optional fields, pipeline-friendly parse API, struct SourcePosition |
| III | Library, Not Framework | PASS | Internal parser modules within Frank.Statecharts; no CLI, no opinions beyond smcat parsing |
| IV | ASP.NET Core Native | N/A | No ASP.NET Core surface; parser is pure F# |
| V | Performance Parity | PASS | Mutable lexer state (matching WSD pattern), no intermediate string concat in hot paths, struct SourcePosition |
| VI | Resource Disposal Discipline | PASS | No IDisposable resources in parser (string input, immutable AST output) |
| VII | No Silent Exception Swallowing | PASS | Structured ParseFailure list with positions; no catch-all handlers |
| VIII | No Duplicated Logic | WATCH | LabelParser (event/guard/action parsing) and SourcePosition may share patterns with WSD; extract if duplication detected during implementation. SourcePosition struct is identical to WSD -- will use same type if spec 020 lands, otherwise tolerate short-lived duplication until migration. |

## Project Structure

### Documentation (this feature)

```
kitty-specs/013-smcat-parser-generator/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── contracts/           # Phase 1 output (API signatures)
└── tasks.md             # Phase 2 output (/spec-kitty.tasks)
```

### Source Code (repository root)

```
src/Frank.Statecharts/
├── Smcat/
│   ├── Types.fs          # smcat-specific AST types (SmcatDocument, SmcatState, etc.)
│   ├── Lexer.fs          # Tokenizer: smcat text -> Token list
│   ├── LabelParser.fs    # Transition label parser: "event [guard] / action"
│   ├── Parser.fs         # Parser: Token list -> SmcatDocument (ParseResult)
│   ├── Generator.fs      # Generator: StateMachineMetadata -> smcat text
│   └── Mapper.fs         # Mapper: SmcatDocument -> StatechartDocument (blocked on spec 020)
├── Wsd/                  # (existing, pattern reference)
│   ├── Types.fs
│   ├── Lexer.fs
│   ├── GuardParser.fs
│   └── Parser.fs
├── Frank.Statecharts.fsproj  # Add Smcat/*.fs entries
└── ...

test/Frank.Statecharts.Tests/
├── Smcat/
│   ├── LexerTests.fs         # Token-level tests
│   ├── LabelParserTests.fs   # Transition label parsing edge cases
│   ├── ParserTests.fs        # Full parse tests (states, transitions, composites)
│   ├── ErrorTests.fs         # Malformed input / structured failure reports
│   ├── GeneratorTests.fs     # Metadata -> smcat text
│   └── RoundTripTests.fs     # Parse -> map -> generate -> reparse consistency
├── Wsd/                      # (existing)
└── Frank.Statecharts.Tests.fsproj  # Add Smcat/*.fs entries
```

**Structure Decision**: Follow the established WSD pattern -- one subdirectory under `src/Frank.Statecharts/` for format-specific modules, mirrored subdirectory under `test/Frank.Statecharts.Tests/` for tests. All modules are `module internal Frank.Statecharts.Smcat.*`. The `LabelParser` is separated from the main parser because transition label parsing (`event [guard] / action`) is a self-contained grammar that benefits from independent testing (analogous to `Wsd/GuardParser.fs`).

## Complexity Tracking

No constitution violations requiring justification.
