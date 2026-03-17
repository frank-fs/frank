# Implementation Plan: ALPS Shared AST Migration

**Branch**: `023-alps-shared-ast-migration` | **Date**: 2026-03-16 | **Spec**: [kitty-specs/023-alps-shared-ast-migration/spec.md](spec.md)
**Input**: Issue #115 -- ALPS: migrate parser and generator to shared StatechartDocument AST
**Parent**: Issue #57 (Shared AST umbrella)

## Summary

Migrate the ALPS JSON parser and generator from format-specific types (`AlpsDocument`, `Descriptor`, `AlpsParseError`) to the shared `StatechartDocument` AST established by spec 020. The parser absorbs the descriptor-to-state mapping heuristics currently in `Mapper.fs`, producing `ParseResult` directly. The generator reconstructs the ALPS descriptor hierarchy from `StatechartDocument` with `AlpsMeta` annotations, including generator-side deduplication of shared transitions. The `AlpsMeta` discriminated union is extended with 4 new cases and the existing `AlpsExtension` case is expanded for full-fidelity roundtripping. After migration, `Alps/Types.fs` format-specific types and `Alps/Mapper.fs` are deleted. All ~110 tests are migrated to shared AST types.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting)
**Primary Dependencies**: Frank.Statecharts (project-internal), System.Text.Json (in-framework)
**Storage**: N/A (stateless parser/generator library)
**Testing**: Expecto 10.2.3, YoloDev.Expecto.TestSdk 0.14.3, Microsoft.NET.Test.Sdk 17.14.1
**Target Platform**: .NET 8.0/9.0/10.0 (multi-targeting, matching Frank.Statecharts)
**Project Type**: Single library project with test project
**Performance Goals**: N/A (not a hot path -- parse/generate are startup-time or developer-tool operations)
**Constraints**: Must preserve existing roundtrip property; must not break cross-format validator
**Scale/Scope**: 4 source files modified/deleted, 5 test files modified/deleted, ~110 tests migrated

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | N/A | Statecharts subsystem, not HTTP resource API |
| II. Idiomatic F# | PASS | Uses DUs (AlpsMeta), Option types, pipeline-friendly functions, computation expressions not applicable here |
| III. Library, Not Framework | PASS | Pure parser/generator library, no opinions beyond statechart modeling |
| IV. ASP.NET Core Native | N/A | No ASP.NET Core interaction in parser/generator |
| V. Performance Parity | PASS | No performance-sensitive hot paths; parser/generator are startup-time |
| VI. Resource Disposal Discipline | PASS | `JsonDocument.Parse` result bound with `use` in current parser (line 67); will be preserved |
| VII. No Silent Exception Swallowing | PASS | Parser returns `ParseResult` with explicit error list; no silent catches |
| VIII. No Duplicated Logic Across Modules | PASS | Migration eliminates duplication between Mapper.fs and parser; shared helper functions for `resolveRt`, `extractGuard`, `extractParameters` absorbed into parser |

## Project Structure

### Documentation (this feature)

```
kitty-specs/023-alps-shared-ast-migration/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research (already complete)
├── data-model.md        # Phase 1: AlpsMeta DU design
└── tasks.md             # Phase 2 output (created by /spec-kitty.tasks)
```

### Source Code (repository root)

