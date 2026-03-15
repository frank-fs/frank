module internal Frank.Statecharts.Wsd.Lexer

open Frank.Statecharts.Wsd.Types

let private keywords =
    [ "participant", TokenKind.Participant
      "title", TokenKind.Title
      "autonumber", TokenKind.AutoNumber
      "note", TokenKind.Note
      "over", TokenKind.Over
      "alt", TokenKind.Alt
      "opt", TokenKind.Opt
      "loop", TokenKind.Loop
      "par", TokenKind.Par
      "break", TokenKind.Break
      "critical", TokenKind.Critical
      "ref", TokenKind.Ref
      "else", TokenKind.Else
      "end", TokenKind.End
      "as", TokenKind.As ]
    |> List.map (fun (k, v) -> (k.ToLowerInvariant(), v))
    |> Map.ofList

let private isIdentStart (c: char) = System.Char.IsLetter(c) || c = '_'

let private isIdentChar (c: char) =
    System.Char.IsLetterOrDigit(c) || c = '_' || c = '-'

let tokenize (source: string) : Token list =
    let src = source
    let len = src.Length
    let mutable pos = 0
    let mutable line = 1
    let mutable col = 1
    let tokens = ResizeArray<Token>()

    let inline peek () = if pos < len then src.[pos] else '\000'

    let inline peekAt offset =
        if pos + offset < len then src.[pos + offset] else '\000'

    let inline makeToken kind l c =
        { Kind = kind
          Position = { Line = l; Column = c } }

    let inline advanceChar () =
        pos <- pos + 1
        col <- col + 1

    let inline newLine () =
        line <- line + 1
        col <- 1

    while pos < len do
        let c = peek ()
        let startLine = line
        let startCol = col

        match c with
        | '\r' ->
            advanceChar ()

            if peek () = '\n' then
                advanceChar ()

            tokens.Add(makeToken Newline startLine startCol)
            newLine ()

        | '\n' ->
            advanceChar ()
            tokens.Add(makeToken Newline startLine startCol)
            newLine ()

        | '#' ->
            // Comment: skip to end of line
            while pos < len && peek () <> '\n' && peek () <> '\r' do
                advanceChar ()
        // Don't consume the newline here; the main loop will handle it

        | ' '
        | '\t' ->
            // Skip whitespace (not newlines)
            advanceChar ()

        | ':' ->
            tokens.Add(makeToken Colon startLine startCol)
            advanceChar ()
            // After colon, skip optional whitespace and emit rest of line as TextContent
            while pos < len && (peek () = ' ' || peek () = '\t') do
                advanceChar ()

            let textStart = pos
            let textLine = line
            let textCol = col

            while pos < len && peek () <> '\n' && peek () <> '\r' do
                advanceChar ()

            if textStart < pos then
                let text = src.Substring(textStart, pos - textStart)
                tokens.Add(makeToken (TextContent text) textLine textCol)

        | '(' ->
            tokens.Add(makeToken LeftParen startLine startCol)
            advanceChar ()
        | ')' ->
            tokens.Add(makeToken RightParen startLine startCol)
            advanceChar ()
        | ',' ->
            tokens.Add(makeToken Comma startLine startCol)
            advanceChar ()
        | '[' ->
            tokens.Add(makeToken LeftBracket startLine startCol)
            advanceChar ()
        | ']' ->
            tokens.Add(makeToken RightBracket startLine startCol)
            advanceChar ()
        | '=' ->
            tokens.Add(makeToken Equals startLine startCol)
            advanceChar ()

        | '"' ->
            // String literal
            advanceChar () // skip opening quote
            let sb = System.Text.StringBuilder()

            while pos < len && peek () <> '"' && peek () <> '\n' && peek () <> '\r' do
                if peek () = '\\' && peekAt 1 = '"' then
                    sb.Append('"') |> ignore
                    advanceChar ()
                    advanceChar ()
                else
                    sb.Append(peek ()) |> ignore
                    advanceChar ()

            if pos < len && peek () = '"' then
                advanceChar () // skip closing quote

            tokens.Add(makeToken (StringLiteral(sb.ToString())) startLine startCol)

        | '-' ->
            // Arrow detection
            if peekAt 1 = '-' && peekAt 2 = '>' && peekAt 3 = '-' then
                // -->-
                tokens.Add(makeToken DashedDeactivate startLine startCol)

                for _ in 1..4 do
                    advanceChar ()
            elif peekAt 1 = '-' && peekAt 2 = '>' then
                // -->
                tokens.Add(makeToken DashedArrow startLine startCol)

                for _ in 1..3 do
                    advanceChar ()
            elif peekAt 1 = '>' && peekAt 2 = '-' then
                // ->-
                tokens.Add(makeToken SolidDeactivate startLine startCol)

                for _ in 1..3 do
                    advanceChar ()
            elif peekAt 1 = '>' then
                // ->
                tokens.Add(makeToken SolidArrow startLine startCol)

                for _ in 1..2 do
                    advanceChar ()
            else
                // Standalone hyphen
                advanceChar ()

        | _ when isIdentStart c ->
            let wordStart = pos
            let mutable hitArrow = false

            while pos < len && isIdentChar (peek ()) && not hitArrow do
                if peek () = '-' then
                    if peekAt 1 = '>' then hitArrow <- true
                    elif peekAt 1 = '-' && peekAt 2 = '>' then hitArrow <- true
                    else advanceChar ()
                else
                    advanceChar ()

            let word = src.Substring(wordStart, pos - wordStart)
            let lower = word.ToLowerInvariant()

            // Check for multi-word keywords: left of, right of
            if lower = "left" || lower = "right" then
                let savedPos = pos
                let savedCol = col
                // Skip whitespace
                while pos < len && (peek () = ' ' || peek () = '\t') do
                    advanceChar ()

                if
                    pos + 2 <= len
                    && System.Char.ToLowerInvariant(src.[pos]) = 'o'
                    && System.Char.ToLowerInvariant(src.[pos + 1]) = 'f'
                    && (pos + 2 >= len || not (isIdentChar (src.[pos + 2])))
                then
                    // Consume "of"
                    advanceChar ()
                    advanceChar ()

                    let kind =
                        if lower = "left" then
                            TokenKind.LeftOf
                        else
                            TokenKind.RightOf

                    tokens.Add(makeToken kind startLine startCol)
                else
                    // Backtrack
                    pos <- savedPos
                    col <- savedCol

                    match Map.tryFind lower keywords with
                    | Some kind -> tokens.Add(makeToken kind startLine startCol)
                    | None -> tokens.Add(makeToken (Identifier word) startLine startCol)
            else
                match Map.tryFind lower keywords with
                | Some kind ->
                    tokens.Add(makeToken kind startLine startCol)

                    // After group keywords, emit rest of line as TextContent
                    match kind with
                    | TokenKind.Alt
                    | TokenKind.Opt
                    | TokenKind.Loop
                    | TokenKind.Par
                    | TokenKind.Break
                    | TokenKind.Critical
                    | TokenKind.Ref ->
                        // Skip whitespace after keyword
                        while pos < len && (peek () = ' ' || peek () = '\t') do
                            advanceChar ()

                        let textStart = pos
                        let textLine = line
                        let textCol = col

                        while pos < len && peek () <> '\n' && peek () <> '\r' do
                            advanceChar ()

                        if textStart < pos then
                            let text = src.Substring(textStart, pos - textStart)
                            tokens.Add(makeToken (TextContent text) textLine textCol)
                    | TokenKind.Title ->
                        // After title keyword, if no colon, emit rest as TextContent
                        let savedP = pos
                        let savedC = col

                        while pos < len && (peek () = ' ' || peek () = '\t') do
                            advanceChar ()

                        if pos < len && peek () = ':' then
                            // Let the colon handling happen normally; backtrack
                            pos <- savedP
                            col <- savedC
                        elif pos < len && peek () <> '\n' && peek () <> '\r' then
                            let textStart = pos
                            let textLine = line
                            let textCol = col

                            while pos < len && peek () <> '\n' && peek () <> '\r' do
                                advanceChar ()

                            let text = src.Substring(textStart, pos - textStart)
                            tokens.Add(makeToken (TextContent text) textLine textCol)
                        else
                            pos <- savedP
                            col <- savedC
                    | _ -> ()
                | None -> tokens.Add(makeToken (Identifier word) startLine startCol)

        | _ ->
            // Unknown character, skip
            advanceChar ()

    tokens.Add(makeToken Eof line col)
    Seq.toList tokens
