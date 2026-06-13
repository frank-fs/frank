module Frank.Cli.MSBuild.Tests.VocabSwapTests

open System
open System.IO
open Expecto
open Frank.Cli.MSBuild
open Frank.Semantic

let private makeTempDir () =
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore
    dir

// Lock file using schema.org vocabulary
let private schemaLockFile : LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2026-04-20T12:00:00+00:00")
      Vocabularies =
        Map.ofList
          [ "schema", { Uri = "https://schema.org/"; FetchedAt = None; Hash = None } ]
      Mappings =
        [ { FsharpType = "TicTacToe.MoveRequest"
            Iri = "schema:MoveAction"
            Confidence = 0.88
            Source = Manual
            Status = Confirmed
            Fields =
              [ { Name = "Row"; Iri = "schema:rowIndex"; Confidence = 0.85; Source = Manual; Status = Confirmed; Pattern = None }
                { Name = "Col"; Iri = "schema:columnIndex"; Confidence = 0.82; Source = Manual; Status = Confirmed; Pattern = None }
                { Name = "Player"; Iri = "schema:agent"; Confidence = 0.90; Source = Manual; Status = Confirmed; Pattern = None } ] } ] }

// Lock file using ex: vocabulary (simulates vocab swap)
let private exLockFile : LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2026-04-20T12:00:00+00:00")
      Vocabularies =
        Map.ofList
          [ "ex", { Uri = "http://example.com/vocab#"; FetchedAt = None; Hash = None } ]
      Mappings =
        [ { FsharpType = "TicTacToe.MoveRequest"
            Iri = "ex:MoveAction"
            Confidence = 0.88
            Source = Manual
            Status = Confirmed
            Fields =
              [ { Name = "Row"; Iri = "ex:rowIndex"; Confidence = 0.85; Source = Manual; Status = Confirmed; Pattern = None }
                { Name = "Col"; Iri = "ex:columnIndex"; Confidence = 0.82; Source = Manual; Status = Confirmed; Pattern = None }
                { Name = "Player"; Iri = "ex:agent"; Confidence = 0.90; Source = Manual; Status = Confirmed; Pattern = None } ] } ] }

let private runValidation (lockFile: LockFile) (dir: string) =
    let lockPath = Path.Combine(dir, "test.lock.json")
    LockFile.write lockPath lockFile
    let task = GenerateValidationTask()
    task.LockFilePath <- lockPath
    task.OutputDirectory <- dir
    task.Execute() |> ignore
    Path.Combine(dir, "GeneratedValidation.fs")

let private runLinkedData (lockFile: LockFile) (dir: string) =
    let lockPath = Path.Combine(dir, "test.lock.json")
    LockFile.write lockPath lockFile
    let task = GenerateLinkedDataTask()
    task.LockFilePath <- lockPath
    task.OutputDirectory <- dir
    task.Execute() |> ignore
    Path.Combine(dir, "GeneratedLinkedData.fs")

let private runDiscovery (lockFile: LockFile) (dir: string) =
    let lockPath = Path.Combine(dir, "test.lock.json")
    LockFile.write lockPath lockFile
    let task = GenerateDiscoveryTask()
    task.LockFilePath <- lockPath
    task.OutputDirectory <- dir
    task.Execute() |> ignore
    Path.Combine(dir, "GeneratedDiscovery.fs")

[<Tests>]
let at1Validation =
    testList "AT1: ValidationGenerator vocab swap" [
        test "schema lock → generated shapes use schema.org IRIs" {
            let dir = makeTempDir ()
            try
                let outFile = runValidation schemaLockFile dir
                let content = File.ReadAllText outFile
                Expect.stringContains content "https://schema.org/rowIndex" "schema lock should emit schema:rowIndex IRI"
                Expect.isFalse (content.Contains("http://example.com/vocab#")) "schema lock must not contain ex: IRIs"
            finally Directory.Delete(dir, true)
        }

        test "ex lock → generated shapes use ex: IRIs" {
            let dir = makeTempDir ()
            try
                let outFile = runValidation exLockFile dir
                let content = File.ReadAllText outFile
                Expect.stringContains content "http://example.com/vocab#rowIndex" "ex lock should emit ex:rowIndex IRI"
                Expect.isFalse (content.Contains("https://schema.org/rowIndex")) "ex lock must not contain schema:rowIndex IRI"
            finally Directory.Delete(dir, true)
        }

        test "schema and ex outputs differ" {
            let dir1 = makeTempDir ()
            let dir2 = makeTempDir ()
            try
                let f1 = runValidation schemaLockFile dir1
                let f2 = runValidation exLockFile dir2
                let c1 = File.ReadAllText f1
                let c2 = File.ReadAllText f2
                Expect.notEqual c1 c2 "vocab swap must produce different generated output"
            finally
                Directory.Delete(dir1, true)
                Directory.Delete(dir2, true)
        }
    ]

[<Tests>]
let at1LinkedData =
    testList "AT1: LinkedDataGenerator vocab swap" [
        test "schema lock → generated graph uses schema.org IRIs" {
            let dir = makeTempDir ()
            try
                let outFile = runLinkedData schemaLockFile dir
                let content = File.ReadAllText outFile
                Expect.stringContains content "https://schema.org/MoveAction" "schema lock should emit schema:MoveAction IRI"
                Expect.isFalse (content.Contains("http://example.com/vocab#")) "schema lock must not contain ex: IRIs"
            finally Directory.Delete(dir, true)
        }

        test "ex lock → generated graph uses ex: IRIs" {
            let dir = makeTempDir ()
            try
                let outFile = runLinkedData exLockFile dir
                let content = File.ReadAllText outFile
                Expect.stringContains content "http://example.com/vocab#MoveAction" "ex lock should emit ex:MoveAction IRI"
                Expect.isFalse (content.Contains("https://schema.org/MoveAction")) "ex lock must not contain schema:MoveAction"
            finally Directory.Delete(dir, true)
        }
    ]

[<Tests>]
let at1Discovery =
    testList "AT1: DiscoveryGenerator vocab swap" [
        test "schema lock → generated ALPS descriptors use schema.org hrefs" {
            let dir = makeTempDir ()
            try
                let outFile = runDiscovery schemaLockFile dir
                let content = File.ReadAllText outFile
                Expect.stringContains content "https://schema.org/rowIndex" "schema lock should emit schema:rowIndex in ALPS descriptor"
                Expect.isFalse (content.Contains("http://example.com/vocab#")) "schema lock must not contain ex: IRIs"
            finally Directory.Delete(dir, true)
        }

        test "ex lock → generated ALPS descriptors use ex: hrefs" {
            let dir = makeTempDir ()
            try
                let outFile = runDiscovery exLockFile dir
                let content = File.ReadAllText outFile
                Expect.stringContains content "http://example.com/vocab#rowIndex" "ex lock should emit ex:rowIndex in ALPS descriptor"
                Expect.isFalse (content.Contains("https://schema.org/rowIndex")) "ex lock must not contain schema:rowIndex"
            finally Directory.Delete(dir, true)
        }
    ]
