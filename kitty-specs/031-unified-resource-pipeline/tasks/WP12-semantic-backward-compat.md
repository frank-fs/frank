---
work_package_id: WP12
title: Semantic Subcommand Backward Compatibility
lane: "doing"
dependencies:
- WP02
base_branch: 031-unified-resource-pipeline-WP02
base_commit: 35175b217c2cce39589a312a0e8317e273430068
created_at: '2026-03-19T04:06:49.968743+00:00'
subtasks:
- T068
- T069
- T070
- T071
- T072
- T073
phase: Phase 3 - Integration
assignee: ''
agent: "claude-opus"
shell_pid: "29237"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-19T02:15:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-030
---

# Work Package Prompt: WP12 -- Semantic Subcommand Backward Compatibility

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
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
Use language identifiers in code blocks: ````fsharp`, ````json`

---

## Implementation Command

Depends on WP02 (unified model types) and WP03 (unified extraction).

```bash
spec-kitty implement WP12 --base WP02
```

---

## Objectives & Success Criteria

1. Create `ExtractionStateProjector.fs` with a `toExtractionState` function that converts `UnifiedExtractionState` to the existing `ExtractionState` type, enabling backward compatibility for semantic subcommands.
2. The projector calls existing pure functions (`TypeMapper.mapTypes`, `ShapeGenerator.generateShapes`) on the unified model's `TypeInfo` data to produce OWL/SHACL graphs.
3. The projector builds `ExtractionState.SourceMap` from `UnifiedResource` route/type location data.
4. Update `ClarifyCommand`, `ValidateCommand` (semantic), `CompileCommand`, and `DiffCommand` to read from unified state via the projector.
5. Detect old-format `state.json` at load time and print a message directing users to re-extract with `frank-cli extract --project`.
6. Write tests verifying the projector produces equivalent OWL/SHACL graphs to what the old semantic extract would have produced.

**Success**: After this WP, all four semantic subcommands (`clarify`, `validate`, `compile`, `diff`) work identically to before but read their data from the unified extraction state. Users running old-format `state.json` get a clear migration message.

---

## Context & Constraints

- **Spec**: `kitty-specs/031-unified-resource-pipeline/spec.md` -- FR-030 (semantic subcommands read from unified state via projector), clarification about `toExtractionState` projection
- **Plan**: `kitty-specs/031-unified-resource-pipeline/plan.md` -- `ExtractionStateProjector.fs` in `src/Frank.Cli.Core/Unified/`
- **Data Model**: `kitty-specs/031-unified-resource-pipeline/data-model.md` -- `UnifiedResource.TypeInfo: AnalyzedType list`, `UnifiedExtractionState` -> `ExtractionState` projection
- **Research**: `kitty-specs/031-unified-resource-pipeline/research.md` -- R2 (unified walk eliminates duplication)

**Key design decisions**:
- The projection function is **pure**: it takes a `UnifiedExtractionState` and returns an `ExtractionState`. No I/O, no side effects.
- The existing `TypeMapper` and `ShapeGenerator` modules are called as pure functions -- they take `AnalyzedType` lists and a `MappingConfig` and return OWL/SHACL graphs. They do NOT need modification (spec assumption).
- One state file (`unified-state.bin`), one format, one source of truth. The old `state.json` format is detected and rejected with a migration message. No dual-file writing.
- The projector is the **only** consumer of `TypeMapper` and `ShapeGenerator` in the unified pipeline -- the unified extractor stores `AnalyzedType` lists, not pre-computed graphs.

**Existing infrastructure**:
- `src/Frank.Cli.Core/State/ExtractionState.fs` -- target type: `ExtractionState` with `Ontology: IGraph`, `Shapes: IGraph`, `SourceMap`, `Clarifications`, `Metadata`, `UnmappedTypes`
- `src/Frank.Cli.Core/Extraction/TypeMapper.fs` -- `TypeMapper.mapTypes: MappingConfig -> AnalyzedType list -> IGraph` (produces OWL ontology graph)
- `src/Frank.Cli.Core/Extraction/ShapeGenerator.fs` -- `ShapeGenerator.generateShapes: MappingConfig -> AnalyzedType list -> IGraph` (produces SHACL shapes graph)
- `src/Frank.Cli.Core/Commands/ClarifyCommand.fs` -- reads `ExtractionState`, produces clarification questions
- `src/Frank.Cli.Core/Commands/ValidateCommand.fs` -- reads `ExtractionState`, validates completeness
- `src/Frank.Cli.Core/Commands/CompileCommand.fs` -- reads `ExtractionState`, generates OWL/XML + SHACL artifacts
- `src/Frank.Cli.Core/Commands/DiffCommand.fs` -- reads two `ExtractionState`s, computes structured diff

