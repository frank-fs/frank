namespace Frank.Cli.Core.Output

open System
open Frank.Cli.Core.Commands

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
        sb.AppendLine(sprintf "%-18s| %5d" "Classes" result.OntologySummary.ClassCount) |> ignore
        sb.AppendLine(sprintf "%-18s| %5d" "Properties" result.OntologySummary.PropertyCount) |> ignore
        sb.AppendLine(sprintf "%-18s| %5d" "Aligned" result.OntologySummary.AlignedCount) |> ignore
        sb.AppendLine(sprintf "%-18s| %5d" "Unaligned" result.OntologySummary.UnalignedCount) |> ignore
        sb.AppendLine(sprintf "%-18s| %5d" "Shapes" result.ShapesSummary.ShapeCount) |> ignore
        sb.AppendLine(sprintf "%-18s| %5d" "Constraints" result.ShapesSummary.ConstraintCount) |> ignore
        sb.AppendLine(sprintf "%-18s| %5d" "Unmapped Types" (result.UnmappedTypes |> List.length)) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine(sprintf "State saved to: %s" result.StateFilePath) |> ignore

        if not result.UnmappedTypes.IsEmpty then
            sb.AppendLine() |> ignore
            sb.AppendLine(yellow (sprintf "Unmapped types (%d):" result.UnmappedTypes.Length)) |> ignore
            for ut in result.UnmappedTypes do
                sb.AppendLine(sprintf "  - %s (%s) at %s:%d" ut.TypeName ut.Reason ut.Location.File ut.Location.Line) |> ignore

        sb.ToString()

    let formatClarifyResult (result: ClarifyCommand.ClarifyResult) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine(bold "Clarification Questions") |> ignore
        sb.AppendLine(sprintf "Resolved: %d / %d" result.ResolvedCount result.TotalCount) |> ignore
        sb.AppendLine(String.replicate 40 "-") |> ignore

        result.Questions
        |> List.iteri (fun i q ->
            sb.AppendLine() |> ignore
            sb.AppendLine(bold (sprintf "%d. [%s] %s" (i + 1) q.Category q.QuestionText)) |> ignore
            let locStr = q.Context.Location |> Option.defaultValue "unknown"
            sb.AppendLine(sprintf "   Context: %s at %s" q.Context.SourceType locStr) |> ignore
            q.Options
            |> List.iteri (fun j opt ->
                let letter = char (int 'a' + j)
                sb.AppendLine(sprintf "   %c) %s — %s" letter opt.Label opt.Impact) |> ignore))

        sb.ToString()

    let formatValidateResult (result: ValidateCommand.ValidateResult) : string =
        let sb = System.Text.StringBuilder()

        let statusText =
            if result.IsValid then green "VALID" else red "INVALID"

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
                    | "error" -> red "ERROR"
                    | "warning" -> yellow "WARN"
                    | _ -> issue.Severity.ToUpperInvariant()

                let uriStr =
                    issue.Uri
                    |> Option.map (fun u -> sprintf " (%s)" u.AbsoluteUri)
                    |> Option.defaultValue ""

                sb.AppendLine(sprintf "  [%s] %s%s" prefix issue.Message uriStr) |> ignore

        sb.ToString()

    let formatDiffResult (result: DiffCommand.DiffCommandResult) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine(bold "Diff Summary") |> ignore
        sb.AppendLine(sprintf "Added: %d  Removed: %d  Modified: %d"
            result.Diff.Added.Length
            result.Diff.Removed.Length
            result.Diff.Modified.Length) |> ignore
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
            sb.AppendLine(sprintf "  <EmbeddedResource Include=\"...\" LogicalName=\"%s\" />" name) |> ignore
        sb.ToString()

    let formatError (message: string) : string =
        red (sprintf "Error: %s" message)
