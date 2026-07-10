module Frank.Cli.Core.Tests.RefreshV3Tests

open System
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core.Refresh
open Frank.Cli.Core.Tests.RefreshFixtures

// ── Fixtures ──────────────────────────────────────────────────────────────────

let private fixedNow = DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero)

let private mkLockV2 (vocabs: (string * VocabularyEntry) list) : LockFile =
    { SchemaVersion = 2
      Generated = fixedNow
      Integrity = None
      Vocabularies = Map.ofList vocabs
      DeclaredPrefixes = Map.empty
      Mappings = [] }

let private runRefresh (fetch: ConnegFetch) (force: bool) (lf: LockFile) : RefreshReport * LockFile =
    refresh fetch SlaPolicy.defaultPolicy fixedNow force lf |> Async.RunSynchronously

let private hasDrift (report: RefreshReport) : bool =
    report.Outcomes
    |> List.exists (fun (_, o) ->
        match o with
        | DriftDetected _ -> true
        | _ -> false)

let private hasFailed (report: RefreshReport) : bool =
    report.Outcomes
    |> List.exists (fun (_, o) ->
        match o with
        | ProbeFailed _ -> true
        | _ -> false)

// ── A-C4: link-rot vs transient exit codes ────────────────────────────────────

[<Tests>]
let ac4Tests =
    testList
        "A-C4 — link-rot vs transient exit codes"
        [ testCase "404 → DriftDetected, Validated=false, exit 2"
          <| fun () ->
              let entry =
                  { mkUnownedEntry "http://localhost:9301/v" 35.0 with
                      Hash = schemaBodyHash }

              let lock = mkLockV2 [ "vocab", entry ]
              let stubUri = Uri "http://localhost:9301/v"
              let fetch = stubConnegFetch (HttpErrorStatus(404, stubUri))
              let (report, updatedLock) = runRefresh fetch false lock
              Expect.isTrue (hasDrift report) "DriftDetected on 404"
              Expect.equal (refreshExitCode report) 2 "exit 2 on 404"
              let updatedEntry = updatedLock.Vocabularies.["vocab"]
              Expect.isFalse updatedEntry.Validated.IsValidated "Validated=false after 404"
              Expect.isSome updatedEntry.Validated.Reason "reason set after 404"
              Expect.stringContains (updatedEntry.Validated.Reason.Value) "404" "reason mentions 404"

          testCase "410 → DriftDetected, Validated=false, exit 2"
          <| fun () ->
              let entry =
                  { mkUnownedEntry "http://localhost:9302/v" 35.0 with
                      Hash = schemaBodyHash }

              let lock = mkLockV2 [ "vocab", entry ]
              let stubUri = Uri "http://localhost:9302/v"
              let fetch = stubConnegFetch (HttpErrorStatus(410, stubUri))
              let (report, updatedLock) = runRefresh fetch false lock
              Expect.isTrue (hasDrift report) "DriftDetected on 410"
              Expect.equal (refreshExitCode report) 2 "exit 2 on 410"
              let updatedEntry = updatedLock.Vocabularies.["vocab"]
              Expect.isFalse updatedEntry.Validated.IsValidated "Validated=false after 410"

          testCase "503 → ProbeFailed, Validated UNCHANGED from prior, exit 1"
          <| fun () ->
              let priorValidated =
                  { IsValidated = true
                    Reason = None
                    LastChecked = Some(fixedNow.AddDays(-35.0)) }

              let entry =
                  { mkUnownedEntry "http://localhost:9303/v" 35.0 with
                      Validated = priorValidated }

              let lock = mkLockV2 [ "vocab", entry ]
              let stubUri = Uri "http://localhost:9303/v"
              let fetch = stubConnegFetch (HttpErrorStatus(503, stubUri))
              let (report, updatedLock) = runRefresh fetch false lock
              Expect.isFalse (hasDrift report) "no drift on 503"
              Expect.isTrue (hasFailed report) "ProbeFailed on 503"
              Expect.equal (refreshExitCode report) 1 "exit 1 on 503"
              let updatedEntry = updatedLock.Vocabularies.["vocab"]
              Expect.equal updatedEntry.Validated.IsValidated true "IsValidated UNCHANGED (still true)"

          testCase "network error → ProbeFailed, Validated UNCHANGED, exit 1"
          <| fun () ->
              let priorValidated =
                  { IsValidated = true
                    Reason = None
                    LastChecked = Some(fixedNow.AddDays(-35.0)) }

              let entry =
                  { mkUnownedEntry "http://localhost:9304/v" 35.0 with
                      Validated = priorValidated }

              let lock = mkLockV2 [ "vocab", entry ]
              let fetch = stubConnegFetch (FetchFailed "connection refused")
              let (report, updatedLock) = runRefresh fetch false lock
              Expect.isFalse (hasDrift report) "no drift on network error"
              Expect.isTrue (hasFailed report) "ProbeFailed on network error"
              Expect.equal (refreshExitCode report) 1 "exit 1 on network error"
              let updatedEntry = updatedLock.Vocabularies.["vocab"]
              Expect.equal updatedEntry.Validated.IsValidated true "IsValidated UNCHANGED"

          testCase "--force on 503 does NOT flip Validated to true"
          <| fun () ->
              let priorValidated =
                  { IsValidated = false
                    Reason = Some "not checked"
                    LastChecked = None }

              let entry =
                  { mkUnownedEntry "http://localhost:9305/v" 5.0 with
                      Validated = priorValidated }

              let lock = mkLockV2 [ "vocab", entry ]
              let stubUri = Uri "http://localhost:9305/v"
              let fetch = stubConnegFetch (HttpErrorStatus(503, stubUri))
              let (report, updatedLock) = runRefresh fetch true lock
              Expect.isFalse (hasDrift report) "no drift on 503 even with force"
              Expect.isTrue (hasFailed report) "ProbeFailed"
              let updatedEntry = updatedLock.Vocabularies.["vocab"]
              Expect.isFalse updatedEntry.Validated.IsValidated "--force must not launder 503 into Validated=true"

          testCase "drift-dominates: 404 on one entry + 503 on another → exit 2"
          <| fun () ->
              let entry1 =
                  { mkUnownedEntry "http://localhost:9306/a" 35.0 with
                      Hash = schemaBodyHash }

              let entry2 =
                  { mkUnownedEntry "http://localhost:9307/b" 35.0 with
                      Validated = { IsValidated = true; Reason = None; LastChecked = None } }

              let lock = mkLockV2 [ "a", entry1; "b", entry2 ]

              let fetch : ConnegFetch =
                  fun uri _etag _lastMod ->
                      async {
                          let path = uri.AbsolutePath

                          if path.Contains "/a" then
                              return HttpErrorStatus(404, uri)
                          else
                              return HttpErrorStatus(503, uri)
                      }

              let (report, _) = runRefresh fetch false lock
              Expect.isTrue (hasDrift report) "DriftDetected on /a"
              Expect.isTrue (hasFailed report) "ProbeFailed on /b"
              Expect.equal (refreshExitCode report) 2 "drift dominates 503" ]

