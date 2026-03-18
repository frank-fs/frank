# Implementation Plan: ALPS Native Annotations and Full Fidelity

**Branch**: `029-alps-native-annotations` | **Date**: 2026-03-18 | **Spec**: [spec.md](spec.md)
**Closes**: #115

## Summary

Fix JSON round-trip fidelity gaps, add ALPS XML parser and generator, extract shared classification logic per constitution Principle VIII. No `AlpsMeta` DU expansion needed — existing 7 cases are sufficient.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0, 9.0, and 10.0 (multi-targeting)
**Primary Dependencies**: Frank.Statecharts (project-internal), System.Text.Json (JSON), System.Xml.Linq (XML, already available from SCXML)
**Storage**: N/A
**Testing**: Expecto, existing `test/Frank.Statecharts.Tests/Alps/` infrastructure
**Performance Goals**: N/A — startup/CLI-time code
**Constraints**: All modules `internal`; breaking changes acceptable (unreleased)
**Scale/Scope**: 3 new files, 2 modified files, 3-4 test files

## Constitution Check

- **I-VII**: All pass (same rationale as specs 027/028 — internal library changes)
- **VIII. No Duplicated Logic**: CRITICAL — JSON and XML parsers share classification heuristics (`isStateDescriptor`, `collectRtTargets`, `buildDescriptorIndex`, etc.). These MUST be extracted to a shared module `Alps/Classification.fs`.

**Result**: All gates pass. Principle VIII drives the architecture (shared classification module).

## Project Structure

### Source Code

```
src/Frank.Statecharts/
└── Alps/
    ├── Classification.fs        # NEW: shared intermediate types + Pass 2 heuristics
    ├── JsonParser.fs            # MODIFY: extract Pass 2 to Classification.fs
    ├── JsonGenerator.fs         # MODIFY: JSON fidelity fixes
    ├── XmlParser.fs             # NEW: ALPS XML parser (Pass 1 XML, shared Pass 2)
    └── XmlGenerator.fs          # NEW: ALPS XML generator

test/Frank.Statecharts.Tests/
└── Alps/
    ├── JsonParserTests.fs       # MODIFY: add round-trip tests
    ├── JsonGeneratorTests.fs    # MODIFY: add fidelity tests
    ├── XmlParserTests.fs        # NEW: XML parser tests
    ├── XmlGeneratorTests.fs     # NEW: XML generator tests
    └── RoundTripTests.fs        # NEW or MODIFY: cross-format equivalence
```

## Dependency Graph

```
WP01: Classification.fs (extract shared logic)
  ↓
  ├── WP02: JsonParser.fs refactor + fidelity fixes
  ├── WP03: XmlParser.fs (new, uses Classification)
  └── WP04: XmlGenerator.fs (new)
       ↓
       WP05: Round-trip + cross-format tests
```

WP02, WP03, WP04 can execute in parallel after WP01. WP05 depends on all.

## Key Design Decisions

### D-001: Shared Classification Module

Extract `ParsedDescriptor`, `ParsedExtension`, `ParsedLink`, `isStateDescriptor`, `collectRtTargets`, `buildDescriptorIndex`, `extractTransitions`, `toStateNode`, `buildStateAnnotations`, `buildTransitionAnnotations` into `Alps/Classification.fs`. Both JSON and XML parsers import from this module.

### D-002: XML Parser Structure

Pass 1: `XDocument` → `ParsedDescriptor list` (convert XML elements to the same intermediate types).
Pass 2: Reuse shared classification from `Classification.fs` → `StatechartDocument`.

### D-003: No AlpsMeta Expansion

The existing 7 cases cover all ALPS concepts. No new cases needed.

## Files Impacted

| File | Change Type | FRs |
|------|-------------|-----|
| `Alps/Classification.fs` | New | FR-005, FR-008 |
| `Alps/JsonParser.fs` | Modify | FR-001, FR-002 |
| `Alps/JsonGenerator.fs` | Modify | FR-002, FR-003, FR-004 |
| `Alps/XmlParser.fs` | New | FR-005, FR-006, FR-008 |
| `Alps/XmlGenerator.fs` | New | FR-007 |
| Test files | New/Modify | FR-009, FR-010, FR-011 |
