module internal Frank.Statecharts.Wsd.Parser

open Frank.Statecharts.Wsd.Types

// T018: Core parser infrastructure

// WP06: Unsupported construct keywords (recognized to emit warnings, then skipped)
let private unsupportedConstructs =
    set [ "activate"; "deactivate"; "destroy"; "box"; "theme"; "skin"; "skinparam" ]

// WP06: Corrective examples catalog for common errors
let private arrowCorrectives =
    "Valid arrow forms: '->' (solid), '-->' (dashed), '->-' (solid deactivating), '-->-' (dashed deactivating)"

type ParserState =
    { Tokens: Token array
      mutable Position: int
      mutable Participants: Map<string, Participant>
      mutable Elements: DiagramElement list
      mutable Errors: ParseFailure list
      mutable Warnings: ParseWarning list
      mutable Title: string option
      mutable AutoNumber: bool
      mutable ErrorLimitReached: bool
      mutable ImplicitWarned: Set<string>
      MaxErrors: int }

let private eofToken =
    { Kind = Eof
      Position = { Line = 0; Column = 0 } }

let private peek (state: ParserState) : Token =
    if state.Position < state.Tokens.Length then
        state.Tokens.[state.Position]
    else
        eofToken

let private advance (state: ParserState) : Token =
    let token = peek state

    if state.Position < state.Tokens.Length then
        state.Position <- state.Position + 1

    token

let private skipNewlines (state: ParserState) : unit =
    while (peek state).Kind = Newline do
        state.Position <- state.Position + 1

let private skipToNewline (state: ParserState) : unit =
    while (peek state).Kind <> Newline && (peek state).Kind <> Eof do
        state.Position <- state.Position + 1
    // Consume the newline too if present
    if (peek state).Kind = Newline then
        state.Position <- state.Position + 1

let private addError
    (state: ParserState)
    (pos: SourcePosition)
    (desc: string)
    (expected: string)
    (found: string)
    (example: string)
    : unit =
    if state.Errors.Length < state.MaxErrors then
        let failure =
            { Position = pos
              Description = desc
              Expected = expected
              Found = found
              CorrectiveExample = example }

        state.Errors <- failure :: state.Errors

        // WP06: Check if error limit reached after adding
        if state.Errors.Length >= state.MaxErrors then
            let limitFailure =
                { Position = pos
                  Description = "Error limit reached; further errors suppressed"
                  Expected = ""
                  Found = ""
                  CorrectiveExample = "" }

            state.Errors <- limitFailure :: state.Errors
            state.ErrorLimitReached <- true

let private addWarning (state: ParserState) (pos: SourcePosition) (desc: string) (suggestion: string option) : unit =
    let warning =
        { Position = pos
          Description = desc
          Suggestion = suggestion }

    state.Warnings <- warning :: state.Warnings

let private tokenDescription (token: Token) : string =
    match token.Kind with
    | Identifier name -> sprintf "identifier '%s'" name
    | StringLiteral s -> sprintf "string \"%s\"" s
    | TextContent s -> sprintf "text '%s'" s
    | Newline -> "newline"
    | Eof -> "end of input"
    | Colon -> "':'"
    | SolidArrow -> "'->'"
    | DashedArrow -> "'-->'"
    | SolidDeactivate -> "'->-'"
    | DashedDeactivate -> "'-->-'"
    | TokenKind.Participant -> "'participant'"
    | TokenKind.Title -> "'title'"
    | TokenKind.AutoNumber -> "'autonumber'"
    | TokenKind.Note -> "'note'"
    | TokenKind.Over -> "'over'"
    | TokenKind.LeftOf -> "'left of'"
    | TokenKind.RightOf -> "'right of'"
    | TokenKind.As -> "'as'"
    | TokenKind.Alt -> "'alt'"
    | TokenKind.Opt -> "'opt'"
    | TokenKind.Loop -> "'loop'"
    | TokenKind.Par -> "'par'"
    | TokenKind.Break -> "'break'"
    | TokenKind.Critical -> "'critical'"
    | TokenKind.Ref -> "'ref'"
    | TokenKind.Else -> "'else'"
    | TokenKind.End -> "'end'"
    | LeftParen -> "'('"
    | RightParen -> "')'"
    | Comma -> "','"
    | LeftBracket -> "'['"
    | RightBracket -> "']'"
    | Equals -> "'='"

