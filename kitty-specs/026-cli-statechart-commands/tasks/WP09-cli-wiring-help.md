---
work_package_id: "WP09"
title: "CLI Wiring & Help Content"
lane: "planned"
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
agent: ""
shell_pid: ""
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

1. Add `statechart` parent command to `Program.fs` root command.
2. Wire all 4 subcommands (`extract`, `generate`, `validate`, `import`) under the `statechart` parent.
3. Register help metadata for each new command in `HelpContent.fs`.
4. Existing commands continue to function correctly (SC-007).
5. All commands are accessible via `frank-cli statechart <subcommand>`.

**Success**: `frank-cli statechart extract`, `generate`, `validate`, `import` are all accessible. `frank-cli help` lists the new commands. Existing commands (extract, clarify, validate, diff, compile, status, help) are unaffected.

---

## Context & Constraints

- **Spec**: `kitty-specs/026-cli-statechart-commands/spec.md` (FR-035, FR-036, FR-037)
- **Plan**: `kitty-specs/026-cli-statechart-commands/plan.md` (D-001, D-007)
- **Existing CLI structure**: `src/Frank.Cli/Program.fs` -- imperative System.CommandLine pattern
- **Existing help system**: `src/Frank.Cli.Core/Help/HelpContent.fs` -- hardcoded `CommandHelp` records
- **Help types**: `src/Frank.Cli.Core/Help/HelpTypes.fs` -- `CommandHelp`, `CommandExample`, `WorkflowPosition`
- **Key decision D-001**: Follow existing imperative pattern, no abstraction layer.
- **Key decision D-007**: All commands use `--output-format` for text/json output rendering. `--format` is for notation format on generate and import.

---

## Subtasks & Detailed Guidance

### Subtask T055 -- Add statechart parent Command to Program.fs

- **Purpose**: Create the `statechart` parent command group on the root command (FR-035).
- **Steps**:
  1. Open `src/Frank.Cli/Program.fs`
  2. After the existing commands (compile), add:
     ```fsharp
     // ── statechart ──
     let statechartCmd = Command("statechart")
     statechartCmd.Description <- "Statechart pipeline operations for stateful Frank resources"
     ```
  3. Add the parent command to root:
     ```fsharp
     root.Subcommands.Add(statechartCmd)
     ```
  4. The 4 subcommands will be added to `statechartCmd` in the following subtasks.

- **Files**: `src/Frank.Cli/Program.fs`
- **Notes**: The `statechart` command is a parent (group) command. It has no handler of its own -- it just groups the 4 subcommands.

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

### Subtask T059 -- Wire import subcommand

- **Purpose**: Add `frank statechart import <spec-file> [--format <fmt>] [--output-format text|json]`.
- **Steps**:
  1. Create the import subcommand:
     ```fsharp
     let scImportCmd = Command("import")
     scImportCmd.Description <- "Parse a spec file and output the StatechartDocument as JSON"

     let scImpFileArg = Argument<string>("spec-file")
     scImpFileArg.Description <- "Path to the spec file to import"
     scImportCmd.Arguments.Add(scImpFileArg)

     let scImpFormatOpt = Option<string>("--format")
     scImpFormatOpt.Description <- "Notation format override (wsd|alps|scxml|smcat|xstate) for ambiguous file extensions"
     scImportCmd.Options.Add(scImpFormatOpt)

     let scImpOutputFormatOpt = Option<string>("--output-format")
     scImpOutputFormatOpt.Description <- "Output format (text|json)"
     scImpOutputFormatOpt.DefaultValueFactory <- (fun _ -> "json")
     scImportCmd.Options.Add(scImpOutputFormatOpt)
     ```
  2. Set the action handler:
     ```fsharp
     scImportCmd.SetAction(fun parseResult ->
         let specFile = parseResult.GetValue(scImpFileArg)
         let formatOverride =
             let v = parseResult.GetValue(scImpFormatOpt)
             if String.IsNullOrEmpty v then None else Some v

         let result = StatechartImportCommand.execute specFile formatOverride

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
  3. Add to parent: `statechartCmd.Subcommands.Add(scImportCmd)`

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

     let statechartImportHelp: CommandHelp =
         { Name = "statechart import"
           Summary = "Parse a spec file and output the StatechartDocument as JSON"
           Examples =
             [ { Invocation = "frank-cli statechart import game.xstate.json"
                 Description = "Import an XState JSON file to StatechartDocument." }
               { Invocation = "frank-cli statechart import game.json --format alps"
                 Description = "Import a .json file explicitly as ALPS format." } ]
           Workflow =
             { StepNumber = 1
               Prerequisites = []
               NextSteps = []
               IsOptional = true }
           Context =
             "Parses a spec file in any supported notation format and outputs the parsed StatechartDocument as JSON. Useful for LLM-assisted code scaffolding: the output can be consumed by code generation tools." }
     ```

  3. Add new commands to the `findCommand` lookup function (if it uses a list/map pattern):
     - Check how existing commands are registered and follow the same pattern.

- **Files**: `src/Frank.Cli.Core/Help/HelpContent.fs`
- **Parallel?**: Yes -- can proceed alongside T055-T059.
- **Notes**: Read the existing `HelpContent.fs` to understand the full pattern including `findCommand` and `allCommands`.

### Subtask T061 -- Verify all commands accessible and no regressions

- **Purpose**: Confirm the new commands work and existing commands are unaffected (SC-007).
- **Steps**:
  1. Run `dotnet build` to verify compilation.
  2. Run `dotnet run --project src/Frank.Cli -- statechart --help` to verify the parent command.
  3. Run `dotnet run --project src/Frank.Cli -- statechart extract --help` to verify the extract subcommand.
  4. Run `dotnet run --project src/Frank.Cli -- statechart generate --help` to verify the generate subcommand.
  5. Run `dotnet run --project src/Frank.Cli -- statechart validate --help` to verify the validate subcommand.
  6. Run `dotnet run --project src/Frank.Cli -- statechart import --help` to verify the import subcommand.
  7. Run `dotnet run --project src/Frank.Cli -- --help` to verify root help includes `statechart`.
  8. Run `dotnet run --project src/Frank.Cli -- extract --help` to verify existing extract command is unaffected.

- **Files**: N/A (verification only)

**Integration test note**: A test project `test/Frank.Cli.Statechart.Tests/` must be created with test scaffolding to cover integration tests for SC-001 through SC-007. At minimum, integration subtasks should verify: assembly loading and metadata extraction (SC-001), round-trip validity of all five format generators (SC-002), mismatch detection by the validate command (SC-003), import parsing for all five formats (SC-004), JSON output validity (SC-005), idempotent file generation (SC-006), and non-regression of existing CLI commands (SC-007). These test subtasks were not included in the current WP breakdown and should be added during implementation planning.

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Name collision: `extract` (existing) vs `statechart extract` (new) | Different command hierarchy. Existing `extract` is root-level; new one is under `statechart`. |
| Name collision: `validate` (existing) vs `statechart validate` (new) | Same approach -- different hierarchy. No conflict. |
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
