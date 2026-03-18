# Implementation Plan: Cross-Format Validation Pipeline and AST Merge

**Branch**: `030-cross-format-validation-pipeline` | **Date**: 2026-03-18 | **Spec**: [spec.md](spec.md)
**Closes**: #117

## Summary

Add AST merge function to the cross-format validation pipeline. Merge combines multiple `StatechartDocument` values into one unified document using format priority ordering (SCXML > smcat > WSD for structure, ALPS annotations-only). Enhance validator with near-match detection using Jaro-Winkler string distance. Wire ALPS XML into pipeline dispatch. Add end-to-end integration tests with real format text.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0, 9.0, and 10.0 (multi-targeting)
**Primary Dependencies**: Frank.Statecharts (project-internal), existing Validation module
**Storage**: N/A
**Testing**: Expecto, existing `test/Frank.Statecharts.Tests/` infrastructure
**Performance Goals**: N/A — startup/CLI-time code
**Constraints**: All modules `internal`; no external dependencies for string distance (implement inline)
**Scale/Scope**: 3 new/modified source files, 1-2 new test files

## Constitution Check

- **I-VII**: All pass (internal library changes)
- **VIII. No Duplicated Logic**: PASS — merge logic is new, not duplicated. Near-match detection is a new validation rule, not a copy.

**Result**: All gates pass.

## Project Structure

### Source Code

```
src/Frank.Statecharts/
└── Validation/
    ├── Types.fs             # MODIFY: add FormatTag.AlpsXml
    ├── Merge.fs             # NEW: mergeSources function + format priority
    ├── StringDistance.fs     # NEW: Jaro-Winkler implementation (~30 lines)
    ├── CrossFormatRules.fs  # MODIFY: add near-match detection rule
    ├── Pipeline.fs          # MODIFY: add mergeSources, wire AlpsXml dispatch
    └── Validator.fs         # UNCHANGED

test/Frank.Statecharts.Tests/
└── Validation/
    ├── MergeTests.fs        # NEW: merge function tests
    ├── NearMatchTests.fs    # NEW: near-match detection tests
    └── PipelineTests.fs     # MODIFY: end-to-end integration tests
```

## Dependency Graph

```
WP01: Types.fs (FormatTag.AlpsXml) + StringDistance.fs + Pipeline.fs (AlpsXml dispatch)
  ↓
  ├── WP02: Merge.fs + MergeTests.fs
  ├── WP03: CrossFormatRules.fs (near-match) + NearMatchTests.fs
  └── WP04: End-to-end integration tests (PipelineTests.fs)
```

WP02 and WP03 can execute in parallel after WP01. WP04 depends on WP02 (needs merge) and WP03 (needs near-match).

## Key Design Decisions

### D-001: Merge as Left Fold

`mergeSources` sorts source documents by format priority (SCXML first, then smcat, then WSD, then ALPS), then folds: the first document is the base, subsequent documents enrich it. For each subsequent document, matching states accumulate annotations and non-None fields fill gaps. Matching transitions accumulate annotations. Unmatched states/transitions are added.

### D-002: Jaro-Winkler for Near-Match

Jaro-Winkler is better for short identifier strings than Levenshtein (it weights prefix matches). ~30 lines of F#, no external dependency. Threshold defaults to 0.8 (configurable).

### D-003: Format Priority as DU Ordering

The priority is encoded as a function `formatPriority: FormatTag -> int` where SCXML=0, smcat=1, WSD=2, Alps=3, AlpsXml=3, XState=4. Lower number = higher priority for structural fields.

### D-004: AlpsXml as Separate FormatTag

`FormatTag.AlpsXml` is a new case that dispatches to `Alps.XmlParser.parseAlpsXml`. It has the same merge priority as `Alps` (annotations only). This avoids overloading the `Alps` tag with format detection logic.

## Files Impacted

| File | Change Type | FRs |
|------|-------------|-----|
| `Validation/Types.fs` | Modify | FR-007 |
| `Validation/StringDistance.fs` | New | FR-012, FR-013 |
| `Validation/Merge.fs` | New | FR-001 through FR-005, FR-011 |
| `Validation/CrossFormatRules.fs` | Modify | FR-012, FR-013 |
| `Validation/Pipeline.fs` | Modify | FR-006, FR-007 |
| Test files | New/Modify | FR-008 through FR-010, FR-014, FR-015 |
