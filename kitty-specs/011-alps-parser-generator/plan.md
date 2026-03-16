# Implementation Plan: ALPS Parser and Generator

**Branch**: `011-alps-parser-generator` | **Date**: 2026-03-15 | **Spec**: [kitty-specs/011-alps-parser-generator/spec.md](spec.md)
**Input**: Feature specification from `kitty-specs/011-alps-parser-generator/spec.md`
**GitHub Issue**: #97
**Dependencies**: Shared Statechart AST (spec 020, #87) -- mapper WP only
**Parallel With**: #98 (SCXML), #100 (smcat), #90 (WSD Parser)

## Summary

Implement bidirectional ALPS (Application-Level Profile Semantics) support for the Frank.Statecharts spec pipeline. The parser reads ALPS JSON and XML documents into a typed ALPS-specific F# AST. A separate mapper converts the ALPS AST to the shared statechart AST (spec 020). The generator produces ALPS JSON from the shared AST. Golden file tests validate roundtrip consistency using the tic-tac-toe and onboarding examples from #57.

The architecture follows option C from planning: ALPS-specific parse types are self-contained (no dependency on spec 020), while the mapper is the only component blocked on the shared AST landing. This enables parallel development of parser, generator, and format-specific tests.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank.Statecharts)
**Primary Dependencies**: System.Text.Json (JSON parsing/generation), System.Xml.Linq (XML parsing) -- both built-in, zero NuGet additions
**Storage**: N/A (stateless parsing/generation)
**Testing**: Expecto (matching existing Frank.Statecharts.Tests patterns)
**Target Platform**: .NET library (multi-target net8.0;net9.0;net10.0)
**Project Type**: Internal module within existing Frank.Statecharts project
**Performance Goals**: Parse 100+ descriptor ALPS documents without degradation (edge case from spec)
**Constraints**: No external NuGet dependencies for serialization; manual JsonDocument/Utf8JsonWriter for JSON, XDocument for XML
**Scale/Scope**: 6-8 source files under `src/Frank.Statecharts/Alps/`, tests under `test/Frank.Statecharts.Tests/Alps/`

### Key Architecture Decisions

**AD-001 (Hybrid AST Strategy)**: Define ALPS-specific parse types (`AlpsDocument`, `Descriptor`, `DescriptorType`, etc.) in `Alps/Types.fs` for parsing. A separate mapper module converts to the shared statechart AST from spec 020. Parser, generator, and format-specific tests can be built independently. Only the mapper WP is blocked on spec 020.

**AD-002 (Serialization Libraries)**: Use `System.Text.Json` (`JsonDocument`/`JsonElement`) for parsing ALPS JSON and `Utf8JsonWriter` for generating ALPS JSON. Use `System.Xml.Linq` (`XDocument`/`XElement`) for parsing ALPS XML. All built-in -- zero NuGet dependencies added. Manual construction for JSON generation avoids needing FSharp.SystemTextJson while keeping the ALPS JSON structure simple enough to emit directly.

**AD-003 (Module Organization)**: Follow the WSD pattern (`src/Frank.Statecharts/Wsd/`). ALPS files live under `src/Frank.Statecharts/Alps/`. Test files under `test/Frank.Statecharts.Tests/Alps/`. Golden files as embedded resources or string literals in test modules.

**AD-004 (Error Handling)**: Return `Result<AlpsDocument, AlpsParseError list>` from parsers. No exceptions thrown for malformed input. `use` bindings for all `IDisposable` values (`JsonDocument`, `XDocument`) per constitution principle VI.

**AD-005 (Forward Compatibility)**: Unknown JSON properties are silently ignored (FR-017). Unknown XML elements are silently ignored. This follows the ALPS specification's forward-compatibility guidance.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | PASS | ALPS describes semantic descriptors and transitions -- directly supports resource-oriented hypermedia design. No route-centric patterns introduced. |
| II. Idiomatic F# | PASS | Discriminated unions for descriptor types, option types for nullable fields, Result type for error handling, pipeline-friendly parse functions. |
| III. Library, Not Framework | PASS | Library-level only. No CLI wiring (deferred to #94). No opinions beyond ALPS parsing/generation. |
| IV. ASP.NET Core Native | PASS / N/A | No ASP.NET Core integration in this spec. Pure parsing/generation library. |
| V. Performance Parity | PASS | System.Text.Json and System.Xml.Linq are high-performance built-in libraries. No allocation-heavy patterns in hot paths. |
| VI. Resource Disposal Discipline | PASS | `JsonDocument` and `XDocument` will use `use` bindings. Documented in AD-004. |
| VII. No Silent Exception Swallowing | PASS | Parse errors return structured `Result.Error`. No catch-all handlers. |
| VIII. No Duplicated Logic | PASS | JSON and XML parsers share the same AST types. Common descriptor-mapping logic extracted to shared helpers. |

## Project Structure

### Documentation (this feature)

```
kitty-specs/011-alps-parser-generator/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
└── tasks.md             # Phase 2 output (created by /spec-kitty.tasks)
```

### Source Code (repository root)

```
src/Frank.Statecharts/
├── Alps/
│   ├── Types.fs           # ALPS-specific AST types (AlpsDocument, Descriptor, etc.)
│   ├── JsonParser.fs      # Parse ALPS JSON → AlpsDocument
│   ├── XmlParser.fs       # Parse ALPS XML → AlpsDocument
│   ├── JsonGenerator.fs   # Generate ALPS JSON from AlpsDocument
│   └── Mapper.fs          # Map AlpsDocument → StatechartDocument (blocked on spec 020)
├── Wsd/                   # (existing)
│   ├── Types.fs
│   ├── Lexer.fs
│   ├── GuardParser.fs
│   └── Parser.fs
├── Types.fs               # (existing runtime types)
└── ...

test/Frank.Statecharts.Tests/
├── Alps/
│   ├── TypeTests.fs       # AST construction and equality tests
│   ├── JsonParserTests.fs # JSON parsing tests including golden files
│   ├── XmlParserTests.fs  # XML parsing tests including golden files
│   ├── JsonGeneratorTests.fs  # Generator output tests including golden files
│   ├── MapperTests.fs     # Mapper tests (blocked on spec 020)
│   ├── RoundTripTests.fs  # Parse → map → generate → re-parse consistency
│   └── GoldenFiles.fs     # Golden file string constants (tic-tac-toe, onboarding)
├── Wsd/                   # (existing)
└── ...
```

**Structure Decision**: ALPS modules are internal to the Frank.Statecharts project (following the WSD pattern). No new .fsproj files needed. Files are added to the existing `Frank.Statecharts.fsproj` compile list and `Frank.Statecharts.Tests.fsproj` compile list.

## Complexity Tracking

No constitution violations requiring justification. All decisions align with existing patterns.
