namespace Frank.Cli.Core.Help

open System.IO
open System.Text
open System.Text.Json

/// Renders help content in text and JSON formats.
/// Handles enriched --help sections (WORKFLOW, EXAMPLES, CONTEXT)
/// and full command help for `frank help <command>`.
module HelpRenderer =

    // --- Text Rendering ---

    /// Render the WORKFLOW section for a command's --help output.
    let renderWorkflowText (help: CommandHelp) (totalSteps: int) : string =
        let sb = StringBuilder()
        sb.AppendLine() |> ignore // blank line before section
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

    // --- JSON Rendering ---

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
