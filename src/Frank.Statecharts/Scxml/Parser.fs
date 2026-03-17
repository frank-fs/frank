module internal Frank.Statecharts.Scxml.Parser

open System.Xml
open System.Xml.Linq
open Frank.Statecharts.Ast

// SCXML namespace constant
let private scxmlNs = XNamespace.Get("http://www.w3.org/2005/07/scxml")

// Extract source position from any XObject via IXmlLineInfo.
// Returns Ast.SourcePosition directly to avoid conversion overhead.
let private extractPosition (obj: XObject) : SourcePosition option =
    let lineInfo = obj :> IXmlLineInfo

    if lineInfo.HasLineInfo() then
        Some { Line = lineInfo.LineNumber; Column = lineInfo.LinePosition }
    else
        None

// Get attribute value as string option
let private attrValue (name: string) (el: XElement) : string option =
    match el.Attribute(XName.Get name) with
    | null -> None
    | attr -> Some attr.Value

// Check if an element matches a local name (supports both namespaced and no-namespace docs)
let private isElement (localName: string) (el: XElement) : bool =
    el.Name.LocalName = localName
    && (el.Name.Namespace = scxmlNs || el.Name.Namespace = XNamespace.None)

// Check if an element is a state-like element (state, parallel, or final)
let private isStateElement (el: XElement) : bool =
    let n = el.Name.LocalName
    (n = "state" || n = "parallel" || n = "final")
    && (el.Name.Namespace = scxmlNs || el.Name.Namespace = XNamespace.None)

// Out-of-scope but known elements (silently skipped, no warning per parser-api.md section 9)
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

// Elements that are recognized within a state context (no warning)
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

// Parse a <transition> element into TransitionEdge
let private parseTransition (sourceId: string) (el: XElement) : TransitionEdge =
    let targets =
        match attrValue "target" el with
        | Some t ->
            t.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
            |> Array.toList
        | None -> []

    let isInternal =
        match attrValue "type" el with
        | Some "internal" -> true
        | _ -> false

    let annotations =
        [ if isInternal then
              yield ScxmlAnnotation(ScxmlTransitionType(true))
          if targets.Length >= 2 then
              yield ScxmlAnnotation(ScxmlMultiTarget(targets)) ]

    { Source = sourceId
      Target = targets |> List.tryHead
      Event = attrValue "event" el
      Guard = attrValue "cond" el
      Action = None
      Parameters = []
      Position = extractPosition el
      Annotations = annotations }

// Parse <datamodel>/<data> elements from a parent element, producing DataEntry list
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

        { DataEntry.Name = (attrValue "id" dataEl) |> Option.defaultValue ""
          Expression = expr
          Position = extractPosition dataEl })
    |> Seq.toList

// Parse a <history> element into StateNode
let private parseHistory
    (warnings: ResizeArray<ParseWarning>)
    (el: XElement)
    : StateNode =
    let historyId = (attrValue "id" el) |> Option.defaultValue ""

    let astHistoryKind, stateKind =
        match attrValue "type" el with
        | Some "deep" -> Deep, DeepHistory
        | Some "shallow" -> Shallow, ShallowHistory
        | Some invalid ->
            // Invalid history type value -- emit warning, default to Shallow
            warnings.Add(
                { ParseWarning.Position = extractPosition el
                  Description =
                    sprintf
                        "Invalid <history> type value '%s'; defaulting to 'shallow'"
                        invalid
                  Suggestion = Some "Use 'shallow' or 'deep'" }
            )

            Shallow, ShallowHistory
        | None -> Shallow, ShallowHistory // Default per W3C spec

    let defaultTarget =
        el.Elements()
        |> Seq.tryFind (fun child -> child.Name.LocalName = "transition")
        |> Option.bind (fun t -> attrValue "target" t)

    { Identifier = historyId
      Label = None
      Kind = stateKind
      Children = []
      Activities = None
      Position = extractPosition el
      Annotations = [ ScxmlAnnotation(ScxmlHistory(historyId, astHistoryKind, defaultTarget)) ] }

// Collect warnings for unknown child elements within a state
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
                { ParseWarning.Position = extractPosition child
                  Description =
                    sprintf "Unknown element <%s> inside <%s>" localName el.Name.LocalName
                  Suggestion = None }
            ))

// Parse <state>, <final>, <parallel> elements recursively.
// Returns (StateNode, TransitionEdge list, DataEntry list).
let rec private parseState
    (warnings: ResizeArray<ParseWarning>)
    (el: XElement)
    : StateNode * TransitionEdge list * DataEntry list =
    let localName = el.Name.LocalName
    let stateId = (attrValue "id" el) |> Option.defaultValue ""

    // Determine StateKind based on element name
    let astKind =
        match localName with
        | "final" -> Final
        | "parallel" -> Parallel
        | "state" -> Regular
        | _ -> Regular // Fallback (should not occur for valid SCXML)

    // Recursively parse child state elements
    let childResults =
        el.Elements()
        |> Seq.filter isStateElement
        |> Seq.map (parseState warnings)
        |> Seq.toList

    let childNodes = childResults |> List.map (fun (n, _, _) -> n)
    let childTransitions = childResults |> List.collect (fun (_, t, _) -> t)
    let childDataEntries = childResults |> List.collect (fun (_, _, d) -> d)

    // Parse child <transition> elements
    let ownTransitions =
        el.Elements()
        |> Seq.filter (fun child -> isElement "transition" child)
        |> Seq.map (parseTransition stateId)
        |> Seq.toList

    // Parse <datamodel>/<data> entries at state level
    let stateDataEntries = parseDataEntries el

    // Parse <history> elements
    let historyNodes =
        el.Elements()
        |> Seq.filter (fun child -> child.Name.LocalName = "history")
        |> Seq.map (parseHistory warnings)
        |> Seq.toList

    // Parse <invoke> elements -> ScxmlAnnotation entries
    let invokeAnnotations =
        el.Elements()
        |> Seq.filter (fun child -> child.Name.LocalName = "invoke")
        |> Seq.map (fun invEl ->
            let invokeType = attrValue "type" invEl |> Option.defaultValue ""
            let src = attrValue "src" invEl
            let invId = attrValue "id" invEl
            ScxmlAnnotation(ScxmlInvoke(invokeType, src, invId)))
        |> Seq.toList

    // Parse state-level initial attribute
    let initialAnnotation =
        match attrValue "initial" el with
        | Some initId -> [ ScxmlAnnotation(ScxmlInitial(initId)) ]
        | None -> []

    // Collect warnings for unknown child elements
    collectChildWarnings warnings el

    let stateNode =
        { Identifier = stateId
          Label = None
          Kind = astKind
          Children = childNodes @ historyNodes
          Activities = None
          Position = extractPosition el
          Annotations = invokeAnnotations @ initialAnnotation }

    stateNode, ownTransitions @ childTransitions, stateDataEntries @ childDataEntries

