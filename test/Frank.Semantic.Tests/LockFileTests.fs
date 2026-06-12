module Frank.Semantic.Tests.LockFileTests

open System
open System.IO
open Expecto
open Frank.Semantic

// Helpers

let sampleLockFile () =
    { SchemaVersion = 1
      Generated = DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero)
      Vocabularies =
        Map.ofList
            [ "schema",
              { Uri = "https://schema.org/"
                FetchedAt = Some "2026-04-20T10:00:00Z"
                Hash = Some "sha256:abc123" } ]
      Mappings =
        [ { FsharpType = "MyApp.Order"
            Iri = "schema:Order"
            Confidence = 0.92
            Source = Convention
            Status = Confirmed
            Fields =
              [ { Name = "Total"
                  Iri = "schema:totalPaymentDue"
                  Confidence = 0.78
                  Source = Llm
                  Status = Confirmed }
                { Name = "LineItems"
                  Iri = "schema:orderedItem"
                  Confidence = 0.65
                  Source = Convention
                  Status = Proposed } ] } ] }

[<Tests>]
let at1 =
    testList
        "AT1: Round-trip preserves content"
        [ test "write then read returns Ok with same data" {
              let path = Path.GetTempFileName()

              try
                  let lf = sampleLockFile ()
                  LockFile.write path lf

                  match LockFile.read path with
                  | Error msg -> failtest $"Expected Ok but got Error: {msg}"
                  | Ok result ->
                      Expect.equal result.SchemaVersion lf.SchemaVersion "schemaVersion"
                      Expect.equal result.Vocabularies lf.Vocabularies "vocabularies"
                      Expect.equal result.Mappings lf.Mappings "mappings"
                      // Generated timestamp: compare to second precision
                      Expect.equal
                          (result.Generated.ToUniversalTime().ToString("O"))
                          (lf.Generated.ToUniversalTime().ToString("O"))
                          "generated timestamp"
              finally
                  if File.Exists path then
                      File.Delete path
          } ]

[<Tests>]
let at2 =
    testList
        "AT2: Schema version mismatch fails closed"
        [ test "schemaVersion 99 returns Error with correct message" {
              let path = Path.GetTempFileName()

              try
                  File.WriteAllText(
                      path,
                      """{"schemaVersion":99,"generated":"2026-04-20T12:00:00+00:00","vocabularies":{},"mappings":[]}"""
                  )

                  match LockFile.read path with
                  | Ok _ -> failtest "Expected Error but got Ok"
                  | Error msg ->
                      Expect.equal msg "lock file schema version 99 not supported by this CLI" "error message"
              finally
                  if File.Exists path then
                      File.Delete path
          }

          test "malformed JSON returns Error" {
              let path = Path.GetTempFileName()

              try
                  File.WriteAllText(path, "not json at all")

                  match LockFile.read path with
                  | Ok _ -> failtest "Expected Error for malformed JSON"
                  | Error _ -> ()
              finally
                  if File.Exists path then
                      File.Delete path
          } ]

[<Tests>]
let at3 =
    testList
        "AT3: Deterministic regeneration"
        [ test "two writes with same data produce byte-identical output (same generated field)" {
              let path1 = Path.GetTempFileName()
              let path2 = Path.GetTempFileName()

              try
                  let lf = sampleLockFile ()
                  LockFile.write path1 lf
                  LockFile.write path2 lf
                  let bytes1 = File.ReadAllBytes(path1)
                  let bytes2 = File.ReadAllBytes(path2)
                  Expect.equal bytes1 bytes2 "output should be byte-identical"
              finally
                  if File.Exists path1 then
                      File.Delete path1

                  if File.Exists path2 then
                      File.Delete path2
          } ]

[<Tests>]
let at4 =
    testList
        "AT4: merge preserves confirmed llm entries"
        [ test "confirmed llm entry not in resolved is preserved" {
              let llmEntry =
                  { FsharpType = "MyApp.Product"
                    Iri = "schema:Product"
                    Confidence = 0.95
                    Source = Llm
                    Status = Confirmed
                    Fields = [] }

              let existing =
                  { (sampleLockFile ()) with
                      Mappings = [ llmEntry ] }

              let resolved: TypeMapping list = []
              let merged = LockFile.merge existing resolved
              Expect.contains merged.Mappings llmEntry "confirmed llm entry should be preserved"
          }

          test "confirmed manual entry not in resolved is preserved" {
              let manualEntry =
                  { FsharpType = "MyApp.Invoice"
                    Iri = "schema:Invoice"
                    Confidence = 1.0
                    Source = Manual
                    Status = Confirmed
                    Fields = [] }

              let existing =
                  { (sampleLockFile ()) with
                      Mappings = [ manualEntry ] }

              let resolved: TypeMapping list = []
              let merged = LockFile.merge existing resolved
              Expect.contains merged.Mappings manualEntry "confirmed manual entry should be preserved"
          } ]

[<Tests>]
let at5 =
    testList
        "AT5: merge adds new convention entries"
        [ test "type in resolved but absent from existing is appended" {
              let newEntry =
                  { FsharpType = "MyApp.Customer"
                    Iri = "schema:Customer"
                    Confidence = 0.88
                    Source = Convention
                    Status = Confirmed
                    Fields = [] }

              let existing =
                  { (sampleLockFile ()) with
                      Mappings = [] }

              let resolved = [ newEntry ]
              let merged = LockFile.merge existing resolved
              Expect.contains merged.Mappings newEntry "new convention entry should be appended"
          }

          test "convention entry in resolved replaces convention entry in existing" {
              let oldEntry =
                  { FsharpType = "MyApp.Order"
                    Iri = "schema:Order"
                    Confidence = 0.5
                    Source = Convention
                    Status = Proposed
                    Fields = [] }

              let updatedEntry =
                  { oldEntry with
                      Confidence = 0.92
                      Status = Confirmed }

              let existing =
                  { (sampleLockFile ()) with
                      Mappings = [ oldEntry ] }

              let resolved = [ updatedEntry ]
              let merged = LockFile.merge existing resolved
              Expect.contains merged.Mappings updatedEntry "updated convention entry should replace old"

              Expect.isFalse
                  (merged.Mappings
                   |> List.exists (fun m -> m.FsharpType = "MyApp.Order" && m.Confidence = 0.5))
                  "old convention entry should not remain"
          }

          test "entries in existing with no counterpart in resolved are preserved" {
              let preserved =
                  { FsharpType = "MyApp.OldType"
                    Iri = "schema:Thing"
                    Confidence = 0.7
                    Source = Convention
                    Status = Confirmed
                    Fields = [] }

              let existing =
                  { (sampleLockFile ()) with
                      Mappings = [ preserved ] }

              let resolved: TypeMapping list = []
              let merged = LockFile.merge existing resolved
              Expect.contains merged.Mappings preserved "unmatched existing entry should be preserved"
          } ]

[<Tests>]
let writeCreatesDirectory =
    testList
        "write creates parent directory if absent"
        [ test "write to non-existent directory creates it" {
              let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              let path = Path.Combine(dir, "sub", "test.lock.json")

              try
                  let lf = sampleLockFile ()
                  LockFile.write path lf
                  Expect.isTrue (File.Exists path) "file should exist after write"
              finally
                  if Directory.Exists dir then
                      Directory.Delete(dir, true)
          } ]
