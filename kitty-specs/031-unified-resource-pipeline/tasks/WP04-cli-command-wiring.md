---
work_package_id: WP04
title: CLI Command Wiring -- Unified Extract & Generate
lane: planned
dependencies:
- WP02
subtasks:
- T019
- T020
- T021
- T022
- T023
- T024
- T025
phase: Phase 1 - Core Pipeline
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
- FR-001
- FR-006
- FR-007
- FR-029
- FR-030
- FR-031
- FR-032
---

# Work Package Prompt: WP04 -- CLI Command Wiring -- Unified Extract & Generate

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
Use language identifiers in code blocks: ````fsharp`, ````xml`, ````bash`

---

## Implementation Command

Depends on WP02 and WP03:

```bash
spec-kitty implement WP04 --base WP02
```

Note: WP04 needs both WP02 (unified extractor) and WP03 (caching). The base branch should include both. If WP02 and WP03 were developed in parallel, merge WP03 into WP02's branch first, or use the branch where both are merged.

---

## Objectives & Success Criteria

1. Create a unified `ExtractCommand.fs` that replaces both `semantic extract` and `statechart extract` (FR-029).
2. Wire `frank-cli extract --project <fsproj>` with `--output-format text|json`, `--force`, and `--base-uri` options.
3. Update the `generate` command to read from unified extraction state (cached), adding `--format affordance-map` support.
4. Update `HelpContent.fs` for the new unified extract command.
5. Update `TextOutput.fs` and `JsonOutput.fs` for unified extraction results.
6. Ensure existing `statechart parse` and `statechart validate` commands continue working unchanged.

**Success**: `frank-cli extract --project <fsproj>` produces a unified extraction containing both type and behavioral data. `frank-cli generate --project <fsproj> --format alps` reads from the cached unified state and generates ALPS with both type and behavioral descriptors. All existing `statechart` subcommands still work.

---

## Context & Constraints

- **Spec**: `kitty-specs/031-unified-resource-pipeline/spec.md` (FR-029 through FR-032, User Stories 1-3)
- **Plan**: `kitty-specs/031-unified-resource-pipeline/plan.md` (CLI structure)
- **Data Model**: `kitty-specs/031-unified-resource-pipeline/data-model.md` (UnifiedExtractionState, AffordanceMapEntry)
- **Existing commands to understand**:
  - `src/Frank.Cli.Core/Commands/ExtractCommand.fs` -- current semantic extract (FR-029: this is being replaced)
  - `src/Frank.Cli.Core/Commands/StatechartExtractCommand.fs` -- current statechart extract (FR-029: this is being replaced)
  - `src/Frank.Cli.Core/Commands/StatechartGenerateCommand.fs` -- current statechart generate (FR-031: must keep working)
  - `src/Frank.Cli.Core/Commands/StatechartValidateCommand.fs` -- current statechart validate (FR-031: must keep working)
  - `src/Frank.Cli.Core/Commands/StatechartParseCommand.fs` -- current statechart parse (FR-031: must keep working)
  - `src/Frank.Cli/Program.fs` -- CLI entry point with command routing
- **Existing output modules**:
  - `src/Frank.Cli.Core/Output/TextOutput.fs` -- text formatting
  - `src/Frank.Cli.Core/Output/JsonOutput.fs` -- JSON formatting
  - `src/Frank.Cli.Core/Help/HelpContent.fs` -- help system metadata
- **Key constraints**:
  - The unified `extract` command replaces BOTH `semantic extract` and `statechart extract`. However, existing `statechart` subcommands (`parse`, `validate`, `generate`) must continue to work -- they read from `StatechartSourceExtractor.extract` directly, not from the unified cache.
  - The `generate` command should read from the unified cache when available, falling back to running the unified extractor if the cache is stale.
  - `--format affordance-map` is a NEW format alongside existing ones (wsd, alps, alps-xml, scxml, smcat, xstate, all).
  - The semantic subcommands (`clarify`, `validate`, `compile`, `diff`) are NOT updated in this WP. They continue reading from `state.json`. A future WP will add the `toExtractionState` projector (FR-030).

---

## Subtasks & Detailed Guidance

### Subtask T019 -- Create unified `ExtractCommand.fs`

- **Purpose**: Implement a new extract command that replaces both semantic and statechart extraction with a unified pipeline using the WP02 extractor and WP03 cache.
- **Steps**:
  1. Create `src/Frank.Cli.Core/Commands/UnifiedExtractCommand.fs` (new file, does NOT modify the existing `ExtractCommand.fs`).
  2. Module: `module Frank.Cli.Core.Commands.UnifiedExtractCommand`.
  3. Define the result type:

```fsharp
module Frank.Cli.Core.Commands.UnifiedExtractCommand

