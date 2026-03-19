# Feature Specification: Unified Resource Pipeline

**Feature Branch**: `031-unified-resource-pipeline`
**Created**: 2026-03-18
**Status**: Draft
**GitHub Issue**: TBD
**Dependencies**: #94 (CLI statechart commands, spec 026), spec 020 (canonical AST), spec 021 (cross-format validator), spec 025 (validation pipeline), Frank.OpenApi
**Parent issue**: #57 (statechart spec pipeline)
**Location**: `src/Frank.Cli.Core/` (unified extraction), `src/Frank.Affordances/` (runtime library), `src/Frank.Datastar/` (affordance-driven fragments)

## Background

Frank's CLI currently has two independent extraction pipelines that analyze the same F# project but produce separate, unconnected outputs:

1. **Semantic pipeline** (`frank-cli semantic extract`): Uses FCS to analyze F# types and routes, producing OWL ontology (classes, properties), SHACL shapes (validation constraints), and vocabulary alignments (Schema.org). Output persisted as `ExtractionState` in `obj/frank-cli/state.json`.

2. **Statechart pipeline** (`frank-cli statechart extract`): Uses FCS to analyze `statefulResource` CEs, producing `ExtractedStatechart` records with state DU case names, HTTP methods per state, initial state, and guard names. Output consumed by format generators (WSD, ALPS, SCXML, smcat, XState).

Both pipelines call `ProjectLoader.loadProject` independently, performing redundant FCS typechecks. More fundamentally, they model the same resources from different angles — type structure vs. behavioral semantics — without connecting the two views. A `statefulResource "/games/{gameId}"` that uses `TicTacToeState` has both type structure (the DU cases, their fields) and behavioral semantics (state transitions, guards, HTTP methods per state), but no CLI command produces a description that carries both.

This separation creates three gaps identified by the expert review panel:

- **Harel's gap**: Structure and behavior are two views of the same resource, but the CLI treats them as unrelated extractions. State names from the statechart don't connect to DU case fields from the type analysis.
- **Fielding's gap**: The CLI generates design-time artifacts (ALPS profiles, OWL ontology) but nothing that helps produce runtime responses with embedded hypermedia controls reflecting current state.
- **Miller's gap**: The CLI's type descriptions (OWL) and the project's OpenAPI schema (from `Frank.OpenApi`) can diverge unchecked — two descriptions of the same API with no consistency validation.

## Clarifications

### Session 2026-03-18