let private registerParticipant
    (state: ParserState)
    (name: string)
    (alias: string option)
    (explicit: bool)
    (pos: SourcePosition)
    : unit =
    match Map.tryFind name state.Participants with
    | Some existing ->
        // If upgrading from implicit to explicit, update the entry
        if explicit && not existing.Explicit then
            let updated =
                { existing with
                    Explicit = true
                    Alias = alias |> Option.orElse existing.Alias }

            state.Participants <- Map.add name updated state.Participants
    | None ->
        let participant =
            { Name = name
              Alias = alias
              Explicit = explicit
              Position = pos }

        state.Participants <- Map.add name participant state.Participants

// WP06: Emit implicit-participant warning only once per name
let private ensureParticipant (state: ParserState) (name: string) (pos: SourcePosition) : unit =
    if not (Map.containsKey name state.Participants) then
        registerParticipant state name None false pos

        if not (state.ImplicitWarned.Contains name) then
            state.ImplicitWarned <- state.ImplicitWarned.Add name

            addWarning
                state
                pos
                (sprintf "Implicit participant '%s' (no prior 'participant' declaration)" name)
                (Some(sprintf "participant %s" name))

// T019: Participant declarations
let private parseParticipant (state: ParserState) : unit =
    let startToken = advance state // consume Participant keyword

    match (peek state).Kind with
    | Identifier name
    | StringLiteral name ->
        advance state |> ignore

        let alias =
            match (peek state).Kind with
            | TokenKind.As ->
                advance state |> ignore

                match (peek state).Kind with
                | Identifier a
                | StringLiteral a ->
                    advance state |> ignore
                    Some a
                | _ ->
                    let t = peek state

                    addError
                        state
                        t.Position
                        "Expected alias name"
                        "identifier or string"
                        (tokenDescription t)
                        "participant API as \"REST API\""

                    None
            | _ -> None

        let participant =
            { Name = name
              Alias = alias
              Explicit = true
              Position = startToken.Position }

        registerParticipant state name alias true startToken.Position
        state.Elements <- ParticipantDecl participant :: state.Elements
    | _ ->
        let t = peek state
        addError state t.Position "Expected participant name" "identifier" (tokenDescription t) "participant Client"

    skipToNewline state

// T020: Message parsing
let private mapArrow (kind: TokenKind) : (ArrowStyle * Direction) option =
    match kind with
    | SolidArrow -> Some(Solid, Forward)
    | DashedArrow -> Some(Dashed, Forward)
    | SolidDeactivate -> Some(Solid, Deactivating)
    | DashedDeactivate -> Some(Dashed, Deactivating)
    | _ -> None

let private parseLabelAndParams (text: string) : (string * string list) =
    let trimmed = text.Trim()
    let parenIdx = trimmed.IndexOf('(')

    if parenIdx < 0 then
        (trimmed, [])
    else
        let label = trimmed.Substring(0, parenIdx).Trim()
        let closeIdx = trimmed.LastIndexOf(')')

        if closeIdx <= parenIdx then
            // No closing paren, treat whole thing as label
            (trimmed, [])
        else
            let inner = trimmed.Substring(parenIdx + 1, closeIdx - parenIdx - 1)

            if inner.Trim().Length = 0 then
                (label, [])
            else
                let parts = inner.Split(',') |> Array.map (fun s -> s.Trim()) |> Array.toList
                (label, parts)

let private parseMessage (state: ParserState) : unit =
    let senderToken = advance state // consume sender Identifier

    let senderName =
        match senderToken.Kind with
        | Identifier name -> name
        | _ -> ""

    let arrowToken = peek state

    match mapArrow arrowToken.Kind with
    | Some(arrowStyle, direction) ->
        advance state |> ignore // consume arrow

        match (peek state).Kind with
        | Identifier receiverName ->
            let receiverToken = advance state // consume receiver
            ensureParticipant state senderName senderToken.Position
            ensureParticipant state receiverName receiverToken.Position

            let label, parameters =
                match (peek state).Kind with
                | Colon ->
                    advance state |> ignore // consume colon

                    match (peek state).Kind with
                    | TextContent text ->
                        advance state |> ignore
                        parseLabelAndParams text
                    | _ -> ("", [])
                | _ -> ("", [])

            let message =
                { Sender = senderName
                  Receiver = receiverName
                  ArrowStyle = arrowStyle
                  Direction = direction
                  Label = label
                  Parameters = parameters
                  Position = senderToken.Position }

            state.Elements <- MessageElement message :: state.Elements
            skipToNewline state
        | _ ->
            let t = peek state

            addError
                state
                t.Position
                "Expected receiver after arrow"
                "identifier"
                (tokenDescription t)
                "Client->Server: message"

            skipToNewline state
    | None ->
        // WP06: Check if identifier is an unsupported construct
        match senderToken.Kind with
        | Identifier name when unsupportedConstructs.Contains(name.ToLowerInvariant()) ->
            addWarning
                state
                senderToken.Position
                (sprintf "Unsupported construct '%s' (ignored)" name)
                (Some(sprintf "Remove '%s' or use a supported construct" name))

            skipToNewline state
        | _ ->
            // Not a message -- bare identifier or unrecognized arrow
            addError
                state
                senderToken.Position
                "Unexpected identifier"
                "message arrow (->), participant, note, or directive"
                (tokenDescription senderToken)
                arrowCorrectives

            skipToNewline state

