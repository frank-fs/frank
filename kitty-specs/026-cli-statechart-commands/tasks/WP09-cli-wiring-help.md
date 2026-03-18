---
work_package_id: "WP09"
title: "CLI Wiring & Help Content"
lane: "doing"
dependencies: ["WP05", "WP06", "WP07", "WP08"]
subtasks:
  - "T055"
  - "T056"
  - "T057"
  - "T058"
  - "T059"
  - "T060"
  - "T061"
assignee: ""
agent: "claude-opus"
shell_pid: "14998"
review_status: ""
reviewed_by: ""
history:
  - timestamp: "2026-03-16T19:12:54Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP09 -- CLI Wiring & Help Content

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

Depends on WP05, WP06, WP07, WP08 (all command modules):

```bash
spec-kitty implement WP09 --base WP08
```

Note: This WP depends on all four command WPs. Ensure they are all merged into the feature branch before starting.

---

## Objectives & Success Criteria

1. Restructure existing commands under `semantic` parent command in `Program.fs` (FR-036).
2. Add `statechart` parent command with 4 subcommands (`extract`, `generate`, `validate`, `parse`) (FR-035).
3. Keep `status` and `help` as top-level commands.
4. Extend help system to support hierarchical command names (FR-038).
5. Register help metadata for all restructured and new commands in `HelpContent.fs`.
6. Add `statechart-workflows` help topic, rename `workflows` to `semantic-workflows` (FR-039).
7. All commands accessible and functional (SC-007).

**Success**: `frank-cli semantic extract`, `frank-cli statechart extract`, etc. all work. `frank-cli help statechart extract` and `frank-cli help semantic extract` return distinct help. `frank-cli help` lists both command groups.

---

## Context & Constraints

- **Spec**: `kitty-specs/026-cli-statechart-commands/spec.md` (FR-035 through FR-039)
- **Plan**: `kitty-specs/026-cli-statechart-commands/plan.md` (D-001, D-007, D-009, D-010)
- **Existing CLI structure**: `src/Frank.Cli/Program.fs` -- imperative System.CommandLine pattern with flat commands
- **Existing help system**: `src/Frank.Cli.Core/Help/HelpContent.fs` -- hardcoded `CommandHelp` records, flat name lookup
- **Help types**: `src/Frank.Cli.Core/Help/HelpTypes.fs` -- `CommandHelp`, `CommandExample`, `WorkflowPosition`
- **Key decision D-001**: Follow existing imperative pattern. Restructure into `semantic` + `statechart` parent commands.
- **Key decision D-007**: All commands use `--output-format` for text/json output rendering. `--format` is for notation format on generate and parse.
- **Key decision D-009**: Existing commands move under `semantic` parent. No backward compat needed (unreleased).
- **Key decision D-010**: Help uses hierarchical names (`"semantic extract"`, `"statechart extract"`).

---

## Subtasks & Detailed Guidance

### Subtask T055 -- Restructure Program.fs with semantic and statechart parent commands

- **Purpose**: Create `semantic` and `statechart` parent command groups (FR-035, FR-036, FR-037).
- **Steps**:
  1. Open `src/Frank.Cli/Program.fs`
  2. Create the `semantic` parent command and move the 5 existing commands under it:
     ```fsharp
     // ── semantic ──
     let semanticCmd = Command("semantic")
     semanticCmd.Description <- "Semantic resource extraction pipeline: OWL ontology and SHACL shapes from F# types"
     root.Subcommands.Add(semanticCmd)
     ```
  3. Move existing command registrations (`extractCmd`, `clarifyCmd`, `validateCmd`, `diffCmd`, `compileCmd`) from `root.Subcommands.Add(...)` to `semanticCmd.Subcommands.Add(...)`.
  4. Create the `statechart` parent command:
     ```fsharp
     // ── statechart ──
     let statechartCmd = Command("statechart")
     statechartCmd.Description <- "Statechart pipeline: extract, generate, validate, and parse state machine artifacts"
     root.Subcommands.Add(statechartCmd)
     ```
  5. `status` and `help` remain as `root.Subcommands.Add(...)`.
  6. The 4 statechart subcommands will be added in the following subtasks.

