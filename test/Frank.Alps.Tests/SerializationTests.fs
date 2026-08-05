module Frank.Alps.Tests.SerializationTests

open System
open System.Text.Json
open Expecto
open Frank.Alps

let private testRootUri = Uri("/.well-known/alps.json", UriKind.Relative)

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
              let root = Serialization.toJson testRootUri [ semantic "x" ] |> parse
              Expect.equal (root.GetProperty("alps").GetProperty("version").GetString()) "1.0" ""
              Expect.equal (descriptorArray root |> List.length) 1 ""
          }

          test "type is omitted for Semantic, present and lowercase otherwise" {
              let root =
                  Serialization.toJson testRootUri [ semantic "a"; safe "b"; unsafe "c"; idempotent "d" ] |> parse

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

              let json = findById "price" (descriptorArray (Serialization.toJson testRootUri [ d ] |> parse))

              Expect.equal (json.GetProperty("def").GetString()) "https://schema.org/price" ""
              Expect.equal (json.GetProperty("doc").GetProperty("value").GetString()) "Price in minor units" ""
              Expect.equal (json.GetProperty("tag").GetString()) "money currency" ""
              Expect.equal (json.GetProperty("rel").GetString()) "self" ""
          }

          test "rt serializes as a local #id reference" {
              let product = semantic "product"
              let d = safe "listProducts" |> rt product
              let json = findById "listProducts" (descriptorArray (Serialization.toJson testRootUri [ product; d ] |> parse))
              Expect.equal (json.GetProperty("rt").GetString()) "#product" ""
          }

          test "href with a Local target serializes as #id; hrefExternal serializes the URI verbatim" {
              let shared = semantic "shared"
              let local = semantic "local" |> href shared
              let external' = semantic "external" |> hrefExternal "https://example.org/other#thing"

              let descriptors = descriptorArray (Serialization.toJson testRootUri [ shared; local; external' ] |> parse)

              Expect.equal ((findById "local" descriptors).GetProperty("href").GetString()) "#shared" ""

              Expect.equal
                  ((findById "external" descriptors).GetProperty("href").GetString())
                  "https://example.org/other#thing"
                  ""
          }

          test "contains serializes as a nested descriptor array" {
              let child = semantic "productId"
              let parent = semantic "product" |> contains [ child ]
              let json = findById "product" (descriptorArray (Serialization.toJson testRootUri [ parent ] |> parse))
              let nested = json.GetProperty("descriptor").EnumerateArray() |> List.ofSeq
              Expect.equal nested.Length 1 ""
              Expect.equal (nested.[0].GetProperty("id").GetString()) "productId" ""
          }

          test "a transition with from [A; B] emits two protocolState/availableInStates ext pairs" {
              let a, b = semantic "a", semantic "b"
              let c = semantic "c"
              let t = unsafe "t" |> from [ a; b ] |> rt c

              let json = findById "t" (descriptorArray (Serialization.toJson testRootUri [ a; b; c; t ] |> parse))
              let extIds = json.GetProperty("ext").EnumerateArray() |> Seq.map (fun e -> e.GetProperty("id").GetString()) |> List.ofSeq

              Expect.equal
                  (extIds |> List.sort)
                  ([ ProtocolStateExtId; ProtocolStateExtId; AvailableInStatesExtId; AvailableInStatesExtId ]
                   |> List.sort)
                  "one pair per declared from state"
          }

          test "a transition with no from emits no protocolState/availableInStates ext" {
              let t = unsafe "t"
              let json = findById "t" (descriptorArray (Serialization.toJson testRootUri [ t ] |> parse))
              Expect.isFalse (json.TryGetProperty("ext") |> fst) ""
          }

          test "empty tag/link/descriptor/ext are omitted entirely, not written as empty arrays" {
              let json = findById "x" (descriptorArray (Serialization.toJson testRootUri [ semantic "x" ] |> parse))
              Expect.isFalse (json.TryGetProperty("tag") |> fst) ""
              Expect.isFalse (json.TryGetProperty("link") |> fst) ""
              Expect.isFalse (json.TryGetProperty("descriptor") |> fst) ""
              Expect.isFalse (json.TryGetProperty("ext") |> fst) ""
          }

          test "authored ext and from-projected ext pairs coexist without corruption" {
              let a = semantic "a"
              let b = semantic "b"
              let t = unsafe "t"
                       |> ext "https://example.org/custom" "custom-value"
                       |> from [ a; b ]

              let json = findById "t" (descriptorArray (Serialization.toJson testRootUri [ a; b; t ] |> parse))
              let extArray = json.GetProperty("ext").EnumerateArray() |> List.ofSeq
              let extIds = extArray |> List.map (fun e -> e.GetProperty("id").GetString())

              Expect.equal (List.length extIds) 5 "should have 5 ext entries (1 authored + 2 pairs for 2 from states)"

              let customExt = extArray |> List.find (fun e -> e.GetProperty("id").GetString() = "https://example.org/custom")
              Expect.equal (customExt.GetProperty("value").GetString()) "custom-value" "authored ext value should be preserved"

              let protocolStatePairs = extIds |> List.filter (fun id -> id = ProtocolStateExtId) |> List.length
              let availableInStatesPairs = extIds |> List.filter (fun id -> id = AvailableInStatesExtId) |> List.length

              Expect.equal protocolStatePairs 2 "should have 2 protocolState entries"
              Expect.equal availableInStatesPairs 2 "should have 2 availableInStates entries"
          }

          test "href to a descriptor NOT in profile resolves to rootUri#id" {
              let shared = semantic "shared"
              let local = semantic "local" |> href shared
              let json = findById "local" (descriptorArray (Serialization.toJson testRootUri [ local ] |> parse))
              Expect.equal (json.GetProperty("href").GetString()) "/.well-known/alps.json#shared" ""
          }

          test "rt to a descriptor NOT in profile resolves to rootUri#id" {
              let shared = semantic "shared"
              let local = unsafe "local" |> rt shared
              let json = findById "local" (descriptorArray (Serialization.toJson testRootUri [ local ] |> parse))
              Expect.equal (json.GetProperty("rt").GetString()) "/.well-known/alps.json#shared" ""
          }

          test "from-state NOT in profile resolves ext value to rootUri#id" {
              let awaitingPing = semantic "awaitingPing"
              let awaitingPong = semantic "awaitingPong" |> from [ awaitingPing ]
              let json = findById "awaitingPong" (descriptorArray (Serialization.toJson testRootUri [ awaitingPong ] |> parse))
              let extArray = json.GetProperty("ext").EnumerateArray() |> List.ofSeq
              let extValue = extArray.[0].GetProperty("value").GetString()
              Expect.equal extValue "/.well-known/alps.json#awaitingPing" ""
          } ]
