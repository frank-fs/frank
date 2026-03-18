module Frank.Statecharts.Tests.Alps.XmlParserTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Alps.XmlParser
open Frank.Statecharts.Alps.JsonParser
open Frank.Statecharts.Tests.Alps.GoldenFiles

// ---------------------------------------------------------------------------
// Helpers (mirrors JsonParserTests helpers)
// ---------------------------------------------------------------------------

let private getStates (doc: StatechartDocument) =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | StateDecl s -> Some s
        | _ -> None)

let private getTransitions (doc: StatechartDocument) =
    doc.Elements
    |> List.choose (fun el ->
        match el with
        | TransitionElement t -> Some t
        | _ -> None)

let private getVersion (doc: StatechartDocument) =
    doc.Annotations
    |> List.tryPick (fun a ->
        match a with
        | AlpsAnnotation(AlpsVersion v) -> Some v
        | _ -> None)

let private getDocumentation (doc: StatechartDocument) =
    doc.Annotations
    |> List.tryPick (fun a ->
        match a with
        | AlpsAnnotation(AlpsDocumentation(fmt, value)) -> Some(fmt, value)
        | _ -> None)

let private getLinks (doc: StatechartDocument) =
    doc.Annotations
    |> List.choose (fun a ->
        match a with
        | AlpsAnnotation(AlpsLink(rel, href)) -> Some(rel, href)
        | _ -> None)

let private getExtensions (doc: StatechartDocument) =
    doc.Annotations
    |> List.choose (fun a ->
        match a with
        | AlpsAnnotation(AlpsExtension(id, href, value)) -> Some(id, href, value)
        | _ -> None)

let private getDataDescriptors (doc: StatechartDocument) =
    doc.Annotations
    |> List.choose (fun a ->
        match a with
        | AlpsAnnotation(AlpsDataDescriptor(id, _)) -> Some id
        | _ -> None)

/// Helper: parse XML and get document (assert no errors).
let private parseOk xml msg =
    let result = parseAlpsXml xml
    Expect.isEmpty result.Errors msg
    result.Document

// ---------------------------------------------------------------------------
// Basic parsing tests
// ---------------------------------------------------------------------------

[<Tests>]
let xmlParserBasicTests =
    testList
        "Alps.XmlParser basic"
        [ testCase "parse basic ALPS XML with descriptors succeeds"
          <| fun _ ->
              let xml =
                  """<alps version="1.0"><descriptor id="StateA" type="semantic"><descriptor id="go" type="safe" rt="#StateA"/></descriptor></alps>"""

              let result = parseAlpsXml xml
              Expect.isEmpty result.Errors "should parse without errors"
              let doc = result.Document
              let states = getStates doc
              Expect.equal states.Length 1 "one state"
              Expect.equal states.[0].Identifier (Some "StateA") "state id"

          testCase "version attribute is captured"
          <| fun _ ->
              let xml = """<alps version="1.0"></alps>"""
              let doc = parseOk xml "parse failed"
              Expect.equal (getVersion doc) (Some "1.0") "version"

          testCase "parse XML with documentation"
          <| fun _ ->
              let xml =
                  """<alps version="1.0"><doc format="text">My description</doc></alps>"""

              let doc = parseOk xml "parse failed"
              let documentation = getDocumentation doc
              Expect.isSome documentation "doc present"
              let (fmt, value) = documentation.Value
              Expect.equal fmt (Some "text") "format"
              Expect.equal value "My description" "value"

          testCase "parse XML with doc element without format attribute"
          <| fun _ ->
              let xml = """<alps><doc>Just some text</doc></alps>"""
              let doc = parseOk xml "parse failed"
              let documentation = getDocumentation doc
              Expect.isSome documentation "doc present"
              let (fmt, value) = documentation.Value
              Expect.isNone fmt "no format"
              Expect.equal value "Just some text" "doc text"

          testCase "parse XML with link elements"
          <| fun _ ->
              let xml =
                  """<alps version="1.0"><link rel="self" href="http://example.com"/></alps>"""

              let doc = parseOk xml "parse failed"
              let links = getLinks doc
              Expect.equal links.Length 1 "one link"
              Expect.equal (fst links.[0]) "self" "link rel"
              Expect.equal (snd links.[0]) "http://example.com" "link href"

          testCase "parse XML with ext elements"
          <| fun _ ->
              let xml =
                  """<alps version="1.0"><ext id="custom" value="data"/></alps>"""

              let doc = parseOk xml "parse failed"
              let exts = getExtensions doc
              Expect.equal exts.Length 1 "one top-level ext"
              let (id, _, value) = exts.[0]
              Expect.equal id "custom" "ext id"
              Expect.equal value (Some "data") "ext value"

          testCase "empty ALPS document (no descriptors)"
          <| fun _ ->
              let xml = """<alps><descriptor/></alps>"""
              let doc = parseOk xml "parse failed"
              Expect.isEmpty (getStates doc) "no states"
              Expect.isEmpty (getTransitions doc) "no transitions"

          testCase "ALPS document with no children"
          <| fun _ ->
              let xml = """<alps/>"""
              let doc = parseOk xml "parse failed"
              Expect.isEmpty (getStates doc) "no states"
              Expect.isEmpty (getTransitions doc) "no transitions" ]

