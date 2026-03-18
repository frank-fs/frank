---
work_package_id: WP06
title: Generate Command
lane: "for_review"
dependencies: [WP02, WP03]
base_branch: 026-cli-statechart-commands-WP06-merge-base
base_commit: 4bb9bf40314cb20e63171875c5eef9b2c691d9e9
created_at: '2026-03-18T02:39:24.507016+00:00'
subtasks:
- T032
- T033
- T034
- T035
- T036
- T037
- T038
- T039
assignee: ''
agent: "claude-opus"
shell_pid: "8948"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-16T19:12:54Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
---

# Work Package Prompt: WP06 -- Generate Command

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

Depends on WP02 and WP03:

```bash
spec-kitty implement WP06 --base WP03
```

---

## Objectives & Success Criteria

1. Create `StatechartGenerateCommand` module implementing `frank statechart generate --format <fmt> <assembly>`.
2. Support format values: `wsd`, `alps`, `scxml`, `smcat`, `xstate`, `all`.
3. Support `--output <directory>` for file writing with naming convention `{resourceSlug}.{extension}`.
4. Support `--resource <name>` to filter to a single resource.
5. Support stdout output with resource/format headers when `--output` is not specified.
6. Handle zero-resources gracefully.

**Success**: Command generates correct format output for each of the 5 formats, writes files when `--output` specified, filters by resource when `--resource` specified.

---

## Context & Constraints

- **Spec**: `kitty-specs/026-cli-statechart-commands/spec.md` -- User Story 2 (FR-010 through FR-016)
- **Plan**: `kitty-specs/026-cli-statechart-commands/plan.md` -- D-007 (output format disambiguation)
- **Dependencies**:
  - `StatechartExtractor.extract` from WP02
  - `FormatPipeline.generateFormat`, `FormatPipeline.resourceSlug`, `FormatPipeline.allFormats` from WP03
  - `FormatDetector.formatExtension` from WP03
- **Key decision D-007**: `--format` is for the target notation format. `--output-format` (text/json) is for the command's own status messages.

---

## Subtasks & Detailed Guidance

### Subtask T032 -- Create StatechartGenerateCommand.fs

- **Purpose**: Implement the generate command logic.
- **Steps**:
  1. Create `src/Frank.Cli.Core/Commands/StatechartGenerateCommand.fs`
  2. Module declaration: `module Frank.Cli.Core.Commands.StatechartGenerateCommand`
  3. Define types:
     ```fsharp
     type GeneratedArtifact =
         { ResourceSlug: string
           RouteTemplate: string
           Format: FormatTag
           Content: string
           FilePath: string option }

     type GenerateResult =
         { Artifacts: GeneratedArtifact list
           OutputDirectory: string option }
     ```
  4. Implement `execute`:
     ```fsharp
     let execute
         (assemblyPath: string)
         (format: string)
         (outputDir: string option)
         (resourceFilter: string option)
         : Result<GenerateResult, string>
     ```

- **Files**: `src/Frank.Cli.Core/Commands/StatechartGenerateCommand.fs` (NEW, ~120-180 lines)

### Subtask T033 -- Implement single-format generation

- **Purpose**: Generate output in a single specified format.
- **Steps**:
  1. Parse the `format` string to determine which format(s) to generate:
     ```fsharp
     let parseFormat (s: string) : Result<FormatTag list, string> =
         match s.ToLowerInvariant() with
         | "wsd" -> Ok [ Wsd ]
         | "alps" -> Ok [ Alps ]
         | "scxml" -> Ok [ Scxml ]
         | "smcat" -> Ok [ Smcat ]
         | "xstate" -> Ok [ XState ]
         | "all" -> Ok FormatPipeline.allFormats
         | other -> Error $"Unknown format: '{other}'. Supported: wsd, alps, scxml, smcat, xstate, all"
     ```
  2. For each resource and each requested format, call `FormatPipeline.generateFormat`.
  3. Collect results into `GeneratedArtifact` records.

- **Files**: `src/Frank.Cli.Core/Commands/StatechartGenerateCommand.fs`

### Subtask T034 -- Implement --format all generation

- **Purpose**: Generate output in all 5 formats at once.
- **Steps**:
  1. When `format = "all"`, iterate `FormatPipeline.allFormats` for each resource.
  2. This produces `n_resources * 5` artifacts.
  3. Each artifact has a unique `(resourceSlug, format)` pair.

- **Files**: `src/Frank.Cli.Core/Commands/StatechartGenerateCommand.fs`

### Subtask T035 -- Implement --output file writing

