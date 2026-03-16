---
work_package_id: WP05
title: System.CommandLine Integration and Program.fs Wiring
lane: planned
dependencies:
- WP01
subtasks:
- T025
- T026
- T027
- T028
- T029
- T030
phase: Phase 4 - Integration
assignee: ''
agent: ''
shell_pid: ''
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-15T23:59:04Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-007
- FR-008
- FR-009
- FR-010
- FR-023
---

# Work Package Prompt: WP05 -- System.CommandLine Integration and Program.fs Wiring

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

This WP depends on WP01-WP04:

```bash
spec-kitty implement WP05 --base WP04
```

---

## Objectives & Success Criteria

1. Custom HelpAction wraps the default System.CommandLine HelpAction and appends WORKFLOW + EXAMPLES (+ CONTEXT when --context is active).
2. `--context` global option is registered on the RootCommand.
3. `status` subcommand is registered with `--project` (required) and `--format` options.
4. `help` subcommand is registered with an optional string argument and `--format` option.
5. Custom HelpAction is attached to the root command's HelpOption.
6. All existing commands continue to work (no regressions).
7. Manual verification passes for all commands listed in quickstart.md.

## Context & Constraints

- **Research**: `kitty-specs/016-frank-cli-help-system/research.md` (R1: HelpAction wrapper pattern, R2: SCL dependency placement, R6: --context design)
- **Quickstart**: `kitty-specs/016-frank-cli-help-system/quickstart.md` (build/test commands, manual verification steps)
- **Spec**: `kitty-specs/016-frank-cli-help-system/spec.md` (FR-007 through FR-017 for help enrichment and subcommand, FR-018 through FR-024 for status)
- **Contracts**: `kitty-specs/016-frank-cli-help-system/contracts/cli-outputs.md` (expected output for all cases)
- **Program.fs**: Current wiring in `src/Frank.Cli/Program.fs` -- all new commands follow the same pattern.

**IMPORTANT**: The `HelpAction` wrapper pattern from research.md (R1):
1. Find the `HelpOption` in root command's Options collection
2. Save reference to the existing `HelpAction`
3. Create a custom `SynchronousCommandLineAction` that calls the default action first, then appends custom sections
4. Replace the HelpOption's action with the custom action

## Subtasks & Detailed Guidance

### Subtask T025 -- Create HelpAction.fs

**Purpose**: Implement a custom `SynchronousCommandLineAction` that wraps the default System.CommandLine help action. When any command's `--help` is invoked, the custom action renders standard help first, then appends WORKFLOW, EXAMPLES, and optionally CONTEXT sections.

**Steps**:

1. Create `src/Frank.Cli.Core/Help/HelpAction.fs`.

2. Use namespace `Frank.Cli.Core.Help`.

3. The module needs access to System.CommandLine types:

```fsharp
open System
open System.CommandLine
open System.CommandLine.Help
open System.CommandLine.Invocation
```

4. Implement the custom help action. The key pattern:

```fsharp
module HelpAction =

    /// Create a custom help action that wraps the default and appends enriched sections.
    /// Parameters:
    ///   defaultAction - the original HelpAction from the HelpOption
    ///   contextOption - the --context Option<bool> to check
    let createEnrichedHelpAction
        (defaultAction: SynchronousCommandLineAction)
        (contextOption: Option<bool>)
        : SynchronousCommandLineAction =

        { new SynchronousCommandLineAction() with
            member _.Invoke(parseResult: ParseResult) : int =
                // 1. Call the default help action to render standard help
                let exitCode = defaultAction.Invoke(parseResult)

                // 2. Determine which command's help is being shown
                let commandName = parseResult.CommandResult.Command.Name

                // 3. Look up the enriched help content
                match HelpContent.findCommand commandName with
                | Some help ->
                    let totalSteps = HelpContent.pipelineStepCount

                    // Only append WORKFLOW/EXAMPLES for pipeline commands (stepNumber > 0)
                    // or always append for all commands to be consistent
                    Console.WriteLine(HelpRenderer.renderWorkflowText help totalSteps)
                    Console.WriteLine(HelpRenderer.renderExamplesText help)

                    // 4. Check if --context is active
                    let showContext = parseResult.GetValue(contextOption)
                    if showContext then
                        Console.WriteLine(HelpRenderer.renderContextText help)

                | None -> ()

                exitCode }
```

