module Frank.Statecharts.Tests.Alps.XmlGeneratorTests

open System.Xml.Linq
open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Alps.Classification
open Frank.Statecharts.Alps.XmlGenerator
open Frank.Statecharts.Alps.XmlParser
open Frank.Statecharts.Alps.JsonParser
open Frank.Statecharts.Tests.Alps.GoldenFiles

// ---------------------------------------------------------------------------
// Helpers: navigate generated XDocument
// ---------------------------------------------------------------------------

/// Parse generated XML into an XDocument (fails test on invalid XML).
let private parseXml (xml: string) = XDocument.Parse(xml)

/// Get the <alps> root element from a parsed document.
let private alpsRoot (xdoc: XDocument) = xdoc.Root

/// Get direct child <descriptor> elements of a parent.
let private descriptors (parent: XElement) =
    parent.Elements(XName.Get "descriptor") |> Seq.toList

/// Get direct child <link> elements of a parent.
let private links (parent: XElement) =
    parent.Elements(XName.Get "link") |> Seq.toList

/// Get direct child <ext> elements of a parent.
let private exts (parent: XElement) =
    parent.Elements(XName.Get "ext") |> Seq.toList

/// Get direct child <doc> elements of a parent.
let private docs (parent: XElement) =
    parent.Elements(XName.Get "doc") |> Seq.toList

/// Read an attribute value (returns empty string if absent).
let private attr (name: string) (el: XElement) =
    let a = el.Attribute(XName.Get name)
    if a = null then "" else a.Value

// ---------------------------------------------------------------------------
// Basic generation tests
// ---------------------------------------------------------------------------

[<Tests>]
let xmlGeneratorTests =
    testList
        "Alps.XmlGenerator"
        [ testCase "generates valid XML with alps root element"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsVersion "1.0") ] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              Expect.equal xdoc.Root.Name.LocalName "alps" "root element is alps"

          testCase "alps root has no namespace"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              Expect.equal xdoc.Root.Name.NamespaceName "" "alps element has no namespace"

          testCase "version attribute is written correctly"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsVersion "1.0") ] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              Expect.equal (attr "version" xdoc.Root) "1.0" "version attribute preserved"

          testCase "defaults to version 1.0 when no version annotation"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              Expect.equal (attr "version" xdoc.Root) "1.0" "defaults to version 1.0"

          testCase "empty document produces minimal alps XML with no child elements"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let root = alpsRoot xdoc
              Expect.isEmpty (descriptors root) "no descriptor elements"
              Expect.isEmpty (links root) "no link elements"
              Expect.isEmpty (exts root) "no ext elements"
              Expect.isEmpty (docs root) "no doc elements"

          testCase "state descriptor has id and type=semantic"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "myState"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let descs = descriptors xdoc.Root
              Expect.equal descs.Length 1 "one descriptor"
              Expect.equal (attr "id" descs.[0]) "myState" "id attribute"
              Expect.equal (attr "type" descs.[0]) "semantic" "type is semantic"

          testCase "transition descriptor has correct type attribute"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "A"
                              Event = Some "doSafe"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Safe) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let stateDesc = (descriptors xdoc.Root).[0]
              let transDescs = descriptors stateDesc
              Expect.equal transDescs.Length 1 "one transition"
              Expect.equal (attr "type" transDescs.[0]) "safe" "transition type safe"

          testCase "all three transition types produce correct XML attributes"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "A"
                              Event = Some "safe_t"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Safe) ] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "A"
                              Event = Some "unsafe_t"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe) ] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "A"
                              Event = Some "idempotent_t"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Idempotent) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let transDescs = descriptors (descriptors xdoc.Root).[0]
              Expect.equal (attr "type" transDescs.[0]) "safe" "safe"
              Expect.equal (attr "type" transDescs.[1]) "unsafe" "unsafe"
              Expect.equal (attr "type" transDescs.[2]) "idempotent" "idempotent"

          testCase "defaults to unsafe when no AlpsTransitionType annotation"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "A"
                              Event = Some "doThing"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let transDesc = (descriptors (descriptors xdoc.Root).[0]).[0]
              Expect.equal (attr "type" transDesc) "unsafe" "defaults to unsafe"

          testCase "rt attribute gets # prefix for local references"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        StateDecl
                            { Identifier = Some "B"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "B"
                              Event = Some "go"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Safe) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generateAlpsXml doc

              Expect.isTrue
                  (xml.Contains("\"#B\"") || xml.Contains("'#B'") || xml.Contains("#B"))
                  "rt target has # prefix"

              let xdoc = parseXml xml
              let transDesc = (descriptors (descriptors xdoc.Root).[0]).[0]
              Expect.equal (attr "rt" transDesc) "#B" "rt attribute value"

          testCase "absolute URL rt values are not prefixed"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "http://example.com/other"
                              Event = Some "go"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Safe) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let transDesc = (descriptors (descriptors xdoc.Root).[0]).[0]
              Expect.equal (attr "rt" transDesc) "http://example.com/other" "absolute URL rt not prefixed" ]

