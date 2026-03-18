namespace Frank.Statecharts.Validation

open Frank.Statecharts.Ast

/// End-to-end validation pipeline: parse format sources and validate.
module Pipeline =

    let private emptyReport =
        { TotalChecks = 0; TotalSkipped = 0; TotalFailures = 0
          Checks = []; Failures = [] }

    /// Look up the parser function for a given format tag.
    /// Returns None for formats with no registered parser (e.g., XState).
    let private parserFor (tag: FormatTag) : (string -> ParseResult) option =
        match tag with
        | FormatTag.Wsd -> Some Frank.Statecharts.Wsd.Parser.parseWsd
        | FormatTag.Smcat -> Some Frank.Statecharts.Smcat.Parser.parseSmcat
        | FormatTag.Scxml -> Some Frank.Statecharts.Scxml.Parser.parseString
        | FormatTag.Alps -> Some Frank.Statecharts.Alps.JsonParser.parseAlpsJson
        | FormatTag.AlpsXml -> Some Frank.Statecharts.Alps.XmlParser.parseAlpsXml
        | FormatTag.XState -> None

    /// Parse a single (FormatTag * string) pair, returning either a
    /// (FormatParseResult * FormatArtifact) on success or a PipelineError.
    let private parseSource (tag: FormatTag) (source: string) =
        match parserFor tag with
        | None -> Error (UnsupportedFormat tag)
        | Some parser ->
            let result = parser source
            let pr =
                { Format = tag
                  Errors = result.Errors
                  Warnings = result.Warnings
                  Succeeded = List.isEmpty result.Errors }
            let art = { Format = tag; Document = result.Document }
            Ok (pr, art)

    /// Validate format sources with custom rules prepended to built-in rules.
    let validateSourcesWithRules
        (customRules: ValidationRule list)
        (sources: (FormatTag * string) list)
        : PipelineResult =
        if List.isEmpty sources then
            { ParseResults = []; Report = emptyReport; Errors = [] }
        else
            let duplicates =
                sources
                |> List.map fst
                |> List.groupBy id
                |> List.filter (fun (_, group) -> group.Length > 1)
                |> List.map (fun (tag, _) -> DuplicateFormat tag)

            if not (List.isEmpty duplicates) then
                { ParseResults = []; Report = emptyReport; Errors = duplicates }
            else
                let parseResults, artifacts, pipelineErrors =
                    (([], [], []), sources)
                    ||> List.fold (fun (prs, arts, errs) (tag, source) ->
                        match parseSource tag source with
                        | Ok (pr, art) -> (pr :: prs, art :: arts, errs)
                        | Error e -> (prs, arts, e :: errs))
                    |> fun (prs, arts, errs) -> (List.rev prs, List.rev arts, List.rev errs)

                let allRules = customRules @ SelfConsistencyRules.rules @ CrossFormatRules.rules
                let report = Validator.validate allRules artifacts

                { ParseResults = parseResults
                  Report = report
                  Errors = pipelineErrors }

    /// Validate format sources using built-in self-consistency and cross-format rules.
    let validateSources (sources: (FormatTag * string) list) : PipelineResult =
        validateSourcesWithRules [] sources