- **Files**: `src/Frank.Cli/Program.fs`
- **Notes**: Both parent commands are groups with no handler of their own. The existing command handlers are unchanged — only their registration moves from root to the `semantic` parent.

### Subtask T056 -- Wire extract subcommand

- **Purpose**: Add `frank statechart extract <assembly> [--output-format text|json]` subcommand.
- **Steps**:
  1. Create the extract subcommand:
     ```fsharp
     let scExtractCmd = Command("extract")
     scExtractCmd.Description <- "Extract state machine metadata from a compiled assembly"

     let scExtractAssemblyArg = Argument<string>("assembly")
     scExtractAssemblyArg.Description <- "Path to the compiled assembly (.dll)"
     scExtractCmd.Arguments.Add(scExtractAssemblyArg)

     let scExtractFormatOpt = Option<string>("--output-format")
     scExtractFormatOpt.Description <- "Output format (text|json)"
     scExtractFormatOpt.DefaultValueFactory <- (fun _ -> "text")
     scExtractCmd.Options.Add(scExtractFormatOpt)
     ```
  2. Set the action handler following the existing pattern:
     ```fsharp
     scExtractCmd.SetAction(fun parseResult ->
         let assembly = parseResult.GetValue(scExtractAssemblyArg)
         let format = parseResult.GetValue(scExtractFormatOpt)

         let result = StatechartExtractCommand.execute assembly

         match result with
         | Ok r ->
             let output =
                 if format = "json" then
                     JsonOutput.formatStatechartExtractResult r
                 else
                     TextOutput.formatStatechartExtractResult r
             Console.WriteLine(output)
         | Error e ->
             Environment.ExitCode <- 1
             let output =
                 if format = "json" then JsonOutput.formatError e
                 else TextOutput.formatError e
             Console.Error.WriteLine(output))
     ```
  3. Add to parent: `statechartCmd.Subcommands.Add(scExtractCmd)`

- **Files**: `src/Frank.Cli/Program.fs`
- **Notes**: Use `Argument<string>` for the assembly path (positional), `Option<string>` for output-format.

### Subtask T057 -- Wire generate subcommand

- **Purpose**: Add `frank statechart generate --format <fmt> <assembly> [--output <dir>] [--resource <name>] [--output-format text|json]`.
- **Steps**:
  1. Create the generate subcommand with multiple options:
     ```fsharp
     let scGenerateCmd = Command("generate")
     scGenerateCmd.Description <- "Generate statechart spec artifacts from a compiled assembly"

     let scGenAssemblyArg = Argument<string>("assembly")
     scGenAssemblyArg.Description <- "Path to the compiled assembly (.dll)"
     scGenerateCmd.Arguments.Add(scGenAssemblyArg)

     let scGenFormatOpt = Option<string>("--format")
     scGenFormatOpt.Description <- "Target notation format (wsd|alps|scxml|smcat|xstate|all)"
     scGenFormatOpt.Required <- true
     scGenerateCmd.Options.Add(scGenFormatOpt)

     let scGenOutputOpt = Option<string>("--output")
     scGenOutputOpt.Description <- "Output directory for generated artifacts"
     scGenerateCmd.Options.Add(scGenOutputOpt)

     let scGenResourceOpt = Option<string>("--resource")
     scGenResourceOpt.Description <- "Generate for a specific resource only"
     scGenerateCmd.Options.Add(scGenResourceOpt)

     let scGenOutputFormatOpt = Option<string>("--output-format")
     scGenOutputFormatOpt.Description <- "Format for status messages (text|json)"
     scGenOutputFormatOpt.DefaultValueFactory <- (fun _ -> "text")
     scGenerateCmd.Options.Add(scGenOutputFormatOpt)
     ```
  2. Set the action handler:
     ```fsharp
     scGenerateCmd.SetAction(fun parseResult ->
         let assembly = parseResult.GetValue(scGenAssemblyArg)
         let format = parseResult.GetValue(scGenFormatOpt)
         let outputDir =
             let v = parseResult.GetValue(scGenOutputOpt)
             if String.IsNullOrEmpty v then None else Some v
         let resource =
             let v = parseResult.GetValue(scGenResourceOpt)
             if String.IsNullOrEmpty v then None else Some v
         let outputFormat = parseResult.GetValue(scGenOutputFormatOpt)

         let result = StatechartGenerateCommand.execute assembly format outputDir resource

         match result with
         | Ok r ->
             // When no --output, write generated content to stdout
             if outputDir.IsNone then
                 // Write generated artifacts with headers
                 for artifact in r.Artifacts do
                     Console.WriteLine($"=== {artifact.RouteTemplate} ({artifact.Format}) ===")
                     Console.WriteLine(artifact.Content)
                     Console.WriteLine()
             else
                 // Write status message about files written
                 let output =
                     if outputFormat = "json" then
                         JsonOutput.formatStatechartGenerateResult r
                     else
                         TextOutput.formatStatechartGenerateResult r
                 Console.WriteLine(output)
         | Error e ->
             Environment.ExitCode <- 1
             let output =
                 if outputFormat = "json" then JsonOutput.formatError e
                 else TextOutput.formatError e
             Console.Error.WriteLine(output))
     ```
  3. Add to parent: `statechartCmd.Subcommands.Add(scGenerateCmd)`

