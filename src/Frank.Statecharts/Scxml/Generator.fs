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

let private generateTransition (ns: XNamespace) (t: TransitionEdge) : XElement =
    let el = XElement(ns + "transition")

    t.Event |> Option.iter (fun ev -> el.SetAttributeValue(XName.Get "event", ev))

    t.Guard |> Option.iter (fun g -> el.SetAttributeValue(XName.Get "cond", g))

    let targets =
        t.Annotations
        |> List.tryPick (fun a ->
            match a with
            | ScxmlAnnotation(ScxmlMultiTarget(targets)) -> Some targets
            | _ -> None)
        |> Option.defaultWith (fun () -> t.Target |> Option.toList)

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

let private generateHistory (ns: XNamespace) (h: StateNode) : XElement =
    let el = XElement(ns + "history")

    let historyMeta =
        h.Annotations
        |> List.tryPick (fun a ->
            match a with
            | ScxmlAnnotation(ScxmlHistory(id, kind, defaultTarget)) -> Some(id, kind, defaultTarget)
            | _ -> None)

    match historyMeta with
    | Some(id, kind, defaultTarget) ->
        el.SetAttributeValue(XName.Get "id", id)

        let typeStr =
            match kind with
            | Deep -> "deep"
            | Shallow -> "shallow"

        el.SetAttributeValue(XName.Get "type", typeStr)

        defaultTarget
        |> Option.iter (fun target ->
            let t = XElement(ns + "transition")
            t.SetAttributeValue(XName.Get "target", target)
            el.Add(t))
    | None ->
        // Fallback: use StateNode fields directly
        h.Identifier |> Option.iter (fun id -> el.SetAttributeValue(XName.Get "id", id))

        let typeStr =
            match h.Kind with
            | DeepHistory -> "deep"
            | _ -> "shallow"

        el.SetAttributeValue(XName.Get "type", typeStr)

    el

let rec private generateState
    (ns: XNamespace)
    (transitionsBySource: Map<string, TransitionEdge list>)
    (state: StateNode)
    : XElement option =

    let elementNameOpt =
        match state.Kind with
        | Final -> Some "final"
        | Parallel -> Some "parallel"
        | Regular
        | Initial
        | Composite -> Some "state"
        | ShallowHistory
        | DeepHistory -> None // handled separately by generateHistory
        | Choice
        | ForkJoin
        | Terminate -> None // no SCXML equivalent, skip

    match elementNameOpt with
    | None -> None
    | Some elementName ->

        let el = XElement(ns + elementName)

        state.Identifier
        |> Option.iter (fun id -> el.SetAttributeValue(XName.Get "id", id))

        // Emit <initial> child element if ScxmlInitialElement annotation is present;
        // otherwise fall back to initial attribute from ScxmlInitial annotation.
        let hasInitialElement =
            state.Annotations
            |> List.tryPick (fun a ->
                match a with
                | ScxmlAnnotation(ScxmlInitialElement(targetId)) -> Some targetId
                | _ -> None)

        match hasInitialElement with
        | Some targetId ->
            let initEl = XElement(ns + "initial")
            let transEl = XElement(ns + "transition")
            transEl.SetAttributeValue(XName.Get "target", targetId)
            initEl.Add(transEl)
            el.Add(initEl)
        | None ->
            // Fall back to initial attribute (existing behavior)
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
                | ShallowHistory
                | DeepHistory -> true
                | _ -> false)

        // Generate history elements
        for h in historyChildren do
            el.Add(generateHistory ns h)

        // Generate transitions for this state
        let ownTransitions =
            state.Identifier
            |> Option.bind (fun id -> Map.tryFind id transitionsBySource)
            |> Option.defaultValue []

        for t in ownTransitions do
            el.Add(generateTransition ns t)

        // Generate invoke elements from ScxmlAnnotation(ScxmlInvoke(...))
        state.Annotations
        |> List.iter (fun a ->
            match a with
            | ScxmlAnnotation(ScxmlInvoke(invokeType, src, id)) ->
                let inv = XElement(ns + "invoke")

                if invokeType <> "" then
                    inv.SetAttributeValue(XName.Get "type", invokeType)

                src |> Option.iter (fun s -> inv.SetAttributeValue(XName.Get "src", s))
                id |> Option.iter (fun i -> inv.SetAttributeValue(XName.Get "id", i))
                el.Add(inv)
            | _ -> ())

        // Emit <onentry> blocks from ScxmlOnEntry annotations
        state.Annotations
        |> List.iter (fun a ->
            match a with
            | ScxmlAnnotation(ScxmlOnEntry(xml)) ->
                try
                    let onEntryEl = XElement.Parse(xml)
                    el.Add(onEntryEl)
                with :? System.Xml.XmlException ->
                    ()
            | _ -> ())

        // Emit <onexit> blocks from ScxmlOnExit annotations
        state.Annotations
        |> List.iter (fun a ->
            match a with
            | ScxmlAnnotation(ScxmlOnExit(xml)) ->
                try
                    let onExitEl = XElement.Parse(xml)
                    el.Add(onExitEl)
                with :? System.Xml.XmlException ->
                    ()
            | _ -> ())

        // Recursively generate regular child states
        for child in regularChildren do
            match generateState ns transitionsBySource child with
            | Some childEl -> el.Add(childEl)
            | None -> () // skip non-SCXML state kinds

        Some el