```
src/Frank.Statecharts/
├── Ast/
│   └── Types.fs                 # MODIFIED: Extend AlpsMeta DU with 4 new cases + expand AlpsExtension
├── Alps/
│   ├── Types.fs                 # DELETED: Format-specific types (AlpsDocument, Descriptor, etc.)
│   ├── JsonParser.fs            # MODIFIED: Return ParseResult with StatechartDocument; absorb mapper heuristics
│   ├── JsonGenerator.fs         # MODIFIED: Accept StatechartDocument; reconstruct descriptor hierarchy
│   └── Mapper.fs                # DELETED: Bridge module no longer needed
└── Frank.Statecharts.fsproj     # MODIFIED: Remove Alps/Types.fs and Alps/Mapper.fs from compile list

test/Frank.Statecharts.Tests/
├── Alps/
│   ├── GoldenFiles.fs           # UNCHANGED: Golden file data
│   ├── TypeTests.fs             # DELETED: Tests format-specific types that no longer exist
│   ├── JsonParserTests.fs       # MODIFIED: Assert against ParseResult/StatechartDocument; absorb mapper test assertions
│   ├── JsonGeneratorTests.fs    # MODIFIED: Construct StatechartDocument with AlpsMeta; assert JSON output
│   ├── RoundTripTests.fs        # MODIFIED: Roundtrip compares StatechartDocument equality
│   └── MapperTests.fs           # DELETED: Mapper no longer exists; assertions absorbed into parser tests
└── Frank.Statecharts.Tests.fsproj  # MODIFIED: Remove Alps/TypeTests.fs and Alps/MapperTests.fs
```

**Structure Decision**: No new files created. This is a migration that modifies 2 source files, deletes 2 source files, modifies 3 test files, deletes 2 test files, and modifies 2 project files.

## Complexity Tracking

No constitution violations. No additional complexity justifications needed.

---

## Phase 0: Research

Research is already complete. See [kitty-specs/023-alps-shared-ast-migration/research.md](research.md).

### Key Decisions from Research

| # | Decision | Rationale |
|---|----------|-----------|
| D-001 | Expand `AlpsExtension` to 3 fields: `id: string * href: string option * value: string option` | Current shape loses `href` field, breaking full fidelity |
| D-002 | Add 4 new `AlpsMeta` cases: `AlpsDocumentation`, `AlpsLink`, `AlpsDataDescriptor`, `AlpsVersion` | Required for full-fidelity roundtripping |
| D-003 | Absorb mapper heuristics into parser (not generator) | Parser is the logical place for descriptor classification |
| D-004 | Generator-side deduplication for shared transitions | Parser emits one `TransitionEdge` per referencing state (semantically accurate); generator detects duplicates by matching event name + `AlpsDescriptorHref` annotation and emits single top-level descriptor + href references |
| D-005 | Annotations emitted in deterministic order | Roundtrip equality depends on annotation ordering; parser/generator must be consistent |
| D-006 | `TypeTests.fs` and `MapperTests.fs` deleted (not migrated) | The types they test no longer exist; mapper assertions absorbed into parser tests |

---

## Phase 1: Design & Contracts

### Data Model: AlpsMeta Extension

The `AlpsMeta` discriminated union in `src/Frank.Statecharts/Ast/Types.fs` is extended as follows:

#### Current AlpsMeta (lines 78-81)

```fsharp
type AlpsMeta =
    | AlpsTransitionType of AlpsTransitionKind
    | AlpsDescriptorHref of string
    | AlpsExtension of name: string * value: string
```

#### Target AlpsMeta

```fsharp
type AlpsMeta =
    | AlpsTransitionType of AlpsTransitionKind
    | AlpsDescriptorHref of string
    | AlpsExtension of id: string * href: string option * value: string option
    | AlpsDocumentation of format: string option * value: string
    | AlpsLink of rel: string * href: string
    | AlpsDataDescriptor of id: string * doc: (string option * string) option
    | AlpsVersion of string
```

**Breaking change assessment**: `AlpsExtension` field names change from `name * value` to `id * href option * value option`. This is a binary-breaking change within `Frank.Statecharts` internals. All pattern matches on `AlpsExtension` must be updated. Since `AlpsMeta` is `internal` (the module is `module internal`), this does not affect external consumers.

**Annotation placement rules**:
- `AlpsVersion` -> `StatechartDocument.Annotations`
- `AlpsDocumentation` (document-level) -> `StatechartDocument.Annotations`
- `AlpsDocumentation` (state-level) -> `StateNode.Annotations`
- `AlpsDocumentation` (transition-level) -> `TransitionEdge.Annotations`
- `AlpsLink` (document-level) -> `StatechartDocument.Annotations`
- `AlpsLink` (state-level) -> `StateNode.Annotations`
- `AlpsDataDescriptor` -> `StatechartDocument.Annotations`
- `AlpsTransitionType` -> `TransitionEdge.Annotations`
- `AlpsDescriptorHref` -> `TransitionEdge.Annotations`
- `AlpsExtension` (non-guard, on state) -> `StateNode.Annotations`
- `AlpsExtension` (non-guard, on transition) -> `TransitionEdge.Annotations`
- `AlpsExtension` (document-level) -> `StatechartDocument.Annotations`

