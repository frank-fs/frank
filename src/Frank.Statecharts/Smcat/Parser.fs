module internal Frank.Statecharts.Smcat.Parser

open Frank.Statecharts.Ast
open Frank.Statecharts.Smcat.Types
open Frank.Statecharts.Smcat.LabelParser
open Frank.Statecharts.Smcat.Lexer

// T013: Core parser infrastructure
// T020-T023: Structured error reporting enhancements

type ParserState =
    { Tokens: Token array
      mutable Position: int
      mutable Elements: StatechartElement list
      mutable Errors: ParseFailure list
      mutable Warnings: ParseWarning list
      mutable ErrorLimitReached: bool
      mutable DeclaredStates: Set<string>
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
        let failure: ParseFailure =
            { Position = Some pos
              Description = desc
              Expected = expected
              Found = found
              CorrectiveExample = example }

        state.Errors <- state.Errors @ [ failure ]

        if state.Errors.Length >= state.MaxErrors then
            state.ErrorLimitReached <- true

let private addWarning (state: ParserState) (pos: SourcePosition) (desc: string) (suggestion: string option) : unit =
    let warning: ParseWarning =
        { Position = Some pos
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
      DeclaredStates = Set.empty
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

// T020: Error recovery -- skip to next statement boundary and consume terminator.
// Guarantees forward progress by advancing at least one token.

/// Skip to the next clean statement boundary, consuming the terminator.
/// Always advances at least one token to prevent infinite loops.
let private skipToNextStatement (state: ParserState) : unit =
    let startPos = state.Position
    let mutable found = false

    while not found && (peek state).Kind <> Eof do
        match (peek state).Kind with
        | Semicolon
        | Comma ->
            advance state |> ignore // consume the terminator
            found <- true
        | Newline ->
            advance state |> ignore // consume newline
            found <- true
        | RightBrace -> found <- true // don't consume -- let composite state handler deal with it
        | _ -> advance state |> ignore // skip unknown token

    // Guarantee forward progress: if we didn't advance at all, force-advance
    if state.Position = startPos && (peek state).Kind <> Eof then
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
/// Returns StateActivities directly (shared AST type).
let private parseActivities (state: ParserState) : StateActivities =
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

    { Entry = entry |> Option.map List.singleton |> Option.defaultValue []
      Exit = exit |> Option.map List.singleton |> Option.defaultValue []
      Do = doActivity |> Option.map List.singleton |> Option.defaultValue [] }

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

/// Convert SmcatAttribute list to Annotation list (inline from Mapper.toAnnotation).
/// Attributes with key "type" are not converted to annotations (consumed by inferStateType).
let private attributesToAnnotations (attributes: SmcatAttribute list) : Annotation list =
    attributes
    |> List.choose (fun attr ->
        match attr.Key.ToLowerInvariant() with
        | "type" -> None // consumed by inferStateType, not stored as annotation
        | "color" -> Some(SmcatAnnotation(SmcatColor attr.Value))
        | "label" -> Some(SmcatAnnotation(SmcatStateLabel attr.Value))
        | key -> Some(SmcatAnnotation(SmcatCustomAttribute(key, attr.Value))))

// T014-T016: Main parsing functions (mutually recursive)

/// Parse a complete smcat document at a given nesting depth.
let rec private parseDocument (state: ParserState) (depth: int) : StatechartDocument =
    if depth > 50 then
        addWarning
            state
            (peek state).Position
            (sprintf "Nesting depth exceeds 50 levels (depth %d)" depth)
            (Some "Consider flattening the state hierarchy")

    let elements = ResizeArray<StatechartElement>()

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

    { Title = None
      InitialStateId = None
      Elements = elements |> Seq.toList
      DataEntries = []
      Annotations = [] }

/// Parse a single element (state declaration or transition).
/// T020: Uses skipToNextStatement for error recovery to ensure forward progress.
and private parseElement (state: ParserState) (depth: int) (elements: ResizeArray<StatechartElement>) : unit =
    let tok = peek state
    let posBeforeParse = state.Position

    match tok.Kind with
    | Identifier _
    | QuotedString _
    | Caret
    | CloseBracketPrefix ->
        let startPos = tok.Position

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

            skipToNextStatement state
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
            "idle => running: start;"

        skipToNextStatement state

    // T020: Safety check -- guarantee forward progress even if no recovery occurred
    if state.Position = posBeforeParse && (peek state).Kind <> Eof then
        advance state |> ignore

// T015: Transition parsing

/// Parse a transition: source => target [: label] [attributes] ;
and private parseTransition
    (state: ParserState)
    (sourceName: string)
    (startPos: SourcePosition)
    (elements: ResizeArray<StatechartElement>)
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
            // T021: Check for "missing colon" pattern -- identifier(s) after target without colon.
            // e.g., "on => off switch flicked;" -- text without preceding colon
            let nextTok = peek state

            match nextTok.Kind with
            | Identifier _
            | QuotedString _ ->
                // There are extra tokens after the target that look like label text but no colon
                addError
                    state
                    nextTok.Position
                    "Missing colon before transition label"
                    (sprintf "':' or ';' after target state '%s'" targetName)
                    (tokenDescription nextTok)
                    (sprintf "%s => %s: eventName;" sourceName targetName)

                // Skip to terminator and emit the transition without a label
                skipToNextStatement state

                // T016: Infer transition kind for annotation
                let transitionKind =
                    if sourceName = "initial" then InitialTransition
                    elif targetName = "final" then FinalTransition
                    elif targetName = sourceName then SelfTransition
                    elif depth > 0 then InternalTransition
                    else ExternalTransition

                let edge: TransitionEdge =
                    { Source = sourceName
                      Target = Some targetName
                      Event = None
                      Guard = None
                      GuardHref = None
                      Action = None
                      Parameters = []
                      SenderRole = None
                      ReceiverRole = None
                      PayloadType = None
                      Position = Some startPos
                      Annotations = [ SmcatAnnotation(SmcatTransition transitionKind) ] }

                elements.Add(TransitionElement edge)
            | _ ->
                // Check for label (colon)
                let label =
                    match nextTok.Kind with
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
                                let bracketPos = next.Position
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
                                        // T021: Unclosed bracket in label
                                        addError
                                            state
                                            bracketPos
                                            "Unclosed bracket in transition label"
                                            "']' to close guard bracket"
                                            "end of input"
                                            (sprintf "%s => %s: event [guard];" sourceName targetName)

                                        inBracket <- false
                                    | Semicolon
                                    | Comma
                                    | Newline ->
                                        // T021: Unclosed bracket terminated by statement end
                                        addError
                                            state
                                            bracketPos
                                            "Unclosed bracket in transition label"
                                            "']' to close guard bracket"
                                            (tokenDescription bt)
                                            (sprintf "%s => %s: event [guard];" sourceName targetName)

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

                // Split label into event/guard/action components
                let (ev, gd, ac) =
                    match label with
                    | Some l -> (l.Event, l.Guard, l.Action)
                    | None -> (None, None, None)

                let attrAnnotations = attributesToAnnotations attributes

                // T016: Infer transition kind for annotation
                let transitionKind =
                    if sourceName = "initial" then InitialTransition
                    elif targetName = "final" then FinalTransition
                    elif targetName = sourceName then SelfTransition
                    elif depth > 0 then InternalTransition
                    else ExternalTransition

                let annotations =
                    attrAnnotations @ [ SmcatAnnotation(SmcatTransition transitionKind) ]

                let edge: TransitionEdge =
                    { Source = sourceName
                      Target = Some targetName
                      Event = ev
                      Guard = gd
                      GuardHref = None
                      Action = ac
                      Parameters = []
                      SenderRole = None
                      ReceiverRole = None
                      PayloadType = None
                      Position = Some startPos
                      Annotations = annotations }

                elements.Add(TransitionElement edge)

                consumeTerminator state
        | None ->
            let tok = peek state

            // T021: Missing target state -- context-aware corrective example
            addError
                state
                tok.Position
                "Expected target state name"
                "identifier or quoted string"
                (tokenDescription tok)
                (sprintf "%s => target;" sourceName)

            skipToNextStatement state

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

/// T021: Check for invalid arrow syntax (e.g., ==> instead of =>).
/// Returns true if an invalid arrow was detected and handled.
and private tryHandleInvalidArrow
    (state: ParserState)
    (name: string)
    (startPos: SourcePosition)
    (elements: ResizeArray<StatechartElement>)
    : bool =
    if (peek state).Kind = Equals then
        let eqPos = (peek state).Position
        let savedPos = state.Position
        advance state |> ignore // consume Equals

        let afterEq = (peek state).Kind

        if afterEq = TransitionArrow then
            // Pattern: name = => target  (user typed ==>)
            advance state |> ignore // consume TransitionArrow
            skipNewlines state

            addError
                state
                eqPos
                "Unrecognized arrow syntax '==>', expected '=>'"
                "'=>'"
                "'==>'"
                (sprintf "%s => target;" name)

            // Try to parse the rest as a transition (target + optional label)
            match readStateName state with
            | Some targetName ->
                // T016: Infer transition kind for annotation (error-recovery path, no depth available)
                let transitionKind =
                    if name = "initial" then InitialTransition
                    elif targetName = "final" then FinalTransition
                    elif targetName = name then SelfTransition
                    else ExternalTransition

                let edge: TransitionEdge =
                    { Source = name
                      Target = Some targetName
                      Event = None
                      Guard = None
                      GuardHref = None
                      Action = None
                      Parameters = []
                      SenderRole = None
                      ReceiverRole = None
                      PayloadType = None
                      Position = Some startPos
                      Annotations = [ SmcatAnnotation(SmcatTransition transitionKind) ] }

                elements.Add(TransitionElement edge)
                consumeTerminator state
            | None -> skipToNextStatement state

            true
        else
            // Not an invalid arrow pattern, restore position
            state.Position <- savedPos
            false
    else
        false

/// Parse a state declaration: name [: activities] [attributes] [{ children }] ;
and private parseStateDeclaration
    (state: ParserState)
    (name: string)
    (startPos: SourcePosition)
    (elements: ResizeArray<StatechartElement>)
    (depth: int)
    : unit =
    // T021: Check for invalid arrow syntax before normal state declaration parsing
    if tryHandleInvalidArrow state name startPos elements then
        () // Invalid arrow was detected and handled
    else

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
        let (childStateNodes, childOtherElements) =
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

                // Extract child state nodes and other elements (transitions)
                let childStates =
                    childDoc.Elements
                    |> List.choose (fun el ->
                        match el with
                        | StateDecl node -> Some node
                        | _ -> None)

                let otherElements =
                    childDoc.Elements
                    |> List.filter (fun el ->
                        match el with
                        | StateDecl _ -> false
                        | _ -> true)

                (childStates, otherElements)
            | _ -> ([], [])

        let stateType = inferStateType name attributes

        // T023: Warning for pseudo-state naming convention vs explicit type attribute mismatch
        let typeAttr =
            attributes
            |> List.tryFind (fun a -> a.Key.Equals("type", System.StringComparison.OrdinalIgnoreCase))

        match typeAttr with
        | Some attr ->
            let inferredFromName = inferStateType name []

            if inferredFromName <> Regular && inferredFromName <> stateType then
                addWarning
                    state
                    startPos
                    (sprintf
                        "State name '%s' matches naming convention for %A state type, but explicit attribute overrides to %s"
                        name
                        inferredFromName
                        attr.Value)
                    (Some "Consider renaming the state or removing the explicit type attribute")
        | None -> ()

        // T023: Warning for duplicate state declarations
        if state.DeclaredStates.Contains(name) then
            addWarning
                state
                startPos
                (sprintf "State '%s' declared multiple times" name)
                (Some "Combine state attributes into a single declaration")
        else
            state.DeclaredStates <- state.DeclaredStates.Add(name)

        // T014 + T015: Compute SmcatStateType annotation to track type origin (explicit vs inferred)
        let hasExplicitType =
            attributes
            |> List.exists (fun a -> a.Key.Equals("type", System.StringComparison.OrdinalIgnoreCase))

        let typeAnnotation =
            if hasExplicitType then
                [ SmcatAnnotation(SmcatStateType(stateType, Explicit)) ]
            elif stateType <> Regular then
                [ SmcatAnnotation(SmcatStateType(stateType, Inferred)) ]
            else
                []

        // Convert attributes to annotations (excluding "type" which is consumed by inferStateType)
        let annotations = typeAnnotation @ (attributesToAnnotations attributes)

        let stateNode: StateNode =
            { Identifier = Some name
              Label = label
              Kind = stateType
              Children = childStateNodes
              Activities = activities
              Position = Some startPos
              Annotations = annotations }

        elements.Add(StateDecl stateNode)

        // Add child transitions (and other non-state elements) to the parent elements list
        for el in childOtherElements do
            elements.Add(el)

        consumeTerminator state

// T017: Public API

/// Parse a token list into a StatechartDocument.
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
