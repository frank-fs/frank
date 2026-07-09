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

let private fixedNow = DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero)

let private confirmedLock: LockFile =
    { SchemaVersion = 2
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Integrity = None
      Vocabularies =
        Map.ofList
            [ "schema",
              { mkVocabEntry schemaBodyHash with
                  FetchedAt = fixedNow.AddDays(-35.0)
                  Uri = "https://schema.org/" } ]
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

// ── AT4 exit code unit tests ──────────────────────────────────────────────────

[<Tests>]
let driftExitCodeTests =
    testList
        "AT4 — refreshExitCode: exit 2/1/0 from EntryOutcome list"
        [ test "exit code is 2 when DriftDetected present" {
              let report: RefreshReport =
                  { Outcomes = [ "schema", DriftDetected "HTTP 404 — gone" ] }

              Expect.equal (refreshExitCode report) 2 "exit 2 when drift present"
          }

          test "exit code is 1 when only ProbeFailed present" {
              let report: RefreshReport =
                  { Outcomes = [ "schema", ProbeFailed "HTTP 503 probe-failed" ] }

              Expect.equal (refreshExitCode report) 1 "exit 1 on probe failure"
          }

          test "exit code is 2 when both DriftDetected and ProbeFailed (drift dominates)" {
              let report: RefreshReport =
                  { Outcomes =
                      [ "a", DriftDetected "gone"
                        "b", ProbeFailed "transient" ] }

              Expect.equal (refreshExitCode report) 2 "drift dominates over probe failure"
          }

          test "exit code is 0 when only EvidenceRefreshed and SkippedFresh" {
              let report: RefreshReport =
                  { Outcomes =
                      [ "a", EvidenceRefreshed
                        "b", SkippedFresh ] }

              Expect.equal (refreshExitCode report) 0 "exit 0 when clean"
          }

          test "AT4: refresh with drifted content → exit 2" {
              let altBody = Text.Encoding.UTF8.GetBytes "{ \"@context\": \"changed\" }"
              let fetch = stubTurtleConnegFetch altBody
              let driftedLock = { confirmedLock with Vocabularies = Map.ofList [ "schema", mkVocabEntry "STALE_HASH" |> (fun e -> { e with FetchedAt = fixedNow.AddDays(-35.0); Uri = "https://schema.org/" }) ] }
              let (report, _) = refresh fetch SlaPolicy.defaultPolicy fixedNow false driftedLock |> Async.RunSynchronously
              Expect.equal (refreshExitCode report) 2 "CLI returns 2 when drift detected"
          } ]

// ── Lock immutability: refresh does not write to disk ─────────────────────────

[<Tests>]
let lockImmutabilityTests =
    testList
        "AT4 — refresh (core function) does not write to disk"
        [ test "lock file bytes are identical before and after calling refresh" {
              withTempDir (fun dir ->
                  let lockPath = Path.Combine(dir, "semantic-mappings.lock.json")
                  LockFile.write lockPath confirmedLock

                  let bytesBefore = File.ReadAllBytes lockPath

                  let altBody = Text.Encoding.UTF8.GetBytes "{ \"@context\": \"changed\" }"
                  let altFetch = stubTurtleConnegFetch altBody
                  let (_report, _) = refresh altFetch SlaPolicy.defaultPolicy fixedNow false confirmedLock |> Async.RunSynchronously

                  let bytesAfter = File.ReadAllBytes lockPath
                  Expect.equal bytesAfter bytesBefore "core refresh must not write to disk")
          }

          test "mappings are preserved in updated lock after drift detected" {
              withTempDir (fun dir ->
                  let altBody = Text.Encoding.UTF8.GetBytes "changed"
                  let altFetch = stubTurtleConnegFetch altBody

                  let (report, updatedLock) =
                      refresh altFetch SlaPolicy.defaultPolicy fixedNow false confirmedLock
                      |> Async.RunSynchronously

                  let hasDrift =
                      report.Outcomes
                      |> List.exists (fun (_, o) ->
                          match o with
                          | DriftDetected _ -> true
                          | _ -> false)

                  Expect.isTrue hasDrift "drift detected (precondition)"
                  Expect.equal updatedLock.Mappings confirmedLock.Mappings "mappings unchanged in updated lock")
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

                  let altFetch = stubTurtleConnegFetch (Text.Encoding.UTF8.GetBytes "changed")

                  let (report, _) =
                      refresh altFetch SlaPolicy.defaultPolicy fixedNow false confirmedLock
                      |> Async.RunSynchronously

                  let hasDrift =
                      report.Outcomes
                      |> List.exists (fun (_, o) ->
                          match o with
                          | DriftDetected _ -> true
                          | _ -> false)

                  Expect.isTrue hasDrift "drift detected (precondition)"

                  let lock =
                      LockFile.read lockPath
                      |> Result.defaultWith (fun e -> failwith $"could not re-read lock: {e}")

                  Expect.isFalse
                      (lockHasUndecidedLiveEntry lock)
                      "gate must pass: no undecided entries after vocab drift")
          } ]
