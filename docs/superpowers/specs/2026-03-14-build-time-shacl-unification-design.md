# Design: Build-Time SHACL Shape Unification

**Date**: 2026-03-14
**Feature**: 005-shacl-validation-from-fsharp-types (amendment)
**Status**: Draft
**GitHub Issue**: #76

---

## Problem

Frank has two parallel SHACL shape derivation pipelines that do the same conceptual work through different mechanisms:

1. **Frank.Cli.Core** uses FSharp.Compiler.Service to analyze F# types at build time, producing `shapes.shacl.ttl` for documentation and ontology publishing. Emits basic structural SHACL only (sh:targetClass, sh:path, sh:datatype, sh:minCount).

2. **Frank.Validation** uses .NET reflection at application startup to derive validation-grade SHACL shapes (sh:pattern, sh:in, sh:or, sh:maxCount, sh:closed, cycle detection). These shapes drive runtime request validation via dotNetRdf.

This duplication means:
- Two codebases derive the same type metadata through different APIs
- The CLI's shapes are incomplete for validation; the runtime's shapes can't leverage FCS's richer type information
- Shape derivation runs at startup (non-deterministic timing) rather than at build time (deterministic, inspectable)

## Design

Unify shape derivation into a single FCS-based build-time pipeline. One analysis pass produces one Turtle artifact with full validation-grade constraints. All consumers (validation, documentation, ontology export) read from this artifact.

### Goals

1. **Eliminate duplication** -- one pipeline, one artifact, multiple readers
2. **Full-fidelity type information** -- FCS provides doc comments, source locations, type abbreviation transparency
3. **Compile-time determinism** -- shapes are fixed at build, not derived at startup
4. **Zero startup derivation cost** -- Frank.Validation loads pre-computed shapes from an embedded resource

### Architecture

```
Build time:
  F# source ──> FCS TypeAnalyzer ──> AnalyzedType[] ──> ShapeGenerator ──> shapes.shacl.ttl
                                                                              │
                                                    Frank.Cli.MSBuild target ──> EmbeddedResource

Runtime:
  Assembly.GetManifestResourceStream("Frank.Semantic.shapes.shacl.ttl")
    │
    v
  ShapeLoader (dotNetRdf Turtle parser ──> IGraph ──> ShaclShape[])
    │
    v
  ShapeCache (pre-populated at startup)
    │
    v
  ValidationMiddleware ──> ShapeResolver (capability filtering) ──> Validator
```

### Key decisions

**One artifact, multiple readers.** The enriched Turtle contains all validation constraints (sh:pattern, sh:in, sh:or, sh:closed, etc.). Documentation consumers read what they need and ignore the rest. SHACL is additive by design -- extra triples don't interfere.

**Unified URI scheme.** Both the CLI shape generator and Frank.Validation's data graph builder must use the same URI conventions. The existing CLI uses configurable `{baseUri}/properties/{typeName}/{fieldName}` URIs while Frank.Validation uses `urn:frank:property:{fieldName}` and `urn:frank:shape:{assembly}:{type}`. The enriched ShapeGenerator adopts the `urn:frank:*` scheme for `sh:path` and `sh:NodeShape` URIs in the shapes graph. The documentation/ontology graph retains the configurable `{baseUri}/` scheme for OWL classes and properties (these are separate graphs in the same extraction pipeline). This ensures the Turtle artifact's property paths match the data graphs that the validator constructs at runtime.

**Build-time generation is automatic and opt-in.** A new MSBuild target runs the FCS analysis during `dotnet build` -- but only for projects that explicitly reference `Frank.Cli.MSBuild`. Frank.Validation itself does not depend on FCS or the CLI tooling; it only requires the embedded resource to be present. Projects can produce the embedded resource by any means: the auto-invoke MSBuild target, manual `frank-cli extract`, or CI pipeline.

**MSBuild invokes the CLI tool via `<Exec>`.** The auto-invoke target shells out to `frank-cli extract --project $(MSBuildProjectFullPath)` as a dotnet tool. This avoids packaging FCS inside an MSBuild task DLL and reuses the existing extraction pipeline. The `extract` command already performs FCS analysis, type mapping, shape generation, and state persistence in a single pass. A new `--emit-artifacts` flag causes it to also write `shapes.shacl.ttl`, `ontology.owl.xml`, and `manifest.json` directly (unifying the current two-step `extract` then `compile` workflow). The target runs `AfterTargets="ResolveAssemblyReferences"` and `BeforeTargets="CoreCompile"` so all project references are resolved but compilation has not yet occurred.

