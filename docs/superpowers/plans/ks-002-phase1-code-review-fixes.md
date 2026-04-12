---
source: kitty-specs/002-phase1-code-review-fixes
type: plan
---

# Implementation Plan: Phase 1.1 Code Review Fixes

**Branch**: `002-phase1-code-review-fixes` | **Date**: 2026-03-06 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `kitty-specs/002-phase1-code-review-fixes/spec.md`
**Tracking Issue**: #81 | **Parent**: #80

## Summary

Resolve 23 code review findings from Phase 1 (001-semantic-resources-phase1) organized by module/subsystem. Fixes span correctness bugs (Tier 1), design issues (Tier 2), F# idiom improvements (Tier 3), and minor polish (Tier 4). All must pass before Phase 2 begins. Constitution principles VI (Resource Disposal), VII (No Silent Exception Swallowing), VIII (No Duplicated Logic) are enforced as acceptance gates.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 10.0 (Frank.Cli.Core, Frank.Cli, samples) and .NET 8.0/9.0/10.0 multi-target (Frank.LinkedData)
**Primary Dependencies**: dotNetRdf 3.5.1, FSharp.Compiler.Service (pin from 43.10.*), Ionide.ProjInfo (pin from 0.74.*), System.CommandLine (pin), FsToolkit.ErrorHandling (new — for result/option/async CEs)
**Storage**: N/A (no persistence changes)
**Testing**: Expecto 10.x + ASP.NET Core TestHost (265 existing tests as baseline)
**Target Platform**: .NET 8.0/9.0/10.0 (library), .NET 10.0 (CLI tooling)
**Project Type**: Multi-project library + CLI
**Performance Goals**: Maintain parity with Phase 1 baseline; balance DU idioms with performance on hot paths (per clarification)
**Constraints**: No breaking public API changes; all 265 existing tests must pass
**Scale/Scope**: ~8,500 lines across 103 files; 23 discrete fixes

## Planning Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| WP decomposition | By module/subsystem | Keeps each WP focused on one codebase area |
| `Int64` XSD mapping | `xsd:long` | Precise 64-bit semantics, matches .NET exactly, OWL-compatible (valid subtype of `xsd:integer`) |
| Cache key strategy | Structural hash of RDF-relevant properties | Content-addressable, survives object recreation |
| CE dependency | Add FsToolkit.ErrorHandling | Provides `result {}`, `option {}`, `asyncResult {}` without reinventing |
| Error handling | Fail-fast | Exceptions for unrecoverable errors; Result/Option only for expected recoverable outcomes (parsing, validation) |
| Composition style | CEs or piped module functions | Extract/unwrap once at top-level call site; pattern match within body; no repeated `Async.RunSynchronously`, `Option.get`, etc. mid-function |
| DU vs performance | Balance case-by-case | Escalate to user when trade-off is unclear |
| Scope | Issue #81 only | Project-wide constitution audit deferred to separate issue |

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Principle I — Resource-Oriented Design
**Status**: PASS. No changes to resource abstraction or routing. Fixes are internal to LinkedData and CLI modules.

### Principle II — Idiomatic F#
**Status**: ACTIVE — this feature directly enforces it. DUs replace string discriminators (FR-014, with performance balance). `[<AutoOpen>]` removed from `FSharpRdf` (FR-017). Match pyramids replaced with CEs/piped module functions (FR-015). Imperative accumulation replaced with folds (FR-016). Dead parameters removed (FR-018).

### Principle III — Library, Not Framework
**Status**: PASS. No new opinions or mandatory patterns imposed on users.

### Principle IV — ASP.NET Core Native
**Status**: PASS. Accept header parsing moves to `MediaTypeHeaderValue` (ASP.NET Core built-in). ILogger injection follows ASP.NET Core patterns.

### Principle V — Performance Parity
**Status**: WATCH. DU refactoring and structural hashing must not regress hot paths. Benchmark if `InstanceProjector` cache key generation shows up in profiles.

### Principle VI — Resource Disposal Discipline
**Status**: ACTIVE — this feature directly enforces it. `StreamReader` in `GraphLoader.fs` and `JsonDocument` in `CompileCommand.verifyRoundTrip` get `use` bindings (FR-004).

### Principle VII — No Silent Exception Swallowing
**Status**: ACTIVE — this feature directly enforces it. Catch-all `with | _ ->` in `WebHostBuilderExtensions.fs:129` replaced with ILogger-backed handler. Fail-fast pattern enforced for unrecoverable errors (FR-007).