### Parser Design: Heuristic Absorption

The parser must perform a two-pass approach:

**Pass 1 (existing)**: Parse JSON into intermediate descriptor records (can be internal types within JsonParser.fs, not exposed). This preserves the clean JSON-to-record mapping.

**Pass 2 (new, absorbs Mapper.toStatechartDocument)**: Convert intermediate descriptors to `StatechartDocument`:
1. Build descriptor index (id -> descriptor) for href resolution
2. Collect rt targets (recursive scan)
3. Classify top-level descriptors: state vs. data descriptor using 3-part heuristic
4. For each state descriptor: extract `StateNode` + `TransitionEdge` entries from children
5. For each data descriptor: emit `AlpsDataDescriptor` annotation
6. Emit document-level annotations (version, documentation, links, extensions)

**Return type change**: `Result<AlpsDocument, AlpsParseError list>` -> `ParseResult`

The intermediate descriptor type can be a private type within `JsonParser.fs` (same shape as current `Descriptor` but not exposed). This keeps the JSON parsing clean while making the module self-contained.

### Generator Design: Hierarchy Reconstruction

The generator reconstructs the ALPS descriptor hierarchy from `StatechartDocument`:

1. **Extract annotations**: Read `AlpsVersion`, `AlpsDocumentation`, `AlpsLink`, `AlpsExtension`, `AlpsDataDescriptor` from `StatechartDocument.Annotations`
2. **Extract states**: Get all `StateNode` elements from `StatechartDocument.Elements`
3. **Extract transitions**: Get all `TransitionEdge` elements, group by `Source`
4. **Shared transition deduplication** (D-004):
   - Identify transitions with `AlpsDescriptorHref` annotations
   - Group these by event name; if multiple states reference the same event via `AlpsDescriptorHref`, this is a shared transition
   - Emit one top-level descriptor for the shared transition (using the full transition data from the first occurrence)
   - Emit `{ "href": "#eventName" }` references inside each referencing state
5. **Reconstruct state descriptors**: Each `StateNode` becomes a semantic descriptor. Its transitions (grouped by source) become child descriptors. Shared transitions become href-only children.
6. **Ordering**: Data descriptors first, then state descriptors (matching ALPS convention), then top-level shared transition descriptors

**Default values** (when no ALPS annotations present):
- Transition type: `unsafe` (FR-016)
- Version: `"1.0"` (FR-017)

### Annotation Ordering Convention (D-005)

For deterministic roundtrip equality, annotations are emitted in this order:
1. `AlpsTransitionType` (always first on transitions)
2. `AlpsDescriptorHref` (if present)
3. `AlpsDocumentation` (if present)
4. `AlpsExtension` elements (in original document order)
5. `AlpsLink` elements (in original document order)

Document-level annotations:
1. `AlpsVersion`
2. `AlpsDocumentation`
3. `AlpsLink` elements (in order)
4. `AlpsExtension` elements (in order)
5. `AlpsDataDescriptor` elements (in order)

### Test Migration Strategy

| Current File | Action | Target File | Test Count |
|-------------|--------|-------------|------------|
| `GoldenFiles.fs` | Unchanged | `GoldenFiles.fs` | 0 (data) |
| `TypeTests.fs` | Delete | N/A | 10 deleted |
| `JsonParserTests.fs` | Rewrite | `JsonParserTests.fs` | 39 rewritten + ~23 absorbed from MapperTests |
| `JsonGeneratorTests.fs` | Rewrite | `JsonGeneratorTests.fs` | 16 rewritten |
| `MapperTests.fs` | Delete (absorbed) | `JsonParserTests.fs` | 33 absorbed |
| `RoundTripTests.fs` | Rewrite | `RoundTripTests.fs` | 10 rewritten |

