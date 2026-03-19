---
work_package_id: WP08
title: Startup Projection -- Binary to In-Memory ALPS/OWL/SHACL/Schema
lane: planned
dependencies: [WP07]
subtasks:
- T045
- T046
- T047
- T048
- T049
- T050
- T051
phase: Phase 2 - Runtime
assignee: ''
agent: ''
shell_pid: ''
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-19T02:15:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-013
- FR-018
---

# Work Package Prompt: WP08 -- Startup Projection -- Binary to In-Memory ALPS/OWL/SHACL/Schema

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately (right below this notice).
- **You must address all feedback** before your work is complete. Feedback items are your implementation TODO list.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.
- **Report progress**: As you address each feedback item, update the Activity Log explaining what you changed.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes. Implementation must address every item listed below before returning for re-review.

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````python`, ````bash`

---

## Implementation Command

Depends on WP07 (affordance middleware):

```bash
spec-kitty implement WP08 --base WP07
```

---

## Objectives & Success Criteria

1. At startup, deserialize binary unified state from embedded resource into memory.
2. Project ALPS profile from `UnifiedResource` data using `UnifiedAlpsGenerator` logic from WP05.
3. Project OWL ontology using existing `TypeMapper.mapTypes`.
4. Project SHACL shapes using existing `ShapeGenerator.generateShapes`.
5. Project JSON Schema per resource using `FSharp.Data.JsonSchema.OpenApi`.
6. Serve ALPS/OWL/SHACL/JSON Schema at configured URLs via content negotiation (Accept header).
7. Pass integration tests: request profile URL with different Accept headers, verify correct content type and valid documents.

**Success**: A running application serves ALPS profile at `GET /alps/{slug}`, OWL at `GET /ontology/{slug}`, SHACL at `GET /shapes/{slug}`, and JSON Schema at `GET /schemas/{slug}`, all projected from the same embedded binary at startup with zero file I/O at request time.

---

## Context & Constraints

- **Spec**: `kitty-specs/031-unified-resource-pipeline/spec.md` -- FR-013 (binary → in-memory projections at startup), FR-018 (`Link: <profile-url>; rel="profile"`, `Link: <schema-url>; rel="describedby"`)
- **Plan**: `kitty-specs/031-unified-resource-pipeline/plan.md` -- Project Structure (`src/Frank.Affordances/StartupProjection.fs`, `src/Frank.Affordances/ProfileMiddleware.fs`)
- **Data Model**: `kitty-specs/031-unified-resource-pipeline/data-model.md` -- `UnifiedResource`, `UnifiedExtractionState`
- **Research**: `kitty-specs/031-unified-resource-pipeline/research.md` -- R1 (MessagePack serialization), R6 (FSharp.Data.JsonSchema for type mapping)
- **Existing projection modules** (pure functions, reusable without modification):
  - `TypeMapper.mapTypes`: `MappingConfig -> AnalyzedType list -> IGraph` (produces OWL ontology graph)
  - `ShapeGenerator.generateShapes`: `MappingConfig -> AnalyzedType list -> IGraph` (produces SHACL shapes graph)
  - `VocabularyAligner.alignVocabularies`: `MappingConfig -> IGraph -> IGraph` (adds Schema.org alignment)
  - `UnifiedAlpsGenerator.generate` (from WP05): `UnifiedResource -> string -> string` (produces ALPS JSON)
- **FSharp.Data.JsonSchema.OpenApi**: Used in `Frank.OpenApi` as `FSharpSchemaTransformer()`. The startup projection uses it to generate per-resource JSON Schemas for `rel="describedby"`.
- **Performance target**: Startup projection must complete in `<500ms`. All projections computed once, served from memory. Zero I/O at request time.
- **Single source of truth**: All served formats are projected from the same binary. No separate embedded files for ALPS, OWL, SHACL, or JSON Schema. This prevents format drift.

---

## Subtasks & Detailed Guidance