// ---------------------------------------------------------------------------
// Nested descriptor tests
// ---------------------------------------------------------------------------

[<Tests>]
let xmlParserNestedTests =
    testList
        "Alps.XmlParser nested descriptors"
        [ testCase "nested transition descriptor extracted"
          <| fun _ ->
              let xml =
                  """<alps version="1.0"><descriptor id="StateA" type="semantic"><descriptor id="go" type="unsafe" rt="#StateB"/></descriptor><descriptor id="StateB" type="semantic"/></alps>"""

              let doc = parseOk xml "parse failed"
              let transitions = getTransitions doc
              Expect.equal transitions.Length 1 "one transition"
              Expect.equal transitions.[0].Source "StateA" "source"
              Expect.equal transitions.[0].Target (Some "StateB") "target"
              Expect.equal transitions.[0].Event (Some "go") "event"

          testCase "descriptor with href-only child (shared transition reference)"
          <| fun _ ->
              let xml =
                  """<alps version="1.0">
  <descriptor id="StateA" type="semantic">
    <descriptor href="#go"/>
  </descriptor>
  <descriptor id="go" type="safe" rt="#StateA"/>
</alps>"""

              let doc = parseOk xml "parse failed"
              let transitions = getTransitions doc
              let viewFromA = transitions |> List.tryFind (fun t -> t.Source = "StateA" && t.Event = Some "go")
              Expect.isSome viewFromA "StateA has go transition via href"

          testCase "href-only descriptor produces AlpsDescriptorHref annotation"
          <| fun _ ->
              let xml =
                  """<alps version="1.0">
  <descriptor id="StateA" type="semantic">
    <descriptor href="#go"/>
  </descriptor>
  <descriptor id="go" type="safe" rt="#StateA"/>
</alps>"""

              let doc = parseOk xml "parse failed"
              let transitions = getTransitions doc
              let goFromA = transitions |> List.find (fun t -> t.Source = "StateA" && t.Event = Some "go")

              let hasDescriptorHref =
                  goFromA.Annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsDescriptorHref _) -> true
                      | _ -> false)

              Expect.isTrue hasDescriptorHref "href-only ref has AlpsDescriptorHref annotation"

          testCase "deeply nested state descriptor with transition child"
          <| fun _ ->
              let xml =
                  """<alps version="1.0"><descriptor id="level1" type="semantic"><descriptor id="go" type="safe" rt="#level1"/></descriptor></alps>"""

              let doc = parseOk xml "parse failed"
              let states = getStates doc
              Expect.equal states.Length 1 "one state"
              Expect.equal states.[0].Identifier (Some "level1") "level1 is a state" ]

