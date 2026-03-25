module Frank.Cli.Core.Commands.StatechartParseCommand

open System.IO
open Frank.Statecharts.Ast
open Frank.Statecharts.Validation
open Frank.Cli.Core.Shared
open Frank.Cli.Core.Statechart.StatechartError

type ParseCommandResult =
    { ParseResult: ParseResult
      Format: FormatTag
      HasErrors: bool }

let private parseFormatString (s: string) : Result<FormatTag, StatechartError> =
    match s.ToLowerInvariant() with
    | "wsd" -> Ok Wsd
    | "alps" -> Ok Alps
    | "alps-xml"
    | "alpsxml" -> Ok AlpsXml
    | "scxml" -> Ok Scxml
    | "smcat" -> Ok Smcat
    | "xstate" -> Ok XState
    | other -> Error(UnknownFormat other)

let private parseContent (format: FormatTag) (content: string) : ParseResult =
    match format with
    | Wsd -> Frank.Statecharts.Wsd.Parser.parseWsd content
    | Alps -> Frank.Statecharts.Alps.JsonParser.parseAlpsJson content
    | AlpsXml -> Frank.Statecharts.Alps.XmlParser.parseAlpsXml content
    | Scxml -> Frank.Statecharts.Scxml.Parser.parseString content
    | Smcat -> Frank.Statecharts.Smcat.Parser.parseSmcat content
    | XState -> Frank.Statecharts.XState.Deserializer.deserialize content

let execute (specFile: string) (explicitFormat: string option) : Result<ParseCommandResult, StatechartError> =
    if not (File.Exists specFile) then
        Error(FileNotFound specFile)
    else
        let content = File.ReadAllText(specFile)

        let resolveFormat () =
            match explicitFormat with
            | Some fmt -> parseFormatString fmt
            | None ->
                match Frank.Cli.Core.Statechart.FormatDetector.detect specFile with
                | Frank.Cli.Core.Statechart.FormatDetector.Detected format -> Ok format
                | Frank.Cli.Core.Statechart.FormatDetector.Ambiguous candidates ->
                    // Ambiguous extension: try each candidate, pick whichever parses without errors.
                    // Failures are expected here — non-matching formats throw, and tryFcs
                    // filters them out so only successfully-parsed formats survive.
                    let tryParse fmt =
                        tryFcs None (fun () ->
                            let r = parseContent fmt content
                            if r.Errors.IsEmpty then Some fmt else None)

                    let successes = candidates |> List.choose tryParse

                    match successes with
                    | [ single ] -> Ok single
                    | [] ->
                        let names =
                            candidates
                            |> List.map (fun t -> Frank.Cli.Core.Statechart.FormatDetector.FormatTag.toString t)

                        Error(AmbiguousParseFailed(specFile, names))
                    | _ -> Error(AmbiguousFileExtension(specFile, candidates))
                | Frank.Cli.Core.Statechart.FormatDetector.Unsupported ext ->
                    Error(UnsupportedFileExtension(ext, specFile))

        match resolveFormat () with
        | Error e -> Error e
        | Ok format ->
            let result = parseContent format content

            Ok
                { ParseResult = result
                  Format = format
                  HasErrors = not result.Errors.IsEmpty }
