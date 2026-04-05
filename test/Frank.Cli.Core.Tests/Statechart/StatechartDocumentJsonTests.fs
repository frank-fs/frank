module Frank.Cli.Core.Tests.Statechart.StatechartDocumentJsonTests

open System.Text.Json
open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Validation
open Frank.Cli.Core.Statechart.StatechartDocumentJson

let private emptyDoc : StatechartDocument =
    { Title = None
      InitialStateId = None
      Elements = []
      DataEntries = []
      Annotations = [] }

let private simpleState kind id : StateNode =
    { Identifier = Some id
      Label = None
      Kind = kind
      Children = []
      Activities = None
      Position = None
      Annotations = [] }

let private simpleTransition src tgt evt : TransitionEdge =
    { Source = src
      Target = Some tgt
      Event = Some evt
      Guard = None
      GuardHref = None
      Action = None
      Parameters = []
      SenderRole = None
      ReceiverRole = None
      PayloadType = None
      Position = None
      Annotations = [] }

let private docWithHierarchy : StatechartDocument =
    let child = simpleState Regular "ChildState"
    let parent =
        { (simpleState Parallel "ParentState") with
            Children = [ child ] }
    { emptyDoc with
        Title = Some "Test"
        Elements = [ StateDecl parent ] }

let private docWithNote : StatechartDocument =
    let note : NoteContent =
        { Target = "SomeState"
          Content = "This is a note"
          Position = None
          Annotations = [] }
    { emptyDoc with
        Elements = [ NoteElement note ] }

let private docWithDirective : StatechartDocument =
    { emptyDoc with
        Elements = [ DirectiveElement (TitleDirective("My Title", None)) ] }

let private docWithTransitions : StatechartDocument =
    let t = simpleTransition "A" "B" "go"
    { emptyDoc with
        Elements =
            [ StateDecl (simpleState Initial "A")
              StateDecl (simpleState Final "B")
              TransitionElement t ] }

let private parseJson (s: string) = JsonDocument.Parse(s)

