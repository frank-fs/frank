module internal Frank.Statecharts.Smcat.Serializer

open Frank.Statecharts.Ast

// ---------------------------------------------------------------------------
// Helpers: quoting
// ---------------------------------------------------------------------------

/// Determines whether an smcat name requires quoting.
/// smcat allows alphanumeric, underscore, dot, and hyphen without quoting.
let private needsQuoting (name: string) =
    name
    |> Seq.exists (fun c ->
        not (System.Char.IsLetterOrDigit c || c = '_' || c = '.' || c = '-'))

/// Wraps a name in double quotes if it contains special characters,
/// escaping any embedded double quotes.
let private quoteName (name: string) =
    if needsQuoting name then
        sprintf "\"%s\"" (name.Replace("\"", "\\\""))
    else
        name

// ---------------------------------------------------------------------------
// Helpers: annotation extraction
// ---------------------------------------------------------------------------

/// Extract color attribute from SmcatAnnotation entries.
let private extractColor (annotations: Annotation list) : string option =
    annotations
    |> List.tryPick (function
        | SmcatAnnotation(SmcatColor c) -> Some c
        | _ -> None)

/// Extract label attribute from SmcatAnnotation entries.
let private extractLabel (annotations: Annotation list) : string option =
    annotations
    |> List.tryPick (function
        | SmcatAnnotation(SmcatStateLabel l) -> Some l
        | _ -> None)

/// Extract non-standard attributes from SmcatAnnotation entries.
let private extractCustomAttributes (annotations: Annotation list) : (string * string) list =
    annotations
    |> List.choose (function
        | SmcatAnnotation(SmcatActivity(kind, body)) -> Some (kind, body)
        | _ -> None)

// ---------------------------------------------------------------------------
// Helpers: transition label formatting
// ---------------------------------------------------------------------------

/// Format a transition label from optional event, guard, and action components.
/// Returns None if all components are absent (no label needed).
/// Format: "event [guard] / action" with absent components omitted.
let private formatLabel (event: string option) (guard: string option) (action: string option) : string option =
    match event, guard, action with
    | None, None, None -> None
    | Some e, None, None -> Some e
    | Some e, Some g, None -> Some(sprintf "%s [%s]" e g)
    | Some e, None, Some a -> Some(sprintf "%s / %s" e a)
    | Some e, Some g, Some a -> Some(sprintf "%s [%s] / %s" e g a)
    | None, Some g, None -> Some(sprintf "[%s]" g)
    | None, Some g, Some a -> Some(sprintf "[%s] / %s" g a)
    | None, None, Some a -> Some(sprintf "/ %s" a)

// ---------------------------------------------------------------------------
// Helpers: attribute serialization
// ---------------------------------------------------------------------------

let private serializeAttributes (annotations: Annotation list) : string =
    let parts = ResizeArray<string>()

    // Color
    match extractColor annotations with
    | Some c -> parts.Add(sprintf "color=\"%s\"" c)
    | None -> ()

    // Label
    match extractLabel annotations with
    | Some l -> parts.Add(sprintf "label=\"%s\"" l)
    | None -> ()

    // Custom attributes
    for (key, value) in extractCustomAttributes annotations do
        parts.Add(sprintf "%s=\"%s\"" key value)

    if parts.Count > 0 then
        sprintf " [%s]" (System.String.Join(" ", parts))
    else
        ""

// ---------------------------------------------------------------------------
// Helpers: activity serialization
// ---------------------------------------------------------------------------

let private serializeActivities (activities: StateActivities option) : string =
    match activities with
    | None -> ""
    | Some a ->
        let parts = ResizeArray<string>()
        for entry in a.Entry do
            parts.Add(sprintf "entry/ %s" entry)
        for exit in a.Exit do
            parts.Add(sprintf "exit/ %s" exit)
        for doAct in a.Do do
            parts.Add(sprintf "...%s" doAct)
        if parts.Count > 0 then
            sprintf ": %s" (System.String.Join(" ", parts))
        else
            ""

// ---------------------------------------------------------------------------
// Main serialization (recursive for composite states)
// ---------------------------------------------------------------------------

/// Serialize a single state node (handles composites recursively).
let rec private serializeState (sb: System.Text.StringBuilder) (indent: string) (node: StateNode) (siblingTransitions: TransitionEdge list) : unit =
    sb.Append(indent) |> ignore
    sb.Append(quoteName (node.Identifier |> Option.defaultValue "")) |> ignore
    sb.Append(serializeActivities node.Activities) |> ignore
    sb.Append(serializeAttributes node.Annotations) |> ignore

    match node.Children with
    | [] -> sb.Append(";\n") |> ignore
    | children ->
        sb.Append(" {\n") |> ignore
        let childNames = children |> List.choose (fun c -> c.Identifier) |> Set.ofList
        let innerIndent = indent + "  "
        let childTransitions =
            siblingTransitions
            |> List.filter (fun t ->
                childNames.Contains(t.Source) ||
                (t.Target |> Option.map childNames.Contains |> Option.defaultValue false))
        for child in children do
            serializeState sb innerIndent child childTransitions
        for t in childTransitions do
            serializeTransition sb innerIndent t
        sb.Append(indent) |> ignore
        sb.Append("};\n") |> ignore

and private serializeTransition (sb: System.Text.StringBuilder) (indent: string) (t: TransitionEdge) : unit =
    sb.Append(indent) |> ignore
    sb.Append(quoteName t.Source) |> ignore
    sb.Append(" => ") |> ignore
    match t.Target with
    | Some target -> sb.Append(quoteName target) |> ignore
    | None -> ()
    match formatLabel t.Event t.Guard t.Action with
    | Some l ->
        sb.Append(": ") |> ignore
        sb.Append(l) |> ignore
    | None -> ()
    sb.Append(";\n") |> ignore

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/// Serialize a StatechartDocument to valid smcat text.
let serialize (document: StatechartDocument) : string =
    let sb = System.Text.StringBuilder()
    let allStates =
        document.Elements
        |> List.choose (function StateDecl s -> Some s | _ -> None)
    let allTransitions =
        document.Elements
        |> List.choose (function TransitionElement t -> Some t | _ -> None)

    // Collect all child state names (to avoid double-emitting transitions)
    let rec collectChildNames (nodes: StateNode list) =
        nodes |> List.collect (fun n -> (n.Identifier |> Option.toList) @ collectChildNames n.Children)
    let childNames =
        allStates
        |> List.collect (fun s -> collectChildNames s.Children)
        |> Set.ofList

    // Emit states (with composite blocks)
    for s in allStates do
        serializeState sb "" s allTransitions

    // Emit top-level transitions (those not inside composite blocks)
    for t in allTransitions do
        let isChild =
            childNames.Contains(t.Source) ||
            (t.Target |> Option.map childNames.Contains |> Option.defaultValue false)
        if not isChild then
            serializeTransition sb "" t

    // Trim trailing newline for clean output
    let result = sb.ToString().TrimEnd('\n')
    result