// ---------------------------------------------------------------------------
// Transition annotation tests
// ---------------------------------------------------------------------------

[<Tests>]
let xmlParserTransitionTests =
    testList
        "Alps.XmlParser transitions"
        [ testCase "safe type maps to Safe annotation"
          <| fun _ ->
              let xml =
                  """<alps version="1.0"><descriptor id="StateA" type="semantic"><descriptor id="view" type="safe" rt="#StateA"/></descriptor></alps>"""

              let doc = parseOk xml "parse failed"
              let view = getTransitions doc |> List.find (fun t -> t.Event = Some "view")

              let hasSafe =
                  view.Annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Safe) -> true
                      | _ -> false)

              Expect.isTrue hasSafe "view has Safe annotation"

          testCase "unsafe type maps to Unsafe annotation"
          <| fun _ ->
              let xml =
                  """<alps version="1.0"><descriptor id="StateA" type="semantic"><descriptor id="submit" type="unsafe" rt="#StateA"/></descriptor></alps>"""

              let doc = parseOk xml "parse failed"
              let submit = getTransitions doc |> List.find (fun t -> t.Event = Some "submit")

              let hasUnsafe =
                  submit.Annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe) -> true
                      | _ -> false)

              Expect.isTrue hasUnsafe "submit has Unsafe annotation"

          testCase "idempotent type maps to Idempotent annotation"
          <| fun _ ->
              let xml =
                  """<alps version="1.0"><descriptor id="StateA" type="semantic"><descriptor id="update" type="idempotent" rt="#StateA"/></descriptor></alps>"""

              let doc = parseOk xml "parse failed"
              let update = getTransitions doc |> List.find (fun t -> t.Event = Some "update")

              let hasIdempotent =
                  update.Annotations
                  |> List.exists (fun a ->
                      match a with
                      | AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Idempotent) -> true
                      | _ -> false)

              Expect.isTrue hasIdempotent "update has Idempotent annotation"

          testCase "guard extracted from ext element"
          <| fun _ ->
              let xml =
                  """<alps version="1.0"><descriptor id="StateA" type="semantic"><descriptor id="go" type="unsafe" rt="#StateA"><ext id="guard" value="role=X"/></descriptor></descriptor></alps>"""

              let doc = parseOk xml "parse failed"
              let go = getTransitions doc |> List.find (fun t -> t.Event = Some "go")
              Expect.equal go.Guard (Some "role=X") "guard captured"

          testCase "parameters extracted from href-only children"
          <| fun _ ->
              let xml =
                  """<alps version="1.0">
  <descriptor id="email" type="semantic"/>
  <descriptor id="StateA" type="semantic">
    <descriptor id="submit" type="unsafe" rt="#StateA">
      <descriptor href="#email"/>
    </descriptor>
  </descriptor>
</alps>"""

              let doc = parseOk xml "parse failed"
              let submit = getTransitions doc |> List.find (fun t -> t.Event = Some "submit")
              Expect.equal submit.Parameters.Length 1 "one parameter"
              Expect.equal submit.Parameters.[0] "email" "param is email" ]

// ---------------------------------------------------------------------------
// Error handling tests
// ---------------------------------------------------------------------------

[<Tests>]
let xmlParserErrorTests =
    testList
        "Alps.XmlParser errors"
        [ testCase "malformed XML returns errors"
          <| fun _ ->
              let result = parseAlpsXml "not valid xml"
              Expect.isNonEmpty result.Errors "should have errors"

          testCase "empty string returns errors"
          <| fun _ ->
              let result = parseAlpsXml ""
              Expect.isNonEmpty result.Errors "should have errors"

          testCase "wrong root element returns errors"
          <| fun _ ->
              let result = parseAlpsXml """<root><descriptor id="foo"/></root>"""
              Expect.isNonEmpty result.Errors "should have errors for wrong root"

          testCase "error description is actionable"
          <| fun _ ->
              let result = parseAlpsXml "not valid xml"
              Expect.isNonEmpty result.Errors "should have errors"
              Expect.isNotEmpty result.Errors.[0].Description "error description not empty"

          testCase "wrong root element error mentions alps"
          <| fun _ ->
              let result = parseAlpsXml "<foo/>"
              Expect.isNonEmpty result.Errors "should have errors"
              Expect.stringContains result.Errors.[0].Description "alps" "mentions alps" ]