// ── A-C5: per-entry continuation ─────────────────────────────────────────────

[<Tests>]
let ac5Tests =
    testList
        "A-C5 — per-entry continuation (no early abort)"
        [ testCase "one dead (404) + two live entries → all three visited"
          <| fun () ->
              let deadEntry =
                  { mkUnownedEntry "http://localhost:9401/dead" 35.0 with
                      Hash = schemaBodyHash }

              let liveEntry1 =
                  { mkUnownedEntry "http://localhost:9402/live1" 35.0 with
                      Hash = schemaBodyHash }

              let liveEntry2 =
                  { mkUnownedEntry "http://localhost:9403/live2" 35.0 with
                      Hash = schemaBodyHash }

              let lock = mkLockV2 [ "dead", deadEntry; "live1", liveEntry1; "live2", liveEntry2 ]
              let visitCount = ref 0

              let fetch : ConnegFetch =
                  fun uri _etag _lastMod ->
                      incr visitCount

                      async {
                          if uri.AbsolutePath.Contains "dead" then
                              return HttpErrorStatus(404, uri)
                          else
                              return turtleResult schemaBody
                      }

              let (report, _) = runRefresh fetch false lock
              Expect.equal !visitCount 3 "all three entries visited (no early abort)"
              Expect.equal report.Outcomes.Length 3 "three outcomes"

              let driftOutcomes =
                  report.Outcomes
                  |> List.filter (fun (_, o) ->
                      match o with
                      | DriftDetected _ -> true
                      | _ -> false)

              let refreshedOutcomes =
                  report.Outcomes
                  |> List.filter (fun (_, o) ->
                      match o with
                      | EvidenceRefreshed -> true
                      | _ -> false)

              Expect.equal driftOutcomes.Length 1 "one drift (the dead entry)"
              Expect.equal refreshedOutcomes.Length 2 "two live entries refreshed"
              Expect.equal (refreshExitCode report) 2 "exit 2 because of the dead entry" ]

// ── A-C8: owned reachability SLA ─────────────────────────────────────────────

