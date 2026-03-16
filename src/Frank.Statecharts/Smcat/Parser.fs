module internal Frank.Statecharts.Smcat.Parser

open Frank.Statecharts.Smcat.Types
open Frank.Statecharts.Smcat.LabelParser
open Frank.Statecharts.Smcat.Lexer

// T013: Core parser infrastructure

type ParserState =
    { Tokens: Token array
      mutable Position: int
      mutable Elements: SmcatElement list
      mutable Errors: ParseFailure list
      mutable Warnings: ParseWarning list
      mutable ErrorLimitReached: bool
      MaxErrors: int }

let private eofToken =
    { Kind = Eof
      Position = { Line = 0; Column = 0 } }

let private peek (state: ParserState) : Token =
    if state.Position < state.Tokens.Length then
        state.Tokens[state.Position]
    else
        eofToken

let private advance (state: ParserState) : Token =
    let token = peek state

    if state.Position < state.Tokens.Length then
        state.Position <- state.Position + 1

    token

let private expect (state: ParserState) (kind: TokenKind) : Token option =
    let tok = peek state

    if tok.Kind = kind then
        advance state |> ignore
        Some tok
    else
        None

let private skipNewlines (state: ParserState) : unit =
    while (peek state).Kind = Newline do
        state.Position <- state.Position + 1

let private addError
    (state: ParserState)
    (pos: SourcePosition)
    (desc: string)
    (expected: string)
    (found: string)
    (example: string)
    : unit =
    if not state.ErrorLimitReached then
        let failure =
            { Position = pos
              Description = desc
              Expected = expected
              Found = found
              CorrectiveExample = example }

        state.Errors <- state.Errors @ [ failure ]

        if state.Errors.Length >= state.MaxErrors then
            state.ErrorLimitReached <- true

let private addWarning (state: ParserState) (pos: SourcePosition) (desc: string) (suggestion: string option) : unit =
    let warning =
        { Position = pos
          Description = desc
          Suggestion = suggestion }

    state.Warnings <- state.Warnings @ [ warning ]

let private createState (tokens: Token list) (maxErrors: int) : ParserState =
    { Tokens = tokens |> Array.ofList
      Position = 0
      Elements = []
      Errors = []
      Warnings = []
      ErrorLimitReached = false
      MaxErrors = maxErrors }

let private tokenDescription (token: Token) : string =
    match token.Kind with
    | Identifier name -> sprintf "identifier '%s'" name
    | QuotedString s -> sprintf "string \"%s\"" s
    | Newline -> "newline"
    | Eof -> "end of input"
    | Colon -> "':'"
    | Semicolon -> "';'"
    | Comma -> "','"
    | LeftBracket -> "'['"
    | RightBracket -> "']'"
    | LeftBrace -> "'{'"
    | RightBrace -> "'}'"
    | ForwardSlash -> "'/'"
    | Equals -> "'='"
    | TransitionArrow -> "'=>'"
    | Caret -> "'^'"
    | CloseBracketPrefix -> "']' (prefix)"
    | EntrySlash -> "'entry/'"
    | ExitSlash -> "'exit/'"
    | Ellipsis -> "'...'"

/// Check whether the current token is a statement terminator.
let private isTerminator (kind: TokenKind) : bool =
    match kind with
    | Semicolon
    | Comma
    | Newline
    | Eof
    | RightBrace -> true
    | _ -> false

/// Skip to the next statement boundary (for basic error recovery).
let private skipToTerminator (state: ParserState) : unit =
    while not (isTerminator (peek state).Kind) do
        advance state |> ignore

/// Consume a statement terminator if present.
let private consumeTerminator (state: ParserState) : unit =
    let kind = (peek state).Kind

    if kind = Semicolon || kind = Comma then
        advance state |> ignore

// T014: Attribute parsing