### Subtask T045 -- Create `StartupProjection.fs`: Deserialize Binary Unified State

- **Purpose**: At application startup, load the embedded binary resource and deserialize it into the `UnifiedExtractionState` model that all projection functions consume.
- **Steps**:
  1. Create `src/Frank.Affordances/StartupProjection.fs`
  2. Module declaration: `module Frank.Affordances.StartupProjection`
  3. Implement the deserialization:
     ```fsharp
     open System.IO
     open System.Reflection
     open MessagePack

     let private embeddedResourceName = "Frank.Affordances.unified-state.bin"

     let loadUnifiedState (assembly: Assembly) : UnifiedExtractionState option =
         use stream = assembly.GetManifestResourceStream(embeddedResourceName)
         if isNull stream then None
         else
             let options = MessagePackSerializerOptions.Standard
                 .WithResolver(
                     MessagePack.Resolvers.CompositeResolver.Create(
                         MessagePack.FSharp.FSharpResolver.Instance,
                         MessagePack.Resolvers.ContractlessStandardResolver.Instance
                     ))
             let state = MessagePackSerializer.Deserialize<UnifiedExtractionState>(stream, options)
             Some state
     ```
  4. The `UnifiedExtractionState` type definition must be shared between `Frank.Cli.Core` (which serializes it) and `Frank.Affordances` (which deserializes it). Options:
     - **Option A**: Define types in a shared `Frank.Affordances.Types` project referenced by both.
     - **Option B**: Define types in `Frank.Affordances` and have `Frank.Cli.Core` reference it.
     - **Option C**: Duplicate the types (violates Principle VIII). Rejected.
     - Recommended: **Option A** -- a minimal types-only project. Or define in `Frank.Affordances` since the CLI already depends on the affordance map schema.
  5. Handle deserialization errors (version mismatch, corrupt data) by returning `None` and logging.

- **Files**: `src/Frank.Affordances/StartupProjection.fs` (NEW, ~40-60 lines)
- **Notes**:
  - The `ContractlessStandardResolver` combined with `FSharpResolver` should handle F# records, option types, lists, and maps. Test with round-trip serialization in unit tests.
  - If the embedded resource is compressed (gzip), decompress before deserializing. The MSBuild target (WP09) determines whether compression is used.
  - The `loadUnifiedState` function is called once at startup. Performance is not critical (up to 500ms is acceptable).

### Subtask T046 -- Project ALPS Profile from Unified Data

- **Purpose**: Generate ALPS JSON for each resource at startup, store in memory for serving via content negotiation.
- **Steps**:
  1. In `StartupProjection`, after loading the unified state:
     ```fsharp
     let projectAlpsProfiles (state: UnifiedExtractionState) : Map<string, string> =
         state.Resources
         |> List.map (fun resource ->
             let slug = resource.ResourceSlug
             let alpsJson = UnifiedAlpsGenerator.generate resource state.BaseUri
             (slug, alpsJson))
         |> Map.ofList
     ```
  2. Each resource gets its own ALPS profile, keyed by slug. The slug is the URL path segment used for serving.
  3. The ALPS JSON string is stored as-is -- no further processing needed. It's served directly in response to `Accept: application/alps+json`.

- **Files**: `src/Frank.Affordances/StartupProjection.fs` (extends T045)
- **Notes**:
  - This reuses `UnifiedAlpsGenerator.generate` from WP05. The `Frank.Affordances` project must reference the module or the shared types project.
  - **Dependency concern**: `UnifiedAlpsGenerator` is in `Frank.Cli.Core`. The runtime project `Frank.Affordances` should NOT depend on `Frank.Cli.Core` (which brings in FCS, dotNetRdf, and other CLI-only dependencies). Solution: Extract the ALPS generation logic into a shared module that both projects can reference, or duplicate the pure generation logic in `Frank.Affordances` (if small enough). Alternatively, generate ALPS at CLI time and include it in the binary state, so the runtime just serves a pre-generated string.
  - **Recommended approach**: Have the CLI pre-generate ALPS JSON per resource during extraction and include it in the `UnifiedExtractionState` as a `Map<string, string>` field (`AlpsProfiles: Map<string, string>`). This way the runtime just reads pre-computed strings. No need for `Frank.Affordances` to know how to generate ALPS.

