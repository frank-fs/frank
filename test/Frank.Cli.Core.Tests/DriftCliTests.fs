module Frank.Cli.Core.Tests.DriftCliTests

open System
open System.IO
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabFetcher
open Frank.Cli.Core.Refresh
open Frank.Cli.Core.Tests.RefreshFixtures
open Frank.TestSupport.TempDir

// ── Fixtures ──────────────────────────────────────────────────────────────────

let private altSchemaBody: byte[] =
    Text.Encoding.UTF8.GetBytes """{ "@context": "https://schema.org/", "@comment": "updated" }"""

let private altSchemaBodyHash: string = sha256Hex altSchemaBody

let private confirmedLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Vocabularies = Map.ofList [ "schema", mkVocabEntry schemaBodyHash ]
      DeclaredPrefixes = Map.empty
      Mappings =
        [ { FSharpType = "TicTacToe.Game"
            Iri = Some "schema:Game"
            Confidence = 1.0
            Source = Convention
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "identifier"
                      Iri = Some "schema:identifier"
                      Confidence = 1.0
                      Source = Convention
                      Status = Confirmed } ] } ] }

// ── AT4 drift completeness tests ─────────────────────────────────────────────

[<Tests>]
let driftExitCodeTests =
    testList
        "AT4 — refreshExitCode maps Result to exit 2/0/1"
        [ test "exit code is 2 when drifted list is non-empty" {
              let report: RefreshReport =
                  { Checked = 1
                    Drifted =
                      [ { Prefix = "schema"
                          Recorded = "DEADBEEF"
                          Current = altSchemaBodyHash } ] }

              Expect.equal (refreshExitCode (Ok report)) 2 "exit 2 when drift present"
          }

          test "exit code is 0 when drifted list is empty" {
              let report: RefreshReport = { Checked = 1; Drifted = [] }
              Expect.equal (refreshExitCode (Ok report)) 0 "zero exit when no drift"
          }

          test "exit code is 1 on Error" { Expect.equal (refreshExitCode (Error "boom")) 1 "Error maps to exit 1" }

          test "AT4: refresh with altered fetch → refreshExitCode returns 2" {
              let driftedLock =
                  { confirmedLock with
                      Vocabularies = Map.ofList [ "schema", mkVocabEntry "STALE_HASH" ] }

              let fetch = stubFetch schemaBody
              let result = refresh fetch driftedLock |> Async.RunSynchronously

              match result with
              | Error e -> failtest $"unexpected error: {e}"
              | Ok report -> Expect.equal (refreshExitCode (Ok report)) 2 "CLI returns 2 when drift detected"
          } ]

[<Tests>]
let lockImmutabilityTests =
    testList
        "AT4 — confirmed lock file not auto-mutated by refresh"
        [ test "lock file bytes are identical before and after refresh when drift detected" {
              withTempDir (fun dir ->
                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")
                  LockFile.write lockPath confirmedLock

                  let bytesBefore = File.ReadAllBytes lockPath

                  let altFetch = stubFetch altSchemaBody
                  let result = refresh altFetch confirmedLock |> Async.RunSynchronously

                  match result with
                  | Error e -> failtest $"unexpected error: {e}"
                  | Ok report -> Expect.equal report.Drifted.Length 1 "drift was detected"

                  let bytesAfter = File.ReadAllBytes lockPath

                  Expect.equal bytesAfter bytesBefore "confirmed lock file must not be mutated by refresh")
          }

          test "confirmed mapping IRI is byte-identical after refresh with drift" {
              withTempDir (fun dir ->
                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")
                  LockFile.write lockPath confirmedLock

                  let lockTextBefore = File.ReadAllText lockPath

                  let altFetch = stubFetch altSchemaBody
                  refresh altFetch confirmedLock |> Async.RunSynchronously |> ignore

                  let lockTextAfter = File.ReadAllText lockPath

                  Expect.equal lockTextAfter lockTextBefore "lock file text unchanged after drift refresh")
          } ]

let private mappingHasUndecided (m: Mapping) : bool =
    let selfUndecided = not (isDecided m.Status)

    let fieldUndecided =
        MappingShape.activePayloadFields m.Shape
        |> List.exists (fun f -> not (isDecided f.Status))

    selfUndecided || fieldUndecided

let private lockHasUndecidedLiveEntry (lock: LockFile) : bool =
    lock.Mappings
    |> List.filter (fun m -> m.Status <> Excluded)
    |> List.exists mappingHasUndecided

[<Tests>]
let buildGateAfterDriftTests =
    testList
        "AT4 — lock gate logic passes after drift detected (mappings unchanged)"
        [ test "confirmed-only lock has zero undecided entries even after drift detected" {
              withTempDir (fun dir ->
                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")
                  LockFile.write lockPath confirmedLock

                  let altFetch = stubFetch altSchemaBody

                  let report =
                      refresh altFetch confirmedLock
                      |> Async.RunSynchronously
                      |> Result.defaultWith (fun e -> failwith $"unexpected refresh error: {e}")

                  Expect.equal report.Drifted.Length 1 "drift detected (precondition)"

                  let lock =
                      LockFile.read lockPath
                      |> Result.defaultWith (fun e -> failwith $"could not re-read lock: {e}")

                  Expect.isFalse
                      (lockHasUndecidedLiveEntry lock)
                      "gate must pass: no undecided entries after vocab drift")
          } ]