---

## Subtasks & Detailed Guidance

### Subtask T068 -- Create `ExtractionStateProjector.fs` with `toExtractionState` function

- **Purpose**: Provide the bridge between the new unified extraction state and the existing semantic subcommands. This is the central backward-compatibility mechanism.

- **Steps**:
  1. Create `src/Frank.Cli.Core/Unified/ExtractionStateProjector.fs`.
  2. Module declaration: `module Frank.Cli.Core.Unified.ExtractionStateProjector`
  3. Open necessary namespaces:
     ```fsharp
     open System
     open Frank.Cli.Core.State        // ExtractionState, ExtractionMetadata, SourceLocation
     open Frank.Cli.Core.Analysis     // AnalyzedType
     open Frank.Cli.Core.Extraction   // TypeMapper, ShapeGenerator
     ```
  4. Define the projection function signature:
     ```fsharp
     /// Project a UnifiedExtractionState into the legacy ExtractionState format
     /// used by semantic subcommands (clarify, validate, compile, diff).
     let toExtractionState (unified: UnifiedExtractionState) : ExtractionState =
         ...
     ```
  5. The function body must:
     - Collect all `AnalyzedType` lists from each `UnifiedResource.TypeInfo` and flatten into a single list
     - Build a `TypeMapper.MappingConfig` from `unified.BaseUri` and `unified.Vocabularies`
     - Call `TypeMapper.mapTypes config allTypes` to produce the OWL ontology `IGraph`
     - Call `ShapeGenerator.generateShapes config allTypes` to produce the SHACL shapes `IGraph`
     - Build the `SourceMap` from resource location data (see T070)
     - Carry over `Clarifications` (empty map for fresh unified state, or preserved if migrated)
     - Build `ExtractionMetadata` from unified state metadata
     - Compute `UnmappedTypes` from the unified model's derived fields

- **Files**: `src/Frank.Cli.Core/Unified/ExtractionStateProjector.fs` (NEW, ~80-120 lines)
- **Notes**:
  - This function is called on-demand when a semantic subcommand needs the `ExtractionState`. It is NOT called during extraction -- the unified extractor stores `AnalyzedType` lists, and the projection happens at command time.
  - The function allocates RDF graphs (`IGraph`) each time it is called. For the CLI use case (one call per command invocation), this is acceptable. For runtime use, caching would be needed, but that is handled by the affordance middleware (separate WP).
  - Important: Check the actual signatures of `TypeMapper.mapTypes` and `ShapeGenerator.generateShapes`. The function names and signatures in this prompt are based on reading the module headers. Verify against the actual implementation:
    - `TypeMapper.fs` line 12: `module TypeMapper` with `MappingConfig` and mapping functions
    - `ShapeGenerator.fs` line 12: `module ShapeGenerator` with shape generation functions

### Subtask T069 -- The projector calls `TypeMapper.mapTypes` and `ShapeGenerator.generateShapes`

- **Purpose**: Ensure the projection uses the exact same OWL/SHACL generation logic that the old semantic extract used, guaranteeing identical output.

