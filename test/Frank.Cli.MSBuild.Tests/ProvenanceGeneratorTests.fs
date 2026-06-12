module Frank.Cli.MSBuild.Tests.ProvenanceGeneratorTests

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
            Fields = [] }
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

    let task = GenerateProvenanceTask()
    task.LockFilePath <- lockPath
    task.OutputDirectory <- outputDir
    let ok = task.Execute()
    ok, Path.Combine(outputDir, "GeneratedProvenance.fs")

// ── AT1: Generated file exists ────────────────────────────────────────────────

[<Tests>]
let at1 =
    testList
        "AT1: GenerateProvenanceTask writes GeneratedProvenance.fs"
        [ test "output file is created in OutputDirectory" {
              let dir = makeTempDir ()

              try
                  let ok, outFile = runTask (makeLockFile ()) dir
                  Expect.isTrue ok "Execute() should return true"
                  Expect.isTrue (File.Exists outFile) $"GeneratedProvenance.fs should exist at {outFile}"
              finally
                  Directory.Delete(dir, true)
          }

          test "output module header matches expected pattern" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "module GeneratedProvenance" "module declaration required"
                  Expect.stringContains content "open Frank.Semantic" "Frank.Semantic open required"
                  Expect.stringContains content "let provClasses" "provClasses binding required"
                  Expect.stringContains content "let typeIris" "typeIris binding required"
              finally
                  Directory.Delete(dir, true)
          } ]

// ── AT2: provClasses map defined ─────────────────────────────────────────────

[<Tests>]
let at2 =
    testList
        "AT2: Generated file defines provClasses as a Map"
        [ test "provClasses binding uses Map type" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile

                  Expect.stringContains
                      content
                      "let provClasses : Map<string, ProvOClass>"
                      "provClasses must have Map<string, ProvOClass> type annotation"
              finally
                  Directory.Delete(dir, true)
          }

          test "typeIris binding uses Map type" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile

                  Expect.stringContains
                      content
                      "let typeIris : Map<string, string>"
                      "typeIris must have Map<string, string> type annotation"
              finally
                  Directory.Delete(dir, true)
          } ]

// ── AT3: Missing provClass type returns None ─────────────────────────────────

[<Tests>]
let at3 =
    testList
        "AT3: Generated maps use Map.empty (no provClass data in lock file)"
        [ test "provClasses is Map.empty when lock file has no provClass data" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "Map.empty" "provClasses must be Map.empty without provClass data"
              finally
                  Directory.Delete(dir, true)
          }

          test "empty lock file produces valid empty maps" {
              let dir = makeTempDir ()

              try
                  let ok, outFile = runTask (makeLockFileEmpty ()) dir
                  Expect.isTrue ok "Execute() should return true for empty lock file"
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "module GeneratedProvenance" "module declaration required"
                  Expect.stringContains content "Map.empty" "maps must be empty"
              finally
                  Directory.Delete(dir, true)
          } ]

// ── AT4: Determinism ──────────────────────────────────────────────────────────

[<Tests>]
let at4 =
    testList
        "AT4 (Provenance): Two calls with the same lock file produce identical output"
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
