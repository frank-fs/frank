# Work Packages: Phase 1.1 Code Review Fixes

**Inputs**: Design documents from `kitty-specs/002-phase1-code-review-fixes/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md
**Tests**: New tests required for behavioral changes (SC-002).

**Organization**: Fine-grained subtasks (`Txxx`) roll up into work packages (`WPxx`). Each work package is scoped to a single module/subsystem per planning decision.

---

## Work Package WP01: Build & Project Integrity (Priority: P1)

**Goal**: Fix solution structure, package references, wildcard versions, and MSBuild target to ensure clean builds from a fresh clone.
**Independent Test**: `dotnet build Frank.sln` succeeds. No wildcard versions in any `.fsproj`. `FSharp.Core` pinning is consistent.
**Prompt**: `tasks/WP01-build-project-integrity.md`
**Estimated size**: ~300 lines
**Requirement Refs**: FR-005, FR-006, FR-013, FR-019, FR-022

### Included Subtasks
- [x] T001 Add `Frank.Cli.MSBuild` project to `Frank.sln`
- [x] T002 Fix `Frank.LinkedData.Sample` to use `ProjectReference` instead of NuGet 7.3.0 for `Frank.Cli.MSBuild`
- [x] T003 Pin `FSharp.Compiler.Service`, `Ionide.ProjInfo`, `Ionide.ProjInfo.FCS` to specific versions in `Frank.Cli.Core.fsproj`
- [x] T004 Normalize `FSharp.Core` pinning across all projects
- [x] T005 Fix `ValidateFrankSemanticDefinitions` MSBuild target to check specific input artifacts instead of output directory

### Implementation Notes
- Use `dotnet sln add` for T001
- For T003, resolve latest stable versions via `dotnet list package`
- For T004, audit all `.fsproj` files and `Directory.Build.props`
- For T005, modify `.targets` file to use `Inputs`/`Outputs` attributes on the target

### Parallel Opportunities
- T001, T002 are independent of T003, T004
- T005 is independent of all others

### Dependencies
- None (starting package — no code changes that affect other WPs)

### Risks & Mitigations
- Pinned versions may not be compatible: verify `dotnet build` succeeds after pinning

---

## Work Package WP02: Frank.LinkedData — Serialization & Disposal (Priority: P1)

**Goal**: Fix typed literal serialization in multi-subject JSON-LD, correct XSD type mappings, fix resource leaks, and deduplicate formatter helpers.
**Independent Test**: Multi-subject JSON-LD output preserves typed literals. `Decimal` maps to `xsd:decimal`. `StreamReader` uses `use` binding. `localName`/`namespaceUri` defined once.
**Prompt**: `tasks/WP02-linkeddata-serialization-disposal.md`
**Estimated size**: ~400 lines
**Requirement Refs**: FR-001, FR-002, FR-003, FR-004, FR-020

### Included Subtasks
- [x] T006 Fix `JsonLdFormatter` multi-subject (`@graph`) branch to preserve typed literal info (int, bool, double, decimal)
- [x] T007 Fix `InstanceProjector` to map `Int64` → `xsd:long` and `Decimal` → `xsd:decimal`
- [x] T008 Fix `GraphLoader.fs` `StreamReader` to use `use` binding
- [x] T009 [P] Create shared `RdfUriHelpers` module in `Frank.LinkedData` with `localName` and `namespaceUri` functions
- [x] T010 [P] Update `JsonLdFormatter`, `InstanceProjector`, and `WebHostBuilderExtensions` to use shared helpers

### Implementation Notes
- T006: The single-subject path correctly handles typed literals — mirror that logic in the `@graph` branch
- T007: `TypeAnalyzer` already uses `xsd:long`; align `InstanceProjector` to match
- T008: Replace `new StreamReader(...)` with `use reader = new StreamReader(...)`
- T009/T010: Extract `localName`/`namespaceUri` to a shared module; update consumers

### Parallel Opportunities
- T009 can be done in parallel with T006-T008
- T010 depends on T009

### Dependencies
- None (independent module)

### Risks & Mitigations
- JSON-LD serialization format change may affect downstream consumers: verify existing tests still pass

---

## Work Package WP03: Frank.LinkedData — Middleware & Negotiation (Priority: P1)

**Goal**: Replace silent exception swallowing with ILogger, fix Accept header parsing, handle null assembly, and replace identity-based cache keys with structural hash.
**Independent Test**: Middleware logs exceptions via ILogger. Accept header parsing uses `MediaTypeHeaderValue`. `Assembly.GetEntryAssembly()` null handled. Cache keys are structurally unique.
**Prompt**: `tasks/WP03-linkeddata-middleware-negotiation.md`
**Estimated size**: ~450 lines
**Requirement Refs**: FR-007, FR-008, FR-009, FR-011, FR-015

### Included Subtasks
- [x] T011 Replace catch-all `with | _ ->` in `linkedDataMiddleware` with ILogger-backed exception handling (fail-fast for unrecoverable errors)
- [x] T012 Replace `String.Contains` in `negotiateRdfType` with `MediaTypeHeaderValue` parsing
- [x] T013 Add null check for `Assembly.GetEntryAssembly()` in `WebHostBuilderExtensions.fs`
- [x] T014 Replace `RuntimeHelpers.GetHashCode` in `InstanceProjector` with structural hash of RDF-relevant properties
- [x] T015 Replace nested match pyramids in `LinkedDataConfig.loadConfig` with composed pipelines (CE or piped module functions)
- [x] T026 Replace nested match pyramids in `GraphLoader.load` with composed pipelines using FsToolkit.ErrorHandling CEs or `Result.bind`/`Option.bind`

### Implementation Notes
- T011: Inject `ILogger<>` into middleware. Log with `LogWarning`/`LogError` + exception context. Let unrecoverable errors propagate (fail-fast). Do not wrap in Result.
- T012: Use `Microsoft.Net.Http.Headers.MediaTypeHeaderValue.ParseList` for proper RFC 7231 parsing with quality factor support
- T013: Use `Option.ofObj` or null check with fallback/informative error
- T014: Hash the properties that affect RDF projection output; use F# structural equality for records
- T015: Add FsToolkit.ErrorHandling to `Frank.LinkedData.fsproj`. Use `result {}` or `Result.bind` pipelines. Extract/unwrap only at top-level call site.
- T026: Second half of FR-015. FsToolkit.ErrorHandling should already be added in T015. Compose with `result {}` or `option {}` CE. Incorporate `use` binding from WP02 T008.

### Parallel Opportunities
- T011, T012, T013 are independent
- T014 is independent
- T015 is independent
- T026 depends on T015 (FsToolkit.ErrorHandling already added)

### Dependencies
- Depends on WP02 (both modify `WebHostBuilderExtensions.fs`, `InstanceProjector.fs`, and `GraphLoader.fs`)

### Risks & Mitigations
- T011: Changing error behavior may expose previously-hidden failures — this is intentional per Constitution VII
- T014: Structural hash performance must be acceptable for the caching use case

---

## Work Package WP04: Frank.Cli.Core — Extraction Deduplication & State (Priority: P2)

**Goal**: Consolidate duplicated URI helpers into shared `UriHelpers` module, fix `ExtractionState` to use immutable `Map`, fix `Int64` mapping consistency, remove dead `scope` parameter.
**Independent Test**: Each URI helper defined exactly once. `ExtractionState.SourceMap` is `Map<string, SourceLocation>`. `Int64` maps to `xsd:long` in both `TypeAnalyzer` and extraction modules. `scope` parameter removed from `ExtractCommand`.
**Prompt**: `tasks/WP04-cli-extraction-deduplication.md`
**Estimated size**: ~450 lines
**Requirement Refs**: FR-002, FR-010, FR-012, FR-018

### Included Subtasks
- [x] T016 Create `UriHelpers.fs` in `Frank.Cli.Core/Extraction/` with `classUri`, `propertyUri`, `resourceUri`, `routeToSlug`, `fieldKindToRange`
- [x] T017 Update `TypeMapper.fs` to use `UriHelpers` (remove local `classUri`, `propertyUri`, `fieldKindToRange`)
- [x] T018 [P] Update `ShapeGenerator.fs` to use `UriHelpers` (remove local `classUri`, `propertyUri`, `fieldKindToRange`, `shapeUri` stays local)
- [x] T019 [P] Update `RouteMapper.fs` to use `UriHelpers` (remove local `routeToSlug`, `resourceUri`)
- [x] T020 [P] Update `CapabilityMapper.fs` to use `UriHelpers` (remove local `routeToSlug`, `resourceUri`)
- [x] T021 Change `ExtractionState.SourceMap` from `Dictionary<Uri, SourceLocation>` to `Map<string, SourceLocation>` and update `save`/`load` functions
- [x] T022 Remove dead `scope` parameter from `ExtractCommand` and `Program.fs` CLI argument definition

### Implementation Notes
- T016: Place `UriHelpers.fs` before `TypeMapper.fs` in project file compilation order
- T017-T020: Replace private helpers with `open Frank.Cli.Core.Extraction.UriHelpers` calls
- T021: Use `Uri.ToString()` as key during migration of existing state files. Handle both old (Uri key) and new (string key) formats in `load`
- T022: Remove from both `ExtractCommand.execute` signature and `System.CommandLine` argument definition in `Program.fs`

### Parallel Opportunities
- T018, T019, T020 can proceed in parallel after T016
- T021, T022 are independent of T016-T020

### Dependencies
- None (independent module)

### Risks & Mitigations
- T016: Compilation order matters in F# — `UriHelpers.fs` must appear before consumers in `.fsproj`
- T021: Existing `state.json` files need backward-compatible loading

---

## Work Package WP05: Frank.Cli.Core — Idiom & Quality (Priority: P2-P3)

**Goal**: Enforce idiomatic F# patterns: DUs for discriminators (with performance evaluation), functional fold in AstAnalyzer, remove `[<AutoOpen>]`, fix disposal, add `[<Literal>]` annotations. Add FsToolkit.ErrorHandling.
**Independent Test**: `ValidationIssue.Severity` and `DiffEntry.Type` are type-safe. `AstAnalyzer.walkCeBody` uses fold. `FSharpRdf` requires explicit `open`. `JsonDocument` disposed. Vocabulary constants are `[<Literal>]`.
**Prompt**: `tasks/WP05-cli-idiom-quality.md`
**Estimated size**: ~500 lines
**Requirement Refs**: FR-004, FR-014, FR-016, FR-017, FR-021

### Included Subtasks
- [x] T023 Add FsToolkit.ErrorHandling package reference to `Frank.Cli.Core.fsproj`
- [x] T024 Replace string-typed `ValidationIssue.Severity` with type-safe alternative (evaluate DU vs static byte array; escalate to user if on hot path)
- [x] T025 [P] Replace string-typed `DiffEntry.Type` with type-safe alternative (evaluate DU vs static byte array; escalate to user if on hot path)
- [x] T027 Replace imperative `ResizeArray` + `ref` cells in `AstAnalyzer.walkCeBody` with functional fold
- [x] T028 Remove `[<AutoOpen>]` from `FSharpRdf` module; add explicit `open` to all consumers
- [x] T029 [P] Fix `CompileCommand.verifyRoundTrip` to dispose `JsonDocument` via `use` binding
- [x] T030 [P] Add `[<Literal>]` annotations to vocabulary constants in `Vocabularies.fs`

### Implementation Notes
- T023: Pin to latest stable version
- T024/T025: These are NOT hot paths (validation/diff are developer-time operations). DUs are likely appropriate, but confirm with user if uncertain.
- T027: Replace `let results = ResizeArray(); let state = ref ...` with `List.fold` or `Array.fold` accumulating an immutable state
- T028: Search all `.fs` files for implicit use of `FSharpRdf` names; add `open Frank.Cli.Core.Rdf.FSharpRdf` where needed. Compiler errors will guide this.
- T029: Wrap `JsonDocument.Parse(...)` result with `use doc = ...`
- T030: Add `[<Literal>]` to `Rdf`, `Rdfs`, `Owl`, `Shacl`, `Hydra`, `SchemaOrg`, `Xsd` constants

### Parallel Opportunities
- T024, T025 are independent
- T029, T030 are independent of everything else
- T026, T027, T028 should be sequential (all touch `Frank.Cli.Core` compilation, potential conflicts)

### Dependencies
- None (independent module), but logically follows WP04 if both modify `Frank.Cli.Core`

### Risks & Mitigations
- T028: Removing `[<AutoOpen>]` will break compilation in many files — use compiler errors to find all consumers
- T024/T025: If these turn out to be hot paths, escalate to user per clarification

---

## Work Package WP06: New Tests for Behavioral Changes (Priority: P1)

**Goal**: Add tests for all behavioral changes introduced by WP02-WP05. Verify all 265 existing tests still pass.
**Independent Test**: All new tests pass. All 265 baseline tests pass. `dotnet test` succeeds across all test projects.
**Prompt**: `tasks/WP06-behavioral-change-tests.md`
**Estimated size**: ~400 lines
**Requirement Refs**: FR-001, FR-002, FR-004, FR-007, FR-008, FR-009, FR-010, FR-011

### Included Subtasks
- [x] T031 Add tests for multi-subject JSON-LD typed literal serialization (verifying @type/@value preserved in @graph output)
- [x] T032 [P] Add tests for `MediaTypeHeaderValue` Accept header parsing (proper media type matching, quality factors, edge cases: malformed, empty)
- [x] T033 [P] Add tests for structural hash cache key uniqueness (same content = same key, different content = different key, different identity same content = same key)
- [x] T034 [P] Add tests for `Assembly.GetEntryAssembly()` null handling
- [x] T035 [P] Add tests for `ExtractionState` serialization/deserialization with `Map<string, SourceLocation>` (including backward-compatible load of old Uri-keyed format)
- [x] T036 Run full test suite (`dotnet test`) to verify all 265 baseline tests pass and no regressions

### Implementation Notes
- T031: Test in `Frank.LinkedData.Tests` — create multi-subject graph with int, bool, double, decimal properties, serialize to JSON-LD, assert `@type` annotations present
- T032: Test in `Frank.LinkedData.Tests` — test `negotiateRdfType` with various Accept headers
- T033: Test in `Frank.LinkedData.Tests` — test `InstanceProjector` cache key generation
- T034: Test in `Frank.LinkedData.Tests` — mock/simulate null assembly scenario
- T035: Test in `Frank.Cli.Core.Tests` — round-trip ExtractionState save/load
- T036: Run from solution root, all test projects

### Parallel Opportunities
- T031-T035 can all proceed in parallel (different test files)
- T036 runs last as final verification

### Dependencies
- Depends on WP02, WP03, WP04, WP05 (tests validate behavioral changes from those WPs)

### Risks & Mitigations
- Test host scenarios for null assembly may require special setup
- Backward-compatible state loading test needs a fixture file with old format

---

## Dependency & Execution Summary

- **Phase 1 (parallel)**: WP01, WP02, WP04 — all independent, can run concurrently. WP03 depends on WP02.
- **Phase 2 (parallel after Phase 1)**: WP05 — logically follows WP04 but no hard dependency
- **Phase 3 (after all)**: WP06 — tests validate behavioral changes from WP02-WP05
- **Parallelization**: WP01-WP04 are fully parallelizable (different subsystems). WP05 can overlap with WP01-WP03.
- **MVP Scope**: WP01 + WP02 + WP03 (Tier 1 bugs: build integrity, correctness, disposal, error handling)

---

## Subtask Index (Reference)

| Subtask ID | Summary | Work Package | Priority | Parallel? |
|------------|---------|--------------|----------|-----------|
| T001 | Add Frank.Cli.MSBuild to Frank.sln | WP01 | P1 | No |
| T002 | Fix sample ProjectReference | WP01 | P1 | No |
| T003 | Pin wildcard package versions | WP01 | P1 | No |
| T004 | Normalize FSharp.Core pinning | WP01 | P1 | No |
| T005 | Fix MSBuild target input check | WP01 | P1 | No |
| T006 | Fix @graph typed literals | WP02 | P1 | No |
| T007 | Fix Int64/Decimal XSD mappings | WP02 | P1 | No |
| T008 | Fix StreamReader disposal | WP02 | P1 | No |
| T009 | Create RdfUriHelpers module | WP02 | P1 | Yes |
| T010 | Update consumers of localName/namespaceUri | WP02 | P1 | Yes |
| T011 | Replace catch-all with ILogger | WP03 | P1 | No |
| T012 | Fix Accept header parsing | WP03 | P1 | No |
| T013 | Handle null Assembly.GetEntryAssembly | WP03 | P1 | No |
| T014 | Structural hash cache keys | WP03 | P2 | No |
| T015 | Replace LinkedDataConfig match pyramids | WP03 | P2 | No |
| T016 | Create UriHelpers.fs module | WP04 | P2 | No |
| T017 | Update TypeMapper to use UriHelpers | WP04 | P2 | No |
| T018 | Update ShapeGenerator to use UriHelpers | WP04 | P2 | Yes |
| T019 | Update RouteMapper to use UriHelpers | WP04 | P2 | Yes |
| T020 | Update CapabilityMapper to use UriHelpers | WP04 | P2 | Yes |
| T021 | ExtractionState Dict→Map migration | WP04 | P2 | No |
| T022 | Remove dead scope parameter | WP04 | P2 | No |
| T023 | Add FsToolkit.ErrorHandling | WP05 | P2 | No |
| T024 | DU for ValidationIssue.Severity | WP05 | P3 | No |
| T025 | DU for DiffEntry.Type | WP05 | P3 | Yes |
| T026 | GraphLoader match pyramid → CE/pipe | WP03 | P2 | No |
| T027 | AstAnalyzer fold refactor | WP05 | P3 | No |
| T028 | Remove [AutoOpen] from FSharpRdf | WP05 | P3 | No |
| T029 | Fix JsonDocument disposal | WP05 | P1 | Yes |
| T030 | Add [Literal] to Vocabularies | WP05 | P3 | Yes |
| T031 | Tests: typed literal serialization | WP06 | P1 | No |
| T032 | Tests: Accept header parsing | WP06 | P1 | Yes |
| T033 | Tests: structural hash cache keys | WP06 | P1 | Yes |
| T034 | Tests: null assembly handling | WP06 | P1 | Yes |
| T035 | Tests: ExtractionState round-trip | WP06 | P1 | Yes |
| T036 | Full test suite verification | WP06 | P1 | No |
