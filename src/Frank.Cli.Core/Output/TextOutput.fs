namespace Frank.Cli.Core.Output

open System
open Frank.Cli.Core.Commands
open Frank.Cli.Core.Help

module TextOutput =

    let private isColorEnabled () =
        let noColor = Environment.GetEnvironmentVariable("NO_COLOR")
        isNull noColor && Console.IsOutputRedirected |> not

    let private bold text =
        if isColorEnabled () then $"\033[1m{text}\033[0m" else text

    let private yellow text =
        if isColorEnabled () then $"\033[33m{text}\033[0m" else text

    let private red text =
        if isColorEnabled () then $"\033[31m{text}\033[0m" else text

    let private green text =
        if isColorEnabled () then $"\033[32m{text}\033[0m" else text

    let formatExtractResult (result: ExtractCommand.ExtractResult) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine(bold "Extraction Summary") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine(sprintf "%-18s| Count" "Category") |> ignore
        sb.AppendLine(sprintf "------------------+------") |> ignore

        sb.AppendLine(sprintf "%-18s| %5d" "Classes" result.OntologySummary.ClassCount)
        |> ignore

        sb.AppendLine(sprintf "%-18s| %5d" "Properties" result.OntologySummary.PropertyCount)
        |> ignore

        sb.AppendLine(sprintf "%-18s| %5d" "Aligned" result.OntologySummary.AlignedCount)
        |> ignore

        sb.AppendLine(sprintf "%-18s| %5d" "Unaligned" result.OntologySummary.UnalignedCount)
        |> ignore

        sb.AppendLine(sprintf "%-18s| %5d" "Shapes" result.ShapesSummary.ShapeCount)
        |> ignore

        sb.AppendLine(sprintf "%-18s| %5d" "Constraints" result.ShapesSummary.ConstraintCount)
        |> ignore

        sb.AppendLine(sprintf "%-18s| %5d" "Unmapped Types" (result.UnmappedTypes |> List.length))
        |> ignore

        sb.AppendLine() |> ignore
        sb.AppendLine(sprintf "State saved to: %s" result.StateFilePath) |> ignore

        if not result.UnmappedTypes.IsEmpty then
            sb.AppendLine() |> ignore

            sb.AppendLine(yellow (sprintf "Unmapped types (%d):" result.UnmappedTypes.Length))
            |> ignore

            for ut in result.UnmappedTypes do
                sb.AppendLine(sprintf "  - %s (%s) at %s:%d" ut.TypeName ut.Reason ut.Location.File ut.Location.Line)
                |> ignore

        sb.ToString()

    let formatClarifyResult (result: ClarifyCommand.ClarifyResult) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine(bold "Clarification Questions") |> ignore

        sb.AppendLine(sprintf "Resolved: %d / %d" result.ResolvedCount result.TotalCount)
        |> ignore

        sb.AppendLine(String.replicate 40 "-") |> ignore

        result.Questions
        |> List.iteri (fun i q ->
            sb.AppendLine() |> ignore

            sb.AppendLine(bold (sprintf "%d. [%s] %s" (i + 1) q.Category q.QuestionText))
            |> ignore

            let locStr = q.Context.Location |> Option.defaultValue "unknown"

            sb.AppendLine(sprintf "   Context: %s at %s" q.Context.SourceType locStr)
            |> ignore

            q.Options
            |> List.iteri (fun j opt ->
                let letter = char (int 'a' + j)
                sb.AppendLine(sprintf "   %c) %s — %s" letter opt.Label opt.Impact) |> ignore))

        sb.ToString()

    let formatValidateResult (result: ValidateCommand.ValidateResult) : string =
        let sb = System.Text.StringBuilder()

        let statusText = if result.IsValid then green "VALID" else red "INVALID"

        sb.AppendLine(bold (sprintf "Validation: %s" statusText)) |> ignore
        sb.AppendLine(sprintf "Coverage: %.1f%%" result.CoveragePercent) |> ignore
        sb.AppendLine() |> ignore

        if result.Issues.IsEmpty then
            sb.AppendLine("No issues found.") |> ignore
        else
            sb.AppendLine(sprintf "Issues (%d):" result.Issues.Length) |> ignore

            for issue in result.Issues do
                let prefix =
                    match issue.Severity with
                    | ValidateCommand.Error -> red "ERROR"
                    | ValidateCommand.Warning -> yellow "WARN"
                    | ValidateCommand.Info -> "INFO"

                let uriStr =
                    issue.Uri
                    |> Option.map (fun u -> sprintf " (%s)" u.AbsoluteUri)
                    |> Option.defaultValue ""

                sb.AppendLine(sprintf "  [%s] %s%s" prefix issue.Message uriStr) |> ignore

        sb.ToString()

    let formatDiffResult (result: DiffCommand.DiffCommandResult) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine(bold "Diff Summary") |> ignore

        sb.AppendLine(
            sprintf
                "Added: %d  Removed: %d  Modified: %d"
                result.Diff.Added.Length
                result.Diff.Removed.Length
                result.Diff.Modified.Length
        )
        |> ignore

        sb.AppendLine() |> ignore
        sb.AppendLine(result.FormattedDiff) |> ignore
        sb.ToString()

    let formatCompileResult (result: CompileCommand.CompileResult) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine(bold "Compile Complete") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine(sprintf "Ontology: %s" result.OntologyPath) |> ignore
        sb.AppendLine(sprintf "Shapes:   %s" result.ShapesPath) |> ignore
        sb.AppendLine(sprintf "Manifest: %s" result.ManifestPath) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("Embed these resources in your project:") |> ignore

        for name in result.EmbeddedResourceNames do
            sb.AppendLine(sprintf "  <EmbeddedResource Include=\"...\" LogicalName=\"%s\" />" name)
            |> ignore

        sb.ToString()

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

    let formatError (message: string) : string = red (sprintf "Error: %s" message)
