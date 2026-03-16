module internal Frank.Statecharts.Smcat.Lexer
<<<<<<< HEAD
// Implementation in WP02
=======

open Frank.Statecharts.Smcat.Types

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

    let inline isIdentStartChar c =
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c = '_'

    let inline isIdentChar c =
        isIdentStartChar c || (c >= '0' && c <= '9') || c = '.' || c = '-'

    let skipWhitespace () =
        while pos < len && (source[pos] = ' ' || source[pos] = '\t') do
            advance ()

    // Main scan loop
    while pos < len do
        let c = source[pos]

        match c with
        | ' '
        | '\t' -> skipWhitespace ()
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
        | '#' ->
            // Comment: skip to end of line (do NOT emit a token)
            while pos < len && source[pos] <> '\n' && source[pos] <> '\r' do
                advance ()
        // Don't emit newline here; the newline char itself will be handled next iteration
        | '=' ->
            let startCol = col

            if peekAt (pos + 1) = '>' then
                // Transition arrow =>
                advance ()
                advance ()
                tokens.Add(makeToken TransitionArrow line startCol)
            else
                // Equals sign (used in attributes)
                advance ()
                tokens.Add(makeToken Equals line startCol)
        | ':' ->
            tokens.Add(makeToken Colon line col)
            advance ()
        | ';' ->
            tokens.Add(makeToken Semicolon line col)
            advance ()
        | ',' ->
            tokens.Add(makeToken Comma line col)
            advance ()
        | '[' ->
            tokens.Add(makeToken LeftBracket line col)
            advance ()
        | ']' ->
            // Context-dependent: CloseBracketPrefix at statement start, RightBracket otherwise.
            // Check if ] appears at statement start (first token, or after statement terminator/newline/brace).
            let isStatementStart =
                tokens.Count = 0
                || (let prevKind = tokens[tokens.Count - 1].Kind

                    prevKind = Semicolon
                    || prevKind = Comma
                    || prevKind = LeftBrace
                    || prevKind = RightBrace
                    || prevKind = Newline)

            if isStatementStart then
                tokens.Add(makeToken CloseBracketPrefix line col)
            else
                tokens.Add(makeToken RightBracket line col)

            advance ()
        | '{' ->
            tokens.Add(makeToken LeftBrace line col)
            advance ()
        | '}' ->
            tokens.Add(makeToken RightBrace line col)
            advance ()
        | '^' ->
            tokens.Add(makeToken Caret line col)
            advance ()
        | '/' ->
            tokens.Add(makeToken ForwardSlash line col)
            advance ()
        | '.' ->
            // Check for ellipsis ...
            if peekAt (pos + 1) = '.' && peekAt (pos + 2) = '.' then
                let startCol = col
                advance ()
                advance ()
                advance ()
                tokens.Add(makeToken Ellipsis line startCol)
            else
                // Lone dot(s) -- treat as start of identifier if followed by ident chars,
                // otherwise skip unknown character
                advance ()
        | '"' ->
            // Quoted string
            let startCol = col
            let startLine = line
            advance () // skip opening quote
            let start = pos
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
                else
                    buf.Append(sc) |> ignore

                    if sc = '\n' then
                        advance ()
                        newline ()
                    elif sc = '\r' then
                        advance ()

                        if pos < len && source[pos] = '\n' then
                            buf.Append('\n') |> ignore
                            advance ()

                        newline ()
                    else
                        advance ()

            tokens.Add(makeToken (QuotedString(buf.ToString())) startLine startCol)
        | _ when isIdentStartChar c || (c >= '0' && c <= '9') ->
            // Identifier scanning
            let startCol = col
            let startLine = line
            let start = pos
            let mutable cont = true

            while cont && pos < len do
                let ch = source[pos]

                if isIdentChar ch then
                    // Check for arrow: stop if '=' followed by '>'
                    if ch = '=' && peekAt (pos + 1) = '>' then
                        cont <- false
                    else
                        advance ()
                else
                    cont <- false

            // Trim trailing dots and hyphens
            while pos > start && (source[pos - 1] = '.' || source[pos - 1] = '-') do
                pos <- pos - 1
                col <- col - 1

            let identText = source.Substring(start, pos - start)

            // Check for entry/ and exit/ activity keywords
            if (identText = "entry" || identText = "exit") && pos < len && source[pos] = '/' then
                advance () // consume the '/'

                if identText = "entry" then
                    tokens.Add(makeToken EntrySlash startLine startCol)
                else
                    tokens.Add(makeToken ExitSlash startLine startCol)
            else
                tokens.Add(makeToken (Identifier identText) startLine startCol)
        | _ ->
            // Unknown character, skip
            advance ()

    tokens.Add(makeToken Eof line col)
    tokens |> Seq.toList
>>>>>>> 013-smcat-parser-generator-WP03
