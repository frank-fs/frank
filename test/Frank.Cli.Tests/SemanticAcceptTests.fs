module Frank.Cli.Tests.SemanticAcceptTests

open System
open System.IO
open Expecto
open Frank.Semantic
open Frank.Cli

// ── Helpers ──────────────────────────────────────────────────────────────────

let private makeLockFile (mappings: TypeMapping list) : LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.UtcNow
      Vocabularies = Map.empty
      Mappings = mappings }

let private writeTempLockFile (mappings: TypeMapping list) : string =
    let tmpDir =
        Path.Combine(Path.GetTempPath(), "frank_accept_test_" + Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory(tmpDir) |> ignore
    let lockPath = Path.Combine(tmpDir, "semantic-mappings.lock.json")
    LockFile.write lockPath (makeLockFile mappings)
    lockPath

let private deleteTempDir (path: string) =
    try
        Directory.Delete(Path.GetDirectoryName(path), true)
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
            Pattern = None } ] }

let private resolvedJsonV1 =
    """{"schemaVersion": 1, "resolved": [{"fsharpType": "MyApp.OrderLine", "iri": "schema:OrderItem", "fields": [{"name": "Quantity", "iri": "schema:orderQuantity"}]}]}"""

let private resolvedJsonSchemaMismatch =
    """{"schemaVersion": 99, "resolved": []}"""

let private resolvedJsonUnknownType =
    """{"schemaVersion": 1, "resolved": [{"fsharpType": "MyApp.Nonexistent", "iri": "schema:Thing", "fields": []}]}"""

let private resolvedJsonMixed =
    """{"schemaVersion": 1, "resolved": [{"fsharpType": "MyApp.Nonexistent", "iri": "schema:Thing", "fields": []}, {"fsharpType": "MyApp.OrderLine", "iri": "schema:OrderItem", "fields": []}]}"""

// ── AT1: accept merges valid resolutions ─────────────────────────────────────

[<Tests>]
let at1AcceptMergesValidResolutions =
    testList
        "AT1: accept merges valid resolutions"
        [ test "accept returns Ok with merge summary" {
              let lockPath = writeTempLockFile [ unresolvedMapping ]

              try
                  let result = SemanticCommands.accept lockPath resolvedJsonV1 Llm

                  match result with
                  | Error msg -> failtest $"Expected Ok but got Error: {msg}"
                  | Ok summary ->
                      Expect.isTrue (summary.Contains("Merged 1")) "summary should report 1 merged"
                      Expect.isTrue (summary.Contains("0 rejected")) "summary should report 0 rejected"
              finally
                  deleteTempDir lockPath
          }

          test "accept updates mapping to Confirmed with Llm source" {
              let lockPath = writeTempLockFile [ unresolvedMapping ]

              try
                  let result = SemanticCommands.accept lockPath resolvedJsonV1 Llm
                  Expect.isOk result "accept should succeed"

                  match LockFile.read lockPath with
                  | Error msg -> failtest $"Could not read lock file: {msg}"
                  | Ok lf ->
                      let mapping = lf.Mappings |> List.find (fun m -> m.FsharpType = "MyApp.OrderLine")
                      Expect.equal mapping.Status Confirmed "status should be Confirmed"
                      Expect.equal mapping.Source Llm "source should be Llm"
                      Expect.equal mapping.Iri "schema:OrderItem" "iri should be set"
              finally
                  deleteTempDir lockPath
          }

          test "accept exit 0 — returns Ok" {
              let lockPath = writeTempLockFile [ unresolvedMapping ]

              try
                  let result = SemanticCommands.accept lockPath resolvedJsonV1 Llm
                  Expect.isOk result "accept should return Ok (exit 0)"
              finally
                  deleteTempDir lockPath
          } ]

// ── AT2: accept rejects schema-version mismatch ───────────────────────────────

[<Tests>]
let at2AcceptRejectsSchemaMismatch =
    testList
        "AT2: accept rejects schema-version mismatch"
        [ test "accept returns Error for schemaVersion 99" {
              let lockPath = writeTempLockFile [ unresolvedMapping ]
              let originalContent = File.ReadAllText lockPath

              try
                  let result = SemanticCommands.accept lockPath resolvedJsonSchemaMismatch Llm

                  match result with
                  | Ok _ -> failtest "Expected Error for unsupported schema version"
                  | Error msg -> Expect.isTrue (msg.Contains("99")) "error should mention the version number"
              finally
                  deleteTempDir lockPath
          }

          test "lock file unchanged after schema-version error" {
              let lockPath = writeTempLockFile [ unresolvedMapping ]
              let originalContent = File.ReadAllText lockPath

              try
                  SemanticCommands.accept lockPath resolvedJsonSchemaMismatch Llm |> ignore
                  let afterContent = File.ReadAllText lockPath
                  Expect.equal afterContent originalContent "lock file must not change on error"
              finally
                  deleteTempDir lockPath
          } ]

// ── AT3: accept rejects unknown F# types ─────────────────────────────────────

[<Tests>]
let at3AcceptIgnoresUnknownTypes =
    testList
        "AT3: accept rejects unknown F# types"
        [ test "accept returns Ok even when resolved.json contains unknown types" {
              let lockPath = writeTempLockFile [ unresolvedMapping ]

              try
                  let result = SemanticCommands.accept lockPath resolvedJsonUnknownType Llm
                  Expect.isOk result "should return Ok; unknown types are warnings, not errors"
              finally
                  deleteTempDir lockPath
          }

          test "valid entries still merge when mixed with unknown types" {
              let lockPath = writeTempLockFile [ unresolvedMapping ]

              try
                  let result = SemanticCommands.accept lockPath resolvedJsonMixed Llm
                  Expect.isOk result "should return Ok"

                  match LockFile.read lockPath with
                  | Error msg -> failtest $"Could not read lock file: {msg}"
                  | Ok lf ->
                      let mapping = lf.Mappings |> List.find (fun m -> m.FsharpType = "MyApp.OrderLine")
                      Expect.equal mapping.Status Confirmed "known type should be Confirmed"
                      let hasUnknown = lf.Mappings |> List.exists (fun m -> m.FsharpType = "MyApp.Nonexistent")
                      Expect.isFalse hasUnknown "unknown type should not appear in lock file"
              finally
                  deleteTempDir lockPath
          } ]

