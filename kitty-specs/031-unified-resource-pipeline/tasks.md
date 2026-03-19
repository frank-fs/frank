# Work Packages: Unified Resource Pipeline

**Inputs**: Design documents from `kitty-specs/031-unified-resource-pipeline/`
**Prerequisites**: plan.md (required), spec.md (user stories), research.md, data-model.md, contracts/affordance-map-schema.json, quickstart.md

**Tests**: Include in each WP where the spec defines acceptance scenarios.

**Organization**: 80 subtasks (`T001`–`T080`) roll up into 13 work packages (`WP01`–`WP13`). Each work package is independently deliverable and testable.

**Prompt Files**: Each work package references a matching prompt file in `tasks/`.

---

## Work Package WP01: Unified Model Types & Project Scaffolding (Priority: P0)

**Goal**: Define the core type system and create the `Frank.Affordances` project.
**Independent Test**: Types compile, MessagePack roundtrip test passes for F# DUs.
**Prompt**: `tasks/WP01-unified-model-types.md`
**Estimated Size**: ~350 lines

### Included Subtasks
- [x] T001 Create `UnifiedModel.fs` with `UnifiedResource`, `DerivedResourceFields`, `HttpCapability` types
- [x] T002 Create `AffordanceMap.fs` types (`AffordanceMapEntry`, `AffordanceLinkRelation`)
- [x] T003 Create `Frank.Affordances` project (framework reference only, no Frank.Statecharts dependency)
- [x] T004 Create `Frank.Affordances.Tests` test project
- [x] T005 Add MessagePack + MessagePack.FSharpExtensions to Frank.Cli.Core, verify F# DU serialization roundtrip

### Dependencies
- None (starting package).

### Parallel Opportunities
- T003/T004 (project scaffolding) can proceed alongside T001/T002 (type definitions).

### Risks & Mitigations
- MessagePack.FSharpExtensions may not handle all DU patterns → test with actual `UnifiedResource` and `ExtractedStatechart` types in T005.

---

## Work Package WP02: Unified AST Walker (Priority: P0)

**Goal**: Single-pass FCS extraction replacing both semantic and statechart extractors.
**Independent Test**: Unified extractor produces identical results to old extractors on tic-tac-toe fixtures.
**Prompt**: `tasks/WP02-unified-ast-walker.md`
**Estimated Size**: ~500 lines

### Included Subtasks
- [x] T006 Create `UnifiedExtractor.fs` module with single-pass syntax AST walker
- [x] T007 Merge `AstAnalyzer.walkExpr` and `StatechartSourceExtractor.walkExprForStateful` into unified `walkExpr`
- [x] T008 Merge `TypeAnalyzer.collectEntities` and `StatechartSourceExtractor.findMachineBindings` into unified typed AST walk
- [x] T009 Cross-reference syntax CEs with typed bindings to produce `UnifiedResource` records
- [x] T010 Compute `DerivedResourceFields` (orphan states, state-to-type mappings, type coverage)
- [x] T011 Handle plain `resource` CEs (type info only, no statechart)
- [x] T012 Write comparison tests: old extractors vs unified, assert identical output

### Dependencies
- Depends on WP01 (type definitions).

### Parallel Opportunities
- T007 (syntax merge) and T008 (typed merge) can proceed in parallel initially.

### Risks & Mitigations
- This is option B (single walk, no fallback). If blocked, stop and ask — do NOT fall back to composition (option A).
- FCS entity traversal may throw for unresolvable externals → use targeted `InvalidOperationException` catch (per spec 026 pattern).

---

## Work Package WP03: FCS Caching & Unified State Persistence (Priority: P1)

**Goal**: Cache the unified extraction state as MessagePack binary with staleness detection.
**Independent Test**: Extract, modify no files, extract again — second completes in <1s from cache.
**Prompt**: `tasks/WP03-fcs-caching.md`
**Estimated Size**: ~350 lines

