module Frank.Statecharts.Tests.Alps.JsonGeneratorTests

open System.Text.Json
open Expecto
open Frank.Statecharts.Alps.Types
open Frank.Statecharts.Alps.JsonParser
open Frank.Statecharts.Alps.JsonGenerator
open Frank.Statecharts.Tests.Alps.GoldenFiles

[<Tests>]
let jsonGeneratorTests =
    testList
        "Alps.JsonGenerator"
        [ testCase "generate from tic-tac-toe AST produces valid reparseable JSON"
          <| fun _ ->
              let originalDoc =
                  parseAlpsJson ticTacToeAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let generatedJson = generateAlpsJson originalDoc

              let reparsedDoc =
                  parseAlpsJson generatedJson
                  |> Result.defaultWith (fun _ -> failwith "re-parse failed")

              Expect.equal reparsedDoc originalDoc "generated JSON round-trips to same AST"

          testCase "generate from onboarding AST produces valid reparseable JSON"
          <| fun _ ->
              let originalDoc =
                  parseAlpsJson onboardingAlpsJson
                  |> Result.defaultWith (fun _ -> failwith "parse failed")

              let generatedJson = generateAlpsJson originalDoc

              let reparsedDoc =
                  parseAlpsJson generatedJson
                  |> Result.defaultWith (fun _ -> failwith "re-parse failed")

              Expect.equal reparsedDoc originalDoc "generated JSON round-trips to same AST"

          testCase "generated JSON has alps root object"
          <| fun _ ->
              let doc =
                  { Version = Some "1.0"
                    Documentation = None
                    Descriptors = []
                    Links = []
                    Extensions = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let hasAlps = parsed.RootElement.TryGetProperty("alps") |> fst
              Expect.isTrue hasAlps "should have alps root"

          testCase "generated JSON preserves version"
          <| fun _ ->
              let doc =
                  { Version = Some "1.0"
                    Documentation = None
                    Descriptors = []
                    Links = []
                    Extensions = [] }

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
                  { Version = None
                    Documentation = None
                    Descriptors = []
                    Links = []
                    Extensions = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let alps = parsed.RootElement.GetProperty("alps")
              Expect.isFalse (alps.TryGetProperty("version") |> fst) "no version"
              Expect.isFalse (alps.TryGetProperty("descriptor") |> fst) "no descriptors"
              Expect.isFalse (alps.TryGetProperty("link") |> fst) "no links"
              Expect.isFalse (alps.TryGetProperty("ext") |> fst) "no extensions"
              Expect.isFalse (alps.TryGetProperty("doc") |> fst) "no doc"

          testCase "descriptor type is written as string"
          <| fun _ ->
              let desc =
                  { Id = Some "test"
                    Type = Unsafe
                    Href = None
                    ReturnType = None
                    Documentation = None
                    Descriptors = []
                    Extensions = []
                    Links = [] }

              let doc =
                  { Version = None
                    Documentation = None
                    Descriptors = [ desc ]
                    Links = []
                    Extensions = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)

              let descriptor =
                  parsed.RootElement.GetProperty("alps").GetProperty("descriptor").[0]

              Expect.equal (descriptor.GetProperty("type").GetString()) "unsafe" "type is unsafe"

          testCase "all four descriptor types produce correct strings"
          <| fun _ ->
              let makeDesc dt =
                  { Id = Some(sprintf "test_%A" dt)
                    Type = dt
                    Href = None
                    ReturnType = None
                    Documentation = None
                    Descriptors = []
                    Extensions = []
                    Links = [] }

              let doc =
                  { Version = None
                    Documentation = None
                    Descriptors = [ makeDesc Semantic; makeDesc Safe; makeDesc Unsafe; makeDesc Idempotent ]
                    Links = []
                    Extensions = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let descriptors = parsed.RootElement.GetProperty("alps").GetProperty("descriptor")
              Expect.equal (descriptors.[0].GetProperty("type").GetString()) "semantic" "semantic"
              Expect.equal (descriptors.[1].GetProperty("type").GetString()) "safe" "safe"
              Expect.equal (descriptors.[2].GetProperty("type").GetString()) "unsafe" "unsafe"
              Expect.equal (descriptors.[3].GetProperty("type").GetString()) "idempotent" "idempotent"

          testCase "nested descriptors are written recursively"
          <| fun _ ->
              let child =
                  { Id = Some "child"
                    Type = Safe
                    Href = None
                    ReturnType = None
                    Documentation = None
                    Descriptors = []
                    Extensions = []
                    Links = [] }

              let parent =
                  { Id = Some "parent"
                    Type = Semantic
                    Href = None
                    ReturnType = None
                    Documentation = None
                    Descriptors = [ child ]
                    Extensions = []
                    Links = [] }

              let doc =
                  { Version = None
                    Documentation = None
                    Descriptors = [ parent ]
                    Links = []
                    Extensions = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)

              let parentDesc =
                  parsed.RootElement.GetProperty("alps").GetProperty("descriptor").[0]

              let childDesc = parentDesc.GetProperty("descriptor").[0]
              Expect.equal (childDesc.GetProperty("id").GetString()) "child" "nested child present"

          testCase "ext elements are written with id and value"
          <| fun _ ->
              let ext =
                  { Id = "guard"
                    Href = None
                    Value = Some "role=PlayerX" }

              let desc =
                  { Id = Some "test"
                    Type = Unsafe
                    Href = None
                    ReturnType = None
                    Documentation = None
                    Descriptors = []
                    Extensions = [ ext ]
                    Links = [] }

              let doc =
                  { Version = None
                    Documentation = None
                    Descriptors = [ desc ]
                    Links = []
                    Extensions = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)

              let extElem =
                  parsed.RootElement
                      .GetProperty("alps")
                      .GetProperty("descriptor").[0]
                      .GetProperty("ext").[0]

              Expect.equal (extElem.GetProperty("id").GetString()) "guard" "ext id"
              Expect.equal (extElem.GetProperty("value").GetString()) "role=PlayerX" "ext value"

          testCase "ext element with href and no value"
          <| fun _ ->
              let ext =
                  { Id = "ref"
                    Href = Some "http://example.com/ext"
                    Value = None }

              let desc =
                  { Id = Some "test"
                    Type = Semantic
                    Href = None
                    ReturnType = None
                    Documentation = None
                    Descriptors = []
                    Extensions = [ ext ]
                    Links = [] }

              let doc =
                  { Version = None
                    Documentation = None
                    Descriptors = [ desc ]
                    Links = []
                    Extensions = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)

              let extElem =
                  parsed.RootElement
                      .GetProperty("alps")
                      .GetProperty("descriptor").[0]
                      .GetProperty("ext").[0]

              Expect.equal (extElem.GetProperty("id").GetString()) "ref" "ext id"
              Expect.equal (extElem.GetProperty("href").GetString()) "http://example.com/ext" "ext href"
              Expect.isFalse (extElem.TryGetProperty("value") |> fst) "no value property"

          testCase "output is indented (human-readable)"
          <| fun _ ->
              let doc =
                  { Version = Some "1.0"
                    Documentation = None
                    Descriptors = []
                    Links = []
                    Extensions = [] }

              let json = generateAlpsJson doc
              Expect.isTrue (json.Contains("\n")) "should contain newlines (indented)"

          testCase "documentation with format is written correctly"
          <| fun _ ->
              let docElem =
                  { Format = Some "html"
                    Value = "Some <b>bold</b> text" }

              let doc =
                  { Version = None
                    Documentation = Some docElem
                    Descriptors = []
                    Links = []
                    Extensions = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let alpsDoc = parsed.RootElement.GetProperty("alps").GetProperty("doc")
              Expect.equal (alpsDoc.GetProperty("format").GetString()) "html" "doc format"

              Expect.equal
                  (alpsDoc.GetProperty("value").GetString())
                  "Some <b>bold</b> text"
                  "doc value"

          testCase "documentation without format omits format property"
          <| fun _ ->
              let docElem = { Format = None; Value = "Plain text" }

              let doc =
                  { Version = None
                    Documentation = Some docElem
                    Descriptors = []
                    Links = []
                    Extensions = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let alpsDoc = parsed.RootElement.GetProperty("alps").GetProperty("doc")
              Expect.isFalse (alpsDoc.TryGetProperty("format") |> fst) "no format property"
              Expect.equal (alpsDoc.GetProperty("value").GetString()) "Plain text" "doc value"

          testCase "link elements are written with rel and href"
          <| fun _ ->
              let doc =
                  { Version = None
                    Documentation = None
                    Descriptors = []
                    Links =
                        [ { Rel = "self"
                            Href = "http://example.com/alps/test" } ]
                    Extensions = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let link = parsed.RootElement.GetProperty("alps").GetProperty("link").[0]
              Expect.equal (link.GetProperty("rel").GetString()) "self" "link rel"
              Expect.equal (link.GetProperty("href").GetString()) "http://example.com/alps/test" "link href"

          testCase "top-level ext elements are written"
          <| fun _ ->
              let doc =
                  { Version = None
                    Documentation = None
                    Descriptors = []
                    Links = []
                    Extensions =
                        [ { Id = "custom"
                            Href = None
                            Value = Some "data" } ] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let ext = parsed.RootElement.GetProperty("alps").GetProperty("ext").[0]
              Expect.equal (ext.GetProperty("id").GetString()) "custom" "ext id"
              Expect.equal (ext.GetProperty("value").GetString()) "data" "ext value"

          testCase "descriptor with href only (no id)"
          <| fun _ ->
              let desc =
                  { Id = None
                    Type = Semantic
                    Href = Some "#otherDescriptor"
                    ReturnType = None
                    Documentation = None
                    Descriptors = []
                    Extensions = []
                    Links = [] }

              let doc =
                  { Version = None
                    Documentation = None
                    Descriptors = [ desc ]
                    Links = []
                    Extensions = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)

              let descriptor =
                  parsed.RootElement.GetProperty("alps").GetProperty("descriptor").[0]

              Expect.isFalse (descriptor.TryGetProperty("id") |> fst) "no id property"

              Expect.equal
                  (descriptor.GetProperty("href").GetString())
                  "#otherDescriptor"
                  "href present"

          testCase "descriptor with rt field"
          <| fun _ ->
              let desc =
                  { Id = Some "viewGame"
                    Type = Safe
                    Href = None
                    ReturnType = Some "#gameState"
                    Documentation = None
                    Descriptors = []
                    Extensions = []
                    Links = [] }

              let doc =
                  { Version = None
                    Documentation = None
                    Descriptors = [ desc ]
                    Links = []
                    Extensions = [] }

              let json = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)

              let descriptor =
                  parsed.RootElement.GetProperty("alps").GetProperty("descriptor").[0]

              Expect.equal (descriptor.GetProperty("rt").GetString()) "#gameState" "rt preserved" ]
