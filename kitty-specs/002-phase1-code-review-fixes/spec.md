# Feature Specification: Phase 1.1 Code Review Fixes

**Feature Branch**: `002-phase1-code-review-fixes`
**Created**: 2026-03-05
**Status**: Done
**Input**: Code review of 001-semantic-resources-phase1 (WP01-WP12, ~8,500 lines across 103 files). 23 items across 4 tiers must be resolved before Phase 2 begins.
**Parent Issue**: #80
**Tracking Issue**: #81
**Phase 1 PR**: #83

## User Scenarios & Testing

### User Story 1 - Correct Typed Literal Serialization (Priority: P1)

A developer using Frank.LinkedData serializes resources with typed properties (integers, booleans, decimals) to JSON-LD. The output must preserve type information regardless of whether the resource graph contains one subject or multiple subjects.

**Why this priority**: Functional correctness bug — data loss in multi-subject JSON-LD output silently produces wrong results.

**Independent Test**: Serialize a resource graph with multiple subjects containing typed literals (int, bool, double) and verify `@type` annotations are preserved in the `@graph` output.

**Acceptance Scenarios**:

1. **Given** a resource graph with multiple subjects containing `int`, `bool`, and `double` properties, **When** serialized to JSON-LD, **Then** each value includes correct `@type` and `@value` annotations matching the single-subject code path.
2. **Given** a resource with a `Decimal` property, **When** type-mapped for RDF, **Then** it maps to `xsd:decimal` (not `xsd:double`).
3. **Given** a resource with an `Int64` property, **When** type-mapped for RDF, **Then** the mapping is consistent across `TypeAnalyzer` and `InstanceProjector` (both use the same XSD type).

---

### User Story 2 - Resource Disposal and Leak Prevention (Priority: P1)

A developer runs Frank applications in production. All disposable resources (streams, JSON documents) are properly disposed, preventing memory leaks and file handle exhaustion under sustained load.

**Why this priority**: Constitution Principle VI (Resource Disposal) — resource leaks cause production failures under load.

**Independent Test**: Verify that `StreamReader` in `GraphLoader.fs` uses `use` binding and that `JsonDocument` in `CompileCommand.verifyRoundTrip` is disposed after use.

**Acceptance Scenarios**:

1. **Given** `GraphLoader.load` is called, **When** a `StreamReader` is created, **Then** it is bound with `use` and disposed when the scope exits.
2. **Given** `CompileCommand.verifyRoundTrip` is called, **When** `JsonDocument.Parse` returns a document, **Then** it is bound with `use` and disposed after validation.

---

### User Story 3 - Build Integrity (Priority: P1)

A developer clones the repository and runs `dotnet build Frank.sln`. All projects build successfully, and sample applications reference dependencies correctly without requiring unpublished NuGet packages.

**Why this priority**: Broken builds block all contributors.

**Independent Test**: Run `dotnet build Frank.sln` from a clean clone and verify all projects compile. Verify sample project references are `ProjectReference` (not NuGet 7.3.0).

**Acceptance Scenarios**:

1. **Given** a fresh clone of the repository, **When** `dotnet build Frank.sln` is run, **Then** `Frank.Cli.MSBuild` is included and builds successfully.
2. **Given** `Frank.LinkedData.Sample`, **When** it references `Frank.Cli.MSBuild`, **Then** the reference is a `ProjectReference` or uses a local feed — not a NuGet reference to an unpublished version.

---

### User Story 4 - Observable Error Handling (Priority: P1)

A developer debugging a Frank.LinkedData application can see errors in middleware via structured logging rather than having exceptions silently swallowed.

**Why this priority**: Constitution Principle VII (No Silent Exception Swallowing) — silent failures make debugging impossible.

**Independent Test**: Trigger an error in LinkedData middleware and verify it is logged via `ILogger` rather than caught and discarded.

**Acceptance Scenarios**:

1. **Given** LinkedData middleware encounters an exception during content negotiation, **When** the exception is caught, **Then** it is logged via an injected `ILogger` with appropriate severity and context.
2. **Given** the middleware has no catch-all `with | _ ->` handler, **When** an unexpected exception occurs, **Then** it propagates or is logged — never silently discarded.

---

### User Story 5 - Correct Cache Keys and Content Negotiation (Priority: P2)

A developer relies on instance projection caching and Accept header negotiation. Cache keys must be unique per object instance, and content negotiation must correctly parse media types without false matches.