- **Files**: `src/Frank.Cli/Program.fs`
- **Notes**: D-007: `--format` is for notation format (generate/import only), `--output-format` is for text/json output rendering (all commands).

### Subtask T058 -- Wire validate subcommand

- **Purpose**: Add `frank statechart validate <spec-file>... <assembly> [--output-format text|json]`.
- **Steps**:
  1. Create the validate subcommand:
     ```fsharp
     let scValidateCmd = Command("validate")
     scValidateCmd.Description <- "Validate spec files against a compiled assembly"

     // Multiple spec files as a variadic argument
     let scValSpecFilesArg = Argument<string array>("spec-files")
     scValSpecFilesArg.Description <- "One or more spec files to validate"
     scValSpecFilesArg.Arity <- ArgumentArity.OneOrMore
     scValidateCmd.Arguments.Add(scValSpecFilesArg)

     let scValAssemblyOpt = Option<string>("--assembly")
     scValAssemblyOpt.Description <- "Path to the compiled assembly (.dll)"
     scValAssemblyOpt.Required <- true
     scValidateCmd.Options.Add(scValAssemblyOpt)

     let scValFormatOpt = Option<string>("--output-format")
     scValFormatOpt.Description <- "Output format (text|json)"
     scValFormatOpt.DefaultValueFactory <- (fun _ -> "text")
     scValidateCmd.Options.Add(scValFormatOpt)
     ```

     Note: Using `--assembly` as an option (not a positional argument) to avoid ambiguity with the variadic spec-files argument.

  2. Set the action handler:
     ```fsharp
     scValidateCmd.SetAction(fun parseResult ->
         let specFiles = parseResult.GetValue(scValSpecFilesArg) |> Array.toList
         let assembly = parseResult.GetValue(scValAssemblyOpt)
         let format = parseResult.GetValue(scValFormatOpt)

         let result = StatechartValidateCommand.execute specFiles assembly

         match result with
         | Ok r ->
             let output =
                 if format = "json" then
                     JsonOutput.formatStatechartValidateResult r
                 else
                     TextOutput.formatStatechartValidateResult r
             Console.WriteLine(output)
             if r.HasFailures then
                 Environment.ExitCode <- 1
         | Error e ->
             Environment.ExitCode <- 1
             let output =
                 if format = "json" then JsonOutput.formatError e
                 else TextOutput.formatError e
             Console.Error.WriteLine(output))
     ```
  3. Add to parent: `statechartCmd.Subcommands.Add(scValidateCmd)`

