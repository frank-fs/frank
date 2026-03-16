---
work_package_id: WP04
title: Rendering and Output
lane: "done"
dependencies:
- WP01
base_branch: 016-frank-cli-help-system-WP01
base_commit: 7c2fcc4753732433dcf675b8b2222356cf6af603
created_at: '2026-03-16T11:46:56.261391+00:00'
subtasks:
- T018
- T019
- T020
- T021
- T022
- T023
- T024
phase: Phase 3 - Rendering and Integration
assignee: ''
agent: "claude-opus-4-6"
shell_pid: "62603"
review_status: "approved"
reviewed_by: "Ryan Riley"
history:
- timestamp: '2026-03-15T23:59:04Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-025
- FR-026
- FR-027
---

# Work Package Prompt: WP04 -- Rendering and Output

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

This WP depends on WP03:

```bash
spec-kitty implement WP04 --base WP03
```

---

## Objectives & Success Criteria

1. HelpRenderer produces text output matching `contracts/cli-outputs.md` exactly (WORKFLOW, EXAMPLES, CONTEXT sections).
2. HelpRenderer produces valid JSON output for all help content types.
3. TextOutput.formatStatusResult produces status output matching contracts.
4. JsonOutput.formatStatusResult produces valid, parseable JSON matching contracts.
5. Help index, topic display, and "did you mean?" output formats match contracts.
6. Text output respects `NO_COLOR` environment variable.
7. `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj` succeeds.

## Context & Constraints

- **Contracts**: `kitty-specs/016-frank-cli-help-system/contracts/cli-outputs.md` -- THIS IS THE SOURCE OF TRUTH for all output formats.
- **Spec**: `kitty-specs/016-frank-cli-help-system/spec.md` (FR-007 through FR-010 for --help enrichment, FR-025 through FR-027 for output integration)
- **Existing Patterns**: See `src/Frank.Cli.Core/Output/TextOutput.fs` and `JsonOutput.fs` for established patterns.
- **JSON convention**: Use `Utf8JsonWriter` with `JsonWriterOptions(Indented = true)` following existing JsonOutput patterns.
- **Text convention**: Use `System.Text.StringBuilder` following existing TextOutput patterns.

## Subtasks & Detailed Guidance

### Subtask T018 -- Create HelpRenderer.fs with Text Rendering

**Purpose**: Render the WORKFLOW, EXAMPLES, and CONTEXT sections that are appended to standard `--help` output.

**Steps**:

1. Create `src/Frank.Cli.Core/Help/HelpRenderer.fs`.

2. Use namespace `Frank.Cli.Core.Help`.

3. Implement text rendering for the enriched --help sections:

```fsharp
module HelpRenderer =

    open System.Text

    /// Render the WORKFLOW section for a command's --help output.
    let renderWorkflowText (help: CommandHelp) (totalSteps: int) : string =
        let sb = StringBuilder()
        sb.AppendLine() |> ignore  // blank line before section
        sb.AppendLine("WORKFLOW") |> ignore

        let optionality = if help.Workflow.IsOptional then "optional" else "required"
        sb.AppendLine($"  Step {help.Workflow.StepNumber} of {totalSteps} ({optionality})") |> ignore

        let prereqs =
            if help.Workflow.Prerequisites.IsEmpty then "(none - this is the first step)"
            else help.Workflow.Prerequisites |> String.concat ", "
        sb.AppendLine($"  Prerequisites: {prereqs}") |> ignore

        let nextSteps =
            if help.Workflow.NextSteps.IsEmpty then "(end of pipeline)"
            else help.Workflow.NextSteps |> String.concat ", "
        sb.AppendLine($"  Next steps: {nextSteps}") |> ignore

        sb.ToString()

    /// Render the EXAMPLES section for a command's --help output.
    let renderExamplesText (help: CommandHelp) : string =
        let sb = StringBuilder()
        sb.AppendLine() |> ignore
        sb.AppendLine("EXAMPLES") |> ignore

        for example in help.Examples do
            sb.AppendLine($"  {example.Invocation}") |> ignore
            sb.AppendLine($"    {example.Description}") |> ignore
            sb.AppendLine() |> ignore

        sb.ToString().TrimEnd('\n') + "\n"

    /// Render the CONTEXT section (only when --context is active).
    let renderContextText (help: CommandHelp) : string =
        let sb = StringBuilder()
        sb.AppendLine() |> ignore
        sb.AppendLine("CONTEXT") |> ignore
        // Wrap context text with 2-space indent
        for line in help.Context.Split('\n') do
            sb.AppendLine($"  {line.Trim()}") |> ignore
        sb.ToString()
```

