module internal Frank.Statecharts.Wsd.Lexer

open Frank.Statecharts.Wsd.Types

/// Keywords that trigger "rest of line as TextContent" after them.
let private groupingKeywords =
    set
        [ TokenKind.Alt
          TokenKind.Opt
          TokenKind.Loop
          TokenKind.Par
          TokenKind.Break
          TokenKind.Critical
          TokenKind.Ref
          TokenKind.Else ]

let tokenize (source: string) : Token list =
    let len = source.Length
    let mutable pos = 0
    let mutable line = 1
    let mutable col = 1
    let tokens = ResizeArray<Token>()

    let inline peek () =
        if pos < len then source[pos] else '\000'

    let inline peekAt i = if i < len then source[i] else '\000'

    let inline advance () =
        pos <- pos + 1
        col <- col + 1

    let inline newline () =
        line <- line + 1
        col <- 1

    let inline makeToken kind l c =
        { Kind = kind
          Position = { Line = l; Column = c } }

    let inline isAlpha c =
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')

    let inline isDigit c = c >= '0' && c <= '9'

    let inline isAlphaOrUnderscore c = isAlpha c || c = '_'

    let inline isIdentChar c = isAlpha c || isDigit c || c = '_'

    let inline isIdentCharOrHyphen c = isIdentChar c || c = '-'

    let skipWhitespace () =
        while pos < len && (source[pos] = ' ' || source[pos] = '\t') do
            advance ()

    let readIdentWithHyphens () =
        // Read identifier that can contain hyphens, but stop before arrow sequences
        let start = pos

        let mutable cont = true

        while cont && pos < len do
            let c = source[pos]

            if isIdentChar c then
                advance ()
            elif c = '-' then
                // Check if this is an arrow: -> or --> or -->- or ->-
                if peekAt (pos + 1) = '>' then
                    cont <- false
                elif peekAt (pos + 1) = '-' && peekAt (pos + 2) = '>' then
                    cont <- false
                else
                    // hyphen continues the identifier
                    advance ()
            else
                cont <- false

        source.Substring(start, pos - start)

    let readTextContent () =
        // Skip leading whitespace after colon/keyword
        while pos < len && (source[pos] = ' ' || source[pos] = '\t') do
            advance ()

        let textCol = col
        let start = pos

        while pos < len && source[pos] <> '\n' && source[pos] <> '\r' do
            advance ()

        let text = source.Substring(start, pos - start).TrimEnd()
        (text, textCol)

    let tryMatchKeyword (word: string) =
        match word.ToLowerInvariant() with
        | "participant" -> Some TokenKind.Participant
        | "title" -> Some TokenKind.Title
        | "autonumber" -> Some TokenKind.AutoNumber
        | "note" -> Some TokenKind.Note
        | "over" -> Some TokenKind.Over
        | "alt" -> Some TokenKind.Alt
        | "opt" -> Some TokenKind.Opt
        | "loop" -> Some TokenKind.Loop
        | "par" -> Some TokenKind.Par
        | "break" -> Some TokenKind.Break
        | "critical" -> Some TokenKind.Critical
        | "ref" -> Some TokenKind.Ref
        | "else" -> Some TokenKind.Else
        | "end" -> Some TokenKind.End
        | "as" -> Some TokenKind.As
        | _ -> None

    // Main scan loop
    while pos < len do
        let c = source[pos]

        match c with
        | '\n' ->
            tokens.Add(makeToken Newline line col)
            advance ()
            newline ()
        | '\r' ->
            tokens.Add(makeToken Newline line col)
            advance ()

            if pos < len && source[pos] = '\n' then
                advance ()

            newline ()
        | ' '
        | '\t' -> skipWhitespace ()
        | '#' ->
            // Comment: skip to end of line
            while pos < len && source[pos] <> '\n' && source[pos] <> '\r' do
                advance ()
        // Don't emit newline here; the newline char itself will be handled next iteration
        | ':' ->
            let startCol = col
            advance ()
            let (text, textCol) = readTextContent ()

            tokens.Add(makeToken Colon line startCol)

            if text.Length > 0 then
                tokens.Add(makeToken (TextContent text) line textCol)
        | '(' ->
            tokens.Add(makeToken LeftParen line col)
            advance ()
        | ')' ->
            tokens.Add(makeToken RightParen line col)
            advance ()
        | ',' ->
            tokens.Add(makeToken Comma line col)
            advance ()
        | '[' ->
            tokens.Add(makeToken LeftBracket line col)
            advance ()
        | ']' ->
            tokens.Add(makeToken RightBracket line col)
            advance ()
        | '=' ->
            tokens.Add(makeToken Equals line col)
            advance ()
        | '"' ->
            // String literal
            let startCol = col
            advance () // skip opening quote
            let buf = System.Text.StringBuilder()
            let mutable closed = false

            while pos < len && not closed do
                let sc = source[pos]

                if sc = '\\' && peekAt (pos + 1) = '"' then
                    buf.Append('"') |> ignore
                    advance ()
                    advance ()
                elif sc = '"' then
                    closed <- true
                    advance ()
                elif sc = '\n' || sc = '\r' then
                    // Unclosed string at end of line
                    closed <- true
                else
                    buf.Append(sc) |> ignore
                    advance ()

            tokens.Add(makeToken (StringLiteral(buf.ToString())) line startCol)
        | '-' ->
            // Arrow detection
            let startCol = col

            if peekAt (pos + 1) = '-' && peekAt (pos + 2) = '>' && peekAt (pos + 3) = '-' then
                // -->-  DashedDeactivate
                tokens.Add(makeToken DashedDeactivate line startCol)
                advance ()
                advance ()
                advance ()
                advance ()
            elif peekAt (pos + 1) = '-' && peekAt (pos + 2) = '>' then
                // -->  DashedArrow
                tokens.Add(makeToken DashedArrow line startCol)
                advance ()
                advance ()
                advance ()
            elif peekAt (pos + 1) = '>' && peekAt (pos + 2) = '-' then
                // ->-  SolidDeactivate
                tokens.Add(makeToken SolidDeactivate line startCol)
                advance ()
                advance ()
                advance ()
            elif peekAt (pos + 1) = '>' then
                // ->  SolidArrow
                tokens.Add(makeToken SolidArrow line startCol)
                advance ()
                advance ()
            else
                // Stray hyphen — emit as identifier so the parser can report a meaningful error
                tokens.Add(makeToken (Identifier "-") line startCol)
                advance ()
        | _ when isAlphaOrUnderscore c ->
            let startCol = col
            let startLine = line
            let word = readIdentWithHyphens ()

            // Check for multi-word keywords: "left of" / "right of"
            let lower = word.ToLowerInvariant()

            if lower = "left" || lower = "right" then
                // Lookahead for whitespace + "of"
                let savedPos = pos
                let savedCol = col

                let mutable spaceCount = 0

                while pos < len && (source[pos] = ' ' || source[pos] = '\t') do
                    advance ()
                    spaceCount <- spaceCount + 1

                if spaceCount > 0 && pos + 1 < len then
                    let isOf =
                        pos + 2 <= len
                        && (source[pos] = 'o' || source[pos] = 'O')
                        && (source[pos + 1] = 'f' || source[pos + 1] = 'F')

                    if isOf && (pos + 2 >= len || not (isIdentCharOrHyphen (peekAt (pos + 2)))) then
                        // Consume "of"
                        advance ()
                        advance ()

                        let kind =
                            if lower = "left" then
                                TokenKind.LeftOf
                            else
                                TokenKind.RightOf

                        tokens.Add(makeToken kind startLine startCol)
                    else
                        // Not "of", revert
                        pos <- savedPos
                        col <- savedCol

                        match tryMatchKeyword word with
                        | Some kind -> tokens.Add(makeToken kind startLine startCol)
                        | None -> tokens.Add(makeToken (Identifier word) startLine startCol)
                else
                    // No space after left/right, revert
                    pos <- savedPos
                    col <- savedCol

                    match tryMatchKeyword word with
                    | Some kind -> tokens.Add(makeToken kind startLine startCol)
                    | None -> tokens.Add(makeToken (Identifier word) startLine startCol)
            else
                match tryMatchKeyword word with
                | Some kind ->
                    tokens.Add(makeToken kind startLine startCol)

                    // For grouping keywords, emit rest of line as TextContent
                    if groupingKeywords.Contains kind then
                        let (text, textCol) = readTextContent ()

                        if text.Length > 0 then
                            tokens.Add(makeToken (TextContent text) startLine textCol)
                | None -> tokens.Add(makeToken (Identifier word) startLine startCol)
        | _ ->
            // Unknown character, skip
            advance ()

    // Emit final Newline if last token wasn't Newline
    if tokens.Count = 0 || tokens[tokens.Count - 1].Kind <> Newline then
        tokens.Add(makeToken Newline line col)

    tokens.Add(makeToken Eof line col)
    tokens |> Seq.toList