let private generateRoot
    (ns: XNamespace)
    (transitionsBySource: Map<string, TransitionEdge list>)
    (doc: StatechartDocument)
    : XElement =
    let root = XElement(ns + "scxml")
    root.SetAttributeValue(XName.Get "version", "1.0")

    doc.InitialStateId
    |> Option.iter (fun id -> root.SetAttributeValue(XName.Get "initial", id))

    doc.Title |> Option.iter (fun n -> root.SetAttributeValue(XName.Get "name", n))

    // Extract document-level SCXML annotations
    doc.Annotations
    |> List.iter (fun a ->
        match a with
        | ScxmlAnnotation(ScxmlDatamodelType(dm)) -> root.SetAttributeValue(XName.Get "datamodel", dm)
        | ScxmlAnnotation(ScxmlBinding(b)) -> root.SetAttributeValue(XName.Get "binding", b)
        | _ -> ())

    // Generate datamodel from doc.DataEntries
    match doc.DataEntries with
    | [] -> ()
    | entries ->
        let dm = XElement(ns + "datamodel")

        for entry in entries do
            let data = XElement(ns + "data")
            data.SetAttributeValue(XName.Get "id", entry.Name)

            entry.Expression
            |> Option.iter (fun expr -> data.SetAttributeValue(XName.Get "expr", expr))
            // Add src attribute from ScxmlDataSrc annotation if present
            doc.Annotations
            |> List.tryPick (fun a ->
                match a with
                | ScxmlAnnotation(ScxmlDataSrc(name, src)) when name = entry.Name -> Some src
                | _ -> None)
            |> Option.iter (fun src -> data.SetAttributeValue(XName.Get "src", src))

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
        match generateState ns transitionsBySource state with
        | Some el -> root.Add(el)
        | None -> ()

    root

let private buildXDocument (doc: StatechartDocument) : XDocument =
    let transitionsBySource = buildTransitionMap doc

    // Compute effective namespace: use ScxmlNamespace annotation if present,
    // otherwise default to the W3C SCXML namespace.
    let effectiveNs =
        doc.Annotations
        |> List.tryPick (fun a ->
            match a with
            | ScxmlAnnotation(ScxmlNamespace(ns)) -> Some ns
            | _ -> None)
        |> Option.map (fun ns ->
            if System.String.IsNullOrEmpty(ns) then
                XNamespace.None
            else
                XNamespace.Get(ns))
        |> Option.defaultValue scxmlNs

    let root = generateRoot effectiveNs transitionsBySource doc
    XDocument(XDeclaration("1.0", "utf-8", null), root :> obj)

let generate (doc: StatechartDocument) : string =
    let xdoc = buildXDocument doc
    use sw = new System.IO.StringWriter()
    xdoc.Save(sw)
    sw.ToString()

let generateTo (writer: System.IO.TextWriter) (doc: StatechartDocument) : unit =
    let xdoc = buildXDocument doc
    xdoc.Save(writer)
