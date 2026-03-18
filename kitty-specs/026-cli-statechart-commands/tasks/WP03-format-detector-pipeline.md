---
work_package_id: WP03
title: FormatDetector & FormatPipeline
lane: "for_review"
dependencies: [WP01, WP02]
base_branch: 026-cli-statechart-commands-WP03-merge-base
base_commit: 30a746dce8d843a01d8834b2b48bd8975dc4d1bc
created_at: '2026-03-18T02:33:02.628667+00:00'
subtasks:
- T014
- T015
- T016
- T017
- T018
- T019
assignee: ''
agent: "claude-opus"
shell_pid: "7358"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-16T19:12:54Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
---

# Work Package Prompt: WP03 -- FormatDetector & FormatPipeline

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

Depends on WP01 and WP02:

```bash
spec-kitty implement WP03 --base WP02
```

---

## Objectives & Success Criteria

1. Create `FormatDetector` module that maps file extensions to `FormatTag` values.
2. Create `FormatPipeline` module that generates format-specific text from `StateMachineMetadata`.
3. Support all 5 formats: WSD, ALPS, SCXML, smcat, XState.
4. Handle ambiguous `.json` extension gracefully.
5. Both modules compile cleanly with `dotnet build`.

**Success**: FormatDetector correctly identifies format from file extensions. FormatPipeline produces valid output text for each of the 5 formats from `StateMachineMetadata`.

---

## Context & Constraints

- **Spec**: `kitty-specs/026-cli-statechart-commands/spec.md` (FR-011, FR-012, FR-020, FR-027)
- **Plan**: `kitty-specs/026-cli-statechart-commands/plan.md` -- Format Detection table and Format Generation Pipelines table
- **Validation types**: `FormatTag` DU in `src/Frank.Statecharts/Validation/Types.fs` (Wsd | Alps | Scxml | Smcat | XState)
- **Existing generators** (all `internal`):
  - `Frank.Statecharts.Wsd.Generator.generate` -- takes `GenerateOptions` + `StateMachineMetadata` -> `Result<StatechartDocument, GeneratorError>`
  - `Frank.Statecharts.Wsd.Serializer.serialize` -- takes `StatechartDocument` -> `string`
  - `Frank.Statecharts.Alps.JsonGenerator.generateAlpsJson` -- `StatechartDocument` -> `string`
  - `Frank.Statecharts.Scxml.Generator.generate` -- `StatechartDocument` -> `string`
  - `Frank.Statecharts.Smcat.Generator.generate` -- takes `GenerateOptions` + `StateMachineMetadata` -> `Result<StatechartDocument, GeneratorError>`
  - `Frank.Statecharts.Smcat.Serializer.serialize` -- `StatechartDocument` -> `string`
  - `Frank.Statecharts.XState.Serializer.serialize` -- `StatechartDocument` -> `string` (from WP01)

---

## Subtasks & Detailed Guidance

### Subtask T014 -- Create FormatDetector.fs

- **Purpose**: Centralize file extension -> format tag mapping used by validate and parse commands (FR-020, FR-027).
- **Steps**:
  1. Create `src/Frank.Cli.Core/Statechart/FormatDetector.fs`
  2. Module declaration: `module Frank.Cli.Core.Statechart.FormatDetector`
  3. Open `Frank.Statecharts.Validation` for `FormatTag`
  4. Define the detection result type:
     ```fsharp
     type DetectionResult =
         | Detected of FormatTag
         | Ambiguous of candidates: FormatTag list
         | Unsupported of extension: string
     ```
  5. Implement `detect (filePath: string) : DetectionResult`:
     ```fsharp
     let detect (filePath: string) : DetectionResult =
         let lower = filePath.ToLowerInvariant()
         // Check compound extensions FIRST (order matters)
         if lower.EndsWith(".alps.json") then Detected Alps
         elif lower.EndsWith(".xstate.json") then Detected XState
         elif lower.EndsWith(".wsd") then Detected Wsd
         elif lower.EndsWith(".scxml") then Detected Scxml
         elif lower.EndsWith(".smcat") then Detected Smcat
         elif lower.EndsWith(".json") then Ambiguous [ Alps; XState ]
         else Unsupported (System.IO.Path.GetExtension(filePath))
     ```
  6. Implement `formatExtension (tag: FormatTag) : string` for generating output file names:
     ```fsharp
     let formatExtension (tag: FormatTag) : string =
         match tag with
         | Wsd -> ".wsd"
         | Alps -> ".alps.json"
         | Scxml -> ".scxml"
         | Smcat -> ".smcat"
         | XState -> ".xstate.json"
     ```
  7. Implement `supportedFormats : string` listing all supported extensions for error messages.

