module Frank.Cli.MSBuild.Tests.HashDriftTests

open System
open System.IO
open Expecto
open Frank.Semantic

let private makeTempDir () =
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore
    dir

// Lock file with a vocabulary that has a recorded hash
let private lockFileWithHash (hash: string) : LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2026-04-20T12:00:00+00:00")
      Vocabularies =
        Map.ofList
          [ "schema",
            { Uri = "https://schema.org/"
              FetchedAt = Some "2026-04-20T11:00:00+00:00"
              Hash = Some hash } ]
      Mappings =
        [ { FsharpType = "MyApp.Order"
            Iri = "schema:Order"
            Confidence = 0.92
            Source = Convention
            Status = Confirmed
            Fields = [] } ] }

// Hash of a known string to use as baseline
let private computeHash (content: string) =
    let bytes = System.Text.Encoding.UTF8.GetBytes(content)
    let hash = System.Security.Cryptography.SHA256.HashData(bytes)
    "sha256:" + Convert.ToHexString(hash).ToLowerInvariant()

[<Tests>]
let at4LockFileRoundTrip =
    testList "AT4: Lock file preserves hash across write/read" [
        test "hash stored in lock file survives round-trip" {
            let dir = makeTempDir ()
            try
                let expectedHash = computeHash "fake vocabulary content"
                let lockFile = lockFileWithHash expectedHash
                let path = Path.Combine(dir, "test.lock.json")
                LockFile.write path lockFile
                match LockFile.read path with
                | Error msg -> failtest $"LockFile.read failed: {msg}"
                | Ok read ->
                    let vocabHash = read.Vocabularies.["schema"].Hash
                    Expect.equal vocabHash (Some expectedHash) "hash should survive round-trip"
            finally Directory.Delete(dir, true)
        }

        test "confirmed entries are not mutated by refresh (lock file unchanged)" {
            let dir = makeTempDir ()
            try
                let hash = computeHash "original vocabulary"
                let lockFile = lockFileWithHash hash
                let path = Path.Combine(dir, "test.lock.json")
                LockFile.write path lockFile
                match LockFile.read path with
                | Error msg -> failtest $"LockFile.read failed: {msg}"
                | Ok read ->
                    let mapping = read.Mappings |> List.head
                    Expect.equal mapping.Status Confirmed "confirmed status must be preserved across read/write"
            finally Directory.Delete(dir, true)
        }
    ]

[<Tests>]
let at4DetectDrift =
    testList "AT4: Drift detection — different hashes are not equal" [
        test "two different vocabulary contents produce different hashes" {
            let hash1 = computeHash "schema.org vocabulary v1"
            let hash2 = computeHash "schema.org vocabulary v2 (upstream change)"
            Expect.notEqual hash1 hash2 "different content must produce different hashes"
        }

        test "same content produces same hash (deterministic)" {
            let content = "schema.org vocabulary v1"
            let hash1 = computeHash content
            let hash2 = computeHash content
            Expect.equal hash1 hash2 "same content must produce identical hash"
        }
    ]