### Included Subtasks
- [x] T013 Create `UnifiedCache.fs` — serialize/deserialize `UnifiedExtractionState` with MessagePack
- [x] T014 Implement source hash computation (hash all .fs files in the project)
- [x] T015 Implement staleness detection: compare cached hash vs current
- [x] T016 Implement cache read path: load `obj/frank-cli/unified-state.bin`
- [x] T017 Implement cache write path: serialize and write after extraction
- [x] T018 Implement `--force` flag to bypass cache
- [x] T018a Include tool version in cache header; detect version mismatch on load and re-extract automatically (FR-028)

### Dependencies
- Depends on WP01 (types).
- Can run in **parallel with WP02**.

### Parallel Opportunities
- WP03 and WP02 share only WP01 types — fully parallelizable.

### Risks & Mitigations
- MessagePack versioning: cache must include tool version for compatibility detection.
- Large projects may have many .fs files → hash computation should be fast (SHA256 of concatenated file hashes).

---

## Work Package WP04: CLI Command Wiring — Unified Extract & Generate (Priority: P1)

**Goal**: Wire the unified extraction and generation commands in the CLI.
**Independent Test**: `frank-cli extract --project <fsproj>` produces unified JSON output.
**Prompt**: `tasks/WP04-cli-command-wiring.md`
**Estimated Size**: ~450 lines

### Included Subtasks
- [x] T019 Create unified `ExtractCommand.fs` replacing both pipelines
- [x] T020 Wire `frank-cli extract --project` in Program.fs with `--output-format` and `--force`
- [x] T021 Update `generate` command to read from unified extraction state (project UnifiedResource → ExtractedStatechart → StatechartDocument → format text via existing generators)
- [ ] T022 Support `--format affordance-map` alongside existing formats (CLI flag wiring only — map generation logic is in WP06/T032-T036)
- [ ] T023 Update `HelpContent.fs` for unified extract command
- [ ] T024 Update `TextOutput.fs` and `JsonOutput.fs` for unified results
- [ ] T025 Ensure existing `statechart parse` and `statechart validate` continue working

### Dependencies
- Depends on WP02 (extractor) and WP03 (caching).

### Parallel Opportunities
- T023/T024 (output formatting) can proceed alongside T019-T022 (command logic).

### Risks & Mitigations
- Breaking existing CLI tests → run full 152 CLI test suite after changes.

---

## Work Package WP05: Unified ALPS Generation (Priority: P1)

**Goal**: Generate ALPS profiles containing both type descriptors and state transition descriptors.
**Independent Test**: Generated ALPS round-trips through the ALPS JSON parser with zero errors.
**Prompt**: `tasks/WP05-unified-alps-generation.md`
**Estimated Size**: ~400 lines

### Included Subtasks
- [ ] T026 Create `UnifiedAlpsGenerator.fs` — takes `UnifiedResource`, produces ALPS JSON
- [ ] T027 Implement Schema.org vocabulary alignment on type descriptors
- [ ] T028 Implement IANA-precedence link relation derivation
- [ ] T029 Handle plain resources (type descriptors + method transitions, no state)
- [ ] T030 Validate generated ALPS round-trips through `Alps.JsonParser.parseAlpsJson`
- [ ] T031 Write tests: ALPS for tic-tac-toe has both semantic and transition descriptors

### Dependencies
- Depends on WP02 (unified extractor provides `UnifiedResource`).

### Parallel Opportunities
- Can run in parallel with WP03, WP04, WP09.

### Risks & Mitigations
- ALPS descriptor ID naming must avoid collisions between type properties and transition names.

---

## Work Package WP06: Affordance Map Generation (Priority: P1)

**Goal**: Generate the machine-readable affordance map JSON/binary from unified resources.
**Independent Test**: Map entries for tic-tac-toe match expected (XTurn→{GET,POST}, Won→{GET}).
**Prompt**: `tasks/WP06-affordance-map-generation.md`
**Estimated Size**: ~350 lines

### Included Subtasks
- [ ] T032 Create `AffordanceMapGenerator.fs` — takes `UnifiedResource list`, produces map
- [ ] T033 Implement composite key generation `"{routeTemplate}|{stateKey}"`
- [ ] T034 Populate `AffordanceLinkRelation` entries with IANA-precedence relation types
- [ ] T035 Add `profileUrl` per entry from `--base-uri` + resource slug
- [ ] T036 Wire `--format affordance-map` output (JSON display + MessagePack binary)
- [ ] T037 Write tests: verify map for tic-tac-toe states

