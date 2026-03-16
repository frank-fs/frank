module Frank.Statecharts.Tests.Scxml.ParserTests

open Expecto
open Frank.Statecharts.Scxml.Types
open Frank.Statecharts.Scxml.Parser

[<Tests>]
let parserTests =
    testList
        "Scxml.Parser"
        [
          // === User Story 1 Acceptance Scenarios ===

          testCase "US1-S1: initial state and basic states"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
  <state id="idle"/>
  <state id="active"/>
</scxml>"""

              let result = parseString xml
              Expect.isSome result.Document "should parse successfully"
              let doc = result.Document.Value
              Expect.equal doc.InitialId (Some "idle") "initial state should be idle"
              Expect.equal doc.States.Length 2 "should have 2 states"
              Expect.equal doc.States.[0].Id (Some "idle") "first state is idle"
              Expect.equal doc.States.[1].Id (Some "active") "second state is active"

          testCase "US1-S2: transition with event and target"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
  <state id="idle">
    <transition event="start" target="active"/>
  </state>
  <state id="active"/>
</scxml>"""

              let result = parseString xml
              let doc = result.Document.Value
              let idleState = doc.States.[0]
              Expect.equal idleState.Transitions.Length 1 "idle has one transition"
              let t = idleState.Transitions.[0]
              Expect.equal t.Event (Some "start") "event is start"
              Expect.equal t.Targets [ "active" ] "target is active"

          testCase "US1-S3: guarded transition"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
  <state id="idle">
    <transition event="submit" cond="isValid" target="submitted"/>
  </state>
  <state id="submitted"/>
</scxml>"""

              let result = parseString xml
              let t = result.Document.Value.States.[0].Transitions.[0]
              Expect.equal t.Event (Some "submit") "event is submit"
              Expect.equal t.Guard (Some "isValid") "guard is isValid"
              Expect.equal t.Targets [ "submitted" ] "target is submitted"

          testCase "US1-S4: final state"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
  <state id="idle"/>
  <final id="done"/>
</scxml>"""

              let result = parseString xml
              let finalState = result.Document.Value.States.[1]
              Expect.equal finalState.Id (Some "done") "final state id"
              Expect.equal finalState.Kind Final "kind is Final"

          testCase "US1-S5: compound states"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="parent">
  <state id="parent">
    <state id="child1"/>
    <state id="child2"/>
  </state>
</scxml>"""

              let result = parseString xml
              let parent = result.Document.Value.States.[0]
              Expect.equal parent.Kind Compound "parent is Compound"
              Expect.equal parent.Children.Length 2 "parent has 2 children"
              Expect.equal parent.Children.[0].Id (Some "child1") "first child"
              Expect.equal parent.Children.[1].Id (Some "child2") "second child" ]

[<Tests>]
let edgeCaseTests =
    testList
        "Scxml.Parser.EdgeCases"
        [
          testCase "empty SCXML document"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml"/>"""

              let result = parseString xml
              Expect.isSome result.Document "should parse successfully"
              Expect.isEmpty result.Document.Value.States "no states"
              Expect.isEmpty result.Errors "no errors"

          testCase "no initial attribute infers from first child"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml">
  <state id="first"/>
</scxml>"""

              let result = parseString xml
              let doc = result.Document.Value
              Expect.equal doc.InitialId (Some "first") "InitialId inferred from first child"

          testCase "self-transition"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <transition event="retry" target="s1"/>
  </state>
</scxml>"""

              let result = parseString xml
              let t = result.Document.Value.States.[0].Transitions.[0]
              Expect.equal t.Event (Some "retry") "event is retry"
              Expect.equal t.Targets [ "s1" ] "target is the containing state"

          testCase "eventless transition"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <transition target="next"/>
  </state>
  <state id="next"/>
</scxml>"""

              let result = parseString xml
              let t = result.Document.Value.States.[0].Transitions.[0]
              Expect.isNone t.Event "Event should be None"
              Expect.equal t.Targets [ "next" ] "target is next"

          testCase "targetless transition"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <transition event="check" cond="isReady"/>
  </state>
</scxml>"""

              let result = parseString xml
              let t = result.Document.Value.States.[0].Transitions.[0]
              Expect.equal t.Event (Some "check") "event is check"
              Expect.equal t.Guard (Some "isReady") "guard is isReady"
              Expect.isEmpty t.Targets "Targets should be empty"

          testCase "multiple transitions same event different guards"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <transition event="submit" cond="isValid" target="ok"/>
    <transition event="submit" cond="isInvalid" target="error"/>
  </state>
  <state id="ok"/>
  <state id="error"/>
</scxml>"""

              let result = parseString xml
              let transitions = result.Document.Value.States.[0].Transitions
              Expect.equal transitions.Length 2 "two transitions"
              Expect.equal transitions.[0].Guard (Some "isValid") "first guard"
              Expect.equal transitions.[1].Guard (Some "isInvalid") "second guard"
              Expect.equal transitions.[0].Targets [ "ok" ] "first target"
              Expect.equal transitions.[1].Targets [ "error" ] "second target"

          testCase "deeply nested states (5 levels)"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="L1">
  <state id="L1">
    <state id="L2">
      <state id="L3">
        <state id="L4">
          <state id="L5"/>
        </state>
      </state>
    </state>
  </state>
</scxml>"""

              let result = parseString xml
              let doc = result.Document.Value
              let l1 = doc.States.[0]
              Expect.equal l1.Id (Some "L1") "level 1"
              Expect.equal l1.Kind Compound "L1 is compound"
              let l2 = l1.Children.[0]
              Expect.equal l2.Id (Some "L2") "level 2"
              Expect.equal l2.Kind Compound "L2 is compound"
              let l3 = l2.Children.[0]
              Expect.equal l3.Id (Some "L3") "level 3"
              let l4 = l3.Children.[0]
              Expect.equal l4.Id (Some "L4") "level 4"
              let l5 = l4.Children.[0]
              Expect.equal l5.Id (Some "L5") "level 5"
              Expect.equal l5.Kind Simple "L5 is simple (leaf)"

          testCase "prefixed namespace"
          <| fun _ ->
              let xml =
                  """<sc:scxml xmlns:sc="http://www.w3.org/2005/07/scxml" initial="s1">
  <sc:state id="s1">
    <sc:transition event="go" target="s2"/>
  </sc:state>
  <sc:state id="s2"/>
</sc:scxml>"""

              let result = parseString xml
              Expect.isSome result.Document "should parse with prefixed namespace"
              let doc = result.Document.Value
              Expect.equal doc.States.Length 2 "two states"
              Expect.equal doc.States.[0].Id (Some "s1") "first state"
              Expect.equal doc.States.[0].Transitions.Length 1 "one transition"

          testCase "multiple space-separated targets"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <transition event="fork" target="s1 s2 s3"/>
  </state>
  <state id="s2"/>
  <state id="s3"/>
</scxml>"""

              let result = parseString xml
              let t = result.Document.Value.States.[0].Transitions.[0]
              Expect.equal t.Targets [ "s1"; "s2"; "s3" ] "three targets"

          testCase "unicode in IDs"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="zustand_bereit">
  <state id="zustand_bereit"/>
</scxml>"""

              let result = parseString xml
              let doc = result.Document.Value
              Expect.equal doc.InitialId (Some "zustand_bereit") "unicode initial"
              Expect.equal doc.States.[0].Id (Some "zustand_bereit") "unicode state id"

          testCase "XML comments are ignored"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <!-- This is a comment -->
  <state id="s1"/>
  <!-- Another comment -->
  <state id="s2"/>
</scxml>"""

              let result = parseString xml
              Expect.isSome result.Document "should parse successfully"
              Expect.equal result.Document.Value.States.Length 2 "two states"
              Expect.isEmpty result.Errors "no errors"

          testCase "parallel state element"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="p">
  <parallel id="p">
    <state id="region1"/>
    <state id="region2"/>
  </parallel>
</scxml>"""

              let result = parseString xml
              let p = result.Document.Value.States.[0]
              Expect.equal p.Id (Some "p") "parallel id"
              Expect.equal p.Kind Parallel "kind is Parallel"
              Expect.equal p.Children.Length 2 "two children"

          testCase "datamodel with data entries"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <datamodel>
    <data id="counter" expr="0"/>
    <data id="name" expr="'test'"/>
  </datamodel>
  <state id="s1"/>
</scxml>"""

              let result = parseString xml
              let doc = result.Document.Value
              Expect.equal doc.DataEntries.Length 2 "two data entries"
              Expect.equal doc.DataEntries.[0].Id "counter" "first data id"
              Expect.equal doc.DataEntries.[0].Expression (Some "0") "first data expr"
              Expect.equal doc.DataEntries.[1].Id "name" "second data id"

          testCase "transition type internal"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <transition event="tick" type="internal" target="s1"/>
  </state>
</scxml>"""

              let result = parseString xml
              let t = result.Document.Value.States.[0].Transitions.[0]
              Expect.equal t.TransitionType Internal "transition type is internal"

          testCase "transition type defaults to external"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <transition event="go" target="s2"/>
  </state>
  <state id="s2"/>
</scxml>"""

              let result = parseString xml
              let t = result.Document.Value.States.[0].Transitions.[0]
              Expect.equal t.TransitionType External "default transition type is external"

          testCase "history pseudo-state"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <history id="h1" type="deep">
      <transition target="child1"/>
    </history>
    <state id="child1"/>
  </state>
</scxml>"""

              let result = parseString xml
              let s1 = result.Document.Value.States.[0]
              Expect.equal s1.HistoryNodes.Length 1 "one history node"
              let h = s1.HistoryNodes.[0]
              Expect.equal h.Id "h1" "history id"
              Expect.equal h.Kind Deep "history kind is deep"
              Expect.isSome h.DefaultTransition "has default transition"
              Expect.equal h.DefaultTransition.Value.Targets [ "child1" ] "default transition target"

          testCase "no-namespace document parses correctly"
          <| fun _ ->
              let xml =
                  """<scxml initial="s1">
  <state id="s1"/>
</scxml>"""

              let result = parseString xml
              Expect.isSome result.Document "should parse without namespace"
              Expect.equal result.Document.Value.States.Length 1 "one state"
              Expect.equal result.Document.Value.States.[0].Id (Some "s1") "state id" ]