// T021: Directive parsing
let private parseTitleDirective (state: ParserState) : unit =
    let startToken = advance state // consume Title keyword

    let titleText =
        match (peek state).Kind with
        | Colon ->
            advance state |> ignore // consume colon

            match (peek state).Kind with
            | TextContent text ->
                advance state |> ignore
                text.Trim()
            | _ -> ""
        | TextContent text ->
            advance state |> ignore
            text.Trim()
        | _ ->
            // Collect identifier tokens until newline (e.g., "title My Diagram")
            let parts = ResizeArray<string>()

            let rec collect () =
                match (peek state).Kind with
                | Identifier word ->
                    advance state |> ignore
                    parts.Add(word)
                    collect ()
                | _ -> ()

            collect ()
            System.String.Join(" ", parts)

    if state.Title.IsSome then
        addWarning state startToken.Position "Duplicate title directive" (Some "Remove the duplicate title")

    state.Title <- Some titleText
    state.Elements <- TitleDirective(titleText, startToken.Position) :: state.Elements
    skipToNewline state

let private parseAutoNumberDirective (state: ParserState) : unit =
    let startToken = advance state // consume AutoNumber keyword
    state.AutoNumber <- true
    state.Elements <- AutoNumberDirective startToken.Position :: state.Elements
    skipToNewline state

// T022: Note parsing with WP06 guard parser integration
let private parseNote (state: ParserState) : unit =
    let startToken = advance state // consume Note keyword

    let notePos =
        match (peek state).Kind with
        | TokenKind.Over ->
            advance state |> ignore
            Some NotePosition.Over
        | TokenKind.LeftOf ->
            advance state |> ignore
            Some NotePosition.LeftOf
        | TokenKind.RightOf ->
            advance state |> ignore
            Some NotePosition.RightOf
        | _ ->
            let t = peek state

            addError
                state
                t.Position
                "Expected note position"
                "'over', 'left of', or 'right of'"
                (tokenDescription t)
                "note over Client: text"

            None

    match notePos with
    | Some position ->
        match (peek state).Kind with
        | Identifier target ->
            advance state |> ignore
            ensureParticipant state target startToken.Position

            let content =
                match (peek state).Kind with
                | Colon ->
                    advance state |> ignore

                    match (peek state).Kind with
                    | TextContent text ->
                        advance state |> ignore
                        text.Trim()
                    | _ -> ""
                | _ ->
                    let t = peek state

                    addError
                        state
                        t.Position
                        "Expected ':' after participant in note"
                        "':'"
                        (tokenDescription t)
                        "note over Client: text"

                    ""

            // WP06: Guard parser integration — parse guard annotation from note content
            let (guard, remainingContent, guardErrors, guardWarnings) =
                GuardParser.tryParseGuard content startToken.Position

            // Merge guard parser errors/warnings into parser state
            for ge in guardErrors do
                if not state.ErrorLimitReached then
                    state.Errors <- ge :: state.Errors

                    if state.Errors.Length >= state.MaxErrors then
                        let limitFailure =
                            { Position = ge.Position
                              Description = "Error limit reached; further errors suppressed"
                              Expected = ""
                              Found = ""
                              CorrectiveExample = "" }

                        state.Errors <- limitFailure :: state.Errors
                        state.ErrorLimitReached <- true

            for gw in guardWarnings do
                state.Warnings <- gw :: state.Warnings

            let finalContent =
                if guard.IsSome && remainingContent.Length > 0 then
                    remainingContent
                elif guard.IsSome then
                    ""
                else
                    content

            let note =
                { NotePosition = position
                  Target = target
                  Content = finalContent
                  Guard = guard
                  Position = startToken.Position }

            state.Elements <- NoteElement note :: state.Elements
        | _ ->
            let t = peek state

            addError
                state
                t.Position
                "Expected participant name after note position"
                "identifier"
                (tokenDescription t)
                "note over Client: text"
    | None -> ()

    skipToNewline state