// ---------------------------------------------------------------------------
// Documentation annotation tests
// ---------------------------------------------------------------------------

[<Tests>]
let xmlGeneratorDocTests =
    testList
        "Alps.XmlGenerator.Documentation"
        [ testCase "document-level documentation produces <doc> element"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsDocumentation(Some "text", "A description")) ] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let docEls = docs xdoc.Root
              Expect.equal docEls.Length 1 "one doc element"
              Expect.equal (attr "format" docEls.[0]) "text" "format attribute"
              Expect.equal docEls.[0].Value "A description" "doc content"

          testCase "documentation without format omits format attribute"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsDocumentation(None, "Plain text")) ] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let docEls = docs xdoc.Root
              Expect.equal docEls.Length 1 "one doc element"
              Expect.isNull (docEls.[0].Attribute(XName.Get "format")) "no format attribute"
              Expect.equal docEls.[0].Value "Plain text" "doc content"

          testCase "state-level documentation produces <doc> child element"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "Home"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsDocumentation(Some "html", "Home <b>page</b>")) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let stateDesc = (descriptors xdoc.Root).[0]
              let stateDocEls = docs stateDesc
              Expect.equal stateDocEls.Length 1 "one doc element on state"
              Expect.equal (attr "format" stateDocEls.[0]) "html" "state doc format"
              Expect.equal stateDocEls.[0].Value "Home <b>page</b>" "state doc content"

          testCase "transition-level documentation produces <doc> child element"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "A"
                              Event = Some "go"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations =
                                [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Safe)
                                  AlpsAnnotation(AlpsDocumentation(None, "Do a thing")) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let transDesc = (descriptors (descriptors xdoc.Root).[0]).[0]
              let transDocEls = docs transDesc
              Expect.equal transDocEls.Length 1 "one doc element on transition"
              Expect.equal transDocEls.[0].Value "Do a thing" "transition doc content" ]

// ---------------------------------------------------------------------------
// Extension and link annotation tests
// ---------------------------------------------------------------------------

[<Tests>]
let xmlGeneratorExtLinkTests =
    testList
        "Alps.XmlGenerator.ExtLinks"
        [ testCase "document-level link produces <link> element"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsLink("self", "http://example.com/alps/test")) ] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let linkEls = links xdoc.Root
              Expect.equal linkEls.Length 1 "one link element"
              Expect.equal (attr "rel" linkEls.[0]) "self" "link rel"
              Expect.equal (attr "href" linkEls.[0]) "http://example.com/alps/test" "link href"

          testCase "document-level ext produces <ext> element"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsExtension("custom", None, Some "data")) ] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let extEls = exts xdoc.Root
              Expect.equal extEls.Length 1 "one ext element"
              Expect.equal (attr "id" extEls.[0]) "custom" "ext id"
              Expect.equal (attr "value" extEls.[0]) "data" "ext value"

          testCase "ext with href produces href attribute"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsExtension("myext", Some "http://example.com/ext", Some "val")) ] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let extEls = exts xdoc.Root
              Expect.equal (attr "href" extEls.[0]) "http://example.com/ext" "ext href"

          testCase "guard produces <ext id=guard value=...> on transition"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "A"
                              Event = Some "go"
                              Guard = Some "role=admin"
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let transDesc = (descriptors (descriptors xdoc.Root).[0]).[0]
              let extEls = exts transDesc
              Expect.equal extEls.Length 1 "one ext on transition"
              Expect.equal (attr "id" extEls.[0]) GuardExtId "ext id is guard"
              Expect.equal (attr "value" extEls.[0]) "role=admin" "guard value" ]

// ---------------------------------------------------------------------------
// Parameter and data descriptor tests
// ---------------------------------------------------------------------------