**No runtime reflection fallback.** Build-time shapes are the single source of truth. If the embedded resource is missing, Frank.Validation fails fast with a clear error. Capability-dependent shape selection (WP05) selects from the pre-computed set at runtime -- no shapes are derived dynamically.

**`ShaclShape.TargetType` becomes optional.** The current `ShaclShape` record has `TargetType: Type` which cannot survive a Turtle round-trip. This field changes to `TargetType: Type option`. The ShapeLoader sets it to `None` for shapes loaded from Turtle. Code that previously keyed on `TargetType` (notably `ShapeCache`) switches to keying on `NodeShapeUri` instead. The `TargetType` field is retained (not removed) because capability-dependent shape construction (WP05) may still create shapes programmatically at startup where the type is known.

**URI helper functions migrate to a shared module.** `ShapeDerivation.buildPropertyPathUri` and `ShapeDerivation.buildNodeShapeUri` are currently called by `DataGraphBuilder.fs` and `ShapeGraphBuilder.fs`. When `ShapeDerivation.fs` is deleted, these functions move to a new `UriConventions.fs` module within Frank.Validation. This is a mechanical refactor -- the functions are pure and have no dependency on reflection.

**Custom constraints via F# attributes.** Custom constraints (WP06) are expressed as attributes on types/fields so the FCS analysis can extract them at build time. These attribute types are defined in a new `Frank.Validation.Annotations` module (or a lightweight assembly if needed to avoid circular dependencies). The ShapeMerger still runs at startup to validate constraint consistency (conflict detection), but its input comes from the embedded shapes + attribute-derived constraints rather than reflection-derived shapes + runtime configuration. Runtime API registration remains as a secondary mechanism for SPARQL cross-field constraints and other constraints that cannot be expressed as attributes.

## Impact on existing work packages

### Done WPs -- minor changes only

| WP | Title | Impact |
|----|-------|--------|
| WP01 | Core Types | **Minor.** `ShaclShape.TargetType` changes from `Type` to `Type option`. All pattern matches on `ShaclShape` that access `TargetType` need updating. |
| WP03 | Validator & Middleware | **Minor.** Middleware itself is unchanged -- it calls `ShapeCache`, not derivation directly. `ShapeCache.GetOrAdd` changes from `Type`-keyed to `Uri`-keyed (see WP12). |
| WP04 | Violation Reporting | None -- serializes validation results, not shapes. |
| WP05 | Capability-Dependent Shapes | **Minor.** ShapeResolver selects from pre-loaded shapes. Logic unchanged; `ShapeResolver.resolve` already operates on `ShaclShape` values, not `Type`. |

### Done WPs -- superseded

| WP | Title | Impact |
|----|-------|--------|
| WP02 | Type Mapping & Shape Derivation | **Superseded.** `ShapeDerivation.fs` and `TypeMapping.fs` are replaced by `ShapeLoader.fs`. The reflection-based derivation code is deleted. The type mapping logic moves into Frank.Cli.Core's enriched ShapeGenerator. |

### Planned WPs -- amended

| WP | Title | Amendment |
|----|-------|-----------|
| WP06 | Custom Constraints | Custom constraints expressed via F# attributes (extractable by FCS) instead of runtime-only configuration. ShapeMerger merges attribute-derived constraints with pre-loaded shapes at startup. |
| WP07 | Builder Extensions | `useValidation` initializes ShapeLoader (not ShapeDerivation). `validate` CE operation still adds ValidationMarker to metadata. Shape cache warm-up is deserialization, not derivation. |
| WP08 | Integration Tests | Scope expands: must verify FCS extraction -> Turtle -> embedded resource -> ShapeLoader -> validation. Test projects include Frank.Cli.MSBuild to exercise auto-generation. |

## New work packages

### WP09: Enrich TypeAnalyzer with Validation-Grade Metadata

**Dependencies**: None (Frank.Cli.Core is independent)

Extend `Frank.Cli.Core.Analysis.TypeAnalyzer` to capture the full metadata needed for validation-grade SHACL:

