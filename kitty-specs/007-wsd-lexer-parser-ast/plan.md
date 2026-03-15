# Implementation Plan: WSD Lexer, Parser, and AST

**Branch**: `007-wsd-lexer-parser-ast` | **Date**: 2026-03-07 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `kitty-specs/007-wsd-lexer-parser-ast/spec.md`
**Research**: [research.md](research.md) | **Data Model**: [data-model.md](data-model.md) | **Quickstart**: [quickstart.md](quickstart.md)

## Summary

Implement a from-scratch F# lexer, parser, and typed AST for Web Sequence Diagrams (WSD) syntax, internal to the `Frank.Statecharts` project. The parser covers the full WSD syntax (participants, messages with four arrow types, notes, directives, grouping blocks) plus a guard extension syntax (`[guard: key=value, ...]`) for the Amundsen API design workflow. The parser produces a best-effort partial AST with structured warnings and errors, enabling downstream consumers (WSD-to-spec pipeline, #57) to use successfully parsed elements even when some constructs produce warnings. This is the P0 foundation for the WSD-to-statechart pipeline.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank.Statecharts)
**Primary Dependencies**: None beyond what Frank.Statecharts already has (no additional NuGet packages)
**Storage**: N/A (pure parser, stateless)
**Testing**: Expecto (matching existing Frank test patterns) in `test/Frank.Statecharts.Tests/`
**Target Platform**: .NET multi-target library (net8.0;net9.0;net10.0)
**Project Type**: Internal module within existing library project (Frank.Statecharts)
**Performance Goals**: Parse 1000-line WSD inputs without measurable allocation pressure; no intermediate string concatenation in hot paths (SC-007)
**Constraints**: Internal visibility only (no public API surface beyond Frank.Statecharts); no external NuGet dependencies
**Scale/Scope**: WSD diagrams typically 20-200 lines; must handle up to 1000 lines; 5+ nesting levels for grouping blocks

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. Resource-Oriented Design -- PASS (Not Directly Applicable)

The WSD parser is a pure text-processing module with no HTTP surface. It produces AST data consumed by the statechart pipeline, which itself produces resource-oriented artifacts. The parser has no opinion on URLs, methods, or handlers. No tension with resource-oriented design.

### II. Idiomatic F# -- PASS

- AST modeled entirely with discriminated unions (`DiagramElement`, `ArrowStyle`, `Direction`, `NotePosition`, `GroupKind`)
- `ParseResult` uses a record with `Diagram` (non-optional, always present, possibly partial) + `ParseFailure list` + `ParseWarning list` (no exceptions for parse errors)
- Lexer produces an immutable token list; parser is a pure function from tokens to AST
- Pipeline-friendly: `string -> ParseResult` top-level signature

### III. Library, Not Framework -- PASS

The parser is a pure function. No framework behavior, no lifecycle management, no opinions beyond WSD syntax. Consumers call `Wsd.parse` and get back a `ParseResult`.

### IV. ASP.NET Core Native -- PASS (Not Applicable)

The WSD parser has no ASP.NET Core dependency. It is a pure text parser consumed by other modules within Frank.Statecharts that do interact with ASP.NET Core. No platform abstractions to hide or expose.

### V. Performance Parity -- PASS

- Hand-written recursive descent parser (no parser combinator overhead)
- Lexer operates on `ReadOnlySpan<char>` internally but produces a `Token list` as output (no intermediate string allocations in tokenization)
- No external library overhead
- SC-007 establishes the performance bar: 1000-line inputs without allocation pressure

### VI. Resource Disposal Discipline -- PASS

The parser is pure: no `IDisposable` resources. Input is a `string`, output is an immutable `ParseResult` record. No streams, readers, or writers involved.

### VII. No Silent Exception Swallowing -- PASS

The parser does not use exceptions for control flow. Parse errors are returned as structured `ParseFailure` values in the result. No `try/with` blocks needed in the parser itself. If an unexpected internal error occurs, it propagates (no catch-all).

### VIII. No Duplicated Logic -- PASS

- Token source position tracking is defined once in the `Token` type and threaded through lexer and parser
- Arrow parsing logic is a single match expression (not duplicated per context)
- Guard annotation parsing is a single function called from the note-parsing path
- Error construction helper shared across all parse error sites

## Project Structure

### Documentation (this feature)

```
kitty-specs/007-wsd-lexer-parser-ast/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Parser strategy decisions, WSD syntax coverage, error recovery
├── data-model.md        # F# type definitions (tokens, AST, parse result)
└── quickstart.md        # Developer quickstart (parse, walk AST, handle errors)
```

### Source Code (repository root)