### Subtask T047 -- Project OWL Ontology from Type Data

- **Purpose**: Generate OWL/XML ontology for each resource's types at startup, store in memory for content-negotiated serving.
- **Steps**:
  1. Call `TypeMapper.mapTypes` with the resource's `TypeInfo`:
     ```fsharp
     let projectOwlOntologies (state: UnifiedExtractionState) : Map<string, string> =
         let config : TypeMapper.MappingConfig =
             { BaseUri = Uri(state.BaseUri)
               Vocabularies = state.Vocabularies }
         state.Resources
         |> List.map (fun resource ->
             let graph = TypeMapper.mapTypes config resource.TypeInfo
             let alignedGraph = VocabularyAligner.alignVocabularies config graph
             let owlXml = ExtractionState.graphToTurtle alignedGraph  // or OWL/XML serialization
             (resource.ResourceSlug, owlXml))
         |> Map.ofList
     ```
  2. **Same dependency concern as T046**: `TypeMapper` and `VocabularyAligner` are in `Frank.Cli.Core`. The runtime should not depend on the CLI.
  3. **Recommended approach**: Pre-generate OWL/XML at CLI time and include in `UnifiedExtractionState` as `OwlOntologies: Map<string, string>`. The runtime serves pre-computed strings.
  4. Alternatively, extract `TypeMapper` and `VocabularyAligner` into a shared library. But these modules depend on `dotNetRdf` (VDS.RDF), which is heavy for a runtime dependency.

- **Files**: `src/Frank.Affordances/StartupProjection.fs` (extends T045)
- **Notes**:
  - OWL can be serialized as RDF/XML, Turtle, or JSON-LD. For content negotiation, pre-generate multiple formats or use a single canonical format and convert on the fly.
  - If pre-generating at CLI time: include at least Turtle (compact) and RDF/XML (widely supported) representations.
  - The `TypeMapper.mapTypes` function is pure and deterministic. Pre-generation at CLI time is safe.

### Subtask T048 -- Project SHACL Shapes from Type Data

- **Purpose**: Generate SHACL shapes for each resource's types at startup (or pre-generate at CLI time).
- **Steps**:
  1. Call `ShapeGenerator.generateShapes` with the resource's `TypeInfo`:
     ```fsharp
     let projectShaclShapes (state: UnifiedExtractionState) : Map<string, string> =
         let config : TypeMapper.MappingConfig =
             { BaseUri = Uri(state.BaseUri)
               Vocabularies = state.Vocabularies }
         state.Resources
         |> List.map (fun resource ->
             let graph = ShapeGenerator.generateShapes config resource.TypeInfo
             let shaclTurtle = ExtractionState.graphToTurtle graph
             (resource.ResourceSlug, shaclTurtle))
         |> Map.ofList
     ```
  2. **Same dependency pattern as T047**: Pre-generate at CLI time and store in `UnifiedExtractionState.ShaclShapes: Map<string, string>`.
  3. SHACL is typically served as Turtle (standard for RDF constraint languages).

- **Files**: `src/Frank.Affordances/StartupProjection.fs` (extends T045)
- **Notes**:
  - The SHACL shapes use `urn:frank:shape:*` URIs for shape identifiers and `urn:frank:property:*` for property paths. These are the same URIs used by the CLI's validation pipeline.
  - Pre-generation avoids the need for `dotNetRdf` at runtime -- a significant dependency reduction.

### Subtask T049 -- Project JSON Schema Per Resource