5. **Important**: The `SynchronousCommandLineAction` is an abstract class in System.CommandLine 2.0.3. The F# object expression syntax `{ new SynchronousCommandLineAction() with ... }` creates an inline subclass.

6. **Alternative approach** if object expressions don't work cleanly with SCL 2.0.3: Create a named class:

```fsharp
    type EnrichedHelpAction(defaultAction: SynchronousCommandLineAction, contextOption: Option<bool>) =
        inherit SynchronousCommandLineAction()

        override _.Invoke(parseResult: ParseResult) : int =
            // ... same logic as above
```

7. The action should work for all commands, including `help` and `status` (which have non-pipeline workflow positions with StepNumber = 0). Decide whether to show WORKFLOW for non-pipeline commands. The contracts show WORKFLOW for pipeline commands; for non-pipeline commands, it could show "Not part of the extraction pipeline" or simply omit the section. The safest approach: only show WORKFLOW/EXAMPLES if the command has a StepNumber > 0, but always show EXAMPLES for all commands since examples are useful regardless.

**Files**: `src/Frank.Cli.Core/Help/HelpAction.fs` (new, ~50 lines)
**Parallel?**: No -- T026-T030 depend on this being in place.

**Edge Cases**:
- RootCommand's `--help` (no subcommand): `parseResult.CommandResult.Command.Name` will be the root command name (empty or "frank-cli"). There is no CommandHelp record for the root -- the custom action should just show default help with no enrichment.
- Command not found in HelpContent: skip enrichment (the `| None -> ()` branch).

---

### Subtask T026 -- Update Frank.Cli.Core.fsproj with SCL and HelpAction

**Purpose**: Add the System.CommandLine 2.0.3 package reference and the HelpAction.fs compile entry.

**Steps**:

1. Add package reference to `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`:

```xml
<PackageReference Include="System.CommandLine" Version="2.0.3" />
```

2. Add compile entry after HelpRenderer.fs:

```xml
<Compile Include="Help/HelpAction.fs" />
```

3. The final Help compile order:

```xml
<Compile Include="Help/HelpTypes.fs" />
<Compile Include="Help/FuzzyMatch.fs" />
<Compile Include="Help/HelpContent.fs" />
<Compile Include="Help/HelpSubcommand.fs" />
<Compile Include="Help/HelpRenderer.fs" />
<Compile Include="Help/HelpAction.fs" />
```

4. Run `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj` to verify.

**Files**: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` (modify)
**Parallel?**: No -- depends on T025.

---

### Subtask T027 -- Add --context Global Option to Program.fs

**Purpose**: Register the `--context` flag as a global option on the RootCommand so it's available to the custom HelpAction for all commands.

**Steps**:

1. Open `src/Frank.Cli/Program.fs`.

2. After creating the `root` RootCommand (line 11), add:

```fsharp
    // ── --context global option ──
    let contextOpt = Option<bool>("--context")
    contextOpt.Description <- "Include semantic context in --help output"
    root.Options.Add(contextOpt)