- **Steps**:
  1. Read the full implementation of `TypeMapper.fs` and `ShapeGenerator.fs` to understand their exact API:
     - `src/Frank.Cli.Core/Extraction/TypeMapper.fs` -- what is the top-level public function? Is it `mapTypes`? What parameters does it take?
     - `src/Frank.Cli.Core/Extraction/ShapeGenerator.fs` -- what is the top-level public function? Is it `generateShapes`? What parameters does it take?

  2. The existing semantic `ExtractCommand.fs` calls these functions. Read it to see the exact call pattern:
     ```fsharp
     // In src/Frank.Cli.Core/Commands/ExtractCommand.fs:
     // Look for how TypeMapper and ShapeGenerator are called
     ```

  3. Replicate the exact same call pattern in the projector:
     ```fsharp
     let private buildMappingConfig (unified: UnifiedExtractionState) : TypeMapper.MappingConfig =
         { BaseUri = Uri(unified.BaseUri)
           Vocabularies = unified.Vocabularies }

     let private collectAllTypes (unified: UnifiedExtractionState) : AnalyzedType list =
         unified.Resources
         |> List.collect (fun r -> r.TypeInfo)
         |> List.distinctBy (fun t -> t.FullName)  // Deduplicate types shared across resources

     let toExtractionState (unified: UnifiedExtractionState) : ExtractionState =
         let config = buildMappingConfig unified
         let allTypes = collectAllTypes unified
         let ontology = TypeMapper.mapTypes config allTypes  // Exact function name TBD
         let shapes = ShapeGenerator.generateShapes config allTypes  // Exact function name TBD
         { Ontology = ontology
           Shapes = shapes
           SourceMap = buildSourceMap unified
           Clarifications = Map.empty  // No clarifications in unified state
           Metadata = buildMetadata unified
           UnmappedTypes = computeUnmappedTypes unified }
     ```

  4. Handle type deduplication: Multiple resources may reference the same F# types. The `AnalyzedType` list must be deduplicated by `FullName` before passing to `TypeMapper` and `ShapeGenerator` to avoid duplicate OWL classes/SHACL shapes.

- **Files**: `src/Frank.Cli.Core/Unified/ExtractionStateProjector.fs` (same file)
- **Notes**:
  - The function names (`mapTypes`, `generateShapes`) are approximations. Read the actual module implementations before coding. The modules use `let` bindings at the module level, so the public API is whatever top-level functions are exposed.
  - If `TypeMapper` and `ShapeGenerator` take a `Graph` as input (to add triples to an existing graph), create a fresh `Graph()` and pass it. Check whether they return a new graph or mutate an existing one.
  - The `VocabularyAligner` module may also need to be called for Schema.org alignment. Check how `ExtractCommand.fs` uses it.

### Subtask T070 -- Build `ExtractionState.SourceMap` from `UnifiedResource` data

- **Purpose**: Construct the `SourceMap: Map<string, SourceLocation>` field that semantic subcommands use to report where types and routes are defined in source code.

- **Steps**:
  1. The `SourceMap` keys are type/property URIs (e.g., `"http://example.com/ontology#Game"`, `"http://example.com/ontology#Game/board"`). The values are `SourceLocation` records with file, line, and column.

  2. The unified model stores `UnifiedResource` records with route template and `TypeInfo: AnalyzedType list`. Each `AnalyzedType` should carry source location data. Check the `AnalyzedType` definition:
     ```
     src/Frank.Cli.Core/Analysis/TypeAnalyzer.fs
     ```

  3. If `AnalyzedType` has source location fields, map them into the `SourceMap`:
     ```fsharp
     let private buildSourceMap (unified: UnifiedExtractionState) : Map<string, SourceLocation> =
         let config = buildMappingConfig unified
         let baseUri = config.BaseUri.ToString().TrimEnd('/')

         unified.Resources
         |> List.collect (fun resource ->
             resource.TypeInfo
             |> List.collect (fun analyzedType ->
                 // Create entries for the type itself
                 let typeEntry =
                     match analyzedType.Location with
                     | Some loc ->
                         [ $"{baseUri}#{analyzedType.FullName}", loc ]
                     | None -> []

                 // Create entries for each field
                 let fieldEntries =
                     analyzedType.Fields
                     |> List.choose (fun field ->
                         match field.Location with
                         | Some loc ->
                             Some ($"{baseUri}#{analyzedType.FullName}/{field.Name}", loc)
                         | None -> None)

                 typeEntry @ fieldEntries))
         |> Map.ofList
     ```

  4. If `AnalyzedType` does NOT carry source locations (check the type definition), the `SourceMap` will be empty. This is acceptable for the unified pipeline -- source locations were originally captured by the semantic extractor's AST walk, which the unified extractor replaces. The unified extractor should carry source locations forward.

  5. Also include route-based entries in the source map if the unified model carries route source locations:
     ```fsharp
     let routeEntry =
         match resource.RouteLocation with
         | Some loc -> [ resource.RouteTemplate, loc ]
         | None -> []
     ```

