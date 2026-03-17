module internal Frank.Statecharts.Smcat.LabelParser

open Frank.Statecharts.Ast
open Frank.Statecharts.Smcat.Types

/// Parse a transition label string in the format "event [guard] / action".
/// Each component is optional. Returns a TransitionLabel record and any warnings.
let parseLabel (label: string) (position: SourcePosition) : TransitionLabel * ParseWarning list =
    if System.String.IsNullOrWhiteSpace(label) then
        ({ Event = None; Guard = None; Action = None }, [])
    else
        let mutable eventText: string option = None
        let mutable guardText: string option = None
        let mutable actionText: string option = None
        let warnings = ResizeArray<ParseWarning>()

        let len = label.Length
        let mutable i = 0

        // Phase 1: Find the bracket position for guard (if any)
        let mutable bracketStart = -1
        let mutable bracketEnd = -1

        let mutable j = 0

        while j < len do
            if label[j] = '[' && bracketStart < 0 then
                bracketStart <- j
            elif label[j] = ']' && bracketStart >= 0 && bracketEnd < 0 then
                bracketEnd <- j

            j <- j + 1

        // Phase 2: Find the action slash position (must be outside brackets)
        let mutable slashPos = -1
        let mutable k = 0

        while k < len do
            if label[k] = '/' then
                // Only count as action slash if it's outside the bracket range
                if bracketStart < 0 || k < bracketStart || (bracketEnd >= 0 && k > bracketEnd) then
                    slashPos <- k
                    k <- len // break
                else
                    k <- k + 1
            else
                k <- k + 1

        // Phase 3: Extract components based on positions found

        // Handle unclosed bracket warning
        if bracketStart >= 0 && bracketEnd < 0 then
            warnings.Add(
                { Position =
                    Some { Line = position.Line
                           Column = position.Column + bracketStart }
                  Description = "Unclosed bracket in transition label"
                  Suggestion = Some "Add closing ']' bracket" }
            )
            // Treat remaining text as guard
            bracketEnd <- len

        // Extract guard text
        if bracketStart >= 0 then
            let guardContent =
                if bracketEnd < len then
                    label.Substring(bracketStart + 1, bracketEnd - bracketStart - 1).Trim()
                else
                    label.Substring(bracketStart + 1).Trim()

            if guardContent.Length > 0 then
                guardText <- Some guardContent

        // Extract action text (everything after the slash)
        if slashPos >= 0 then
            let actionContent = label.Substring(slashPos + 1).Trim()

            if actionContent.Length > 0 then
                actionText <- Some actionContent

        // Extract event text (everything before the first delimiter: [ or /)
        let eventEnd =
            match bracketStart, slashPos with
            | bs, sp when bs >= 0 && sp >= 0 -> min bs sp
            | bs, _ when bs >= 0 -> bs
            | _, sp when sp >= 0 -> sp
            | _ -> len

        let eventContent = label.Substring(0, eventEnd).Trim()

        if eventContent.Length > 0 then
            eventText <- Some eventContent

        ({ Event = eventText
           Guard = guardText
           Action = actionText },
         warnings |> Seq.toList)
