module internal Frank.Statecharts.Wsd.Serializer

open Frank.Statecharts.Ast

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

/// Extract WsdNotePosition from annotations, defaulting to Over.
let private extractNotePosition (annotations: Annotation list) : WsdNotePosition =
    annotations
    |> List.tryPick (function
        | WsdAnnotation(WsdNotePosition pos) -> Some pos
        | _ -> None)
    |> Option.defaultValue Over

/// Serialize WSD note position to text.
let private notePositionString (pos: WsdNotePosition) : string =
    match pos with
    | Over -> "over"
    | LeftOf -> "left of"
    | RightOf -> "right of"

/// Extract WsdGuardData pairs from annotations.
let private extractGuardPairs (annotations: Annotation list) : (string * string) list option =
    annotations
    |> List.tryPick (function
        | WsdAnnotation(WsdGuardData pairs) -> Some pairs
        | _ -> None)

/// Extract TransitionStyle from annotations, defaulting to Solid Forward.
let private extractTransitionStyle (annotations: Annotation list) : TransitionStyle =
    annotations
    |> List.tryPick (function
        | WsdAnnotation(WsdTransitionStyle style) -> Some style
        | _ -> None)
    |> Option.defaultValue { ArrowStyle = Solid; Direction = Forward }

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

/// Serialize a list of statechart elements to a StringBuilder.
let rec private serializeElements (sb: System.Text.StringBuilder) (elements: StatechartElement list) : unit =
    for element in elements do
        match element with
        | StateDecl _ -> ()
        | DirectiveElement(TitleDirective _) -> ()
        | DirectiveElement(AutoNumberDirective _) ->
            sb.Append("autonumber\n") |> ignore
        | TransitionElement t ->
            let style = extractTransitionStyle t.Annotations
            sb.Append(quoteName t.Source) |> ignore
            sb.Append(arrowString style.ArrowStyle style.Direction) |> ignore

            match t.Target with
            | Some target -> sb.Append(quoteName target) |> ignore
            | None -> ()

            let label = t.Event |> Option.defaultValue ""

            if label.Length > 0 || t.Parameters.Length > 0 then
                sb.Append(": ") |> ignore
                sb.Append(label) |> ignore

                if t.Parameters.Length > 0 then
                    sb.Append("(") |> ignore
                    sb.Append(System.String.Join(", ", t.Parameters)) |> ignore
                    sb.Append(")") |> ignore

            sb.Append("\n") |> ignore
        | NoteElement n ->
            let notePos = extractNotePosition n.Annotations
            sb.Append("note ") |> ignore
            sb.Append(notePositionString notePos) |> ignore
            sb.Append(" ") |> ignore
            sb.Append(quoteName n.Target) |> ignore
            sb.Append(": ") |> ignore

            match extractGuardPairs n.Annotations with
            | Some pairs when pairs.Length > 0 ->
                sb.Append("[guard: ") |> ignore

                let pairStrings =
                    pairs
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

/// Serialize a StatechartDocument AST to WSD text with Unix line endings.
let serialize (document: StatechartDocument) : string =
    let sb = System.Text.StringBuilder()

    // Title
    match document.Title with
    | Some title ->
        sb.Append("title ") |> ignore
        sb.Append(title) |> ignore
        sb.Append("\n") |> ignore
    | None -> ()

    // AutoNumber (from directive elements)
    let hasAutoNumber =
        document.Elements
        |> List.exists (function
            | DirectiveElement(AutoNumberDirective _) -> true
            | _ -> false)

    if hasAutoNumber then
        sb.Append("autonumber\n") |> ignore

    // State declarations (participant declarations in WSD)
    let hasStates =
        document.Elements
        |> List.exists (function
            | StateDecl _ -> true
            | _ -> false)

    for element in document.Elements do
        match element with
        | StateDecl s ->
            sb.Append("participant ") |> ignore
            sb.Append(quoteName s.Identifier) |> ignore

            match s.Label with
            | Some alias ->
                sb.Append(" as ") |> ignore
                sb.Append(quoteName alias) |> ignore
            | None -> ()

            sb.Append("\n") |> ignore
        | _ -> ()

    // Blank line after state declarations (or title/autonumber) before body elements
    let hasBodyElements =
        document.Elements
        |> List.exists (function
            | StateDecl _ -> false
            | DirectiveElement(TitleDirective _) -> false
            | DirectiveElement(AutoNumberDirective _) -> false
            | _ -> true)

    if (hasStates || document.Title.IsSome || hasAutoNumber) && hasBodyElements then
        sb.Append("\n") |> ignore

    // Body elements (skip state declarations, title directives, autonumber directives -- already handled)
    serializeElements sb document.Elements

    sb.ToString()
