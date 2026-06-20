module Frank.Cli.Core.Tests.ClarifyTests

open System.Text.Json.Nodes
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core

// ── Fixtures ──────────────────────────────────────────────────────────────────

let private unresolvedField: FieldMapping =
    { Name = "Blah"
      Iri = None
      Confidence = 0.0
      Source = Convention
      Status = Unresolved }

let private proposedField: FieldMapping =
    { Name = "Amount"
      Iri = Some "schema:price"
      Confidence = 0.72
      Source = Convention
      Status = Proposed }

let private unresolvedMapping: Mapping =
    { FSharpType = "MyApp.Mystery"
      Iri = None
      Confidence = 0.0
      Source = Convention
      Status = Unresolved
      Alternates = [ "schema:Thing"; "schema:Event" ]
      Shape = MappingShape.Record [ unresolvedField ] }

let private proposedMapping: Mapping =
    { FSharpType = "MyApp.Order"
      Iri = Some "schema:Order"
      Confidence = 0.65
      Source = Convention
      Status = Proposed
      Alternates = [ "schema:OrderAction" ]
      Shape = MappingShape.Record [ proposedField ] }

let private confirmedMapping: Mapping =
    { FSharpType = "MyApp.Customer"
      Iri = Some "schema:Person"
      Confidence = 0.91
      Source = Llm
      Status = Confirmed
      Alternates = []
      Shape = MappingShape.Record [] }

let private emptyVocabs: Map<string, VocabularyEntry> = Map.empty

let private mixedLock: LockFile =
    { SchemaVersion = 1
      Generated = System.DateTimeOffset.UnixEpoch
      Vocabularies = emptyVocabs
      Mappings = [ unresolvedMapping; proposedMapping; confirmedMapping ] }

