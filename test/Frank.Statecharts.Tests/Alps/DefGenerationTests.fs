module Frank.Statecharts.Tests.Alps.DefGenerationTests

open System.Text.Json
open System.Xml.Linq
open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Alps.GeneratorCommon
open Frank.Statecharts.Alps.JsonGenerator
open Frank.Statecharts.Alps.XmlGenerator

/// Helper to build a minimal doc with one state descriptor and one data descriptor.
let private buildDocWithDataDescriptor () =
    { Title = None
      InitialStateId = None
      Elements =
        [ StateDecl
              { Identifier = Some "gameState"
                Label = None
                Kind = StateKind.Regular
                Children = []
                Activities = None
                Position = None
                Annotations = [] }
          TransitionElement
              { Source = "gameState"
                Target = Some "gameState"
                Event = Some "makeMove"
                Guard = None
                Action = None
                Parameters = [ "position" ]
                Position = None
                Annotations = [ AlpsAnnotation(AlpsTransitionType AlpsTransitionKind.Unsafe) ] } ]
      DataEntries = []
      Annotations =
        [ AlpsAnnotation(AlpsVersion "1.0")
          AlpsAnnotation(AlpsDataDescriptor("position", Some(None, "Board position to play"))) ] }

[<Tests>]
let jsonDefGenerationTests =
    testList
        "Alps.JsonGenerator def URIs"
        [
          // #174: JSON generator emits def on state descriptors when defBaseUri provided
          testCase "generateAlpsJsonWithOptions emits def on state descriptors"
          <| fun _ ->
              let doc = buildDocWithDataDescriptor ()

              let options: AlpsGeneratorOptions =
                  { DefBaseUri = Some "http://example.com/api"
                    ResourceSlug = Some "games" }

              let json: string = generateAlpsJsonWithOptions options doc
              use parsed = JsonDocument.Parse(json)
              let alps: JsonElement = parsed.RootElement.GetProperty("alps")
              let descriptors: JsonElement = alps.GetProperty("descriptor")

              // Find the state descriptor (gameState)
              let stateDesc: JsonElement =
                  [ for i in 0 .. descriptors.GetArrayLength() - 1 -> descriptors.[i] ]
                  |> List.find (fun (d: JsonElement) ->
                      match d.TryGetProperty("id") with
                      | true, id -> id.GetString() = "gameState"
                      | _ -> false)

              let def = stateDesc.GetProperty("def").GetString()

              Expect.equal
                  def
                  "http://example.com/api/shapes/games#gameState"
                  "def should be HTTP shape URI with fragment"

          testCase "generateAlpsJsonWithOptions emits def on data descriptors"
          <| fun _ ->
              let doc = buildDocWithDataDescriptor ()

              let options: AlpsGeneratorOptions =
                  { DefBaseUri = Some "http://example.com/api"
                    ResourceSlug = Some "games" }

              let json: string = generateAlpsJsonWithOptions options doc
              use parsed = JsonDocument.Parse(json)
              let alps: JsonElement = parsed.RootElement.GetProperty("alps")
              let descriptors: JsonElement = alps.GetProperty("descriptor")

              // Find the data descriptor (position)
              let dataDesc: JsonElement =
                  [ for i in 0 .. descriptors.GetArrayLength() - 1 -> descriptors.[i] ]
                  |> List.find (fun (d: JsonElement) ->
                      match d.TryGetProperty("id") with
                      | true, id -> id.GetString() = "position"
                      | _ -> false)

              let def = dataDesc.GetProperty("def").GetString()

              Expect.equal
                  def
                  "http://example.com/api/shapes/games#position"
                  "def should point to shape fragment for data descriptor"

          testCase "generateAlpsJson without options does not emit def"
          <| fun _ ->
              let doc = buildDocWithDataDescriptor ()
              let json: string = generateAlpsJson doc
              use parsed = JsonDocument.Parse(json)
              let alps: JsonElement = parsed.RootElement.GetProperty("alps")
              let descriptors: JsonElement = alps.GetProperty("descriptor")

              for i in 0 .. descriptors.GetArrayLength() - 1 do
                  let desc: JsonElement = descriptors.[i]
                  let hasDef = desc.TryGetProperty("def") |> fst
                  Expect.isFalse hasDef (sprintf "descriptor[%d] should not have def without options" i)

          testCase "generateAlpsJsonWithOptions with no defBaseUri does not emit def"
          <| fun _ ->
              let doc = buildDocWithDataDescriptor ()

              let options: AlpsGeneratorOptions =
                  { DefBaseUri = None
                    ResourceSlug = Some "games" }

              let json: string = generateAlpsJsonWithOptions options doc
              use parsed = JsonDocument.Parse(json)
              let alps: JsonElement = parsed.RootElement.GetProperty("alps")
              let descriptors: JsonElement = alps.GetProperty("descriptor")

              for i in 0 .. descriptors.GetArrayLength() - 1 do
                  let desc: JsonElement = descriptors.[i]
                  let hasDef = desc.TryGetProperty("def") |> fst
                  Expect.isFalse hasDef (sprintf "descriptor[%d] should not have def without defBaseUri" i) ]

[<Tests>]
let xmlDefGenerationTests =
    testList
        "Alps.XmlGenerator def URIs"
        [
          // #174: XML generator emits def on state descriptors when defBaseUri provided
          testCase "generateAlpsXmlWithOptions emits def on state descriptors"
          <| fun _ ->
              let doc = buildDocWithDataDescriptor ()

              let options: AlpsGeneratorOptions =
                  { DefBaseUri = Some "http://example.com/api"
                    ResourceSlug = Some "games" }

              let xml: string = generateAlpsXmlWithOptions options doc
              let xdoc = XDocument.Parse(xml)
              let alps = xdoc.Root

              let descriptors = alps.Elements(XName.Get "descriptor") |> Seq.toList

              // Find gameState descriptor
              let stateDesc =
                  descriptors
                  |> List.find (fun el ->
                      let id = el.Attribute(XName.Get "id")
                      id <> null && id.Value = "gameState")

              let def = stateDesc.Attribute(XName.Get "def")
              Expect.isNotNull def "Should have def attribute"

              Expect.equal
                  def.Value
                  "http://example.com/api/shapes/games#gameState"
                  "def should be HTTP shape URI with fragment"

          testCase "generateAlpsXml without options does not emit def"
          <| fun _ ->
              let doc = buildDocWithDataDescriptor ()
              let xml: string = generateAlpsXml doc
              let xdoc = XDocument.Parse(xml)
              let alps = xdoc.Root

              let descriptors = alps.Elements(XName.Get "descriptor") |> Seq.toList

              for desc in descriptors do
                  let def = desc.Attribute(XName.Get "def")
                  Expect.isNull def "Should not have def attribute without options" ]