- **Files**: `src/Frank.Cli/Program.fs`
- **Notes**: The assembly is an option (not positional) because spec-files is variadic. Exit code 1 when validation has failures.

### Subtask T059 -- Wire parse subcommand

- **Purpose**: Add `frank statechart parse <spec-file> [--format <fmt>] [--output-format text|json]`.
- **Steps**:
  1. Create the parse subcommand:
     ```fsharp
     let scParseCmd = Command("parse")
     scParseCmd.Description <- "Parse a spec file and output the StatechartDocument as JSON"

     let scParseFileArg = Argument<string>("spec-file")
     scParseFileArg.Description <- "Path to the spec file to parse"
     scParseCmd.Arguments.Add(scParseFileArg)

     let scParseFormatOpt = Option<string>("--format")
     scParseFormatOpt.Description <- "Notation format override (wsd|alps|scxml|smcat|xstate) for ambiguous file extensions"
     scParseCmd.Options.Add(scParseFormatOpt)

     let scParseOutputFormatOpt = Option<string>("--output-format")
     scParseOutputFormatOpt.Description <- "Output format (text|json)"
     scParseOutputFormatOpt.DefaultValueFactory <- (fun _ -> "json")
     scParseCmd.Options.Add(scParseOutputFormatOpt)
     ```
  2. Set the action handler:
     ```fsharp
     scParseCmd.SetAction(fun parseResult ->
         let specFile = parseResult.GetValue(scParseFileArg)
         let formatOverride =
             let v = parseResult.GetValue(scParseFormatOpt)
             if String.IsNullOrEmpty v then None else Some v

         let result = StatechartParseCommand.execute specFile formatOverride

         match result with
         | Ok r ->
             // Import always outputs JSON (the parsed document)
             let output = StatechartDocumentJson.serializeParseResult r.ParseResult
             Console.WriteLine(output)
             if r.HasErrors then
                 Environment.ExitCode <- 1
         | Error e ->
             Environment.ExitCode <- 1
             Console.Error.WriteLine(TextOutput.formatError e))
     ```
  3. Add to parent: `statechartCmd.Subcommands.Add(scParseCmd)`

- **Files**: `src/Frank.Cli/Program.fs`
- **Notes**: Import output is JSON by default (the StatechartDocument). The `--format` option is for notation disambiguation (per spec FR-031). The `--output-format` option controls text/json output rendering. Need to open `Frank.Cli.Core.Statechart` for `StatechartDocumentJson`.

### Subtask T060 -- Add help entries to HelpContent.fs

