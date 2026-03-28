module Frank.Statecharts.Tests.Alps.JsonGeneratorTests

open System.Text.Json
open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Alps.Classification
open Frank.Statecharts.Alps.JsonParser
open Frank.Statecharts.Alps.JsonGenerator
open Frank.Statecharts.Tests.Alps.GoldenFiles

[<Tests>]
let jsonGeneratorTests =
    testList
        "Alps.JsonGenerator"
        [ testCase "generate from tic-tac-toe AST produces valid reparseable JSON"
          <| fun _ ->
              let originalResult = parseAlpsJson ticTacToeAlpsJson
              Expect.isEmpty originalResult.Errors "parse failed"
              let originalDoc = originalResult.Document

              let generatedJson = generateAlpsJson originalDoc

              let reparsedResult = parseAlpsJson generatedJson
              Expect.isEmpty reparsedResult.Errors "re-parse failed"
              let reparsedDoc = reparsedResult.Document

              Expect.equal reparsedDoc originalDoc "generated JSON round-trips to same AST"

          testCase "generate from onboarding AST produces valid reparseable JSON"
          <| fun _ ->
              let originalResult = parseAlpsJson onboardingAlpsJson
              Expect.isEmpty originalResult.Errors "parse failed"
              let originalDoc = originalResult.Document

              let generatedJson = generateAlpsJson originalDoc

              let reparsedResult = parseAlpsJson generatedJson
              Expect.isEmpty reparsedResult.Errors "re-parse failed"
              let reparsedDoc = reparsedResult.Document

              Expect.equal reparsedDoc originalDoc "generated JSON round-trips to same AST"

          testCase "generated JSON has alps root object"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsVersion "1.0") ] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let hasAlps = parsed.RootElement.TryGetProperty("alps") |> fst
              Expect.isTrue hasAlps "should have alps root"

          testCase "generated JSON preserves version"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsVersion "1.0") ] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let alps = parsed.RootElement.GetProperty("alps")
              let version = alps.GetProperty("version").GetString()
              Expect.equal version "1.0" "version preserved" ]