- **Purpose**: Write generated artifacts to files in the specified output directory (FR-013, FR-015).
- **Steps**:
  1. When `outputDir` is `Some dir`:
     - Create the directory if it doesn't exist (`Directory.CreateDirectory`)
     - For each artifact, compute the file path:
       ```fsharp
       let fileName = $"{artifact.ResourceSlug}{FormatDetector.formatExtension artifact.Format}"
       let filePath = Path.Combine(dir, fileName)
       ```
     - Write the content to the file:
       ```fsharp
       File.WriteAllText(filePath, artifact.Content)
       ```
     - Set `artifact.FilePath` to `Some filePath`
  2. File naming convention: `{resourceSlug}.{format-extension}` (FR-013)
     - e.g., `games.wsd`, `games.alps.json`, `games.scxml`, `games.smcat`, `games.xstate.json`
  3. Multiple resources: `games.wsd`, `orders.wsd`, etc.

- **Files**: `src/Frank.Cli.Core/Commands/StatechartGenerateCommand.fs`
- **Notes**: FR-015 requires auto-creating the directory.

### Subtask T036 -- Implement --resource filter

- **Purpose**: Filter generation to a single named resource (FR-016).
- **Steps**:
  1. When `resourceFilter` is `Some name`:
     ```fsharp
     let filtered =
         machines
         |> List.filter (fun m ->
             let slug = FormatPipeline.resourceSlug m.RouteTemplate
             slug = name || m.RouteTemplate.Contains(name))
     ```
  2. If no matching resource found, return error: `"No resource matching '{name}' found. Available: {slugList}"`
  3. Apply filter before generation, not after.

- **Files**: `src/Frank.Cli.Core/Commands/StatechartGenerateCommand.fs`

### Subtask T037 -- Implement stdout output with headers

- **Purpose**: When `--output` is not specified, write all artifacts to stdout with headers (FR-014).
- **Steps**:
  1. Add formatters to `TextOutput.fs` and `JsonOutput.fs`:

  **Text output** (for status messages -- the generated artifact content itself goes to stdout):
  ```
  === Resource: /games/{id} (games) ===

  --- WSD ---
  <wsd content>

  --- ALPS ---
  <alps json content>
  ```

  **JSON output** (for `--output-format json` status):
  ```json
  {
    "status": "ok",
    "artifacts": [
      {
        "resourceSlug": "games",
        "routeTemplate": "/games/{id}",
        "format": "wsd",
        "filePath": "./output/games.wsd"
      }
    ]
  }
  ```

  2. When writing to stdout (no `--output`), write the generated content directly with format/resource separators.
  3. When writing to files (with `--output`), write status summary (files written) to stdout.

- **Files**: `src/Frank.Cli.Core/Output/TextOutput.fs`, `src/Frank.Cli.Core/Output/JsonOutput.fs`

### Subtask T038 -- Handle zero-resources and directory creation

- **Purpose**: Gracefully handle edge cases.
- **Steps**:
  1. Zero resources: output "No state machines found." and exit 0.
  2. Directory creation: `Directory.CreateDirectory(outputDir)` (creates recursively, no error if exists).
  3. Resource filter with no match: return error with available resource names.

- **Files**: `src/Frank.Cli.Core/Commands/StatechartGenerateCommand.fs`

### Subtask T039 -- Add compile entry to Frank.Cli.Core.fsproj

- **Purpose**: Register the new command module.
- **Steps**:
  1. Add after `StatechartExtractCommand.fs`:
     ```xml
     <Compile Include="Commands/StatechartGenerateCommand.fs" />
     ```

- **Files**: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Format pipeline errors for some formats | Collect errors per format, don't abort entire generation. Report partial results. |
| File naming collisions (two resources with same slug) | Append numeric suffix if collision detected. |
| `--format` / `--output-format` confusion | Document clearly in help text. Use separate option names (D-007). |

---

## Review Guidance

- Verify each of the 5 format pipelines produces output.
- Verify `--output` creates files with correct names and extensions.
- Verify `--resource` filter works and produces clear error for no match.
- Verify stdout output has clear resource/format separators.
- Verify zero-resources produces "no state machines found" message with exit 0.
- Verify `dotnet build` passes cleanly.

---

## Activity Log

- 2026-03-16T19:12:54Z -- system -- lane=planned -- Prompt created.
- 2026-03-18T02:39:24Z – claude-opus – shell_pid=8948 – lane=doing – Assigned agent via workflow command
- 2026-03-18T02:55:40Z – claude-opus – shell_pid=8948 – lane=for_review – Ready for review: StatechartGenerateCommand with multi-format generation, file output, and resource filtering.