/// Parse attributes: [key=value key2="quoted value"]
let private parseAttributes (state: ParserState) : SmcatAttribute list =
    // LeftBracket already consumed by caller
    let attrs = ResizeArray<SmcatAttribute>()
    let mutable finished = false

    while not finished do
        let tok = peek state

        match tok.Kind with
        | RightBracket ->
            advance state |> ignore
            finished <- true
        | Eof ->
            addError state tok.Position "Unclosed attribute bracket" "']'" "end of input" "[key=value]"
            finished <- true
        | Newline ->
            // Attributes should not span lines; treat as implicit close
            finished <- true
        | Identifier key ->
            advance state |> ignore

            // Check for =
            match (peek state).Kind with
            | Equals ->
                advance state |> ignore

                // Read value
                match (peek state).Kind with
                | Identifier value ->
                    advance state |> ignore
                    attrs.Add({ Key = key; Value = value })
                | QuotedString value ->
                    advance state |> ignore
                    attrs.Add({ Key = key; Value = value })
                | _ ->
                    let t = peek state

                    addError
                        state
                        t.Position
                        "Expected attribute value"
                        "identifier or quoted string"
                        (tokenDescription t)
                        "[key=value]"
            | _ ->
                // Key without value -- treat as key with empty value
                attrs.Add({ Key = key; Value = "" })
        | QuotedString key ->
            advance state |> ignore

            match (peek state).Kind with
            | Equals ->
                advance state |> ignore

                match (peek state).Kind with
                | Identifier value ->
                    advance state |> ignore
                    attrs.Add({ Key = key; Value = value })
                | QuotedString value ->
                    advance state |> ignore
                    attrs.Add({ Key = key; Value = value })
                | _ ->
                    let t = peek state

                    addError
                        state
                        t.Position
                        "Expected attribute value"
                        "identifier or quoted string"
                        (tokenDescription t)
                        "[key=value]"
            | _ -> attrs.Add({ Key = key; Value = "" })
        | _ ->
            // Unknown token inside attributes -- skip
            advance state |> ignore

    attrs |> Seq.toList

// T014: Activity parsing

/// Parse state activities (entry/, exit/, ...) after a colon.
let private parseActivities (state: ParserState) : StateActivity =
    let mutable entry: string option = None
    let mutable exit: string option = None
    let mutable doActivity: string option = None
    let mutable cont = true

    while cont do
        let tok = peek state

        match tok.Kind with
        | EntrySlash ->
            advance state |> ignore
            // Collect text tokens until next activity keyword, terminator, or bracket
            let parts = ResizeArray<string>()

            let rec collectActivityText () =
                let next = peek state

                match next.Kind with
                | Identifier text ->
                    advance state |> ignore
                    parts.Add(text)
                    collectActivityText ()
                | QuotedString text ->
                    advance state |> ignore
                    parts.Add(text)
                    collectActivityText ()
                | _ -> () // stop: activity keyword, terminator, etc.

            collectActivityText ()

            if parts.Count > 0 then
                entry <- Some(System.String.Join(" ", parts))
        | ExitSlash ->
            advance state |> ignore
            let parts = ResizeArray<string>()

            let rec collectActivityText () =
                let next = peek state

                match next.Kind with
                | Identifier text ->
                    advance state |> ignore
                    parts.Add(text)
                    collectActivityText ()
                | QuotedString text ->
                    advance state |> ignore
                    parts.Add(text)
                    collectActivityText ()
                | _ -> ()

            collectActivityText ()

            if parts.Count > 0 then
                exit <- Some(System.String.Join(" ", parts))
        | Ellipsis ->
            advance state |> ignore
            let parts = ResizeArray<string>()

            let rec collectActivityText () =
                let next = peek state

                match next.Kind with
                | Identifier text ->
                    advance state |> ignore
                    parts.Add(text)
                    collectActivityText ()
                | QuotedString text ->
                    advance state |> ignore
                    parts.Add(text)
                    collectActivityText ()
                | _ -> ()

            collectActivityText ()

            if parts.Count > 0 then
                doActivity <- Some(System.String.Join(" ", parts))
        | _ -> cont <- false

    { Entry = entry
      Exit = exit
      Do = doActivity }

/// Read an identifier name, handling Caret and CloseBracketPrefix prefixes for pseudo-states.
let private readStateName (state: ParserState) : string option =
    let tok = peek state

    match tok.Kind with
    | Identifier name ->
        advance state |> ignore
        Some name
    | QuotedString name ->
        advance state |> ignore
        Some name
    | Caret ->
        advance state |> ignore
        // Next token should be an identifier
        match (peek state).Kind with
        | Identifier name ->
            advance state |> ignore
            Some("^" + name)
        | _ -> Some "^"
    | CloseBracketPrefix ->
        advance state |> ignore

        match (peek state).Kind with
        | Identifier name ->
            advance state |> ignore
            Some("]" + name)
        | _ -> Some "]"
    | _ -> None

