module Frank.Cli.Core.Commands.StatechartValidateCommand

open System.IO
open Frank.Statecharts.Ast
open Frank.Statecharts.Validation
open Frank.Cli.Core.Statechart
open Frank.Cli.Core.Statechart.StatechartError

/// Result of running the statechart validate command.
type ValidateResult =
    { Report: ValidationReport
      HasFailures: bool }

/// Parse a single spec file to a FormatArtifact based on detected format.
/// Returns Error if format detection fails, the file is missing, or parsing fails fatally.
let private parseSpecFile (filePath: string) : Result<FormatArtifact, StatechartError> =
    if not (File.Exists(filePath)) then
        Error (FileNotFound filePath)
    else

    let content =
        try
            Ok(File.ReadAllText(filePath))
        with ex ->
            Error (FileReadError(filePath, ex.Message))

    match content with
    | Error e -> Error e
    | Ok text ->

    match FormatDetector.detect filePath with
    | FormatDetector.DetectionResult.Unsupported ext ->
        Error (UnsupportedFileExtension(ext, filePath))
    | FormatDetector.DetectionResult.Ambiguous candidates ->
        // For ambiguous files (.json or .xml), try each candidate format until one parses cleanly
        let tryParse =
            candidates
            |> List.tryPick (fun tag ->
                match tag with
                | Alps ->
                    let result = Frank.Statecharts.Alps.JsonParser.parseAlpsJson text
                    if result.Errors.IsEmpty then
                        Some { Format = Alps; Document = result.Document }
                    else
                        None
                | AlpsXml ->
                    let result = Frank.Statecharts.Alps.XmlParser.parseAlpsXml text
                    if result.Errors.IsEmpty then
                        Some { Format = AlpsXml; Document = result.Document }
                    else
                        None
                | Scxml ->
                    let result = Frank.Statecharts.Scxml.Parser.parseString text
                    if result.Errors.IsEmpty then
                        Some { Format = Scxml; Document = result.Document }
                    else
                        None
                | XState ->
                    let result = Frank.Statecharts.XState.Deserializer.deserialize text
                    if result.Errors.IsEmpty then
                        Some { Format = XState; Document = result.Document }
                    else
                        None
                | _ -> None)

        match tryParse with
        | Some artifact -> Ok artifact
        | None ->
            Error (AmbiguousFileExtension(filePath, candidates))
    | FormatDetector.DetectionResult.Detected format ->
        let parseResult : Result<ParseResult, StatechartError> =
            match format with
            | Wsd ->
                let result = Frank.Statecharts.Wsd.Parser.parseWsd text
                Ok result
            | Alps ->
                let result = Frank.Statecharts.Alps.JsonParser.parseAlpsJson text
                Ok result
            | AlpsXml ->
                let result = Frank.Statecharts.Alps.XmlParser.parseAlpsXml text
                Ok result
            | Scxml ->
                let result = Frank.Statecharts.Scxml.Parser.parseString text
                Ok result
            | Smcat ->
                let result = Frank.Statecharts.Smcat.Parser.parseSmcat text
                Ok result
            | XState ->
                let result = Frank.Statecharts.XState.Deserializer.deserialize text
                Ok result

        match parseResult with
        | Error e -> Error e
        | Ok pr -> Ok { Format = format; Document = pr.Document }

/// Run cross-format validation on spec files and return the ValidationReport.
///
/// specFiles: paths to statechart spec files (.wsd, .alps.json, .alps.xml, .scxml, .smcat, .xstate.json)
let execute (specFiles: string list) : Result<ValidateResult, StatechartError> =
    // 1. Parse all spec files
    let parseResults =
        specFiles |> List.map (fun f -> (f, parseSpecFile f))

    // Return first error encountered
    let firstError =
        parseResults
        |> List.tryPick (fun (_, r) ->
            match r with
            | Error e -> Some e
            | Ok _ -> None)

    match firstError with
    | Some e -> Error e
    | None ->

    let specArtifacts =
        parseResults
        |> List.choose (fun (_, r) ->
            match r with
            | Ok artifact -> Some artifact
            | Error _ -> None)

    // 2. Run validation with both rule sets
    let allRules =
        SelfConsistencyRules.rules @ CrossFormatRules.rules

    let report = Validator.validate allRules specArtifacts

    // 3. Build result
    let result =
        { Report = report
          HasFailures = report.TotalFailures > 0 }

    Ok result
