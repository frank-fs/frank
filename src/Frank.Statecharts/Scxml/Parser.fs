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

// T010: Out-of-scope but known elements (silently skipped, no warning per parser-api.md section 9)
let private outOfScopeElements =
    set
        [ "onentry"
          "onexit"
          "script"
          "send"
          "raise"
          "log"
          "assign"
          "cancel"
          "foreach"
          "param"
          "content"
          "donedata"
          "finalize" ]

// T010: Elements that are recognized within a state context (no warning)
let private knownStateChildElements =
    set
        [ "state"
          "parallel"
          "final"
          "transition"
          "datamodel"
          "history"
          "invoke"
          "initial" ]

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

// T007: Parse <datamodel>/<data> elements from a parent element
let private parseDataEntries (parent: XElement) : DataEntry list =
    parent.Elements()
    |> Seq.filter (fun el -> el.Name.LocalName = "datamodel")
    |> Seq.collect (fun dm ->
        dm.Elements()
        |> Seq.filter (fun el -> el.Name.LocalName = "data"))
    |> Seq.map (fun dataEl ->
        let expr =
            // expr attribute takes precedence over child text content (parser-api.md section 7)
            match attrValue "expr" dataEl with
            | Some e -> Some e
            | None ->
                // Fall back to trimmed child text content
                let text = dataEl.Value.Trim()

                if System.String.IsNullOrEmpty(text) then
                    None
                else
                    Some text

        { Id = (attrValue "id" dataEl) |> Option.defaultValue ""
          Expression = expr
          Position = extractPosition dataEl })
    |> Seq.toList

// T008: Parse a <history> element into ScxmlHistory
let private parseHistory
    (warnings: ResizeArray<ParseWarning>)
    (el: XElement)
    : ScxmlHistory =
    let kind =
        match attrValue "type" el with
        | Some "deep" -> Deep
        | Some "shallow" -> Shallow
        | Some invalid ->
            // T010: Invalid history type value -- emit warning, default to Shallow
            warnings.Add(
                { Description =
                    sprintf
                        "Invalid <history> type value '%s'; defaulting to 'shallow'"
                        invalid
                  Position = extractPosition el
                  Suggestion = Some "Use 'shallow' or 'deep'" }
            )

            Shallow
        | None -> Shallow // Default per W3C spec (FR-010)

    let defaultTransition =
        el.Elements()
        |> Seq.tryFind (fun child -> child.Name.LocalName = "transition")
        |> Option.map parseTransition

    { Id = (attrValue "id" el) |> Option.defaultValue ""
      Kind = kind
      DefaultTransition = defaultTransition
      Position = extractPosition el }

// T008: Parse an <invoke> element into ScxmlInvoke
let private parseInvoke (el: XElement) : ScxmlInvoke =
    { InvokeType = attrValue "type" el
      Src = attrValue "src" el
      Id = attrValue "id" el
      Position = extractPosition el }

// T010: Collect warnings for unknown child elements within a state
let private collectChildWarnings
    (warnings: ResizeArray<ParseWarning>)
    (el: XElement)
    : unit =
    el.Elements()
    |> Seq.iter (fun child ->
        let localName = child.Name.LocalName

        if
            not (knownStateChildElements.Contains localName)
            && not (outOfScopeElements.Contains localName)
        then
            warnings.Add(
                { Description =
                    sprintf "Unknown element <%s> inside <%s>" localName el.Name.LocalName
                  Position = extractPosition child
                  Suggestion = None }
            ))

// T005: Parse <state>, <final>, <parallel> elements recursively
let rec private parseState
    (warnings: ResizeArray<ParseWarning>)
    (el: XElement)
    : ScxmlState =
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
        |> Seq.map (parseState warnings)
        |> Seq.toList

    // T006: Parse child <transition> elements
    let transitions =
        el.Elements()
        |> Seq.filter (fun child -> isElement "transition" child)
        |> Seq.map parseTransition
        |> Seq.toList

    // T007: Parse <datamodel>/<data> entries
    let dataEntries = parseDataEntries el

    // T008: Parse <history> elements
    let historyNodes =
        el.Elements()
        |> Seq.filter (fun child -> child.Name.LocalName = "history")
        |> Seq.map (parseHistory warnings)
        |> Seq.toList

    // T008: Parse <invoke> elements
    let invokeNodes =
        el.Elements()
        |> Seq.filter (fun child -> child.Name.LocalName = "invoke")
        |> Seq.map parseInvoke
        |> Seq.toList

    // T010: Collect warnings for unknown child elements
    collectChildWarnings warnings el

    { Id = attrValue "id" el
      Kind = kind
      InitialId = attrValue "initial" el
      Transitions = transitions
      Children = children
      DataEntries = dataEntries
      HistoryNodes = historyNodes
      InvokeNodes = invokeNodes
      Position = extractPosition el }

// T010: Collect warnings for unknown child elements at root <scxml> level
let private collectRootWarnings
    (warnings: ResizeArray<ParseWarning>)
    (root: XElement)
    : unit =
    let knownRootChildren =
        set [ "state"; "parallel"; "final"; "datamodel"; "script"; "initial" ]

    root.Elements()
    |> Seq.iter (fun child ->
        let localName = child.Name.LocalName

        if
            not (knownRootChildren.Contains localName)
            && not (outOfScopeElements.Contains localName)
        then
            warnings.Add(
                { Description =
                    sprintf "Unknown element <%s> inside <scxml>" localName
                  Position = extractPosition child
                  Suggestion = None }
            ))

// T004/T011: Parse XDocument into ScxmlParseResult (core logic shared by all entry points)
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
        // T010: Accumulate warnings during parsing
        let warnings = ResizeArray<ParseWarning>()

        // T005: Collect top-level state elements
        let states =
            root.Elements()
            |> Seq.filter isStateElement
            |> Seq.map (parseState warnings)
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

        // T010: Collect warnings for unknown root children
        collectRootWarnings warnings root

        // T007: Parse document-level <datamodel>/<data> entries
        let dataEntries = parseDataEntries root

        // T004: Build the ScxmlDocument
        let doc =
            { Name = attrValue "name" root
              InitialId = initialId
              DatamodelType = attrValue "datamodel" root
              Binding = attrValue "binding" root
              States = states
              DataEntries = dataEntries
              Position = extractPosition root }

        { Document = Some doc
          Errors = []
          Warnings = warnings |> Seq.toList }

// T010/T011: Shared error-handling wrapper for all parse entry points
let private tryParseWith (loadFn: unit -> XDocument) : ScxmlParseResult =
    try
        loadFn () |> parseDocument
    with :? XmlException as ex ->
        { Document = None
          Errors =
            [ { Description = ex.Message
                Position =
                    Some
                        { Line = ex.LineNumber
                          Column = ex.LinePosition } } ]
          Warnings = [] }

// T003/T004/T010: Parse SCXML from a string
let parseString (xml: string) : ScxmlParseResult =
    tryParseWith (fun () -> XDocument.Parse(xml, LoadOptions.SetLineInfo))

// T011: Parse SCXML from a TextReader (caller owns lifetime)
let parseReader (reader: System.IO.TextReader) : ScxmlParseResult =
    tryParseWith (fun () -> XDocument.Load(reader, LoadOptions.SetLineInfo))

// T011: Parse SCXML from a Stream (caller owns lifetime)
let parseStream (stream: System.IO.Stream) : ScxmlParseResult =
    tryParseWith (fun () -> XDocument.Load(stream, LoadOptions.SetLineInfo))
