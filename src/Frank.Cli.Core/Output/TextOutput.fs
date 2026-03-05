namespace Frank.Cli.Core.Output

open System

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

    let formatExtractResult (result: JsonOutput.ExtractResultJson) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine(bold "Extraction Summary") |> ignore
        sb.AppendLine(String.replicate 40 "-") |> ignore
        sb.AppendLine(sprintf "  %-20s %d" "Classes" result.ClassCount) |> ignore
        sb.AppendLine(sprintf "  %-20s %d" "Properties" result.PropertyCount) |> ignore
        sb.AppendLine(sprintf "  %-20s %d" "Shapes" result.ShapeCount) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine(sprintf "State saved to: %s" result.StateFilePath) |> ignore

        if not result.UnmappedTypes.IsEmpty then
            sb.AppendLine() |> ignore
            sb.AppendLine(yellow (sprintf "Unmapped types (%d):" result.UnmappedTypes.Length)) |> ignore
            for ut in result.UnmappedTypes do
                sb.AppendLine(sprintf "  - %s (%s) at %s:%d" ut.TypeName ut.Reason ut.File ut.Line) |> ignore

        sb.ToString()

    let formatClarifyResult (result: JsonOutput.ClarifyResultJson) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine(bold "Clarification Questions") |> ignore
        sb.AppendLine(sprintf "Resolved: %d / %d" result.ResolvedCount result.TotalCount) |> ignore
        sb.AppendLine(String.replicate 40 "-") |> ignore

        result.Questions
        |> List.iteri (fun i q ->
            sb.AppendLine() |> ignore
            sb.AppendLine(bold (sprintf "%d. [%s] %s" (i + 1) q.Category q.QuestionText)) |> ignore
            sb.AppendLine(sprintf "   Context: %s at %s" q.Context.SourceType q.Context.Location) |> ignore
            q.Options
            |> List.iteri (fun j opt ->
                let letter = char (int 'a' + j)
                sb.AppendLine(sprintf "   %c) %s — %s" letter opt.Label opt.Impact) |> ignore))

        sb.ToString()

    let formatError (message: string) : string =
        red (sprintf "Error: %s" message)