- **Purpose**: Register help metadata for each new command following spec 016 patterns (FR-037).
- **Steps**:
  1. Open `src/Frank.Cli.Core/Help/HelpContent.fs`
  2. Add help entries for each statechart subcommand. Follow the existing pattern (see `extractHelp`, `clarifyHelp`, etc.):

     ```fsharp
     let statechartExtractHelp: CommandHelp =
         { Name = "statechart extract"
           Summary = "Extract state machine metadata from a compiled assembly"
           Examples =
             [ { Invocation = "frank-cli statechart extract bin/Debug/net10.0/MyApp.dll"
                 Description = "Extract all stateful resource metadata." }
               { Invocation = "frank-cli statechart extract MyApp.dll --output-format json"
                 Description = "Extract metadata in JSON format." } ]
           Workflow =
             { StepNumber = 1
               Prerequisites = []
               NextSteps = [ "statechart generate"; "statechart validate" ]
               IsOptional = false }
           Context =
             "Loads a compiled Frank assembly, scans for stateful resources defined with the statefulResource computation expression, and outputs state machine metadata: state names, initial state, allowed HTTP methods per state, guard names, and state metadata. The output can be piped to other tools or used by LLM agents." }

     let statechartGenerateHelp: CommandHelp =
         { Name = "statechart generate"
           Summary = "Generate statechart spec artifacts from a compiled assembly"
           Examples =
             [ { Invocation = "frank-cli statechart generate --format wsd MyApp.dll"
                 Description = "Generate WSD notation for all stateful resources." }
               { Invocation = "frank-cli statechart generate --format all MyApp.dll --output ./specs/"
                 Description = "Generate all format artifacts and write to files." } ]
           Workflow =
             { StepNumber = 2
               Prerequisites = [ "statechart extract" ]
               NextSteps = [ "statechart validate" ]
               IsOptional = false }
           Context =
             "Extracts state machine metadata from a compiled assembly and generates spec artifacts in the specified notation format. Supports WSD, ALPS, SCXML, smcat, and XState JSON formats." }

     let statechartValidateHelp: CommandHelp =
         { Name = "statechart validate"
           Summary = "Validate spec files against a compiled assembly"
           Examples =
             [ { Invocation = "frank-cli statechart validate game.wsd --assembly MyApp.dll"
                 Description = "Validate a WSD spec against the compiled assembly." }
               { Invocation = "frank-cli statechart validate game.wsd game.alps.json --assembly MyApp.dll --output-format json"
                 Description = "Cross-format validation with JSON output." } ]
           Workflow =
             { StepNumber = 3
               Prerequisites = [ "statechart generate" ]
               NextSteps = []
               IsOptional = false }
           Context =
             "Parses spec files, extracts metadata from the compiled assembly, and runs cross-format validation. Reports state/transition mismatches between spec files and code." }

     let statechartParseHelp: CommandHelp =
         { Name = "statechart parse"
           Summary = "Parse a spec file and output the StatechartDocument as JSON"
           Examples =
             [ { Invocation = "frank-cli statechart parse game.xstate.json"
                 Description = "Parse an XState JSON file to StatechartDocument." }
               { Invocation = "frank-cli statechart parse game.json --format alps"
                 Description = "Parse a .json file explicitly as ALPS format." } ]
           Workflow =
             { StepNumber = 1
               Prerequisites = []
               NextSteps = []
               IsOptional = true }
           Context =
             "Parses a spec file in any supported notation format and outputs the parsed StatechartDocument as JSON. Useful for LLM-assisted code scaffolding: the output can be consumed by code generation tools." }
     ```

  3. Rename existing command help entries to use hierarchical names:
     - `extractHelp` → `Name = "semantic extract"` (was `"extract"`)
     - `clarifyHelp` → `Name = "semantic clarify"` (was `"clarify"`)
     - `validateHelp` → `Name = "semantic validate"` (was `"validate"`)
     - `diffHelp` → `Name = "semantic diff"` (was `"diff"`)
     - `compileHelp` → `Name = "semantic compile"` (was `"compile"`)
     - Update `Workflow.NextSteps` and `Prerequisites` to use qualified names too.
  4. Add all 4 statechart command help entries and the 5 renamed semantic entries to `allCommands`.
  5. Rename the `workflows` topic to `semantic-workflows` and add a `statechart-workflows` topic:
     ```fsharp
     let statechartWorkflowsTopic: HelpTopic =
         { Name = "statechart-workflows"
           Summary = "End-to-end guide to the statechart pipeline"
           Content = """The statechart pipeline extracts, generates, validates, and parses
     state machine artifacts from Frank applications:

       Step 1: statechart extract (required)
         Extracts state machine metadata from a compiled assembly.
         Prerequisites: (none)
         Next: statechart generate, statechart validate

       Step 2: statechart generate (required)
         Generates spec artifacts in notation formats (WSD, ALPS, SCXML, smcat, XState).
         Prerequisites: statechart extract
         Next: statechart validate

       Step 3: statechart validate (required)
         Validates spec files against compiled assembly code-truth.
         Prerequisites: statechart extract
         Next: (end of pipeline)

       statechart parse (standalone, optional)
         Parses a spec file and outputs the StatechartDocument as JSON.
         Prerequisites: (none)
         Use case: LLM-assisted code scaffolding from notation files.""" }
     ```
  6. Update `allTopics` to include both workflow topics.
  7. Update `pipelineStepCount` or add a separate count for the statechart pipeline.

