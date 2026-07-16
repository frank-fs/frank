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
let rfc6570Tests =
    testList
        "JsonHomeSerializer.extractTemplateVars — RFC 6570"
        [ testCase "{id} simple var"
          <| fun _ ->
              let result = JsonHomeSerializer.extractTemplateVars "{id}"
              Expect.equal result [ "id" ] "simple var name"

          testCase "/a/{x}/b/{y} two simple vars"
          <| fun _ ->
              let result = JsonHomeSerializer.extractTemplateVars "/a/{x}/b/{y}"
              Expect.equal result [ "x"; "y" ] "two simple vars"

          testCase "{+base} plus operator stripped"
          <| fun _ ->
              let result = JsonHomeSerializer.extractTemplateVars "{+base}"
              Expect.equal result [ "base" ] "result is [base]"
              Expect.isFalse (List.contains "+base" result) "+base absent"

          testCase "{#frag} hash operator stripped"
          <| fun _ ->
              let result = JsonHomeSerializer.extractTemplateVars "{#frag}"
              Expect.equal result [ "frag" ] "result is [frag]"
              Expect.isFalse (List.contains "#frag" result) "#frag absent"

          testCase "{/path} slash operator stripped"
          <| fun _ ->
              let result = JsonHomeSerializer.extractTemplateVars "{/path}"
              Expect.equal result [ "path" ] "result is [path]"
              Expect.isFalse (List.contains "/path" result) "/path absent"

          testCase "{.ext} dot operator stripped"
          <| fun _ ->
              let result = JsonHomeSerializer.extractTemplateVars "{.ext}"
              Expect.equal result [ "ext" ] "result is [ext]"
              Expect.isFalse (List.contains ".ext" result) ".ext absent"

          testCase "{;p} semicolon operator stripped"
          <| fun _ ->
              let result = JsonHomeSerializer.extractTemplateVars "{;p}"
              Expect.equal result [ "p" ] "result is [p]"
              Expect.isFalse (List.contains ";p" result) ";p absent"

          testCase "{?q} query operator stripped"
          <| fun _ ->
              let result = JsonHomeSerializer.extractTemplateVars "{?q}"
              Expect.equal result [ "q" ] "result is [q]"
              Expect.isFalse (List.contains "?q" result) "?q absent"

          testCase "{&r} continuation operator stripped"
          <| fun _ ->
              let result = JsonHomeSerializer.extractTemplateVars "{&r}"
              Expect.equal result [ "r" ] "result is [r]"
              Expect.isFalse (List.contains "&r" result) "&r absent"

          testCase "{x,y} multi-var expression"
          <| fun _ ->
              let result = JsonHomeSerializer.extractTemplateVars "{x,y}"
              Expect.equal result [ "x"; "y" ] "splits on comma"
              Expect.isFalse (List.contains "x,y" result) "x,y not a single name"

          testCase "{x:3} prefix modifier stripped"
          <| fun _ ->
              let result = JsonHomeSerializer.extractTemplateVars "{x:3}"
              Expect.equal result [ "x" ] "result is [x]"
              Expect.isFalse (List.contains "x:3" result) "x:3 absent"

          testCase "{list*} explode modifier stripped"
          <| fun _ ->
              let result = JsonHomeSerializer.extractTemplateVars "{list*}"
              Expect.equal result [ "list" ] "result is [list]"
              Expect.isFalse (List.contains "list*" result) "list* absent" ]

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
                      Rt = None
                      ClassIri = None
                      RequestClrTypeName = None }
                    { Id = "makeMove"
                      Type = "unsafe"
                      Doc = None
                      Href = None
                      Descriptors = []
                      Rt = None
                      ClassIri = None
                      RequestClrTypeName = None } ]

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
                            Rt = None
                            ClassIri = None
                            RequestClrTypeName = None })

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
                      HrefVars = Map.ofList [ ("id", "https://schema.org/identifier") ] }
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

              Expect.isTrue
                  (gameEl.TryGetProperty("href-vars", &hrefVarsEl))
                  "href-vars present on href-template resource"

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
              Expect.isFalse (json.Contains "href-vars") "fixed href resource must have no href-vars"

          testCase "MINOR-4: template variable with no meaning IRI throws invalidOp (not silent empty string)"
          <| fun _ ->
              let resources =
                  [ { Relation = "https://schema.org/Game"
                      Href = "/games/{id}"
                      Allow = [ "GET" ]
                      HrefVars = Map.empty } ]

              Expect.throws
                  (fun () -> JsonHomeSerializer.serialize resources |> ignore)
                  "missing href-var meaning must throw invalidOp naming the variable"

          testCase "MINOR-4: partial HrefVars (covers one variable, missing another) also throws"
          <| fun _ ->
              let resources =
                  [ { Relation = "https://schema.org/Game"
                      Href = "/games/{gameId}/moves/{moveId}"
                      Allow = [ "GET" ]
                      HrefVars = Map.ofList [ ("gameId", "https://schema.org/identifier") ] } ]

              Expect.throws
                  (fun () -> JsonHomeSerializer.serialize resources |> ignore)
                  "partially-covered href-template must throw for the unmapped variable"

          testCase "MINOR-4: all template variables covered → no throw (sample passes)"
          <| fun _ ->
              let resources =
                  [ { Relation = "https://schema.org/Game"
                      Href = "/games/{id}"
                      Allow = [ "GET" ]
                      HrefVars = Map.ofList [ ("id", "https://schema.org/identifier") ] } ]

              let json = JsonHomeSerializer.serialize resources
              use doc = JsonDocument.Parse json
              let mutable resourcesEl = Unchecked.defaultof<System.Text.Json.JsonElement>
              doc.RootElement.TryGetProperty("resources", &resourcesEl) |> ignore
              let mutable gameEl = Unchecked.defaultof<System.Text.Json.JsonElement>
              resourcesEl.TryGetProperty("https://schema.org/Game", &gameEl) |> ignore
              let mutable hrefVarsEl = Unchecked.defaultof<System.Text.Json.JsonElement>
              Expect.isTrue (gameEl.TryGetProperty("href-vars", &hrefVarsEl)) "href-vars present when all vars resolved"
              let mutable idEl = Unchecked.defaultof<System.Text.Json.JsonElement>
              hrefVarsEl.TryGetProperty("id", &idEl) |> ignore

              Expect.equal
                  (idEl.GetString())
                  "https://schema.org/identifier"
                  "id maps to schema:identifier (no empty string)" ]