- **Files**: `src/Frank.Cli.Core/Statechart/FormatDetector.fs` (NEW, ~40-60 lines)
- **Parallel?**: Yes -- can proceed alongside T015.
- **Notes**: Compound extensions (`.alps.json`, `.xstate.json`) must be checked before plain `.json`. Case-insensitive matching.

### Subtask T015 -- Create FormatPipeline.fs

- **Purpose**: Centralize metadata -> format text generation pipelines used by generate and validate commands (FR-012).
- **Steps**:
  1. Create `src/Frank.Cli.Core/Statechart/FormatPipeline.fs`
  2. Module declaration: `module Frank.Cli.Core.Statechart.FormatPipeline`
  3. Open required namespaces for all format modules
  4. Define the pipeline function type:
     ```fsharp
     /// Generate format text from metadata and resource name.
     let generateFormat (format: FormatTag) (resourceName: string) (metadata: StateMachineMetadata) : Result<string, string>
     ```
  5. Implement each format pipeline per plan.md table:

     **WSD pipeline**:
     ```fsharp
     | Wsd ->
         let opts: Wsd.Generator.GenerateOptions = { ResourceName = resourceName }
         match Wsd.Generator.generate opts metadata with
         | Ok doc -> Ok (Wsd.Serializer.serialize doc)
         | Error (Wsd.Generator.UnrecognizedMachineType typeName) ->
             Error $"Unrecognized machine type: {typeName}"
     ```

     **ALPS pipeline**:
     ```fsharp
     | Alps ->
         let opts: Wsd.Generator.GenerateOptions = { ResourceName = resourceName }
         match Wsd.Generator.generate opts metadata with
         | Ok doc -> Ok (Alps.JsonGenerator.generateAlpsJson doc)
         | Error err -> Error (...)
     ```

     **SCXML pipeline**:
     ```fsharp
     | Scxml ->
         let opts: Wsd.Generator.GenerateOptions = { ResourceName = resourceName }
         match Wsd.Generator.generate opts metadata with
         | Ok doc -> Ok (Scxml.Generator.generate doc)
         | Error err -> Error (...)
     ```

     **smcat pipeline**:
     ```fsharp
     | Smcat ->
         let opts: Smcat.Generator.GenerateOptions = { ResourceName = resourceName }
         match Smcat.Generator.generate opts metadata with
         | Ok doc -> Ok (Smcat.Serializer.serialize doc)
         | Error (Smcat.Generator.UnrecognizedMachineType typeName) ->
             Error $"Unrecognized machine type: {typeName}"
     ```

     **XState pipeline**:
     ```fsharp
     | XState ->
         let opts: Wsd.Generator.GenerateOptions = { ResourceName = resourceName }
         match Wsd.Generator.generate opts metadata with
         | Ok doc -> Ok (XState.Serializer.serialize doc)
         | Error err -> Error (...)
     ```

  6. Implement `resourceSlug (routeTemplate: string) : string` helper:
     ```fsharp
     /// Extract a filename-safe slug from a route template.
     /// e.g., "/games/{id}" -> "games", "/api/orders/{orderId}" -> "api-orders"
     let resourceSlug (routeTemplate: string) : string =
         routeTemplate
             .TrimStart('/')
             .Split('/')
         |> Array.filter (fun s -> not (s.StartsWith("{") && s.EndsWith("}")))
         |> Array.map (fun s -> s.Replace(" ", "-"))
         |> String.concat "-"
         |> fun s -> if System.String.IsNullOrEmpty(s) then "resource" else s
     ```

  7. Implement `allFormats : FormatTag list`:
     ```fsharp
     let allFormats : FormatTag list = [ Wsd; Alps; Scxml; Smcat; XState ]
     ```