- **Files**: `src/Frank.Cli.Core/Help/HelpContent.fs`
- **Parallel?**: Yes -- can proceed alongside T055-T059.
- **Notes**: The `findCommand` function uses `List.tryFind` with case-insensitive name matching. Since names now include spaces (`"statechart extract"`), no code change is needed for lookup — the existing string equality check handles spaces. Verify by testing `frank-cli help statechart extract`.

### Subtask T061 -- Verify all commands accessible and no regressions

- **Purpose**: Confirm restructured and new commands work (SC-007).
- **Steps**:
  1. Run `dotnet build` to verify compilation.
  2. Run `dotnet run --project src/Frank.Cli -- --help` to verify root help shows `semantic`, `statechart`, `status`, `help`.
  3. Run `dotnet run --project src/Frank.Cli -- semantic --help` to verify the semantic parent lists subcommands.
  4. Run `dotnet run --project src/Frank.Cli -- semantic extract --help` to verify existing extract moved correctly.
  5. Run `dotnet run --project src/Frank.Cli -- statechart --help` to verify the statechart parent lists subcommands.
  6. Run `dotnet run --project src/Frank.Cli -- statechart extract --help` to verify extract subcommand.
  7. Run `dotnet run --project src/Frank.Cli -- statechart generate --help` to verify generate subcommand.
  8. Run `dotnet run --project src/Frank.Cli -- statechart validate --help` to verify validate subcommand.
  9. Run `dotnet run --project src/Frank.Cli -- statechart parse --help` to verify parse subcommand.
  10. Run `dotnet run --project src/Frank.Cli -- help statechart extract` to verify hierarchical help lookup.
  11. Run `dotnet run --project src/Frank.Cli -- help semantic extract` to verify no collision with statechart extract.

- **Files**: N/A (verification only)

**Integration test note**: A test project `test/Frank.Cli.Statechart.Tests/` must be created with test scaffolding to cover integration tests for SC-001 through SC-007. At minimum, integration subtasks should verify: assembly loading and metadata extraction (SC-001), round-trip validity of all five format generators (SC-002), mismatch detection by the validate command (SC-003), import parsing for all five formats (SC-004), JSON output validity (SC-005), idempotent file generation (SC-006), and non-regression of existing CLI commands (SC-007). These test subtasks were not included in the current WP breakdown and should be added during implementation planning.

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Existing command breakage during restructuring | Mechanical move: commands go from `root.Subcommands.Add` to `semanticCmd.Subcommands.Add`. Handlers unchanged. |
| Help lookup with hierarchical names | Existing `findCommand` uses string equality — works with spaces. Test `help semantic extract` and `help statechart extract`. |
| System.CommandLine argument parsing with variadic args | Use `ArgumentArity.OneOrMore` for spec-files. Assembly as `--assembly` option to avoid ambiguity. |
| Missing opens for new modules | Open `Frank.Cli.Core.Statechart` and `Frank.Cli.Core.Commands` in Program.fs. |

---

## Review Guidance

- Verify all 4 subcommands are registered under `statechart` parent.
- Verify `--help` works for parent and each subcommand.
- Verify existing root-level commands are unaffected.
- Verify exit codes are set correctly (0 for success, 1 for errors/validation failures).
- Verify help entries are complete with examples, workflow, and context.
- Verify `dotnet build` passes cleanly.

---

## Activity Log

- 2026-03-16T19:12:54Z -- system -- lane=planned -- Prompt created.
- 2026-03-18T03:14:50Z – unknown – lane=for_review – Ready for review: CLI restructured with semantic/statechart parent commands. 4 statechart subcommands wired. Help entries added. Build clean. kitty-specs diffs are from merge topology, not intentional changes.
- 2026-03-18T03:14:54Z – claude-opus – shell_pid=14998 – lane=doing – Started review via workflow command
