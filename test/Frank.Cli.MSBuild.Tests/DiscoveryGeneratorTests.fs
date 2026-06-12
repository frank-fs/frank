module Frank.Cli.MSBuild.Tests.DiscoveryGeneratorTests

open System
open System.IO
open Expecto
open Frank.Cli.MSBuild
open Frank.Semantic

// ── Helpers ──────────────────────────────────────────────────────────────────

let private makeTempDir () =
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore
    dir

let private makeLockFile () : LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2026-04-20T12:00:00+00:00")
      Vocabularies =
        Map.ofList
            [ "schema",
              { Uri = "https://schema.org/"
                FetchedAt = None
                Hash = None } ]
      Mappings =
        [ { FsharpType = "MyApp.Order"
            Iri = "schema:Order"
            Confidence = 0.92
            Source = Convention
            Status = Confirmed
            Fields =
              [ { Name = "OrderId"
                  Iri = "schema:identifier"
                  Confidence = 0.9
                  Source = Convention
                  Status = Confirmed
                  Pattern = None }
                { Name = "Notes"
                  Iri = "schema:description"
                  Confidence = 0.6
                  Source = Convention
                  Status = Proposed
                  Pattern = None } ] }
          { FsharpType = "MyApp.Invoice"
            Iri = "schema:Invoice"
            Confidence = 0.5
            Source = Convention
            Status = Proposed
            Fields = [] } ] }

let private makeLockFileEmpty () : LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2026-04-20T12:00:00+00:00")
      Vocabularies = Map.empty
      Mappings = [] }

let private runTask (lockFile: LockFile) (outputDir: string) =
    let lockPath = Path.Combine(outputDir, "test.lock.json")
    LockFile.write lockPath lockFile

    let task = GenerateDiscoveryTask()
    task.LockFilePath <- lockPath
    task.OutputDirectory <- outputDir
    let ok = task.Execute()
    ok, Path.Combine(outputDir, "GeneratedDiscovery.fs")

// ── AT1: Generated file exists ────────────────────────────────────────────────

[<Tests>]
let at1 =
    testList
        "AT1: GenerateDiscoveryTask writes GeneratedDiscovery.fs"
        [ test "output file is created in OutputDirectory" {
              let dir = makeTempDir ()

              try
                  let ok, outFile = runTask (makeLockFile ()) dir
                  Expect.isTrue ok "Execute() should return true"
                  Expect.isTrue (File.Exists outFile) $"GeneratedDiscovery.fs should exist at {outFile}"
              finally
                  Directory.Delete(dir, true)
          }

          test "output module header matches expected pattern" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "module GeneratedDiscovery" "module declaration required"
                  Expect.stringContains content "let alpsDescriptors" "alpsDescriptors binding required"
                  Expect.stringContains content "let describedByLinks" "describedByLinks binding required"
              finally
                  Directory.Delete(dir, true)
          } ]

// ── AT2: alpsDescriptors contains vocabulary IRIs ─────────────────────────────

[<Tests>]
let at2 =
    testList
        "AT2: alpsDescriptors entries contain expanded vocabulary IRIs"
        [ test "confirmed type descriptor has Href with expanded IRI" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "https://schema.org/Order" "schema:Order CURIE must be expanded"
                  Expect.isFalse (content.Contains "urn:frank:") "must not use urn:frank: IRIs in discovery output"
              finally
                  Directory.Delete(dir, true)
          }

          test "confirmed field descriptor uses lowercased field name as Id" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "\"orderid\"" "field Id must be field name lowercased"
                  Expect.stringContains content "https://schema.org/identifier" "schema:identifier must be expanded"
              finally
                  Directory.Delete(dir, true)
          }

          test "proposed type mapping excluded from alpsDescriptors" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile
                  Expect.isFalse (content.Contains "MyApp.Invoice") "proposed mapping must not appear"
              finally
                  Directory.Delete(dir, true)
          }

          test "proposed field mapping excluded from child descriptors" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile
                  Expect.isFalse (content.Contains "\"notes\"") "proposed field must not appear"
              finally
                  Directory.Delete(dir, true)
          }

          test "empty lock file produces empty maps" {
              let dir = makeTempDir ()

              try
                  let ok, outFile = runTask (makeLockFileEmpty ()) dir
                  Expect.isTrue ok "Execute() should return true for empty lock file"
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "Map.ofList []" "empty mappings must emit Map.ofList []"
              finally
                  Directory.Delete(dir, true)
          } ]

// ── AT3: describedByLinks are RFC 8288 compliant ──────────────────────────────

[<Tests>]
let at3 =
    testList
        "AT3: describedByLinks entries are RFC 8288 Link header values"
        [ test "describedBy link uses angle-bracket IRI with rel=describedby" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "<https://schema.org/Order>" "Link value must use angle-bracket IRI"
                  Expect.stringContains content "rel=\\\"describedby\\\"" "Link value must include rel=describedby"
              finally
                  Directory.Delete(dir, true)
          }

          test "describedBy link key is F# full type name" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "\"MyApp.Order\"" "map key must be F# full type name"
              finally
                  Directory.Delete(dir, true)
          } ]

// ── AT4: Determinism ──────────────────────────────────────────────────────────

[<Tests>]
let at4 =
    testList
        "AT4 (Discovery): Two calls with the same lock file produce identical output"
        [ test "byte-identical output for repeated calls" {
              let dir1 = makeTempDir ()
              let dir2 = makeTempDir ()

              try
                  let lockFile = makeLockFile ()
                  let _, out1 = runTask lockFile dir1
                  let _, out2 = runTask lockFile dir2
                  let bytes1 = File.ReadAllBytes out1
                  let bytes2 = File.ReadAllBytes out2
                  Expect.equal bytes1 bytes2 "repeated calls should produce identical output"
              finally
                  Directory.Delete(dir1, true)
                  Directory.Delete(dir2, true)
          } ]