[<Tests>]
let ac8Tests =
    testList
        "A-C8 — owned reachability SLA (90d)"
        [ testCase "owned entry within 90d is NOT re-probed (request count = 0)"
          <| fun () ->
              let entry = mkOwnedEntry "http://localhost:9501/vocab" 85.0

              let lock = mkLockV2 [ "vocab", entry ]
              let (fetch, count) = countingConnegFetch (turtleResult schemaBody)
              let (report, _) = runRefresh fetch false lock
              Expect.equal !count 0 "NOT re-probed within 90d SLA"

              let outcome = report.Outcomes |> List.head |> snd
              Expect.equal outcome SkippedFresh "SkippedFresh within 90d"

          testCase "owned entry past 90d IS reachability-probed (request count = 1)"
          <| fun () ->
              let entry = mkOwnedEntry "http://localhost:9502/vocab" 95.0

              let lock = mkLockV2 [ "vocab", entry ]
              let (fetch, count) = countingConnegFetch (turtleResult schemaBody)
              let (report, _) = runRefresh fetch false lock
              Expect.equal !count 1 "re-probed past 90d SLA"

              let outcome = report.Outcomes |> List.head |> snd
              Expect.equal outcome EvidenceRefreshed "EvidenceRefreshed past 90d"

          testCase "owned entry with same hash past 90d → EvidenceRefreshed (no content-drift for owned)"
          <| fun () ->
              // Valid Turtle with different content than schemaBody → different hash, but owned so NOT drift.
              let altBody =
                  Text.Encoding.UTF8.GetBytes
                      "@prefix schema: <https://schema.org/> .\nschema:Person a <http://www.w3.org/2000/01/rdf-schema#Class> .\n"

              let entry =
                  { mkOwnedEntry "http://localhost:9503/vocab" 95.0 with
                      Hash = schemaBodyHash }

              let lock = mkLockV2 [ "vocab", entry ]
              let fetch = stubTurtleConnegFetch altBody
              let (report, updatedLock) = runRefresh fetch false lock

              let outcome = report.Outcomes |> List.head |> snd
              Expect.equal outcome EvidenceRefreshed "owned: content change is NOT flagged as drift"
              Expect.equal (refreshExitCode report) 0 "exit 0 for owned content change (only reachability)"

          testCase "owned entry past 90d that 404s → DriftDetected, Validated=false, exit 2"
          <| fun () ->
              let entry = mkOwnedEntry "http://localhost:9504/vocab" 95.0

              let lock = mkLockV2 [ "vocab", entry ]
              let stubUri = Uri "http://localhost:9504/vocab"
              let fetch = stubConnegFetch (HttpErrorStatus(404, stubUri))
              let (report, updatedLock) = runRefresh fetch false lock
              Expect.isTrue (hasDrift report) "owned 404 is drift"
              Expect.equal (refreshExitCode report) 2 "exit 2 on owned 404"
              let updatedEntry = updatedLock.Vocabularies.["vocab"]
              Expect.isFalse updatedEntry.Validated.IsValidated "Validated=false on owned 404" ]

// ── A-C9: unowned SLA ─────────────────────────────────────────────────────────

[<Tests>]
let ac9Tests =
    testList
        "A-C9 — unowned SLA (30d)"
        [ testCase "unowned entry within 30d is NOT re-fetched (request count = 0)"
          <| fun () ->
              let entry = mkUnownedEntry "http://localhost:9601/vocab" 25.0

              let lock = mkLockV2 [ "vocab", entry ]
              let (fetch, count) = countingConnegFetch (turtleResult schemaBody)
              let (report, _) = runRefresh fetch false lock
              Expect.equal !count 0 "NOT re-fetched within 30d SLA"

              let outcome = report.Outcomes |> List.head |> snd
              Expect.equal outcome SkippedFresh "SkippedFresh within 30d"

          testCase "unowned entry past 30d IS re-fetched (request count = 1)"
          <| fun () ->
              let entry =
                  { mkUnownedEntry "http://localhost:9602/vocab" 35.0 with
                      Hash = schemaBodyHash }

              let lock = mkLockV2 [ "vocab", entry ]
              let (fetch, count) = countingConnegFetch (turtleResult schemaBody)
              let (report, _) = runRefresh fetch false lock
              Expect.equal !count 1 "re-fetched past 30d SLA"

          testCase "--force re-fetches even within 30d (request count = 1)"
          <| fun () ->
              let entry =
                  { mkUnownedEntry "http://localhost:9603/vocab" 5.0 with
                      Hash = schemaBodyHash }

              let lock = mkLockV2 [ "vocab", entry ]
              let (fetch, count) = countingConnegFetch (turtleResult schemaBody)
              let (report, _) = runRefresh fetch true lock
              Expect.equal !count 1 "force re-fetches within 30d"

              let outcome = report.Outcomes |> List.head |> snd
              Expect.equal outcome EvidenceRefreshed "EvidenceRefreshed with --force within 30d" ]

