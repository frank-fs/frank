module Frank.Statecharts.Tests.Scxml.GeneratorTests

open Expecto
open Frank.Statecharts.Scxml.Types
open Frank.Statecharts.Scxml.Generator
open Frank.Statecharts.Scxml.Parser

// === User Story 2 Acceptance Scenarios (T026) ===

[<Tests>]
let generatorTests =
    testList
        "Scxml.Generator"
        [
          testCase "US2-S1: generate basic states"
          <| fun _ ->
              let doc =
                  { Name = None
                    InitialId = Some "idle"
                    DatamodelType = None
                    Binding = None
                    States =
                      [ { Id = Some "idle"
                          Kind = Simple
                          InitialId = None
                          Transitions = []
                          Children = []
                          DataEntries = []
                          HistoryNodes = []
                          InvokeNodes = []
                          Position = None }
                        { Id = Some "active"
                          Kind = Simple
                          InitialId = None
                          Transitions = []
                          Children = []
                          DataEntries = []
                          HistoryNodes = []
                          InvokeNodes = []
                          Position = None }
                        { Id = Some "done"
                          Kind = Final
                          InitialId = None
                          Transitions = []
                          Children = []
                          DataEntries = []
                          HistoryNodes = []
                          InvokeNodes = []
                          Position = None } ]
                    DataEntries = []
                    Position = None }

              let xml = generate doc
              let reparsed = parseString xml
              Expect.isSome reparsed.Document "generated XML should reparse"
              let rdoc = reparsed.Document.Value
              Expect.equal rdoc.InitialId (Some "idle") "initial state preserved"
              Expect.equal rdoc.States.Length 3 "3 states"
              Expect.equal rdoc.States.[2].Kind Final "third state is final"

          testCase "US2-S2: generate guarded transition"
          <| fun _ ->
              let doc =
                  { Name = None
                    InitialId = Some "active"
                    DatamodelType = None
                    Binding = None
                    States =
                      [ { Id = Some "active"
                          Kind = Simple
                          InitialId = None
                          Transitions =
                            [ { Event = Some "submit"
                                Guard = Some "isValid"
                                Targets = [ "submitted" ]
                                TransitionType = External
                                Position = None } ]
                          Children = []
                          DataEntries = []
                          HistoryNodes = []
                          InvokeNodes = []
                          Position = None }
                        { Id = Some "submitted"
                          Kind = Simple
                          InitialId = None
                          Transitions = []
                          Children = []
                          DataEntries = []
                          HistoryNodes = []
                          InvokeNodes = []
                          Position = None } ]
                    DataEntries = []
                    Position = None }

              let xml = generate doc
              let t = (parseString xml).Document.Value.States.[0].Transitions.[0]
              Expect.equal t.Event (Some "submit") "event preserved"
              Expect.equal t.Guard (Some "isValid") "guard preserved"
              Expect.equal t.Targets [ "submitted" ] "target preserved"

          testCase "US2-S3: generate compound states"
          <| fun _ ->
              let doc =
                  { Name = None
                    InitialId = Some "parent"
                    DatamodelType = None
                    Binding = None
                    States =
                      [ { Id = Some "parent"
                          Kind = Compound
                          InitialId = Some "child1"
                          Transitions = []
                          Children =
                            [ { Id = Some "child1"
                                Kind = Simple
                                InitialId = None
                                Transitions = []
                                Children = []
                                DataEntries = []
                                HistoryNodes = []
                                InvokeNodes = []
                                Position = None }
                              { Id = Some "child2"
                                Kind = Simple
                                InitialId = None
                                Transitions = []
                                Children = []
                                DataEntries = []
                                HistoryNodes = []
                                InvokeNodes = []
                                Position = None } ]
                          DataEntries = []
                          HistoryNodes = []
                          InvokeNodes = []
                          Position = None } ]
                    DataEntries = []
                    Position = None }

              let xml = generate doc
              let parent = (parseString xml).Document.Value.States.[0]
              Expect.equal parent.Kind Compound "parent is compound"
              Expect.equal parent.Children.Length 2 "2 children"

          testCase "US2-S4: generate datamodel"
          <| fun _ ->
              let doc =
                  { Name = None
                    InitialId = Some "s1"
                    DatamodelType = None
                    Binding = None
                    States =
                      [ { Id = Some "s1"
                          Kind = Simple
                          InitialId = None
                          Transitions = []
                          Children = []
                          DataEntries = []
                          HistoryNodes = []
                          InvokeNodes = []
                          Position = None } ]
                    DataEntries =
                      [ { Id = "count"
                          Expression = Some "0"
                          Position = None }
                        { Id = "name"
                          Expression = Some "'default'"
                          Position = None } ]
                    Position = None }

              let xml = generate doc
              let rdoc = (parseString xml).Document.Value
              Expect.equal rdoc.DataEntries.Length 2 "2 data entries"
              Expect.equal rdoc.DataEntries.[0].Id "count" "first entry"
              Expect.equal rdoc.DataEntries.[0].Expression (Some "0") "first expr" ]