open System
open System.IO
open Frank.Cli.Core.Unified
open Frank.Cli.Core.Statechart.StatechartError

type UnifiedExtractResult =
    { /// Number of resources extracted
      ResourceCount: int
      /// Number of resources with statechart data
      StatefulResourceCount: int
      /// Number of resources without statechart data
      PlainResourceCount: int
      /// Total types analyzed
      TypeCount: int
      /// Derived field warnings (orphan states, unhandled cases)
      Warnings: string list
      /// Path to the cache file
      CacheFilePath: string
      /// Whether the result was loaded from cache
      FromCache: bool
      /// The full unified extraction state (for downstream commands)
      State: UnifiedExtractionState }
```

  4. Implement `execute`:

```fsharp
let execute
    (projectPath: string)
    (baseUri: string)
    (vocabularies: string list)
    (force: bool)
    : Async<Result<UnifiedExtractResult, StatechartError>> =
    async {
        if not (File.Exists projectPath) then
            return Error (FileNotFound projectPath)
        else
            let projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))

            // Step 1: Check cache
            let cacheResult = UnifiedCache.tryLoadFresh projectDir force

            match cacheResult with
            | Ok cachedState ->
                // Cache is fresh -- build result from cached state
                let warnings = collectWarnings cachedState.Resources
                return Ok
                    { ResourceCount = cachedState.Resources.Length
                      StatefulResourceCount =
                          cachedState.Resources |> List.filter (fun r -> r.Statechart.IsSome) |> List.length
                      PlainResourceCount =
                          cachedState.Resources |> List.filter (fun r -> r.Statechart.IsNone) |> List.length
                      TypeCount =
                          cachedState.Resources |> List.collect (fun r -> r.TypeInfo) |> List.distinctBy _.FullName |> List.length
                      Warnings = warnings
                      CacheFilePath = UnifiedCache.cachePath projectDir
                      FromCache = true
                      State = cachedState }

            | Error _reason ->
                // Cache is stale/missing -- run unified extraction
                match! UnifiedExtractor.extract projectPath with
                | Error e -> return Error e
                | Ok resources ->
                    // Save to cache
                    let saveResult =
                        UnifiedCache.saveExtractionState projectDir resources baseUri vocabularies
                    let state =
                        match saveResult with
                        | Ok s -> s
                        | Error _msg ->
                            // Cache write failed -- construct state manually, warn later
                            { Resources = resources
                              SourceHash = ""
                              BaseUri = baseUri
                              Vocabularies = vocabularies
                              ExtractedAt = DateTimeOffset.UtcNow
                              ToolVersion = UnifiedCache.currentToolVersion }

                    let warnings = collectWarnings resources
                    return Ok
                        { ResourceCount = resources.Length
                          StatefulResourceCount =
                              resources |> List.filter (fun r -> r.Statechart.IsSome) |> List.length
                          PlainResourceCount =
                              resources |> List.filter (fun r -> r.Statechart.IsNone) |> List.length
                          TypeCount =
                              resources |> List.collect (fun r -> r.TypeInfo) |> List.distinctBy _.FullName |> List.length
                          Warnings = warnings
                          CacheFilePath = UnifiedCache.cachePath projectDir
                          FromCache = false
                          State = state }
    }
```

  5. Helper to collect warnings from derived fields:

```fsharp
let private collectWarnings (resources: UnifiedResource list) : string list =
    resources
    |> List.collect (fun r ->
        let orphanWarnings =
            r.DerivedFields.OrphanStates
            |> List.map (fun s ->
                $"Resource {r.RouteTemplate}: state '{s}' is declared but never handled in inState/forState")
        let unhandledWarnings =
            r.DerivedFields.UnhandledCases
            |> List.map (fun c ->
                $"Resource {r.RouteTemplate}: DU case '{c}' not found in statechart state list")
        orphanWarnings @ unhandledWarnings)
