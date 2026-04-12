---
source: kitty-specs/001-semantic-resources-phase1
type: plan
---

# Implementation Plan: Semantic Resources Phase 1

**Branch**: `001-semantic-resources-phase1` | **Date**: 2026-03-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/kitty-specs/001-semantic-resources-phase1/spec.md`
**GitHub Issues**: #80 (tracking), #79 (frank-cli), #75 (Frank.LinkedData)

## Summary

Build the foundation layer for Frank's semantic web capabilities: a CLI tool (`frank-cli`) that extracts OWL ontology and SHACL shapes from F# source definitions, and an extension library (`Frank.LinkedData`) that serves semantic resource representations via content negotiation. Both share `dotNetRdf.Core` as the RDF runtime. The CLI uses FSharp.Compiler.Service for both untyped AST walking (route/handler detection) and typed AST analysis (type structure extraction), producing embedded assembly resources consumed by Frank.LinkedData at runtime. No compiled assembly is required for extraction.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0, 9.0, 10.0 (multi-targeting, matching Frank core)
**Primary Dependencies**:
- `dotNetRdf.Core` 3.5.1 — RDF triple model, serializers (JSON-LD, Turtle, RDF/XML)
- `dotNetRdf.Ontology` 3.5.1 — OWL abstractions (OntologyGraph, OntologyClass)
- `dotNetRdf.Shacl` 3.5.1 — SHACL shape generation/validation
- `FSharp.Compiler.Service` 43.10.103 — F# AST parsing and type extraction
- `Ionide.ProjInfo` 0.74.1 + `Ionide.ProjInfo.FCS` — .fsproj cracking and project loading
- `System.CommandLine` — CLI argument parsing for frank-cli
**Extraction Approach**: FCS AST-only (no compiled assembly required). Untyped AST for route/handler detection, typed AST for type analysis.
**Storage**: Intermediate extraction state in `obj/frank-cli/` (file-based); final artifacts as embedded assembly resources
**Testing**: Expecto 10.x + YoloDev.Expecto.TestSdk 0.14.x + Microsoft.NET.Test.Sdk 17.x (matching existing Frank test conventions)
**Target Platform**: .NET 8.0/9.0/10.0 (cross-platform CLI tool + library)
**Project Type**: Multi-project library + CLI tool
**Performance Goals**: Extraction should complete in under 30 seconds for a typical Frank project (<50 source files). Content negotiation serialization should add <10ms overhead per request.
**Constraints**: dotNetRdf.Core is the only non-framework external dependency for Frank.LinkedData. frank-cli may have heavier dependencies (FCS, Ionide.ProjInfo) since it's a developer tool, not a runtime dependency.
**Scale/Scope**: Phase 1 of 4. Scoped to single-project extraction (no cross-assembly). 5 new projects, ~30 source files estimated.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. Resource-Oriented Design — PASS
Frank.LinkedData enriches resources with semantic representations via content negotiation. The `linkedData` CE operation extends the existing `resource` abstraction. Link relations and content negotiation are first-class.

### II. Idiomatic F# — PASS
- `linkedData` is a computation expression custom operation
- OWL mappings use F# discriminated unions and records naturally
- Pipeline-friendly: `resource "/x" { linkedData; get handler }`

### III. Library, Not Framework — PASS
Frank.LinkedData is opt-in per resource. frank-cli is a standalone tool. Neither imposes structure. Easy to adopt incrementally.

### IV. ASP.NET Core Native — PASS
Frank.LinkedData extends content negotiation via ASP.NET Core's formatter pipeline. Uses `HttpContext` directly. No platform abstractions hidden.

### V. Performance Parity — PASS (conditional)
LinkedData serialization only runs for opted-in resources and only when semantic media types are requested. No impact on non-LinkedData resources. Performance target: <10ms serialization overhead.

### Dependencies — JUSTIFIED VIOLATION
Constitution says "Minimize external dependencies." This plan adds `dotNetRdf.Core` (with transitive deps: AngleSharp, HtmlAgilityPack, Newtonsoft.Json, VDS.Common) to Frank.LinkedData.

**Justification**: Writing correct RDF serializers for 3 formats (JSON-LD, Turtle, RDF/XML) is a large, error-prone effort that would result in more code to maintain than the dependency itself. Phase 4 SPARQL requires dotNetRDF anyway. The dependency is only pulled in by projects that opt into Frank.LinkedData — it does not affect the core Frank package.

## Project Structure

### Source Code (repository root)

```
src/
├── Frank/                          # Existing — no changes
├── Frank.Auth/                     # Existing — no changes
├── Frank.Analyzers/                # Existing — no changes (analyzer update dropped)
├── Frank.Datastar/                 # Existing — no changes
├── Frank.OpenApi/                  # Existing — no changes
├── Frank.Cli.Core/                 # NEW — extraction library
│   ├── Frank.Cli.Core.fsproj
│   ├── Rdf/
│   │   └── FSharpRdf.fs
│   ├── Extraction/
│   │   ├── TypeMapper.fs
│   │   ├── RouteMapper.fs
│   │   ├── CapabilityMapper.fs
│   │   ├── ShapeGenerator.fs
│   │   └── VocabularyAligner.fs
│   ├── Analysis/
│   │   ├── AstAnalyzer.fs
│   │   ├── TypeAnalyzer.fs
│   │   └── ProjectLoader.fs
│   ├── State/
│   │   ├── ExtractionState.fs
│   │   └── DiffEngine.fs
│   └── Commands/
│       ├── ExtractCommand.fs
│       ├── ClarifyCommand.fs
│       ├── ValidateCommand.fs
│       ├── DiffCommand.fs
│       └── CompileCommand.fs
├── Frank.Cli/                      # NEW — dotnet tool entry point
│   ├── Frank.Cli.fsproj
│   └── Program.fs
├── Frank.Cli.MSBuild/              # NEW — MSBuild integration package
│   ├── Frank.Cli.MSBuild.csproj
│   ├── build/
│   │   ├── Frank.Cli.MSBuild.props
│   │   └── Frank.Cli.MSBuild.targets
│   └── buildTransitive/
│       └── Frank.Cli.MSBuild.targets
└── Frank.LinkedData/               # NEW — content negotiation extension
    ├── Frank.LinkedData.fsproj
    ├── Rdf/
    │   ├── GraphLoader.fs
    │   └── InstanceProjector.fs
    ├── Negotiation/
    │   ├── JsonLdFormatter.fs
    │   ├── TurtleFormatter.fs
    │   └── RdfXmlFormatter.fs
    ├── LinkedDataConfig.fs
    ├── ResourceBuilderExtensions.fs
    └── WebHostBuilderExtensions.fs

test/
├── Frank.Cli.Core.Tests/
└── Frank.LinkedData.Tests/

sample/
└── Frank.LinkedData.Sample/
```

**Structure Decision**: Multi-project layout following existing Frank conventions.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| dotNetRdf.Core dependency | RDF serialization (3 formats) + shared triple model + SPARQL path | Writing custom serializers is more code, more bugs, and requires migration in Phase 4 |
| 5 new projects | Separation of concerns: tool vs runtime, core logic vs entry point, build integration vs CLI | Fewer projects would conflate tool dependencies (FCS) with runtime dependencies |
| FSharp.Compiler.Service dependency in Cli.Core | AST-level type extraction impossible via reflection alone | Regex/text parsing is fragile; reflection-only misses type structure details |

## Post-Design Constitution Re-check

### Dependencies re-check — JUSTIFIED
- Frank.LinkedData depends on: `dotNetRdf.Core` (justified above)
- Frank.Cli.Core depends on: `FSharp.Compiler.Service`, `Ionide.ProjInfo`, `dotNetRdf.Core`, `dotNetRdf.Ontology`, `dotNetRdf.Shacl` — developer tool dependencies, not runtime
- Core Frank package: unchanged, no new dependencies

### All other principles — PASS (unchanged from initial check)