- **Purpose**: Generate JSON Schema for each resource using `FSharp.Data.JsonSchema.OpenApi` for consistent type mapping.
- **Steps**:
  1. Use `FSharp.Data.JsonSchema.OpenApi.FSharpSchemaTransformer` to generate JSON Schema for each resource's primary type:
     ```fsharp
     let projectJsonSchemas (state: UnifiedExtractionState) : Map<string, string> =
         state.Resources
         |> List.map (fun resource ->
             // Find the primary response type for this resource
             let primaryType = resource.TypeInfo |> List.tryHead
             match primaryType with
             | Some t ->
                 let schema = generateJsonSchema t  // Using FSharp.Data.JsonSchema
                 (resource.ResourceSlug, schema)
             | None ->
                 (resource.ResourceSlug, """{"type":"object"}"""))
         |> Map.ofList
     ```
  2. **Dependency on FSharp.Data.JsonSchema.OpenApi**: This package IS suitable for runtime use (unlike dotNetRdf). Add it as a dependency of `Frank.Affordances` if needed, or pre-generate at CLI time.
  3. **Recommended approach**: Pre-generate JSON Schema at CLI time and include in `UnifiedExtractionState.JsonSchemas: Map<string, string>`. This keeps the runtime dependency footprint minimal and ensures the schema matches exactly what OpenAPI serves.
  4. The generated JSON Schema is served at `GET /schemas/{slug}` with `Content-Type: application/schema+json`.

- **Files**: `src/Frank.Affordances/StartupProjection.fs` (extends T045)
- **Notes**:
  - FR-024a requires using `FSharp.Data.JsonSchema.OpenApi` as the canonical type mapping. This ensures JSON Schema served via `rel="describedby"` uses the same type encoding as OpenAPI schemas.
  - For DU types, `FSharpSchemaTransformer` produces `oneOf` with discriminator. This is the correct JSON Schema representation.
  - The `AnalyzedType` from the data model contains `Kind` (Record/DU/Enum) but not the FSharp.Compiler.Service type information. If `FSharp.Data.JsonSchema.OpenApi` requires `System.Type` or `FSharpType`, the schema must be pre-generated at CLI time when FCS context is available.

### Subtask T050 -- Create `ProfileMiddleware.fs`: Serve Formats via Content Negotiation

- **Purpose**: Create middleware that serves ALPS, OWL, SHACL, and JSON Schema at configured URL patterns based on the `Accept` header.
- **Steps**:
  1. Create `src/Frank.Affordances/ProfileMiddleware.fs`
  2. Define URL patterns and content types:
     ```fsharp
     // URL patterns:
     // GET /alps/{slug}     → ALPS profile
     // GET /ontology/{slug} → OWL ontology
     // GET /shapes/{slug}   → SHACL shapes
     // GET /schemas/{slug}  → JSON Schema
     ```
  3. Content negotiation based on Accept header:
     | Accept Header | Served Format | Content-Type |
     |---------------|---------------|-------------|
     | `application/alps+json` | ALPS JSON | `application/alps+json` |
     | `application/alps+xml` | ALPS XML | `application/alps+xml` |
     | `application/rdf+xml` | OWL RDF/XML | `application/rdf+xml` |
     | `text/turtle` | OWL/SHACL Turtle | `text/turtle` |
     | `application/schema+json` | JSON Schema | `application/schema+json` |
     | `application/json` (default) | ALPS JSON / JSON Schema | `application/json` |
  4. Implementation approach -- use endpoint routing:
     ```fsharp
     [<AutoOpen>]
     module ProfileMiddlewareExtensions =
         type IEndpointRouteBuilder with
             member endpoints.MapProfiles(profiles: ProjectedProfiles) =
                 // ALPS endpoints
                 for slug, alpsJson in Map.toSeq profiles.AlpsProfiles do
                     endpoints.MapGet(sprintf "/alps/%s" slug, fun (ctx: HttpContext) ->
                         ctx.Response.ContentType <- "application/alps+json"
                         ctx.Response.WriteAsync(alpsJson)
                     ) |> ignore
                 // OWL endpoints
                 for slug, owlXml in Map.toSeq profiles.OwlOntologies do
                     endpoints.MapGet(sprintf "/ontology/%s" slug, fun (ctx: HttpContext) ->
                         ctx.Response.ContentType <- "text/turtle"
                         ctx.Response.WriteAsync(owlXml)
                     ) |> ignore
                 // SHACL endpoints
                 for slug, shaclTurtle in Map.toSeq profiles.ShaclShapes do
                     endpoints.MapGet(sprintf "/shapes/%s" slug, fun (ctx: HttpContext) ->
                         ctx.Response.ContentType <- "text/turtle"
                         ctx.Response.WriteAsync(shaclTurtle)
                     ) |> ignore
                 // JSON Schema endpoints
                 for slug, jsonSchema in Map.toSeq profiles.JsonSchemas do
                     endpoints.MapGet(sprintf "/schemas/%s" slug, fun (ctx: HttpContext) ->
                         ctx.Response.ContentType <- "application/schema+json"
                         ctx.Response.WriteAsync(jsonSchema)
                     ) |> ignore
     ```
  5. Combine projected profiles into a single type:
     ```fsharp
     type ProjectedProfiles =
         { AlpsProfiles: Map<string, string>
           OwlOntologies: Map<string, string>
           ShaclShapes: Map<string, string>
           JsonSchemas: Map<string, string> }
     ```