```

- **Files**: `src/Frank.Cli.Core/Commands/UnifiedExtractCommand.fs` (NEW)
- **Notes**:
  - The existing `ExtractCommand.fs` (semantic) and `StatechartExtractCommand.fs` are NOT modified. They remain for backward compatibility until the semantic subcommands are migrated.
  - `FromCache = true` lets the output formatter display "Loaded from cache" vs "Extracted from source".
  - Cache write failure is non-fatal. The extraction succeeded; only caching failed.
  - The `State` field contains the full `UnifiedExtractionState` so downstream commands can use it without re-loading from cache.

### Subtask T020 -- Wire `frank-cli extract --project` in Program.fs

- **Purpose**: Add the unified extract command to the CLI's command routing.
- **Steps**:
  1. Read `src/Frank.Cli/Program.fs` to understand the current command routing structure.
  2. Add a new top-level `extract` command alongside the existing `semantic` group:

```fsharp
// In Program.fs command routing:
| "extract" ->
    // frank-cli extract --project <fsproj> [--output-format text|json] [--force] [--base-uri <uri>]
    let projectPath = parseOption "--project" args
    let outputFormat = parseOption "--output-format" args |> Option.defaultValue "text"
    let force = args |> Array.contains "--force"
    let baseUri = parseOption "--base-uri" args |> Option.defaultValue "http://example.org/"
    let vocabularies =
        parseOption "--vocabularies" args
        |> Option.map (fun s -> s.Split(',') |> Array.toList)
        |> Option.defaultValue [ "schema.org" ]

    match projectPath with
    | None ->
        eprintfn "Error: --project is required"
        1
    | Some path ->
        let result =
            UnifiedExtractCommand.execute path baseUri vocabularies force
            |> Async.RunSynchronously
        match result with
        | Ok extractResult ->
            match outputFormat with
            | "json" -> printfn "%s" (JsonOutput.formatUnifiedExtractResult extractResult)
            | _ -> printfn "%s" (TextOutput.formatUnifiedExtractResult extractResult)
            0
        | Error e ->
            eprintfn "Error: %s" (StatechartError.format e)
            1
```

  3. Keep the existing `"semantic"` command group working (it routes to `semantic extract`, `semantic clarify`, etc.). The new `extract` is a top-level command that is the recommended replacement.

  4. Update argument parsing to handle the `--output-format`, `--force`, `--base-uri`, and `--vocabularies` options. Follow whatever parsing pattern `Program.fs` already uses.

- **Files**: `src/Frank.Cli/Program.fs` (MODIFIED)
- **Notes**:
  - The command is `frank-cli extract`, NOT `frank-cli unified extract` or `frank-cli semantic extract`. It's a top-level command per FR-029.
  - The `--base-uri` default is `http://example.org/` matching the existing semantic extract default.
  - The `--vocabularies` default is `["schema.org"]` matching the existing default.
  - **IMPORTANT**: Read `Program.fs` carefully before modifying. It may use `System.CommandLine` (spec 016 added it) or a custom argument parser. Adapt accordingly.

### Subtask T021 -- Update `generate` command to read from unified state

- **Purpose**: Make the generate command read from the cached unified extraction state instead of re-running `StatechartSourceExtractor.extract`.
- **Steps**:
  1. Create `src/Frank.Cli.Core/Commands/UnifiedGenerateCommand.fs` (new file alongside existing `StatechartGenerateCommand.fs`).
  2. Module: `module Frank.Cli.Core.Commands.UnifiedGenerateCommand`.
  3. The generate command should:
     - First try to load the unified cache (`UnifiedCache.tryLoadFresh`).
     - If the cache is fresh, extract `ExtractedStatechart` records from `UnifiedResource.Statechart` and feed them to the existing `FormatPipeline.generateFormatFromExtracted`.
     - If the cache is stale/missing, run the unified extractor (not the old statechart extractor), cache the result, then generate.
  4. Implement:

