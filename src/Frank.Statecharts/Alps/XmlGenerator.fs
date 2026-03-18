module internal Frank.Statecharts.Alps.XmlGenerator

open System.Xml.Linq
open Frank.Statecharts.Ast

// ---------------------------------------------------------------------------
// Helper: Extract StateNodes and TransitionEdges from StatechartDocument
// ---------------------------------------------------------------------------

/// Extract all StateNodes from elements.
let private extractStateNodes (doc: StatechartDocument) : StateNode list =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | StateDecl s -> Some s
        | _ -> None)

/// Extract all TransitionEdges from elements.
let private extractTransitionEdges (doc: StatechartDocument) : TransitionEdge list =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | TransitionElement t -> Some t
        | _ -> None)

// ---------------------------------------------------------------------------
// Helper: Annotation extraction (mirrors JsonGenerator helpers)
// ---------------------------------------------------------------------------

/// Extract ALPS version from document annotations, defaulting to "1.0".
let private extractVersion (annotations: Annotation list) : string =
    annotations
    |> List.tryPick (fun a ->
        match a with
        | AlpsAnnotation(AlpsVersion v) -> Some v
        | _ -> None)
    |> Option.defaultValue "1.0"

/// Extract transition type string from transition annotations, defaulting to "unsafe".
let private transitionTypeStr (t: TransitionEdge) : string =
    t.Annotations
    |> List.tryPick (fun a ->
        match a with
        | AlpsAnnotation(AlpsTransitionType kind) ->
            match kind with
            | AlpsTransitionKind.Safe -> Some "safe"
            | AlpsTransitionKind.Unsafe -> Some "unsafe"
            | AlpsTransitionKind.Idempotent -> Some "idempotent"
        | _ -> None)
    |> Option.defaultValue "unsafe"

/// Try to get the AlpsDescriptorHref annotation from a transition.
let private tryGetDescriptorHref (t: TransitionEdge) : string option =
    t.Annotations
    |> List.tryPick (fun ann ->
        match ann with
        | AlpsAnnotation(AlpsDescriptorHref href) -> Some href
        | _ -> None)

/// Check if a transition is a shared transition (has AlpsDescriptorHref).
let private isSharedTransition (t: TransitionEdge) : bool =
    tryGetDescriptorHref t |> Option.isSome

/// Extract documentation annotation from an annotation list.
let private tryGetDocAnnotation (annotations: Annotation list) : (string option * string) option =
    annotations
    |> List.tryPick (fun a ->
        match a with
        | AlpsAnnotation(AlpsDocumentation(fmt, value)) -> Some(fmt, value)
        | _ -> None)

/// Extract non-guard extension annotations from an annotation list.
let private getExtAnnotations (annotations: Annotation list) : (string * string option * string option) list =
    annotations
    |> List.choose (fun a ->
        match a with
        | AlpsAnnotation(AlpsExtension(id, href, value)) -> Some(id, href, value)
        | _ -> None)

/// Extract link annotations from an annotation list.
let private getLinkAnnotations (annotations: Annotation list) : (string * string) list =
    annotations
    |> List.choose (fun a ->
        match a with
        | AlpsAnnotation(AlpsLink(rel, href)) -> Some(rel, href)
        | _ -> None)

/// Extract data descriptor annotations from an annotation list.
let private getDataDescriptors (annotations: Annotation list) : (string * (string option * string) option) list =
    annotations
    |> List.choose (fun a ->
        match a with
        | AlpsAnnotation(AlpsDataDescriptor(id, doc)) -> Some(id, doc)
        | _ -> None)

/// Re-add '#' prefix to rt values for local references.
let private rtValue (target: string option) : string option =
    target
    |> Option.map (fun t ->
        if t.StartsWith("http://") || t.StartsWith("https://") then t
        else "#" + t)

// ---------------------------------------------------------------------------
// XML element helpers
// ---------------------------------------------------------------------------

/// Build a <doc> element from documentation annotation data.
let private buildDocElement (fmt: string option) (value: string) : XElement =
    let docEl = XElement(XName.Get "doc")
    fmt |> Option.iter (fun f -> docEl.SetAttributeValue(XName.Get "format", f))
    docEl.Value <- value
    docEl

/// Build <ext> elements from extension annotation list.
let private buildExtElements (exts: (string * string option * string option) list) : XElement list =
    exts
    |> List.map (fun (id, href, value) ->
        let extEl = XElement(XName.Get "ext")
        extEl.SetAttributeValue(XName.Get "id", id)
        href |> Option.iter (fun h -> extEl.SetAttributeValue(XName.Get "href", h))
        value |> Option.iter (fun v -> extEl.SetAttributeValue(XName.Get "value", v))
        extEl)

/// Build <link> elements from link annotation list.
let private buildLinkElements (links: (string * string) list) : XElement list =
    links
    |> List.map (fun (rel, href) ->
        let linkEl = XElement(XName.Get "link")
        linkEl.SetAttributeValue(XName.Get "rel", rel)
        linkEl.SetAttributeValue(XName.Get "href", href)
        linkEl)

