---
work_package_id: "WP07"
title: "Validate Command"
lane: "doing"
dependencies: ["WP02", "WP03", "WP04"]
subtasks:
  - "T040"
  - "T041"
  - "T042"
  - "T043"
  - "T044"
  - "T045"
  - "T046"
  - "T047"
assignee: ""
agent: "claude-opus"
shell_pid: "10795"
review_status: ""
reviewed_by: ""
history:
  - timestamp: "2026-03-16T19:12:54Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP07 -- Validate Command

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

Depends on WP02, WP03, and WP04:

```bash
spec-kitty implement WP07 --base WP04
```

Note: WP04 depends on WP01, and WP03 depends on WP01+WP02. If WP03 is not yet merged into WP04's base, you may need to use `--base WP03` instead, or merge both into the feature branch first.

---

## Objectives & Success Criteria

1. Create `StatechartValidateCommand` module implementing `frank statechart validate <spec-file>... <assembly>`.
2. Parse spec files by detecting format from file extension.
3. Generate code-truth artifact from extracted assembly metadata.
4. Run cross-format validator and produce validation report.
5. Support `--output-format text|json` output.
6. Exit with non-zero code when validation has failures.

**Success**: Command takes spec files + assembly path, produces a validation report showing passed/failed/skipped checks with actionable diagnostics.

---

## Context & Constraints

- **Spec**: `kitty-specs/026-cli-statechart-commands/spec.md` -- User Story 3 (FR-019 through FR-025)
- **Plan**: `kitty-specs/026-cli-statechart-commands/plan.md` -- D-006 (code-truth artifact), Validate Command Pipeline
- **Dependencies**:
  - `StatechartExtractor.extract` from WP02
  - `FormatDetector.detect` from WP03
  - `Frank.Statecharts.Validation.Pipeline` from spec 025 (provides `validateSources` / `validateSourcesWithRules` for parser dispatch + rule composition)
  - `ValidationReportFormatter.formatText/formatJson` from WP04
  - `Validator.validate` from `src/Frank.Statecharts/Validation/Validator.fs`
  - `SelfConsistencyRules.rules` and `CrossFormatRules.rules` from `Validator.fs`
- **Key decision D-006**: Code-truth artifact uses `Wsd.Generator.generate` to produce a `StatechartDocument`, wrapped as `FormatArtifact { Format = Wsd; Document = doc }`.

---

## Subtasks & Detailed Guidance

### Subtask T040 -- Create StatechartValidateCommand.fs

- **Purpose**: Implement the validate command logic.
- **Steps**:
  1. Create `src/Frank.Cli.Core/Commands/StatechartValidateCommand.fs`
  2. Module declaration: `module Frank.Cli.Core.Commands.StatechartValidateCommand`
  3. Define result type:
     ```fsharp
     type ValidateResult =
         { Report: ValidationReport
           HasFailures: bool }
     ```
  4. Implement `execute`:
     ```fsharp
     let execute
         (specFiles: string list)
         (assemblyPath: string)
         : Result<ValidateResult, string>
     ```

- **Files**: `src/Frank.Cli.Core/Commands/StatechartValidateCommand.fs` (NEW, ~120-180 lines)

### Subtask T041 -- Implement spec file parsing

- **Purpose**: Parse each spec file into a `FormatArtifact` for validation (FR-019, FR-020).
- **Steps**:
  1. For each spec file path:
     a. Detect format using `FormatDetector.detect filePath`
     b. Read file contents: `System.IO.File.ReadAllText(filePath)`
     c. Dispatch to the appropriate parser based on format:

     All 5 parsers return `Ast.ParseResult` directly (with `Document`, `Errors`, `Warnings` fields) -- no mapper step is needed:

     ```fsharp
     let parseSpecFile (filePath: string) (format: FormatTag) : Result<FormatArtifact, string> =
         let content = File.ReadAllText(filePath)
         let result =
             match format with
             | Wsd   -> Wsd.Parser.parseWsd content
             | Alps  -> Alps.JsonParser.parseAlpsJson content
             | Scxml -> Scxml.Parser.parseString content
             | Smcat -> Smcat.Parser.parseSmcat content
             | XState -> XState.Deserializer.deserialize content
         if result.Errors |> List.isEmpty then
             Ok { Format = format; Document = result.Document }
         else
             Error (result.Errors |> List.map string |> String.concat "; ")
     ```

     **Note**: The `Pipeline` module from spec 025 (`Frank.Statecharts.Validation.Pipeline`) provides `validateSources` and `validateSourcesWithRules` which handle parser dispatch and rule composition automatically. However, the validate command adds a code-truth artifact that Pipeline does not support, so calling `Validator.validate` directly (as shown in T043) is the appropriate approach. The parser dispatch above mirrors what Pipeline does internally.

  2. Handle `Ambiguous` detection result: error message listing supported formats with `--format` flag suggestion.
  3. Handle `Unsupported` detection result: error message listing supported extensions.
  4. Handle file not found: check `File.Exists` before reading.

- **Files**: `src/Frank.Cli.Core/Commands/StatechartValidateCommand.fs`
- **Notes**: All 5 parsers return `Ast.ParseResult` directly -- there are no separate mapper modules or intermediate types. Verify return types against actual parser source files if needed.

### Subtask T042 -- Implement code-truth artifact generation