**Why this priority**: Incorrect cache keys cause data corruption; incorrect Accept parsing causes wrong response formats.

**Independent Test**: Verify cache key uniqueness for distinct objects and verify Accept header parsing rejects partial string matches.

**Acceptance Scenarios**:

1. **Given** two distinct objects in `InstanceProjector`, **When** cache keys are generated, **Then** each object gets a unique key (not based on `RuntimeHelpers.GetHashCode`).
2. **Given** an Accept header containing `text/html`, **When** `negotiateRdfType` checks for `application/ld+json`, **Then** it does not false-match via `String.Contains`.
3. **Given** an Accept header `application/ld+json;q=0.9, text/turtle;q=0.8`, **When** parsed, **Then** proper `MediaTypeHeaderValue` parsing is used.

---

### User Story 6 - Robust Extraction State and Assembly Access (Priority: P2)

A developer runs the CLI extraction pipeline in various hosting scenarios (including test hosts). The extraction state uses immutable, correctly-compared data structures, and assembly access handles null gracefully.

**Why this priority**: Mutable state in immutable records and null assembly references cause subtle runtime failures.

**Independent Test**: Run CLI extraction in a test host scenario and verify no `NullReferenceException` from `Assembly.GetEntryAssembly()`. Verify `ExtractionState` uses `Map<string, SourceLocation>` instead of `Dictionary<Uri, SourceLocation>`.

**Acceptance Scenarios**:

1. **Given** a test host scenario, **When** `Assembly.GetEntryAssembly()` returns null, **Then** the code handles it gracefully (fallback or informative error).
2. **Given** `ExtractionState`, **When** source locations are stored, **Then** they use `Map<string, SourceLocation>` (not `Dictionary<Uri, SourceLocation>`).

---

### User Story 7 - Deduplicated Shared Utilities (Priority: P2)

A developer maintaining the CLI codebase finds URI construction logic in one place rather than duplicated across 4 modules.

**Why this priority**: Constitution Principle VIII (No Duplicated Logic) — duplicated helpers diverge over time and increase maintenance burden.

**Independent Test**: Verify that `classUri`, `propertyUri`, `resourceUri`, `routeToSlug`, and `fieldKindToRange` exist in a single shared `UriHelpers` module and are referenced (not duplicated) by consuming modules.

**Acceptance Scenarios**:

1. **Given** URI construction helpers, **When** searching the codebase, **Then** each helper is defined exactly once in a shared module.
2. **Given** the 4 extraction modules, **When** they need URI construction, **Then** they reference the shared `UriHelpers` module.

---

### User Story 8 - Reproducible Builds (Priority: P2)

A developer building the project gets the same dependency versions regardless of when they restore packages. No floating wildcard versions.

**Why this priority**: Floating versions cause non-reproducible builds and surprise breaking changes.

**Independent Test**: Inspect all `.fsproj` files and verify no wildcard version specifiers (`*`) remain on `FSharp.Compiler.Service`, `Ionide.ProjInfo`, or `System.CommandLine`.

**Acceptance Scenarios**:

1. **Given** all project files, **When** package references are inspected, **Then** `FSharp.Compiler.Service`, `Ionide.ProjInfo`, and `System.CommandLine` have pinned versions (no `*` wildcards).

---

### User Story 9 - Idiomatic F# Patterns (Priority: P3)

A developer reading the codebase encounters idiomatic F# patterns: discriminated unions for type-safe discriminators, `result {}` CE for error handling, functional folds instead of imperative accumulation, and explicit module opens instead of `[<AutoOpen>]` on broadly-named modules.

**Why this priority**: Code quality and maintainability — idiomatic F# is easier to reason about and less error-prone.

**Independent Test**: Verify each refactoring independently — DU usage, nested match removal, imperative-to-functional conversion, AutoOpen removal, dead parameter removal.

**Acceptance Scenarios**:

1. **Given** `ValidationIssue.Severity` and `DiffEntry.Type`, **When** inspected, **Then** they are discriminated unions (not string-typed).
2. **Given** `LinkedDataConfig.loadConfig` and `GraphLoader.load`, **When** inspected, **Then** nested `match` pyramids are replaced with `result {}` CE or `Result.bind` chains.
3. **Given** `AstAnalyzer.walkCeBody`, **When** inspected, **Then** `ResizeArray` + `ref` cells are replaced with a functional fold.
4. **Given** the `FSharpRdf` module, **When** inspected, **Then** `[<AutoOpen>]` is removed and consumers use explicit `open`.
5. **Given** `ExtractCommand`, **When** its parameters are inspected, **Then** the dead `scope` parameter is removed.
6. **Given** the `ValidateFrankSemanticDefinitions` MSBuild target, **When** a clean build runs, **Then** it checks specific input artifacts (not the output directory) to avoid unnecessary re-validation.