```

3. This option is a global option -- it propagates to all subcommands automatically.

4. It only has an effect when combined with `--help` (the custom HelpAction checks for it).

**Files**: `src/Frank.Cli/Program.fs` (modify, ~4 lines added)
**Parallel?**: No -- must be done before T030 (HelpAction attachment needs the option reference).

---

### Subtask T028 -- Register Status Subcommand in Program.fs

**Purpose**: Wire the `status` subcommand into the CLI with `--project` and `--format` options.

**Steps**:

1. In `src/Frank.Cli/Program.fs`, add after the compile command block (before `let parseResult = root.Parse(args)`):

```fsharp
    // ── status ──
    let statusCmd = Command("status")
    statusCmd.Description <- "Show project extraction and compilation status"
    let statusProjectOpt = Option<string>("--project")
    statusProjectOpt.Description <- "Path to .fsproj file"
    statusProjectOpt.Required <- true
    let statusFormatOpt = Option<string>("--format")
    statusFormatOpt.Description <- "Output format (text|json)"
    statusFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    statusCmd.Options.Add(statusProjectOpt)
    statusCmd.Options.Add(statusFormatOpt)

    statusCmd.SetAction(fun parseResult ->
        let project = parseResult.GetValue(statusProjectOpt)
        let format = parseResult.GetValue(statusFormatOpt)

        let result = StatusCommand.execute project

        match result with
        | Ok r ->
            let output =
                if format = "json" then
                    JsonOutput.formatStatusResult r
                else
                    TextOutput.formatStatusResult r
            Console.WriteLine(output)
        | Error e ->
            Environment.ExitCode <- 1
            let output =
                if format = "json" then
                    JsonOutput.formatError e
                else
                    TextOutput.formatError e
            Console.Error.WriteLine(output))

    root.Subcommands.Add(statusCmd)
```

2. Add `open Frank.Cli.Core.Help` to the top of the file (for ProjectStatus types used indirectly).

3. Follow the exact same pattern as existing commands (extract, clarify, etc.) for consistency.

**Files**: `src/Frank.Cli/Program.fs` (modify, ~30 lines added)
**Parallel?**: No -- touches the same file as T027, T029, T030.

---

### Subtask T029 -- Register Help Subcommand in Program.fs

**Purpose**: Wire the `help` subcommand that accepts an optional string argument for command/topic lookup.

**Steps**:

1. In `src/Frank.Cli/Program.fs`, add after the status command block:

```fsharp
    // ── help ──
    let helpCmd = Command("help")
    helpCmd.Description <- "Show help topics and command documentation"
    let helpArg = Argument<string>("subject")
    helpArg.Description <- "Command or topic name"
    helpArg.Arity <- ArgumentArity.ZeroOrOne
    let helpFormatOpt = Option<string>("--format")
    helpFormatOpt.Description <- "Output format (text|json)"
    helpFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    helpCmd.Arguments.Add(helpArg)
    helpCmd.Options.Add(helpFormatOpt)

    helpCmd.SetAction(fun parseResult ->
        let subject = parseResult.GetValue(helpArg)
        let format = parseResult.GetValue(helpFormatOpt)

        if String.IsNullOrEmpty subject then
            // No argument: show index
            let index = HelpSubcommand.listAll()
            let output =
                if format = "json" then
                    JsonOutput.formatHelpIndex index
                else
                    TextOutput.formatHelpIndex index
            Console.WriteLine(output)
        else
            // Resolve the argument
            match HelpSubcommand.resolve subject with
            | HelpLookupResult.CommandMatch cmd ->
                let output =
                    if format = "json" then
                        HelpRenderer.renderCommandJson cmd HelpContent.pipelineStepCount
                    else
                        HelpRenderer.renderFullCommandText cmd HelpContent.pipelineStepCount
                Console.WriteLine(output)
            | HelpLookupResult.TopicMatch topic ->
                let output =
                    if format = "json" then
                        JsonOutput.formatTopicJson topic
                    else
                        TextOutput.formatTopicText topic
                Console.WriteLine(output)
            | HelpLookupResult.NoMatch suggestions ->
                Environment.ExitCode <- 1
                let output =
                    if format = "json" then
                        JsonOutput.formatNoMatch subject suggestions
                    else
                        TextOutput.formatNoMatch subject suggestions
                Console.Error.WriteLine(output))

    root.Subcommands.Add(helpCmd)