```fsharp
module Frank.Cli.Core.Commands.UnifiedGenerateCommand

open System.IO
open Frank.Statecharts.Validation
open Frank.Cli.Core.Unified
open Frank.Cli.Core.Statechart
open Frank.Cli.Core.Statechart.StatechartError

type GeneratedArtifact =
    { ResourceSlug: string
      RouteTemplate: string
      Format: FormatTag
      Content: string
      FilePath: string option }

type GenerateResult =
    { Artifacts: GeneratedArtifact list
      OutputDirectory: string option
      GenerationErrors: StatechartError list
      FromCache: bool }

let private parseFormat (s: string) : Result<FormatTag list, StatechartError> =
    match s.ToLowerInvariant() with
    | "wsd" -> Ok [ Wsd ]
    | "alps" -> Ok [ Alps ]
    | "alps-xml" | "alpsxml" -> Ok [ AlpsXml ]
    | "scxml" -> Ok [ Scxml ]
    | "smcat" -> Ok [ Smcat ]
    | "xstate" -> Ok [ XState ]
    | "affordance-map" -> Ok [ ] // Handled separately -- no FormatTag for this
    | "all" -> Ok FormatPipeline.allFormats
    | other -> Error (UnknownFormat other)

let private isAffordanceMapFormat (s: string) : bool =
    s.ToLowerInvariant() = "affordance-map"

/// Get unified resources, using cache if available.
let private getResources
    (projectPath: string)
    (force: bool)
    : Async<Result<UnifiedResource list * bool, StatechartError>> =
    async {
        let projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))
        match UnifiedCache.tryLoadFresh projectDir force with
        | Ok state -> return Ok (state.Resources, true)
        | Error _ ->
            match! UnifiedExtractor.extract projectPath with
            | Ok resources -> return Ok (resources, false)
            | Error e -> return Error e
    }

let execute
    (projectPath: string)
    (format: string)
    (outputDir: string option)
    (resourceFilter: string option)
    (force: bool)
    : Async<Result<GenerateResult, StatechartError>> =
    async {
        if isAffordanceMapFormat format then
            // Handle affordance-map generation separately
            match! getResources projectPath force with
            | Error e -> return Error e
            | Ok (resources, fromCache) ->
                let mapContent = generateAffordanceMapJson resources ""
                return Ok
                    { Artifacts =
                        [ { ResourceSlug = "affordance-map"
                            RouteTemplate = "*"
                            Format = Alps // placeholder -- affordance-map doesn't have a FormatTag
                            Content = mapContent
                            FilePath = None } ]
                      OutputDirectory = outputDir
                      GenerationErrors = []
                      FromCache = fromCache }
        else
            match parseFormat format with
            | Error e -> return Error e
            | Ok formats ->
                match! getResources projectPath force with
                | Error e -> return Error e
                | Ok (resources, fromCache) ->
                    // Extract statecharts from unified resources
                    let machines =
                        resources
                        |> List.choose (fun r ->
                            r.Statechart |> Option.map (fun sc -> sc))

                    let filtered =
                        match resourceFilter with
                        | None -> machines
                        | Some name ->
                            machines
                            |> List.filter (fun m ->
                                let slug = FormatPipeline.resourceSlug m.RouteTemplate
                                slug = name || m.RouteTemplate.Contains(name))

                    match resourceFilter, filtered with
                    | Some name, [] ->
                        let available =
                            machines
                            |> List.map (fun m -> FormatPipeline.resourceSlug m.RouteTemplate)
                        return Error (ResourceNotFound(name, available))
                    | _ ->
                        let mutable generationErrors = []
                        let artifacts =
                            [ for m in filtered do
                                  let slug = FormatPipeline.resourceSlug m.RouteTemplate
                                  for fmt in formats do
                                      match FormatPipeline.generateFormatFromExtracted fmt slug m with
                                      | Ok content ->
                                          { ResourceSlug = slug
                                            RouteTemplate = m.RouteTemplate
                                            Format = fmt
                                            Content = content
                                            FilePath = None }
                                      | Error err ->
                                          generationErrors <- err :: generationErrors ]

                        match outputDir with
                        | None ->
                            return Ok
                                { Artifacts = artifacts
                                  OutputDirectory = None
                                  GenerationErrors = List.rev generationErrors
                                  FromCache = fromCache }
                        | Some dir ->
                            Directory.CreateDirectory(dir) |> ignore
                            let written =
                                artifacts
                                |> List.map (fun a ->
                                    let fileName =
                                        sprintf "%s%s" a.ResourceSlug (FormatDetector.formatExtension a.Format)
                                    let filePath = Path.Combine(dir, fileName)
                                    File.WriteAllText(filePath, a.Content)
                                    { a with FilePath = Some filePath })
                            return Ok
                                { Artifacts = written
                                  OutputDirectory = Some dir
                                  GenerationErrors = List.rev generationErrors
                                  FromCache = fromCache }
    }
```

  5. The `generateAffordanceMapJson` helper (stub for now):

