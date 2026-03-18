module Frank.Cli.Core.Statechart.ValidationReportFormatter

open System
open System.IO
open System.Text
open System.Text.Json
open Frank.Statecharts.Validation
open Frank.Cli.Core.Output.AnsiColors
open Frank.Cli.Core.Statechart.FormatDetector

let private statusString (status: CheckStatus) =
    match status with
    | Pass -> "pass"
    | Fail -> "fail"
    | Skip -> "skip"

/// Format a ValidationReport as human-readable text with optional ANSI colors.
let formatText (report: ValidationReport) : string =
    let sb = StringBuilder()

    sb.AppendLine(bold "Validation Report") |> ignore
    sb.AppendLine(String('-', 40)) |> ignore

    let status =
        if report.TotalFailures = 0 then
            green "PASSED"
        else
            red "FAILED"

    sb.AppendLine($"Status: {status}") |> ignore

    sb.AppendLine(
        $"Checks: {report.TotalChecks} | Failures: {report.TotalFailures} | Skipped: {report.TotalSkipped}"
    )
    |> ignore

    sb.AppendLine() |> ignore

    let passed = report.Checks |> List.filter (fun c -> c.Status = Pass)
    let failed = report.Checks |> List.filter (fun c -> c.Status = Fail)
    let skipped = report.Checks |> List.filter (fun c -> c.Status = Skip)

    for c in passed do
        let prefix = green "PASS"
        sb.AppendLine($"  {prefix} {c.Name}") |> ignore

    for c in failed do
        let prefix = red "FAIL"

        let reason =
            match c.Reason with
            | Some r -> $" — {r}"
            | None -> ""

        sb.AppendLine($"  {prefix} {c.Name}{reason}") |> ignore

    for c in skipped do
        let prefix = yellow "SKIP"

        let reason =
            match c.Reason with
            | Some r -> $" — {r}"
            | None -> ""

        sb.AppendLine($"  {prefix} {c.Name}{reason}") |> ignore

    if report.TotalFailures > 0 && not (List.isEmpty report.Failures) then
        sb.AppendLine() |> ignore
        sb.AppendLine(bold "Failure Details") |> ignore
        sb.AppendLine(String('-', 40)) |> ignore

        for f in report.Failures do
            sb.AppendLine($"  {bold f.Description}") |> ignore

            let formats = f.Formats |> List.map FormatTag.toString |> String.concat ", "
            sb.AppendLine($"    Formats: {formats}") |> ignore
            sb.AppendLine($"    Entity:   {f.EntityType}") |> ignore
            sb.AppendLine($"    Expected: {f.Expected}") |> ignore
            sb.AppendLine($"    Actual:   {f.Actual}") |> ignore
            sb.AppendLine() |> ignore

    sb.ToString()

/// Format a ValidationReport as indented JSON.
let formatJson (report: ValidationReport) : string =
    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

    writer.WriteStartObject()

    writer.WriteString("status", if report.TotalFailures = 0 then "passed" else "failed")
    writer.WriteNumber("totalChecks", report.TotalChecks)
    writer.WriteNumber("totalFailures", report.TotalFailures)
    writer.WriteNumber("totalSkipped", report.TotalSkipped)

    writer.WriteStartArray("checks")

    for c in report.Checks do
        writer.WriteStartObject()
        writer.WriteString("name", c.Name)
        writer.WriteString("status", statusString c.Status)

        match c.Reason with
        | Some r -> writer.WriteString("reason", r)
        | None -> writer.WriteNull("reason")

        writer.WriteEndObject()

    writer.WriteEndArray()

    writer.WriteStartArray("failures")

    for f in report.Failures do
        writer.WriteStartObject()

        writer.WriteStartArray("formats")

        for fmt in f.Formats do
            writer.WriteStringValue(FormatTag.toString fmt)

        writer.WriteEndArray()

        writer.WriteString("entityType", f.EntityType)
        writer.WriteString("expected", f.Expected)
        writer.WriteString("actual", f.Actual)
        writer.WriteString("description", f.Description)
        writer.WriteEndObject()

    writer.WriteEndArray()

    writer.WriteEndObject()
    writer.Flush()
    Encoding.UTF8.GetString(stream.ToArray())
