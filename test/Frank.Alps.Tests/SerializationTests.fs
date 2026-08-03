module Frank.Alps.Tests.SerializationTests

open System.Text.Json
open Expecto
open Frank.Alps

let private parse (json: string) = JsonDocument.Parse(json).RootElement

let private descriptorArray (root: JsonElement) =
    root.GetProperty("alps").GetProperty("descriptor").EnumerateArray() |> List.ofSeq

let private findById (id: string) (descriptors: JsonElement list) =
    descriptors |> List.find (fun d -> d.GetProperty("id").GetString() = id)

[<Tests>]
let tests =
    testList
        "Serialization.toJson"
        [ test "root shape is alps.version = 1.0, alps.descriptor as an array" {
              let root = Serialization.toJson [ semantic "x" ] |> parse
              Expect.equal (root.GetProperty("alps").GetProperty("version").GetString()) "1.0" ""
              Expect.equal (descriptorArray root |> List.length) 1 ""
          }

          test "type is omitted for Semantic, present and lowercase otherwise" {
              let root =
                  Serialization.toJson [ semantic "a"; safe "b"; unsafe "c"; idempotent "d" ] |> parse

              let descriptors = descriptorArray root
              let hasType id = (findById id descriptors).TryGetProperty("type") |> fst

              Expect.isFalse (hasType "a") "semantic omits type"
              Expect.equal ((findById "b" descriptors).GetProperty("type").GetString()) "safe" ""
              Expect.equal ((findById "c" descriptors).GetProperty("type").GetString()) "unsafe" ""
              Expect.equal ((findById "d" descriptors).GetProperty("type").GetString()) "idempotent" ""
          }

          test "def, doc, tag, rel serialize correctly" {
              let d =
                  semantic "price"
                  |> def "https://schema.org/price"
                  |> doc "Price in minor units"
                  |> tag "money"
                  |> tag "currency"
                  |> rel "self"

              let json = findById "price" (descriptorArray (Serialization.toJson [ d ] |> parse))

              Expect.equal (json.GetProperty("def").GetString()) "https://schema.org/price" ""
              Expect.equal (json.GetProperty("doc").GetProperty("value").GetString()) "Price in minor units" ""
              Expect.equal (json.GetProperty("tag").GetString()) "money currency" ""
              Expect.equal (json.GetProperty("rel").GetString()) "self" ""
          }

          test "rt serializes as a local #id reference" {
              let product = semantic "product"
              let d = safe "listProducts" |> rt product
              let json = findById "listProducts" (descriptorArray (Serialization.toJson [ product; d ] |> parse))
              Expect.equal (json.GetProperty("rt").GetString()) "#product" ""
          }

          test "href with a Local target serializes as #id; hrefExternal serializes the URI verbatim" {
              let shared = semantic "shared"
              let local = semantic "local" |> href shared
              let external' = semantic "external" |> hrefExternal "https://example.org/other#thing"

              let descriptors = descriptorArray (Serialization.toJson [ shared; local; external' ] |> parse)

              Expect.equal ((findById "local" descriptors).GetProperty("href").GetString()) "#shared" ""

              Expect.equal
                  ((findById "external" descriptors).GetProperty("href").GetString())
                  "https://example.org/other#thing"
                  ""
          }

          test "contains serializes as a nested descriptor array" {
              let child = semantic "productId"
              let parent = semantic "product" |> contains [ child ]
              let json = findById "product" (descriptorArray (Serialization.toJson [ parent ] |> parse))
              let nested = json.GetProperty("descriptor").EnumerateArray() |> List.ofSeq
              Expect.equal nested.Length 1 ""
              Expect.equal (nested.[0].GetProperty("id").GetString()) "productId" ""
          }

          test "a transition with from [A; B] emits two protocolState/availableInStates ext pairs" {
              let a, b = semantic "a", semantic "b"
              let c = semantic "c"
              let t = unsafe "t" |> from [ a; b ] |> rt c

              let json = findById "t" (descriptorArray (Serialization.toJson [ a; b; c; t ] |> parse))
              let extIds = json.GetProperty("ext").EnumerateArray() |> Seq.map (fun e -> e.GetProperty("id").GetString()) |> List.ofSeq

              Expect.equal
                  (extIds |> List.sort)
                  ([ ProtocolStateExtId; ProtocolStateExtId; AvailableInStatesExtId; AvailableInStatesExtId ]
                   |> List.sort)
                  "one pair per declared from state"
          }

          test "a transition with no from emits no protocolState/availableInStates ext" {
              let t = unsafe "t"
              let json = findById "t" (descriptorArray (Serialization.toJson [ t ] |> parse))
              Expect.isFalse (json.TryGetProperty("ext") |> fst) ""
          }

          test "empty tag/link/descriptor/ext are omitted entirely, not written as empty arrays" {
              let json = findById "x" (descriptorArray (Serialization.toJson [ semantic "x" ] |> parse))
              Expect.isFalse (json.TryGetProperty("tag") |> fst) ""
              Expect.isFalse (json.TryGetProperty("link") |> fst) ""
              Expect.isFalse (json.TryGetProperty("descriptor") |> fst) ""
              Expect.isFalse (json.TryGetProperty("ext") |> fst) ""
          } ]
