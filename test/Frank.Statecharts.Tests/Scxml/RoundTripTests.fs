module Frank.Statecharts.Tests.Scxml.RoundTripTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Scxml.Parser
open Frank.Statecharts.Scxml.Generator

// Strip Position fields for structural comparison (positions differ between
// original parse and re-parse of generated output).
let rec private stripStatePositions (state: StateNode) : StateNode =
    { state with
        Position = None
        Children = state.Children |> List.map stripStatePositions }

let private stripElementPositions (el: StatechartElement) : StatechartElement =
    match el with
    | StateDecl s -> StateDecl(stripStatePositions s)
    | TransitionElement t -> TransitionElement { t with Position = None }
    | NoteElement n -> NoteElement { n with Position = None }
    | GroupElement g -> GroupElement { g with Position = None }
    | DirectiveElement d -> DirectiveElement d

let private stripDocPositions (doc: StatechartDocument) : StatechartDocument =
    { doc with
        Elements = doc.Elements |> List.map stripElementPositions
        DataEntries =
            doc.DataEntries
            |> List.map (fun d -> { d with Position = None }) }

// === User Story 5 Acceptance Scenarios (T028) ===

[<Tests>]
let roundTripTests =
    testList
        "Scxml.RoundTrip"
        [
          testCase "US5-S1: roundtrip reference document"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
  <datamodel>
    <data id="count" expr="0"/>
  </datamodel>
  <state id="idle">
    <transition event="start" target="active"/>
  </state>
  <state id="active">
    <transition event="submit" cond="isValid" target="done"/>
    <transition event="cancel" target="idle"/>
  </state>
  <final id="done"/>
</scxml>"""

              let result1 = parseString xml
              let generated = generate result1.Document
              let result2 = parseString generated
              let doc1 = stripDocPositions result1.Document
              let doc2 = stripDocPositions result2.Document
              Expect.equal doc1 doc2 "ASTs should be structurally equal"

          testCase "US5-S2: roundtrip minimal document"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
  <state id="s1"/>
</scxml>"""

              let result1 = parseString xml
              let generated = generate result1.Document
              let result2 = parseString generated
              let doc1 = stripDocPositions result1.Document
              let doc2 = stripDocPositions result2.Document
              Expect.equal doc1 doc2 "minimal doc roundtrips"

          testCase "US5-S3: roundtrip parallel, history, invoke"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="p1">
  <parallel id="p1">
    <state id="r1">
      <history id="h1" type="deep"/>
    </state>
    <state id="r2">
      <invoke type="http" src="https://example.com"/>
    </state>
  </parallel>
</scxml>"""

              let result1 = parseString xml
              let generated = generate result1.Document
              let result2 = parseString generated
              let doc1 = stripDocPositions result1.Document
              let doc2 = stripDocPositions result2.Document
              Expect.equal doc1 doc2 "parallel/history/invoke roundtrip"

          testCase "roundtrip compound states with initial"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="parent">
  <state id="parent" initial="child1">
    <state id="child1">
      <transition event="next" target="child2"/>
    </state>
    <state id="child2"/>
  </state>
</scxml>"""

              let result1 = parseString xml
              let generated = generate result1.Document
              let result2 = parseString generated
              let doc1 = stripDocPositions result1.Document
              let doc2 = stripDocPositions result2.Document
              Expect.equal doc1 doc2 "compound state roundtrip"

          testCase "roundtrip internal transition"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
  <state id="s1">
    <transition event="tick" type="internal" target="s1"/>
  </state>
</scxml>"""

              let result1 = parseString xml
              let generated = generate result1.Document
              let result2 = parseString generated
              let doc1 = stripDocPositions result1.Document
              let doc2 = stripDocPositions result2.Document
              Expect.equal doc1 doc2 "internal transition roundtrip"

          testCase "roundtrip state-scoped datamodel"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
  <state id="s1">
    <datamodel>
      <data id="localVar" expr="42"/>
    </datamodel>
  </state>
</scxml>"""

              let result1 = parseString xml
              let generated = generate result1.Document
              let result2 = parseString generated
              let doc1 = stripDocPositions result1.Document
              let doc2 = stripDocPositions result2.Document
              Expect.equal doc1 doc2 "state-scoped datamodel roundtrip"

          testCase "roundtrip history with default transition"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
  <state id="s1">
    <history id="h1" type="shallow">
      <transition target="child1"/>
    </history>
    <state id="child1"/>
  </state>
</scxml>"""

              let result1 = parseString xml
              let generated = generate result1.Document
              let result2 = parseString generated
              let doc1 = stripDocPositions result1.Document
              let doc2 = stripDocPositions result2.Document
              Expect.equal doc1 doc2 "history with default transition roundtrip"

          testCase "roundtrip traffic light state machine"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" name="trafficLight" initial="off">
  <datamodel>
    <data id="cycleCount" expr="0"/>
    <data id="mode" expr="'normal'"/>
  </datamodel>
  <state id="off">
    <transition event="powerOn" target="red"/>
  </state>
  <state id="red">
    <transition event="timer" target="redAmber"/>
    <transition event="powerOff" target="off"/>
    <transition event="emergency" target="flashingRed"/>
  </state>
  <state id="redAmber">
    <transition event="timer" target="green"/>
  </state>
  <state id="green">
    <transition event="timer" target="amber"/>
    <transition event="emergency" target="flashingRed"/>
  </state>
  <state id="amber">
    <transition event="timer" target="red"/>
    <transition event="emergency" target="flashingRed"/>
  </state>
  <state id="flashingRed">
    <transition event="resume" target="red"/>
  </state>
  <state id="pedestrianCrossing">
    <transition event="done" target="red"/>
  </state>
  <state id="nightMode">
    <transition event="dayMode" target="red"/>
  </state>
  <state id="maintenance">
    <transition event="fixed" target="off"/>
  </state>
  <final id="decommissioned"/>
</scxml>"""

              let result1 = parseString xml
              let generated = generate result1.Document
              let result2 = parseString generated
              let doc1 = stripDocPositions result1.Document
              let doc2 = stripDocPositions result2.Document
              Expect.equal doc1 doc2 "complex traffic light roundtrip"

          testCase "roundtrip executable content (onentry/onexit)"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
  <state id="s1">
    <onentry><log expr="entering s1"/><send event="started"/></onentry>
    <onexit><assign location="x" expr="1"/></onexit>
    <transition event="next" target="s2"/>
  </state>
  <state id="s2">
    <onentry><log expr="entering s2"/></onentry>
  </state>
</scxml>"""

              let result1 = parseString xml
              Expect.isEmpty result1.Errors "parse succeeds"
              // Verify activities populated
              let states =
                  result1.Document.Elements
                  |> List.choose (function StateDecl s -> Some s | _ -> None)
              let s1 = states |> List.find (fun s -> s.Identifier = Some "s1")
              Expect.isSome s1.Activities "s1 has activities"
              Expect.isNonEmpty s1.Activities.Value.Entry "s1 has entry actions"
              Expect.isNonEmpty s1.Activities.Value.Exit "s1 has exit actions"
              // Verify annotations
              let hasOnEntry =
                  s1.Annotations |> List.exists (function
                      | ScxmlAnnotation(ScxmlOnEntry _) -> true | _ -> false)
              Expect.isTrue hasOnEntry "s1 has ScxmlOnEntry annotation"
              let hasOnExit =
                  s1.Annotations |> List.exists (function
                      | ScxmlAnnotation(ScxmlOnExit _) -> true | _ -> false)
              Expect.isTrue hasOnExit "s1 has ScxmlOnExit annotation"
              // Round-trip
              let generated = generate result1.Document
              let result2 = parseString generated
              Expect.isEmpty result2.Errors "re-parse succeeds"
              let doc1 = stripDocPositions result1.Document
              let doc2 = stripDocPositions result2.Document
              Expect.equal doc1 doc2 "executable content roundtrip"

          testCase "roundtrip initial child element"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="container">
  <state id="container">
    <initial><transition target="child1"/></initial>
    <state id="child1">
      <transition event="go" target="child2"/>
    </state>
    <state id="child2"/>
  </state>
</scxml>"""

              let result1 = parseString xml
              Expect.isEmpty result1.Errors "parse succeeds"
              // Verify ScxmlInitialElement annotation
              let states =
                  result1.Document.Elements
                  |> List.choose (function StateDecl s -> Some s | _ -> None)
              let container = states |> List.find (fun s -> s.Identifier = Some "container")
              let hasInitialElement =
                  container.Annotations |> List.exists (function
                      | ScxmlAnnotation(ScxmlInitialElement _) -> true | _ -> false)
              Expect.isTrue hasInitialElement "container has ScxmlInitialElement annotation"
              // Round-trip
              let generated = generate result1.Document
              let result2 = parseString generated
              Expect.isEmpty result2.Errors "re-parse succeeds"
              let doc1 = stripDocPositions result1.Document
              let doc2 = stripDocPositions result2.Document
              Expect.equal doc1 doc2 "initial child element roundtrip"

          testCase "roundtrip data src attribute"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
  <datamodel>
    <data id="settings" src="settings.json"/>
    <data id="count" expr="0"/>
  </datamodel>
  <state id="s1"/>
</scxml>"""

              let result1 = parseString xml
              Expect.isEmpty result1.Errors "parse succeeds"
              // Verify ScxmlDataSrc annotation
              let hasDataSrc =
                  result1.Document.Annotations |> List.exists (function
                      | ScxmlAnnotation(ScxmlDataSrc(name, src)) ->
                          name = "settings" && src = "settings.json"
                      | _ -> false)
              Expect.isTrue hasDataSrc "document has ScxmlDataSrc annotation"
              // Round-trip
              let generated = generate result1.Document
              let result2 = parseString generated
              Expect.isEmpty result2.Errors "re-parse succeeds"
              let doc1 = stripDocPositions result1.Document
              let doc2 = stripDocPositions result2.Document
              Expect.equal doc1 doc2 "data src roundtrip"

          testCase "roundtrip comprehensive document"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle" name="comprehensive">
  <datamodel>
    <data id="count" expr="0"/>
    <data id="config" src="config.json"/>
  </datamodel>
  <state id="idle">
    <onentry><log expr="entering idle"/></onentry>
    <onexit><send event="leaving"/></onexit>
    <transition event="start" target="active"/>
  </state>
  <state id="active">
    <initial><transition target="sub1"/></initial>
    <state id="sub1">
      <transition event="next" target="sub2"/>
    </state>
    <state id="sub2">
      <transition event="done" target="finished"/>
    </state>
    <onentry><assign location="count" expr="count + 1"/></onentry>
    <transition event="cancel" target="idle"/>
  </state>
  <state id="finished">
    <history id="h1" type="shallow">
      <transition target="sub1"/>
    </history>
    <invoke type="http" src="http://example.com" id="inv1"/>
    <transition event="restart" target="active"/>
  </state>
  <final id="done"/>
</scxml>"""

              let result1 = parseString xml
              Expect.isEmpty result1.Errors "parse succeeds"
              let generated = generate result1.Document
              let result2 = parseString generated
              Expect.isEmpty result2.Errors "re-parse succeeds"
              let doc1 = stripDocPositions result1.Document
              let doc2 = stripDocPositions result2.Document
              Expect.equal doc1 doc2 "comprehensive document roundtrip" ]