// ---------------------------------------------------------------------------
// Golden file tests
// ---------------------------------------------------------------------------

[<Tests>]
let xmlParserGoldenTests =
    testList
        "Alps.XmlParser golden files"
        [ testCase "parse tic-tac-toe XML golden file succeeds"
          <| fun _ ->
              let result = parseAlpsXml ticTacToeAlpsXml
              Expect.isEmpty result.Errors "should parse without errors"
              let doc = result.Document
              Expect.equal (getVersion doc) (Some "1.0") "version"
              Expect.isSome (getDocumentation doc) "should have documentation"
              let docFmt, docVal = (getDocumentation doc).Value
              Expect.equal docVal "Tic-Tac-Toe game state machine" "doc value"
              Expect.equal docFmt (Some "text") "doc format"

          testCase "tic-tac-toe XML has state elements"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsXml "parse failed"
              let stateIds = getStates doc |> List.choose (fun s -> s.Identifier) |> Set.ofList

              Expect.containsAll
                  stateIds
                  (Set.ofList [ "XTurn"; "OTurn"; "Won"; "Draw" ])
                  "all state descriptors present"

          testCase "tic-tac-toe XML has data descriptor annotations"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsXml "parse failed"
              let dataIds = getDataDescriptors doc |> Set.ofList

              Expect.containsAll
                  dataIds
                  (Set.ofList [ "position"; "player" ])
                  "data descriptors present as annotations"

          testCase "tic-tac-toe XML makeMove has correct target"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsXml "parse failed"

              let makeMove =
                  getTransitions doc
                  |> List.find (fun t -> t.Event = Some "makeMove" && t.Source = "XTurn" && t.Target = Some "OTurn")

              Expect.equal makeMove.Target (Some "OTurn") "first makeMove target is OTurn"

          testCase "tic-tac-toe XML XTurn has three makeMove transitions"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsXml "parse failed"

              let makeMoves =
                  getTransitions doc
                  |> List.filter (fun t -> t.Event = Some "makeMove" && t.Source = "XTurn")

              Expect.equal makeMoves.Length 3 "XTurn has 3 makeMove transitions"

          testCase "tic-tac-toe XML guards are captured"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsXml "parse failed"

              let makeMoves =
                  getTransitions doc
                  |> List.filter (fun t -> t.Event = Some "makeMove" && t.Source = "XTurn")

              let xToO = makeMoves |> List.find (fun t -> t.Target = Some "OTurn")
              Expect.equal xToO.Guard (Some "role=PlayerX") "guard value"

              let xToWon = makeMoves |> List.find (fun t -> t.Target = Some "Won")
              Expect.equal xToWon.Guard (Some "wins") "wins guard"

              let xToDraw = makeMoves |> List.find (fun t -> t.Target = Some "Draw")
              Expect.equal xToDraw.Guard (Some "boardFull") "boardFull guard"

          testCase "tic-tac-toe XML has link annotation"
          <| fun _ ->
              let doc = parseOk ticTacToeAlpsXml "parse failed"
              let links = getLinks doc
              Expect.equal links.Length 1 "one link"
              Expect.equal (fst links.[0]) "self" "link rel"
              Expect.equal (snd links.[0]) "http://example.com/alps/tic-tac-toe" "link href"

          testCase "parse onboarding XML golden file succeeds"
          <| fun _ ->
              let result = parseAlpsXml onboardingAlpsXml
              Expect.isEmpty result.Errors "should parse without errors"
              let doc = result.Document
              Expect.equal (getVersion doc) (Some "1.0") "version"
              Expect.isSome (getDocumentation doc) "should have documentation"

          testCase "onboarding XML has all state elements"
          <| fun _ ->
              let doc = parseOk onboardingAlpsXml "parse failed"
              let stateIds = getStates doc |> List.choose (fun s -> s.Identifier) |> Set.ofList

              Expect.containsAll
                  stateIds
                  (Set.ofList [ "Welcome"; "CollectEmail"; "CollectProfile"; "Review"; "Complete" ])
                  "all onboarding states present" ]

