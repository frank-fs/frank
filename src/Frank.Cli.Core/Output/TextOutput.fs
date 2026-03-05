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

    let formatError (message: string) : string =
        red (sprintf "Error: %s" message)
