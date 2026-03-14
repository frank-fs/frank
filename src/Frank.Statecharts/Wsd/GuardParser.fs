module internal Frank.Statecharts.Wsd.GuardParser

open Frank.Statecharts.Wsd.Types

/// Try to parse a guard annotation from the beginning of content.
/// Returns (guard option, remaining text, errors, warnings).
let tryParseGuard
    (content: string)
    (position: SourcePosition)
    : (GuardAnnotation option * string * ParseFailure list * ParseWarning list) =
    if System.String.IsNullOrEmpty(content) then
        (None, content, [], [])
    else
        // Trim leading whitespace and track column offset
        let mutable leadingSpaces = 0

        while leadingSpaces < content.Length && content.[leadingSpaces] = ' ' do
            leadingSpaces <- leadingSpaces + 1

        let trimmed = content.Substring(leadingSpaces)
        let baseCol = position.Column + leadingSpaces

        // Check if starts with [guard: (case-insensitive)
        let prefix = "[guard:"
        let prefixLen = prefix.Length

        if trimmed.Length < prefixLen then
            (None, content, [], [])
        elif
            System.String.Compare(trimmed.Substring(0, prefixLen), prefix, System.StringComparison.OrdinalIgnoreCase)
            <> 0
        then
            (None, content, [], [])
        else
            // Find closing ]
            let mutable closingIdx = -1
            let mutable i = prefixLen

            while i < trimmed.Length && closingIdx < 0 do
                if trimmed.[i] = ']' then
                    closingIdx <- i

                i <- i + 1

            if closingIdx < 0 then
                // Unclosed bracket
                let failure =
                    { Position =
                        { Line = position.Line
                          Column = baseCol }
                      Description = "Unclosed guard annotation bracket"
                      Expected = "Closing ']' bracket"
                      Found = trimmed
                      CorrectiveExample = trimmed + "]" }

                (None, content, [ failure ], [])
            else
                let inner = trimmed.Substring(prefixLen, closingIdx - prefixLen)

                let remaining =
                    let afterBracket = trimmed.Substring(closingIdx + 1)

                    if afterBracket.Length > 0 && afterBracket.[0] = ' ' then
                        afterBracket.Substring(1)
                    else
                        afterBracket

                let innerTrimmed = inner.Trim()

                if innerTrimmed.Length = 0 then
                    // Empty guard
                    let warning =
                        { Position =
                            { Line = position.Line
                              Column = baseCol + prefixLen }
                          Description = "Empty guard annotation"
                          Suggestion = Some "Add key=value pairs or remove the guard annotation" }

                    let guard =
                        { Pairs = []
                          Position =
                            { Line = position.Line
                              Column = baseCol } }

                    (Some guard, remaining, [], [ warning ])
                else
                    // Split on commas
                    let parts = innerTrimmed.Split(',')
                    let mutable pairs = []
                    let mutable errors = []
                    let mutable warnings = []
                    // Track column offset within inner content
                    let mutable colOffset = baseCol + prefixLen

                    for partIdx in 0 .. parts.Length - 1 do
                        let part = parts.[partIdx]
                        let partTrimmed = part.Trim()
                        // Calculate column for this part within the inner string
                        let partCol =
                            if partIdx = 0 then
                                colOffset + (inner.Length - inner.TrimStart().Length)
                            else
                                // Approximate: after previous parts + commas
                                colOffset

                        if partTrimmed.Length = 0 then
                            // Skip empty parts from trailing commas
                            ()
                        else
                            let eqIdx = partTrimmed.IndexOf('=')

                            if eqIdx < 0 then
                                // Missing equals
                                let failure =
                                    { Position =
                                        { Line = position.Line
                                          Column = partCol }
                                      Description = "Missing '=' in guard pair"
                                      Expected = "key=value"
                                      Found = partTrimmed
                                      CorrectiveExample = partTrimmed + "=value" }

                                errors <- errors @ [ failure ]
                            elif eqIdx = 0 then
                                // Empty key
                                let failure =
                                    { Position =
                                        { Line = position.Line
                                          Column = partCol }
                                      Description = "Empty key in guard annotation"
                                      Expected = "key=value"
                                      Found = partTrimmed
                                      CorrectiveExample = "key" + partTrimmed }

                                errors <- errors @ [ failure ]
                            else
                                let key = partTrimmed.Substring(0, eqIdx).Trim()
                                let value = partTrimmed.Substring(eqIdx + 1).Trim()

                                if value.Length = 0 then
                                    let warning =
                                        { Position =
                                            { Line = position.Line
                                              Column = partCol + eqIdx + 1 }
                                          Description = "Empty value in guard annotation"
                                          Suggestion = Some("Provide a value for key '" + key + "'") }

                                    warnings <- warnings @ [ warning ]

                                pairs <- pairs @ [ (key, value) ]

                        // Advance column offset past this part and the comma
                        colOffset <- colOffset + part.Length + 1

                    let guard =
                        { Pairs = pairs
                          Position =
                            { Line = position.Line
                              Column = baseCol } }

                    (Some guard, remaining, errors, warnings)