- **Files**: `src/Frank.Cli.Core/Statechart/FormatPipeline.fs` (NEW, ~100-140 lines)
- **Parallel?**: Yes -- can proceed alongside T014.
- **Notes**: The WSD Generator is the central metadata-to-AST converter. All format pipelines except smcat go through it. ALPS and SCXML generators take `StatechartDocument` directly (no intermediate mapper step). The smcat pipeline uses its own `Smcat.Generator.generate` -> `Smcat.Serializer.serialize` chain. All parsers return `Ast.ParseResult` directly (no mapper step). Check function signatures by reading the source files -- they may have slightly different names or signatures than documented here.

### Subtask T016 -- Implement format detection logic

- **Purpose**: This is covered by T014. Included for tracking -- verify edge cases.
- **Steps**: Verify the following edge cases work:
  1. `.ALPS.JSON` (uppercase) -> detected as ALPS (case-insensitive)
  2. `path/to/game.wsd` -> detected as WSD (full path, not just extension)
  3. `game.json` -> Ambiguous (plain `.json`)
  4. `game.txt` -> Unsupported
  5. `game.xstate.json` -> detected as XState (not ambiguous `.json`)
- **Files**: `src/Frank.Cli.Core/Statechart/FormatDetector.fs`

### Subtask T017 -- Implement generation pipelines

- **Purpose**: This is covered by T015. Included for tracking -- verify each pipeline.
- **Steps**: Verify each format pipeline produces non-empty output from valid `StateMachineMetadata`. Check function signatures of existing generators by reading source.
- **Files**: `src/Frank.Cli.Core/Statechart/FormatPipeline.fs`
- **Notes**: Read the actual generator source files to verify function signatures:
  - `src/Frank.Statecharts/Alps/JsonGenerator.fs` -- `generateAlpsJson` takes `StatechartDocument` directly
  - `src/Frank.Statecharts/Scxml/Generator.fs` -- `generate` takes `StatechartDocument` directly
  - `src/Frank.Statecharts/Smcat/Generator.fs` -- `generate` returns `Result<StatechartDocument, GeneratorError>`
  - `src/Frank.Statecharts/Smcat/Serializer.fs` -- `serialize` takes `StatechartDocument` -> `string`

### Subtask T018 -- Add compile entries to Frank.Cli.Core.fsproj

- **Purpose**: Register the new source files in the project compile order.
- **Steps**:
  1. Open `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
  2. Add compile entries **after** the `StatechartExtractor.fs` entry (from WP02):
     ```xml
     <Compile Include="Statechart/FormatDetector.fs" />
     <Compile Include="Statechart/FormatPipeline.fs" />
     ```
  3. FormatDetector must come before FormatPipeline (pipeline may reference detector types).
  4. Both must come before command files that depend on them.

- **Files**: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`

### Subtask T019 -- Verify modules compile

- **Purpose**: Confirm all changes compile cleanly.
- **Steps**:
  1. Run `dotnet build` from the repository root
  2. Fix any compilation errors, especially:
     - Internal module access (verify `InternalsVisibleTo` from WP01 works)
     - Function signature mismatches with existing format modules
     - Missing namespace opens
- **Files**: N/A

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Internal module function signatures differ from plan | Read actual source files before coding pipeline. Adapt signatures. |
| ALPS/SCXML generator signatures differ from plan | Read actual source files before coding pipeline. Both take `StatechartDocument` directly. |
| Compound extension detection order | Test compound extensions are checked before plain `.json`. |

---

## Review Guidance

- Verify FormatDetector handles all 5 format extensions plus ambiguous `.json`.
- Verify FormatPipeline delegates to the correct existing library functions.
- Verify `resourceSlug` produces sensible filenames from route templates.
- Verify compile order in fsproj is correct.
- Verify `dotnet build` passes cleanly.

---

## Activity Log

- 2026-03-16T19:12:54Z -- system -- lane=planned -- Prompt created.
- 2026-03-18T02:33:02Z – claude-opus – shell_pid=7358 – lane=doing – Assigned agent via workflow command
- 2026-03-18T02:35:06Z – claude-opus – shell_pid=7358 – lane=for_review – Ready for review: FormatDetector and FormatPipeline modules. All 5 format pipelines route through correct generators with proper StatechartDocument→format-specific document mapping.