/// Build a full transition descriptor element (non-shared reference).
let private buildTransitionDescriptor (t: TransitionEdge) : XElement =
    let el = XElement(XName.Get "descriptor")

    t.Event |> Option.iter (fun id -> el.SetAttributeValue(XName.Get "id", id))
    el.SetAttributeValue(XName.Get "type", transitionTypeStr t)
    rtValue t.Target |> Option.iter (fun rt -> el.SetAttributeValue(XName.Get "rt", rt))

    // Transition-level documentation
    tryGetDocAnnotation t.Annotations
    |> Option.iter (fun (fmt, value) -> el.Add(buildDocElement fmt value))

    // Parameter descriptors as child href-only elements
    for param in t.Parameters do
        let paramEl = XElement(XName.Get "descriptor")
        paramEl.SetAttributeValue(XName.Get "href", "#" + param)
        el.Add(paramEl)

    // Extensions: guard first, then other extension annotations
    let nonGuardExts = getExtAnnotations t.Annotations

    let extElements =
        match t.Guard with
        | Some guard -> ("guard", None, Some guard) :: nonGuardExts
        | None -> nonGuardExts

    for extEl in buildExtElements extElements do
        el.Add(extEl)

    // Transition-level links (unusual but possible)
    for linkEl in buildLinkElements (getLinkAnnotations t.Annotations) do
        el.Add(linkEl)

    el

/// Build a data descriptor as a top-level semantic descriptor element.
let private buildDataDescriptor (id: string) (doc: (string option * string) option) : XElement =
    let el = XElement(XName.Get "descriptor")
    el.SetAttributeValue(XName.Get "id", id)
    el.SetAttributeValue(XName.Get "type", "semantic")

    doc
    |> Option.iter (fun (fmt, value) -> el.Add(buildDocElement fmt value))

    el

// ---------------------------------------------------------------------------
// Shared transition deduplication (mirrors JsonGenerator D-004)
// ---------------------------------------------------------------------------

/// Collect shared transitions: group by event name, take the first (canonical) transition.
let private collectSharedTransitions (transitions: TransitionEdge list) : (string * TransitionEdge) list =
    transitions
    |> List.filter isSharedTransition
    |> List.choose (fun t ->
        match t.Event with
        | Some eventName -> Some(eventName, t)
        | None -> None)
    |> List.groupBy fst
    |> List.map (fun (eventName, group) -> eventName, group |> List.head |> snd)

/// Get the set of shared transition event names.
let private sharedTransitionNames (sharedTransitions: (string * TransitionEdge) list) : Set<string> =
    sharedTransitions |> List.map fst |> Set.ofList

// ---------------------------------------------------------------------------
// Document builder
// ---------------------------------------------------------------------------

let private buildXDocument (doc: StatechartDocument) : XDocument =
    let states = extractStateNodes doc
    let transitions = extractTransitionEdges doc

    // Group transitions by source state
    let transitionsBySource =
        transitions
        |> List.groupBy (fun t -> t.Source)
        |> Map.ofList

    // Identify shared transitions (D-004)
    let sharedTransitions = collectSharedTransitions transitions
    let sharedNames = sharedTransitionNames sharedTransitions

    // Extract document-level annotations
    let version = extractVersion doc.Annotations
    let dataDescriptors = getDataDescriptors doc.Annotations
    let docLinks = getLinkAnnotations doc.Annotations
    let docExts = getExtAnnotations doc.Annotations

    // Build root <alps> element — no namespace (ALPS XML uses no namespace)
    let root = XElement(XName.Get "alps")
    root.SetAttributeValue(XName.Get "version", version)

    // Document-level documentation
    tryGetDocAnnotation doc.Annotations
    |> Option.iter (fun (fmt, value) -> root.Add(buildDocElement fmt value))

    // 1. Data descriptors
    for (id, docOpt) in dataDescriptors do
        root.Add(buildDataDescriptor id docOpt)

    // 2. State descriptors
    for state in states do
        let stateEl = XElement(XName.Get "descriptor")
        state.Identifier |> Option.iter (fun id -> stateEl.SetAttributeValue(XName.Get "id", id))
        stateEl.SetAttributeValue(XName.Get "type", "semantic")

        // State-level documentation
        tryGetDocAnnotation state.Annotations
        |> Option.iter (fun (fmt, value) -> stateEl.Add(buildDocElement fmt value))

        // State's transitions as child descriptors
        let stateTransitions =
            state.Identifier
            |> Option.bind (fun id -> Map.tryFind id transitionsBySource)
            |> Option.defaultValue []

        for t in stateTransitions do
            match t.Event with
            | Some eventName when Set.contains eventName sharedNames ->
                // Shared transition: emit href-only reference
                let href =
                    tryGetDescriptorHref t
                    |> Option.defaultValue ("#" + eventName)

                let refEl = XElement(XName.Get "descriptor")
                refEl.SetAttributeValue(XName.Get "href", href)
                stateEl.Add(refEl)
            | _ ->
                // Regular transition: emit full descriptor
                stateEl.Add(buildTransitionDescriptor t)

        // State-level extensions
        for extEl in buildExtElements (getExtAnnotations state.Annotations) do
            stateEl.Add(extEl)

        // State-level links
        for linkEl in buildLinkElements (getLinkAnnotations state.Annotations) do
            stateEl.Add(linkEl)

        root.Add(stateEl)

    // 3. Shared transition descriptors (top-level)
    for (_eventName, canonical) in sharedTransitions do
        root.Add(buildTransitionDescriptor canonical)

    // Document-level links
    for linkEl in buildLinkElements docLinks do
        root.Add(linkEl)

    // Document-level extensions
    for extEl in buildExtElements docExts do
        root.Add(extEl)

    XDocument(XDeclaration("1.0", "utf-8", null), root :> obj)

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/// Generate an ALPS XML string from a StatechartDocument.
let generateAlpsXml (doc: StatechartDocument) : string =
    let xdoc = buildXDocument doc
    use sw = new System.IO.StringWriter()
    xdoc.Save(sw)
    sw.ToString()