// WP05: Map group keyword token to GroupKind
let private mapGroupKind (kind: TokenKind) : GroupKind option =
    match kind with
    | TokenKind.Alt -> Some GroupKind.Alt
    | TokenKind.Opt -> Some GroupKind.Opt
    | TokenKind.Loop -> Some GroupKind.Loop
    | TokenKind.Par -> Some GroupKind.Par
    | TokenKind.Break -> Some GroupKind.Break
    | TokenKind.Critical -> Some GroupKind.Critical
    | TokenKind.Ref -> Some GroupKind.Ref
    | _ -> None

// WP05: Parse condition text after group/else keyword (TextContent until Newline)
let private parseConditionText (state: ParserState) : string option =
    match (peek state).Kind with
    | TextContent text ->
        advance state |> ignore
        let trimmed = text.Trim()
        if trimmed.Length > 0 then Some trimmed else None
    | Identifier text ->
        advance state |> ignore
        // Collect remaining identifiers/text on the same line
        let parts = ResizeArray<string>()
        parts.Add(text)

        let rec collect () =
            match (peek state).Kind with
            | Identifier word ->
                advance state |> ignore
                parts.Add(word)
                collect ()
            | TextContent word ->
                advance state |> ignore
                parts.Add(word)
            | _ -> ()

        collect ()
        Some(System.String.Join(" ", parts))
    | _ -> None

// WP06: Skip tokens until a matching 'end' for group error recovery
let private skipToMatchingEnd (state: ParserState) : unit =
    let mutable depth = 1

    while depth > 0 && (peek state).Kind <> Eof do
        let token = peek state

        match token.Kind with
        | TokenKind.Alt
        | TokenKind.Opt
        | TokenKind.Loop
        | TokenKind.Par
        | TokenKind.Break
        | TokenKind.Critical
        | TokenKind.Ref ->
            depth <- depth + 1
            advance state |> ignore
        | TokenKind.End ->
            depth <- depth - 1
            advance state |> ignore
        | _ -> advance state |> ignore

// WP05: Group parsing with full branch support and recursive nesting
// parseGroup and parseElements are mutually recursive
let rec private parseGroup (state: ParserState) (depth: int) : unit =
    let startToken = advance state // consume group keyword

    let groupKind =
        match mapGroupKind startToken.Kind with
        | Some kind -> kind
        | None -> failwithf "Internal error: parseGroup called with non-group token %A" startToken.Kind

    if depth > 50 then
        addWarning
            state
            startToken.Position
            (sprintf "Deeply nested grouping block (depth %d)" depth)
            (Some "Consider simplifying the diagram to reduce nesting depth")

    // Parse condition text
    let condition = parseConditionText state

    // Consume newline after group keyword line
    if (peek state).Kind = Newline then
        state.Position <- state.Position + 1

    // Parse branches: initial branch + else branches
    let branches = ResizeArray<GroupBranch>()

    // Parse initial branch body
    let initialElements = parseBranchBody state depth

    branches.Add(
        { Condition = condition
          Elements = initialElements }
    )

    // Parse else branches
    let mutable finished = false

    while not finished do
        skipNewlines state
        let current = peek state

        match current.Kind with
        | TokenKind.Else ->
            advance state |> ignore // consume Else
            let elseCondition = parseConditionText state

            if (peek state).Kind = Newline then
                state.Position <- state.Position + 1

            let elseElements = parseBranchBody state depth

            branches.Add(
                { Condition = elseCondition
                  Elements = elseElements }
            )
        | TokenKind.End ->
            advance state |> ignore // consume End
            skipToNewline state
            finished <- true
        | Eof ->
            addError
                state
                startToken.Position
                (sprintf
                    "Unclosed grouping block '%s' starting at line %d"
                    (startToken.Kind.ToString().ToLowerInvariant())
                    startToken.Position.Line)
                "'end'"
                "end of input"
                (sprintf "%s condition\n  ...\nend" (startToken.Kind.ToString().ToLowerInvariant()))

            finished <- true
        | _ ->
            // Unexpected token — this shouldn't happen but handle gracefully
            addError
                state
                current.Position
                "Unexpected token in grouping block"
                "'else' or 'end'"
                (tokenDescription current)
                ""

            skipToNewline state
            finished <- true

    let group =
        { Kind = groupKind
          Branches = branches |> Seq.toList
          Position = startToken.Position }

    state.Elements <- GroupElement group :: state.Elements

