module Frank.Statecharts.Tests.Ast.PartialPopulationTests

open Expecto
open Frank.Statecharts.Ast

// ---------------------------------------------------------------------------
// T024: Partial population tests -- each format populates different subsets
// ---------------------------------------------------------------------------

[<Tests>]
let partialPopulationTests =
    testList
        "Ast.PartialPopulation"
        [
          // US3-S4: WSD-like population
          testCase "WSD parser: states, transitions, groups -- no hierarchy, no final state"
          <| fun _ ->
              let client =
                  { Identifier = Some "Client"
                    Label = None
                    Kind = Regular
                    Children = []
                    Activities = None
                    Position = Some { Line = 1; Column = 1 }
                    Annotations = [] }

              let server =
                  { Identifier = Some "Server"
                    Label = None
                    Kind = Regular
                    Children = []
                    Activities = None
                    Position = Some { Line = 2; Column = 1 }
                    Annotations = [] }

              let transition =
                  { Source = "Client"
                    Target = Some "Server"
                    Event = Some "request"
                    Guard = None
                    Action = None
                    Parameters = []
                    Position = Some { Line = 3; Column = 1 }
                    Annotations =
                        [ WsdAnnotation(
                              WsdTransitionStyle
                                  { ArrowStyle = Solid
                                    Direction = Forward }) ] }

              let doc =
                  { Title = Some "WSD Example"
                    InitialStateId = None
                    Elements = [ StateDecl client; StateDecl server; TransitionElement transition ]
                    DataEntries = []
                    Annotations = [] }

              Expect.isEmpty doc.DataEntries "WSD has no data model"
              Expect.isEmpty client.Children "WSD has no hierarchy"
              Expect.isNone doc.InitialStateId "WSD has no explicit initial state"
              Expect.equal client.Kind Regular "WSD states are Regular"

          // US3-S1: smcat-like population
          testCase "smcat parser: states with hierarchy -- no data model"
          <| fun _ ->
              let child1 =
                  { Identifier = Some "sub1"
                    Label = None
                    Kind = Regular
                    Children = []
                    Activities = None
                    Position = None
                    Annotations = [] }

              let child2 =
                  { Identifier = Some "sub2"
                    Label = None
                    Kind = Regular
                    Children = []
                    Activities = None
                    Position = None
                    Annotations = [] }

              let parent =
                  { Identifier = Some "parent"
                    Label = None
                    Kind = Regular
                    Children = [ child1; child2 ]
                    Activities = None
                    Position = None
                    Annotations = [] }

              let initial =
                  { Identifier = Some "start"
                    Label = None
                    Kind = Initial
                    Children = []
                    Activities = None
                    Position = None
                    Annotations = [] }

              let final =
                  { Identifier = Some "end"
                    Label = None
                    Kind = Final
                    Children = []
                    Activities = None
                    Position = None
                    Annotations = [] }

              let doc =
                  { Title = None
                    InitialStateId = Some "start"
                    Elements = [ StateDecl initial; StateDecl parent; StateDecl final ]
                    DataEntries = []
                    Annotations = [] }

              Expect.isEmpty doc.DataEntries "smcat has no data model"
              Expect.hasLength parent.Children 2 "hierarchy preserved"
              Expect.equal initial.Kind Initial "initial state"
              Expect.equal final.Kind Final "final state"

          // US3-S2: ALPS-like population
          testCase "ALPS parser: states and transitions with transition type -- no initial state"
          <| fun _ ->
              let desc1 =
                  { Identifier = Some "user"
                    Label = Some "User Descriptor"
                    Kind = Regular
                    Children = []
                    Activities = None
                    Position = None
                    Annotations = [] }

              let transition =
                  { Source = "user"
                    Target = Some "profile"
                    Event = None
                    Guard = None
                    Action = None
                    Parameters = []
                    Position = None
                    Annotations = [ AlpsAnnotation(AlpsTransitionType Idempotent) ] }

              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = [ StateDecl desc1; TransitionElement transition ]
                    DataEntries = []
                    Annotations = [] }

              Expect.isNone doc.InitialStateId "ALPS has no initial state concept"
              Expect.isNone doc.Title "ALPS may have no title"

          // US3-S3: SCXML-like population (most expressive)
          testCase "SCXML parser: full population (most expressive format)"
          <| fun _ ->
              let state =
                  { Identifier = Some "s1"
                    Label = None
                    Kind = Regular
                    Children = []
                    Activities =
                        Some
                            { Entry = [ "action1" ]
                              Exit = [ "action2" ]
                              Do = [] }
                    Position = Some { Line = 5; Column = 3 }
                    Annotations = [ ScxmlAnnotation(ScxmlNamespace "http://www.w3.org/2005/07/scxml") ] }

              let finalState =
                  { Identifier = Some "done"
                    Label = None
                    Kind = Final
                    Children = []
                    Activities = None
                    Position = None
                    Annotations = [] }

              let data =
                  { Name = "counter"
                    Expression = Some "0"
                    Position = None }

              let transition =
                  { Source = "s1"
                    Target = Some "done"
                    Event = Some "finish"
                    Guard = Some "counter > 3"
                    Action = Some "logDone"
                    Parameters = []
                    Position = None
                    Annotations = [] }

              let doc =
                  { Title = Some "SCXML Machine"
                    InitialStateId = Some "s1"
                    Elements =
                        [ StateDecl state
                          StateDecl finalState
                          TransitionElement transition ]
                    DataEntries = [ data ]
                    Annotations = [] }

              Expect.isSome doc.InitialStateId "SCXML has initial state"
              Expect.isNonEmpty doc.DataEntries "SCXML has data model"
              Expect.isSome state.Activities "SCXML has state activities"
              Expect.isSome doc.Title "SCXML has title"

          // XState-like population
          testCase "XState parser: states with context and actions -- no grouping blocks"
          <| fun _ ->
              let state =
                  { Identifier = Some "active"
                    Label = None
                    Kind = Regular
                    Children = []
                    Activities =
                        Some
                            { Entry = [ "startTimer" ]
                              Exit = [ "stopTimer" ]
                              Do = [] }
                    Position = None
                    Annotations = [ XStateAnnotation(XStateAction "logEntry") ] }

              let data =
                  { Name = "retries"
                    Expression = Some "0"
                    Position = None }

              let doc =
                  { Title = None
                    InitialStateId = Some "active"
                    Elements = [ StateDecl state ]
                    DataEntries = [ data ]
                    Annotations = [] }

              let hasGrouping =
                  doc.Elements
                  |> List.exists (function
                      | GroupElement _ -> true
                      | _ -> false)

              Expect.isFalse hasGrouping "XState has no grouping blocks"
              Expect.isNonEmpty doc.DataEntries "XState has context data" ]
