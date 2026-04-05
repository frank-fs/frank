module Frank.Statecharts.Tests.Scxml.ParserTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Scxml.Parser

/// Extract StateDecl entries from a StatechartDocument's Elements.
let private stateDecls (doc: StatechartDocument) =
    doc.Elements |> List.choose (function StateDecl s -> Some s | _ -> None)

/// Extract TransitionEdge entries for a given source state from a StatechartDocument.
let private transitionsFrom (source: string) (doc: StatechartDocument) =
    doc.Elements
    |> List.choose (function TransitionElement t when t.Source = source -> Some t | _ -> None)

/// Extract history child nodes from a StateNode.
let private historyChildren (state: StateNode) =
    state.Children
    |> List.filter (fun c -> match c.Kind with ShallowHistory | DeepHistory -> true | _ -> false)

/// Extract ScxmlInvoke annotations from a StateNode.
let private invokeAnnotations (state: StateNode) =
    state.Annotations
    |> List.choose (fun a ->
        match a with
        | ScxmlAnnotation(ScxmlInvoke(t, src, id)) -> Some(t, src, id)
        | _ -> None)

/// Check if a TransitionEdge has the ScxmlTransitionType(true) annotation (i.e., internal).
let private isInternalTransition (t: TransitionEdge) =
    t.Annotations
    |> List.exists (fun a ->
        match a with
        | ScxmlAnnotation(ScxmlTransitionType(true)) -> true
        | _ -> false)

/// Get all targets for a transition (checking ScxmlMultiTarget annotation first).
let private allTargets (t: TransitionEdge) =
    t.Annotations
    |> List.tryPick (fun a ->
        match a with
        | ScxmlAnnotation(ScxmlMultiTarget(targets)) -> Some targets
        | _ -> None)
    |> Option.defaultWith (fun () -> t.Target |> Option.toList)

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
              let doc = result.Document
              Expect.equal doc.InitialStateId (Some "idle") "initial state should be idle"
              let states = stateDecls doc
              Expect.equal states.Length 2 "should have 2 states"
              Expect.equal states.[0].Identifier (Some "idle") "first state is idle"
              Expect.equal states.[1].Identifier (Some "active") "second state is active"

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
              let doc = result.Document
              let idleTransitions = transitionsFrom "idle" doc
              Expect.equal idleTransitions.Length 1 "idle has one transition"
              let t = idleTransitions.[0]
              Expect.equal t.Event (Some "start") "event is start"
              Expect.equal t.Target (Some "active") "target is active"

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
              let t = (transitionsFrom "idle" result.Document).[0]
              Expect.equal t.Event (Some "submit") "event is submit"
              Expect.equal t.Guard (Some "isValid") "guard is isValid"
              Expect.equal t.Target (Some "submitted") "target is submitted"

          testCase "US1-S4: final state"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
  <state id="idle"/>
  <final id="done"/>
</scxml>"""

              let result = parseString xml
              let states = stateDecls result.Document
              let finalState = states.[1]
              Expect.equal finalState.Identifier (Some "done") "final state id"
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
              let states = stateDecls result.Document
              let parent = states.[0]
              Expect.equal parent.Kind Regular "parent is Regular"
              Expect.equal parent.Children.Length 2 "parent has 2 children"
              Expect.equal parent.Children.[0].Identifier (Some "child1") "first child"
              Expect.equal parent.Children.[1].Identifier (Some "child2") "second child" ]

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
              Expect.isEmpty (stateDecls result.Document) "no states"
              Expect.isEmpty result.Errors "no errors"

          testCase "no initial attribute infers from first child"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml">
  <state id="first"/>
</scxml>"""

              let result = parseString xml
              let doc = result.Document
              Expect.equal doc.InitialStateId (Some "first") "InitialStateId inferred from first child"

          testCase "self-transition"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <transition event="retry" target="s1"/>
  </state>