// === Generator Edge Cases and Advanced Tests (T027) ===

[<Tests>]
let generatorAdvancedTests =
    testList
        "Scxml.Generator.Advanced"
        [
          testCase "generate history nodes"
          <| fun _ ->
              let doc =
                  { Name = None
                    InitialId = Some "s1"
                    DatamodelType = None
                    Binding = None
                    States =
                      [ { Id = Some "s1"
                          Kind = Compound
                          InitialId = Some "child1"
                          Transitions = []
                          Children =
                            [ { Id = Some "child1"
                                Kind = Simple
                                InitialId = None
                                Transitions = []
                                Children = []
                                DataEntries = []
                                HistoryNodes = []
                                InvokeNodes = []
                                Position = None } ]
                          DataEntries = []
                          HistoryNodes =
                            [ { Id = "h1"
                                Kind = Deep
                                DefaultTransition = None
                                Position = None } ]
                          InvokeNodes = []
                          Position = None } ]
                    DataEntries = []
                    Position = None }

              let xml = generate doc
              let state = (parseString xml).Document.Value.States.[0]
              Expect.equal state.HistoryNodes.Length 1 "has history node"
              Expect.equal state.HistoryNodes.[0].Kind Deep "deep history"

          testCase "generate invoke nodes"
          <| fun _ ->
              let doc =
                  { Name = None
                    InitialId = Some "s1"
                    DatamodelType = None
                    Binding = None
                    States =
                      [ { Id = Some "s1"
                          Kind = Simple
                          InitialId = None
                          Transitions = []
                          Children = []
                          DataEntries = []
                          HistoryNodes = []
                          InvokeNodes =
                            [ { InvokeType = Some "http"
                                Src = Some "https://example.com/api"
                                Id = Some "inv1"
                                Position = None } ]
                          Position = None } ]
                    DataEntries = []
                    Position = None }

              let xml = generate doc
              let state = (parseString xml).Document.Value.States.[0]
              Expect.equal state.InvokeNodes.Length 1 "has invoke node"
              let inv = state.InvokeNodes.[0]
              Expect.equal inv.InvokeType (Some "http") "invoke type"
              Expect.equal inv.Src (Some "https://example.com/api") "invoke src"
              Expect.equal inv.Id (Some "inv1") "invoke id"

          testCase "generate empty document"
          <| fun _ ->
              let doc =
                  { Name = None
                    InitialId = None
                    DatamodelType = None
                    Binding = None
                    States = []
                    DataEntries = []
                    Position = None }

              let xml = generate doc
              let reparsed = parseString xml
              Expect.isSome reparsed.Document "empty doc should reparse"
              Expect.isEmpty reparsed.Document.Value.States "no states"

          testCase "generate with name attribute"
          <| fun _ ->
              let doc =
                  { Name = Some "myMachine"
                    InitialId = Some "s1"
                    DatamodelType = None
                    Binding = None
                    States =
                      [ { Id = Some "s1"
                          Kind = Simple
                          InitialId = None
                          Transitions = []
                          Children = []
                          DataEntries = []
                          HistoryNodes = []
                          InvokeNodes = []
                          Position = None } ]
                    DataEntries = []
                    Position = None }

              let xml = generate doc
              let rdoc = (parseString xml).Document.Value
              Expect.equal rdoc.Name (Some "myMachine") "name attribute preserved"

          testCase "generate internal transition type"
          <| fun _ ->
              let doc =
                  { Name = None
                    InitialId = Some "s1"
                    DatamodelType = None
                    Binding = None
                    States =
                      [ { Id = Some "s1"
                          Kind = Simple
                          InitialId = None
                          Transitions =
                            [ { Event = Some "tick"
                                Guard = None
                                Targets = [ "s1" ]
                                TransitionType = Internal
                                Position = None } ]
                          Children = []
                          DataEntries = []
                          HistoryNodes = []
                          InvokeNodes = []
                          Position = None } ]
                    DataEntries = []
                    Position = None }

              let xml = generate doc
              let t = (parseString xml).Document.Value.States.[0].Transitions.[0]
              Expect.equal t.TransitionType Internal "internal transition preserved"

          testCase "generate multi-target transition"
          <| fun _ ->
              let doc =
                  { Name = None
                    InitialId = Some "s1"
                    DatamodelType = None
                    Binding = None
                    States =
                      [ { Id = Some "s1"
                          Kind = Simple
                          InitialId = None
                          Transitions =
                            [ { Event = Some "fork"
                                Guard = None
                                Targets = [ "s2"; "s3" ]
                                TransitionType = External
                                Position = None } ]
                          Children = []
                          DataEntries = []
                          HistoryNodes = []
                          InvokeNodes = []
                          Position = None }
                        { Id = Some "s2"
                          Kind = Simple
                          InitialId = None
                          Transitions = []
                          Children = []
                          DataEntries = []
                          HistoryNodes = []
                          InvokeNodes = []
                          Position = None }
                        { Id = Some "s3"
                          Kind = Simple
                          InitialId = None
                          Transitions = []
                          Children = []
                          DataEntries = []
                          HistoryNodes = []
                          InvokeNodes = []
                          Position = None } ]
                    DataEntries = []
                    Position = None }

              let xml = generate doc
              let t = (parseString xml).Document.Value.States.[0].Transitions.[0]
              Expect.equal t.Targets [ "s2"; "s3" ] "multi-target preserved"

          testCase "generate data entry with no expression"
          <| fun _ ->
              let doc =
                  { Name = None
                    InitialId = Some "s1"
                    DatamodelType = None
                    Binding = None
                    States =
                      [ { Id = Some "s1"
                          Kind = Simple
                          InitialId = None
                          Transitions = []
                          Children = []
                          DataEntries = []
                          HistoryNodes = []
                          InvokeNodes = []
                          Position = None } ]
                    DataEntries =
                      [ { Id = "x"
                          Expression = None
                          Position = None } ]
                    Position = None }

              let xml = generate doc
              let entry = (parseString xml).Document.Value.DataEntries.[0]
              Expect.equal entry.Id "x" "data id preserved"
              Expect.isNone entry.Expression "no expression"

          testCase "generate history with default transition"
          <| fun _ ->
              let doc =
                  { Name = None
                    InitialId = Some "s1"
                    DatamodelType = None
                    Binding = None
                    States =
                      [ { Id = Some "s1"
                          Kind = Compound
                          InitialId = Some "child1"
                          Transitions = []
                          Children =
                            [ { Id = Some "child1"
                                Kind = Simple
                                InitialId = None
                                Transitions = []
                                Children = []
                                DataEntries = []
                                HistoryNodes = []
                                InvokeNodes = []
                                Position = None } ]
                          DataEntries = []
                          HistoryNodes =
                            [ { Id = "h1"
                                Kind = Shallow
                                DefaultTransition =
                                  Some
                                      { Event = None
                                        Guard = None
                                        Targets = [ "child1" ]
                                        TransitionType = External
                                        Position = None }
                                Position = None } ]
                          InvokeNodes = []
                          Position = None } ]
                    DataEntries = []
                    Position = None }

              let xml = generate doc
              let h = (parseString xml).Document.Value.States.[0].HistoryNodes.[0]
              Expect.isSome h.DefaultTransition "has default transition"
              Expect.equal h.DefaultTransition.Value.Targets [ "child1" ] "default target preserved" ]
