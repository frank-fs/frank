module Frank.Statecharts.Tests.Scxml.GeneratorTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Scxml.Generator
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

// === User Story 2 Acceptance Scenarios (T026) ===

[<Tests>]
let generatorTests =
    testList
        "Scxml.Generator"
        [
          testCase "US2-S1: generate basic states"
          <| fun _ ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = Some "idle"
                    Elements =
                      [ StateDecl
                            { Identifier = "idle"; Label = None; Kind = Regular
                              Children = []; Activities = None; Position = None; Annotations = [] }
                        StateDecl
                            { Identifier = "active"; Label = None; Kind = Regular
                              Children = []; Activities = None; Position = None; Annotations = [] }
                        StateDecl
                            { Identifier = "done"; Label = None; Kind = Final
                              Children = []; Activities = None; Position = None; Annotations = [] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generate doc
              let reparsed = parseString xml
              let rdoc = reparsed.Document
              Expect.equal rdoc.InitialStateId (Some "idle") "initial state preserved"
              let states = stateDecls rdoc
              Expect.equal states.Length 3 "3 states"
              Expect.equal states.[2].Kind Final "third state is final"

          testCase "US2-S2: generate guarded transition"
          <| fun _ ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = Some "active"
                    Elements =
                      [ StateDecl
                            { Identifier = "active"; Label = None; Kind = Regular
                              Children = []; Activities = None; Position = None; Annotations = [] }
                        StateDecl
                            { Identifier = "submitted"; Label = None; Kind = Regular
                              Children = []; Activities = None; Position = None; Annotations = [] }
                        TransitionElement
                            { Source = "active"; Target = Some "submitted"
                              Event = Some "submit"; Guard = Some "isValid"
                              Action = None; Parameters = []; Position = None; Annotations = [] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generate doc
              let t = (transitionsFrom "active" (parseString xml).Document).[0]
              Expect.equal t.Event (Some "submit") "event preserved"
              Expect.equal t.Guard (Some "isValid") "guard preserved"
              Expect.equal t.Target (Some "submitted") "target preserved"

          testCase "US2-S3: generate compound states"
          <| fun _ ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = Some "parent"
                    Elements =
                      [ StateDecl
                            { Identifier = "parent"; Label = None; Kind = Regular
                              Children =
                                [ { Identifier = "child1"; Label = None; Kind = Regular
                                    Children = []; Activities = None; Position = None; Annotations = [] }
                                  { Identifier = "child2"; Label = None; Kind = Regular
                                    Children = []; Activities = None; Position = None; Annotations = [] } ]
                              Activities = None; Position = None
                              Annotations = [ ScxmlAnnotation(ScxmlInitial("child1")) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generate doc
              let parent = (stateDecls (parseString xml).Document).[0]
              Expect.equal parent.Kind Regular "parent is Regular"
              Expect.equal parent.Children.Length 2 "2 children"

          testCase "US2-S4: generate datamodel"
          <| fun _ ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = Some "s1"
                    Elements =
                      [ StateDecl
                            { Identifier = "s1"; Label = None; Kind = Regular
                              Children = []; Activities = None; Position = None; Annotations = [] } ]
                    DataEntries =
                      [ { Name = "count"
                          Expression = Some "0"
                          Position = None }
                        { Name = "name"
                          Expression = Some "'default'"
                          Position = None } ]
                    Annotations = [] }

              let xml = generate doc
              let rdoc = (parseString xml).Document
              Expect.equal rdoc.DataEntries.Length 2 "2 data entries"
              Expect.equal rdoc.DataEntries.[0].Name "count" "first entry"
              Expect.equal rdoc.DataEntries.[0].Expression (Some "0") "first expr" ]

// === Generator Edge Cases and Advanced Tests (T027) ===

[<Tests>]
let generatorAdvancedTests =
    testList
        "Scxml.Generator.Advanced"
        [
          testCase "generate history nodes"
          <| fun _ ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = Some "s1"
                    Elements =
                      [ StateDecl
                            { Identifier = "s1"; Label = None; Kind = Regular
                              Children =
                                [ { Identifier = "child1"; Label = None; Kind = Regular
                                    Children = []; Activities = None; Position = None; Annotations = [] }
                                  { Identifier = "h1"; Label = None; Kind = DeepHistory
                                    Children = []; Activities = None; Position = None
                                    Annotations = [ ScxmlAnnotation(ScxmlHistory("h1", Deep, None)) ] } ]
                              Activities = None; Position = None
                              Annotations = [ ScxmlAnnotation(ScxmlInitial("child1")) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generate doc
              let state = (stateDecls (parseString xml).Document).[0]
              let historyNodes = historyChildren state
              Expect.equal historyNodes.Length 1 "has history node"
              Expect.equal historyNodes.[0].Kind DeepHistory "deep history"

          testCase "generate invoke nodes"
          <| fun _ ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = Some "s1"
                    Elements =
                      [ StateDecl
                            { Identifier = "s1"; Label = None; Kind = Regular
                              Children = []; Activities = None; Position = None
                              Annotations =
                                [ ScxmlAnnotation(ScxmlInvoke("http", Some "https://example.com/api", Some "inv1")) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generate doc
              let state = (stateDecls (parseString xml).Document).[0]
              let invokes = invokeAnnotations state
              Expect.equal invokes.Length 1 "has invoke node"
              let (invType, invSrc, invId) = invokes.[0]
              Expect.equal invType "http" "invoke type"
              Expect.equal invSrc (Some "https://example.com/api") "invoke src"
              Expect.equal invId (Some "inv1") "invoke id"

          testCase "generate empty document"
          <| fun _ ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [] }

              let xml = generate doc
              let reparsed = parseString xml
              Expect.isEmpty (stateDecls reparsed.Document) "no states"

          testCase "generate with name attribute"
          <| fun _ ->
              let doc: StatechartDocument =
                  { Title = Some "myMachine"
                    InitialStateId = Some "s1"
                    Elements =
                      [ StateDecl
                            { Identifier = "s1"; Label = None; Kind = Regular
                              Children = []; Activities = None; Position = None; Annotations = [] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generate doc
              let rdoc = (parseString xml).Document
              Expect.equal rdoc.Title (Some "myMachine") "name attribute preserved"

          testCase "generate internal transition type"
          <| fun _ ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = Some "s1"
                    Elements =
                      [ StateDecl
                            { Identifier = "s1"; Label = None; Kind = Regular
                              Children = []; Activities = None; Position = None; Annotations = [] }
                        TransitionElement
                            { Source = "s1"; Target = Some "s1"
                              Event = Some "tick"; Guard = None
                              Action = None; Parameters = []; Position = None
                              Annotations = [ ScxmlAnnotation(ScxmlTransitionType(true)) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generate doc
              let t = (transitionsFrom "s1" (parseString xml).Document).[0]
              Expect.isTrue (isInternalTransition t) "internal transition preserved"

          testCase "generate multi-target transition"
          <| fun _ ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = Some "s1"
                    Elements =
                      [ StateDecl
                            { Identifier = "s1"; Label = None; Kind = Regular
                              Children = []; Activities = None; Position = None; Annotations = [] }
                        StateDecl
                            { Identifier = "s2"; Label = None; Kind = Regular
                              Children = []; Activities = None; Position = None; Annotations = [] }
                        StateDecl
                            { Identifier = "s3"; Label = None; Kind = Regular
                              Children = []; Activities = None; Position = None; Annotations = [] }
                        TransitionElement
                            { Source = "s1"; Target = Some "s2"
                              Event = Some "fork"; Guard = None
                              Action = None; Parameters = []; Position = None
                              Annotations = [ ScxmlAnnotation(ScxmlMultiTarget([ "s2"; "s3" ])) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generate doc
              let t = (transitionsFrom "s1" (parseString xml).Document).[0]
              Expect.equal (allTargets t) [ "s2"; "s3" ] "multi-target preserved"

          testCase "generate data entry with no expression"
          <| fun _ ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = Some "s1"
                    Elements =
                      [ StateDecl
                            { Identifier = "s1"; Label = None; Kind = Regular
                              Children = []; Activities = None; Position = None; Annotations = [] } ]
                    DataEntries =
                      [ { Name = "x"
                          Expression = None
                          Position = None } ]
                    Annotations = [] }

              let xml = generate doc
              let entry = (parseString xml).Document.DataEntries.[0]
              Expect.equal entry.Name "x" "data id preserved"
              Expect.isNone entry.Expression "no expression"

          testCase "generate history with default transition"
          <| fun _ ->
              let doc: StatechartDocument =
                  { Title = None
                    InitialStateId = Some "s1"
                    Elements =
                      [ StateDecl
                            { Identifier = "s1"; Label = None; Kind = Regular
                              Children =
                                [ { Identifier = "child1"; Label = None; Kind = Regular
                                    Children = []; Activities = None; Position = None; Annotations = [] }
                                  { Identifier = "h1"; Label = None; Kind = ShallowHistory
                                    Children = []; Activities = None; Position = None
                                    Annotations = [ ScxmlAnnotation(ScxmlHistory("h1", Shallow, Some "child1")) ] } ]
                              Activities = None; Position = None
                              Annotations = [ ScxmlAnnotation(ScxmlInitial("child1")) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let xml = generate doc
              let state = (stateDecls (parseString xml).Document).[0]
              let h = (historyChildren state).[0]
              let historyMeta =
                  h.Annotations
                  |> List.tryPick (fun a ->
                      match a with
                      | ScxmlAnnotation(ScxmlHistory(_, _, defaultTarget)) -> Some defaultTarget
                      | _ -> None)
              Expect.isSome historyMeta "has ScxmlHistory annotation"
              Expect.equal historyMeta.Value (Some "child1") "default target preserved" ]
