module internal Frank.Statecharts.Wsd.Serializer

open Frank.Statecharts.Wsd.Types

/// Check if a participant name requires quoting in WSD output.
/// Returns true if the name contains any character that is NOT alphanumeric, underscore, or hyphen.
let needsQuoting (name: string) : bool =
    if System.String.IsNullOrEmpty(name) then
        false
    else
        name
        |> Seq.exists (fun c -> not (System.Char.IsLetterOrDigit(c) || c = '_' || c = '-'))

/// Quote a participant name if it contains non-identifier characters.
/// Escapes internal double quotes with backslash.
let quoteName (name: string) : string =
    if needsQuoting name then
        sprintf "\"%s\"" (name.Replace("\"", "\\\""))
    else
        name

/// Serialize arrow style and direction to WSD arrow syntax.
let private arrowString (style: ArrowStyle) (direction: Direction) : string =
    match style, direction with
    | Solid, Forward -> "->"
    | Dashed, Forward -> "-->"
    | Solid, Deactivating -> "->-"
    | Dashed, Deactivating -> "-->-"

/// Serialize note position to WSD syntax.
let private notePositionString (pos: NotePosition) : string =
    match pos with
    | NotePosition.Over -> "over"
    | NotePosition.LeftOf -> "left of"
    | NotePosition.RightOf -> "right of"

/// Serialize group kind to WSD keyword.
let private groupKindString (kind: GroupKind) : string =
    match kind with
    | GroupKind.Alt -> "alt"
    | GroupKind.Opt -> "opt"
    | GroupKind.Loop -> "loop"
    | GroupKind.Par -> "par"
    | GroupKind.Break -> "break"
    | GroupKind.Critical -> "critical"
    | GroupKind.Ref -> "ref"

/// Serialize a list of diagram elements to a StringBuilder.
let rec private serializeElements (sb: System.Text.StringBuilder) (elements: DiagramElement list) : unit =
    for element in elements do
        match element with
        | ParticipantDecl _ -> ()
        | TitleDirective _ -> ()
        | AutoNumberDirective _ ->
            sb.Append("autonumber\n") |> ignore
        | MessageElement m ->
            sb.Append(quoteName m.Sender) |> ignore
            sb.Append(arrowString m.ArrowStyle m.Direction) |> ignore
            sb.Append(quoteName m.Receiver) |> ignore

            if m.Label.Length > 0 || m.Parameters.Length > 0 then
                sb.Append(": ") |> ignore
                sb.Append(m.Label) |> ignore

                if m.Parameters.Length > 0 then
                    sb.Append("(") |> ignore
                    sb.Append(System.String.Join(", ", m.Parameters)) |> ignore
                    sb.Append(")") |> ignore

            sb.Append("\n") |> ignore
        | NoteElement n ->
            sb.Append("note ") |> ignore
            sb.Append(notePositionString n.NotePosition) |> ignore
            sb.Append(" ") |> ignore
            sb.Append(quoteName n.Target) |> ignore
            sb.Append(": ") |> ignore

            match n.Guard with
            | Some guard when guard.Pairs.Length > 0 ->
                sb.Append("[guard: ") |> ignore

                let pairStrings =
                    guard.Pairs
                    |> List.map (fun (k, v) -> sprintf "%s=%s" k v)

                sb.Append(System.String.Join(", ", pairStrings)) |> ignore
                sb.Append("]") |> ignore

                if n.Content.Length > 0 then
                    sb.Append(" ") |> ignore
                    sb.Append(n.Content) |> ignore
            | _ ->
                sb.Append(n.Content) |> ignore

            sb.Append("\n") |> ignore
        | GroupElement g ->
            let keyword = groupKindString g.Kind

            match g.Branches with
            | [] -> ()
            | firstBranch :: restBranches ->
                sb.Append(keyword) |> ignore

                match firstBranch.Condition with
                | Some cond ->
                    sb.Append(" ") |> ignore
                    sb.Append(cond) |> ignore
                | None -> ()

                sb.Append("\n") |> ignore
                serializeElements sb firstBranch.Elements

                for branch in restBranches do
                    sb.Append("else") |> ignore

                    match branch.Condition with
                    | Some cond ->
                        sb.Append(" ") |> ignore
                        sb.Append(cond) |> ignore
                    | None -> ()

                    sb.Append("\n") |> ignore
                    serializeElements sb branch.Elements

                sb.Append("end\n") |> ignore

/// Serialize a Diagram AST to WSD text with Unix line endings.
let serialize (diagram: Diagram) : string =
    let sb = System.Text.StringBuilder()

    // Title
    match diagram.Title with
    | Some title ->
        sb.Append("title ") |> ignore
        sb.Append(title) |> ignore
        sb.Append("\n") |> ignore
    | None -> ()

    // AutoNumber
    if diagram.AutoNumber then
        sb.Append("autonumber\n") |> ignore

    // Participant declarations from Elements
    let hasParticipants =
        diagram.Elements
        |> List.exists (function
            | ParticipantDecl _ -> true
            | _ -> false)

    for element in diagram.Elements do
        match element with
        | ParticipantDecl p ->
            sb.Append("participant ") |> ignore
            sb.Append(quoteName p.Name) |> ignore

            match p.Alias with
            | Some alias ->
                sb.Append(" as ") |> ignore
                sb.Append(quoteName alias) |> ignore
            | None -> ()

            sb.Append("\n") |> ignore
        | _ -> ()

    // Blank line after participant declarations (or title/autonumber) before body elements
    let hasBodyElements =
        diagram.Elements
        |> List.exists (function
            | ParticipantDecl _ -> false
            | TitleDirective _ -> false
            | AutoNumberDirective _ -> false
            | _ -> true)

    if (hasParticipants || diagram.Title.IsSome || diagram.AutoNumber) && hasBodyElements then
        sb.Append("\n") |> ignore

    // Body elements (skip participants, title directives, autonumber directives -- already handled)
    serializeElements sb diagram.Elements

    sb.ToString()
