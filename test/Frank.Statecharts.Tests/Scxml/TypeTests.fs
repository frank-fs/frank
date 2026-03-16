module Frank.Statecharts.Tests.Scxml.TypeTests

open Expecto
open Frank.Statecharts.Scxml.Types

[<Tests>]
let typeTests =
    testList
        "Scxml.Types"
        [
          testCase "SourcePosition construction"
          <| fun _ ->
              let pos = { Line = 1; Column = 5 }
              Expect.equal pos.Line 1 "line"
              Expect.equal pos.Column 5 "column"

          testCase "ScxmlStateKind has all four cases"
          <| fun _ ->
              let kinds = [ Simple; Compound; Parallel; Final ]
              Expect.hasLength kinds 4 "four state kinds"

          testCase "ScxmlTransitionType has Internal and External"
          <| fun _ ->
              let types = [ Internal; External ]
              Expect.hasLength types 2 "two transition types"
              Expect.notEqual Internal External "Internal <> External"

          testCase "ScxmlHistoryKind has Shallow and Deep"
          <| fun _ ->
              let kinds = [ Shallow; Deep ]
              Expect.hasLength kinds 2 "two history kinds"
              Expect.notEqual Shallow Deep "Shallow <> Deep"

          testCase "DataEntry construction"
          <| fun _ ->
              let entry =
                  { Id = "counter"
                    Expression = Some "0"
                    Position = None }

              Expect.equal entry.Id "counter" "id"
              Expect.equal entry.Expression (Some "0") "expression"
              Expect.isNone entry.Position "position is None"

          testCase "ScxmlTransition construction"
          <| fun _ ->
              let t =
                  { Event = Some "submit"
                    Guard = Some "isValid"
                    Targets = [ "submitted" ]
                    TransitionType = External
                    Position = Some { Line = 3; Column = 5 } }

              Expect.equal t.Event (Some "submit") "event"
              Expect.equal t.Guard (Some "isValid") "guard"
              Expect.equal t.Targets [ "submitted" ] "targets"
              Expect.equal t.TransitionType External "transition type"
              Expect.isSome t.Position "has position"

          testCase "ScxmlState construction with nested children"
          <| fun _ ->
              let child =
                  { Id = Some "child1"
                    Kind = Simple
                    InitialId = None
                    Transitions = []
                    Children = []
                    DataEntries = []
                    HistoryNodes = []
                    InvokeNodes = []
                    Position = None }

              let parent =
                  { Id = Some "parent"
                    Kind = Compound
                    InitialId = None
                    Transitions = []
                    Children = [ child ]
                    DataEntries = []
                    HistoryNodes = []
                    InvokeNodes = []
                    Position = None }

              Expect.equal parent.Children.Length 1 "parent has one child"
              Expect.equal parent.Children.[0].Id (Some "child1") "child id"

          testCase "ScxmlDocument construction"
          <| fun _ ->
              let doc =
                  { Name = Some "test"
                    InitialId = Some "s1"
                    DatamodelType = None
                    Binding = None
                    States = []
                    DataEntries =
                      [ { Id = "x"
                          Expression = Some "1"
                          Position = None } ]
                    Position = None }

              Expect.equal doc.Name (Some "test") "name"
              Expect.equal doc.InitialId (Some "s1") "initial"
              Expect.equal doc.DataEntries.Length 1 "one data entry"

          testCase "ScxmlDocument structural equality"
          <| fun _ ->
              let doc1 =
                  { Name = Some "test"
                    InitialId = Some "s1"
                    DatamodelType = None
                    Binding = None
                    States = []
                    DataEntries = []
                    Position = None }

              let doc2 =
                  { Name = Some "test"
                    InitialId = Some "s1"
                    DatamodelType = None
                    Binding = None
                    States = []
                    DataEntries = []
                    Position = None }

              Expect.equal doc1 doc2 "identical documents should be equal"

          testCase "ScxmlDocument inequality on different InitialId"
          <| fun _ ->
              let doc1 =
                  { Name = None
                    InitialId = Some "s1"
                    DatamodelType = None
                    Binding = None
                    States = []
                    DataEntries = []
                    Position = None }

              let doc2 =
                  { Name = None
                    InitialId = Some "s2"
                    DatamodelType = None
                    Binding = None
                    States = []
                    DataEntries = []
                    Position = None }

              Expect.notEqual doc1 doc2 "different InitialId should not be equal" ]