- **Primitives**: Add DateOnly, TimeOnly, TimeSpan, Uri, byte[] to `mapFieldType` (currently maps 8 types; validation needs 13+)
- **Guid**: Emit as its own FieldKind variant (not just `xsd:string`) so ShapeGenerator can attach `sh:pattern`
- **Collection cardinality**: Track whether a field is scalar vs. collection so ShapeGenerator can emit `sh:maxCount`
- **Closed shape flag**: Records should be marked as closed (ShapeGenerator emits `sh:closed true`)
- **Custom constraint attributes**: If a field or type carries validation attributes (e.g., `[<Pattern("...")>]`, `[<MinInclusive(0)>]`), extract them into `AnalyzedField`

The `FieldKind` ADT may need new variants or metadata to carry this information.

**Files**: `src/Frank.Cli.Core/Analysis/TypeAnalyzer.fs`
**Test**: Existing TypeAnalyzer tests plus new cases for the added metadata.

### WP10: Enrich ShapeGenerator with Full SHACL Constraints

**Dependencies**: WP09

Extend `Frank.Cli.Core.Extraction.ShapeGenerator` to emit validation-grade SHACL triples:

- `sh:maxCount 1` for scalar fields (absent for collections)
- `sh:pattern` for Guid fields (UUID RFC 4122 regex)
- `sh:in` for simple DUs (case names as string literals)
- `sh:or` with per-case `sh:NodeShape` for payload DUs
- `sh:closed true` for records
- `sh:nodeKind` where appropriate
- Cycle detection markers for recursive types (configurable depth, default 5)
- Custom constraint triples from attribute metadata (WP09)

Use `ShapeDerivation.fs` (WP02) as the reference implementation -- the constraint logic is the same, only the metadata source changes (FCS entities instead of System.Reflection).

**Files**: `src/Frank.Cli.Core/Extraction/ShapeGenerator.fs`
**Test**: Round-trip test: generate Turtle, parse with dotNetRdf, verify constraint triples match expectations.

### WP11: MSBuild Auto-Invoke Target

**Dependencies**: WP09, WP10

Add an MSBuild target that runs shape generation automatically during `dotnet build` for projects that reference Frank.Cli.MSBuild:

- New target `GenerateFrankSemanticDefinitions` in `Frank.Cli.MSBuild.targets`
- Runs `AfterTargets="ResolveAssemblyReferences"` and `BeforeTargets="CoreCompile"` -- all project references are resolved but compilation has not yet started
- Uses `<Exec Command="dotnet frank-cli extract --project $(MSBuildProjectFullPath) --emit-artifacts --output $(FrankCliOutputPath)" />` to shell out to the CLI tool
- The existing `EmbedFrankSemanticDefinitions` target picks up the output as today
- Incremental build support via MSBuild `Inputs="@(Compile)"` / `Outputs="$(FrankCliOutputPath)shapes.shacl.ttl"` -- skip regeneration if no source files changed
- `frank-cli extract --emit-artifacts` unifies the current two-step `extract` then `compile` workflow into a single command
- `frank-cli extract` (without `--emit-artifacts`) and `frank-cli compile` remain available for inspection/debugging/CI
- Requires `frank-cli` to be installed as a dotnet tool (local or global); the target emits a clear warning if the tool is not found

**Frank.Cli.Core targets net10.0 only.** Since the CLI runs as a standalone process via `<Exec>`, it does not need to match the MSBuild task host's runtime. The `<Exec>` invocation runs `dotnet frank-cli` which uses whatever .NET runtime is available.

**Files**: `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.targets`
**Test**: Build a sample project, verify embedded resource contains validation-grade shapes.

### WP12: ShapeLoader and Internal Refactoring in Frank.Validation

**Dependencies**: WP10 (enriched Turtle artifact must exist). WP11 is parallel, not a prerequisite -- WP12 can develop against a manually-generated Turtle file.

Replace reflection-based shape derivation with a loader that reads pre-computed shapes, and refactor internal coupling:

1. **Create `src/Frank.Validation/UriConventions.fs`** (new, early in compile order):
   - Move `buildPropertyPathUri` and `buildNodeShapeUri` from `ShapeDerivation.fs` into this module
   - These are pure functions with no reflection dependency
   - Update `DataGraphBuilder.fs` and `ShapeGraphBuilder.fs` to reference `UriConventions` instead of `ShapeDerivation`