- **Files**: `src/Frank.Affordances/ProfileMiddleware.fs` (NEW, ~80-120 lines)
- **Notes**:
  - Use `IEndpointRouteBuilder.MapGet` (endpoint routing) rather than middleware. This plays better with OpenAPI documentation and route matching.
  - Pre-computed strings are served directly -- no per-request computation.
  - For content negotiation on a single endpoint (e.g., `/alps/{slug}` serving both JSON and XML), check the `Accept` header and select the appropriate pre-generated format.
  - If only one format is pre-generated (e.g., only ALPS JSON, not ALPS XML), serve that format regardless of Accept header and set the Content-Type accordingly. Add a `Vary: Accept` header.
  - Consider a `MapProfileDiscovery` endpoint at `GET /.well-known/frank-profiles` that lists all available profile URLs for discoverability.

### Subtask T051 -- Integration Tests: Content Negotiation

- **Purpose**: Verify that requesting profile URLs with different Accept headers returns correct content types and valid documents.
- **Steps**:
  1. Create test file `test/Frank.Affordances.Tests/ProfileMiddlewareTests.fs`.
  2. Build a TestHost with `MapProfiles` registered using fixture data:
     ```fsharp
     let profiles : ProjectedProfiles =
         { AlpsProfiles = Map.ofList [("games", alpsJsonFixture)]
           OwlOntologies = Map.ofList [("games", owlTurtleFixture)]
           ShaclShapes = Map.ofList [("games", shaclTurtleFixture)]
           JsonSchemas = Map.ofList [("games", jsonSchemaFixture)] }

     let host = WebHostBuilder()
         .Configure(fun app ->
             app.UseRouting() |> ignore
             app.UseEndpoints(fun ep -> ep.MapProfiles(profiles)) |> ignore
         )
         .Build()
     ```
  3. Test cases:
     - **ALPS JSON**: `GET /alps/games` with `Accept: application/alps+json` -> 200, `Content-Type: application/alps+json`, body is valid ALPS JSON.
     - **OWL Turtle**: `GET /ontology/games` with `Accept: text/turtle` -> 200, `Content-Type: text/turtle`, body is valid Turtle.
     - **SHACL Turtle**: `GET /shapes/games` with `Accept: text/turtle` -> 200, `Content-Type: text/turtle`, body is valid Turtle.
     - **JSON Schema**: `GET /schemas/games` with `Accept: application/schema+json` -> 200, `Content-Type: application/schema+json`, body is valid JSON Schema.
     - **Unknown slug**: `GET /alps/nonexistent` -> 404.
     - **Default Accept**: `GET /alps/games` with `Accept: */*` -> 200 with ALPS JSON (default).
  4. Validate document content:
     - ALPS JSON: parse with `JsonDocument`, verify `alps.version` is `"1.0"` and descriptors are present.
     - JSON Schema: parse with `JsonDocument`, verify `type` property exists.
     - Turtle: check that the content starts with `@prefix` or contains RDF triples (basic structural check without dotNetRdf dependency in tests).