// ---------------------------------------------------------------------------
// Cross-format equivalence tests
// ---------------------------------------------------------------------------

[<Tests>]
let crossFormatEquivalenceTests =
    testList
        "Alps.XmlParser cross-format equivalence"
        [ testCase "minimal profile: JSON and XML produce identical ASTs"
          <| fun _ ->
              let json =
                  """{"alps":{"version":"1.0","descriptor":[{"id":"home","type":"semantic","descriptor":[{"href":"#go"}]},{"id":"go","type":"unsafe","rt":"#home"}]}}"""

              let xml =
                  """<alps version="1.0"><descriptor id="home" type="semantic"><descriptor href="#go"/></descriptor><descriptor id="go" type="unsafe" rt="#home"/></alps>"""

              let jsonResult = parseAlpsJson json
              let xmlResult = parseAlpsXml xml

              Expect.isEmpty jsonResult.Errors "JSON parse should succeed"
              Expect.isEmpty xmlResult.Errors "XML parse should succeed"
              Expect.equal jsonResult.Document xmlResult.Document "cross-format: JSON and XML produce identical ASTs"

          testCase "tic-tac-toe golden files produce identical ASTs"
          <| fun _ ->
              let jsonResult = parseAlpsJson ticTacToeAlpsJson
              let xmlResult = parseAlpsXml ticTacToeAlpsXml

              Expect.isEmpty jsonResult.Errors "JSON parse should succeed"
              Expect.isEmpty xmlResult.Errors "XML parse should succeed"
              Expect.equal jsonResult.Document xmlResult.Document "tic-tac-toe cross-format equivalence"

          testCase "onboarding golden files produce identical ASTs"
          <| fun _ ->
              let jsonResult = parseAlpsJson onboardingAlpsJson
              let xmlResult = parseAlpsXml onboardingAlpsXml

              Expect.isEmpty jsonResult.Errors "JSON parse should succeed"
              Expect.isEmpty xmlResult.Errors "XML parse should succeed"
              Expect.equal jsonResult.Document xmlResult.Document "onboarding cross-format equivalence"

          testCase "cross-format: states identical"
          <| fun _ ->
              let jsonDoc = (parseAlpsJson ticTacToeAlpsJson).Document
              let xmlDoc = (parseAlpsXml ticTacToeAlpsXml).Document

              let jsonStates = getStates jsonDoc |> List.map (fun s -> s.Identifier) |> Set.ofList
              let xmlStates = getStates xmlDoc |> List.map (fun s -> s.Identifier) |> Set.ofList
              Expect.equal jsonStates xmlStates "states identical"

          testCase "cross-format: transitions identical"
          <| fun _ ->
              let jsonDoc = (parseAlpsJson ticTacToeAlpsJson).Document
              let xmlDoc = (parseAlpsXml ticTacToeAlpsXml).Document

              let jsonTransitions =
                  getTransitions jsonDoc
                  |> List.map (fun t -> t.Source, t.Event, t.Target)
                  |> Set.ofList

              let xmlTransitions =
                  getTransitions xmlDoc
                  |> List.map (fun t -> t.Source, t.Event, t.Target)
                  |> Set.ofList

              Expect.equal jsonTransitions xmlTransitions "transitions identical"

          testCase "cross-format: document annotations identical"
          <| fun _ ->
              let jsonDoc = (parseAlpsJson ticTacToeAlpsJson).Document
              let xmlDoc = (parseAlpsXml ticTacToeAlpsXml).Document
              Expect.equal jsonDoc.Annotations xmlDoc.Annotations "document annotations identical" ]
