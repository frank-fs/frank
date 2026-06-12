module Frank.Cli.Tests.SemanticClarifyTests

open System
open System.IO
open System.Text.Json
open Expecto
open Frank.Semantic
open Frank.Cli

// ── Helpers ──────────────────────────────────────────────────────────────────

let private writeTempLockFile (mappings: TypeMapping list) : string =
    let tmpDir =
        Path.Combine(Path.GetTempPath(), "frank_clarify_test_" + Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory(tmpDir) |> ignore
    let lockPath = Path.Combine(tmpDir, "semantic-mappings.lock.json")

    let lockFile: LockFile =
        { SchemaVersion = 1
          Generated = DateTimeOffset.UtcNow
          Vocabularies = Map.empty
          Mappings = mappings }

    LockFile.write lockPath lockFile
    lockPath

let private deleteTempDir (path: string) =
    try
        let dir = Path.GetDirectoryName(path)
        Directory.Delete(dir, true)
    with _ ->
        ()

let private unresolvedMapping : TypeMapping =
    { FsharpType = "MyApp.OrderLine"
      Iri = ""
      Confidence = 0.0
      Source = Convention
      Status = Unresolved
      Fields =
        [ { Name = "Quantity"
            Iri = ""
            Confidence = 0.0
            Source = Convention
            Status = Unresolved
            Pattern = None }
          { Name = "UnitPrice"
            Iri = ""
            Confidence = 0.0
            Source = Convention
            Status = Unresolved
            Pattern = None } ] }

let private proposedMapping : TypeMapping =
    { FsharpType = "MyApp.Order"
      Iri = "schema:Order"
      Confidence = 0.65
      Source = Convention
      Status = Proposed
      Fields =
        [ { Name = "LineItems"
            Iri = "schema:orderedItem"
            Confidence = 0.65
            Source = Convention
            Status = Proposed
            Pattern = None } ] }

let private confirmedMapping : TypeMapping =
    { FsharpType = "MyApp.Product"
      Iri = "schema:Product"
      Confidence = 0.92
      Source = Convention
      Status = Confirmed
      Fields = [] }

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let at1JsonWellFormedAndSchemaVersioned =
    testList
        "AT1: JSON output is well-formed and schema-versioned"
        [ test "output is valid JSON with schemaVersion 1 and required arrays" {
              let lockPath = writeTempLockFile [ unresolvedMapping; proposedMapping ]

              try
                  let output = SemanticCommands.clarify lockPath SemanticCommands.ClarifyFormat.Json

                  let doc = JsonDocument.Parse(output)
                  let root = doc.RootElement

                  Expect.equal (root.GetProperty("schemaVersion").GetInt32()) 1 "schemaVersion must be 1"
                  Expect.equal (root.GetProperty("unresolved").ValueKind) JsonValueKind.Array "unresolved must be array"
                  Expect.equal (root.GetProperty("proposed").ValueKind) JsonValueKind.Array "proposed must be array"
              finally
                  deleteTempDir lockPath
          }

          test "unresolved and proposed arrays are present even when empty" {
              let lockPath = writeTempLockFile []

              try
                  let output = SemanticCommands.clarify lockPath SemanticCommands.ClarifyFormat.Json
                  let doc = JsonDocument.Parse(output)
                  let root = doc.RootElement

                  Expect.equal (root.GetProperty("schemaVersion").GetInt32()) 1 "schemaVersion"
                  Expect.equal (root.GetProperty("unresolved").GetArrayLength()) 0 "unresolved empty"
                  Expect.equal (root.GetProperty("proposed").GetArrayLength()) 0 "proposed empty"
              finally
                  deleteTempDir lockPath
          } ]

[<Tests>]
let at2UnresolvedFieldMetadataIncluded =
    testList
        "AT2: unresolved entries include field name and type metadata"
        [ test "unresolved entry has fsharpType and fields array" {
              let lockPath = writeTempLockFile [ unresolvedMapping ]

              try
                  let output = SemanticCommands.clarify lockPath SemanticCommands.ClarifyFormat.Json
                  let doc = JsonDocument.Parse(output)
                  let root = doc.RootElement

                  let unresolved = root.GetProperty("unresolved")
                  Expect.equal (unresolved.GetArrayLength()) 1 "one unresolved entry"

                  let entry = unresolved.[0]
                  Expect.equal (entry.GetProperty("fsharpType").GetString()) "MyApp.OrderLine" "fsharpType"

                  let fields = entry.GetProperty("fields")
                  Expect.equal (fields.GetArrayLength()) 2 "two fields"

                  let field0 = fields.[0]
                  Expect.equal (field0.GetProperty("name").GetString()) "Quantity" "first field name"

                  let field1 = fields.[1]
                  Expect.equal (field1.GetProperty("name").GetString()) "UnitPrice" "second field name"
              finally
                  deleteTempDir lockPath
          } ]

[<Tests>]
let at3ProposedEntriesIncludeCandidate =
    testList
        "AT3 (partial): proposed entries include current candidate and confidence"
        [ test "proposed entry has fsharpType, field, currentCandidate, confidence" {
              let lockPath = writeTempLockFile [ proposedMapping ]

              try
                  let output = SemanticCommands.clarify lockPath SemanticCommands.ClarifyFormat.Json
                  let doc = JsonDocument.Parse(output)
                  let root = doc.RootElement

                  let proposed = root.GetProperty("proposed")
                  Expect.equal (proposed.GetArrayLength()) 1 "one proposed type entry"

                  let entry = proposed.[0]
                  Expect.equal (entry.GetProperty("fsharpType").GetString()) "MyApp.Order" "fsharpType"
                  Expect.equal (entry.GetProperty("currentCandidate").GetString()) "schema:Order" "currentCandidate"
                  Expect.isTrue (entry.GetProperty("confidence").GetDouble() > 0.0) "confidence > 0"
              finally
                  deleteTempDir lockPath
          } ]

[<Tests>]
let at4EmptyWhenNothingToClarify =
    testList
        "AT4: empty arrays when all mappings confirmed"
        [ test "confirmed-only lock file produces empty unresolved and proposed" {
              let lockPath = writeTempLockFile [ confirmedMapping ]

              try
                  let output = SemanticCommands.clarify lockPath SemanticCommands.ClarifyFormat.Json
                  let doc = JsonDocument.Parse(output)
                  let root = doc.RootElement

                  Expect.equal (root.GetProperty("unresolved").GetArrayLength()) 0 "no unresolved"
                  Expect.equal (root.GetProperty("proposed").GetArrayLength()) 0 "no proposed"
              finally
                  deleteTempDir lockPath
          }

          test "confirmed-only produces exit-0-compatible output with schemaVersion 1" {
              let lockPath = writeTempLockFile [ confirmedMapping ]

              try
                  let output = SemanticCommands.clarify lockPath SemanticCommands.ClarifyFormat.Json
                  let doc = JsonDocument.Parse(output)
                  Expect.equal (doc.RootElement.GetProperty("schemaVersion").GetInt32()) 1 "schemaVersion"
              finally
                  deleteTempDir lockPath
          } ]

[<Tests>]
let at5ConfirmedExcluded =
    testList
        "AT5: confirmed mappings are excluded from clarify output"
        [ test "only unresolved and proposed types appear; confirmed is filtered out" {
              let lockPath = writeTempLockFile [ unresolvedMapping; proposedMapping; confirmedMapping ]

              try
                  let output = SemanticCommands.clarify lockPath SemanticCommands.ClarifyFormat.Json
                  let doc = JsonDocument.Parse(output)
                  let root = doc.RootElement

                  let unresolved = root.GetProperty("unresolved")
                  let proposed = root.GetProperty("proposed")

                  Expect.equal (unresolved.GetArrayLength()) 1 "one unresolved"
                  Expect.equal (proposed.GetArrayLength()) 1 "one proposed"

                  let toFsharpType (el: JsonElement) = el.GetProperty("fsharpType").GetString()

                  let fsharpTypes =
                      (unresolved.EnumerateArray() |> Seq.map toFsharpType |> List.ofSeq)
                      @ (proposed.EnumerateArray() |> Seq.map toFsharpType |> List.ofSeq)

                  Expect.isFalse (fsharpTypes |> List.contains "MyApp.Product") "confirmed type must not appear"
              finally
                  deleteTempDir lockPath
          } ]

[<Tests>]
let at6MarkdownOutput =
    testList
        "AT6: markdown output contains expected sections"
        [ test "markdown output for unresolved type includes type header" {
              let lockPath = writeTempLockFile [ unresolvedMapping ]

              try
                  let output = SemanticCommands.clarify lockPath SemanticCommands.ClarifyFormat.Markdown

                  Expect.isTrue (output.Contains("MyApp.OrderLine")) "type name in markdown"
                  Expect.isTrue (output.Contains("Quantity")) "field Quantity in markdown"
                  Expect.isTrue (output.Contains("UnitPrice")) "field UnitPrice in markdown"
              finally
                  deleteTempDir lockPath
          }

          test "markdown output for proposed type includes candidate info" {
              let lockPath = writeTempLockFile [ proposedMapping ]

              try
                  let output = SemanticCommands.clarify lockPath SemanticCommands.ClarifyFormat.Markdown

                  Expect.isTrue (output.Contains("MyApp.Order")) "type name in markdown"
                  Expect.isTrue (output.Contains("schema:Order")) "candidate IRI in markdown"
              finally
                  deleteTempDir lockPath
          } ]