```fsharp
open System.Text.Json

let private generateAffordanceMapJson (resources: UnifiedResource list) (baseUri: string) : string =
    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
    writer.WriteStartObject()
    writer.WriteString("version", Frank.Affordances.AffordanceMap.currentVersion)
    writer.WriteStartArray("entries")
    for resource in resources do
        match resource.Statechart with
        | Some sc ->
            for stateName in sc.StateNames do
                let methods =
                    sc.StateMetadata
                    |> Map.tryFind stateName
                    |> Option.map _.AllowedMethods
                    |> Option.defaultValue []
                writer.WriteStartObject()
                writer.WriteString("routeTemplate", resource.RouteTemplate)
                writer.WriteString("stateKey", stateName)
                writer.WriteStartArray("allowedMethods")
                for m in methods do writer.WriteStringValue(m)
                writer.WriteEndArray()
                writer.WriteStartArray("linkRelations")
                writer.WriteEndArray() // placeholder
                writer.WriteString("profileUrl", $"{baseUri}{resource.ResourceSlug}")
                writer.WriteEndObject()
        | None ->
            writer.WriteStartObject()
            writer.WriteString("routeTemplate", resource.RouteTemplate)
            writer.WriteString("stateKey", Frank.Affordances.AffordanceMap.WildcardStateKey)
            writer.WriteStartArray("allowedMethods")
            for cap in resource.HttpCapabilities do
                writer.WriteStringValue(cap.Method)
            writer.WriteEndArray()
            writer.WriteStartArray("linkRelations")
            writer.WriteEndArray() // placeholder
            writer.WriteString("profileUrl", $"{baseUri}{resource.ResourceSlug}")
            writer.WriteEndObject()
    writer.WriteEndArray()
    writer.WriteEndObject()
    writer.Flush()
    System.Text.Encoding.UTF8.GetString(stream.ToArray())
```

- **Files**: `src/Frank.Cli.Core/Commands/UnifiedGenerateCommand.fs` (NEW)
- **Notes**:
  - The existing `StatechartGenerateCommand.fs` is NOT modified. The new `UnifiedGenerateCommand` is wired to the `generate` top-level command; the old one stays wired to `statechart generate`.
  - The `affordance-map` format doesn't map to a `FormatTag` enum value -- it's handled as a special case. A future WP may add it to the `FormatTag` DU, but that requires modifying `Frank.Statecharts` which is outside scope.
  - The `generateAffordanceMapJson` function uses `Utf8JsonWriter` following existing CLI output patterns.
  - `linkRelations` is empty for now -- ALPS-derived link relation computation is a later WP.

### Subtask T022 -- Support `--format affordance-map`

- **Purpose**: Add affordance-map as a valid format option in the generate command.
- **Steps**:
  1. This is already handled in T021's `isAffordanceMapFormat` check and `parseFormat` function.
  2. Wire the generate command in `Program.fs`:

```fsharp
| "generate" ->
    // frank-cli generate --project <fsproj> --format <format> [--output <dir>] [--resource <name>] [--force]
    let projectPath = parseOption "--project" args
    let format = parseOption "--format" args
    let outputDir = parseOption "--output" args
    let resourceFilter = parseOption "--resource" args
    let force = args |> Array.contains "--force"

    match projectPath, format with
    | None, _ ->
        eprintfn "Error: --project is required"
        1
    | _, None ->
        eprintfn "Error: --format is required"
        1
    | Some path, Some fmt ->
        let result =
            UnifiedGenerateCommand.execute path fmt outputDir resourceFilter force
            |> Async.RunSynchronously
        match result with
        | Ok genResult ->
            for artifact in genResult.Artifacts do
                match artifact.FilePath with
                | Some fp -> printfn "Generated: %s" fp
                | None -> printfn "%s" artifact.Content
            0
        | Error e ->
            eprintfn "Error: %s" (StatechartError.format e)
            1
```

  3. Ensure `--format all` does NOT include affordance-map (it includes only the statechart formats). Affordance-map must be explicitly requested.

- **Files**: `src/Frank.Cli/Program.fs` (MODIFIED)
- **Notes**:
  - The `all` format expands to `FormatPipeline.allFormats` which is `[Wsd; Alps; AlpsXml; Scxml; Smcat; XState]`. Affordance-map is separate.
  - Users can generate both: `frank-cli generate --format all` + `frank-cli generate --format affordance-map`.

### Subtask T023 -- Update `HelpContent.fs`

- **Purpose**: Add help entries for the new unified `extract` and `generate` commands.
- **Steps**:
  1. Read `src/Frank.Cli.Core/Help/HelpContent.fs` to understand the structure.
  2. Add a new help entry for the unified extract:

