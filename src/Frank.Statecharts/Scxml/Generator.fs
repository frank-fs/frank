module internal Frank.Statecharts.Scxml.Generator

open System.Xml.Linq
open Frank.Statecharts.Scxml.Types

let private scxmlNs = XNamespace.Get("http://www.w3.org/2005/07/scxml")

let private generateTransition (t: ScxmlTransition) : XElement =
    let el = XElement(scxmlNs + "transition")

    t.Event |> Option.iter (fun ev -> el.SetAttributeValue(XName.Get "event", ev))

    t.Guard |> Option.iter (fun g -> el.SetAttributeValue(XName.Get "cond", g))

    match t.Targets with
    | [] -> ()
    | targets ->
        el.SetAttributeValue(XName.Get "target", System.String.Join(" ", targets))

    match t.TransitionType with
    | Internal -> el.SetAttributeValue(XName.Get "type", "internal")
    | External -> ()

    el

let private generateDatamodel (entries: DataEntry list) : XElement option =
    match entries with
    | [] -> None
    | entries ->
        let dm = XElement(scxmlNs + "datamodel")
        for entry in entries do
            let data = XElement(scxmlNs + "data")
            data.SetAttributeValue(XName.Get "id", entry.Id)
            entry.Expression |> Option.iter (fun expr ->
                data.SetAttributeValue(XName.Get "expr", expr))
            dm.Add(data)
        Some dm

let private generateHistory (h: ScxmlHistory) : XElement =
    let el = XElement(scxmlNs + "history")
    el.SetAttributeValue(XName.Get "id", h.Id)

    let typeStr =
        match h.Kind with
        | Shallow -> "shallow"
        | Deep -> "deep"
    el.SetAttributeValue(XName.Get "type", typeStr)

    h.DefaultTransition |> Option.iter (fun t ->
        el.Add(generateTransition t))

    el

let private generateInvoke (inv: ScxmlInvoke) : XElement =
    let el = XElement(scxmlNs + "invoke")
    inv.InvokeType |> Option.iter (fun t -> el.SetAttributeValue(XName.Get "type", t))
    inv.Src |> Option.iter (fun s -> el.SetAttributeValue(XName.Get "src", s))
    inv.Id |> Option.iter (fun id -> el.SetAttributeValue(XName.Get "id", id))
    el

let rec private generateState (state: ScxmlState) : XElement =
    let elementName =
        match state.Kind with
        | Final -> "final"
        | Parallel -> "parallel"
        | Simple | Compound -> "state"

    let el = XElement(scxmlNs + elementName)

    state.Id |> Option.iter (fun id -> el.SetAttributeValue(XName.Get "id", id))

    state.InitialId |> Option.iter (fun id -> el.SetAttributeValue(XName.Get "initial", id))

    generateDatamodel state.DataEntries
    |> Option.iter (fun dm -> el.Add(dm))

    for h in state.HistoryNodes do
        el.Add(generateHistory h)

    for t in state.Transitions do
        el.Add(generateTransition t)

    for inv in state.InvokeNodes do
        el.Add(generateInvoke inv)

    for child in state.Children do
        el.Add(generateState child)

    el

let private generateRoot (doc: ScxmlDocument) : XElement =
    let root = XElement(scxmlNs + "scxml")
    root.SetAttributeValue(XName.Get "version", "1.0")
    doc.InitialId |> Option.iter (fun id -> root.SetAttributeValue(XName.Get "initial", id))
    doc.Name |> Option.iter (fun n -> root.SetAttributeValue(XName.Get "name", n))
    doc.DatamodelType |> Option.iter (fun dm -> root.SetAttributeValue(XName.Get "datamodel", dm))
    doc.Binding |> Option.iter (fun b -> root.SetAttributeValue(XName.Get "binding", b))

    generateDatamodel doc.DataEntries
    |> Option.iter (fun dm -> root.Add(dm))

    for state in doc.States do
        root.Add(generateState state)

    root

let private buildXDocument (doc: ScxmlDocument) : XDocument =
    let root = generateRoot doc
    XDocument(XDeclaration("1.0", "utf-8", null), root :> obj)

let generate (doc: ScxmlDocument) : string =
    let xdoc = buildXDocument doc
    use sw = new System.IO.StringWriter()
    xdoc.Save(sw)
    sw.ToString()

let generateTo (writer: System.IO.TextWriter) (doc: ScxmlDocument) : unit =
    let xdoc = buildXDocument doc
    xdoc.Save(writer)
