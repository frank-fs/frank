namespace Frank.Statecharts.Validation

open Frank.Statecharts.Ast

/// End-to-end validation pipeline: parse format sources and validate.
module Pipeline =

    /// Look up the parser function for a given format tag.
    /// Returns None for formats with no registered parser (e.g., XState).
    let private parserFor (tag: FormatTag) : (string -> ParseResult) option =
        match tag with
        | FormatTag.Wsd -> Some Frank.Statecharts.Wsd.Parser.parseWsd
        | FormatTag.Smcat -> Some Frank.Statecharts.Smcat.Parser.parseSmcat
        | FormatTag.Scxml -> Some Frank.Statecharts.Scxml.Parser.parseString
        | FormatTag.Alps -> Some Frank.Statecharts.Alps.JsonParser.parseAlpsJson
        | FormatTag.XState -> None

    /// Validate format sources with custom rules prepended to built-in rules.
    let validateSourcesWithRules
        (customRules: ValidationRule list)
        (sources: (FormatTag * string) list)
        : PipelineResult =
        if List.isEmpty sources then
            { ParseResults = []
              Report =
                  { TotalChecks = 0; TotalSkipped = 0; TotalFailures = 0
                    Checks = []; Failures = [] }
              Errors = [] }
        else
            let duplicates =
                sources
                |> List.map fst
                |> List.groupBy id
                |> List.filter (fun (_, group) -> group.Length > 1)
                |> List.map (fun (tag, _) -> DuplicateFormat tag)

            if not (List.isEmpty duplicates) then
                { ParseResults = []
                  Report =
                      { TotalChecks = 0; TotalSkipped = 0; TotalFailures = 0
                        Checks = []; Failures = [] }
                  Errors = duplicates }
            else
                let mutable pipelineErrors = []
                let parseResults = ResizeArray<FormatParseResult>()
                let artifacts = ResizeArray<FormatArtifact>()

                for (tag, source) in sources do
                    match parserFor tag with
                    | None ->
                        pipelineErrors <- UnsupportedFormat tag :: pipelineErrors
                    | Some parser ->
                        let result = parser source
                        parseResults.Add(
                            { Format = tag
                              Errors = result.Errors
                              Warnings = result.Warnings
                              Succeeded = List.isEmpty result.Errors })
                        artifacts.Add(
                            { Format = tag
                              Document = result.Document })

                let allRules = customRules @ SelfConsistencyRules.rules @ CrossFormatRules.rules
                let report = Validator.validate allRules (Seq.toList artifacts)

                { ParseResults = Seq.toList parseResults
                  Report = report
                  Errors = List.rev pipelineErrors }

    /// Validate format sources using built-in self-consistency and cross-format rules.
    let validateSources (sources: (FormatTag * string) list) : PipelineResult =
        validateSourcesWithRules [] sources