4. **Format rules** (from contracts):
   - Section headers (WORKFLOW, EXAMPLES, CONTEXT) are UPPERCASE, no colon.
   - One blank line before each section header.
   - WORKFLOW: step/total, required/optional, prerequisites, next steps -- all indented 2 spaces.
   - EXAMPLES: invocation indented 2 spaces, description indented 4 spaces on next line.
   - CONTEXT: free-form paragraph text indented 2 spaces.

**Files**: `src/Frank.Cli.Core/Help/HelpRenderer.fs` (new, ~60 lines)
**Parallel?**: No -- T019, T022, T023 extend this file.

---

### Subtask T019 -- Add JSON Rendering for Enriched Help Sections

**Purpose**: Render command help as JSON for `--format json` output.

**Steps**:

1. Add to HelpRenderer:

```fsharp
    open System.IO
    open System.Text.Json

    /// Render a CommandHelp record as JSON.
    let renderCommandJson (help: CommandHelp) (totalSteps: int) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

        writer.WriteStartObject()
        writer.WriteString("name", help.Name)
        writer.WriteString("summary", help.Summary)

        writer.WriteStartArray("examples")
        for ex in help.Examples do
            writer.WriteStartObject()
            writer.WriteString("invocation", ex.Invocation)
            writer.WriteString("description", ex.Description)
            writer.WriteEndObject()
        writer.WriteEndArray()

        writer.WriteStartObject("workflow")
        writer.WriteNumber("stepNumber", help.Workflow.StepNumber)
        writer.WriteNumber("totalSteps", totalSteps)
        writer.WriteBoolean("isOptional", help.Workflow.IsOptional)
        writer.WriteStartArray("prerequisites")
        for p in help.Workflow.Prerequisites do writer.WriteStringValue(p)
        writer.WriteEndArray()
        writer.WriteStartArray("nextSteps")
        for n in help.Workflow.NextSteps do writer.WriteStringValue(n)
        writer.WriteEndArray()
        writer.WriteEndObject()

        writer.WriteString("context", help.Context)

        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())
```

2. Match the JSON structure from `contracts/cli-outputs.md` Contract 2 (`frank-cli help <command>` -- JSON).

**Files**: `src/Frank.Cli.Core/Help/HelpRenderer.fs` (extend, ~40 lines)
**Parallel?**: Yes -- can be developed alongside T018 if done carefully (different functions in same file).

---

### Subtask T020 -- Extend TextOutput.fs with Status and Help Functions

**Purpose**: Add text formatting functions for the status command and help subcommand outputs.

**Steps**:

1. Open `src/Frank.Cli.Core/Output/TextOutput.fs`.

2. Add `open Frank.Cli.Core.Help` at the top.

3. Add `formatStatusResult`:

```fsharp
    let formatStatusResult (result: ProjectStatus) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine($"Project: {result.ProjectPath}") |> ignore
        sb.AppendLine($"State directory: {result.StateDirectory}") |> ignore
        sb.AppendLine() |> ignore

        let extractionText =
            match result.Extraction with
            | ExtractionStatus.NotExtracted -> "not performed"
            | ExtractionStatus.Current -> "current"
            | ExtractionStatus.Stale -> "stale (source files changed since extraction)"
            | ExtractionStatus.Unreadable reason -> $"unreadable ({reason})"
        sb.AppendLine($"Extraction: {extractionText}") |> ignore

        let artifactText =
            match result.Artifacts with
            | ArtifactStatus.Present ->
                match result.Extraction with
                | ExtractionStatus.Stale -> "present (may be outdated)"
                | _ -> "present"
            | ArtifactStatus.Missing _ -> "not present"
        sb.AppendLine($"Artifacts: {artifactText}") |> ignore

        let actionText =
            match result.RecommendedAction with
            | RecommendedAction.RunExtract ->
                $"run 'frank-cli extract --project {result.ProjectPath} --base-uri <URI>'"
            | RecommendedAction.ReExtract ->
                $"run 'frank-cli extract --project {result.ProjectPath} --base-uri <URI>'"
            | RecommendedAction.RunCompile ->
                $"run 'frank-cli compile --project {result.ProjectPath}'"
            | RecommendedAction.UpToDate -> "up to date (no action needed)"
            | RecommendedAction.RecoverExtract _ ->
                $"run 'frank-cli extract --project {result.ProjectPath} --base-uri <URI>' to recover"
        sb.AppendLine($"Recommended action: {actionText}") |> ignore

        sb.ToString()
```