2. **Create `src/Frank.Validation/ShapeLoader.fs`** (new):
   - Read `Frank.Semantic.shapes.shacl.ttl` from the application assembly's embedded resources via `Assembly.GetManifestResourceStream`
   - Parse Turtle via dotNetRdf into an `IGraph`
   - Deserialize the graph into `ShaclShape` domain values (reverse of ShapeGraphBuilder): walk `sh:NodeShape` subjects, extract `sh:property` blank nodes, read `sh:path`, `sh:datatype`, `sh:minCount`, `sh:maxCount`, `sh:pattern`, `sh:in`, `sh:or`, `sh:closed` triples
   - Set `TargetType = None` on all loaded shapes (type is not available from Turtle)
   - Populate `ShapeCache` with the loaded shapes
   - Fail fast with `InvalidOperationException` if the resource is missing

3. **Update `ShaclShape.TargetType`** from `Type` to `Type option` in `Types.fs` (WP01 amendment):
   - Update all pattern matches that access `TargetType` across Frank.Validation
   - `ShapeCache.GetOrAdd` switches from `ConcurrentDictionary<Type, ...>` to `ConcurrentDictionary<Uri, ...>`, keyed on `NodeShapeUri`
   - `ShapeCache` becomes pre-populated at startup via `ShapeLoader.loadAll` rather than lazy `GetOrAdd` with derivation

