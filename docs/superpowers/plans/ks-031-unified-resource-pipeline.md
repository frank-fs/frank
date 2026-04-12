---
source: kitty-specs/031-unified-resource-pipeline
type: plan
---

# Implementation Plan: Unified Resource Pipeline

**Branch**: `031-unified-resource-pipeline` | **Date**: 2026-03-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `kitty-specs/031-unified-resource-pipeline/spec.md`

## Summary

Merge the semantic (OWL/SHACL) and statechart (state machine) CLI pipelines into a single unified extraction that walks an F# project once via FCS and produces a combined resource description carrying type structure, behavioral semantics, and HTTP capabilities per resource. From this unified model: generate ALPS profiles with both type and behavioral descriptors, validate consistency with OpenAPI schemas, produce a binary affordance map embedded in the assembly, and serve runtime affordance headers (Link + Allow) and content-negotiated profiles (ALPS, OWL, SHACL) via middleware — all projected at startup from a single embedded binary artifact. A Datastar helper enables state-aware fragment projection from the same affordance data.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting for library projects, net10.0 for CLI/tests)
**Primary Dependencies**: FSharp.Compiler.Service (FCS), dotNetRdf.Core, System.Text.Json, MessagePack + MessagePack.FSharpExtensions (binary serialization), FSharp.Data.JsonSchema.OpenApi (canonical F# type-to-schema mapping), Microsoft.AspNetCore.App (framework reference)
**Storage**: Binary embedded resource in assembly (MessagePack + MessagePack.FSharpExtensions), `obj/frank-cli/unified-state.bin` cache file
**Testing**: Expecto (unit), Microsoft.AspNetCore.TestHost (integration), existing 1726-test baseline
**Target Platform**: .NET 8.0+ server (ASP.NET Core)
**Project Type**: Multi-project F# library + CLI tool
**Performance Goals**: Extraction <15s for 20 resources; cached commands <1s; affordance middleware zero allocation per request beyond Link header string; startup projection <500ms
**Constraints**: Single FCS typecheck per extraction (no redundant walks); single embedded binary artifact (no static files in deployment); backward compat with semantic subcommands via projector
**Scale/Scope**: Validated against tic-tac-toe sample; designed for up to 50+ resources

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | **PASS** | Unified model is resource-centric (keyed by route template). ALPS profiles carry resource vocabulary + affordances. Link headers enable HATEOAS. |
| II. Idiomatic F# | **PASS** | Unified extractor uses FCS typed AST walking, F# records for models, pure projection functions, pipeline-friendly design. |
| III. Library, Not Framework | **PASS** | `Frank.Affordances` is opt-in middleware. Datastar helper is a standalone function. No forced adoption. |
| IV. ASP.NET Core Native | **PASS** | `useAffordances` CE custom operation follows `useOpenApi` pattern. Embedded resources via standard `GetManifestResourceStream`. Content negotiation via standard Accept header handling. |
| V. Performance Parity | **PASS** | Binary serialization for startup, pre-computed dictionary lookup at request time, zero allocation on hot path per @7sharp9 guidance. Needs benchmarking during implementation. |
| VI. Resource Disposal | **WATCH** | FCS `FSharpChecker` and dotNetRdf `IGraph` instances need proper `use` bindings in the unified extractor. Verify during implementation. |
| VII. No Silent Exception Swallowing | **WATCH** | Unified extractor must not swallow FCS errors. Existing `with :? InvalidOperationException -> []` pattern from spec 026 is acceptable for unresolvable external entities only. |
| VIII. No Duplicated Logic | **PASS** | Unified walk eliminates the current duplication between semantic and statechart extractors. Projection functions reuse existing modules (TypeMapper, ShapeGenerator, etc.). |

## Project Structure

### Documentation (this feature)

```
kitty-specs/031-unified-resource-pipeline/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (NOT created by /spec-kitty.plan)
```

### Source Code (repository root)

```
src/
├── Frank.Cli.Core/
│   ├── Unified/                    # NEW: Unified extraction pipeline
│   │   ├── UnifiedExtractor.fs     # Single FCS walk → UnifiedResource list
│   │   ├── UnifiedModel.fs         # UnifiedResource, UnifiedExtractionState types
│   │   ├── UnifiedCache.fs         # Binary cache read/write (obj/frank-cli/)
│   │   └── ExtractionStateProjector.fs  # toExtractionState for backward compat
│   ├── Statechart/                 # MODIFIED: reads from unified state
│   ├── Commands/                   # MODIFIED: unified extract/generate/validate
│   └── Output/                     # MODIFIED: affordance map generation
│
├── Frank.Affordances/              # NEW: Runtime affordance middleware
│   ├── Frank.Affordances.fsproj    # References Microsoft.AspNetCore.App only
│   ├── AffordanceMap.fs            # Map types + binary deserialization
│   ├── AffordanceMiddleware.fs     # ASP.NET Core middleware class (InvokeAsync)
│   ├── WebHostBuilderExtensions.fs # useAffordances CE custom operation (same pattern as Frank.OpenApi)
│   ├── ProfileMiddleware.fs        # Content-negotiated ALPS/OWL/SHACL serving
│   └── StartupProjection.fs        # Binary → in-memory projections at startup
│
├── Frank.Affordances.MSBuild/      # NEW: MSBuild target for embedding
│   └── Frank.Affordances.targets   # Auto-embed unified-state.bin as EmbeddedResource
│
├── Frank.Datastar/                 # MODIFIED: add affordancesFor helper
│   └── AffordanceHelper.fs         # NEW: affordancesFor(route, state, map)
│
├── Frank.Statecharts/              # UNCHANGED (parsers, generators, AST)
├── Frank.OpenApi/                  # UNCHANGED (runtime OpenAPI — compared against)
└── Frank.Cli/                      # MODIFIED: unified extract command wiring

test/
├── Frank.Affordances.Tests/        # NEW: middleware integration tests
├── Frank.Cli.Core.Tests/           # MODIFIED: unified extraction tests
└── Frank.Statecharts.Tests/        # UNCHANGED (baseline protection)

sample/                             # Tic-tac-toe validation target
└── Frank.TicTacToe.Sample/         # NEW or modified from existing test fixtures
```

**Structure Decision**: Three new projects (`Frank.Affordances`, `Frank.Affordances.MSBuild`, test project). `Frank.Affordances` has no dependency on `Frank.Statecharts` — decoupled via the binary affordance map format. Datastar helper added to existing `Frank.Datastar`. Unified extraction logic in `Frank.Cli.Core/Unified/`.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| New `Frank.Affordances` project | Runtime middleware is a separate concern from CLI extraction and from statecharts | Adding to `Frank.Statecharts` would couple non-statechart resources to statechart types |
| New `Frank.Affordances.MSBuild` project | MSBuild targets require a separate package for NuGet distribution | Embedding the target in `Frank.Affordances` fsproj limits distribution |
| Binary serialization dependency | MemoryPack/MessagePack for zero-parse startup | JSON parsing at startup is 10-100x slower and allocates heavily |