```fsharp
let unifiedExtractHelp: CommandHelp =
    { Name = "extract"
      Summary = "Extract unified resource descriptions from F# source"
      Examples =
        [ { Invocation = "frank-cli extract --project MyApp/MyApp.fsproj"
            Description = "Extract type and behavioral data from all resources in the project." }
          { Invocation = "frank-cli extract --project MyApp/MyApp.fsproj --output-format json"
            Description = "Output unified extraction in JSON format." }
          { Invocation = "frank-cli extract --project MyApp/MyApp.fsproj --force"
            Description = "Force re-extraction, bypassing the cache." }
          { Invocation = "frank-cli extract --project MyApp/MyApp.fsproj --base-uri https://api.example.com/"
            Description = "Extract with a custom base URI for ALPS profiles." } ]
      Workflow =
        { StepNumber = 1
          Prerequisites = []
          NextSteps = [ "generate"; "semantic clarify"; "semantic validate" ]
          IsOptional = false }
      Context =
        "The extract command replaces both 'semantic extract' and 'statechart extract'. It analyzes F# source code using FCS in a single pass, producing a unified resource description that includes type structure (records, DUs mapped to OWL classes/properties), behavioral semantics (statechart states, transitions, HTTP methods per state), and HTTP capabilities. Results are cached to obj/frank-cli/unified-state.bin for fast reuse by subsequent commands (generate, validate). Use --force to bypass the cache." }
```

  3. Add updated help for the unified generate:

```fsharp
let unifiedGenerateHelp: CommandHelp =
    { Name = "generate"
      Summary = "Generate format artifacts from unified extraction"
      Examples =
        [ { Invocation = "frank-cli generate --project MyApp/MyApp.fsproj --format alps"
            Description = "Generate ALPS profile with type and behavioral descriptors." }
          { Invocation = "frank-cli generate --project MyApp/MyApp.fsproj --format affordance-map"
            Description = "Generate machine-readable affordance map for runtime middleware." }
          { Invocation = "frank-cli generate --project MyApp/MyApp.fsproj --format all --output ./docs"
            Description = "Generate all statechart formats to an output directory." }
          { Invocation = "frank-cli generate --project MyApp/MyApp.fsproj --format wsd --resource games"
            Description = "Generate WSD for a specific resource." } ]
      Workflow =
        { StepNumber = 2
          Prerequisites = [ "extract" ]
          NextSteps = [ "semantic validate" ]
          IsOptional = false }
      Context =
        "The generate command reads from the unified extraction cache and produces format artifacts. Supported formats: wsd, alps, alps-xml, scxml, smcat, xstate, affordance-map, all. If the cache is stale or missing, extraction runs automatically. The affordance-map format produces a JSON document mapping (route, state) pairs to available HTTP methods and link relations, consumed by the runtime affordance middleware." }
```

  4. Register these in the `allCommands` list (or however the help system indexes commands).

- **Files**: `src/Frank.Cli.Core/Help/HelpContent.fs` (MODIFIED)
- **Notes**:
  - Keep the existing `extractHelp` (semantic extract) entry for backward compatibility. The help system should show both but indicate the unified `extract` is recommended.
  - The `Workflow.NextSteps` for extract includes `"generate"` and the semantic subcommands, bridging both workflows.

### Subtask T024 -- Update `TextOutput.fs` and `JsonOutput.fs`

- **Purpose**: Add output formatters for the unified extraction result.
- **Steps**:
  1. Add to `src/Frank.Cli.Core/Output/TextOutput.fs`:

```fsharp
let formatUnifiedExtractResult (result: UnifiedExtractCommand.UnifiedExtractResult) : string =
    let sb = System.Text.StringBuilder()
    let appendLine (s: string) = sb.AppendLine(s) |> ignore

    if result.FromCache then
        appendLine "Loaded from cache (source unchanged)"
    else
        appendLine "Extracted from source"

    appendLine ""
    appendLine $"Resources: {result.ResourceCount}"
    appendLine $"  Stateful: {result.StatefulResourceCount}"
    appendLine $"  Plain: {result.PlainResourceCount}"
    appendLine $"Types analyzed: {result.TypeCount}"

    for resource in result.State.Resources do
        appendLine ""
        appendLine $"  {resource.RouteTemplate}"
        appendLine $"    Slug: {resource.ResourceSlug}"
        appendLine $"    Types: {resource.TypeInfo.Length}"

        match resource.Statechart with
        | None ->
            let methods =
                resource.HttpCapabilities
                |> List.map _.Method
                |> String.concat ", "
            appendLine $"    Methods: {methods}"
        | Some sc ->
            appendLine $"    States: {sc.StateNames |> String.concat ", "}"
            appendLine $"    Initial: {sc.InitialStateKey}"
            if not sc.GuardNames.IsEmpty then
                appendLine $"    Guards: {sc.GuardNames |> String.concat ", "}"
            for stateName in sc.StateNames do
                let methods =
                    sc.StateMetadata
                    |> Map.tryFind stateName
                    |> Option.map (fun si -> si.AllowedMethods |> String.concat ", ")
                    |> Option.defaultValue ""
                appendLine $"      {stateName}: {methods}"

    if not result.Warnings.IsEmpty then
        appendLine ""
        appendLine "Warnings:"
        for w in result.Warnings do
            appendLine $"  - {w}"

    appendLine ""
    appendLine $"Cache: {result.CacheFilePath}"
    sb.ToString()
```

  2. Add to `src/Frank.Cli.Core/Output/JsonOutput.fs`:

```fsharp
let formatUnifiedExtractResult (result: UnifiedExtractCommand.UnifiedExtractResult) : string =
    use stream = new System.IO.MemoryStream()
    use writer = new System.Text.Json.Utf8JsonWriter(stream, System.Text.Json.JsonWriterOptions(Indented = true))
    writer.WriteStartObject()
    writer.WriteString("status", "ok")
    writer.WriteBoolean("fromCache", result.FromCache)
    writer.WriteNumber("resourceCount", result.ResourceCount)
    writer.WriteNumber("statefulResourceCount", result.StatefulResourceCount)
    writer.WriteNumber("plainResourceCount", result.PlainResourceCount)
    writer.WriteNumber("typeCount", result.TypeCount)

    writer.WriteStartArray("resources")
    for r in result.State.Resources do
        writer.WriteStartObject()
        writer.WriteString("routeTemplate", r.RouteTemplate)
        writer.WriteString("resourceSlug", r.ResourceSlug)
        writer.WriteNumber("typeCount", r.TypeInfo.Length)

        match r.Statechart with
        | None ->
            writer.WriteNull("statechart")
        | Some sc ->
            writer.WriteStartObject("statechart")
            writer.WriteString("initialState", sc.InitialStateKey)
            writer.WriteStartArray("states")
            for s in sc.StateNames do writer.WriteStringValue(s)
            writer.WriteEndArray()
            writer.WriteStartArray("guards")
            for g in sc.GuardNames do writer.WriteStringValue(g)
            writer.WriteEndArray()
            writer.WriteStartObject("stateMetadata")
            for kvp in sc.StateMetadata do
                writer.WriteStartObject(kvp.Key)
                writer.WriteStartArray("allowedMethods")
                for m in kvp.Value.AllowedMethods do writer.WriteStringValue(m)
                writer.WriteEndArray()
                writer.WriteBoolean("isFinal", kvp.Value.IsFinal)
                writer.WriteEndObject()
            writer.WriteEndObject()
            writer.WriteEndObject()

        writer.WriteStartArray("httpCapabilities")
        for cap in r.HttpCapabilities do
            writer.WriteStartObject()
            writer.WriteString("method", cap.Method)
            if cap.StateKey.IsSome then
                writer.WriteString("stateKey", cap.StateKey.Value)
            else
                writer.WriteNull("stateKey")
            writer.WriteString("linkRelation", cap.LinkRelation)
            writer.WriteBoolean("isSafe", cap.IsSafe)
            writer.WriteEndObject()
        writer.WriteEndArray()

        writer.WriteStartObject("derivedFields")
        writer.WriteStartArray("orphanStates")
        for s in r.DerivedFields.OrphanStates do writer.WriteStringValue(s)
        writer.WriteEndArray()
        writer.WriteStartArray("unhandledCases")
        for c in r.DerivedFields.UnhandledCases do writer.WriteStringValue(c)
        writer.WriteEndArray()
        writer.WriteNumber("typeCoverage", r.DerivedFields.TypeCoverage)
        writer.WriteEndObject()

        writer.WriteEndObject()
    writer.WriteEndArray()

    writer.WriteStartArray("warnings")
    for w in result.Warnings do writer.WriteStringValue(w)
    writer.WriteEndArray()

    writer.WriteString("cacheFilePath", result.CacheFilePath)
    writer.WriteEndObject()
    writer.Flush()
    System.Text.Encoding.UTF8.GetString(stream.ToArray())
```