**Parser test absorption**: The following MapperTests test lists are absorbed into JsonParserTests:
- `Alps.Mapper.StateExtraction` (5 tests) -> new `Alps.JsonParser.StateExtraction` test list
- `Alps.Mapper.TransitionMapping` (5 tests) -> new `Alps.JsonParser.TransitionMapping` test list
- `Alps.Mapper.GuardExtraction` (4 tests) -> new `Alps.JsonParser.GuardExtraction` test list
- `Alps.Mapper.HttpMethodHints` (3 tests) -> new `Alps.JsonParser.HttpMethodHints` test list
- `Alps.Mapper.Roundtrip` (7 tests) -> absorbed into `Alps.RoundTrip` (these test parse-generate-reparse)
- `Alps.Mapper.EdgeCases` (8 tests) -> new `Alps.JsonParser.EdgeCases` test list (parser edge cases)

### Cross-Format Validator Impact

**No code changes needed.** The cross-format validator (`src/Frank.Statecharts/Validation/`) and its tests (`test/Frank.Statecharts.Tests/Validation/`) do not reference `AlpsDocument`, `Alps.Types`, or `Alps.Mapper`. They work exclusively with `StatechartDocument` and `FormatArtifact`. Verified by grep across all validation source and test files.

The only observable change: ALPS `FormatArtifact` values can now be constructed directly from parser output (no mapper step), which is the architectural goal. SC-008 will be verified by adding a cross-format test using ALPS parser output directly.

### Compilation Order Changes

In `src/Frank.Statecharts/Frank.Statecharts.fsproj`, the ALPS section changes from:

```xml
<!-- ALPS parser -->
<Compile Include="Alps/Types.fs" />
<Compile Include="Alps/JsonParser.fs" />
<Compile Include="Alps/JsonGenerator.fs" />
<Compile Include="Alps/Mapper.fs" />
```

To:

```xml
<!-- ALPS parser -->
<Compile Include="Alps/JsonParser.fs" />
<Compile Include="Alps/JsonGenerator.fs" />
```

In `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`, the ALPS section changes from:

```xml
<!-- ALPS tests -->
<Compile Include="Alps/GoldenFiles.fs" />
<Compile Include="Alps/TypeTests.fs" />
<Compile Include="Alps/JsonParserTests.fs" />
<Compile Include="Alps/JsonGeneratorTests.fs" />
<Compile Include="Alps/RoundTripTests.fs" />
<Compile Include="Alps/MapperTests.fs" />
```

To:

```xml
<!-- ALPS tests -->
<Compile Include="Alps/GoldenFiles.fs" />
<Compile Include="Alps/JsonParserTests.fs" />
<Compile Include="Alps/JsonGeneratorTests.fs" />
<Compile Include="Alps/RoundTripTests.fs" />
```

### Existing AlpsExtension Pattern Match Sites

The existing `AlpsExtension of name * value` pattern is used in the following locations that must be updated when the signature changes to `AlpsExtension of id * href option * value option`:

1. `src/Frank.Statecharts/Alps/Mapper.fs` (deleted -- no update needed)
2. `src/Frank.Statecharts/Wsd/Generator.fs` -- check for wildcard match on AlpsMeta (if any)
3. Any cross-format validator code (verified: none found)

These must be identified and updated as part of the AlpsMeta extension work.

---

## Post-Design Constitution Re-check

| Principle | Status | Notes |
|-----------|--------|-------|
| VI. Resource Disposal Discipline | PASS | `JsonDocument.Parse` result in parser still bound with `use`; generator's `MemoryStream` and `Utf8JsonWriter` still bound with `use` |
| VII. No Silent Exception Swallowing | PASS | Parser catches `JsonException` and returns it in `ParseResult.Errors` list -- no silent swallowing |
| VIII. No Duplicated Logic Across Modules | PASS | Migration actively removes duplication: mapper heuristics absorbed into parser; no remaining duplicated logic between modules |

No new violations introduced. All gates pass.
