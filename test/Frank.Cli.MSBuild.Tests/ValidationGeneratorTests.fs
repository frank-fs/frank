module Frank.Cli.MSBuild.Tests.ValidationGeneratorTests

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
              [ { Name = "Total"
                  Iri = "schema:totalPaymentDue"
                  Confidence = 0.78
                  Source = Llm
                  Status = Confirmed
                  Pattern = None }
                { Name = "ZipCode"
                  Iri = "schema:postalCode"
                  Confidence = 0.85
                  Source = Convention
                  Status = Confirmed
                  Pattern = Some "^\\d{5}$" } ] }
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

    let task = GenerateValidationTask()
    task.LockFilePath <- lockPath
    task.OutputDirectory <- outputDir
    let ok = task.Execute()
    ok, Path.Combine(outputDir, "GeneratedValidation.fs")

// ── AT1: Generated file compiles ─────────────────────────────────────────────

[<Tests>]
let at1 =
    testList
        "AT1: GenerateValidationTask writes GeneratedValidation.fs"
        [ test "output file is created in OutputDirectory" {
              let dir = makeTempDir ()

              try
                  let ok, outFile = runTask (makeLockFile ()) dir
                  Expect.isTrue ok "Execute() should return true"
                  Expect.isTrue (File.Exists outFile) $"GeneratedValidation.fs should exist at {outFile}"
              finally
                  Directory.Delete(dir, true)
          }

          test "output module header matches expected pattern" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "module GeneratedValidation" "module declaration required"
                  Expect.stringContains content "open VDS.RDF" "VDS.RDF open required"
                  Expect.stringContains content "open VDS.RDF.Shacl" "VDS.RDF.Shacl open required"
                  Expect.stringContains content "let shapesGraph : ShapesGraph" "shapesGraph binding required"
              finally
                  Directory.Delete(dir, true)
          } ]

// ── AT2: Shape targets vocabulary IRIs ───────────────────────────────────────

[<Tests>]
let at2 =
    testList
        "AT2: Generated shapes use vocabulary IRIs from lock file"
        [ test "sh:targetClass uses expanded vocabulary IRI" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile

                  Expect.stringContains
                      content
                      "https://schema.org/Order"
                      "sh:targetClass should use expanded IRI, not CURIE"

                  Expect.isFalse (content.Contains "schema:Order") "CURIE should be expanded to full IRI"
              finally
                  Directory.Delete(dir, true)
          }

          test "sh:path uses expanded vocabulary IRI for field" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile

                  Expect.stringContains
                      content
                      "https://schema.org/totalPaymentDue"
                      "sh:path should use expanded IRI for Total field"
              finally
                  Directory.Delete(dir, true)
          }

          test "proposed mappings excluded from shapes" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile

                  Expect.isFalse
                      (content.Contains "https://schema.org/Invoice")
                      "proposed mapping should not appear in shapes"
              finally
                  Directory.Delete(dir, true)
          }

          test "sh:NodeShape present for confirmed type" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "shacl#NodeShape" "sh:NodeShape triple required"
              finally
                  Directory.Delete(dir, true)
          }

          test "sh:property triple present for confirmed field" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "shacl#property" "sh:property triple required"
              finally
                  Directory.Delete(dir, true)
          } ]

// ── AT3: Validation report produced ─────────────────────────────────────────

[<Tests>]
let at3 =
    testList
        "AT3: Generated file constructs valid ShapesGraph structure"
        [ test "generated code uses ShapesGraph constructor" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "ShapesGraph(g)" "ShapesGraph must be constructed from graph"
              finally
                  Directory.Delete(dir, true)
          }

          test "generated code creates a new Graph" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "new Graph()" "Graph must be instantiated"
              finally
                  Directory.Delete(dir, true)
          } ]

// ── AT4: ConstraintPatterns from CE ─────────────────────────────────────────

[<Tests>]
let at4 =
    testList
        "AT4: sh:pattern emitted for fields with Pattern in lock file"
        [ test "sh:pattern value present for field with Pattern" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFile ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "shacl#pattern" "sh:pattern triple required for ZipCode field"
                  Expect.stringContains content "^\\d{5}$" "pattern value should appear in generated code"
              finally
                  Directory.Delete(dir, true)
          }

          test "no sh:pattern emitted for field without Pattern" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFileEmpty ()) dir
                  let content = File.ReadAllText outFile

                  Expect.isFalse (content.Contains "shacl#pattern") "no sh:pattern when no fields with patterns"
              finally
                  Directory.Delete(dir, true)
          } ]

// ── AT5: Determinism ─────────────────────────────────────────────────────────

[<Tests>]
let at5 =
    testList
        "AT5: Two calls with the same lock file produce identical output"
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