- **Files**: `src/Frank.Cli.Core/Unified/ExtractionStateProjector.fs` (same file)
- **Notes**:
  - Read `src/Frank.Cli.Core/Analysis/TypeAnalyzer.fs` to check the `AnalyzedType` definition for source location fields. The type may have a `Location` field or a `SourceFile` field.
  - The source map key format must match what existing commands expect. Read `ClarifyCommand.fs` and `ValidateCommand.fs` to see how they look up source map entries.
  - If the existing source map uses URI-style keys (like `http://example.com/ontology#Game`), replicate that format. If it uses simple string keys (like `Game`), use that format.

### Subtask T071 -- Update semantic subcommands to read from unified state via projector

- **Purpose**: Modify the four semantic subcommands (`ClarifyCommand`, `ValidateCommand`, `CompileCommand`, `DiffCommand`) to read from the unified extraction state instead of the old `state.json` file.

- **Steps**:
  1. Each of these commands currently calls `ExtractionState.load(statePath)` to read the old JSON format. Replace this with:
     ```fsharp
     // Old pattern:
     let statePath = ExtractionState.defaultStatePath projectDir
     match ExtractionState.load statePath with
     | Ok state -> ...
     | Error e -> ...

     // New pattern:
     let unifiedPath = UnifiedCache.defaultStatePath projectDir  // obj/frank-cli/unified-state.bin
     let legacyPath = ExtractionState.defaultStatePath projectDir  // obj/frank-cli/state.json
     match UnifiedCache.load unifiedPath with
     | Ok unified ->
         let state = ExtractionStateProjector.toExtractionState unified
         // ... existing logic using 'state' unchanged ...
     | Error _ ->
         // Check for old format
         if File.Exists legacyPath then
             Error "Old state format detected. Please re-extract: frank-cli extract --project <fsproj>"
         else
             Error "No extraction state found. Run: frank-cli extract --project <fsproj>"
     ```

  2. Update each command:

     **ClarifyCommand.fs** (`src/Frank.Cli.Core/Commands/ClarifyCommand.fs`):
     - The `execute` function takes a project path and reads `ExtractionState`.
     - Replace the `ExtractionState.load` call with unified state loading + projection.
     - The rest of the command logic (question generation) operates on `ExtractionState` and needs no changes.

     **ValidateCommand.fs** (`src/Frank.Cli.Core/Commands/ValidateCommand.fs`):
     - The `execute` function reads `ExtractionState` for OWL class/property analysis.
     - Replace loading logic as above.
     - The validation checks operate on `IGraph` from `state.Ontology` and `state.Shapes` -- unchanged.

     **CompileCommand.fs** (`src/Frank.Cli.Core/Commands/CompileCommand.fs`):
     - The `execute` function reads `ExtractionState` and writes OWL/XML + SHACL artifacts.
     - Replace loading logic as above.
     - The compilation logic (graph serialization, manifest generation) operates on `ExtractionState` -- unchanged.

     **DiffCommand.fs** (`src/Frank.Cli.Core/Commands/DiffCommand.fs`):
     - The `execute` function reads TWO `ExtractionState`s (current and previous).
     - Replace both loading calls with unified state loading + projection.
     - The diff engine operates on `ExtractionState` pairs -- unchanged.
     - Special case: the "previous" state may be an old-format `state.json` (from before migration). Handle this gracefully: if the previous state file is JSON, try loading it with the old format parser. If it is binary, load it as unified and project.

  3. Extract the loading + projection + old-format detection into a shared helper:
     ```fsharp
     module UnifiedStateLoader =
         /// Load extraction state from either unified binary or detect old format.
         let loadExtractionState (projectDir: string) : Result<ExtractionState, string> =
             let unifiedPath = UnifiedCache.defaultStatePath projectDir
             let legacyPath = ExtractionState.defaultStatePath projectDir

             match UnifiedCache.load unifiedPath with
             | Ok unified ->
                 Ok (ExtractionStateProjector.toExtractionState unified)
             | Error _ ->
                 if File.Exists legacyPath then
                     Error "Old state format detected (state.json). Please re-extract with: frank-cli extract --project <fsproj>"
                 else
                     Error "No extraction state found. Run: frank-cli extract --project <fsproj>"
     ```

  4. All four commands call `UnifiedStateLoader.loadExtractionState projectDir` instead of `ExtractionState.load`.