- **Files**: `src/Frank.Cli.Core/Output/TextOutput.fs` (MODIFIED), `src/Frank.Cli.Core/Output/JsonOutput.fs` (MODIFIED)
- **Notes**:
  - The JSON output structure matches the acceptance scenario in spec User Story 1 acceptance scenario 4: "each resource entry contains typeInfo, statechart (nullable), route, and derivedFields."
  - Follow existing `Utf8JsonWriter` patterns in `JsonOutput.fs`.

### Subtask T025 -- Ensure existing statechart commands continue working

- **Purpose**: Verify that `statechart parse`, `statechart validate`, and `statechart generate` still function after the new commands are added.
- **Steps**:
  1. Run the existing statechart command tests:

```bash
dotnet test test/Frank.Statecharts.Tests/
```

  2. Manually verify (or write a test) that `statechart generate` still reads from `StatechartSourceExtractor.extract` and produces output.
  3. Verify `statechart parse` and `statechart validate` are unmodified.
  4. Check that no compile order changes break existing command modules.
  5. Add compile entries for the new files to `Frank.Cli.Core.fsproj`:

```xml
<!-- Unified pipeline -->
<Compile Include="Unified/UnifiedModel.fs" />
<Compile Include="Unified/UnifiedCache.fs" />
<Compile Include="Unified/UnifiedExtractor.fs" />
<!-- After statechart commands but before output -->
<Compile Include="Commands/UnifiedExtractCommand.fs" />
<Compile Include="Commands/UnifiedGenerateCommand.fs" />
```

Place these entries in the correct compile order:
- `Unified/` files after the statechart pipeline section (they depend on `ExtractedStatechart`)
- `Commands/Unified*Command.fs` after the existing commands section but before `Output/` (output formatters reference command result types)

  6. Run full build and all tests:

```bash
dotnet build
dotnet test
```

- **Files**: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` (MODIFIED), `src/Frank.Cli/Program.fs` (verified)
- **Notes**:
  - The key risk is compile order. F# is sensitive to file ordering. The new `Unified/` files and `Commands/Unified*Command.fs` must come after their dependencies.
  - The existing `statechart` command group in `Program.fs` should be UNCHANGED. Only the top-level `extract` and `generate` commands are new.

---

## Test Strategy

- **Unified extract**: Test against tic-tac-toe sample project. Verify `ResourceCount`, `StatefulResourceCount`, `TypeCount` match expected values. Verify `FromCache = false` on first run, `FromCache = true` on second run.
- **Unified generate**: Generate ALPS from unified cache. Verify output contains state transition descriptors. Generate affordance-map. Verify JSON is valid and contains correct entries per state.
- **Backward compatibility**: Run existing `statechart parse`, `statechart validate`, `statechart generate` commands. Verify they still produce correct output.
- **Cache integration**: Extract, modify a source file, generate. Verify second command detects staleness and re-extracts.
- **Build validation**: `dotnet build` and `dotnet test` pass for all projects.

```bash
dotnet build
dotnet test
```

---

## Risks & Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Compile order conflicts with new files | Medium | Carefully place `Unified/` after statechart pipeline, `Commands/Unified*` after existing commands. Test with `dotnet build`. |
| `Program.fs` command routing changes break existing commands | Medium | Only ADD new routes (`extract`, `generate`). Do NOT modify existing `semantic` or `statechart` routes. |
| `UnifiedGenerateCommand` duplicates logic from `StatechartGenerateCommand` | Low | Acceptable duplication. The unified command is the successor; the old one stays for backward compat. Once all commands are migrated, old files become removable. |
| `affordance-map` format has no `FormatTag` enum value | Low | Handled as special case in `isAffordanceMapFormat`. A future WP can add it to `FormatTag` if needed. |
| Output formatters reference `UnifiedExtractCommand.UnifiedExtractResult` -- compile order sensitivity | Medium | Place `Commands/UnifiedExtractCommand.fs` before `Output/TextOutput.fs` and `Output/JsonOutput.fs` in fsproj. |

---

## Review Guidance

- Verify `frank-cli extract --project <fsproj>` works end-to-end (extract + cache + output).
- Verify `frank-cli generate --project <fsproj> --format affordance-map` produces valid JSON.
- Verify `frank-cli generate --project <fsproj> --format alps` reads from cache (no re-extraction if source unchanged).
- Verify `statechart parse`, `statechart validate`, `statechart generate` still work (backward compat).
- Verify compile order in `Frank.Cli.Core.fsproj` is correct (no circular dependencies, all files compile).
- Verify `HelpContent.fs` has entries for both `extract` and `generate`.
- Verify JSON output matches spec User Story 1 acceptance scenario 4 structure.
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