/// Check if the next non-newline token is a TransitionArrow (for disambiguation).
let private isTransitionAhead (state: ParserState) : bool =
    let mutable pos = state.Position
    // Skip newlines ahead
    while pos < state.Tokens.Length && state.Tokens[pos].Kind = Newline do
        pos <- pos + 1

    pos < state.Tokens.Length && state.Tokens[pos].Kind = TransitionArrow

// T014-T016: Main parsing functions (mutually recursive)

/// Parse a complete smcat document at a given nesting depth.
let rec private parseDocument (state: ParserState) (depth: int) : SmcatDocument =
    if depth > 50 then
        addWarning
            state
            (peek state).Position
            (sprintf "Nesting depth exceeds 50 levels (depth %d)" depth)
            (Some "Consider flattening the state hierarchy")

    let elements = ResizeArray<SmcatElement>()

    let rec loop () =
        if state.ErrorLimitReached then
            ()
        else
            skipNewlines state
            let tok = peek state

            match tok.Kind with
            | Eof -> () // done at top level
            | RightBrace when depth > 0 -> () // end of nested document
            | RightBrace when depth = 0 ->
                // Unexpected closing brace at top level
                addError
                    state
                    tok.Position
                    "Unexpected '}' at top level"
                    "state name, transition, or end of input"
                    "'}'"
                    ""

                advance state |> ignore
                loop ()
            | _ ->
                parseElement state depth elements
                loop ()

    loop ()
    { Elements = elements |> Seq.toList }

/// Parse a single element (state declaration or transition).
and private parseElement (state: ParserState) (depth: int) (elements: ResizeArray<SmcatElement>) : unit =
    let tok = peek state

    match tok.Kind with
    | Identifier _
    | QuotedString _
    | Caret
    | CloseBracketPrefix ->
        let startPos = tok.Position
        let savedPosition = state.Position

        match readStateName state with
        | Some name ->
            // Disambiguate: is this a transition (=>) or a state declaration?
            if isTransitionAhead state then
                parseTransition state name startPos elements depth
            else
                parseStateDeclaration state name startPos elements depth
        | None ->
            addError
                state
                tok.Position
                "Expected state name"
                "identifier or quoted string"
                (tokenDescription tok)
                "stateName => target: event;"

            skipToTerminator state
            consumeTerminator state
    | Semicolon
    | Comma ->
        // Empty statement, skip
        advance state |> ignore
    | _ ->
        addError
            state
            tok.Position
            "Unexpected token"
            "state name, transition, or end of input"
            (tokenDescription tok)
            ""

        skipToTerminator state
        consumeTerminator state

// T015: Transition parsing

/// Parse a transition: source => target [: label] [attributes] ;
and private parseTransition
    (state: ParserState)
    (sourceName: string)
    (startPos: SourcePosition)
    (elements: ResizeArray<SmcatElement>)
    (depth: int)
    : unit =
    // Skip any newlines between source and arrow
    skipNewlines state

    // Consume the transition arrow
    match expect state TransitionArrow with
    | None ->
        let tok = peek state

        addError
            state
            tok.Position
            "Expected transition arrow"
            "'=>'"
            (tokenDescription tok)
            (sprintf "%s => target;" sourceName)

        skipToTerminator state
        consumeTerminator state
    | Some _ ->
        // Read target name
        skipNewlines state

        match readStateName state with
        | Some targetName ->
            // Check for label (colon)
            let label =
                match (peek state).Kind with
                | Colon ->
                    advance state |> ignore
                    // Collect label text from tokens until terminator or attribute bracket
                    let labelParts = ResizeArray<string>()
                    let labelStart = (peek state).Position
                    let mutable collecting = true

                    while collecting do
                        let next = peek state

                        match next.Kind with
                        | Identifier text ->
                            advance state |> ignore
                            labelParts.Add(text)
                        | QuotedString text ->
                            advance state |> ignore
                            labelParts.Add(text)
                        | LeftBracket ->
                            // This is the guard bracket inside the label
                            advance state |> ignore
                            labelParts.Add("[")
                            // Collect until RightBracket
                            let mutable inBracket = true

                            while inBracket do
                                let bt = peek state

                                match bt.Kind with
                                | RightBracket ->
                                    advance state |> ignore
                                    labelParts.Add("]")
                                    inBracket <- false
                                | Identifier text ->
                                    advance state |> ignore
                                    labelParts.Add(text)
                                | QuotedString text ->
                                    advance state |> ignore
                                    labelParts.Add(text)
                                | Eof ->
                                    inBracket <- false
                                | _ ->
                                    advance state |> ignore
                                    labelParts.Add(tokenToText bt)
                        | ForwardSlash ->
                            advance state |> ignore
                            labelParts.Add("/")
                        | _ -> collecting <- false

                    let labelText = System.String.Join(" ", labelParts)
                    let (parsedLabel, labelWarnings) = parseLabel labelText labelStart
                    state.Warnings <- state.Warnings @ labelWarnings
                    Some parsedLabel
                | _ -> None

            // Check for attributes
            let attributes =
                match (peek state).Kind with
                | LeftBracket ->
                    advance state |> ignore
                    parseAttributes state
                | _ -> []

            let transition =
                { Source = sourceName
                  Target = targetName
                  Label = label
                  Attributes = attributes
                  Position = startPos }

            elements.Add(TransitionElement transition)

            consumeTerminator state
        | None ->
            let tok = peek state

            addError
                state
                tok.Position
                "Expected target state name"
                "identifier or quoted string"
                (tokenDescription tok)
                (sprintf "%s => target;" sourceName)

            skipToTerminator state
            consumeTerminator state