- **Files**:
  - `src/Frank.Cli.Core/Unified/ExtractionStateProjector.fs` (add `UnifiedStateLoader` helper)
  - `src/Frank.Cli.Core/Commands/ClarifyCommand.fs` (MODIFY loading logic)
  - `src/Frank.Cli.Core/Commands/ValidateCommand.fs` (MODIFY loading logic)
  - `src/Frank.Cli.Core/Commands/CompileCommand.fs` (MODIFY loading logic)
  - `src/Frank.Cli.Core/Commands/DiffCommand.fs` (MODIFY loading logic)
- **Notes**:
  - The changes to each command are minimal: replace the `ExtractionState.load` call with `UnifiedStateLoader.loadExtractionState`. The rest of the command logic is unchanged.
  - The `UnifiedCache.load` function is from the unified cache module (WP03 or earlier). Verify it exists and returns `Result<UnifiedExtractionState, string>`.
  - Compile order matters: `ExtractionStateProjector.fs` must compile before the command modules. Place it in the `Unified/` directory and add it to the fsproj before the `Commands/` entries.

### Subtask T072 -- Detect old `state.json` format and print migration message

- **Purpose**: Users who have previously used `frank-cli semantic extract` will have `obj/frank-cli/state.json` files. When they run semantic subcommands after the unified pipeline is deployed, they need a clear message telling them to re-extract.

- **Steps**:
  1. In `UnifiedStateLoader.loadExtractionState`, when the unified binary is not found:
     - Check if `obj/frank-cli/state.json` exists
     - If it does, return an `Error` with a specific migration message:
       ```
       Old state format detected (obj/frank-cli/state.json).

       The unified extraction pipeline has replaced the separate semantic and statechart
       extraction commands. Please re-extract your project:

           frank-cli extract --project <your-project>.fsproj

       This will create a unified extraction state that all subcommands can read.
       The old state.json file can be safely deleted after re-extraction.
       ```
     - If it does not, return a generic "no state found" error

  2. The detection is simple: check `File.Exists(legacyPath)` where `legacyPath = ExtractionState.defaultStatePath projectDir`. The existing function `ExtractionState.defaultStatePath` returns `Path.Combine(projectDir, "obj", "frank-cli", "state.json")`.

  3. Do NOT attempt to auto-migrate the old format. The unified extraction is a fundamentally different model (it includes behavioral data, not just type data). Re-extraction is required.

  4. Do NOT delete the old `state.json` automatically. Let the user decide when to clean up.

