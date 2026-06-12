module Frank.Cli.MSBuild.Tests.LinkedDataGeneratorTests

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

let private makeLockFileWithVocabs () : LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2026-04-20T12:00:00+00:00")
      Vocabularies =
        Map.ofList
            [ "schema",
              { Uri = "https://schema.org/"
                FetchedAt = None
                Hash = None }
              "foaf",
              { Uri = "https://xmlns.com/foaf/0.1/"
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
                  Pattern = None } ] }
          { FsharpType = "MyApp.Person"
            Iri = "foaf:Person"
            Confidence = 0.95
            Source = Manual
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

    let task = GenerateLinkedDataTask()
    task.LockFilePath <- lockPath
    task.OutputDirectory <- outputDir
    let ok = task.Execute()
    ok, Path.Combine(outputDir, "GeneratedLinkedData.fs")

// ── AT1: Generated file exists ────────────────────────────────────────────────

[<Tests>]
let at1 =
    testList
        "AT1: GenerateLinkedDataTask writes GeneratedLinkedData.fs"
        [ test "output file is created in OutputDirectory" {
              let dir = makeTempDir ()

              try
                  let ok, outFile = runTask (makeLockFileWithVocabs ()) dir
                  Expect.isTrue ok "Execute() should return true"
                  Expect.isTrue (File.Exists outFile) $"GeneratedLinkedData.fs should exist at {outFile}"
              finally
                  Directory.Delete(dir, true)
          } ]

// ── AT2: Graph triples in generated content ───────────────────────────────────

[<Tests>]
let at2 =
    testList
        "AT2: Generated file contains triple construction for confirmed mappings"
        [ test "owl:equivalentClass triple present for confirmed TypeMapping" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFileWithVocabs ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "owl#equivalentClass" "should emit owl:equivalentClass triple"
                  Expect.stringContains content "https://schema.org/Order" "should expand schema:Order CURIE"
                  Expect.stringContains content "urn:frank:type:MyApp.Order" "should use frank IRI for type subject"
              finally
                  Directory.Delete(dir, true)
          }

          test "rdf:type owl:Class triple present for confirmed TypeMapping" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFileWithVocabs ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "owl#Class" "should emit rdf:type owl:Class triple"
              finally
                  Directory.Delete(dir, true)
          }

          test "field triples present for confirmed FieldMapping" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFileWithVocabs ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "urn:frank:property:MyApp.Order.OrderId" "should emit field subject IRI"
                  Expect.stringContains content "https://schema.org/identifier" "should expand schema:identifier CURIE"
              finally
                  Directory.Delete(dir, true)
          }

          test "proposed mappings excluded from triples" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFileWithVocabs ()) dir
                  let content = File.ReadAllText outFile

                  Expect.isFalse
                      (content.Contains "urn:frank:type:MyApp.Invoice")
                      "proposed mapping should not appear in triples"
              finally
                  Directory.Delete(dir, true)
          } ]

// ── AT3: @context includes vocabulary URIs ────────────────────────────────────

[<Tests>]
let at3 =
    testList
        "AT3: jsonLdContext includes vocabulary URIs from lock file"
        [ test "schema vocabulary URI appears in context" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFileWithVocabs ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "https://schema.org/" "schema vocab URI should be in jsonLdContext"
              finally
                  Directory.Delete(dir, true)
          }

          test "foaf vocabulary URI appears in context" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFileWithVocabs ()) dir
                  let content = File.ReadAllText outFile

                  Expect.stringContains
                      content
                      "https://xmlns.com/foaf/0.1/"
                      "foaf vocab URI should be in jsonLdContext"
              finally
                  Directory.Delete(dir, true)
          }

          test "empty vocabularies produces minimal context" {
              let dir = makeTempDir ()

              try
                  let _, outFile = runTask (makeLockFileEmpty ()) dir
                  let content = File.ReadAllText outFile
                  Expect.stringContains content "jsonLdContext" "should still define jsonLdContext binding"
                  Expect.stringContains content "{\"@context\": []}" "empty vocab list produces empty context array"
              finally
                  Directory.Delete(dir, true)
          } ]

// ── AT4: Determinism ──────────────────────────────────────────────────────────

[<Tests>]
let at4 =
    testList
        "AT4: Two calls with the same lock file produce identical output"
        [ test "byte-identical output for repeated calls" {
              let dir1 = makeTempDir ()
              let dir2 = makeTempDir ()

              try
                  let lockFile = makeLockFileWithVocabs ()
                  let _, out1 = runTask lockFile dir1
                  let _, out2 = runTask lockFile dir2
                  let bytes1 = File.ReadAllBytes out1
                  let bytes2 = File.ReadAllBytes out2
                  Expect.equal bytes1 bytes2 "repeated calls should produce identical output"
              finally
                  Directory.Delete(dir1, true)
                  Directory.Delete(dir2, true)
          } ]