</scxml>"""

              let result = parseString xml
              let t = (transitionsFrom "s1" result.Document).[0]
              Expect.equal t.Event (Some "retry") "event is retry"
              Expect.equal t.Target (Some "s1") "target is the containing state"

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
              let t = (transitionsFrom "s1" result.Document).[0]
              Expect.isNone t.Event "Event should be None"
              Expect.equal t.Target (Some "next") "target is next"

          testCase "targetless transition"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <transition event="check" cond="isReady"/>
  </state>
</scxml>"""

              let result = parseString xml
              let t = (transitionsFrom "s1" result.Document).[0]
              Expect.equal t.Event (Some "check") "event is check"
              Expect.equal t.Guard (Some "isReady") "guard is isReady"
              Expect.isNone t.Target "Target should be None"

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
              let transitions = transitionsFrom "s1" result.Document
              Expect.equal transitions.Length 2 "two transitions"
              Expect.equal transitions.[0].Guard (Some "isValid") "first guard"
              Expect.equal transitions.[1].Guard (Some "isInvalid") "second guard"
              Expect.equal transitions.[0].Target (Some "ok") "first target"
              Expect.equal transitions.[1].Target (Some "error") "second target"

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
              let doc = result.Document
              let states = stateDecls doc
              let l1 = states.[0]
              Expect.equal l1.Identifier (Some "L1") "level 1"
              Expect.equal l1.Kind Regular "L1 is Regular"
              let l2 = l1.Children.[0]
              Expect.equal l2.Identifier (Some "L2") "level 2"
              Expect.equal l2.Kind Regular "L2 is Regular"
              let l3 = l2.Children.[0]
              Expect.equal l3.Identifier (Some "L3") "level 3"
              let l4 = l3.Children.[0]
              Expect.equal l4.Identifier (Some "L4") "level 4"
              let l5 = l4.Children.[0]
              Expect.equal l5.Identifier (Some "L5") "level 5"
              Expect.equal l5.Kind Regular "L5 is Regular (leaf)"

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
              let doc = result.Document
              let states = stateDecls doc
              Expect.equal states.Length 2 "two states"
              Expect.equal states.[0].Identifier (Some "s1") "first state"
              let s1Transitions = transitionsFrom "s1" doc
              Expect.equal s1Transitions.Length 1 "one transition"

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
              let t = (transitionsFrom "s1" result.Document).[0]
              Expect.equal (allTargets t) [ "s1"; "s2"; "s3" ] "three targets"

          testCase "unicode in IDs"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="zustand_bereit">
  <state id="zustand_bereit"/>
</scxml>"""

              let result = parseString xml
              let doc = result.Document
              Expect.equal doc.InitialStateId (Some "zustand_bereit") "unicode initial"
              Expect.equal (stateDecls doc).[0].Identifier (Some "zustand_bereit") "unicode state id"

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
              Expect.equal (stateDecls result.Document).Length 2 "two states"
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
              let p = (stateDecls result.Document).[0]
              Expect.equal p.Identifier (Some "p") "parallel id"
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
              let doc = result.Document
              Expect.equal doc.DataEntries.Length 2 "two data entries"
              Expect.equal doc.DataEntries.[0].Name "counter" "first data id"
              Expect.equal doc.DataEntries.[0].Expression (Some "0") "first data expr"
              Expect.equal doc.DataEntries.[1].Name "name" "second data id"

          testCase "transition type internal"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <transition event="tick" type="internal" target="s1"/>
  </state>
</scxml>"""

              let result = parseString xml
              let t = (transitionsFrom "s1" result.Document).[0]
              Expect.isTrue (isInternalTransition t) "transition type is internal"

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
              let t = (transitionsFrom "s1" result.Document).[0]
              Expect.isFalse (isInternalTransition t) "default transition type is external"

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
              let s1 = (stateDecls result.Document).[0]
              let historyNodes = historyChildren s1
              Expect.equal historyNodes.Length 1 "one history node"
              let h = historyNodes.[0]
              Expect.equal h.Identifier (Some "h1") "history id"
              Expect.equal h.Kind DeepHistory "history kind is deep"
              // Check the ScxmlHistory annotation for the default transition target
              let historyMeta =
                  h.Annotations
                  |> List.tryPick (fun a ->
                      match a with
                      | ScxmlAnnotation(ScxmlHistory(_, _, defaultTarget)) -> Some defaultTarget
                      | _ -> None)
              Expect.isSome historyMeta "has ScxmlHistory annotation"
              Expect.equal historyMeta.Value (Some "child1") "default transition target"

          testCase "no-namespace document parses correctly"
          <| fun _ ->
              let xml =
                  """<scxml initial="s1">
  <state id="s1"/>
</scxml>"""

              let result = parseString xml
              let states = stateDecls result.Document
              Expect.equal states.Length 1 "one state"
              Expect.equal states.[0].Identifier (Some "s1") "state id"

          // === AC-2: Non-WSD parsers default SenderRole/ReceiverRole/PayloadType to None (issue #307) ===
          testCase "AC-2: SCXML transition has SenderRole = None, ReceiverRole = None, PayloadType = None"
          <| fun _ ->
              let xml =
                  """<?xml version="1.0"?><scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle"><state id="idle"><transition event="submit" target="submitted"/></state><state id="submitted"/></scxml>"""
              let result = parseString xml
              Expect.isEmpty result.Errors "no parse errors"
              let t = (transitionsFrom "idle" result.Document).[0]
              Expect.isNone t.SenderRole "SenderRole = None for SCXML (no participant semantics)"
              Expect.isNone t.ReceiverRole "ReceiverRole = None for SCXML (no participant semantics)"
              Expect.isNone t.PayloadType "PayloadType = None for SCXML" ]