```

2. Add the necessary opens at the top of Program.fs:
   - `open Frank.Cli.Core.Help` (for HelpLookupResult, HelpSubcommand, HelpRenderer, HelpContent)

3. Note: `frank-cli help <unknown>` returns exit code 1 (error).

**Files**: `src/Frank.Cli/Program.fs` (modify, ~45 lines added)
**Parallel?**: No -- touches the same file.

---

### Subtask T030 -- Attach Custom HelpAction to Root Command

**Purpose**: Replace the default HelpOption action on the root command with the custom enriched help action.

**Steps**:

1. In `src/Frank.Cli/Program.fs`, after all commands are registered but before `let parseResult = root.Parse(args)`, add:

```fsharp
    // ── Attach enriched help action ──
    // Find the HelpOption on the root command and wrap its action
    let helpOption =
        root.Options
        |> Seq.tryFind (fun o -> o :? HelpOption)
        |> Option.map (fun o -> o :?> HelpOption)

    match helpOption with
    | Some ho ->
        let defaultAction = ho.Action :?> SynchronousCommandLineAction
        ho.Action <- HelpAction.createEnrichedHelpAction defaultAction contextOpt
    | None -> ()
```

2. Add necessary opens:
   - `open System.CommandLine.Help` (for `HelpOption`)
   - `open System.CommandLine.Invocation` (for `SynchronousCommandLineAction`)

3. **Important**: The HelpOption is auto-added by System.CommandLine. Finding it in the Options collection and replacing its Action is the documented pattern from research R1.

4. **Alternative** if `HelpOption` is not directly findable: iterate `root.Options` looking for options with alias `-h` or `--help`.

**Files**: `src/Frank.Cli/Program.fs` (modify, ~10 lines added)
**Parallel?**: No -- depends on T025 (HelpAction module) and T027 (contextOpt variable).

**Validation**:
Run all manual verification commands from quickstart.md:

```bash
# Build
dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj
dotnet build src/Frank.Cli/Frank.Cli.fsproj

# Enriched --help
dotnet run --project src/Frank.Cli -- extract --help
# Should show: standard help + WORKFLOW + EXAMPLES

dotnet run --project src/Frank.Cli -- --context extract --help
# Should show: standard help + WORKFLOW + EXAMPLES + CONTEXT

# Help subcommand
dotnet run --project src/Frank.Cli -- help
# Should show: command/topic index

dotnet run --project src/Frank.Cli -- help workflows
# Should show: workflows topic content

dotnet run --project src/Frank.Cli -- help concepts
# Should show: concepts topic content

dotnet run --project src/Frank.Cli -- help extract
# Should show: full enriched help for extract (with context)

dotnet run --project src/Frank.Cli -- help comiple
# Should show: "Did you mean? compile"

# Status command (needs a project path -- may fail on "not found" which is expected)
dotnet run --project src/Frank.Cli -- status --project path/to/MyApp.fsproj
# Should show: error (project not found) or status output

# Existing commands still work
dotnet run --project src/Frank.Cli -- extract --help
# Should still show standard help (no regressions)
```

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| HelpAction wrapper API doesn't work as documented | Fall back to HelpBuilder.CustomizeLayout (research R1 alternative) |
| System.CommandLine version conflict between Frank.Cli and Frank.Cli.Core | Both use 2.0.3; no conflict expected |
| HelpOption not findable in Options collection | Use alternative: iterate Options looking for --help alias |
| Global --context option interferes with existing commands | --context has no effect except in HelpAction -- existing commands ignore it |
| Program.fs becomes too large | It's a thin wiring file; the logic is in Core modules. Size is acceptable. |

## Review Guidance

- Verify custom HelpAction correctly wraps the default action (standard help appears first, enriched sections after).
- Verify --context only adds CONTEXT section, doesn't affect other sections.
- Verify status and help subcommands follow the same pattern as existing commands.
- Verify all existing commands still work (no regressions).
- Run manual verification commands from quickstart.md.
- Run `dotnet build` for both Frank.Cli.Core and Frank.Cli projects.

## Activity Log

- 2026-03-15T23:59:04Z -- system -- lane=planned -- Prompt created.