---

### User Story 10 - Minor Polish (Priority: P3)

A developer finds consistent helper definitions, proper literal annotations, and uniform `FSharp.Core` pinning across the codebase.

**Why this priority**: Polish items that reduce noise and improve consistency.

**Independent Test**: Verify each item independently.

**Acceptance Scenarios**:

1. **Given** `localName`/`namespaceUri` helpers, **When** searching the formatter modules, **Then** they are defined once in a shared location.
2. **Given** vocabulary constants in `Vocabularies.fs`, **When** inspected, **Then** they use `[<Literal>]` annotations.
3. **Given** all project files, **When** `FSharp.Core` references are inspected, **Then** pinning is consistent across all projects.

---

### Edge Cases

- What happens when `GraphLoader.load` encounters a malformed file and the `StreamReader` must still be disposed? Disposal must occur even on exception paths.
- What happens when `Assembly.GetEntryAssembly()` returns null and the code needs assembly metadata? Graceful fallback required.
- What happens when the Accept header is malformed or empty? `MediaTypeHeaderValue` parsing must handle gracefully.
- What happens when `InstanceProjector` caches two objects with the same structural content but different identity? Cache keys must distinguish them.

## Clarifications

### Session 2026-03-06

- Q: Should string-typed discriminators always become DUs, or should performance trade-offs (e.g., static byte arrays as used in Frank.Datastar) be considered? → A: The constitution emphasizes both idiomatic F# and performance. DUs are idiomatic; static byte arrays may be more performant for hot paths. These trade-offs must be held in balance. Where uncertain, ask — do not decide unilaterally.
- Q: Should exceptions be wrapped in Result types for error handling, or should the fail-fast pattern be used? → A: Follow fail-fast per F# style guidelines. Unrecoverable errors should throw exceptions and surface immediately — do not wrap them in Result or attempt to bubble them up. Use Result/Option only for expected, recoverable outcomes (e.g., parsing, validation). .NET has exceptions, and they should be used where warranted. Overuse of Result is an anti-pattern in F#. This reinforces Constitution Principle VII: errors must be visible, not hidden — whether by silent catch-all handlers or by over-wrapping in Result types.
- Q: Is `result {}` CE preferred over `Result.bind` with pipe operators for replacing nested match pyramids? → A: Both are idiomatic — no preference between CEs and piped module functions. Nested match pyramids may involve `Result`, `Option`, `Async`, or other wrapper types. Compose naturally using the appropriate CE or piped module functions (`Result.bind`, `Option.bind`, `Async.bind`, etc.). Value extraction functions (`Async.RunSynchronously`, `Option.get`/`Option.defaultValue`, `Result.defaultValue`, etc.) and forced unwrapping should only occur once at the top-level call site — not repeatedly mid-function. Within the body, pattern match rather than force-unwrap.

## Requirements

### Functional Requirements