// === User Story 3: Data Model Parsing (T022) ===

[<Tests>]
let dataModelTests =
    testList
        "Scxml.Parser.DataModel"
        [
          testCase "US3-S1: data entries with expr attribute"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
  <datamodel>
    <data id="count" expr="0"/>
    <data id="name" expr="'default'"/>
  </datamodel>
  <state id="s1"/>
</scxml>"""

              let result = parseString xml
              let doc = result.Document
              Expect.equal doc.DataEntries.Length 2 "should have 2 data entries"
              Expect.equal doc.DataEntries.[0].Name "count" "first entry id"
              Expect.equal doc.DataEntries.[0].Expression (Some "0") "first entry expr"
              Expect.equal doc.DataEntries.[1].Name "name" "second entry id"
              Expect.equal doc.DataEntries.[1].Expression (Some "'default'") "second entry expr"

          testCase "US3-S2: data entry with no expr"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
  <datamodel>
    <data id="items"/>
  </datamodel>
  <state id="s1"/>
</scxml>"""

              let result = parseString xml
              let entry = result.Document.DataEntries.[0]
              Expect.equal entry.Name "items" "entry id"
              Expect.isNone entry.Expression "expression should be None"

          testCase "US3-S3: state-scoped datamodel"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
  <state id="s1">
    <datamodel>
      <data id="localVar" expr="42"/>
    </datamodel>
  </state>
</scxml>"""

              let result = parseString xml
              // State-scoped data entries are flattened into document DataEntries
              let doc = result.Document
              Expect.equal doc.DataEntries.Length 1 "should have 1 data entry (flattened)"
              Expect.equal doc.DataEntries.[0].Name "localVar" "entry id"
              Expect.equal doc.DataEntries.[0].Expression (Some "42") "entry expr"

          testCase "data with child text content"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
  <datamodel>
    <data id="config">some content</data>
  </datamodel>
  <state id="s1"/>
</scxml>"""

              let result = parseString xml
              let entry = result.Document.DataEntries.[0]
              Expect.equal entry.Name "config" "entry id"
              Expect.equal entry.Expression (Some "some content") "expression from child content" ]

// === User Story 4: Parallel, History, and Invoke Parsing (T023) ===

[<Tests>]
let advancedParserTests =
    testList
        "Scxml.Parser.Advanced"
        [
          testCase "US4-S1: parallel state with children"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="p1">
  <parallel id="p1">
    <state id="region1"/>
    <state id="region2"/>
  </parallel>
</scxml>"""

              let result = parseString xml
              let p = (stateDecls result.Document).[0]
              Expect.equal p.Kind Parallel "kind is Parallel"
              Expect.equal p.Identifier (Some "p1") "id is p1"
              Expect.equal p.Children.Length 2 "has 2 child states"

          testCase "US4-S2: history with type deep"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
  <state id="s1">
    <history id="h1" type="deep"/>
    <state id="child1"/>
  </state>
</scxml>"""

              let result = parseString xml
              let state = (stateDecls result.Document).[0]
              let historyNodes = historyChildren state
              Expect.equal historyNodes.Length 1 "has 1 history node"
              let h = historyNodes.[0]
              Expect.equal h.Identifier (Some "h1") "history id"
              Expect.equal h.Kind DeepHistory "history kind is Deep"

          testCase "US4-S3: history defaults to shallow"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
  <state id="s1">
    <history id="h2"/>
    <state id="child1"/>
  </state>
</scxml>"""

              let result = parseString xml
              let h = (historyChildren (stateDecls result.Document).[0]).[0]
              Expect.equal h.Kind ShallowHistory "history kind defaults to Shallow"

          testCase "US4-S4: invoke with attributes"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
  <state id="s1">
    <invoke type="http" src="https://example.com/service"/>
  </state>
</scxml>"""

              let result = parseString xml
              let state = (stateDecls result.Document).[0]
              let invokes = invokeAnnotations state
              Expect.equal invokes.Length 1 "has 1 invoke node"
              let (invType, invSrc, _invId) = invokes.[0]
              Expect.equal invType "http" "invoke type"
              Expect.equal invSrc (Some "https://example.com/service") "invoke src"

          testCase "history with default transition"
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

              let result = parseString xml
              let h = (historyChildren (stateDecls result.Document).[0]).[0]
              let historyMeta =
                  h.Annotations
                  |> List.tryPick (fun a ->
                      match a with
                      | ScxmlAnnotation(ScxmlHistory(_, _, defaultTarget)) -> Some defaultTarget
                      | _ -> None)
              Expect.isSome historyMeta "should have ScxmlHistory annotation"
              Expect.equal historyMeta.Value (Some "child1") "default target" ]