### Dependencies
- Depends on WP02 (extractor) and WP05 (link relation derivation logic).

### Parallel Opportunities
- Can run in parallel with WP03, WP04, WP09.

### Risks & Mitigations
- Affordance map schema must match `contracts/affordance-map-schema.json` exactly.

---

## Work Package WP07: Runtime Affordance Middleware (Priority: P1) 🎯 MVP Runtime

**Goal**: Middleware that injects Link + Allow headers based on current state.
**Independent Test**: GET in state XTurn returns `Allow: GET, POST` + Link headers. GET in Won returns `Allow: GET` only.
**Prompt**: `tasks/WP07-affordance-middleware.md`
**Estimated Size**: ~450 lines

### Included Subtasks
- [x] T038 Implement `AffordanceMap.fs` in `Frank.Affordances` — deserialize binary, build dictionary
- [x] T039 Implement `AffordanceMiddleware.fs` with `useAffordances` custom operation on `webHost` CE (same pattern as `useOpenApi`)
- [x] T040 Request-time: read state key, lookup map, inject Allow + Link + profile + describedby headers
- [x] T041 Handle plain resources (wildcard state key lookup)
- [x] T042 Graceful degradation when no map loaded (log warning, pass through)
- [x] T043 Pre-compute Link header strings at startup (zero allocation per request)
- [x] T044 Integration tests with TestHost

### Dependencies
- Depends on WP01 (types in Frank.Affordances) and WP06 (map format).

### Parallel Opportunities
- T043 (pre-computation) is independent of T040-T042 (request-time logic).

### Risks & Mitigations
- State key must match exactly what the statechart middleware puts in `HttpContext.Items` → verify key name.
- Zero-alloc goal requires pre-built header strings, not per-request string concatenation.

---

## Work Package WP08: Startup Projection — Binary → In-Memory Views (Priority: P2)

**Goal**: At startup, project ALPS, OWL, SHACL, and JSON Schema from the binary unified state.
**Independent Test**: Request profile URL with different Accept headers, receive correct format.
**Prompt**: `tasks/WP08-startup-projection.md`
**Estimated Size**: ~450 lines

### Included Subtasks
- [ ] T045 Create `StartupProjection.fs` — deserialize binary, project all views
- [ ] T046 Project ALPS profile (reuse `UnifiedAlpsGenerator` from WP05)
- [ ] T047 Project OWL ontology (call `TypeMapper.mapTypes`)
- [ ] T048 Project SHACL shapes (call `ShapeGenerator.generateShapes`)
- [ ] T049 Project JSON Schema per resource via `FSharp.Data.JsonSchema.OpenApi`
- [ ] T050 Create `ProfileMiddleware.fs` — serve views at configured URLs via content negotiation
- [ ] T051 Integration tests: different Accept headers → correct content types

### Dependencies
- Depends on WP07 (middleware infrastructure).

### Parallel Opportunities
- T046-T049 (projections) are independent pure functions — all parallelizable.

### Risks & Mitigations
- Content negotiation must handle Accept header quality factors correctly.
- Projection at startup adds to cold-start time → benchmark; target <500ms.

---

## Work Package WP09: MSBuild Target for Binary Embedding (Priority: P2)

**Goal**: Auto-embed `unified-state.bin` in the application assembly at build time.
**Independent Test**: Build sample project, verify `Assembly.GetManifestResourceStream()` returns binary.
**Prompt**: `tasks/WP09-msbuild-embedding.md`
**Estimated Size**: ~300 lines