[<Tests>]
let tests =
    testList "StatechartDocumentJson" [
        testList "serializeDocument" [
            testCase "empty document produces valid JSON" <| fun _ ->
                let json = serializeDocument emptyDoc
                let doc = parseJson json
                Expect.equal (doc.RootElement.GetProperty("elements").GetArrayLength()) 0 "should have empty elements"

            testCase "states with children produce nested JSON" <| fun _ ->
                let json = serializeDocument docWithHierarchy
                let doc = parseJson json
                let elements = doc.RootElement.GetProperty("elements")
                Expect.equal (elements.GetArrayLength()) 1 "should have 1 top-level element"
                let parentEl = elements.[0]
                Expect.equal (parentEl.GetProperty("type").GetString()) "state" "should be state type"
                Expect.equal (parentEl.GetProperty("identifier").GetString()) "ParentState" "should have parent identifier"
                let children = parentEl.GetProperty("children")
                Expect.equal (children.GetArrayLength()) 1 "should have 1 child"
                Expect.equal (children.[0].GetProperty("identifier").GetString()) "ChildState" "child should have correct identifier"

            testCase "hierarchical states are all reachable through elements" <| fun _ ->
                let json = serializeDocument docWithHierarchy
                let doc = parseJson json
                let elements = doc.RootElement.GetProperty("elements")
                // Parent state is a top-level element
                Expect.equal (elements.GetArrayLength()) 1 "should have 1 top-level element"
                let parent = elements.[0]
                Expect.equal (parent.GetProperty("identifier").GetString()) "ParentState" "parent identifier"
                Expect.equal (parent.GetProperty("kind").GetString()) "Parallel" "parent kind"
                // Child state is nested inside parent's children array
                let children = parent.GetProperty("children")
                Expect.equal (children.GetArrayLength()) 1 "should have 1 child"
                let child = children.[0]
                Expect.equal (child.GetProperty("identifier").GetString()) "ChildState" "child identifier"
                Expect.equal (child.GetProperty("kind").GetString()) "Regular" "child kind"

            testCase "transitions appear in elements" <| fun _ ->
                let json = serializeDocument docWithTransitions
                let doc = parseJson json
                let elements = doc.RootElement.GetProperty("elements")
                let hasTransition =
                    [ 0 .. elements.GetArrayLength() - 1 ]
                    |> List.exists (fun i -> elements.[i].GetProperty("type").GetString() = "transition")
                Expect.isTrue hasTransition "elements should contain a transition"

            testCase "notes appear in elements" <| fun _ ->
                let json = serializeDocument docWithNote
                let doc = parseJson json
                let elements = doc.RootElement.GetProperty("elements")
                Expect.equal (elements.GetArrayLength()) 1 "should have 1 element"
                Expect.equal (elements.[0].GetProperty("type").GetString()) "note" "should be note type"
                Expect.equal (elements.[0].GetProperty("target").GetString()) "SomeState" "should have target"

            testCase "directives appear in elements" <| fun _ ->
                let json = serializeDocument docWithDirective
                let doc = parseJson json
                let elements = doc.RootElement.GetProperty("elements")
                Expect.equal (elements.GetArrayLength()) 1 "should have 1 element"
                Expect.equal (elements.[0].GetProperty("type").GetString()) "directive" "should be directive type"
                Expect.equal (elements.[0].GetProperty("directiveType").GetString()) "title" "should be title directive"
                Expect.equal (elements.[0].GetProperty("value").GetString()) "My Title" "should have title value"

            testCase "StateKind uses explicit toString" <| fun _ ->
                let json = serializeDocument docWithTransitions
                let doc = parseJson json
                let elements = doc.RootElement.GetProperty("elements")
                // First two elements are StateDecl entries
                Expect.equal (elements.[0].GetProperty("kind").GetString()) "Initial" "should use Initial not %A"
                Expect.equal (elements.[1].GetProperty("kind").GetString()) "Final" "should use Final not %A"

            // AC-7: JSON serialization includes new TransitionEdge role/payload fields
            testCase "transition with SenderRole ReceiverRole PayloadType serializes all three fields" <| fun _ ->
                let t =
                    { simpleTransition "Client" "Server" "PlaceOrder" with
                        SenderRole = Some "Client"
                        ReceiverRole = Some "Server"
                        PayloadType = Some "OrderDetails" }
                let doc =
                    { emptyDoc with
                        Elements =
                            [ StateDecl (simpleState Initial "Client")
                              StateDecl (simpleState Regular "Server")
                              TransitionElement t ] }
                let json = serializeDocument doc
                let parsed = parseJson json
                let elements = parsed.RootElement.GetProperty("elements")
                // Find the transition element in the elements array
                let transitionEl =
                    [ 0 .. elements.GetArrayLength() - 1 ]
                    |> List.tryPick (fun i ->
                        let el = elements.[i]
                        if el.GetProperty("type").GetString() = "transition" then Some el
                        else None)
                Expect.isSome transitionEl "should have a transition element"
                let te = transitionEl.Value
                Expect.equal (te.GetProperty("senderRole").GetString()) "Client" "senderRole should be Client"
                Expect.equal (te.GetProperty("receiverRole").GetString()) "Server" "receiverRole should be Server"
                Expect.equal (te.GetProperty("payloadType").GetString()) "OrderDetails" "payloadType should be OrderDetails"

            testCase "transition with None role/payload fields omits those properties from JSON" <| fun _ ->
                let t = simpleTransition "A" "B" "go"
                let doc =
                    { emptyDoc with
                        Elements =
                            [ StateDecl (simpleState Initial "A")
                              StateDecl (simpleState Final "B")
                              TransitionElement t ] }
                let json = serializeDocument doc
                let parsed = parseJson json
                let elements = parsed.RootElement.GetProperty("elements")
                let transitionEl =
                    [ 0 .. elements.GetArrayLength() - 1 ]
                    |> List.tryPick (fun i ->
                        let el = elements.[i]
                        if el.GetProperty("type").GetString() = "transition" then Some el
                        else None)
                Expect.isSome transitionEl "should have a transition element"
                let te = transitionEl.Value
                Expect.isFalse (te.TryGetProperty("senderRole") |> fst) "senderRole must be absent when None"
                Expect.isFalse (te.TryGetProperty("receiverRole") |> fst) "receiverRole must be absent when None"
                Expect.isFalse (te.TryGetProperty("payloadType") |> fst) "payloadType must be absent when None"

            testCase "deep hierarchy serializes 3 levels of nesting" <| fun _ ->
                let grandchild = simpleState Regular "GrandchildState"
                let parent =
                    { (simpleState Parallel "ParentState") with
                        Children =
                            [ { (simpleState Regular "ChildState") with
                                    Children = [ grandchild ] } ] }
                let doc =
                    { emptyDoc with
                        Elements = [ StateDecl parent ] }
                let json = serializeDocument doc
                let parsed = parseJson json
                let elements = parsed.RootElement.GetProperty("elements")
                Expect.equal (elements.GetArrayLength()) 1 "should have 1 top-level element"
                let parentEl = elements.[0]
                Expect.equal (parentEl.GetProperty("identifier").GetString()) "ParentState" "grandparent identifier"
                let level2 = parentEl.GetProperty("children")
                Expect.equal (level2.GetArrayLength()) 1 "parent should have 1 child"
                let childEl = level2.[0]
                Expect.equal (childEl.GetProperty("identifier").GetString()) "ChildState" "child identifier"
                let level3 = childEl.GetProperty("children")
                Expect.equal (level3.GetArrayLength()) 1 "child should have 1 grandchild"
                Expect.equal (level3.[0].GetProperty("identifier").GetString()) "GrandchildState" "grandchild identifier"

            testCase "mixed states and transitions produces correct element count and types" <| fun _ ->
                let doc =
                    { emptyDoc with
                        Elements =
                            [ StateDecl (simpleState Initial "S1")
                              StateDecl (simpleState Regular "S2")
                              StateDecl (simpleState Final "S3")
                              TransitionElement (simpleTransition "S1" "S2" "step")
                              TransitionElement (simpleTransition "S2" "S3" "done") ] }
                let json = serializeDocument doc
                let parsed = parseJson json
                let elements = parsed.RootElement.GetProperty("elements")
                Expect.equal (elements.GetArrayLength()) 5 "should have 5 elements total (3 states + 2 transitions)"
                let types =
                    [ 0 .. elements.GetArrayLength() - 1 ]
                    |> List.map (fun i -> elements.[i].GetProperty("type").GetString())
                let stateCount = types |> List.filter (fun t -> t = "state") |> List.length
                let transitionCount = types |> List.filter (fun t -> t = "transition") |> List.length
                Expect.equal stateCount 3 "should have 3 state elements"
                Expect.equal transitionCount 2 "should have 2 transition elements"
        ]

        testList "serializeParseResult" [
            testCase "includes errors array" <| fun _ ->
                let result : ParseResult =
                    { Document = emptyDoc
                      Errors =
                        [ { Position = Some { Line = 1; Column = 5 }
                            Description = "test error"
                            Expected = "something"
                            Found = "nothing"
                            CorrectiveExample = "" } ]
                      Warnings = [] }
                let json = serializeParseResult result
                let doc = parseJson json
                let errors = doc.RootElement.GetProperty("errors")
                Expect.equal (errors.GetArrayLength()) 1 "should have 1 error"
                Expect.equal (errors.[0].GetProperty("description").GetString()) "test error" "should include description"

            testCase "serializeParseResult includes transitions in document elements" <| fun _ ->
                let result : ParseResult =
                    { Document =
                        { emptyDoc with
                            Elements =
                                [ StateDecl (simpleState Initial "Start")
                                  StateDecl (simpleState Final "End")
                                  TransitionElement (simpleTransition "Start" "End" "finish") ] }
                      Errors = []
                      Warnings = [] }
                let json = serializeParseResult result
                let parsed = parseJson json
                let elements = parsed.RootElement.GetProperty("document").GetProperty("elements")
                let hasTransition =
                    [ 0 .. elements.GetArrayLength() - 1 ]
                    |> List.exists (fun i -> elements.[i].GetProperty("type").GetString() = "transition")
                Expect.isTrue hasTransition "document.elements should contain a transition"
                Expect.equal (elements.GetArrayLength()) 3 "document.elements should have 3 items (2 states + 1 transition)"
        ]

        testList "serializeParseResultWithFormat" [
            testCase "includes format field" <| fun _ ->
                let result : ParseResult =
                    { Document = emptyDoc; Errors = []; Warnings = [] }
                let json = serializeParseResultWithFormat result Wsd
                let doc = parseJson json
                Expect.equal (doc.RootElement.GetProperty("format").GetString()) "wsd" "should include format"

            testCase "includes document and empty arrays" <| fun _ ->
                let result : ParseResult =
                    { Document = emptyDoc; Errors = []; Warnings = [] }
                let json = serializeParseResultWithFormat result Alps
                let doc = parseJson json
                Expect.equal (doc.RootElement.GetProperty("format").GetString()) "alps" "should be alps"
                Expect.isTrue (doc.RootElement.TryGetProperty("document") |> fst) "should have document"
                Expect.equal (doc.RootElement.GetProperty("errors").GetArrayLength()) 0 "should have empty errors"
                Expect.equal (doc.RootElement.GetProperty("warnings").GetArrayLength()) 0 "should have empty warnings"
        ]
    ]