[<Tests>]
let xmlGeneratorParamTests =
    testList
        "Alps.XmlGenerator.Parameters"
        [ testCase "transition parameters produce href-only child descriptors"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "A"
                              Event = Some "submit"
                              Guard = None
                              Action = None
                              Parameters = [ "name"; "email" ]
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let transDesc = (descriptors (descriptors xdoc.Root).[0]).[0]
              let paramDescs = descriptors transDesc
              Expect.equal paramDescs.Length 2 "two parameter descriptors"
              Expect.equal (attr "href" paramDescs.[0]) "#name" "first param href"
              Expect.equal (attr "href" paramDescs.[1]) "#email" "second param href"

          testCase "data descriptor produces semantic descriptor at top level"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsDataDescriptor("myField", None)) ] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let descs = descriptors xdoc.Root
              Expect.equal descs.Length 1 "one descriptor"
              Expect.equal (attr "id" descs.[0]) "myField" "data descriptor id"
              Expect.equal (attr "type" descs.[0]) "semantic" "data descriptor type"

          testCase "data descriptor with doc produces <doc> child"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsDataDescriptor("email", Some(Some "text", "Email address"))) ] }

              let xml = generateAlpsXml doc
              let xdoc = parseXml xml
              let dataDesc = (descriptors xdoc.Root).[0]
              let docEls = docs dataDesc
              Expect.equal docEls.Length 1 "one doc on data descriptor"
              Expect.equal docEls.[0].Value "Email address" "data descriptor doc content" ]

// ---------------------------------------------------------------------------
// Round-trip tests: structural verification and true AST round-trips
// ---------------------------------------------------------------------------