### Included Subtasks
- [ ] T052 Create `Frank.Affordances.MSBuild` project (C# .csproj)
- [ ] T053 Create `Frank.Affordances.targets` — discover and embed binary
- [ ] T054 Wire target to AfterTargets="Build"
- [ ] T055 Verify embedding via test project + GetManifestResourceStream
- [ ] T056 Document NuGet packaging strategy (buildTransitive/)

### Dependencies
- Depends on WP03 (binary cache format).
- Can run in **parallel with WP04-WP08**.

### Parallel Opportunities
- Fully independent of the CLI command and middleware work.

### Risks & Mitigations
- MSBuild target ordering: must run after the main Build but before Pack.
- Need to handle case where binary doesn't exist (first build before extraction).

---

## Work Package WP10: Datastar Affordance-Driven Fragments (Priority: P2)

**Goal**: Helper function for conditional rendering based on affordance map.
**Independent Test**: `affordancesFor` returns correct methods per state with mock map.
**Prompt**: `tasks/WP10-datastar-affordance-helper.md`
**Estimated Size**: ~300 lines

### Included Subtasks
- [ ] T057 Create `AffordanceHelper.fs` in `Frank.Datastar`
- [ ] T058 Return `AffordanceResult` with `AllowedMethods`, `LinkRelations`, convenience booleans
- [ ] T059 Permissive default when map not loaded
- [ ] T060 Add to Frank.Datastar.fsproj (no new project references)
- [ ] T061 Write tests with mock affordance map

### Dependencies
- Depends on WP06 (affordance map types/format).
- Can run in **parallel with WP07-WP09**.

### Parallel Opportunities
- Fully independent of the middleware and MSBuild work.

### Risks & Mitigations
- Must not introduce dependency from Frank.Datastar → Frank.Affordances. Map types passed as parameter or defined locally.

---

## Work Package WP11: OpenAPI Consistency Validation (Priority: P2)

**Goal**: Compare unified model against OpenAPI schema for drift detection.
**Independent Test**: Intentional type mismatch detected as validation failure.
**Prompt**: `tasks/WP11-openapi-consistency.md`
**Estimated Size**: ~400 lines

### Included Subtasks
- [ ] T062 Create `OpenApiConsistencyValidator.fs` in Frank.Cli.Core
- [ ] T063 Generate expected JSON Schema from `UnifiedResource.TypeInfo` via `FSharp.Data.JsonSchema.OpenApi`
- [ ] T064 Compare: match fields, report unmapped/orphan/type mismatches
- [ ] T065 Wire `frank-cli validate --project <fsproj> --openapi` in Program.fs
- [ ] T066 Format results using `ValidationReport` structure
- [ ] T067 Write tests with intentional mismatches

### Dependencies
- Depends on WP02 (unified extractor).
- Can run in **parallel with WP03-WP10**.

### Parallel Opportunities
- Fully independent of runtime/middleware work.

### Risks & Mitigations
- OpenAPI schema comparison requires building the app via TestHost to capture runtime OpenAPI output → integration test, not static analysis.

---

## Work Package WP12: Semantic Subcommand Backward Compatibility (Priority: P2)

**Goal**: Existing `semantic` subcommands read from unified state via projector.
**Independent Test**: `semantic compile` produces identical OWL/SHACL output from unified state.
**Prompt**: `tasks/WP12-semantic-backward-compat.md`
**Estimated Size**: ~400 lines

### Included Subtasks
- [ ] T068 Create `ExtractionStateProjector.fs` with `toExtractionState` function
- [ ] T069 Project OWL graphs via `TypeMapper.mapTypes`
- [ ] T070 Project `ExtractionState.SourceMap` from unified resource data
- [ ] T071 Update `ClarifyCommand`, `ValidateCommand`, `CompileCommand`, `DiffCommand`
- [ ] T072 Detect old `state.json` format, prompt re-extraction
- [ ] T073 Write comparison tests: projected state matches old semantic extract output

### Dependencies
- Depends on WP02 (extractor) and WP03 (cache).

### Parallel Opportunities
- Can run in parallel with WP04-WP11.

### Risks & Mitigations
- Old `semantic validate` command shares name with `statechart validate` — ensure CLI routing is unambiguous.
- Projector must produce byte-identical OWL/SHACL to the old pipeline → use comparison tests.

---

## Work Package WP13: Minimal Tic-Tac-Toe Reference & End-to-End Validation (Priority: P1) 🎯 E2E

**Goal**: Validate the entire pipeline end-to-end against a minimal sample app.
**Independent Test**: Full pipeline: extract → generate → build → serve → verify headers.
**Prompt**: `tasks/WP13-tictactoe-reference-app.md`
**Estimated Size**: ~400 lines

### Included Subtasks
- [ ] T074 Create minimal `sample/Frank.TicTacToe.Sample/` project (reuse existing test fixtures)
- [ ] T075 Add Frank.Affordances + `useAffordances` in webHost CE
- [ ] T076 Run `frank-cli extract`, verify unified extraction output
- [ ] T077 Run `frank-cli generate --format alps`, verify ALPS has type + behavior descriptors
- [ ] T078 Run `frank-cli generate --format affordance-map`, verify map entries
- [ ] T079 Build, start TestHost, verify Link + Allow + profile + describedby headers per state
- [ ] T080 Add Datastar SSE handler with `affordancesFor()`, verify state-aware fragments

### Dependencies
- Depends on WP07 (middleware), WP08 (projections), WP09 (MSBuild), WP10 (Datastar helper).

### Parallel Opportunities
- None — this is the integration test that validates everything together.

### Risks & Mitigations
- Uses existing `StatefulResourceTests.fs` fixtures (minimal tic-tac-toe), NOT the full `panesofglass/tic-tac-toe` repo. Full MPST-based tic-tac-toe is deferred to after session type issues land.

---

## Dependency & Execution Summary

```
Phase 0 (Foundation):
  WP01 ──→ WP02 (unified walker)
       └──→ WP03 (caching) [parallel with WP02]

Phase 1 (Core Pipeline):
  WP02 + WP03 ──→ WP04 (CLI wiring)
  WP02 ──→ WP05 (ALPS generation)
  WP05 ──→ WP06 (affordance map)

Phase 2 (Runtime):
  WP06 + WP01 ──→ WP07 (middleware)
  WP07 ──→ WP08 (startup projection)
  WP03 ──→ WP09 (MSBuild) [parallel with WP04-WP08]

Phase 3 (Integration):
  WP06 ──→ WP10 (Datastar helper) [parallel with WP07-WP09]
  WP02 ──→ WP11 (OpenAPI validation) [parallel with WP03-WP10]
  WP02 + WP03 ──→ WP12 (semantic compat) [parallel with WP04-WP11]

Phase 4 (Validation):
  WP07 + WP08 + WP09 + WP10 ──→ WP13 (E2E)
```

**Parallelization**: After WP01 completes, WP02 and WP03 can run in parallel. After WP02, WP05/WP11/WP12 can run in parallel. WP09 and WP10 are independently parallelizable with most other WPs.

**MVP Scope**: WP01 → WP02 → WP05 → WP06 → WP07 (unified extraction + ALPS + affordance map + runtime middleware). This delivers the core Fielding gap closure.

---

## Subtask Index (Reference)

| Subtask | Summary | WP | Priority | Parallel? |
|---------|---------|-----|----------|-----------|
| T001 | UnifiedModel.fs types | WP01 | P0 | No |
| T002 | AffordanceMap.fs types | WP01 | P0 | Yes |
| T003 | Frank.Affordances project | WP01 | P0 | Yes |
| T004 | Frank.Affordances.Tests project | WP01 | P0 | Yes |
| T005 | MessagePack DU roundtrip test | WP01 | P0 | No |
| T006 | UnifiedExtractor.fs module | WP02 | P0 | No |
| T007 | Merge syntax AST walkers | WP02 | P0 | Yes |
| T008 | Merge typed AST walkers | WP02 | P0 | Yes |
| T009 | Cross-reference CEs → UnifiedResource | WP02 | P0 | No |
| T010 | Compute DerivedResourceFields | WP02 | P0 | No |
| T011 | Handle plain resource CEs | WP02 | P0 | No |
| T012 | Comparison tests (old vs new) | WP02 | P0 | No |
| T013 | UnifiedCache.fs (MessagePack) | WP03 | P1 | No |
| T014 | Source hash computation | WP03 | P1 | Yes |
| T015 | Staleness detection | WP03 | P1 | No |
| T016 | Cache read path | WP03 | P1 | No |
| T017 | Cache write path | WP03 | P1 | No |
| T018 | --force flag | WP03 | P1 | No |
| T019 | Unified ExtractCommand.fs | WP04 | P1 | No |
| T020 | CLI wiring (extract) | WP04 | P1 | No |
| T021 | Generate from unified state | WP04 | P1 | No |
| T022 | --format affordance-map | WP04 | P1 | No |
| T023 | HelpContent.fs update | WP04 | P1 | Yes |
| T024 | TextOutput/JsonOutput update | WP04 | P1 | Yes |
| T025 | Preserve parse/validate commands | WP04 | P1 | No |
| T026 | UnifiedAlpsGenerator.fs | WP05 | P1 | No |
| T027 | Schema.org alignment | WP05 | P1 | No |
| T028 | IANA-precedence link relations | WP05 | P1 | No |
| T029 | Plain resource ALPS | WP05 | P1 | No |
| T030 | ALPS roundtrip validation | WP05 | P1 | No |
| T031 | ALPS tic-tac-toe tests | WP05 | P1 | No |
| T032 | AffordanceMapGenerator.fs | WP06 | P1 | No |
| T033 | Composite key generation | WP06 | P1 | No |
| T034 | LinkRelation population | WP06 | P1 | No |
| T035 | profileUrl derivation | WP06 | P1 | No |
| T036 | --format affordance-map output | WP06 | P1 | No |
| T037 | Affordance map tests | WP06 | P1 | No |
| T038 | AffordanceMap.fs (deserialize) | WP07 | P1 | No |
| T039 | AffordanceMiddleware.fs | WP07 | P1 | No |
| T040 | Request-time header injection | WP07 | P1 | No |
| T041 | Plain resource handling | WP07 | P1 | No |
| T042 | Graceful degradation | WP07 | P1 | No |
| T043 | Pre-computed header strings | WP07 | P1 | No |
| T044 | Middleware integration tests | WP07 | P1 | No |
| T045 | StartupProjection.fs | WP08 | P2 | No |
| T046 | Project ALPS at startup | WP08 | P2 | Yes |
| T047 | Project OWL at startup | WP08 | P2 | Yes |
| T048 | Project SHACL at startup | WP08 | P2 | Yes |
| T049 | Project JSON Schema at startup | WP08 | P2 | Yes |
| T050 | ProfileMiddleware.fs (conneg) | WP08 | P2 | No |
| T051 | Content negotiation tests | WP08 | P2 | No |
| T052 | Frank.Affordances.MSBuild project | WP09 | P2 | No |
| T053 | Frank.Affordances.targets | WP09 | P2 | No |
| T054 | AfterTargets="Build" wiring | WP09 | P2 | No |
| T055 | Embedding verification test | WP09 | P2 | No |
| T056 | NuGet packaging docs | WP09 | P2 | No |
| T057 | AffordanceHelper.fs | WP10 | P2 | No |
| T058 | AffordanceResult type | WP10 | P2 | No |
| T059 | Permissive default | WP10 | P2 | No |
| T060 | Add to Frank.Datastar.fsproj | WP10 | P2 | No |
| T061 | Datastar helper tests | WP10 | P2 | No |
| T062 | OpenApiConsistencyValidator.fs | WP11 | P2 | No |
| T063 | Generate expected JSON Schema | WP11 | P2 | No |
| T064 | Field comparison logic | WP11 | P2 | No |
| T065 | CLI validate --openapi wiring | WP11 | P2 | No |
| T066 | ValidationReport formatting | WP11 | P2 | No |
| T067 | OpenAPI mismatch tests | WP11 | P2 | No |
| T068 | ExtractionStateProjector.fs | WP12 | P2 | No |
| T069 | Project OWL via TypeMapper | WP12 | P2 | No |
| T070 | Project SourceMap | WP12 | P2 | No |
| T071 | Update semantic subcommands | WP12 | P2 | No |
| T072 | Old state.json detection | WP12 | P2 | No |
| T073 | Projector comparison tests | WP12 | P2 | No |
| T074 | Minimal tic-tac-toe sample | WP13 | P1 | No |
| T075 | Add Frank.Affordances to sample | WP13 | P1 | No |
| T076 | Verify unified extraction | WP13 | P1 | No |
| T077 | Verify ALPS generation | WP13 | P1 | No |
| T078 | Verify affordance map | WP13 | P1 | No |
| T079 | Verify runtime headers per state | WP13 | P1 | No |
| T080 | Verify Datastar state-aware fragments | WP13 | P1 | No |
