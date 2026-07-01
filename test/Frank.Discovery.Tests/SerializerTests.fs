module Frank.Discovery.Tests.SerializerTests

open System.Text.Json
open Expecto
open FsCheck
open Frank.Discovery

let private fsCfg = FsCheckConfig.defaultConfig

let private safeToken =
    gen {
        let alphabet = [ 'a' .. 'z' ] @ [ 'A' .. 'Z' ] @ [ '0' .. '9' ]
        let! chars = Gen.nonEmptyListOf (Gen.elements alphabet)
        return System.String(List.toArray chars)
    }

let private iriGen = safeToken |> Gen.map (fun t -> "https://schema.org/" + t)

[<Tests>]
let alpsTests =
    testList
        "AlpsSerializer"
        [ testCase "emits a valid ALPS document with vocabulary IRIs and no urn:frank:"
          <| fun _ ->
              let descriptors =
                  [ { Id = "Game"
                      Type = "semantic"
                      Doc = Some "doc"
                      Href = Some "https://schema.org/Game"
                      Descriptors = []
                      Rt = None }
                    { Id = "makeMove"
                      Type = "unsafe"
                      Doc = None
                      Href = None
                      Descriptors = []
                      Rt = None } ]

              let json = AlpsSerializer.serialize descriptors
              use doc = JsonDocument.Parse json // throws if invalid
              Expect.stringContains json "https://schema.org/Game" "vocabulary IRI present"
              Expect.isFalse (json.Contains "urn:frank:") "no urn:frank: IRIs"
              Expect.stringContains json "\"unsafe\"" "action descriptor type present"

          testPropertyWithConfig fsCfg "every descriptor Href appears in the output"
          <| fun () ->
              Prop.forAll (Arb.fromGen (Gen.listOf (Gen.zip safeToken iriGen))) (fun pairs ->
                  let descriptors =
                      pairs
                      |> List.map (fun (id, iri) ->
                          { Id = id
                            Type = "semantic"
                            Doc = None
                            Href = Some iri
                            Descriptors = []
                            Rt = None })

                  let json = AlpsSerializer.serialize descriptors
                  use _ = JsonDocument.Parse json
                  pairs |> List.forall (fun (_, iri) -> json.Contains iri)) ]

[<Tests>]
let jsonHomeTests =
    testList
        "JsonHomeSerializer"
        [ testCase "templated href uses href-template, fixed uses href"
          <| fun _ ->
              let resources =
                  [ { Relation = "https://schema.org/Game"
                      Href = "/games/{id}"
                      Allow = [ "GET" ]
                      HrefVars = Map.empty }
                    { Relation = "https://schema.org/About"
                      Href = "/about"
                      Allow = [ "GET" ]
                      HrefVars = Map.empty } ]

              let json = JsonHomeSerializer.serialize resources
              use _ = JsonDocument.Parse json
              Expect.stringContains json "href-template" "template entry uses href-template"
              Expect.stringContains json "\"href\":\"/about\"" "fixed entry uses href"

          testPropertyWithConfig fsCfg "every relation appears and output is valid JSON"
          <| fun () ->
              Prop.forAll (Arb.fromGen (Gen.nonEmptyListOf (Gen.zip iriGen safeToken))) (fun pairs ->
                  let resources =
                      pairs
                      |> List.mapi (fun i (rel, seg) ->
                          { Relation = rel + string i
                            Href = "/" + seg
                            Allow = [ "GET" ]
                            HrefVars = Map.empty })

                  let json = JsonHomeSerializer.serialize resources
                  use _ = JsonDocument.Parse json
                  resources |> List.forall (fun r -> json.Contains r.Relation))

          testCase "href-template resource includes href-vars with meaningful absolute meaning IRI (#9)"
          <| fun _ ->
              let resources =
                  [ { Relation = "https://schema.org/Game"
                      Href = "/games/{id}"
                      Allow = [ "GET" ]
                      HrefVars = Map.ofList [ ("id", "https://schema.org/identifier") ] } ]

              let json = JsonHomeSerializer.serialize resources
              use doc = JsonDocument.Parse json
              let mutable resourcesEl = Unchecked.defaultof<JsonElement>
              Expect.isTrue (doc.RootElement.TryGetProperty("resources", &resourcesEl)) "resources present"
              let mutable gameEl = Unchecked.defaultof<JsonElement>
              Expect.isTrue (resourcesEl.TryGetProperty("https://schema.org/Game", &gameEl)) "game resource present"
              let mutable hrefVarsEl = Unchecked.defaultof<JsonElement>
              Expect.isTrue (gameEl.TryGetProperty("href-vars", &hrefVarsEl)) "href-vars present on href-template resource"
              let mutable idEl = Unchecked.defaultof<JsonElement>
              Expect.isTrue (hrefVarsEl.TryGetProperty("id", &idEl)) "href-vars contains 'id' key"
              let idValue = idEl.GetString()
              Expect.isFalse (System.String.IsNullOrEmpty idValue) "href-vars 'id' value must not be empty"
              Expect.isTrue (idValue.StartsWith "http") "href-vars 'id' value must be an absolute URI"
              Expect.equal idValue "https://schema.org/identifier" "href-vars 'id' value is schema:identifier"

          testCase "fixed href resource does NOT include href-vars (#9)"
          <| fun _ ->
              let resources =
                  [ { Relation = "https://schema.org/About"
                      Href = "/about"
                      Allow = [ "GET" ]
                      HrefVars = Map.empty } ]

              let json = JsonHomeSerializer.serialize resources
              Expect.isFalse (json.Contains "href-vars") "fixed href resource must have no href-vars" ]
