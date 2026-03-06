# Research: Phase 1.1 Code Review Fixes

## R1: FsToolkit.ErrorHandling Version Selection

**Decision**: Add FsToolkit.ErrorHandling as a dependency to Frank.Cli.Core
**Rationale**: Provides `result {}`, `option {}`, `asyncResult {}` CEs without custom implementation. Widely adopted in F# ecosystem. Enables natural composition for replacing nested match pyramids (FR-015).
**Alternatives considered**:
- Custom minimal CEs: More code to maintain, less tested
- `Result.bind` pipelines only: Acceptable but less readable for multi-step chains
- Both CEs and piped module functions are acceptable per user clarification

**Action**: Pin to latest stable version at implementation time.

## R2: XSD Type Mapping — Int64

**Decision**: Map `Int64` to `xsd:long` consistently across `TypeAnalyzer` and `InstanceProjector`
**Rationale**: `xsd:long` precisely represents 64-bit signed integers, matching .NET `Int64` semantics exactly. `xsd:integer` is an abstract unbounded integer type — less precise. `xsd:long` is a valid derived type of `xsd:integer` in the XSD hierarchy, so OWL reasoners handle it correctly.
**Alternatives considered**:
- `xsd:integer`: More abstract, broader compatibility claim, but less precise
- User confirmed precision over abstraction

## R3: Structural Hash for InstanceProjector Cache Keys

**Decision**: Replace `RuntimeHelpers.GetHashCode` with structural hash of RDF-relevant properties
**Rationale**: `RuntimeHelpers.GetHashCode` returns identity hash codes which are not unique (collisions possible) and not stable across GC compaction. Structural hashing based on the object's RDF-relevant property values provides content-addressable keys that survive object recreation.
**Alternatives considered**:
- `ConditionalWeakTable<obj, RdfGraph>`: Identity-based, GC-friendly, but doesn't survive object recreation
- User selected structural hash for content-addressability

**Implementation note**: Hash should cover the properties that affect RDF projection output. Consider using F# structural equality where the projected type is a record, or a custom hash combining field values for other types.

## R4: MediaTypeHeaderValue Parsing

**Decision**: Replace `String.Contains` in `negotiateRdfType` with `MediaTypeHeaderValue` parsing
**Rationale**: `String.Contains("application/ld+json")` false-matches on edge cases (e.g., `x-application/ld+json`, media types containing the substring in parameters). ASP.NET Core provides `MediaTypeHeaderValue` for proper RFC 7231 parsing including quality factors.
**Alternatives considered**:
- Regex: Fragile, doesn't handle quality factors
- Manual split on `,` and `;`: Reinvents what ASP.NET Core already provides

**Implementation**: Use `MediaTypeHeaderValue.TryParse` or parse the Accept header via `Microsoft.Net.Http.Headers.MediaTypeHeaderValue.ParseList`.

## R5: ExtractionState SourceMap Type Change

**Decision**: Replace `Dictionary<Uri, SourceLocation>` with `Map<string, SourceLocation>`
**Rationale**: `Dictionary` is mutable inside an otherwise immutable F# record — violates immutability expectations. `Uri` has surprising equality semantics (scheme-insensitive comparison, trailing-slash normalization) that can cause key lookup failures. `Map<string, _>` is immutable and uses string comparison which is predictable.
**Alternatives considered**:
- `Map<Uri, SourceLocation>`: Still has Uri equality issues
- `ImmutableDictionary<string, SourceLocation>`: Heavier than F# Map, no structural equality

**Migration note**: Serialization format in `state.json` may need migration — existing state files use Uri keys. The `load` function should handle both formats during transition.

## R6: Fail-Fast vs Result Wrapping

**Decision**: Follow fail-fast pattern per F# style guidelines
**Rationale**: .NET has exceptions and they should be used for unrecoverable errors. Over-wrapping in `Result` is an anti-pattern in F# — it obscures failure modes and adds ceremony without value when the error is not recoverable. `Result`/`Option` are reserved for expected, recoverable outcomes (parsing user input, validation, configuration loading). Unrecoverable errors (file system failures, assembly loading errors, malformed internal data) should throw and surface immediately.
**Alternatives considered**:
- Railway-oriented programming everywhere: Adds ceremony for unrecoverable paths
- User explicitly stated this is an anti-pattern in F#

## R7: Wildcard Package Version Pinning

**Decision**: Pin all wildcard versions to specific releases
**Packages to pin** (in `Frank.Cli.Core.fsproj`):
- `FSharp.Compiler.Service` 43.10.* → pin to latest 43.10.x release
- `Ionide.ProjInfo` 0.74.* → pin to latest 0.74.x release
- `Ionide.ProjInfo.FCS` 0.74.* → pin to latest 0.74.x release

**FSharp.Core inconsistency**: Currently 10.0.101 (Frank.Analyzers) vs 10.0.103 (Frank.Cli.Core, Frank.OpenApi, Frank.Cli). Normalize to a single version.

**Action**: Resolve latest stable versions at implementation time using `dotnet list package --outdated`.