- **Files**: `src/Frank.Cli.Core/Unified/ExtractionStateProjector.fs` (same `UnifiedStateLoader` helper)
- **Notes**:
  - The migration message should include the actual project path if available. If the command receives a project path, include it in the suggested command:
    ```
    frank-cli extract --project /path/to/MyApp.fsproj
    ```
  - Edge case: both `unified-state.bin` AND `state.json` exist (user re-extracted but didn't clean up). In this case, the unified state takes precedence -- load it normally. The old `state.json` is ignored.
  - Edge case: the unified binary exists but is corrupted or from an incompatible version. The `UnifiedCache.load` function should return an `Error` in this case. The loader should then check for the old format AND suggest re-extraction.

### Subtask T073 -- Write tests for the projector

- **Purpose**: Verify that projecting a `UnifiedExtractionState` through `toExtractionState` produces OWL/SHACL graphs equivalent to what the old semantic extract would have produced.

- **Steps**:
  1. Create test file: `test/Frank.Cli.Core.Tests/ExtractionStateProjectorTests.fs`.

  2. Build a test `UnifiedExtractionState` with known `AnalyzedType` data:
     ```fsharp
     let testTypes : AnalyzedType list = [
         { FullName = "Game"
           Fields = [
               { Name = "Board"; Kind = Primitive "string"; ... }
               { Name = "CurrentTurn"; Kind = Primitive "string"; ... }
               { Name = "Winner"; Kind = Optional (Primitive "string"); ... }
           ]
           ... }
     ]

     let testUnifiedState : UnifiedExtractionState =
         { Resources = [
               { RouteTemplate = "/games/{gameId}"
                 ResourceSlug = "games"
                 TypeInfo = testTypes
                 Statechart = None  // Plain resource for this test
                 HttpCapabilities = []
                 DerivedFields = { OrphanStates = []; UnhandledCases = []; StateStructure = Map.empty; TypeCoverage = 1.0 } }
           ]
           AffordanceMap = []
           SourceHash = "abc123"
           BaseUri = "http://example.com/ontology"
           Vocabularies = [ "https://schema.org" ]
           ExtractedAt = DateTimeOffset.UtcNow
           ToolVersion = "7.0.0" }
     ```

  3. Generate the "expected" `ExtractionState` by calling `TypeMapper` and `ShapeGenerator` directly on the same `AnalyzedType` data:
     ```fsharp
     let config : TypeMapper.MappingConfig =
         { BaseUri = Uri "http://example.com/ontology"
           Vocabularies = [ "https://schema.org" ] }
     let expectedOntology = TypeMapper.mapTypes config testTypes
     let expectedShapes = ShapeGenerator.generateShapes config testTypes
     ```

  4. Project the unified state through the projector:
     ```fsharp
     let projected = ExtractionStateProjector.toExtractionState testUnifiedState
     ```

  5. Compare the projected graphs against the expected graphs:
     ```fsharp
     testCase "Projected ontology matches direct TypeMapper output" (fun () ->
         let projectedTriples = projected.Ontology.Triples |> Seq.length
         let expectedTriples = expectedOntology.Triples |> Seq.length
         Expect.equal projectedTriples expectedTriples "Triple counts should match"
         // For deeper comparison, use graph isomorphism or triple-by-triple comparison
     )

     testCase "Projected shapes match direct ShapeGenerator output" (fun () ->
         let projectedTriples = projected.Shapes.Triples |> Seq.length
         let expectedTriples = expectedShapes.Triples |> Seq.length
         Expect.equal projectedTriples expectedTriples "Triple counts should match"
     )
     ```

  6. Test the metadata projection:
     ```fsharp
     testCase "Projected metadata carries correct values" (fun () ->
         Expect.equal projected.Metadata.SourceHash "abc123" "Source hash"
         Expect.equal projected.Metadata.ToolVersion "7.0.0" "Tool version"
         Expect.equal projected.Metadata.BaseUri (Uri "http://example.com/ontology") "Base URI"
         Expect.equal projected.Metadata.Vocabularies [ "https://schema.org" ] "Vocabularies"
     )
     ```

  7. Test old-format detection:
     ```fsharp
     testCase "Loading with old state.json present returns migration message" (fun () ->
         // Create a temp directory with a state.json but no unified-state.bin
         let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
         let frankCliDir = Path.Combine(tempDir, "obj", "frank-cli")
         Directory.CreateDirectory(frankCliDir) |> ignore
         File.WriteAllText(Path.Combine(frankCliDir, "state.json"), "{}")

         let result = UnifiedStateLoader.loadExtractionState tempDir
         match result with
         | Error msg ->
             Expect.stringContains msg "Old state format detected" "Should mention old format"
             Expect.stringContains msg "frank-cli extract --project" "Should suggest re-extraction"
         | Ok _ -> failtest "Should have returned Error for old format"

         // Cleanup
         Directory.Delete(tempDir, true)
     )
     ```

  8. Test type deduplication:
     ```fsharp
     testCase "Duplicate types across resources are deduplicated" (fun () ->
         let sharedType = { FullName = "SharedModel"; Fields = []; ... }
         let unifiedWithDupes =
             { testUnifiedState with
                 Resources = [
                     { testUnifiedState.Resources.[0] with TypeInfo = [ sharedType ] }
                     { testUnifiedState.Resources.[0] with
                         RouteTemplate = "/other"
                         TypeInfo = [ sharedType ] }
                 ] }

         let projected = ExtractionStateProjector.toExtractionState unifiedWithDupes
         // OWL should have one class for SharedModel, not two
         // Count owl:Class triples -- should match single-type extraction
     )
     ```

  9. Run tests: `dotnet test test/Frank.Cli.Core.Tests/`

- **Files**: `test/Frank.Cli.Core.Tests/ExtractionStateProjectorTests.fs` (NEW, ~200-250 lines)
- **Notes**:
  - The `AnalyzedType` constructor must match the actual type definition. Read `src/Frank.Cli.Core/Analysis/TypeAnalyzer.fs` before writing tests.
  - RDF graph comparison is non-trivial. For unit tests, comparing triple counts is a reasonable approximation. For deeper verification, use dotNetRdf's graph isomorphism check (`graph1.Equals(graph2)`) or compare sorted triple strings.
  - The temp directory tests need cleanup in a `finally` block to avoid leaving test artifacts.
  - If `TypeMapper` or `ShapeGenerator` function signatures differ from what is assumed here, adjust the test accordingly.

---

## Test Strategy

- **Unit tests**: Projector function tested with mock `UnifiedExtractionState` records. RDF graph comparison via triple counts and isomorphism.
- **Integration tests**: Old-format detection tested with temp directories and real file I/O.
- **Regression tests**: Verify existing semantic subcommand behavior is preserved by running them through the projector. Deferred to WP13 end-to-end validation if complex.
- **Test framework**: Expecto (matching existing Frank test conventions).
- **Commands**:
  ```bash
  dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj
  dotnet test test/Frank.Cli.Core.Tests/
  ```
- **Coverage targets**: Projector produces valid OWL/SHACL graphs, metadata fields are correctly mapped, type deduplication works, old-format detection triggers migration message.

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| `TypeMapper` or `ShapeGenerator` function signatures differ from assumed names | Read actual implementation before coding. These are pure functions in existing modules -- the API is stable. |
| Graph comparison in tests is fragile (blank node IDs differ) | Use triple count comparison for basic validation. Use dotNetRdf's `GraphDiffReport` for detailed comparison if needed. |
| Clarifications are lost in migration (unified state has no clarifications) | Document that clarifications from old extractions are not migrated. Users can re-run `frank-cli clarify` to regenerate. |
| DiffCommand needs to compare old-format previous state with projected current state | Support both formats for the "previous" argument: detect format by file extension (.json = old, .bin = unified). |
| Compile order issues when adding `ExtractionStateProjector.fs` before command modules | Place it in the `Unified/` directory group in the fsproj, after the unified model types but before command modules. |

---

## Review Guidance

- Verify `toExtractionState` calls `TypeMapper` and `ShapeGenerator` with the same configuration the old semantic extract used.
- Verify type deduplication prevents duplicate OWL classes when multiple resources share types.
- Verify all four semantic subcommands (`clarify`, `validate`, `compile`, `diff`) use `UnifiedStateLoader.loadExtractionState` instead of `ExtractionState.load`.
- Verify old-format detection produces a clear, actionable migration message.
- Verify the compile order in `Frank.Cli.Core.fsproj` places `ExtractionStateProjector.fs` before command modules.
- Run `dotnet build Frank.sln` to verify clean build.
- Run `dotnet test test/Frank.Cli.Core.Tests/` to verify all tests pass.
- Cross-check: existing semantic subcommands still produce correct output when given a projected `ExtractionState` from a unified extraction.

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
- 2026-03-19T04:06:50Z – claude-opus – shell_pid=26347 – lane=doing – Assigned agent via workflow command
- 2026-03-19T04:21:51Z – claude-opus – shell_pid=26347 – lane=for_review – Ready for review: ExtractionStateProjector with TypeMapper/ShapeGenerator integration, 4 command updates, unified loader with legacy fallback, and 9 tests
- 2026-03-19T04:27:40Z – claude-opus – shell_pid=29237 – lane=doing – Started review via workflow command