```
src/
└── Frank.Statecharts/                  # Existing project (from #87)
    ├── Frank.Statecharts.fsproj        # Multi-target: net8.0;net9.0;net10.0
    ├── Types.fs                        # Existing statechart types
    ├── Store.fs                        # Existing store abstraction
    ├── ...                             # Other existing modules
    │
    └── Wsd/                            # NEW: WSD parser modules (internal)
        ├── Types.fs                    # Token, AST DUs, ParseResult, ParseFailure, ParseWarning
        ├── Lexer.fs                    # Tokenizer: string -> Token list
        ├── Parser.fs                   # Recursive descent: Token list -> ParseResult
        └── GuardParser.fs              # Guard annotation extraction from note content

test/
└── Frank.Statecharts.Tests/            # Existing test project
    ├── ...                             # Existing test modules
    │
    ├── Wsd/                            # NEW: WSD parser test modules
    │   ├── LexerTests.fs              # Token-level tests for all WSD constructs
    │   ├── ParserTests.fs             # AST-level tests, acceptance scenarios
    │   ├── GuardParserTests.fs        # Guard annotation parsing tests
    │   ├── GroupingTests.fs           # Nested grouping block tests
    │   ├── ErrorTests.fs              # Structured failure report tests
    │   └── RoundTripTests.fs          # End-to-end WSD examples (Amundsen onboarding, tic-tac-toe)
    └── Program.fs                      # Expecto entry point (updated with new modules)
```

**Structure Decision**: Internal modules within the existing `Frank.Statecharts` project, under a `Wsd/` subdirectory. This follows the spec requirement that the parser is internal to Frank.Statecharts with no standalone use case. The `Wsd/` subdirectory keeps parser files separate from the statechart runtime files. New `.fs` files must be added to the `.fsproj` `<Compile>` items in dependency order: `Wsd/Types.fs` before `Wsd/Lexer.fs` before `Wsd/GuardParser.fs` before `Wsd/Parser.fs`.

## Parallel Work Analysis

### Dependency Graph

```
Wsd/Types.fs (AST type definitions)
    |
    ├── Wsd/Lexer.fs (depends on Token types from Types.fs)
    |       |
    |       └── Wsd/Parser.fs (depends on Lexer output + AST types)
    |
    └── Wsd/GuardParser.fs (depends on GuardAnnotation type from Types.fs)
            |
            └── Wsd/Parser.fs (calls GuardParser from note-parsing path)
```

### Parallelizable Work

1. **Types.fs** must come first -- all other modules depend on the shared type definitions.
2. **Lexer.fs** and **GuardParser.fs** can be developed in parallel after Types.fs is complete:
   - Lexer.fs depends only on `Token` and `SourcePosition` from Types.fs
   - GuardParser.fs depends only on `GuardAnnotation` from Types.fs
   - Neither depends on the other
3. **Parser.fs** must come last -- it consumes the lexer's token stream and calls GuardParser for note content.
4. **Test modules** can be developed alongside their corresponding source modules (TDD).

### Recommended Implementation Order

| Phase | Module | Depends On | Can Parallel With |
|-------|--------|------------|-------------------|
| 1 | Wsd/Types.fs | Nothing | -- |
| 2a | Wsd/Lexer.fs + LexerTests.fs | Types.fs | GuardParser |
| 2b | Wsd/GuardParser.fs + GuardParserTests.fs | Types.fs | Lexer |
| 3 | Wsd/Parser.fs + ParserTests.fs + GroupingTests.fs + ErrorTests.fs | Lexer, GuardParser | -- |
| 4 | RoundTripTests.fs | Parser (all modules) | -- |

## Design Decisions

### DD-01: Hand-Written Recursive Descent Parser

See [research.md](research.md) for full analysis. Recursive descent was chosen over parser combinators (FParsec) because: (a) no additional NuGet dependency, (b) full control over error recovery for partial AST generation, (c) WSD grammar is simple enough that a hand-written parser is straightforward, (d) better allocation profile than combinator-based approaches.

### DD-02: Partial AST with Warnings Model

The parser returns `ParseResult = { Diagram: Diagram; Errors: ParseFailure list; Warnings: ParseWarning list }`. The `Diagram` is always present (possibly empty/partial). Errors represent hard failures (unrecognizable syntax). Warnings represent valid WSD that may not map cleanly to Frank.Statecharts semantics. Consumers check `Errors` for fail-fast; `Warnings` for graceful degradation.

### DD-03: Internal Visibility

All WSD parser types and functions are `internal` to the `Frank.Statecharts` assembly. No `[<AutoOpen>]` modules, no public API surface. The parser is consumed exclusively by the statechart pipeline within the same assembly.

### DD-04: Separate Lexer and Parser Phases

Two-phase architecture (lexer then parser) rather than single-pass, because: (a) lexer can normalize line endings and strip comments before the parser sees them, (b) token stream enables lookahead without re-scanning, (c) error positions are easier to track with pre-computed token positions.

## Complexity Tracking

No constitution violations requiring justification. All new code lives within an existing project as internal modules. No new NuGet dependencies. No new projects.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| WSD syntax is not formally specified | Medium | Use websequencediagrams.com renderer as reference; test against Amundsen's published examples |
| Guard extension syntax may conflict with future WSD features | Low | Guard syntax is clearly delimited (`[guard: ...]`) and only parsed from note content, not general WSD |
| Partial AST recovery adds parser complexity | Medium | Start with error collection (skip-to-newline recovery); add finer-grained recovery per construct later |
| WSD namespace placement within Frank.Statecharts | Low | Frank.Statecharts project exists on master. Add Wsd/ subdirectory with new source files to the existing .fsproj. |