4. Add `formatHelpOutput` for help subcommand text output:

```fsharp
    let formatHelpIndex (index: HelpSubcommand.HelpIndex) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine("frank-cli: Semantic resource extraction for Frank applications") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("COMMANDS") |> ignore
        for (name, summary) in index.Commands do
            sb.AppendLine(sprintf "  %-12s%s" name summary) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("TOPICS") |> ignore
        for (name, summary) in index.Topics do
            sb.AppendLine(sprintf "  %-12s%s" name summary) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("Use 'frank-cli help <command>' for detailed help on a command.") |> ignore
        sb.AppendLine("Use 'frank-cli help <topic>' for topic documentation.") |> ignore
        sb.ToString()

    let formatTopicText (topic: HelpTopic) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine(topic.Name.ToUpperInvariant()) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine(topic.Content) |> ignore
        sb.ToString()

    let formatNoMatch (query: string) (suggestions: string list) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine($"Unknown command or topic: '{query}'") |> ignore
        if not suggestions.IsEmpty then
            sb.AppendLine() |> ignore
            sb.AppendLine("Did you mean?") |> ignore
            for name in suggestions do
                match HelpContent.findCommand name with
                | Some cmd -> sb.AppendLine(sprintf "  %-12s%s" name cmd.Summary) |> ignore
                | None ->
                    match HelpContent.findTopic name with
                    | Some topic -> sb.AppendLine(sprintf "  %-12s%s" name topic.Summary) |> ignore
                    | None -> sb.AppendLine($"  {name}") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("Use 'frank-cli help' to see all commands and topics.") |> ignore
        sb.ToString()
```

5. Match the exact output format from `contracts/cli-outputs.md` Contract 2 and Contract 3.

**Files**: `src/Frank.Cli.Core/Output/TextOutput.fs` (modify, ~80 additional lines)
**Parallel?**: Yes -- can be developed alongside T021.

---

### Subtask T021 -- Extend JsonOutput.fs with Status and Help Functions

**Purpose**: Add JSON formatting functions for the status command and help subcommand outputs.

**Steps**:

1. Open `src/Frank.Cli.Core/Output/JsonOutput.fs`.

2. Add `open Frank.Cli.Core.Help` at the top.

3. Add `formatStatusResult` following the JSON structure from contracts:

```fsharp
    let formatStatusResult (result: ProjectStatus) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

        writer.WriteStartObject()
        writeString writer "status" "ok"
        writeString writer "projectPath" result.ProjectPath
        writeString writer "stateDirectory" result.StateDirectory

        writer.WriteStartObject("extraction")
        let extractionState =
            match result.Extraction with
            | ExtractionStatus.NotExtracted -> "not_extracted"
            | ExtractionStatus.Current -> "current"
            | ExtractionStatus.Stale -> "stale"
            | ExtractionStatus.Unreadable _ -> "unreadable"
        writeString writer "state" extractionState
        writer.WriteEndObject()

        writer.WriteStartObject("artifacts")
        let artifactState =
            match result.Artifacts with
            | ArtifactStatus.Present -> "present"
            | ArtifactStatus.Missing _ -> "missing"
        writeString writer "state" artifactState
        match result.Artifacts with
        | ArtifactStatus.Present ->
            writer.WriteStartArray("files")
            // Note: List actual files present (full paths would be ideal)
            writer.WriteEndArray()
        | ArtifactStatus.Missing missing ->
            writer.WriteStartArray("missingFiles")
            for f in missing do writer.WriteStringValue(f)
            writer.WriteEndArray()
        writer.WriteEndObject()

        writer.WriteStartObject("recommendedAction")
        let (action, message) =
            match result.RecommendedAction with
            | RecommendedAction.RunExtract -> ("run_extract", "Run extract to begin")
            | RecommendedAction.ReExtract -> ("re_extract", "Re-run extract (source files changed)")
            | RecommendedAction.RunCompile -> ("run_compile", "Run compile to generate artifacts")
            | RecommendedAction.UpToDate -> ("up_to_date", "No action needed")
            | RecommendedAction.RecoverExtract reason -> ("recover_extract", $"Re-extract to recover: {reason}")
        writeString writer "action" action
        writeString writer "message" message
        writer.WriteEndObject()

        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())
```