- **Files**: `test/Frank.Affordances.Tests/ProfileMiddlewareTests.fs` (NEW, ~120-180 lines)
- **Notes**:
  - The test fixtures (ALPS JSON, OWL Turtle, etc.) can be hardcoded strings or generated from fixture `UnifiedResource` data at test time.
  - If pre-generating formats at CLI time (recommended approach from T046-T049), the test just verifies that the middleware serves the pre-generated strings correctly. The format correctness is validated in WP05 and CLI tests.
  - Content negotiation testing: verify that the `Vary: Accept` header is set on negotiated responses.

---

## Test Strategy

- **Unit tests**: Test `loadUnifiedState` with a MessagePack-serialized byte array. Test format projection functions in isolation.
- **Integration tests**: TestHost-based tests (T051) verifying content negotiation and correct responses.
- **Round-trip tests**: Serialize `UnifiedExtractionState` → binary → deserialize → project ALPS → parse ALPS JSON → verify structure.

Run tests:
```bash
dotnet test test/Frank.Affordances.Tests/ --filter "Profile"
```

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| `Frank.Affordances` depending on `Frank.Cli.Core` for projection functions | Pre-generate all formats at CLI time and store in `UnifiedExtractionState`. Runtime only deserializes and serves pre-computed strings. |
| dotNetRdf dependency at runtime for OWL/SHACL | Pre-generate Turtle/RDF-XML at CLI time. Runtime does not need dotNetRdf. |
| `FSharp.Data.JsonSchema.OpenApi` requiring FCS types for schema generation | Generate JSON Schema at CLI time when FCS context is available. Include generated schema strings in the binary state. |
| Startup projection exceeding 500ms budget | Pre-generation at CLI time means startup only deserializes binary + builds a few `Map` lookups from pre-computed strings. Should be `<50ms`. |
| Content negotiation complexity | Start with single-format endpoints (ALPS=JSON, OWL=Turtle, SHACL=Turtle, Schema=JSON). Add multi-format negotiation in a follow-up if needed. |

---

## Review Guidance

- Verify no dependency from `Frank.Affordances` to `Frank.Cli.Core` or `dotNetRdf`.
- Verify all served formats are pre-generated strings loaded from binary, not computed at request time.
- Verify content types match IANA media type registrations.
- Verify 404 for unknown slugs (not 500 or empty 200).
- Verify startup projection completes within 500ms budget.
- Verify `Link: <url>; rel="profile"` and `Link: <url>; rel="describedby"` URLs in the affordance middleware (WP07) match the served profile endpoints.
- Verify `dotnet build` and `dotnet test` pass cleanly.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

### How to Add Activity Log Entries

**When adding an entry**:
1. Scroll to the bottom of this file (Activity Log section below "Valid lanes")
2. **APPEND the new entry at the END** (do NOT prepend or insert in middle)
3. Use exact format: `- YYYY-MM-DDTHH:MM:SSZ -- agent_id -- lane=<lane> -- <action>`
4. Timestamp MUST be current time in UTC (check with `date -u "+%Y-%m-%dT%H:%M:%SZ"`)
5. Lane MUST match the frontmatter `lane:` field exactly
6. Agent ID should identify who made the change (claude-sonnet-4-5, codex, etc.)

**Format**:
```
- YYYY-MM-DDTHH:MM:SSZ -- <agent_id> -- lane=<lane> -- <brief action description>
```

**Valid lanes**: `planned`, `doing`, `for_review`, `done`

**Initial entry**:
- 2026-03-19T02:15:00Z -- system -- lane=planned -- Prompt created.