### Principle VIII — No Duplicated Logic
**Status**: ACTIVE — this feature directly enforces it. URI helpers consolidated into shared `UriHelpers` module (FR-012). `localName`/`namespaceUri` deduplicated (FR-020).

## Project Structure

### Documentation (this feature)

```
kitty-specs/002-phase1-code-review-fixes/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0: dependency research
├── data-model.md        # Phase 1: affected types and modules
├── checklists/          # Quality checklists
└── tasks/               # Work packages (created by /spec-kitty.tasks)
```

### Source Code (affected files by module)

```
src/
├── Frank.LinkedData/
│   ├── Frank.LinkedData.fsproj           # Multi-target net8.0;net9.0;net10.0
│   ├── LinkedDataConfig.fs               # FR-015: nested match → CE/pipe
│   ├── WebHostBuilderExtensions.fs       # FR-007: catch-all handler → ILogger
│   │                                     # FR-009: String.Contains → MediaTypeHeaderValue
│   │                                     # FR-011: Assembly.GetEntryAssembly null check
│   ├── Rdf/
│   │   ├── GraphLoader.fs                # FR-004: StreamReader use binding
│   │   │                                 # FR-015: nested match → CE/pipe
│   │   └── InstanceProjector.fs          # FR-001: typed literals in @graph
│   │                                     # FR-003: Decimal → xsd:decimal
│   │                                     # FR-008: structural hash cache key
│   │                                     # FR-020: deduplicate localName
│   └── Negotiation/
│       └── JsonLdFormatter.fs            # FR-001: typed literals in @graph
│                                         # FR-020: deduplicate localName/namespaceUri
│
├── Frank.Cli.Core/
│   ├── Frank.Cli.Core.fsproj             # FR-013: pin wildcard versions
│   │                                     # FR-022: FSharp.Core consistency
│   │                                     # NEW: add FsToolkit.ErrorHandling
│   ├── Analysis/
│   │   ├── AstAnalyzer.fs                # FR-016: ResizeArray+ref → fold
│   │   └── TypeAnalyzer.fs               # FR-002: Int64 → xsd:long consistently
│   ├── Rdf/
│   │   ├── FSharpRdf.fs                  # FR-017: remove [<AutoOpen>]
│   │   └── Vocabularies.fs               # FR-021: [<Literal>] annotations
│   ├── Extraction/
│   │   ├── TypeMapper.fs                 # FR-002: Int64 → xsd:long
│   │   │                                 # FR-012: extract helpers to UriHelpers
│   │   ├── ShapeGenerator.fs             # FR-012: extract helpers to UriHelpers
│   │   ├── RouteMapper.fs                # FR-012: extract helpers to UriHelpers
│   │   ├── CapabilityMapper.fs           # FR-012: extract helpers to UriHelpers
│   │   ├── VocabularyAligner.fs          # (no changes expected)
│   │   └── UriHelpers.fs                 # NEW: shared URI construction module
│   ├── State/
│   │   ├── ExtractionState.fs            # FR-010: Dictionary<Uri,_> → Map<string,_>
│   │   └── DiffEngine.fs                 # FR-014: DiffEntry.Type string → DU/perf
│   ├── Commands/
│   │   ├── ExtractCommand.fs             # FR-018: remove dead scope parameter
│   │   ├── CompileCommand.fs             # FR-004: JsonDocument use binding
│   │   └── ValidateCommand.fs            # FR-014: ValidationIssue.Severity string → DU/perf
│   └── Output/                           # (no changes expected)
│
├── Frank.Cli/
│   ├── Frank.Cli.fsproj                  # FR-022: FSharp.Core consistency
│   └── Program.fs                        # FR-018: remove scope arg from extract command
│
├── Frank.Cli.MSBuild/
│   ├── Frank.Cli.MSBuild.csproj          # FR-005: add to Frank.sln
│   └── build/
│       └── Frank.Cli.MSBuild.targets     # FR-019: check specific input artifacts
│
sample/
└── Frank.LinkedData.Sample/
    └── Frank.LinkedData.Sample.fsproj    # FR-006: NuGet 7.3.0 → ProjectReference

test/
├── Frank.LinkedData.Tests/               # Existing tests + new behavioral tests
└── Frank.Cli.Core.Tests/                 # Existing tests + new behavioral tests

Frank.sln                                 # FR-005: add Frank.Cli.MSBuild
```

**Structure Decision**: No new projects created. One new file (`UriHelpers.fs`) added to `Frank.Cli.Core/Extraction/`. FsToolkit.ErrorHandling added as dependency to `Frank.Cli.Core`. All changes modify existing files within the established structure.

## Complexity Tracking

No constitution violations requiring justification. All changes simplify and align with constitutional principles.
