module internal Frank.Statecharts.Scxml.Generator

open System.Xml.Linq
open Frank.Statecharts.Ast

let private scxmlNs = XNamespace.Get("http://www.w3.org/2005/07/scxml")

let private buildTransitionMap (doc: StatechartDocument) : Map<string, TransitionEdge list> =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | TransitionElement t -> Some t
        | _ -> None)
    |> List.groupBy (fun t -> t.Source)
    |> Map.ofList

let private generateTransition (t: TransitionEdge) : XElement =
    let el = XElement(scxmlNs + "transition")

    t.Event |> Option.iter (fun ev -> el.SetAttributeValue(XName.Get "event", ev))

    t.Guard |> Option.iter (fun g -> el.SetAttributeValue(XName.Get "cond", g))

    let targets =
        t.Annotations
        |> List.tryPick (fun a ->
            match a with
            | ScxmlAnnotation(ScxmlMultiTarget(targets)) -> Some targets
            | _ -> None)
        |> Option.defaultWith (fun () ->
            t.Target |> Option.toList)

    match targets with
    | [] -> ()
    | targets -> el.SetAttributeValue(XName.Get "target", System.String.Join(" ", targets))

    t.Annotations
    |> List.tryPick (fun a ->
        match a with
        | ScxmlAnnotation(ScxmlTransitionType(isInternal)) -> Some isInternal
        | _ -> None)
    |> Option.iter (fun isInternal ->
        if isInternal then
            el.SetAttributeValue(XName.Get "type", "internal"))

    el

let private generateHistory (h: StateNode) : XElement =
    let el = XElement(scxmlNs + "history")

    let historyMeta =
        h.Annotations
        |> List.tryPick (fun a ->
            match a with
            | ScxmlAnnotation(ScxmlHistory(id, kind, defaultTarget)) -> Some(id, kind, defaultTarget)
            | _ -> None)

    match historyMeta with
    | Some(id, kind, defaultTarget) ->
        el.SetAttributeValue(XName.Get "id", id)
        let typeStr = match kind with | Deep -> "deep" | Shallow -> "shallow"
        el.SetAttributeValue(XName.Get "type", typeStr)
        defaultTarget |> Option.iter (fun target ->
            let t = XElement(scxmlNs + "transition")
            t.SetAttributeValue(XName.Get "target", target)
            el.Add(t))
    | None ->
        // Fallback: use StateNode fields directly
        h.Identifier |> Option.iter (fun id ->
            el.SetAttributeValue(XName.Get "id", id))
        let typeStr = match h.Kind with | DeepHistory -> "deep" | _ -> "shallow"
        el.SetAttributeValue(XName.Get "type", typeStr)

    el

let rec private generateState
    (transitionsBySource: Map<string, TransitionEdge list>)
    (state: StateNode)
    : XElement option =

    let elementNameOpt =
        match state.Kind with
        | Final -> Some "final"
        | Parallel -> Some "parallel"
        | Regular | Initial -> Some "state"
        | ShallowHistory | DeepHistory -> None  // handled separately by generateHistory
        | Choice | ForkJoin | Terminate -> None  // no SCXML equivalent, skip

    match elementNameOpt with
    | None -> None
    | Some elementName ->

    let el = XElement(scxmlNs + elementName)
    state.Identifier |> Option.iter (fun id ->
        el.SetAttributeValue(XName.Get "id", id))

    // Extract ScxmlInitial annotation for the initial attribute
    state.Annotations
    |> List.tryPick (fun a ->
        match a with
        | ScxmlAnnotation(ScxmlInitial(id)) -> Some id
        | _ -> None)
    |> Option.iter (fun id -> el.SetAttributeValue(XName.Get "initial", id))

    // Separate children into history nodes and regular nodes
    let historyChildren, regularChildren =
        state.Children
        |> List.partition (fun c ->
            match c.Kind with
            | ShallowHistory | DeepHistory -> true
            | _ -> false)

    // Generate history elements
    for h in historyChildren do
        el.Add(generateHistory h)

    // Generate transitions for this state
    let ownTransitions =
        state.Identifier
        |> Option.bind (fun id -> Map.tryFind id transitionsBySource)
        |> Option.defaultValue []
    for t in ownTransitions do
        el.Add(generateTransition t)

    // Generate invoke elements from ScxmlAnnotation(ScxmlInvoke(...))
    state.Annotations
    |> List.iter (fun a ->
        match a with
        | ScxmlAnnotation(ScxmlInvoke(invokeType, src, id)) ->
            let inv = XElement(scxmlNs + "invoke")
            if invokeType <> "" then inv.SetAttributeValue(XName.Get "type", invokeType)
            src |> Option.iter (fun s -> inv.SetAttributeValue(XName.Get "src", s))
            id |> Option.iter (fun i -> inv.SetAttributeValue(XName.Get "id", i))
            el.Add(inv)
        | _ -> ())

    // Recursively generate regular child states
    for child in regularChildren do
        match generateState transitionsBySource child with
        | Some childEl -> el.Add(childEl)
        | None -> ()  // skip non-SCXML state kinds

    Some el

let private generateRoot
    (transitionsBySource: Map<string, TransitionEdge list>)
    (doc: StatechartDocument)
    : XElement =
    let root = XElement(scxmlNs + "scxml")
    root.SetAttributeValue(XName.Get "version", "1.0")
    doc.InitialStateId |> Option.iter (fun id -> root.SetAttributeValue(XName.Get "initial", id))
    doc.Title |> Option.iter (fun n -> root.SetAttributeValue(XName.Get "name", n))

    // Extract document-level SCXML annotations
    doc.Annotations
    |> List.iter (fun a ->
        match a with
        | ScxmlAnnotation(ScxmlDatamodelType(dm)) ->
            root.SetAttributeValue(XName.Get "datamodel", dm)
        | ScxmlAnnotation(ScxmlBinding(b)) ->
            root.SetAttributeValue(XName.Get "binding", b)
        | _ -> ())

    // Generate datamodel from doc.DataEntries
    match doc.DataEntries with
    | [] -> ()
    | entries ->
        let dm = XElement(scxmlNs + "datamodel")
        for entry in entries do
            let data = XElement(scxmlNs + "data")
            data.SetAttributeValue(XName.Get "id", entry.Name)
            entry.Expression |> Option.iter (fun expr ->
                data.SetAttributeValue(XName.Get "expr", expr))
            dm.Add(data)
        root.Add(dm)

    // Extract top-level StateNode entries from doc.Elements and generate them
    let stateNodes =
        doc.Elements
        |> List.choose (fun el ->
            match el with
            | StateDecl s -> Some s
            | _ -> None)

    for state in stateNodes do
        match generateState transitionsBySource state with
        | Some el -> root.Add(el)
        | None -> ()

    root

let private buildXDocument (doc: StatechartDocument) : XDocument =
    let transitionsBySource = buildTransitionMap doc
    let root = generateRoot transitionsBySource doc
    XDocument(XDeclaration("1.0", "utf-8", null), root :> obj)

let generate (doc: StatechartDocument) : string =
    let xdoc = buildXDocument doc
    use sw = new System.IO.StringWriter()
    xdoc.Save(sw)
    sw.ToString()

let generateTo (writer: System.IO.TextWriter) (doc: StatechartDocument) : unit =
    let xdoc = buildXDocument doc
    xdoc.Save(writer)
