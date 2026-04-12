---
source: kitty-specs/028-scxml-native-annotations
type: plan
---

# Implementation Plan: SCXML Native Annotations and Generator Fidelity

**Branch**: `028-scxml-native-annotations` | **Date**: 2026-03-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/kitty-specs/028-scxml-native-annotations/spec.md`
**Closes**: #114

## Summary

Eliminate all lossy transformations in the SCXML parser and generator. Expand `ScxmlMeta` DU with `ScxmlOnEntry`, `ScxmlOnExit`, `ScxmlInitialElement`, and `ScxmlDataSrc` cases. Parser stores raw XML for executable content, captures `<initial>` child elements, `<data src>` attributes, and namespace origin. Generator reconstructs all captured content. Dual-layer: portable `StateActivities` for cross-format tools, raw XML annotations for round-trip fidelity.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0, 9.0, and 10.0 (multi-targeting)
**Primary Dependencies**: Frank.Statecharts (project-internal), System.Xml.Linq (in-framework)
**Storage**: N/A (stateless parser/generator library)
**Testing**: Expecto, dotnet test, existing test infrastructure in `test/Frank.Statecharts.Tests/Scxml/`
**Target Platform**: .NET multi-target library (net8.0;net9.0;net10.0)
**Project Type**: Single library project with test project
**Performance Goals**: N/A — startup/CLI-time code
**Constraints**: All modules are `internal`; breaking changes acceptable (unreleased)
**Scale/Scope**: 3 source files modified, 3-4 test files modified, 0 new files

## Constitution Check

- **I. Resource-Oriented Design**: N/A — Internal type system changes
- **II. Idiomatic F#**: PASS — DU expansion, pattern matching, raw XML as strings (pragmatic)
- **III. Library, Not Framework**: PASS — Internal changes only
- **IV. ASP.NET Core Native**: N/A — No ASP.NET Core changes
- **V. Performance Parity**: PASS — Startup/CLI-time code. Raw XML strings add payload but no hot-path cost.
- **VI. Resource Disposal Discipline**: PASS — `XElement.Parse()` returns non-disposable. `StringWriter` in generator already uses `use`.
- **VII. No Silent Exception Swallowing**: PASS — Parser already propagates XmlException. New code follows same pattern.
- **VIII. No Duplicated Logic Across Modules**: PASS — Reuses existing annotation patterns (`ScxmlInvoke` multiple-per-state).

**Result**: All gates pass.

## Project Structure

### Source Code (files modified)

```
src/Frank.Statecharts/
├── Ast/
│   └── Types.fs                 # ScxmlMeta expansion (4 new cases)
└── Scxml/
    ├── Parser.fs                # Capture onentry/onexit, <initial>, <data src>, namespace
    └── Generator.fs             # Emit onentry/onexit, <initial>, <data src>, respect namespace

test/Frank.Statecharts.Tests/
└── Scxml/
    ├── ParserTests.fs           # New tests for captured content
    ├── GeneratorTests.fs        # New tests for emitted content
    └── RoundTripTests.fs        # Extended with comprehensive fixtures
```

## Dependency Graph

```
WP01: Ast/Types.fs (DU expansion)
  ↓
  ├── WP02: Parser.fs + ParserTests.fs (capture all content)
  └── WP03: Generator.fs + GeneratorTests.fs (emit all content)
       ↓
       WP04: RoundTripTests.fs (depends on both parser + generator)
```

WP02 and WP03 can execute in parallel after WP01. WP04 depends on both.

## Key Design Decisions

### D-001: Raw XML Storage for Executable Content

Store `<onentry>`/`<onexit>` blocks as raw XML strings via `XElement.ToString()`. Generator reconstructs via `XElement.Parse()`. No typed AST for executable content — keeps scope manageable while achieving zero information loss. Expert consensus: Don (pragmatic), Scott (dual-layer), Dave (minimal code).

### D-002: One Annotation Per Block

Multiple `<onentry>` blocks on the same state produce multiple `ScxmlOnEntry` annotations (not concatenated). Matches existing `ScxmlInvoke` pattern. Expert consensus: unanimous.

### D-003: Dual-Layer Activities

Portable `StateActivities.Entry`/`Exit` populated with action descriptions (`"send done"`, `"log hello"`). Format-specific `ScxmlOnEntry(xml)` preserves full XML. Both layers present when content exists.

### D-004: Namespace Tracking

Populate existing `ScxmlNamespace` DU case (already defined, never stored). Generator checks for annotation to decide whether to use W3C namespace or no-namespace.

## Files Impacted

| File | Change Type | FRs Addressed |
|------|-------------|---------------|
| `src/Frank.Statecharts/Ast/Types.fs` | Modify | FR-001, FR-002, FR-003, FR-004 |
| `src/Frank.Statecharts/Scxml/Parser.fs` | Modify | FR-005 through FR-010 |
| `src/Frank.Statecharts/Scxml/Generator.fs` | Modify | FR-011 through FR-015 |
| `test/Frank.Statecharts.Tests/Scxml/ParserTests.fs` | Modify | FR-005 through FR-010, FR-018 |
| `test/Frank.Statecharts.Tests/Scxml/GeneratorTests.fs` | Modify | FR-011 through FR-015, FR-018 |
| `test/Frank.Statecharts.Tests/Scxml/RoundTripTests.fs` | Modify | FR-016, FR-018 |
