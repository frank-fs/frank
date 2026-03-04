# Research: Semantic Resources Phase 1

**Date**: 2026-03-04
**Feature**: 001-semantic-resources-phase1

## Decision 1: RDF Library

**Decision**: Adopt `dotNetRdf.Core` 3.5.1 from Phase 1.

**Rationale**: Provides battle-tested triple model (`IGraph`, `Triple`, `INode`), serializers for all three target formats (JSON-LD via `JsonLdWriter`, Turtle via `CompressingTurtleWriter`, RDF/XML via `PrettyRdfXmlWriter`), and `EmbeddedResourceLoader` for loading from assembly resources. The `dotNetRdf.Ontology` package adds OWL-level abstractions (`OntologyGraph`, `OntologyClass`, `OntologyProperty`). The `dotNetRdf.Shacl` package provides SHACL validation. Clean upgrade path to `dotNetRdf.Query` for SPARQL in Phase 4.

**Alternatives considered**:
- Custom minimal triple model: Would require writing 3 RDF serializers from scratch (non-trivial, especially Turtle and JSON-LD). Would need migration to dotNetRDF in Phase 4 for SPARQL.
- Strings-only triples: Insufficient — can't represent literal datatypes, blank nodes, language tags.

**Constitution tension**: The constitution says "Minimize external dependencies; prefer ASP.NET Core built-ins." dotNetRdf.Core pulls in AngleSharp, HtmlAgilityPack, Newtonsoft.Json, and VDS.Common as transitive dependencies. This is justified because: (a) writing correct RDF serializers is a large, error-prone effort; (b) the alternative is a larger codebase with more maintenance burden; (c) Phase 4 SPARQL would require this dependency anyway.

**Key packages**:
- `dotNetRdf.Core` 3.5.1 — triple model, parsers, writers (.NET Standard 2.0)
- `dotNetRdf.Ontology` 3.5.1 — OWL abstractions (`OntologyGraph`, `OntologyClass`)
- `dotNetRdf.Shacl` 3.5.1 — SHACL shape validation

**F# interop notes**: The library is C#-oriented. Recommended thin F# wrapper for:
- Null-to-Option conversion (`Option.ofObj` for node lookups)
- DU wrapper for `INode` types (dispatch on `NodeType` enum)
- Mutable `IGraph` API — wrap in functional create-and-freeze pattern

## Decision 2: F# Source Analysis

**Decision**: Use FSharp.Compiler.Service (via Ionide.ProjInfo) for all source analysis — untyped AST for route/handler detection, typed AST for type structure extraction. No compiled assembly required. (Updated 2026-03-04: reflection-based analysis dropped to eliminate two-pass build workflow.)

**Rationale**: FCS provides both untyped AST (fast, for route template string extraction) and typed AST (for DU/record/field enumeration). Ionide.ProjInfo handles `.fsproj` cracking and reference resolution. The existing `DuplicateHandlerAnalyzer.fs` already demonstrates the exact AST walking pattern needed for detecting `resource "..."` CE invocations.

**Alternatives considered**:
- Packaging as a `[<CliAnalyzer>]` loaded by `fsharp-analyzers`: Cleanest project loading (free via SDK) but constrains output to diagnostics format. The CLI needs structured JSON output for 5 different commands — too constraining.
- Reflection-only: Misses type structure details (DU case names, record field optionality, route template strings).
- Source text regex: Fragile, misses nested types, can't resolve type references.

**Key packages**:
- `FSharp.Compiler.Service` 43.10.103 (transitively via Ionide.ProjInfo.FCS)
- `Ionide.ProjInfo` 0.74.1 — MSBuild project cracking
- `Ionide.ProjInfo.FCS` — bridge to FSharpProjectOptions

**AST extraction patterns** (proven in existing Frank.Analyzers):
- Route templates: `SynExpr.App(funcExpr=SynExpr.App(funcExpr=Ident "resource", argExpr=Const(String route)))` → `SynExpr.ComputationExpr`
- HTTP methods: `SynExpr.App(funcExpr=Ident "get"|"post"|..., argExpr=handler)` inside CE body
- Type extraction: `FSharpEntity.IsFSharpUnion`, `.IsFSharpRecord`, `.FSharpFields`, `.UnionCases`

## Decision 3: MSBuild Integration

**Decision**: Separate `Frank.Cli.MSBuild` NuGet package with `.props`/`.targets` in `build/` and `buildTransitive/` folders.

**Rationale**: Tool package (`frank-cli`) and build integration package are separate concerns. The MSBuild package auto-embeds compiled artifacts from `obj/frank-cli/` as `EmbeddedResource` items. Using `buildTransitive/` ensures the targets flow through indirect dependencies.

**Alternatives considered**:
- Bundling targets in the tool package: Conflates tool installation with build integration. Users of non-MSBuild systems (e.g., FAKE, Nuke) would get unwanted build targets.
- Modifying `.fsproj` directly: Invasive, fragile, poor UX.

**Package structure**:
```
Frank.Cli.MSBuild/
  build/
    Frank.Cli.MSBuild.props    # defaults (IntermediateOutputPath for frank-cli)
    Frank.Cli.MSBuild.targets  # EmbeddedResource inclusion from obj/frank-cli/
  buildTransitive/
    Frank.Cli.MSBuild.targets  # same targets for transitive consumers
```

**MSBuild target pattern**:
```xml
<Target Name="EmbedFrankSemanticDefinitions" BeforeTargets="CoreCompile"
        Condition="Exists('$(IntermediateOutputPath)frank-cli/')">
  <ItemGroup>
    <EmbeddedResource Include="$(IntermediateOutputPath)frank-cli/**/*.owl.xml"
                      LogicalName="Frank.Semantic.%(Filename)%(Extension)" />
    <EmbeddedResource Include="$(IntermediateOutputPath)frank-cli/**/*.shacl.ttl"
                      LogicalName="Frank.Semantic.%(Filename)%(Extension)" />
  </ItemGroup>
</Target>
```

## Decision 4: CLI Architecture

**Decision**: Core extraction library (`Frank.Cli.Core`) + thin console entry point (`frank-cli`).

**Rationale**: Isolates FCS dependency, makes extraction logic independently testable, allows potential reuse from other tools (e.g., an analyzer that also needs type info).

**Key insight from research**: `Frank.Cli.Core` should NOT depend on FSharp.Analyzers.SDK despite the similar AST walking patterns. The SDK is designed for analyzer plugins, not general-purpose libraries. Instead, depend directly on FCS + Ionide.ProjInfo.

## Decision 5: Default Vocabularies

**Decision**: Schema.org + Hydra as defaults via `--vocabularies` parameter.

**Rationale**: Schema.org provides broad concept coverage (Actions for HTTP capabilities). Hydra adds hypermedia-specific semantics (operations, parameters, link relations). Both are stable vocabularies despite Hydra's stalled W3C process.

**Implementation note**: Vocabulary alignment should be a post-extraction enrichment step — first extract the raw F#-to-OWL mapping, then align to standard vocabularies. This keeps the core extraction deterministic.