4. Add `formatHelpIndex`, `formatTopicJson`, `formatNoMatchJson`:

```fsharp
    let formatHelpIndex (index: HelpSubcommand.HelpIndex) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writer.WriteStartArray("commands")
        for (name, summary) in index.Commands do
            writer.WriteStartObject()
            writeString writer "name" name
            writeString writer "summary" summary
            writer.WriteEndObject()
        writer.WriteEndArray()
        writer.WriteStartArray("topics")
        for (name, summary) in index.Topics do
            writer.WriteStartObject()
            writeString writer "name" name
            writeString writer "summary" summary
            writer.WriteEndObject()
        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    let formatTopicJson (topic: HelpTopic) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writeString writer "name" topic.Name
        writeString writer "summary" topic.Summary
        writeString writer "content" topic.Content
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    let formatNoMatch (query: string) (suggestions: string list) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writeString writer "status" "not_found"
        writeString writer "query" query
        writer.WriteStartArray("suggestions")
        for name in suggestions do
            writer.WriteStartObject()
            writeString writer "name" name
            // Look up summary and type
            match HelpContent.findCommand name with
            | Some cmd ->
                writeString writer "summary" cmd.Summary
                writeString writer "type" "command"
            | None ->
                match HelpContent.findTopic name with
                | Some topic ->
                    writeString writer "summary" topic.Summary
                    writeString writer "type" "topic"
                | None -> ()
            writer.WriteEndObject()
        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())
```

5. Match JSON structure from `contracts/cli-outputs.md`.

**Files**: `src/Frank.Cli.Core/Output/JsonOutput.fs` (modify, ~100 additional lines)
**Parallel?**: Yes -- can be developed alongside T020.

---

### Subtask T022 -- Add Text Rendering for Help Index, Topic, and Fuzzy Match

**Purpose**: Ensure HelpRenderer has text rendering for help topic display (the section-header format used when displaying topics via `frank-cli help <topic>`).

**Steps**:

1. The main text rendering for help index, topic, and "did you mean?" is handled in TextOutput.fs (T020). This subtask ensures HelpRenderer also has a unified rendering function that combines WORKFLOW + EXAMPLES + CONTEXT for the `frank-cli help <command>` case (which shows full enriched help including context):

```fsharp
    /// Render full enriched help for a command (used by `help <command>` subcommand).
    /// Always includes context (equivalent to --context being active).
    let renderFullCommandText (help: CommandHelp) (totalSteps: int) : string =
        let sb = StringBuilder()
        sb.Append(help.Summary) |> ignore
        sb.AppendLine() |> ignore
        sb.Append(renderWorkflowText help totalSteps) |> ignore
        sb.Append(renderExamplesText help) |> ignore
        sb.Append(renderContextText help) |> ignore
        sb.ToString()
```

**Files**: `src/Frank.Cli.Core/Help/HelpRenderer.fs` (extend, ~15 lines)
**Parallel?**: No -- must follow T018.

---

### Subtask T023 -- Add JSON Rendering for Help Index, Topic, Fuzzy Match

**Purpose**: The JSON rendering for these cases is handled in JsonOutput.fs (T021). This subtask is a verification step to ensure the JSON output from HelpRenderer.renderCommandJson is consistent with the JsonOutput help functions.

**Steps**:

1. Verify `HelpRenderer.renderCommandJson` produces the same structure as `JsonOutput` expects for the `frank-cli help <command> --format json` case.