- Q: How is the affordance map loaded into the running application at runtime? → A: Embedded resource via MSBuild target. The CLI generates `obj/frank-cli/affordance-map.json`; an MSBuild target auto-embeds it as `EmbeddedResource` with logical name `Frank.Affordances.affordance-map.json`. The middleware loads it via `Assembly.GetManifestResourceStream()` at startup and parses it once into a pre-computed dictionary lookup. Same pattern as existing semantic artifacts (`Frank.Semantic.ontology.owl.xml`). No resx — use `<EmbeddedResource>` directly.
- Q: What link relation vocabulary do affordance Link headers use? → A: ALPS-derived relations with IANA precedence. Precedence: (1) IANA-registered relation if transition maps to one (self, edit, collection, etc.); (2) ALPS profile fragment URI for domain-specific transitions (e.g., `https://example.com/alps/games#move`); (3) never bare HTTP methods. Responses also include `Link: <profile-url>; rel="profile"` so agents can discover relation type definitions. The CLI pre-computes relation types into the affordance map using `--base-uri` as the ALPS profile namespace. No CE changes needed — relation types are auto-derived from (state, method, base URI). CE extensions deferred unless validation reveals the need.
- Q: How do existing semantic subcommands (clarify, validate, compile, diff) read from the new unified state format? → A: Subcommands are updated to read `UnifiedExtractionState` via a projection function (`toExtractionState`) that calls existing pure functions (TypeMapper, ShapeGenerator) on the unified model's type data to produce OWL/SHACL graphs. One state file (`unified-state.bin`), one format, one source of truth. Old `state.json` format detected at load time and prompts re-extraction. No dual-file writing.
- Q: What serialization format for the cache and embedded resource? → A: Binary (MemoryPack, or MessagePack as fallback for F# DU compatibility). Both the `obj/frank-cli/unified-state.bin` cache and the assembly-embedded resource use the same binary format. JSON is only for CLI display output (`--output-format json`). Human-readable served formats (ALPS, OWL/XML, SHACL Turtle) are NOT embedded as separate files — they are projected at startup from the deserialized binary unified state and served from memory via content negotiation. One embedded binary artifact, zero static files in deployment.
- Q: Should served formats (ALPS, OWL, SHACL) be embedded files or computed at startup? → A: Computed at startup from the single embedded binary unified state. The middleware deserializes the binary, projects ALPS/OWL/SHACL into memory, and serves via content negotiation. No static files, no generated files in deployment. Same projection functions the CLI uses. Single source of truth prevents drift between formats.

## User Scenarios & Testing

### User Story 1 - Unified Extraction from a Single Project (Priority: P1)

A developer runs `frank-cli extract --project MyApp/MyApp.fsproj` and receives a unified description of all resources in their application. For each resource, the output includes type structure (F# record/DU fields mapped to properties), behavioral semantics (statechart states, transitions, HTTP methods per state), and route information. Plain `resource` CEs produce type-only descriptions; `statefulResource` CEs produce type + behavior descriptions. The extraction runs FCS once and caches the result.

**Why this priority**: This replaces both `semantic extract` and `statechart extract` with a single command. It's the foundation everything else builds on.

**Independent Test**: Run unified extraction against the tic-tac-toe sample. Verify the output contains both type information (`TicTacToeState` DU cases with fields, `TicTacToeEvent`) and behavioral information (states XTurn/OTurn/Won/Draw, initial state XTurn, HTTP methods GET/POST per state, guard names). Verify a plain `resource` in the same project produces type info without behavioral data.

**Acceptance Scenarios**:

1. **Given** an F# project with a `statefulResource "/games/{gameId}"` using `TicTacToeState`, **When** `frank-cli extract --project <fsproj>` is invoked, **Then** the output contains a unified resource entry with route template `/games/{gameId}`, type structure (4 DU cases: XTurn, OTurn, Won, Draw), behavioral semantics (4 states with HTTP methods), and initial state `XTurn`.
2. **Given** an F# project with both a `resource "/health"` and a `statefulResource "/games/{gameId}"`, **When** unified extraction runs, **Then** `/health` has type structure but no behavioral semantics, and `/games/{gameId}` has both.
3. **Given** a project that was previously extracted, **When** the developer runs extraction again without changing source, **Then** the cached result is returned without re-running FCS typecheck (staleness detection via source hash).
4. **Given** `frank-cli extract --project <fsproj> --output-format json`, **When** the output is parsed, **Then** each resource entry contains `typeInfo`, `statechart` (nullable), `route`, and `derivedFields` (orphan states, state-to-type mappings).
5. **Given** a `statefulResource` where a state DU case name does not appear in any `inState` call, **When** extraction runs, **Then** the derived `orphanStates` field lists the unhandled case name as a warning.

---

### User Story 2 - ALPS Generation from Unified Model (Priority: P1)

A developer runs `frank-cli generate --project <fsproj> --format alps` and receives an ALPS profile that combines type descriptors (from OWL class/property analysis) with state transition descriptors (from statechart analysis) in a single document. The ALPS profile carries both what a resource *is* (data descriptors with Schema.org alignment) and what it *can do* (safe/unsafe/idempotent transitions per state).

**Why this priority**: ALPS is the convergence format — vocabulary + behavior in one profile. This realizes Amundsen's vision and provides the machine-readable description agents consume.

**Independent Test**: Generate ALPS for the tic-tac-toe resource. Verify the ALPS document contains data descriptors for `board`, `currentTurn`, `winner` (if applicable) AND transition descriptors for GET (safe) and POST (unsafe) mapped to states. Verify the document is valid ALPS JSON parseable by the ALPS parser.

**Acceptance Scenarios**:

1. **Given** a unified extraction of a tic-tac-toe `statefulResource`, **When** `frank-cli generate --format alps` is invoked, **Then** the ALPS document contains both `semantic` descriptors for type properties and `safe`/`unsafe` descriptors for HTTP method transitions.
2. **Given** a plain `resource` with no statechart, **When** ALPS is generated, **Then** the ALPS document contains `semantic` descriptors for type properties and `safe`/`unsafe` descriptors for HTTP method capabilities (GET, POST, etc.) without state-dependent transitions.
3. **Given** the generated ALPS document, **When** it is parsed by the ALPS JSON parser, **Then** parsing succeeds with no errors.

---

### User Story 3 - Affordance Map Generation (Priority: P1)

A developer runs `frank-cli generate --project <fsproj> --format affordance-map` and receives a machine-readable JSON document mapping `(routeTemplate, stateName)` pairs to available actions (HTTP methods, link relations, transition targets). For plain resources without statecharts, the map contains a single entry with all available methods. The affordance map is the artifact consumed by the runtime middleware and Datastar handlers.

**Why this priority**: The affordance map is the bridge between compile-time extraction and runtime behavior. Without it, the runtime library has nothing to consume.

**Independent Test**: Generate an affordance map for the tic-tac-toe resource. Verify it contains entries for each state (XTurn, OTurn, Won, Draw) with the correct HTTP methods per state. Parse the map JSON and verify it matches the expected schema.

**Acceptance Scenarios**:

1. **Given** a tic-tac-toe `statefulResource`, **When** the affordance map is generated, **Then** it contains entries: `(games, XTurn) → {GET, POST}`, `(games, OTurn) → {GET, POST}`, `(games, Won) → {GET}`, `(games, Draw) → {GET}`.
2. **Given** a plain `resource "/health"` with GET only, **When** the affordance map is generated, **Then** it contains one entry: `(health, *) → {GET}` (where `*` indicates no state dependency).
3. **Given** the generated affordance map JSON, **When** it is loaded by the runtime middleware, **Then** it can be indexed by `(routeTemplate, stateKey)` and returns the available methods and link relations for that state.

---

### User Story 4 - OpenAPI Consistency Validation (Priority: P2)

A developer runs `frank-cli validate --project <fsproj> --openapi` and receives a report comparing the unified extraction's type descriptions against the OpenAPI schema that `Frank.OpenApi` would generate at runtime. Discrepancies (missing properties, type mismatches, route differences) are reported as warnings or errors.

**Why this priority**: Prevents drift between the compile-time unified model and the runtime OpenAPI description. Important for correctness but not blocking for the core extraction and generation workflow.

**Independent Test**: Create a project where the F# types include a field not exposed in the OpenAPI schema (or vice versa). Run validation and verify the discrepancy is reported.

**Acceptance Scenarios**:

1. **Given** a project where all F# types match the OpenAPI schema exactly, **When** `frank-cli validate --openapi` is invoked, **Then** the report shows all checks passed.
2. **Given** a project where the F# type `Game` has a field `internalState` that is not exposed in the OpenAPI schema, **When** validation runs, **Then** the report contains a warning identifying the unmapped field.
3. **Given** a project where the OpenAPI schema defines a property `score` that doesn't correspond to any F# field, **When** validation runs, **Then** the report contains a warning identifying the orphan schema property.
4. **Given** `--output-format json`, **When** the validation report is parsed, **Then** it follows the same `ValidationReport` structure used by statechart validation.

---

### User Story 5 - Runtime Affordance Middleware (Priority: P1)

A developer adds `app.UseAffordances()` to their Frank application's middleware pipeline. At startup, the middleware loads the affordance map (generated by the CLI or embedded as a resource). At request time, for each response from a stateful resource, the middleware injects `Link` headers describing available state transitions and sets the `Allow` header based on the current state's permitted HTTP methods. The middleware reads the current state from `StateMachineMetadata` (already resolved by the statechart middleware) and looks up the affordance map entry.

**Why this priority**: This closes Fielding's HATEOAS gap. Without runtime affordance projection, the CLI generates design-time artifacts but responses remain affordance-free.

**Independent Test**: Build the tic-tac-toe app with affordance middleware. Send a GET request when the game is in state `XTurn`. Verify the response includes `Allow: GET, POST` and `Link` headers describing the POST transition. Send GET when in state `Won`. Verify `Allow: GET` only and no POST link.

**Acceptance Scenarios**:

1. **Given** a tic-tac-toe app with affordance middleware and the game in state `XTurn`, **When** a GET request is made, **Then** the response includes `Allow: GET, POST`, a `Link` header with `rel="https://example.com/alps/games#move"` pointing to the POST action, and `Link: <https://example.com/alps/games>; rel="profile"`.
2. **Given** the game in state `Won`, **When** a GET request is made, **Then** the response includes `Allow: GET` only and no transition links.
3. **Given** a plain `resource` (no statechart), **When** a request is made, **Then** the middleware passes through without modification (no affordance injection for non-stateful resources).
4. **Given** an application without an affordance map loaded, **When** the middleware is registered, **Then** it logs a warning at startup and passes all requests through without modification (graceful degradation).
5. **Given** the affordance map is pre-loaded at startup, **When** requests are served under load, **Then** the middleware adds zero allocations per request beyond the Link header string construction (pre-computed lookup from map).

---

### User Story 6 - Datastar Affordance-Driven Fragments (Priority: P2)

A developer building a Datastar-powered UI uses the affordance map to determine which HTML controls to stream per state. When the game is in state `XTurn`, the SSE stream includes a "Make Move" button fragment. When in state `Won`, the button fragment is replaced with a "Game Over" display. The affordance map drives which fragments are active — the developer writes state-aware handlers that consult the map rather than hardcoding state checks.

**Why this priority**: Connects the affordance map to the Datastar streaming model. Important for the tic-tac-toe reference app but builds on the affordance map (P1) and Datastar integration (already shipped).

**Independent Test**: Build the tic-tac-toe Datastar app. Connect via SSE. Make moves until the game reaches `Won` state. Verify the streamed fragments transition from interactive controls to read-only display based on the affordance map's available methods.

**Acceptance Scenarios**:

1. **Given** a Datastar tic-tac-toe app in state `XTurn`, **When** SSE fragments are streamed, **Then** the response includes interactive controls (POST form for making a move) because the affordance map shows POST is available.
2. **Given** the game transitions to state `Won`, **When** the next SSE fragment is streamed, **Then** the response includes read-only display (no POST form) because the affordance map shows only GET is available.
3. **Given** a helper function `affordancesFor(routeTemplate, stateKey, affordanceMap)`, **When** called in a Datastar handler, **Then** it returns the available methods and link relations for the current state, usable to conditionally render controls.

---

### User Story 7 - FCS Analysis Caching (Priority: P2)

A developer runs `frank-cli extract`, then `frank-cli generate`, then `frank-cli validate` in sequence. The first command performs the full FCS typecheck and caches the unified extraction state (including source hash). Subsequent commands detect the cache is fresh and skip FCS analysis, operating directly on the cached unified model.

**Why this priority**: FCS typecheck can take 5-30 seconds for large projects. Running it three times in a typical workflow (extract → generate → validate) is unacceptable. Caching makes the pipeline practical.

**Independent Test**: Run extraction on a project, note the time. Run generation immediately after without changing source. Verify the second command completes in under 1 second by reading from cache.

**Acceptance Scenarios**:

1. **Given** a project that has been extracted, **When** `frank-cli generate` is run without source changes, **Then** the command reads from the cached unified state and completes without FCS analysis.
2. **Given** a cached extraction where the developer modifies a source file, **When** `frank-cli generate` is run, **Then** the command detects staleness (source hash mismatch), re-runs FCS analysis, and updates the cache.
3. **Given** `frank-cli extract --force`, **When** invoked, **Then** the command ignores the cache and performs a fresh FCS analysis regardless of staleness.
4. **Given** the cache file does not exist, **When** any command that needs the unified model is run, **Then** it performs FCS analysis automatically and creates the cache.

---

### Edge Cases

- A `statefulResource` CE whose machine binding is defined in a different file or project (cross-file resolution via FCS typed AST)
- A `statefulResource` that uses a state DU with generic type parameters (e.g., `Result<GameState, Error>`) — the extractor should report the concrete type, not the generic wrapper
- A project with compilation errors — the unified extraction should report FCS diagnostics clearly and fail fast
- An affordance map referencing a state that no longer exists (code changed after map generation) — the runtime middleware should log a warning and serve without affordances for that state
- A project with 50+ resources — extraction and map generation should complete within 30 seconds
- ALPS generation from a resource with no type information (only route + methods) should produce a valid but minimal ALPS document
- The affordance map format should be stable across Frank versions to allow independent CLI and runtime upgrades
- Datastar fragment projection when the affordance map is not loaded — should render all controls (permissive default), not hide everything (restrictive default)

## Requirements

### Functional Requirements

**Unified Extraction**

- **FR-001**: System MUST provide a `frank-cli extract --project <fsproj>` command that replaces both `semantic extract` and `statechart extract`, performing a single FCS typecheck and producing a unified resource model
- **FR-002**: System MUST walk the FCS typed AST to identify both `resource` and `statefulResource` CE invocations, extracting type information (record fields, DU cases, property types) and behavioral information (states, HTTP methods, initial state, guards) in one pass
- **FR-003**: For each resource, the unified model MUST include: route template, type structure (analyzed types with OWL-mappable properties), behavioral semantics (statechart data, nullable for plain resources), HTTP capabilities (methods per state or per resource), and derived fields (orphan states, state-to-type structure mapping)
- **FR-004**: The unified model MUST compute derived fields that enforce structure-behavior invariants: state names that don't correspond to DU cases, DU cases not covered by `inState` calls, and per-state field accessibility (which type fields are relevant in each state)
- **FR-005**: System MUST persist the unified extraction state to a cache file with a source hash for staleness detection, so subsequent commands skip FCS analysis when source is unchanged
- **FR-006**: System MUST support `--output-format text|json` and `--force` (bypass cache) options

**Unified Generation**

- **FR-007**: System MUST support generating all existing formats (WSD, ALPS, ALPS XML, SCXML, smcat, XState JSON) from the unified model, replacing the current statechart-only generation
- **FR-008**: ALPS generation from the unified model MUST produce profiles containing both `semantic` descriptors (type properties with Schema.org alignment) and `safe`/`unsafe`/`idempotent` transition descriptors (from statechart behavior)
- **FR-009**: System MUST support a new `--format affordance-map` option that generates the machine-readable affordance map JSON

**Affordance Map**

- **FR-010**: The affordance map MUST be a JSON document keyed by `(routeTemplate, stateKey)` pairs, where each entry specifies: available HTTP methods, link relations, and transition target states
- **FR-011**: For plain resources without statecharts, the affordance map MUST use a wildcard state key indicating the methods are always available
- **FR-012**: The affordance map format MUST be versioned to support independent CLI and runtime library upgrades
- **FR-013**: The unified state MUST be serialized to a binary format (MemoryPack or MessagePack) and auto-embedded into the application assembly via an MSBuild target as a single `EmbeddedResource`. At startup, the middleware deserializes the binary into memory and projects all runtime views (affordance map lookup, ALPS profile, OWL/SHACL graphs) from it. No separate embedded files for each format

**OpenAPI Consistency**

- **FR-014**: System MUST provide a `frank-cli validate --project <fsproj> --openapi` command that compares the unified model's type descriptions against the OpenAPI schema the project would produce at runtime
- **FR-015**: Validation MUST report: unmapped F# fields (in unified model but not in OpenAPI), orphan OpenAPI properties (in schema but not in unified model), type mismatches (different property types), and route discrepancies
- **FR-016**: The unified model is authoritative — discrepancies are reported as drift from the code, not as errors in the code

**Runtime Affordance Middleware**

- **FR-017**: System MUST provide an `app.UseAffordances()` middleware extension that loads the affordance map at startup and injects affordance headers into responses for stateful resources
- **FR-018**: The middleware MUST inject `Allow` headers reflecting the current state's permitted HTTP methods, `Link` headers describing available transitions using IANA-registered relations where applicable and ALPS profile fragment URIs for domain-specific transitions, and a `Link: <profile-url>; rel="profile"` header pointing to the ALPS profile
- **FR-019**: The middleware MUST read the current state key from the statechart middleware's resolution (already in `HttpContext.Items`) and look up the affordance map entry
- **FR-020**: The middleware MUST add zero per-request allocations beyond Link header string construction — all map lookups MUST be pre-computed at startup
- **FR-021**: The middleware MUST degrade gracefully when no affordance map is loaded (log warning at startup, pass requests through unmodified)

**Datastar Affordance-Driven Fragments**

- **FR-022**: System MUST provide a helper function `affordancesFor(routeTemplate, stateKey, affordanceMap)` that returns the available methods and link relations for a given state, usable in Datastar SSE handlers
- **FR-023**: The helper MUST enable Datastar handlers to conditionally render HTML controls based on state-dependent affordances (e.g., show/hide action buttons)
- **FR-024**: When the affordance map is not loaded, the helper MUST return a permissive default (all methods available) rather than hiding controls

**FCS Caching**

- **FR-025**: System MUST cache the unified extraction state (including full unified model and source hash) to `obj/frank-cli/unified-state.bin` using the same binary format as the embedded resource
- **FR-026**: All commands that consume the unified model (generate, validate) MUST check the cache first and skip FCS analysis when the source hash matches
- **FR-027**: System MUST support a `--force` flag to bypass cache and re-run FCS analysis
- **FR-028**: Cache format MUST be forward-compatible — newer CLI versions MUST be able to read caches produced by older versions (or detect incompatibility and re-extract)

**CLI Structure**

- **FR-029**: The unified `extract` command MUST replace both `semantic extract` and `statechart extract` as a single top-level command: `frank-cli extract --project <fsproj>`
- **FR-030**: Existing `semantic` subcommands (clarify, validate, compile, diff) MUST continue to function by reading from the unified extraction state via a projection function that produces the `ExtractionState` fields they expect (OWL graphs, SHACL shapes, source map, metadata). If an old-format `state.json` is detected, the CLI MUST prompt the user to re-extract
- **FR-031**: The `statechart` subcommands (generate, validate, parse) MUST continue to function, reading behavioral data from the unified extraction state
- **FR-032**: The `generate` command MUST support all existing formats plus `affordance-map`

### Key Entities

- **UnifiedResource**: A combined description of a single HTTP resource, containing route template, type structure (analyzed types), behavioral semantics (statechart, nullable), HTTP capabilities, and derived fields (orphan states, state-to-type mappings). Produced by unified extraction.
- **AffordanceMap**: A machine-readable JSON document mapping `(routeTemplate, stateKey)` pairs to available actions (HTTP methods, link relations, transition targets). Produced by the CLI, consumed at runtime.
- **UnifiedExtractionState**: The persisted cache of the entire unified extraction, including all `UnifiedResource` records, source hash, and metadata. Replaces separate semantic `ExtractionState` and statechart outputs.
- **AffordanceMiddleware**: ASP.NET Core middleware that loads an `AffordanceMap` at startup and injects `Allow` and `Link` headers into responses based on current state.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A developer can run `frank-cli extract --project <fsproj>` and receive a unified description of all resources (type + behavior + routes) in a single command, completing within 15 seconds for a project with up to 20 resources
- **SC-002**: The tic-tac-toe sample application produces a unified extraction containing both type structure (4 DU cases with fields) and behavioral semantics (4 states with correct HTTP methods per state), verified by automated test
- **SC-003**: ALPS generation from the unified model produces a single document containing both type descriptors and state transition descriptors, parseable by the ALPS parser with zero errors
- **SC-004**: Sequential CLI commands (extract → generate → validate) complete the second and third commands in under 1 second when source is unchanged, by reading from cache
- **SC-005**: The runtime affordance middleware injects correct `Allow` and `Link` headers that change based on the resource's current state, verified by integration tests against the tic-tac-toe app
- **SC-006**: A Datastar handler using `affordancesFor()` renders interactive controls in state `XTurn` and read-only display in state `Won`, verified by the tic-tac-toe Datastar sample
- **SC-007**: OpenAPI consistency validation detects intentionally introduced type discrepancies (added/removed fields) with zero false negatives
- **SC-008**: The affordance map format is stable — a map generated by the CLI can be loaded by the runtime middleware across minor version bumps without regeneration

## Assumptions

- The existing `ProjectLoader.loadProject` infrastructure is sufficient for the unified extraction — no changes to Ionide.ProjInfo or FCS integration are needed
- The existing semantic extraction modules (`TypeMapper`, `RouteMapper`, `VocabularyAligner`, `ShapeGenerator`) can be called both from the unified extractor and from the `toExtractionState` projector without modification — they are pure functions operating on `AnalyzedType` lists
- The existing statechart `StatechartSourceExtractor` module can be called from the unified extractor without modification — it operates on `LoadedProject` which the unified extractor produces
- The `StateMachineMetadata` on endpoints (resolved by the statechart middleware) provides the current state key in `HttpContext.Items`, accessible to the affordance middleware
- `Frank.OpenApi` generates OpenAPI schemas at runtime from endpoint metadata; the CLI can produce an equivalent schema from the unified model for comparison
- The tic-tac-toe sample application in `test/Frank.Statecharts.Tests/` or a new sample project will serve as the validation target
- The affordance map JSON format will be designed for forward compatibility using a version field and additive-only schema evolution
- Datastar affordance-driven fragments are a composable helper, not a new Datastar CE or middleware — developers call the helper in their existing SSE handlers
