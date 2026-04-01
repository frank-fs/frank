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
                Expect.equal (doc.RootElement.GetProperty("states").GetArrayLength()) 0 "should have empty states"
                Expect.equal (doc.RootElement.GetProperty("transitions").GetArrayLength()) 0 "should have empty transitions"

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

            testCase "flattened states include parent and child" <| fun _ ->
                let json = serializeDocument docWithHierarchy
                let doc = parseJson json
                let states = doc.RootElement.GetProperty("states")
                Expect.equal (states.GetArrayLength()) 2 "should flatten to 2 states"

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
                let states = doc.RootElement.GetProperty("states")
                Expect.equal (states.[0].GetProperty("kind").GetString()) "Initial" "should use Initial not %A"
                Expect.equal (states.[1].GetProperty("kind").GetString()) "Final" "should use Final not %A"
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