[<Tests>]
let xmlGeneratorRoundTripTests =
    testList
        "Alps.XmlGenerator.RoundTrip"
        [ testCase "XML structure round-trip: home->go minimal statechart"
          <| fun _ ->
              // Build a minimal AST equivalent to:
              // <alps version="1.0">
              //   <descriptor id="home" type="semantic"><descriptor href="#go"/></descriptor>
              //   <descriptor id="go" type="unsafe" rt="#home"/>
              // </alps>
              //
              // (This is the minimal example from the spec.)
              // Since we don't have parseAlpsXml in WP04, we generate and verify XDocument structure.
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "home"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "home"
                              Target = Some "home"
                              Event = Some "go"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe) ] } ]
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsVersion "1.0") ] }

              let xml1 = generateAlpsXml doc
              let xdoc1 = parseXml xml1

              // Parse the generated XML using XDocument (no parseAlpsXml available)
              // and rebuild an equivalent doc from the element structure, then generate again.
              // Verify both generations produce structurally equivalent XML.
              let xml2 = generateAlpsXml doc
              let xdoc2 = parseXml xml2

              // Both should have same structure (deterministic generation)
              Expect.equal (xdoc1.ToString()) (xdoc2.ToString()) "generation is deterministic"

              // Verify structure of the generated XML
              let root = xdoc1.Root
              Expect.equal (attr "version" root) "1.0" "version preserved"
              let descs = descriptors root
              Expect.equal descs.Length 1 "one top-level descriptor (home state)"
              Expect.equal (attr "id" descs.[0]) "home" "home state id"
              Expect.equal (attr "type" descs.[0]) "semantic" "home state type"

              // home has one child transition to go
              let homeChildren = descriptors descs.[0]
              Expect.equal homeChildren.Length 1 "home has one child descriptor"
              Expect.equal (attr "id" homeChildren.[0]) "go" "transition id"
              Expect.equal (attr "type" homeChildren.[0]) "unsafe" "transition type"
              Expect.equal (attr "rt" homeChildren.[0]) "#home" "transition rt"

          testCase "tic-tac-toe JSON AST generates structurally correct XML"
          <| fun _ ->
              let jsonResult = parseAlpsJson ticTacToeAlpsJson
              Expect.isEmpty jsonResult.Errors "parse failed"
              let astDoc = jsonResult.Document

              let xml = generateAlpsXml astDoc
              let xdoc = parseXml xml

              // Verify top-level structure
              let root = xdoc.Root
              Expect.equal (attr "version" root) "1.0" "version"

              // Should have doc element
              let docEls = docs root
              Expect.isNonEmpty docEls "should have doc element"

              // Should have descriptor elements (data + states + shared transitions)
              let descs = descriptors root
              Expect.isNonEmpty descs "should have descriptor elements"

              // Should have link element
              let linkEls = links root
              Expect.isNonEmpty linkEls "should have link element"
              Expect.equal (attr "rel" linkEls.[0]) "self" "link rel"
              Expect.equal (attr "href" linkEls.[0]) "http://example.com/alps/tic-tac-toe" "link href"

          testCase "onboarding JSON AST generates structurally correct XML"
          <| fun _ ->
              let jsonResult = parseAlpsJson onboardingAlpsJson
              Expect.isEmpty jsonResult.Errors "parse failed"
              let astDoc = jsonResult.Document

              let xml = generateAlpsXml astDoc
              let xdoc = parseXml xml

              let root = xdoc.Root

              // Should have version
              Expect.equal (attr "version" root) "1.0" "version"

              // Should have descriptors
              let descs = descriptors root
              Expect.isNonEmpty descs "should have descriptor elements"

              // Should have a doc element
              Expect.isNonEmpty (docs root) "should have doc element"

              // No links in onboarding
              Expect.isEmpty (links root) "onboarding has no links"

          testCase "generation is deterministic across multiple calls"
          <| fun _ ->
              let jsonResult = parseAlpsJson ticTacToeAlpsJson
              Expect.isEmpty jsonResult.Errors "parse failed"
              let astDoc = jsonResult.Document

              let xml1 = generateAlpsXml astDoc
              let xml2 = generateAlpsXml astDoc
              let xml3 = generateAlpsXml astDoc

              Expect.equal xml1 xml2 "first two calls identical"
              Expect.equal xml2 xml3 "second two calls identical"

          testCase "generated XML is well-formed (parseable as XDocument)"
          <| fun _ ->
              let jsonResult = parseAlpsJson ticTacToeAlpsJson
              Expect.isEmpty jsonResult.Errors "JSON parse failed"
              let astDoc = jsonResult.Document

              let xml = generateAlpsXml astDoc
              // If this doesn't throw, XML is well-formed
              let xdoc = XDocument.Parse(xml)
              Expect.equal xdoc.Root.Name.LocalName "alps" "root is alps"

          testCase "generated XML preserves state count from JSON AST"
          <| fun _ ->
              let jsonResult = parseAlpsJson onboardingAlpsJson
              Expect.isEmpty jsonResult.Errors "parse failed"
              let astDoc = jsonResult.Document

              // Count state descriptors in the AST
              let stateCount =
                  astDoc.Elements
                  |> List.filter (fun el ->
                      match el with
                      | StateDecl _ -> true
                      | _ -> false)
                  |> List.length

              let xml = generateAlpsXml astDoc
              let xdoc = parseXml xml

              // Count descriptors that have type=semantic and an id (states, not data descriptors)
              // Data descriptors from doc.Annotations also appear — we count all top-level id-bearing descriptors
              let topLevelDescriptors = descriptors xdoc.Root

              let stateDescs =
                  topLevelDescriptors
                  |> List.filter (fun d -> attr "type" d = "semantic" && attr "id" d <> "")

              // The number of state+data descriptors in XML >= stateCount
              // (data descriptors from annotations appear before states)
              Expect.isGreaterThanOrEqual
                  stateDescs.Length
                  stateCount
                  "XML has at least as many semantic descriptors as AST states"

          // True round-trip tests (now that XmlParser is available on master)
          testCase "true XML round-trip: generate then parse produces equal AST"
          <| fun _ ->
              let json =
                  """{"alps":{"version":"1.0","descriptor":[{"id":"home","type":"semantic","descriptor":[{"href":"#go"}]},{"id":"go","type":"unsafe","rt":"#home"}]}}"""

              let ast = (parseAlpsJson json).Document
              let xml = generateAlpsXml ast
              let reparsed = parseAlpsXml xml
              Expect.isEmpty reparsed.Errors "re-parse succeeds"
              Expect.equal ast reparsed.Document "AST → XML → AST round-trip"

          testCase "true XML round-trip: tic-tac-toe golden file"
          <| fun _ ->
              let ast = (parseAlpsJson ticTacToeAlpsJson).Document
              let xml = generateAlpsXml ast
              let reparsed = parseAlpsXml xml
              Expect.isEmpty reparsed.Errors "re-parse succeeds"
              Expect.equal ast reparsed.Document "tic-tac-toe AST → XML → AST round-trip"

          testCase "true XML round-trip: onboarding golden file"
          <| fun _ ->
              let ast = (parseAlpsJson amundsenOnboardingAlpsJson).Document
              let xml = generateAlpsXml ast
              let reparsed = parseAlpsXml xml
              Expect.isEmpty reparsed.Errors "re-parse succeeds"
              Expect.equal ast reparsed.Document "onboarding AST → XML → AST round-trip" ]