// Collect warnings for unknown child elements at root <scxml> level
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
                { ParseWarning.Position = extractPosition child
                  Description =
                    sprintf "Unknown element <%s> inside <scxml>" localName
                  Suggestion = None }
            ))

// Parse XDocument into ParseResult (core logic shared by all entry points)
let private parseDocument (xdoc: XDocument) : ParseResult =
    let root = xdoc.Root

    let emptyDoc =
        { Title = None
          InitialStateId = None
          Elements = []
          DataEntries = []
          Annotations = [] }

    // Validate root element is <scxml> (namespaced or no-namespace)
    if root = null then
        { Document = emptyDoc
          Errors =
            [ { Position = None
                Description = "Empty XML document: no root element found"
                Expected = ""; Found = ""; CorrectiveExample = "" } ]
          Warnings = [] }
    elif not (isElement "scxml" root) then
        { Document = emptyDoc
          Errors =
            [ { Position = extractPosition root
                Description =
                    sprintf
                        "Expected root element <scxml> but found <%s>"
                        root.Name.LocalName
                Expected = ""; Found = ""; CorrectiveExample = "" } ]
          Warnings = [] }
    else
        // Accumulate warnings during parsing
        let warnings = ResizeArray<ParseWarning>()

        // Collect top-level state elements
        let stateResults =
            root.Elements()
            |> Seq.filter isStateElement
            |> Seq.map (parseState warnings)
            |> Seq.toList

        let stateNodes = stateResults |> List.map (fun (n, _, _) -> n)
        let allTransitions = stateResults |> List.collect (fun (_, t, _) -> t)
        let stateDataEntries = stateResults |> List.collect (fun (_, _, d) -> d)

        // Initial state inference
        let initialId =
            match attrValue "initial" root with
            | Some id -> Some id
            | None ->
                // Per W3C section 3.2: use first child state's id
                stateNodes
                |> List.tryHead
                |> Option.map (fun s -> s.Identifier)

        // Collect warnings for unknown root children
        collectRootWarnings warnings root

        // Parse document-level <datamodel>/<data> entries
        let docDataEntries = parseDataEntries root

        // Combine document-level + state-scoped data entries (flattened)
        let allDataEntries = docDataEntries @ stateDataEntries

        // Build Elements list (states first, then transitions -- matching Mapper order)
        let stateElements = stateNodes |> List.map StateDecl
        let transitionElements = allTransitions |> List.map TransitionElement
        let elements = stateElements @ transitionElements

        // Build document-level annotations
        let docAnnotations =
            [ match attrValue "datamodel" root with
              | Some dm -> yield ScxmlAnnotation(ScxmlDatamodelType(dm))
              | None -> ()
              match attrValue "binding" root with
              | Some b -> yield ScxmlAnnotation(ScxmlBinding(b))
              | None -> () ]

        // Build the StatechartDocument
        let doc =
            { Title = attrValue "name" root
              InitialStateId = initialId
              Elements = elements
              DataEntries = allDataEntries
              Annotations = docAnnotations }

        { Document = doc
          Errors = []
          Warnings = warnings |> Seq.toList }

// SECURITY: XDocument.Parse/Load in .NET 8+ prohibits DTD processing by default
// (DtdProcessing.Prohibit, XmlResolver = null). XXE and billion-laughs attacks
// are mitigated without explicit configuration. See: https://learn.microsoft.com/en-us/dotnet/standard/linq/linq-xml-security

// Shared error-handling wrapper for all parse entry points
let private tryParseWith (loadFn: unit -> XDocument) : ParseResult =
    try
        loadFn () |> parseDocument
    with :? XmlException as ex ->
        { Document =
            { Title = None
              InitialStateId = None
              Elements = []
              DataEntries = []
              Annotations = [] }
          Errors =
            [ { Position = Some { Line = ex.LineNumber; Column = ex.LinePosition }
                Description = ex.Message
                Expected = ""; Found = ""; CorrectiveExample = "" } ]
          Warnings = [] }

// Parse SCXML from a string
let parseString (xml: string) : ParseResult =
    tryParseWith (fun () -> XDocument.Parse(xml, LoadOptions.SetLineInfo))

// Parse SCXML from a TextReader (caller owns lifetime)
let parseReader (reader: System.IO.TextReader) : ParseResult =
    tryParseWith (fun () -> XDocument.Load(reader, LoadOptions.SetLineInfo))

// Parse SCXML from a Stream (caller owns lifetime)
let parseStream (stream: System.IO.Stream) : ParseResult =
    tryParseWith (fun () -> XDocument.Load(stream, LoadOptions.SetLineInfo))