// ── AT4: refresh detects drift ────────────────────────────────────────────────

[<Tests>]
let at4RefreshDetectsDrift =
    testList
        "AT4: refresh detects drift"
        [ test "refresh returns empty list when no cache files exist" {
              let lockPath = writeTempLockFile []

              try
                  let cacheDir =
                      Path.Combine(Path.GetTempPath(), "frank_cache_" + Guid.NewGuid().ToString("N"))

                  let drifted = SemanticCommands.refresh lockPath cacheDir
                  Expect.isEmpty drifted "no drift when cache is empty"
              finally
                  deleteTempDir lockPath
          }

          test "refresh detects drift when cached file hash differs from lock file hash" {
              let tmpDir =
                  Path.Combine(Path.GetTempPath(), "frank_drift_test_" + Guid.NewGuid().ToString("N"))

              Directory.CreateDirectory(tmpDir) |> ignore
              let cacheDir = Path.Combine(tmpDir, "cache")
              Directory.CreateDirectory(cacheDir) |> ignore

              let prefix = "schema"
              // VocabFetcher.findCached searches for "<prefix>.<something>.<ext>" via glob "<prefix>.*.ttl"
              let cachedPath = Path.Combine(cacheDir, $"{prefix}.aabbccdd.ttl")
              File.WriteAllText(cachedPath, "@prefix schema: <https://schema.org/> .")

              let lockFile =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.UtcNow
                    Vocabularies =
                      Map.ofList
                          [ prefix,
                            { Uri = "https://schema.org/"
                              FetchedAt = None
                              Hash = Some "sha256:deadbeef000000000000000000000000000000000000000000000000deadbeef" } ]
                    Mappings = [] }

              let lockPath = Path.Combine(tmpDir, "semantic-mappings.lock.json")
              LockFile.write lockPath lockFile

              try
                  let drifted = SemanticCommands.refresh lockPath cacheDir
                  Expect.equal (List.length drifted) 1 "one drifted vocabulary"
                  let p, oldH, newH = drifted.[0]
                  Expect.equal p prefix "drifted prefix"
                  Expect.isTrue (oldH <> newH) "hashes should differ"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "refresh does not mutate confirmed lock-file entries" {
              let mappings = [ unresolvedMapping ]
              let lockPath = writeTempLockFile mappings
              let originalContent = File.ReadAllText lockPath

              try
                  let cacheDir =
                      Path.Combine(Path.GetTempPath(), "frank_empty_cache_" + Guid.NewGuid().ToString("N"))

                  SemanticCommands.refresh lockPath cacheDir |> ignore
                  let afterContent = File.ReadAllText lockPath
                  Expect.equal afterContent originalContent "lock file must not change after refresh"
              finally
                  deleteTempDir lockPath
          } ]

// ── AT5: status summary ───────────────────────────────────────────────────────

[<Tests>]
let at5StatusSummary =
    testList
        "AT5: status summary"
        [ test "status returns formatted counts" {
              let confirmed : TypeMapping =
                  { FsharpType = "MyApp.A"
                    Iri = "schema:A"
                    Confidence = 1.0
                    Source = Convention
                    Status = Confirmed
                    Fields = [] }

              let proposed : TypeMapping =
                  { FsharpType = "MyApp.B"
                    Iri = "schema:B"
                    Confidence = 0.7
                    Source = Convention
                    Status = Proposed
                    Fields = [] }

              let lockPath =
                  writeTempLockFile
                      [ confirmed
                        confirmed |> fun m -> { m with FsharpType = "MyApp.A2" }
                        proposed
                        unresolvedMapping ]

              try
                  let output = SemanticCommands.status lockPath
                  Expect.isTrue (output.Contains("2")) "output should mention 2 confirmed"
                  Expect.isTrue (output.Contains("1")) "output should mention 1 proposed and 1 unresolved"
                  Expect.isTrue (output.ToLower().Contains("confirmed")) "output should mention Confirmed"
                  Expect.isTrue (output.ToLower().Contains("proposed")) "output should mention Proposed"
                  Expect.isTrue (output.ToLower().Contains("unresolved")) "output should mention Unresolved"
              finally
                  deleteTempDir lockPath
          }

          test "status with only confirmed mappings" {
              let confirmed : TypeMapping =
                  { FsharpType = "MyApp.A"
                    Iri = "schema:A"
                    Confidence = 1.0
                    Source = Convention
                    Status = Confirmed
                    Fields = [] }

              let lockPath = writeTempLockFile (List.replicate 42 confirmed |> List.mapi (fun i m -> { m with FsharpType = $"MyApp.T{i}" }))

              try
                  let output = SemanticCommands.status lockPath
                  Expect.isTrue (output.Contains("42")) "output should report 42 confirmed"
              finally
                  deleteTempDir lockPath
          }

          test "status with empty lock file" {
              let lockPath = writeTempLockFile []

              try
                  let output = SemanticCommands.status lockPath
                  Expect.isTrue (output.Contains("0")) "output should report 0 counts"
              finally
                  deleteTempDir lockPath
          } ]
