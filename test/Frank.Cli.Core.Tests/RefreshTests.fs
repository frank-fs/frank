module Frank.Cli.Core.Tests.RefreshTests

open System
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core.Refresh
open Frank.Cli.Core.Tests.RefreshFixtures

// ── Helpers ───────────────────────────────────────────────────────────────────

let private mkLock (vocabs: Map<string, VocabularyEntry>) : LockFile =
    { SchemaVersion = 2
      Generated = DateTimeOffset.UnixEpoch
      Integrity = None
      Vocabularies = vocabs
      DeclaredPrefixes = Map.empty
      Mappings = [] }

let private fixedNow = DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero)

let private runRefresh (fetch: ConnegFetch) (lf: LockFile) : RefreshReport * LockFile =
    refresh fetch SlaPolicy.defaultPolicy fixedNow false lf |> Async.RunSynchronously

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let refreshTests =
    testList
        "Refresh — core per-entry continuation and classification"
        [ testCase "hash drift detected — DriftDetected outcome with reason"
          <| fun () ->
              let entry =
                  { mkVocabEntry "DEADBEEF" with
                      FetchedAt = fixedNow.AddDays(-35.0) }

              let lock = mkLock (Map.ofList [ "schema", entry ])
              let altBody = Text.Encoding.UTF8.GetBytes "changed"
              let fetch = stubTurtleConnegFetch altBody
              let (report, _) = runRefresh fetch lock

              let driftOutcomes =
                  report.Outcomes
                  |> List.choose (fun (p, o) ->
                      match o with
                      | DriftDetected r -> Some(p, r)
                      | _ -> None)

              Expect.equal driftOutcomes.Length 1 "one drift outcome"
              let (prefix, _reason) = driftOutcomes.[0]
              Expect.equal prefix "schema" "prefix"
              Expect.equal (refreshExitCode report) 2 "exit code 2 on drift"

          testCase "no drift — EvidenceRefreshed, exit 0"
          <| fun () ->
              let entry =
                  { mkVocabEntry schemaBodyHash with
                      FetchedAt = fixedNow.AddDays(-35.0) }

              let lock = mkLock (Map.ofList [ "schema", entry ])
              let fetch = stubTurtleConnegFetch schemaBody
              let (report, _) = runRefresh fetch lock

              Expect.equal
                  (report.Outcomes |> List.forall (fun (_, o) -> match o with | EvidenceRefreshed -> true | _ -> false))
                  true
                  "all EvidenceRefreshed"

              Expect.equal (refreshExitCode report) 0 "exit code 0 on no drift"

          testCase "empty vocabularies — empty outcomes, exit 0"
          <| fun () ->
              let lock = mkLock Map.empty
              let fetch = stubConnegFetch (FetchFailed "should not be called")
              let (report, _) = runRefresh fetch lock
              Expect.equal report.Outcomes [] "no outcomes"
              Expect.equal (refreshExitCode report) 0 "exit 0"

          testCase "all entries visited — per-entry continuation"
          <| fun () ->
              let entry1 =
                  { mkVocabEntry "DEADBEEF" with
                      FetchedAt = fixedNow.AddDays(-35.0)
                      Uri = "http://localhost:9001/v1" }

              let entry2 =
                  { mkVocabEntry "DEADBEEF" with
                      FetchedAt = fixedNow.AddDays(-35.0)
                      Uri = "http://localhost:9002/v2" }

              let lock =
                  mkLock (Map.ofList [ "v1", entry1; "v2", entry2 ])

              let count = ref 0

              let fetch : ConnegFetch =
                  fun _uri _etag _lastMod ->
                      incr count
                      async { return HttpErrorStatus(404, Uri "http://localhost:9001/v1") }

              let (report, _) = runRefresh fetch lock
              Expect.equal !count 2 "both entries visited (no early abort)"
              Expect.equal report.Outcomes.Length 2 "two outcomes" ]