// ── M1: 406/415/401/403 → durable Undereferenceable (exit 2) ─────────────────

[<Tests>]
let m1DurableHttpStatusTests =
    testList
        "M1 — 406/415/401/403 are durable (exit 2), 5xx remain transient (exit 1)"
        [ testCase "406 → DriftDetected, Validated=false, exit 2"
          <| fun () ->
              let entry =
                  { mkUnownedEntry "http://localhost:9701/v" 35.0 with
                      Hash = schemaBodyHash }

              let lock = mkLockV2 [ "vocab", entry ]
              let stubUri = Uri "http://localhost:9701/v"
              let fetch = stubConnegFetch (HttpErrorStatus(406, stubUri))
              let (report, updatedLock) = runRefresh fetch false lock
              Expect.isTrue (hasDrift report) "DriftDetected on 406"
              Expect.equal (refreshExitCode report) 2 "exit 2 on 406"
              let updatedEntry = updatedLock.Vocabularies.["vocab"]
              Expect.isFalse updatedEntry.Validated.IsValidated "Validated=false after 406"
              Expect.isSome updatedEntry.Validated.Reason "reason set after 406"

          testCase "415 → DriftDetected, exit 2"
          <| fun () ->
              let entry =
                  { mkUnownedEntry "http://localhost:9702/v" 35.0 with
                      Hash = schemaBodyHash }

              let lock = mkLockV2 [ "vocab", entry ]
              let stubUri = Uri "http://localhost:9702/v"
              let fetch = stubConnegFetch (HttpErrorStatus(415, stubUri))
              let (report, _) = runRefresh fetch false lock
              Expect.isTrue (hasDrift report) "DriftDetected on 415"
              Expect.equal (refreshExitCode report) 2 "exit 2 on 415"

          testCase "401 → DriftDetected auth-walled, exit 2"
          <| fun () ->
              let entry =
                  { mkUnownedEntry "http://localhost:9703/v" 35.0 with
                      Hash = schemaBodyHash }

              let lock = mkLockV2 [ "vocab", entry ]
              let stubUri = Uri "http://localhost:9703/v"
              let fetch = stubConnegFetch (HttpErrorStatus(401, stubUri))
              let (report, updatedLock) = runRefresh fetch false lock
              Expect.isTrue (hasDrift report) "DriftDetected on 401"
              Expect.equal (refreshExitCode report) 2 "exit 2 on 401"
              let updatedEntry = updatedLock.Vocabularies.["vocab"]
              Expect.isSome updatedEntry.Validated.Reason "reason set for 401"
              Expect.stringContains (updatedEntry.Validated.Reason.Value) "auth" "reason mentions auth-walled"

          testCase "503 regression — still ProbeFailed, Validated unchanged, exit 1"
          <| fun () ->
              let priorValidated =
                  { IsValidated = true
                    Reason = None
                    LastChecked = Some(fixedNow.AddDays(-35.0)) }

              let entry =
                  { mkUnownedEntry "http://localhost:9704/v" 35.0 with
                      Validated = priorValidated }

              let lock = mkLockV2 [ "vocab", entry ]
              let stubUri = Uri "http://localhost:9704/v"
              let fetch = stubConnegFetch (HttpErrorStatus(503, stubUri))
              let (report, updatedLock) = runRefresh fetch false lock
              Expect.isFalse (hasDrift report) "no drift on 503"
              Expect.isTrue (hasFailed report) "ProbeFailed on 503"
              Expect.equal (refreshExitCode report) 1 "exit 1 on 503"
              let updatedEntry = updatedLock.Vocabularies.["vocab"]
              Expect.equal updatedEntry.Validated.IsValidated true "IsValidated UNCHANGED on 503" ]

// ── M2: unowned text/html → UnverifiableNonRdf (NOT exit 2) ──────────────────