2. Both should use the same JSON field names: `name`, `summary`, `examples`, `workflow`, `context`.

**Files**: `src/Frank.Cli.Core/Help/HelpRenderer.fs` (verify)
**Parallel?**: Yes -- verification can happen alongside other subtasks.

---

### Subtask T024 -- Update Frank.Cli.Core.fsproj

**Purpose**: Add HelpRenderer.fs to the compile list.

**Steps**:

1. Add to `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`:

```xml
<!-- After Help/HelpSubcommand.fs -->
<Compile Include="Help/HelpRenderer.fs" />
```

2. The Help compile order after WP04:

```xml
<Compile Include="Help/HelpTypes.fs" />
<Compile Include="Help/FuzzyMatch.fs" />
<Compile Include="Help/HelpContent.fs" />
<Compile Include="Help/HelpSubcommand.fs" />
<Compile Include="Help/HelpRenderer.fs" />
```

3. **Note**: HelpRenderer.fs must come after HelpSubcommand.fs because the TextOutput/JsonOutput functions reference `HelpSubcommand.HelpIndex`. However, HelpRenderer.fs itself only depends on HelpTypes.fs and HelpContent.fs. The key dependency is that TextOutput.fs and JsonOutput.fs must come after HelpSubcommand.fs and HelpRenderer.fs. The current compile order already has Output files after Help and Commands files, so this should be fine.

4. Run `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj` to verify.

**Files**: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` (modify)
**Parallel?**: No -- must be done after T018-T023.

**Validation**:
- [ ] `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj` succeeds
- [ ] Text WORKFLOW section matches contracts format (uppercase header, 2-space indent, step/total/optional/prereqs/nextsteps)
- [ ] Text EXAMPLES section matches contracts format (2-space invocation, 4-space description)
- [ ] Text CONTEXT section matches contracts format (uppercase header, 2-space indent)
- [ ] JSON command help matches contracts structure
- [ ] Text status output matches contracts for all states
- [ ] JSON status output matches contracts for all states
- [ ] Help index, topic, and "did you mean?" outputs match contracts

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Output format doesn't match contracts exactly | Compare character-by-character against contracts/cli-outputs.md |
| Namespace conflicts between HelpRenderer and Output modules | HelpRenderer handles --help section rendering; Output modules handle subcommand output |
| JSON field naming inconsistency | Follow existing camelCase convention from JsonOutput.fs |

## Review Guidance

- Compare every text output against `contracts/cli-outputs.md` line by line.
- Verify JSON output is valid (well-formed, parseable).
- Check that `NO_COLOR` is respected in text formatting.
- Verify the compile order in .fsproj is correct (HelpRenderer after HelpSubcommand, Output after Help).
- Run `dotnet build` to confirm compilation.

## Activity Log

- 2026-03-15T23:59:04Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T11:46:56Z – claude-opus – shell_pid=60524 – lane=doing – Assigned agent via workflow command
- 2026-03-16T11:50:46Z – claude-opus – shell_pid=60524 – lane=for_review – Ready for review: HelpRenderer.fs created with text/JSON rendering for enriched --help sections. TextOutput.fs and JsonOutput.fs extended with formatStatusResult, formatHelpIndex, formatTopicText/Json, formatNoMatch. Build succeeds with 0 warnings.
- 2026-03-16T11:52:11Z – claude-opus-4-6 – shell_pid=62603 – lane=doing – Started review via workflow command
- 2026-03-16T11:54:45Z – claude-opus-4-6 – shell_pid=62603 – lane=done – Review passed: All 7 subtasks (T018-T024) correctly implemented. HelpRenderer.fs provides text and JSON rendering for enriched --help sections matching contracts. TextOutput.fs and JsonOutput.fs extended with formatStatusResult, formatHelpIndex, formatTopicText/Json, formatNoMatch per spec guidance. Build succeeds with 0 warnings. Compile order in .fsproj is correct. NO_COLOR respected (no color in new help/status output). Two minor data model limitations noted (extraction timestamp not rendered, artifacts files list empty when present) but these are outside WP04 scope.
- 2026-03-16T14:33:09Z – claude-opus-4-6 – shell_pid=62603 – lane=done – Moved to done
