module Frank.Statecharts.Tests.Scxml.TypeTests

open Expecto
open Frank.Statecharts.Ast

[<Tests>]
let typeTests =
    testList
        "Scxml.Types"
        [
          testCase "SourcePosition construction"
          <| fun _ ->
              let pos: SourcePosition = { Line = 1; Column = 5 }
              Expect.equal pos.Line 1 "line"
              Expect.equal pos.Column 5 "column"

          testCase "StateKind has Regular, Parallel, and Final cases"
          <| fun _ ->
              let kinds: StateKind list = [ Regular; Parallel; Final ]
              Expect.hasLength kinds 3 "three SCXML-relevant state kinds"

          testCase "HistoryKind has Shallow and Deep"
          <| fun _ ->
              let kinds = [ Shallow; Deep ]
              Expect.hasLength kinds 2 "two history kinds"
              Expect.notEqual Shallow Deep "Shallow <> Deep"

          testCase "DataEntry construction"
          <| fun _ ->
              let entry: DataEntry =
                  { Name = "counter"
                    Expression = Some "0"
                    Position = None }

              Expect.equal entry.Name "counter" "name"
              Expect.equal entry.Expression (Some "0") "expression"
              Expect.isNone entry.Position "position is None"

          testCase "TransitionEdge construction"
          <| fun _ ->
              let t: TransitionEdge =
                  { Source = "s1"
                    Target = Some "submitted"
                    Event = Some "submit"
                    Guard = Some "isValid"
                    Action = None
                    Parameters = []
                    Position = Some { Line = 3; Column = 5 }
                    Annotations = [] }

              Expect.equal t.Event (Some "submit") "event"
              Expect.equal t.Guard (Some "isValid") "guard"
              Expect.equal t.Target (Some "submitted") "target"
              Expect.equal t.Source "s1" "source"
              Expect.isSome t.Position "has position"

          testCase "StateNode construction with nested children"
          <| fun _ ->
              let child: StateNode =
                  { Identifier = Some "child1"
                    Label = None
                    Kind = Regular
                    Children = []
                    Activities = None
                    Position = None
                    Annotations = [] }

              let parent: StateNode =
                  { Identifier = Some "parent"
                    Label = None
                    Kind = Regular
                    Children = [ child ]
                    Activities = None
                    Position = None
                    Annotations = [] }

              Expect.equal parent.Children.Length 1 "parent has one child"
              Expect.equal parent.Children.[0].Identifier (Some "child1") "child id"

          testCase "StatechartDocument construction"
          <| fun _ ->
              let doc: StatechartDocument =
                  { Title = Some "test"
                    InitialStateId = Some "s1"
                    Elements = []
                    DataEntries =
                      [ { Name = "x"
                          Expression = Some "1"
                          Position = None } ]
                    Annotations = [] }

              Expect.equal doc.Title (Some "test") "title"
              Expect.equal doc.InitialStateId (Some "s1") "initial"
              Expect.equal doc.DataEntries.Length 1 "one data entry"

          testCase "StatechartDocument structural equality"
          <| fun _ ->
              let doc1: StatechartDocument =
                  { Title = Some "test"
                    InitialStateId = Some "s1"
                    Elements = []
                    DataEntries = []
                    Annotations = [] }

              let doc2: StatechartDocument =
                  { Title = Some "test"
                    InitialStateId = Some "s1"
                    Elements = []
                    DataEntries = []
                    Annotations = [] }

              Expect.equal doc1 doc2 "identical documents should be equal"

          testCase "StatechartDocument inequality on different InitialStateId"
          <| fun _ ->
              let doc1: StatechartDocument =
                  { Title = None
                    InitialStateId = Some "s1"
                    Elements = []
                    DataEntries = []
                    Annotations = [] }

              let doc2: StatechartDocument =
                  { Title = None
                    InitialStateId = Some "s2"
                    Elements = []
                    DataEntries = []
                    Annotations = [] }

              Expect.notEqual doc1 doc2 "different InitialStateId should not be equal"

          // === ScxmlMeta annotation tests ===

          testCase "ScxmlTransitionType annotation"
          <| fun _ ->
              let ann = ScxmlAnnotation(ScxmlTransitionType(true))
              match ann with
              | ScxmlAnnotation(ScxmlTransitionType(isInternal)) ->
                  Expect.isTrue isInternal "should be internal"
              | _ -> failtest "wrong annotation type"

          testCase "ScxmlMultiTarget annotation"
          <| fun _ ->
              let ann = ScxmlAnnotation(ScxmlMultiTarget([ "s1"; "s2"; "s3" ]))
              match ann with
              | ScxmlAnnotation(ScxmlMultiTarget(targets)) ->
                  Expect.equal targets [ "s1"; "s2"; "s3" ] "targets"
              | _ -> failtest "wrong annotation type"

          testCase "ScxmlInvoke annotation"
          <| fun _ ->
              let ann = ScxmlAnnotation(ScxmlInvoke("http", Some "url", Some "id"))
              match ann with
              | ScxmlAnnotation(ScxmlInvoke(t, src, id)) ->
                  Expect.equal t "http" "invoke type"
                  Expect.equal src (Some "url") "src"
                  Expect.equal id (Some "id") "id"
              | _ -> failtest "wrong annotation type"

          testCase "ScxmlHistory annotation"
          <| fun _ ->
              let ann = ScxmlAnnotation(ScxmlHistory("h1", Deep, Some "child1"))
              match ann with
              | ScxmlAnnotation(ScxmlHistory(id, kind, defaultTarget)) ->
                  Expect.equal id "h1" "history id"
                  Expect.equal kind Deep "history kind"
                  Expect.equal defaultTarget (Some "child1") "default target"
              | _ -> failtest "wrong annotation type"

          testCase "ScxmlDatamodelType annotation"
          <| fun _ ->
              let ann = ScxmlAnnotation(ScxmlDatamodelType("ecmascript"))
              match ann with
              | ScxmlAnnotation(ScxmlDatamodelType(dm)) ->
                  Expect.equal dm "ecmascript" "datamodel type"
              | _ -> failtest "wrong annotation type"

          testCase "ScxmlBinding annotation"
          <| fun _ ->
              let ann = ScxmlAnnotation(ScxmlBinding("late"))
              match ann with
              | ScxmlAnnotation(ScxmlBinding(b)) ->
                  Expect.equal b "late" "binding"
              | _ -> failtest "wrong annotation type"

          testCase "ScxmlInitial annotation"
          <| fun _ ->
              let ann = ScxmlAnnotation(ScxmlInitial("child1"))
              match ann with
              | ScxmlAnnotation(ScxmlInitial(id)) ->
                  Expect.equal id "child1" "initial id"
              | _ -> failtest "wrong annotation type" ]