[<Tests>]
let m2HtmlNonRdfTests =
    testList
        "M2 — unowned text/html is not durable drift; owned text/html is still drift (A-C7)"
        [ testCase "unowned text/html 200 → NOT exit 2 (non-durable, unverifiable)"
          <| fun () ->
              let entry = mkUnownedEntry "http://localhost:9801/v" 35.0
              let lock = mkLockV2 [ "vocab", entry ]

              let fetch =
                  stubConnegFetch (NonRdfContent {| MediaType = "text/html"; HttpStatus = 200 |})

              let (report, _) = runRefresh fetch false lock
              Expect.isFalse (hasDrift report) "text/html from unowned must NOT be drift"
              Expect.notEqual (refreshExitCode report) 2 "exit must NOT be 2 for unowned text/html"

          testCase "unowned text/html entry has Validated=false (unverifiable, not confirmed)"
          <| fun () ->
              let entry =
                  { mkUnownedEntry "http://localhost:9802/v" 35.0 with
                      Validated = { IsValidated = true; Reason = None; LastChecked = None } }

              let lock = mkLockV2 [ "vocab", entry ]

              let fetch =
                  stubConnegFetch (NonRdfContent {| MediaType = "text/html"; HttpStatus = 200 |})

              let (_, updatedLock) = runRefresh fetch false lock
              let updatedEntry = updatedLock.Vocabularies.["vocab"]
              Expect.isFalse updatedEntry.Validated.IsValidated "Validated=false for unverifiable text/html" ]

// ── M4: owned path through buildEvidence — reachability failures durable ──────

[<Tests>]
let m4OwnedBuildEvidenceTests =
    testList
        "M4 — owned classifyOwned is a transform over buildEvidence"
        [ testCase "owned entry hitting RedirectCapHit → DriftDetected (durable), exit 2"
          <| fun () ->
              let entry = mkOwnedEntry "http://localhost:9901/vocab" 95.0
              let lock = mkLockV2 [ "vocab", entry ]
              let fetch = stubConnegFetch RedirectCapHit
              let (report, updatedLock) = runRefresh fetch false lock
              Expect.isTrue (hasDrift report) "owned RedirectCapHit must be DriftDetected"
              Expect.equal (refreshExitCode report) 2 "exit 2 on owned RedirectCapHit"
              let updatedEntry = updatedLock.Vocabularies.["vocab"]
              Expect.isFalse updatedEntry.Validated.IsValidated "Validated=false on owned redirect cap"

          testCase "owned 404 → DriftDetected durable (regression after M4 refactor)"
          <| fun () ->
              let entry = mkOwnedEntry "http://localhost:9902/vocab" 95.0
              let lock = mkLockV2 [ "vocab", entry ]
              let stubUri = Uri "http://localhost:9902/vocab"
              let fetch = stubConnegFetch (HttpErrorStatus(404, stubUri))
              let (report, _) = runRefresh fetch false lock
              Expect.isTrue (hasDrift report) "owned 404 must still be DriftDetected after M4 refactor"
              Expect.equal (refreshExitCode report) 2 "exit 2 on owned 404"

          testCase "owned content hash change → NOT drift (suppress content-drift, A-C8 regression)"
          <| fun () ->
              // Valid Turtle with different content → different hash than schemaBodyHash, but owned so NOT drift.
              let altBody =
                  Text.Encoding.UTF8.GetBytes
                      "@prefix schema: <https://schema.org/> .\nschema:Person a <http://www.w3.org/2000/01/rdf-schema#Class> .\n"

              let entry =
                  { mkOwnedEntry "http://localhost:9903/vocab" 95.0 with
                      Hash = schemaBodyHash }

              let lock = mkLockV2 [ "vocab", entry ]
              let fetch = stubTurtleConnegFetch altBody
              let (report, _) = runRefresh fetch false lock
              Expect.isFalse (hasDrift report) "owned: content hash change is NOT drift (suppress)"
              Expect.equal (refreshExitCode report) 0 "exit 0 — owned content change is reachability only" ]

// ── M5: any 2xx → success ────────────────────────────────────────────────────

[<Tests>]
let m5Any2xxTests =
    testList
        "M5 — any 2xx status accepted, not just 200"
        [ testCase "203 + Turtle body → EvidenceRefreshed, Validated=true"
          <| fun () ->
              let entry =
                  { mkUnownedEntry "http://localhost:9951/v" 35.0 with
                      Hash = schemaBodyHash }

              let lock = mkLockV2 [ "vocab", entry ]

              let result203 =
                  RdfContent
                      {| MediaType = "text/turtle"
                         Body = schemaBody
                         HttpStatus = 203
                         ETag = None
                         LastModified = None
                         CacheControlMaxAge = None |}

              let fetch = stubConnegFetch result203
              let (report, updatedLock) = runRefresh fetch false lock
              Expect.isFalse (hasDrift report) "203 + Turtle: no drift"
              Expect.isFalse (hasFailed report) "203 + Turtle: not a failure"
              let updatedEntry = updatedLock.Vocabularies.["vocab"]
              Expect.isTrue updatedEntry.Validated.IsValidated "203 + Turtle → Validated=true" ]
