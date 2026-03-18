module internal Frank.Statecharts.Alps.XmlParser

open System.Xml
open System.Xml.Linq
open Frank.Statecharts.Ast
open Frank.Statecharts.Alps.Classification

// ---------------------------------------------------------------------------
// Pass 1: XML to intermediate records
// ---------------------------------------------------------------------------

/// Get an XML attribute value as string option.
let private attrValue (name: string) (elem: XElement) : string option =
    let attr = elem.Attribute(XName.Get name)
    if attr = null then None else Some attr.Value

/// Parse an ALPS <ext> element to intermediate type.
let private parseExtension (elem: XElement) : ParsedExtension option =
    match attrValue "id" elem with
    | None -> None
    | Some id ->
        Some
            { Id = id
              Href = attrValue "href" elem
              Value = attrValue "value" elem }

/// Parse an ALPS <link> element to intermediate type.
let private parseLink (elem: XElement) : ParsedLink option =
    match attrValue "rel" elem, attrValue "href" elem with
    | Some rel, Some href -> Some { Rel = rel; Href = href }
    | _ -> None

/// Parse a <doc> child element to (format option * value option).
/// In ALPS XML, <doc format="text">value text</doc> — format is attribute, value is text content.
let private parseDocElement (parent: XElement) : string option * string option =
    let docElem =
        parent.Elements()
        |> Seq.tryFind (fun e -> e.Name.LocalName = "doc")

    match docElem with
    | None -> None, None
    | Some doc ->
        let fmt = attrValue "format" doc
        let value = doc.Value

        if System.String.IsNullOrEmpty value then
            fmt, None
        else
            fmt, Some value

/// Parse a single <descriptor> element to intermediate type, recursively parsing nested children.
let rec private parseDescriptor (elem: XElement) : ParsedDescriptor =
    let docFormat, docValue = parseDocElement elem

    let children =
        elem.Elements()
        |> Seq.filter (fun e -> e.Name.LocalName = "descriptor")
        |> Seq.map parseDescriptor
        |> Seq.toList

    let extensions =
        elem.Elements()
        |> Seq.filter (fun e -> e.Name.LocalName = "ext")
        |> Seq.choose parseExtension
        |> Seq.toList

    let links =
        elem.Elements()
        |> Seq.filter (fun e -> e.Name.LocalName = "link")
        |> Seq.choose parseLink
        |> Seq.toList

    { Id = attrValue "id" elem
      Type = attrValue "type" elem
      Href = attrValue "href" elem
      ReturnType = attrValue "rt" elem
      DocFormat = docFormat
      DocValue = docValue
      Children = children
      Extensions = extensions
      Links = links }

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/// An empty StatechartDocument used as best-effort in error cases.
let private emptyDoc : StatechartDocument =
    { Title = None
      InitialStateId = None
      Elements = []
      DataEntries = []
      Annotations = [] }

/// Parse an ALPS XML document into a shared AST ParseResult.
let parseAlpsXml (xml: string) : ParseResult =
    try
        let xdoc = XDocument.Parse(xml)
        let root = xdoc.Root

        if root = null || root.Name.LocalName <> "alps" then
            { Document = emptyDoc
              Errors =
                  [ { Position = None
                      Description = "Expected root element <alps>, got " + (if root = null then "null" else root.Name.LocalName)
                      Expected = "<alps> root element"
                      Found = if root = null then "null" else root.Name.LocalName
                      CorrectiveExample = """<alps version="1.0"><descriptor id="StateA" type="semantic"/></alps>""" } ]
              Warnings = [] }
        else
            // -- Pass 1: Parse XML to intermediate records --
            let version = attrValue "version" root

            let rootDocFormat, rootDocValue = parseDocElement root

            let rootLinks =
                root.Elements()
                |> Seq.filter (fun e -> e.Name.LocalName = "link")
                |> Seq.choose parseLink
                |> Seq.toList

            let rootExtensions =
                root.Elements()
                |> Seq.filter (fun e -> e.Name.LocalName = "ext")
                |> Seq.choose parseExtension
                |> Seq.toList

            let descriptors =
                root.Elements()
                |> Seq.filter (fun e -> e.Name.LocalName = "descriptor")
                |> Seq.map parseDescriptor
                |> Seq.toList

            // -- Pass 2: Classify descriptors and build StatechartDocument --
            let statechartDoc =
                classifyDescriptors descriptors version rootDocFormat rootDocValue rootLinks rootExtensions

            { Document = statechartDoc
              Errors = []
              Warnings = [] }

    with :? XmlException as ex ->
        { Document = emptyDoc
          Errors =
              [ { Position = None
                  Description = ex.Message
                  Expected = "valid ALPS XML"
                  Found = "malformed XML"
                  CorrectiveExample = """<alps version="1.0"><descriptor id="StateA" type="semantic"/></alps>""" } ]
          Warnings = [] }