- **Purpose**: Generate a `FormatArtifact` from extracted assembly metadata to serve as the "code truth" (D-006, FR-021).
- **Steps**:
  1. After extracting metadata from the assembly, for each `ExtractedStatechart`:
     ```fsharp
     let codeTruthArtifact (extracted: ExtractedStatechart) : Result<FormatArtifact, string> =
         let opts: Wsd.Generator.GenerateOptions =
             { ResourceName = FormatPipeline.resourceSlug extracted.RouteTemplate }
         match Wsd.Generator.generate opts extracted.RawMetadata with
         | Ok doc -> Ok { Format = Wsd; Document = doc }
         | Error (Wsd.Generator.UnrecognizedMachineType t) ->
             Error $"Unrecognized machine type: {t}"
     ```
  2. The code-truth artifact uses `FormatTag.Wsd` since it was generated by the WSD generator.
  3. This artifact is included alongside parsed spec file artifacts in the validation set.

- **Files**: `src/Frank.Cli.Core/Commands/StatechartValidateCommand.fs`
- **Notes**: For multiple resources, you may need to decide whether to validate each resource separately or combine. The simplest approach: validate all artifacts together (cross-format rules will match by state names).

### Subtask T043 -- Implement validation orchestration

- **Purpose**: Run the cross-format validator with all artifacts (FR-022).
- **Steps**:
  1. Combine spec file artifacts + code-truth artifacts into a single list:
     ```fsharp
     let allArtifacts = specArtifacts @ codeTruthArtifacts
     ```
  2. Get all validation rules:
     ```fsharp
     let rules = SelfConsistencyRules.rules @ CrossFormatRules.rules
     ```
  3. Run validation:
     ```fsharp
     let report = Validator.validate rules allArtifacts
     ```
  4. Return `ValidateResult`:
     ```fsharp
     Ok { Report = report; HasFailures = report.TotalFailures > 0 }
     ```

- **Files**: `src/Frank.Cli.Core/Commands/StatechartValidateCommand.fs`
- **Notes**: `Validator.validate` is in `src/Frank.Statecharts/Validation/Validator.fs`. It handles format filtering (rules with `RequiredFormats` are skipped when required formats are not present).

### Subtask T044 -- Implement text output for validate

- **Purpose**: Format validation report as human-readable text (FR-023, FR-024).
- **Steps**:
  1. Add to `src/Frank.Cli.Core/Output/TextOutput.fs`:
     ```fsharp
     let formatStatechartValidateResult (result: StatechartValidateCommand.ValidateResult) : string =
         ValidationReportFormatter.formatText result.Report
     ```
  2. Or inline the call in `Program.fs` -- either approach works.
  3. The actual formatting is done by `ValidationReportFormatter.formatText` from WP04.

- **Files**: `src/Frank.Cli.Core/Output/TextOutput.fs`

### Subtask T045 -- Implement JSON output for validate

- **Purpose**: Format validation report as structured JSON (FR-024).
- **Steps**:
  1. Add to `src/Frank.Cli.Core/Output/JsonOutput.fs`:
     ```fsharp
     let formatStatechartValidateResult (result: StatechartValidateCommand.ValidateResult) : string =
         ValidationReportFormatter.formatJson result.Report
     ```
  2. The actual formatting is done by `ValidationReportFormatter.formatJson` from WP04.

- **Files**: `src/Frank.Cli.Core/Output/JsonOutput.fs`

### Subtask T046 -- Handle errors and exit codes

- **Purpose**: Proper error handling and exit code behavior (FR-025).
- **Steps**:
  1. **Unsupported format extension**: return `Error "Unsupported file extension '.xyz'. Supported: .wsd, .alps.json, .scxml, .smcat, .xstate.json"` -> exit code 1.
  2. **File not found**: return `Error "Spec file not found: game.wsd"` -> exit code 1.
  3. **File read error**: return `Error "Cannot read file: {path}: {reason}"` -> exit code 1.
  4. **Validation failures**: the command returns `Ok result` even when there are failures. The **CLI wiring** (WP09) sets exit code 1 when `result.HasFailures = true`.
  5. **Assembly load error**: propagated from `StatechartExtractor.extract` as `Error msg`.

- **Files**: `src/Frank.Cli.Core/Commands/StatechartValidateCommand.fs`

### Subtask T047 -- Add compile entry to Frank.Cli.Core.fsproj

- **Purpose**: Register the new command module.
- **Steps**:
  1. Add after `StatechartGenerateCommand.fs`:
     ```xml
     <Compile Include="Commands/StatechartValidateCommand.fs" />
     ```

- **Files**: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Parser function signature mismatches | Read actual parser source files before implementing dispatch. |
| Multiple resources with overlapping state names | Validate per-resource or document that cross-resource validation is combined. |
| Spec file encoding issues | Use `File.ReadAllText` which handles BOM. |

---

## Review Guidance

- Verify spec file parsing dispatches to the correct parser for each format.
- Verify code-truth artifact is generated using `Wsd.Generator.generate` (D-006).
- Verify validation report includes both self-consistency and cross-format checks.
- Verify exit code is non-zero when validation has failures.
- Verify unsupported file extensions produce clear error with list of supported formats.
- Verify `dotnet build` passes cleanly.

---

## Activity Log

- 2026-03-16T19:12:54Z -- system -- lane=planned -- Prompt created.
- 2026-03-18T02:48:28Z – unknown – lane=for_review – Ready for review: StatechartValidateCommand with cross-format validation. Build clean.
- 2026-03-18T02:48:32Z – claude-opus – shell_pid=10795 – lane=doing – Started review via workflow command