4. **Delete `ShapeDerivation.fs`** from Frank.Validation. **Trim `TypeMapping.fs`** to retain only `xsdUri : XsdDatatype -> Uri` (the pure mapping function used by `DataGraphBuilder.fs` for constructing data graph literals). Delete `mapType : Type -> XsdDatatype option` (the reflection-based function superseded by Frank.Cli.Core's FCS analysis).

5. **Update `ValidationMarker.ShapeType`** from `Type` to `Uri` (the NodeShape URI). The middleware's primary lookup path (`shapeCache.GetOrAdd(marker.ShapeType)`) becomes URI-keyed, consistent with the new `ShapeCache`. The `validate` CE operation (WP07) populates `ValidationMarker.ShapeType` with the NodeShape URI from the loaded shapes rather than a `System.Type`.

5. **Keep `ShapeGraphBuilder.fs`** -- needed for capability-dependent shape variant construction (WP05's `ShapeResolver` may produce modified shapes that need to be compiled into `ShapesGraph` instances)

**Files**:
- `src/Frank.Validation/UriConventions.fs` (new)
- `src/Frank.Validation/ShapeLoader.fs` (new)
- `src/Frank.Validation/Types.fs` (amend `TargetType`)
- `src/Frank.Validation/ShapeCache.fs` (refactor keying)
- `src/Frank.Validation/ShapeDerivation.fs` (delete)
- `src/Frank.Validation/DataGraphBuilder.fs` (update imports)
- `src/Frank.Validation/ShapeGraphBuilder.fs` (update imports)

**Test**: Load a known Turtle file, verify ShaclShape domain values match expected structure. Verify DataGraphBuilder and ShapeGraphBuilder still work with the refactored imports.

## Amended WP details

### WP06 (amended): Custom Constraints via Attributes

Original design: custom constraints are registered via runtime API calls and merged at startup.

**Amendment**: Custom constraints are also expressible as F# attributes on types/fields:

```fsharp
type CreateCustomer = {
    [<Pattern("^[^@]+@[^@]+$")>]
    Email: string
    [<MinInclusive(0)>]
    Age: int
}
```

The FCS analysis (WP09) extracts these attributes and the ShapeGenerator (WP10) emits the corresponding SHACL triples. ShapeMerger still validates constraints at startup (conflict detection) but receives them from the pre-loaded shapes rather than runtime registration.

Runtime API registration (for constraints that can't be expressed as attributes, e.g., SPARQL cross-field constraints) remains as a secondary mechanism -- ShapeMerger applies these on top of the pre-loaded shapes.

### WP07 (amended): Builder Extensions

- `useValidation` initializes `ShapeLoader` instead of shape derivation
- Shape cache warm-up calls `ShapeLoader.loadFromAssembly` instead of reflecting over types
- `ValidationOptions.MaxDerivationDepth` is no longer relevant at runtime (depth is controlled at build time); rename to `MaxShapeDepth` and pass it to the CLI configuration
- Startup warnings for framework types (FR-017) move to build-time diagnostics (emitted by the MSBuild target)

### WP08 (amended): Integration Tests

Additional test coverage:
- Build-time pipeline: verify a sample project's embedded resource contains expected shapes
- ShapeLoader: verify deserialization from Turtle to ShaclShape domain values
- End-to-end: build sample project with Frank.Cli.MSBuild, run it, send requests, verify validation behavior
- Missing resource: verify Frank.Validation fails fast with clear error when embedded resource is absent

## Dependency graph (updated)

```
Existing (done):
  WP01 ──> WP02 (superseded by WP09+WP10+WP12)
  WP01 ──> WP03
  WP03 ──> WP04
  WP01 ──> WP05

New:
  WP09: Enrich TypeAnalyzer
    │
    v
  WP10: Enrich ShapeGenerator
    │
    v
  WP11: MSBuild Auto-Invoke ──┐
                              │ (parallel)
  WP10 ──────────────────────>WP12: ShapeLoader
                                  │
                                  v
                      WP06 (amended): Custom Constraints
                                  │
                                  v
                      WP07 (amended): Builder Extensions
                                  │
                                  v
                      WP08 (amended): Integration Tests
```

**Critical path**: WP09 -> WP10 -> WP12 -> WP07 -> WP08

WP11 (MSBuild auto-invoke) and WP12 (ShapeLoader) can be developed in parallel after WP10 -- WP12's core work (UriConventions, ShapeLoader, domain type changes, ShapeCache refactor) only needs a valid Turtle file, not the MSBuild target. WP11 and WP12 must both complete before WP07.

WP06 can be developed in parallel with WP12 (both depend on WP10 outputs).

## Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| FCS analysis adds build time (~5-15s for moderate projects) | High | Incremental build support (MSBuild Inputs/Outputs) is critical-path -- shapes only regenerated when source files change. First build is slower; subsequent builds skip generation. |
| URI scheme mismatch between CLI shapes and validation data graphs | High | Enriched ShapeGenerator adopts `urn:frank:property:{fieldName}` for `sh:path` and `urn:frank:shape:{assembly}:{type}` for NodeShape URIs. Round-trip integration test validates that loaded shapes produce correct validation results. |
| Turtle round-trip loses fidelity (blank node handling, RDF list structure, datatype canonicalization) | Medium | Round-trip tests verify every constraint type survives serialization/deserialization. dotNetRdf's Turtle writer/parser is well-tested for SHACL patterns. |
| `ShaclShape.TargetType` change cascades to ShapeResolver, DataGraphBuilder, ReportSerializer | Medium | The change is `Type` to `Type option`, which the compiler will flag at every usage site. Most consumers only use `NodeShapeUri` and `Properties`. |
| FCS version coupling with .NET SDK | Medium | Pin FCS version in Frank.Cli.Core; test against all target frameworks. CLI runs as separate process so SDK mismatch does not affect the host project's compilation. |
| ShapeLoader deserialization complexity | Medium | Use ShapeGraphBuilder's logic in reverse; comprehensive test coverage for each constraint type. |
| `frank-cli` tool not installed when MSBuild target runs | Low | Target emits a clear warning (not error) if tool is not found. Existing `ValidateFrankSemanticDefinitions` target already warns when artifacts are missing. |
| Custom constraint attributes may not cover all cases | Low | Keep runtime API as secondary mechanism for SPARQL and complex constraints. |
| Frank.Cli.MSBuild auto-invoke is implicit for referencing projects | Low | Auto-invoke only runs for projects that explicitly add a `<PackageReference>` to Frank.Cli.MSBuild. Frank.Validation works without it -- just needs the embedded resource by any means. |

## Constitution compliance

This amendment strengthens compliance:

- **III. Library, Not Framework**: FCS dependency is in the CLI tooling (Frank.Cli.Core), not in the runtime library (Frank.Validation). The validation library remains lightweight. The MSBuild auto-invoke is opt-in (requires explicit Frank.Cli.MSBuild reference).
- **V. Performance Parity**: Startup cost changes from reflection-based derivation to Turtle deserialization. Both are one-time startup costs; deserialization of a pre-computed artifact should be comparable or faster. Benchmark to confirm.
- **VIII. No Duplicated Logic**: Eliminates the primary source of duplication (two shape derivation pipelines).

## Amendment notes

This design amends the following statements in the original spec and plan:

- **spec.md Assumptions**: "Shape derivation uses .NET reflection on handler parameter types at application startup. No FSharp.Compiler.Service dependency is required" -- replaced by build-time FCS analysis via CLI tooling.
- **plan.md Technical Context**: "Shape derivation via .NET reflection only (no FSharp.Compiler.Service)" -- superseded.
- **research.md Decision 1**: ".NET Reflection at Startup" -- the rationale (FCS too heavy for runtime) remains valid; the amendment moves FCS to build-time tooling where the weight is acceptable.

The original spec.md and plan.md should be annotated with a reference to this design doc.