/// Convert a token to its text representation (for label reconstruction).
and private tokenToText (token: Token) : string =
    match token.Kind with
    | Identifier s -> s
    | QuotedString s -> s
    | Colon -> ":"
    | Semicolon -> ";"
    | Comma -> ","
    | LeftBracket -> "["
    | RightBracket -> "]"
    | LeftBrace -> "{"
    | RightBrace -> "}"
    | ForwardSlash -> "/"
    | Equals -> "="
    | TransitionArrow -> "=>"
    | Caret -> "^"
    | CloseBracketPrefix -> "]"
    | EntrySlash -> "entry/"
    | ExitSlash -> "exit/"
    | Ellipsis -> "..."
    | Newline -> ""
    | Eof -> ""

// T014: State declaration parsing

/// Parse a state declaration: name [: activities] [attributes] [{ children }] ;
and private parseStateDeclaration
    (state: ParserState)
    (name: string)
    (startPos: SourcePosition)
    (elements: ResizeArray<SmcatElement>)
    (depth: int)
    : unit =
    // Check for colon (activities or label)
    let activities =
        match (peek state).Kind with
        | Colon ->
            advance state |> ignore
            // Check if activities follow
            let tok = peek state

            match tok.Kind with
            | EntrySlash
            | ExitSlash
            | Ellipsis ->
                let act = parseActivities state
                Some act
            | _ ->
                // Not activity tokens after colon; could be an identifier (label text)
                // For now, skip -- state declarations with colon + non-activity text
                // are uncommon; basic handling covers the spec requirements
                None
        | _ -> None

    // Check for attributes
    let attributes =
        match (peek state).Kind with
        | LeftBracket ->
            advance state |> ignore
            parseAttributes state
        | _ -> []

    // Extract label from attributes if present
    let label =
        attributes
        |> List.tryFind (fun a -> a.Key = "label")
        |> Option.map (fun a -> a.Value)

    // Check for composite children
    let children =
        match (peek state).Kind with
        | LeftBrace ->
            advance state |> ignore
            let childDoc = parseDocument state (depth + 1)
            // Consume the closing brace
            match expect state RightBrace with
            | Some _ -> ()
            | None ->
                let tok = peek state

                addError
                    state
                    tok.Position
                    "Expected closing '}' for composite state"
                    "'}'"
                    (tokenDescription tok)
                    (sprintf "%s { ... };" name)

            Some childDoc
        | _ -> None

    let stateType = inferStateType name attributes

    let smcatState =
        { Name = name
          Label = label
          StateType = stateType
          Activities = activities
          Attributes = attributes
          Children = children
          Position = startPos }

    elements.Add(StateDeclaration smcatState)

    consumeTerminator state

    // Check for comma-separated state list
    // After consumeTerminator, if the consumed token was a comma and
    // there's another identifier ahead (not a transition), parse additional states.
    // However, the comma was already consumed. Instead, we handle this via
    // the main loop -- multiple state declarations separated by commas each parse separately.

    ()

// T017: Public API

/// Parse a token list into an SmcatDocument.
let parse (tokens: Token list) (maxErrors: int) : ParseResult =
    let state = createState tokens maxErrors
    let doc = parseDocument state 0

    { Document = doc
      Errors = state.Errors
      Warnings = state.Warnings }

/// Convenience: tokenize + parse in one call.
let parseSmcat (source: string) : ParseResult =
    let tokens = tokenize source
    parse tokens 50
