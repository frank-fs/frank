module internal Frank.Statecharts.Scxml.Parser

open System.Xml
open System.Xml.Linq
open Frank.Statecharts.Scxml.Types

// T003: SCXML namespace constant
let private scxmlNs = XNamespace.Get("http://www.w3.org/2005/07/scxml")

// T003: Extract source position from any XObject via IXmlLineInfo
let private extractPosition (obj: XObject) : SourcePosition option =
    let lineInfo = obj :> IXmlLineInfo

    if lineInfo.HasLineInfo() then
        Some { Line = lineInfo.LineNumber; Column = lineInfo.LinePosition }
    else
        None

// T003: Get attribute value as string option
let private attrValue (name: string) (el: XElement) : string option =
    match el.Attribute(XName.Get name) with
    | null -> None
    | attr -> Some attr.Value

// T003: Check if an element matches a local name (supports both namespaced and no-namespace docs)
let private isElement (localName: string) (el: XElement) : bool =
    el.Name.LocalName = localName
    && (el.Name.Namespace = scxmlNs || el.Name.Namespace = XNamespace.None)

// T003: Check if an element is a state-like element (state, parallel, or final)
let private isStateElement (el: XElement) : bool =
    let n = el.Name.LocalName
    (n = "state" || n = "parallel" || n = "final")
    && (el.Name.Namespace = scxmlNs || el.Name.Namespace = XNamespace.None)

// T006: Parse a <transition> element into ScxmlTransition
let private parseTransition (el: XElement) : ScxmlTransition =
    let targets =
        match attrValue "target" el with
        | Some t ->
            t.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
            |> Array.toList
        | None -> []

    let transType =
        match attrValue "type" el with
        | Some "internal" -> Internal
        | _ -> External // Default per W3C spec

    { Event = attrValue "event" el
      Guard = attrValue "cond" el
      Targets = targets
      TransitionType = transType
      Position = extractPosition el }

// T005: Parse <state>, <final>, <parallel> elements recursively
let rec private parseState (el: XElement) : ScxmlState =
    let localName = el.Name.LocalName

    // T005: Determine ScxmlStateKind based on element name and child presence
    let hasChildStates =
        el.Elements()
        |> Seq.exists isStateElement

    let kind =
        match localName with
        | "final" -> Final
        | "parallel" -> Parallel
        | "state" ->
            if hasChildStates then Compound else Simple
        | _ -> Simple // Fallback (should not occur for valid SCXML)

    // Recursively parse child state elements
    let children =
        el.Elements()
        |> Seq.filter isStateElement
        |> Seq.map parseState
        |> Seq.toList

    // T006: Parse child <transition> elements
    let transitions =
        el.Elements()
        |> Seq.filter (fun child -> isElement "transition" child)
        |> Seq.map parseTransition
        |> Seq.toList

    { Id = attrValue "id" el
      Kind = kind
      InitialId = attrValue "initial" el
      Transitions = transitions
      Children = children
      DataEntries = []     // Stub: filled by WP03/T007
      HistoryNodes = []    // Stub: filled by WP03/T008
      InvokeNodes = []     // Stub: filled by WP03/T008
      Position = extractPosition el }

// T004: Parse XDocument into ScxmlParseResult (core logic shared by all entry points)
let private parseDocument (xdoc: XDocument) : ScxmlParseResult =
    let root = xdoc.Root

    // T004: Validate root element is <scxml> (namespaced or no-namespace)
    if root = null then
        { Document = None
          Errors =
            [ { Description = "Empty XML document: no root element found"
                Position = None } ]
          Warnings = [] }
    elif not (isElement "scxml" root) then
        { Document = None
          Errors =
            [ { Description =
                    sprintf
                        "Expected root element <scxml> but found <%s>"
                        root.Name.LocalName
                Position = extractPosition root } ]
          Warnings = [] }
    else
        // T005: Collect top-level state elements
        let states =
            root.Elements()
            |> Seq.filter isStateElement
            |> Seq.map parseState
            |> Seq.toList

        // T009: Initial state inference
        let initialId =
            match attrValue "initial" root with
            | Some id -> Some id
            | None ->
                // Per W3C section 3.2: use first child state's id
                states
                |> List.tryHead
                |> Option.bind (fun s -> s.Id)

        // T004: Build the ScxmlDocument
        let doc =
            { Name = attrValue "name" root
              InitialId = initialId
              DatamodelType = attrValue "datamodel" root
              Binding = attrValue "binding" root
              States = states
              DataEntries = [] // Stub: filled by WP03/T007
              Position = extractPosition root }

        { Document = Some doc
          Errors = []
          Warnings = [] }

// T003/T004: Parse SCXML from a string
let parseString (xml: string) : ScxmlParseResult =
    let xdoc = XDocument.Parse(xml, LoadOptions.SetLineInfo)
    parseDocument xdoc
