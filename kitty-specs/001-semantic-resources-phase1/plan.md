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

### Documentation (this feature)

```
kitty-specs/001-semantic-resources-phase1/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # CLI command JSON schemas
└── tasks.md             # Phase 2 output (NOT created by /spec-kitty.plan)
```

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
│   ├── Rdf/                        # F# wrappers around dotNetRdf
│   │   └── FSharpRdf.fs            # Option conversions, DU node wrappers
│   ├── Extraction/
│   │   ├── TypeMapper.fs           # F# types → OWL classes/properties
│   │   ├── RouteMapper.fs          # Frank routes → RDF resource identities
│   │   ├── CapabilityMapper.fs     # HTTP methods → Schema.org Actions + Hydra
│   │   ├── ShapeGenerator.fs       # F# constraints → SHACL shapes
│   │   └── VocabularyAligner.fs    # Align to standard vocabularies
│   ├── Analysis/
│   │   ├── AstAnalyzer.fs          # FCS untyped AST walking (routes, CE structure, HTTP methods)
│   │   ├── TypeAnalyzer.fs         # FCS typed AST (DUs, records, fields)
│   │   └── ProjectLoader.fs        # Ionide.ProjInfo project loading
│   ├── State/
│   │   ├── ExtractionState.fs      # Persist/load from obj/frank-cli/
│   │   └── DiffEngine.fs           # Compare extraction states
│   └── Commands/
│       ├── ExtractCommand.fs       # extract orchestration
│       ├── ClarifyCommand.fs       # ambiguity detection
│       ├── ValidateCommand.fs      # completeness/consistency checks
│       ├── DiffCommand.fs          # structured diff
│       └── CompileCommand.fs       # generate final artifacts
├── Frank.Cli/                      # NEW — dotnet tool entry point
│   ├── Frank.Cli.fsproj            # PackAsTool=true
│   └── Program.fs                  # System.CommandLine dispatch
├── Frank.Cli.MSBuild/              # NEW — MSBuild integration package
│   ├── Frank.Cli.MSBuild.csproj    # Pack targets only, no code
│   ├── build/
│   │   ├── Frank.Cli.MSBuild.props
│   │   └── Frank.Cli.MSBuild.targets
│   └── buildTransitive/
│       └── Frank.Cli.MSBuild.targets
└── Frank.LinkedData/               # NEW — content negotiation extension
    ├── Frank.LinkedData.fsproj
    ├── Rdf/
    │   ├── GraphLoader.fs          # Load ontology/shapes from embedded resources
    │   └── InstanceProjector.fs    # Reflect handler return → RDF triples
    ├── Negotiation/
    │   ├── JsonLdFormatter.fs      # application/ld+json output formatter
    │   ├── TurtleFormatter.fs      # text/turtle output formatter
    │   └── RdfXmlFormatter.fs      # application/rdf+xml output formatter
    ├── LinkedDataConfig.fs         # Runtime config loaded at startup
    ├── ResourceBuilderExtensions.fs # linkedData CE operation
    └── WebHostBuilderExtensions.fs  # useLinkedData service registration

test/
├── Frank.Cli.Core.Tests/           # NEW — unit tests for extraction logic
│   ├── Frank.Cli.Core.Tests.fsproj
│   ├── TypeMapperTests.fs
│   ├── RouteMapperTests.fs
│   ├── CapabilityMapperTests.fs
│   ├── ShapeGeneratorTests.fs
│   ├── AstAnalyzerTests.fs
│   └── Program.fs
└── Frank.LinkedData.Tests/         # NEW — unit tests for content negotiation
    ├── Frank.LinkedData.Tests.fsproj
    ├── InstanceProjectorTests.fs
    ├── FormatterTests.fs
    ├── ContentNegotiationTests.fs
    └── Program.fs

sample/
└── Frank.LinkedData.Sample/        # NEW — end-to-end sample + integration tests
    ├── Frank.LinkedData.Sample.fsproj
    └── Program.fs
```

**Structure Decision**: Multi-project layout following existing Frank conventions. Each new library gets its own `src/` project and `test/` project. The CLI tool is split into core library (testable) + thin entry point (packaged as dotnet tool). MSBuild integration is a separate package with no code, only build targets.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| dotNetRdf.Core dependency | RDF serialization (3 formats) + shared triple model + SPARQL path | Writing custom serializers is more code, more bugs, and requires migration in Phase 4 |
| 5 new projects (Cli.Core, Cli, Cli.MSBuild, LinkedData, LinkedData sample) | Separation of concerns: tool vs runtime, core logic vs entry point, build integration vs CLI | Fewer projects would conflate tool dependencies (FCS) with runtime dependencies, or bundle MSBuild targets into the tool package |
| FSharp.Compiler.Service dependency in Cli.Core | AST-level type extraction (DUs, records, route templates) impossible via reflection alone | Regex/text parsing is fragile; reflection-only misses type structure details |

## Post-Design Constitution Re-check

### Dependencies re-check — JUSTIFIED
- Frank.LinkedData depends on: `dotNetRdf.Core` (justified above)
- Frank.Cli.Core depends on: `FSharp.Compiler.Service`, `Ionide.ProjInfo`, `dotNetRdf.Core`, `dotNetRdf.Ontology`, `dotNetRdf.Shacl` — these are developer tool dependencies, not runtime. Acceptable per constitution since frank-cli is not a runtime dependency of Frank applications.
- Core Frank package: unchanged, no new dependencies

### All other principles — PASS (unchanged from initial check)
