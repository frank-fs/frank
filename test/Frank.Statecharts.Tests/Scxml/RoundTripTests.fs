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
              Expect.equal doc1 doc2 "complex traffic light roundtrip" ]