// WP05: Parse branch body elements, isolating from parent state.Elements
and private parseBranchBody (state: ParserState) (depth: int) : DiagramElement list =
    // Save parent elements
    let savedElements = state.Elements
    state.Elements <- []

    // Parse elements until Else, End, or Eof
    parseBranchElements state depth

    // Collect branch elements (they were prepended, so reverse)
    let branchElements = List.rev state.Elements

    // Restore parent elements
    state.Elements <- savedElements
    branchElements

// WP05: Parse elements within a branch body — stops at Else, End, or Eof
// WP06: Checks ErrorLimitReached to stop parsing on error limit
and private parseBranchElements (state: ParserState) (depth: int) : unit =
    if state.ErrorLimitReached then
        ()
    else

        skipNewlines state
        let token = peek state

        match token.Kind with
        | Eof -> ()
        | TokenKind.Else -> () // branch terminator — let caller handle
        | TokenKind.End -> () // block terminator — let caller handle
        | TokenKind.Participant ->
            parseParticipant state
            parseBranchElements state depth
        | TokenKind.Title ->
            parseTitleDirective state
            parseBranchElements state depth
        | TokenKind.AutoNumber ->
            parseAutoNumberDirective state
            parseBranchElements state depth
        | TokenKind.Note ->
            parseNote state
            parseBranchElements state depth
        | TokenKind.Alt
        | TokenKind.Opt
        | TokenKind.Loop
        | TokenKind.Par
        | TokenKind.Break
        | TokenKind.Critical
        | TokenKind.Ref ->
            parseGroup state (depth + 1)
            parseBranchElements state depth
        | Identifier _ ->
            parseMessage state
            parseBranchElements state depth
        | _ ->
            addError
                state
                token.Position
                "Unexpected token"
                "participant, message, note, or directive"
                (tokenDescription token)
                ""

            skipToNewline state
            parseBranchElements state depth

// Main parse loop
// WP06: Checks ErrorLimitReached to stop parsing on error limit
and private parseElements (state: ParserState) : unit =
    if state.ErrorLimitReached then
        ()
    else

        skipNewlines state
        let token = peek state

        match token.Kind with
        | Eof -> ()
        | TokenKind.Participant ->
            parseParticipant state
            parseElements state
        | TokenKind.Title ->
            parseTitleDirective state
            parseElements state
        | TokenKind.AutoNumber ->
            parseAutoNumberDirective state
            parseElements state
        | TokenKind.Note ->
            parseNote state
            parseElements state
        | TokenKind.Alt
        | TokenKind.Opt
        | TokenKind.Loop
        | TokenKind.Par
        | TokenKind.Break
        | TokenKind.Critical
        | TokenKind.Ref ->
            parseGroup state 0
            parseElements state
        | Identifier _ ->
            parseMessage state
            parseElements state
        | TokenKind.End ->
            addError
                state
                token.Position
                "'end' without matching grouping block"
                "participant, message, note, or directive"
                (tokenDescription token)
                ""

            skipToNewline state
            parseElements state
        | TokenKind.Else ->
            addError
                state
                token.Position
                "'else' without matching grouping block"
                "participant, message, note, or directive"
                (tokenDescription token)
                ""

            skipToNewline state
            parseElements state
        | _ ->
            addError
                state
                token.Position
                "Unexpected token"
                "participant, message, note, or directive"
                (tokenDescription token)
                ""

            skipToNewline state
            parseElements state

// Top-level API
let parse (tokens: Token list) (maxErrors: int) : ParseResult =
    let tokenArray = List.toArray tokens

    let state =
        { Tokens = tokenArray
          Position = 0
          Participants = Map.empty
          Elements = []
          Errors = []
          Warnings = []
          Title = None
          AutoNumber = false
          ErrorLimitReached = false
          ImplicitWarned = Set.empty
          MaxErrors = maxErrors }

    parseElements state

    // Build participants list ordered by first appearance
    let participants =
        state.Participants
        |> Map.toList
        |> List.map snd
        |> List.sortBy (fun p -> p.Position.Line, p.Position.Column)

    { Diagram =
        { Title = state.Title
          AutoNumber = state.AutoNumber
          Participants = participants
          Elements = List.rev state.Elements }
      Errors = List.rev state.Errors
      Warnings = List.rev state.Warnings }

let parseWsd (source: string) : ParseResult =
    let tokens = Lexer.tokenize source
    parse tokens 50