// === WP02: Executable Content, Initial Elements, Data Src, Namespace (T003-T007) ===

[<Tests>]
let executableContentTests =
    testList
        "Scxml.Parser.ExecutableContent"
        [
          // T003: onentry with send and log actions
          testCase "onentry with send produces entry actions and ScxmlOnEntry annotation"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <onentry>
      <send event="done"/>
      <log expr="hello"/>
    </onentry>
  </state>
</scxml>"""

              let result = parseString xml
              let state = (stateDecls result.Document).[0]

              // Activities.Entry should be populated
              Expect.isSome state.Activities "Activities should be Some"
              let activities = state.Activities.Value
              Expect.equal activities.Entry [ "send done"; "log hello" ] "entry actions"
              Expect.isEmpty activities.Exit "no exit actions"
              Expect.isEmpty activities.Do "Do is always empty for SCXML"

              // ScxmlOnEntry annotation should be present
              let onEntryAnnotations =
                  state.Annotations
                  |> List.choose (fun a ->
                      match a with
                      | ScxmlAnnotation(ScxmlOnEntry xml) -> Some xml
                      | _ -> None)

              Expect.equal onEntryAnnotations.Length 1 "one ScxmlOnEntry annotation"
              Expect.isTrue (onEntryAnnotations.[0].Contains "send") "annotation contains send element"

          // T003: assign action uses location attribute as first attribute value
          testCase "onentry with assign uses location as first attribute"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <onentry>
      <assign location="x" expr="1"/>
    </onentry>
  </state>
</scxml>"""

              let result = parseString xml
              let state = (stateDecls result.Document).[0]
              Expect.isSome state.Activities "Activities should be Some"
              let activities = state.Activities.Value
              Expect.equal activities.Entry [ "assign x" ] "entry action is 'assign x'"

          // T004: onexit with log produces exit actions and ScxmlOnExit annotation
          testCase "onexit with log produces exit actions and ScxmlOnExit annotation"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <onexit>
      <log expr="leaving"/>
    </onexit>
  </state>
</scxml>"""

              let result = parseString xml
              let state = (stateDecls result.Document).[0]

              Expect.isSome state.Activities "Activities should be Some"
              let activities = state.Activities.Value
              Expect.isEmpty activities.Entry "no entry actions"
              Expect.equal activities.Exit [ "log leaving" ] "exit actions"

              let onExitAnnotations =
                  state.Annotations
                  |> List.choose (fun a ->
                      match a with
                      | ScxmlAnnotation(ScxmlOnExit xml) -> Some xml
                      | _ -> None)

              Expect.equal onExitAnnotations.Length 1 "one ScxmlOnExit annotation"
              Expect.isTrue (onExitAnnotations.[0].Contains "log") "annotation contains log element"

          // T003 + T004: both onentry and onexit
          testCase "onentry and onexit together populate both entry and exit activities"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <onentry>
      <send event="started"/>
    </onentry>
    <onexit>
      <send event="stopped"/>
    </onexit>
  </state>
</scxml>"""

              let result = parseString xml
              let state = (stateDecls result.Document).[0]
              Expect.isSome state.Activities "Activities should be Some"
              let activities = state.Activities.Value
              Expect.equal activities.Entry [ "send started" ] "entry actions"
              Expect.equal activities.Exit [ "send stopped" ] "exit actions"

          // T003: multiple onentry blocks produce multiple annotations
          testCase "multiple onentry blocks produce multiple ScxmlOnEntry annotations"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <onentry>
      <send event="first"/>
    </onentry>
    <onentry>
      <log expr="second"/>
    </onentry>
  </state>
</scxml>"""

              let result = parseString xml
              let state = (stateDecls result.Document).[0]

              let onEntryAnnotations =
                  state.Annotations
                  |> List.choose (fun a ->
                      match a with
                      | ScxmlAnnotation(ScxmlOnEntry xml) -> Some xml
                      | _ -> None)

              Expect.equal onEntryAnnotations.Length 2 "two ScxmlOnEntry annotations (one per block)"
              // Combined entry actions from both blocks
              Expect.isSome state.Activities "Activities should be Some"
              Expect.equal state.Activities.Value.Entry [ "send first"; "log second" ] "entry actions from both blocks"

          // T003/T004: state with no onentry/onexit has Activities = None
          testCase "state with no onentry or onexit has Activities = None"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <transition event="go" target="s2"/>
  </state>
  <state id="s2"/>
</scxml>"""

              let result = parseString xml
              let state = (stateDecls result.Document).[0]
              Expect.isNone state.Activities "Activities should be None when no onentry/onexit"

          // T005: initial child element produces ScxmlInitialElement annotation
          testCase "initial child element produces ScxmlInitialElement annotation"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <initial>
      <transition target="child1"/>
    </initial>
    <state id="child1"/>
    <state id="child2"/>
  </state>
</scxml>"""

              let result = parseString xml
              let state = (stateDecls result.Document).[0]

              let initialElAnnotations =
                  state.Annotations
                  |> List.choose (fun a ->
                      match a with
                      | ScxmlAnnotation(ScxmlInitialElement targetId) -> Some targetId
                      | _ -> None)

              Expect.equal initialElAnnotations.Length 1 "one ScxmlInitialElement annotation"
              Expect.equal initialElAnnotations.[0] "child1" "target is child1"

          // T006: data element with src attribute produces ScxmlDataSrc annotation
          testCase "data element with src attribute produces ScxmlDataSrc annotation"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <datamodel>
    <data id="config" src="config.json"/>
  </datamodel>
  <state id="s1"/>
</scxml>"""

              let result = parseString xml
              let doc = result.Document

              let dataSrcAnnotations =
                  doc.Annotations
                  |> List.choose (fun a ->
                      match a with
                      | ScxmlAnnotation(ScxmlDataSrc(name, src)) -> Some(name, src)
                      | _ -> None)

              Expect.equal dataSrcAnnotations.Length 1 "one ScxmlDataSrc annotation"
              let (name, src) = dataSrcAnnotations.[0]
              Expect.equal name "config" "data id is config"
              Expect.equal src "config.json" "src is config.json"

          // T006: data src at state level produces annotation on state
          testCase "data element with src at state level produces ScxmlDataSrc on state"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <datamodel>
      <data id="local" src="local.json"/>
    </datamodel>
  </state>
</scxml>"""

              let result = parseString xml
              let state = (stateDecls result.Document).[0]

              let dataSrcAnnotations =
                  state.Annotations
                  |> List.choose (fun a ->
                      match a with
                      | ScxmlAnnotation(ScxmlDataSrc(name, src)) -> Some(name, src)
                      | _ -> None)

              Expect.equal dataSrcAnnotations.Length 1 "one ScxmlDataSrc annotation on state"
              let (name, src) = dataSrcAnnotations.[0]
              Expect.equal name "local" "data id is local"
              Expect.equal src "local.json" "src is local.json"

          // T007: namespace is stored as ScxmlNamespace annotation on document
          testCase "SCXML namespace is stored as ScxmlNamespace annotation"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1"/>
</scxml>"""

              let result = parseString xml
              let doc = result.Document

              let nsAnnotations =
                  doc.Annotations
                  |> List.choose (fun a ->
                      match a with
                      | ScxmlAnnotation(ScxmlNamespace ns) -> Some ns
                      | _ -> None)

              Expect.equal nsAnnotations.Length 1 "one ScxmlNamespace annotation"
              Expect.equal nsAnnotations.[0] "http://www.w3.org/2005/07/scxml" "namespace URI"

          // T007: no-namespace document stores empty namespace
          testCase "no-namespace document stores empty namespace string"
          <| fun _ ->
              let xml =
                  """<scxml initial="s1">
  <state id="s1"/>
</scxml>"""

              let result = parseString xml
              let doc = result.Document

              let nsAnnotations =
                  doc.Annotations
                  |> List.choose (fun a ->
                      match a with
                      | ScxmlAnnotation(ScxmlNamespace ns) -> Some ns
                      | _ -> None)

              Expect.equal nsAnnotations.Length 1 "one ScxmlNamespace annotation"
              Expect.equal nsAnnotations.[0] "" "no-namespace document has empty string" ]