- **FR-001**: System MUST serialize typed literals (int, bool, double, decimal) correctly in both single-subject and multi-subject (`@graph`) JSON-LD output paths.
- **FR-002**: System MUST map `Int64` to the same XSD type consistently across `TypeAnalyzer` and `InstanceProjector`.
- **FR-003**: System MUST map `Decimal` to `xsd:decimal` (not `xsd:double`).
- **FR-004**: System MUST dispose all `IDisposable` resources (`StreamReader`, `JsonDocument`) via `use` bindings.
- **FR-005**: System MUST include `Frank.Cli.MSBuild` in `Frank.sln`.
- **FR-006**: Sample projects MUST reference `Frank.Cli.MSBuild` via `ProjectReference` or local feed — not an unpublished NuGet version.
- **FR-007**: System MUST follow the fail-fast pattern. Unrecoverable errors MUST throw exceptions and surface immediately — not be wrapped in Result types or silently caught. Catch-all `with | _ ->` handlers that discard exceptions are prohibited. Where exceptions are caught for recoverable scenarios, they MUST be logged via `ILogger` with appropriate severity and context. Result/Option types are reserved for expected, recoverable outcomes (parsing, validation) — not as a general error-handling mechanism.
- **FR-008**: System MUST use unique, stable cache keys for instance projection (not `RuntimeHelpers.GetHashCode`).
- **FR-009**: System MUST parse Accept headers using `MediaTypeHeaderValue` — not `String.Contains`.
- **FR-010**: System MUST use `Map<string, SourceLocation>` (not `Dictionary<Uri, SourceLocation>`) in `ExtractionState`.
- **FR-011**: System MUST handle `Assembly.GetEntryAssembly()` returning null without throwing.
- **FR-012**: System MUST consolidate duplicated URI construction helpers into a shared `UriHelpers` module.
- **FR-013**: System MUST pin all floating wildcard package versions to specific versions.
- **FR-014**: System MUST replace string-typed discriminators (`ValidationIssue.Severity`, `DiffEntry.Type`) with type-safe alternatives. The choice between discriminated unions and static byte arrays (as used in Frank.Datastar) must balance idiomatic F# with performance. Where the trade-off is unclear, escalate to the user rather than deciding unilaterally.
- **FR-015**: System MUST replace nested `match` pyramids in `LinkedDataConfig.loadConfig` and `GraphLoader.load` with composed pipelines using the appropriate CE (`result {}`, `option {}`, `async {}`, etc.) or piped module functions (`Result.bind`, `Option.bind`, etc.). Value extraction (`Async.RunSynchronously`, `Option.get`, `Result.defaultValue`, etc.) and forced unwrapping MUST only occur once at the top-level call site. Within function bodies, use pattern matching or composition — not repeated force-unwrapping.
- **FR-016**: System MUST replace imperative accumulation (`ResizeArray` + `ref`) with functional fold in `AstAnalyzer.walkCeBody`.
- **FR-017**: System MUST remove `[<AutoOpen>]` from `FSharpRdf` module.
- **FR-018**: System MUST remove dead `scope` parameter from `ExtractCommand`.
- **FR-019**: System MUST fix `ValidateFrankSemanticDefinitions` MSBuild target to check specific input artifacts.
- **FR-020**: System MUST deduplicate `localName`/`namespaceUri` helpers across formatter modules.
- **FR-021**: System MUST annotate vocabulary constants with `[<Literal>]` in `Vocabularies.fs`.
- **FR-022**: System MUST use consistent `FSharp.Core` pinning across all projects.

## Success Criteria

### Measurable Outcomes

- **SC-001**: All 265 existing tests continue to pass after all changes are applied.
- **SC-002**: New tests are added for every behavioral change (typed literal serialization, Accept header parsing, cache key uniqueness, null assembly handling).
- **SC-003**: `dotnet build Frank.sln` succeeds from a clean clone with zero warnings related to missing projects or unresolvable package references.
- **SC-004**: Zero instances of `with | _ ->` catch-all handlers remain in the codebase (excluding intentional, documented cases with logging).
- **SC-005**: Zero instances of floating wildcard (`*`) package versions remain in project files.
- **SC-006**: Zero duplicated URI construction helpers — each helper defined exactly once.
- **SC-007**: Constitution principles VI (Resource Disposal), VII (No Silent Exception Swallowing), and VIII (No Duplicated Logic) are verifiably enforced across all changed code.

## Assumptions

- The existing 265 tests from Phase 1 PR #83 represent the baseline. No tests should regress.
- `xsd:integer` vs `xsd:long` decision will be made during implementation based on OWL/XSD conventions (either is acceptable as long as it is consistent).
- `ConditionalWeakTable` or object reference identity is an acceptable replacement for `RuntimeHelpers.GetHashCode` as cache key strategy.
- The `result {}` CE is available in the project's F# version (8.0+) or via FsToolkit.ErrorHandling.
- Idiomatic F# and performance are both constitutional values. Refactoring toward DUs is preferred for type safety, but static byte arrays or other performant representations (per Frank.Datastar precedent) may be more appropriate on hot paths. When the trade-off is ambiguous, the implementer must ask the user rather than choose independently.
- Both CEs and piped module functions are acceptable idiomatic patterns for `Result`, `Option`, `Async`, and other wrapper types. Repeated force-unwrapping mid-function (`Async.RunSynchronously`, `Option.get`, etc.) is not acceptable — compose naturally and extract/unwrap once at the top-level call site. Within function bodies, pattern match rather than force-unwrap.
- Follow fail-fast per F# style guidelines. Use exceptions for unrecoverable errors — do not over-wrap in Result. Result/Option are for expected, recoverable outcomes (parsing, validation). Overuse of Result is an anti-pattern in F#.