let private allConfirmedLock: LockFile =
    { SchemaVersion = 1
      Generated = System.DateTimeOffset.UnixEpoch
      Vocabularies = emptyVocabs
      Mappings = [ confirmedMapping ] }

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let clarifyTests =
    testList
        "Clarify"
        [ testCase "AT1: toJson mixed lock — schemaVersion 1, unresolved and proposed present, confirmed excluded"
          <| fun () ->
              let json: string = Clarify.toJson mixedLock
              let root: JsonNode = JsonNode.Parse json

              Expect.equal (root.["schemaVersion"].GetValue<int>()) 1 "schemaVersion must be 1"

              let unresolved: JsonArray = root.["unresolved"].AsArray()
              let proposed: JsonArray = root.["proposed"].AsArray()

              Expect.equal unresolved.Count 1 "one unresolved entry"
              Expect.equal proposed.Count 1 "one proposed entry"

              let unresolvedTypes =
                  [ for i in 0 .. unresolved.Count - 1 do
                        yield unresolved.[i].["fsharpType"].GetValue<string>() ]

              let proposedTypes =
                  [ for i in 0 .. proposed.Count - 1 do
                        yield proposed.[i].["fsharpType"].GetValue<string>() ]

              let allTypes = unresolvedTypes @ proposedTypes
              Expect.isFalse (List.contains "MyApp.Customer" allTypes) "confirmed type must not appear"

              let unresolvedEntry: JsonNode = unresolved.[0]

              Expect.equal (unresolvedEntry.["fsharpType"].GetValue<string>()) "MyApp.Mystery" "unresolved fsharpType"

              let candidates: JsonArray = unresolvedEntry.["candidates"].AsArray()
              Expect.equal candidates.Count 2 "two candidates"

              Expect.equal (candidates.[0].GetValue<string>()) "schema:Thing" "first candidate"

              let proposedEntry: JsonNode = proposed.[0]

              Expect.equal (proposedEntry.["fsharpType"].GetValue<string>()) "MyApp.Order" "proposed fsharpType"

              Expect.equal (proposedEntry.["currentCandidate"].GetValue<string>()) "schema:Order" "currentCandidate"

              let proposedCandidates: JsonArray = proposedEntry.["candidates"].AsArray()
              Expect.equal proposedCandidates.Count 1 "proposed entry has one candidate"

              Expect.equal
                  (proposedCandidates.[0].GetValue<string>())
                  "schema:OrderAction"
                  "proposed candidate is schema:OrderAction"

              Expect.isTrue (isNull (proposedEntry.["alternates"])) "alternates key must not exist on proposed entry"

          testCase "AT4: toJson all-confirmed lock — unresolved and proposed are empty arrays"
          <| fun () ->
              let json: string = Clarify.toJson allConfirmedLock
              let root: JsonNode = JsonNode.Parse json

              let unresolved: JsonArray = root.["unresolved"].AsArray()
              let proposed: JsonArray = root.["proposed"].AsArray()

              Expect.equal unresolved.Count 0 "unresolved must be empty"
              Expect.equal proposed.Count 0 "proposed must be empty"

          testCase "AT3: toMarkdown mixed lock — contains type names and table header"
          <| fun () ->
              let md: string = Clarify.toMarkdown mixedLock

              Expect.stringContains md "MyApp.Mystery" "unresolved type name"
              Expect.stringContains md "MyApp.Order" "proposed type name"
              Expect.stringContains md "Field" "table header Field column"
              Expect.stringContains md "IRI" "table header IRI column"

          testCase "field with Iri = None serializes iri as null"
          <| fun () ->
              let lock: LockFile =
                  { mixedLock with
                      Mappings = [ unresolvedMapping ] }

              let json: string = Clarify.toJson lock
              let root: JsonNode = JsonNode.Parse json
              let unresolved: JsonArray = root.["unresolved"].AsArray()
              let fields: JsonArray = unresolved.[0].["fields"].AsArray()
              Expect.equal fields.Count 1 "one field"
              let iriNode: JsonNode = fields.[0].["iri"]

              let iriIsNull =
                  isNull iriNode
                  || (iriNode :? JsonValue && (iriNode.AsValue().GetValue<obj>() |> isNull))

              Expect.isTrue iriIsNull "iri must be null for None"

          testCase "toResolvedTemplate — proposed and unresolved included, confirmed excluded, iris pre-filled"
          <| fun () ->
              let json: string = Clarify.toResolvedTemplate mixedLock
              let root: JsonNode = JsonNode.Parse json

              Expect.equal (root.["schemaVersion"].GetValue<int>()) 1 "schemaVersion must be 1"

              let resolved: JsonArray = root.["resolved"].AsArray()
              Expect.equal resolved.Count 2 "proposed + unresolved = 2 entries"

              let types =
                  [ for i in 0 .. resolved.Count - 1 do
                        yield resolved.[i].["fsharpType"].GetValue<string>() ]

              Expect.isFalse (List.contains "MyApp.Customer" types) "confirmed must not appear"
              Expect.isTrue (List.contains "MyApp.Order" types) "proposed must appear"
              Expect.isTrue (List.contains "MyApp.Mystery" types) "unresolved must appear"

              let orderEntry =
                  resolved
                  |> Seq.cast<JsonNode>
                  |> Seq.find (fun n -> n.["fsharpType"].GetValue<string>() = "MyApp.Order")

              Expect.equal (orderEntry.["iri"].GetValue<string>()) "schema:Order" "proposed iri pre-filled"

              let orderFields: JsonArray = orderEntry.["fields"].AsArray()
              Expect.equal orderFields.Count 1 "one field on Order"
              Expect.equal (orderFields.[0].["name"].GetValue<string>()) "Amount" "field name"
              Expect.equal (orderFields.[0].["iri"].GetValue<string>()) "schema:price" "field iri pre-filled"

              let mysteryEntry =
                  resolved
                  |> Seq.cast<JsonNode>
                  |> Seq.find (fun n -> n.["fsharpType"].GetValue<string>() = "MyApp.Mystery")

              let mysteryIriNode: JsonNode = mysteryEntry.["iri"]

              let mysteryIriIsNull =
                  isNull mysteryIriNode
                  || (mysteryIriNode :? JsonValue
                      && (mysteryIriNode.AsValue().GetValue<obj>() |> isNull))

              Expect.isTrue mysteryIriIsNull "unresolved iri is null"

              let mysteryFields: JsonArray = mysteryEntry.["fields"].AsArray()
              Expect.equal mysteryFields.Count 1 "one field on Mystery"
              Expect.equal (mysteryFields.[0].["name"].GetValue<string>()) "Blah" "field name"

              let blahIriNode: JsonNode = mysteryFields.[0].["iri"]

              let blahIriIsNull =
                  isNull blahIriNode
                  || (blahIriNode :? JsonValue && (blahIriNode.AsValue().GetValue<obj>() |> isNull))

              Expect.isTrue blahIriIsNull "unresolved field iri is null" ]