[<Tests>]
let jsonGeneratorStructureTests =
    testList
        "Alps.JsonGenerator structure"
        [ testCase "empty document produces minimal ALPS JSON"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let alps = parsed.RootElement.GetProperty("alps")
              // Empty doc still gets version "1.0" from generator default
              Expect.isFalse (alps.TryGetProperty("descriptor") |> fst) "no descriptors"
              Expect.isFalse (alps.TryGetProperty("link") |> fst) "no links"
              Expect.isFalse (alps.TryGetProperty("ext") |> fst) "no extensions"
              Expect.isFalse (alps.TryGetProperty("doc") |> fst) "no doc"

          testCase "state with unsafe transition produces correct type string"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "A"
                              Event = Some "test"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)

              let stateDesc = parsed.RootElement.GetProperty("alps").GetProperty("descriptor").[0]

              let transDesc = stateDesc.GetProperty("descriptor").[0]
              Expect.equal (transDesc.GetProperty("type").GetString()) "unsafe" "type is unsafe"

          testCase "all three transition types produce correct strings"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "A"
                              Event = Some "safe_t"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Safe) ] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "A"
                              Event = Some "unsafe_t"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe) ] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "A"
                              Event = Some "idempotent_t"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Idempotent) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let stateDesc = parsed.RootElement.GetProperty("alps").GetProperty("descriptor").[0]
              let children = stateDesc.GetProperty("descriptor")

              let typeOf (idx: int) =
                  children.[idx].GetProperty("type").GetString()

              Expect.equal (typeOf 0) "safe" "safe"
              Expect.equal (typeOf 1) "unsafe" "unsafe"
              Expect.equal (typeOf 2) "idempotent" "idempotent"

          testCase "guard extension is written on transition"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "A"
                              Event = Some "test"
                              Guard = Some "role=PlayerX"
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)

              let stateDesc = parsed.RootElement.GetProperty("alps").GetProperty("descriptor").[0]
              let transDesc = stateDesc.GetProperty("descriptor").[0]
              let extElem = transDesc.GetProperty("ext").[0]

              Expect.equal (extElem.GetProperty("id").GetString()) GuardExtId "ext id"
              Expect.equal (extElem.GetProperty("value").GetString()) "role=PlayerX" "ext value"

          testCase "output is indented (human-readable)"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsVersion "1.0") ] }

              let json = generateAlpsJson doc
              Expect.isTrue (json.Contains("\n")) "should contain newlines (indented)"

          testCase "documentation annotation is written correctly"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsDocumentation(Some "html", "Some <b>bold</b> text")) ] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let alpsDoc = parsed.RootElement.GetProperty("alps").GetProperty("doc")
              Expect.equal (alpsDoc.GetProperty("format").GetString()) "html" "doc format"

              Expect.equal (alpsDoc.GetProperty("value").GetString()) "Some <b>bold</b> text" "doc value"

          testCase "documentation without format omits format property"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsDocumentation(None, "Plain text")) ] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let alpsDoc = parsed.RootElement.GetProperty("alps").GetProperty("doc")
              Expect.isFalse (alpsDoc.TryGetProperty("format") |> fst) "no format property"
              Expect.equal (alpsDoc.GetProperty("value").GetString()) "Plain text" "doc value"

          testCase "link annotations are written with rel and href"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsLink("self", "http://example.com/alps/test")) ] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let link = parsed.RootElement.GetProperty("alps").GetProperty("link").[0]
              Expect.equal (link.GetProperty("rel").GetString()) "self" "link rel"
              Expect.equal (link.GetProperty("href").GetString()) "http://example.com/alps/test" "link href"

          testCase "top-level ext annotations are written"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [ AlpsAnnotation(AlpsExtension("custom", None, Some "data")) ] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let ext = parsed.RootElement.GetProperty("alps").GetProperty("ext").[0]
              Expect.equal (ext.GetProperty("id").GetString()) "custom" "ext id"
              Expect.equal (ext.GetProperty("value").GetString()) "data" "ext value"

          testCase "transition with rt field produces correct output"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        StateDecl
                            { Identifier = Some "gameState"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "gameState"
                              Event = Some "viewGame"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Safe) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)

              let stateA = parsed.RootElement.GetProperty("alps").GetProperty("descriptor").[0]

              let transDesc = stateA.GetProperty("descriptor").[0]
              Expect.equal (transDesc.GetProperty("rt").GetString()) "#gameState" "rt preserved"

          // ---------------------------------------------------------------
          // Absorbed from MapperTests.EdgeCases (generator-direction)
          // ---------------------------------------------------------------

          testCase "defaults to unsafe when transition has no AlpsTransitionType annotation"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "A"
                              Event = Some "doThing"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [] } ]
                    DataEntries = []
                    Annotations = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let stateDesc = parsed.RootElement.GetProperty("alps").GetProperty("descriptor").[0]
              let transDesc = stateDesc.GetProperty("descriptor").[0]
              Expect.equal (transDesc.GetProperty("type").GetString()) "unsafe" "should default to unsafe"

          testCase "re-adds # prefix to local rt targets"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements =
                      [ StateDecl
                            { Identifier = Some "A"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        StateDecl
                            { Identifier = Some "B"
                              Label = None
                              Kind = StateKind.Regular
                              Children = []
                              Activities = None
                              Position = None
                              Annotations = [] }
                        TransitionElement
                            { Source = "A"
                              Target = Some "B"
                              Event = Some "go"
                              Guard = None
                              Action = None
                              Parameters = []
                              Position = None
                              Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Safe) ] } ]
                    DataEntries = []
                    Annotations = [] }

              let json = generateAlpsJson doc
              Expect.isTrue (json.Contains("\"#B\"")) "local rt target should have # prefix" ]
