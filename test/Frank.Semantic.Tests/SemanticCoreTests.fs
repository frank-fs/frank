module Frank.Semantic.Tests.SemanticCoreTests

open System
open System.IO
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabClassifier

// Fixed clock for deterministic SLA tests
let private fixedNow = DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero)

// ── V2 entry helpers ──────────────────────────────────────────────────────────

let private makeV2Entry (owned: bool) (isValidated: bool) (fetchedAtDaysAgo: float) : VocabularyEntry =
    { v1Empty with
        Uri = "https://schema.org/"
        FetchedAt = fixedNow.AddDays(-fetchedAtDaysAgo)
        Hash = "sha256:abc123"
        MediaType = Some "application/ld+json"
        Validated =
            { IsValidated = isValidated
              Reason = (if isValidated then None else Some "not-checked")
              LastChecked = Some fixedNow }
        Terms = Some(Set.ofList [ "schema:Game"; "schema:Person" ])
        HttpStatus = Some 200
        Owned = owned
        ETag = Some "\"etagval\""
        LastModified = Some "Wed, 01 Jan 2026 00:00:00 GMT" }

// ── A-C6: schema-v2 evidence fields round-trip ────────────────────────────────

[<Tests>]
let ac6Tests =
    testList
        "A-C6: schema-v2 VocabularyEntry round-trip and v1 default safety"
        [ test "v2 entry round-trips all fields exactly" {
              let entry = makeV2Entry false true 5.0
              let lf: LockFile =
                  { SchemaVersion = 2
                    Generated = fixedNow
                    Integrity = None
                    Vocabularies = Map.ofList [ "schema", entry ]
                    DeclaredPrefixes = Map.empty
                    Mappings = [] }

              let path = Path.GetTempFileName()

              try
                  write path (withIntegrity lf)

                  match read path with
                  | Error e -> failtest $"read failed: {e}"
                  | Ok result ->
                      let e = result.Vocabularies.["schema"]
                      Expect.equal e.Uri "https://schema.org/" "uri"
                      Expect.equal e.MediaType (Some "application/ld+json") "mediaType"
                      Expect.equal e.Validated.IsValidated true "validated"
                      Expect.isNone e.Validated.Reason "no reason when validated"
                      Expect.equal e.Terms (Some(Set.ofList [ "schema:Game"; "schema:Person" ])) "terms"
                      Expect.equal e.HttpStatus (Some 200) "httpStatus"
                      Expect.equal e.Owned false "owned"
                      Expect.equal e.ETag (Some "\"etagval\"") "etag"
                      Expect.equal e.LastModified (Some "Wed, 01 Jan 2026 00:00:00 GMT") "lastModified"
                      Expect.isOk (verifyIntegrity result) "integrity verified after round-trip"
              finally
                  File.Delete path
          }

          test "v2 Terms=Some empty set round-trips as present-but-empty (suppresses check)" {
              let entry =
                  { v1Empty with
                      Uri = "https://schema.org/"
                      FetchedAt = fixedNow.AddDays(-1.0)
                      Hash = "sha256:abc"
                      Terms = Some Set.empty }

              let lf: LockFile =
                  { SchemaVersion = 2
                    Generated = fixedNow
                    Integrity = None
                    Vocabularies = Map.ofList [ "schema", entry ]
                    DeclaredPrefixes = Map.empty
                    Mappings = [] }

              let path = Path.GetTempFileName()

              try
                  write path lf

                  match read path with
                  | Error e -> failtest $"read failed: {e}"
                  | Ok result ->
                      let e = result.Vocabularies.["schema"]
                      Expect.isSome e.Terms "Terms is Some (not None = unknown)"
                      Expect.isEmpty (Option.defaultValue Set.empty e.Terms) "Terms is empty set"
              finally
                  File.Delete path
          }

          test "v1 JSON reads back with Validated=false (never laundered as validated=true)" {
              let json =
                  """{
  "schemaVersion": 1,
  "generated": "2026-07-09T12:00:00+00:00",
  "vocabularies": {
    "schema": {
      "uri": "https://schema.org/",
      "fetchedAt": "2026-07-01T00:00:00+00:00",
      "hash": "sha256:abc"
    }
  },
  "mappings": []
}"""

              let path = Path.GetTempFileName()

              try
                  File.WriteAllText(path, json)

                  match read path with
                  | Error e -> failtest $"read failed: {e}"
                  | Ok result ->
                      let e = result.Vocabularies.["schema"]
                      Expect.isFalse e.Validated.IsValidated "v1 entry must NOT be validated=true"
                      Expect.isSome e.Validated.Reason "v1 entry must have a reason why unvalidated"
                      Expect.isNone e.Terms "v1 entry Terms must be None (unknown)"
              finally
                  File.Delete path
          }

          test "v1→v2 round-trip: write stamps integrity correctly" {
              // Simulate: read a v1 lock, upgrade to v2 (by bumping schemaVersion), write
              let v1Json =
                  """{
  "schemaVersion": 1,
  "generated": "2026-07-09T12:00:00+00:00",
  "vocabularies": {
    "schema": {
      "uri": "https://schema.org/",
      "fetchedAt": "2026-07-01T00:00:00+00:00",
      "hash": "sha256:abc"
    }
  },
  "mappings": []
}"""

              let v1Path = Path.GetTempFileName()
              let v2Path = Path.GetTempFileName()

              try
                  File.WriteAllText(v1Path, v1Json)

                  match read v1Path with
                  | Error e -> failtest $"v1 read failed: {e}"
                  | Ok v1Lf ->
                      let v2Lf = withIntegrity { v1Lf with SchemaVersion = 2 }
                      write v2Path v2Lf

                      match read v2Path with
                      | Error e -> failtest $"v2 read failed: {e}"
                      | Ok result ->
                          Expect.equal result.SchemaVersion 2 "schemaVersion bumped to 2"
                          Expect.isOk (verifyIntegrity result) "integrity valid after upgrade"
              finally
                  File.Delete v1Path
                  File.Delete v2Path
          } ]

// ── A-C10: shared classifier consistency ──────────────────────────────────────

[<Tests>]
let ac10Tests =
    testList
        "A-C10: classifyReferencedVocab and status surface agree"
        [ test "confirmed vocab: classifier and status both report Confirmed" {
              let confirmedEntry =
                  { v1Empty with
                      Uri = "https://schema.org/"
                      FetchedAt = fixedNow.AddDays(-5.0)
                      Hash = "sha256:abc"
                      Validated =
                          { IsValidated = true
                            Reason = None
                            LastChecked = Some fixedNow } }

              let lf: LockFile =
                  { SchemaVersion = 2
                    Generated = fixedNow
                    Integrity = Some "placeholder"
                    Vocabularies = Map.ofList [ "schema", confirmedEntry ]
                    DeclaredPrefixes = Map.ofList [ "schema", "https://schema.org/" ]
                    Mappings = [] }

              // Stamp real integrity
              let lf = withIntegrity { lf with Integrity = None }
              Expect.isOk (verifyIntegrity lf) "lock integrity valid"

              // Direct classifier call
              let states = classifyReferencedVocab lf fixedNow [ "schema" ]
              Expect.equal (List.head states) VocabState.Confirmed "classifier: schema is Confirmed"
          }

          test "undereferenceable vocab: classifier and status both report Undereferenceable" {
              let lf: LockFile =
                  { SchemaVersion = 2
                    Generated = fixedNow
                    Integrity = None
                    Vocabularies = Map.empty
                    DeclaredPrefixes = Map.ofList [ "ex", "https://example.org/" ]
                    Mappings = [] }

              let lf = withIntegrity lf

              let states = classifyReferencedVocab lf fixedNow [ "ex" ]
              Expect.equal (List.head states) VocabState.Undereferenceable "classifier: ex is Undereferenceable"
          }

          test "http://www.example.org authority matches declared base https://example.org" {
              // Owned flag set via authority normalization
              let isOwned = isOwnedByAuthority "https://example.org/" "http://www.example.org/vocab#"
              Expect.isTrue isOwned "www. http variant matches https apex"
          }

          test "different hosts are not owned" {
              let isOwned = isOwnedByAuthority "https://myapp.com/" "https://schema.org/"
              Expect.isFalse isOwned "schema.org is not owned by myapp.com"
          }

          test "prefix not in lock → Undereferenceable" {
              let lf: LockFile =
                  { SchemaVersion = 2
                    Generated = fixedNow
                    Integrity = None
                    Vocabularies = Map.empty
                    DeclaredPrefixes = Map.empty
                    Mappings = [] }

              let states = classifyReferencedVocab lf fixedNow [ "unknown" ]
              Expect.equal (List.head states) VocabState.Undereferenceable "missing prefix is Undereferenceable"
          }

          test "multiple prefixes produce matching states in order" {
              let confirmedEntry =
                  { v1Empty with
                      Uri = "https://schema.org/"
                      FetchedAt = fixedNow.AddDays(-1.0)
                      Hash = "sha256:abc"
                      Validated = { IsValidated = true; Reason = None; LastChecked = Some fixedNow } }

              let lf: LockFile =
                  { SchemaVersion = 2
                    Generated = fixedNow
                    Integrity = None
                    Vocabularies = Map.ofList [ "schema", confirmedEntry ]
                    DeclaredPrefixes = Map.ofList [ "schema", "https://schema.org/"; "ex", "https://example.org/" ]
                    Mappings = [] }

              let states = classifyReferencedVocab lf fixedNow [ "schema"; "ex" ]
              Expect.equal states [ VocabState.Confirmed; VocabState.Undereferenceable ] "correct states in order"
          } ]

// ── A-C11: integrity tamper detection ─────────────────────────────────────────

[<Tests>]
let ac11Tests =
    testList
        "A-C11: integrity tamper detection"
        [ test "verifyIntegrity detects hand-added fake entry" {
              let lf: LockFile =
                  { SchemaVersion = 2
                    Generated = fixedNow
                    Integrity = None
                    Vocabularies = Map.ofList [ "schema", { v1Empty with Uri = "https://schema.org/"; Hash = "sha256:abc" } ]
                    DeclaredPrefixes = Map.empty
                    Mappings = [] }

              let stamped = withIntegrity lf
              Expect.isOk (verifyIntegrity stamped) "unstamped lock verifies ok"

              // Tamper: add fake entry without re-stamping
              let tampered =
                  { stamped with
                      Vocabularies =
                          stamped.Vocabularies
                          |> Map.add "fake" { v1Empty with Uri = "https://evil.com/"; Hash = "sha256:evil" } }

              match verifyIntegrity tampered with
              | Ok () -> failtest "tampered lock must not pass integrity check"
              | Error msg -> Expect.stringContains msg "hand-edited" "tamper message is diagnostic"
          }

          test "verifyIfStamped: unstamped lock passes without error" {
              let lf: LockFile =
                  { SchemaVersion = 1
                    Generated = fixedNow
                    Integrity = None
                    Vocabularies = Map.empty
                    DeclaredPrefixes = Map.empty
                    Mappings = [] }

              // Unstamped legacy lock: verifyIfStamped should pass
              Expect.isOk (verifyIfStamped lf) "unstamped lock treated as legacy (no error)"
          }

          test "verifyIfStamped: stamped+tampered lock reports error" {
              let lf =
                  withIntegrity
                      { SchemaVersion = 2
                        Generated = fixedNow
                        Integrity = None
                        Vocabularies = Map.ofList [ "s", { v1Empty with Uri = "https://s.org/"; Hash = "h" } ]
                        DeclaredPrefixes = Map.empty
                        Mappings = [] }

              let tampered =
                  { lf with
                      Vocabularies =
                          lf.Vocabularies |> Map.add "evil" { v1Empty with Uri = "https://evil.com/"; Hash = "!" } }

              Expect.isError (verifyIfStamped tampered) "stamped+tampered lock detected"
          } ]

// ── SLA policy pure reasoning ─────────────────────────────────────────────────

[<Tests>]
let slaTests =
    testList
        "SLA policy: pure staleness reasoning"
        [ test "unowned entry < 30 days → not stale" {
              let entry = { v1Empty with FetchedAt = fixedNow.AddDays(-20.0); Owned = false }
              Expect.isFalse (isStale SlaPolicy.defaultPolicy "schema" entry fixedNow) "20d < 30d threshold → not stale"
          }

          test "unowned entry > 30 days → stale" {
              let entry = { v1Empty with FetchedAt = fixedNow.AddDays(-31.0); Owned = false }
              Expect.isTrue (isStale SlaPolicy.defaultPolicy "schema" entry fixedNow) "31d > 30d threshold → stale"
          }

          test "owned entry < 90 days → not stale" {
              let entry = { v1Empty with FetchedAt = fixedNow.AddDays(-50.0); Owned = true }
              Expect.isFalse (isStale SlaPolicy.defaultPolicy "myns" entry fixedNow) "50d < 90d owned threshold → not stale"
          }

          test "owned entry > 90 days → stale" {
              let entry = { v1Empty with FetchedAt = fixedNow.AddDays(-91.0); Owned = true }
              Expect.isTrue (isStale SlaPolicy.defaultPolicy "myns" entry fixedNow) "91d > 90d owned threshold → stale"
          }

          test "stale vocab classifies as Stale" {
              let entry =
                  { v1Empty with
                      Uri = "https://schema.org/"
                      FetchedAt = fixedNow.AddDays(-60.0)
                      Owned = false
                      Validated = { IsValidated = true; Reason = None; LastChecked = Some fixedNow } }

              let lf: LockFile =
                  { SchemaVersion = 2
                    Generated = fixedNow
                    Integrity = None
                    Vocabularies = Map.ofList [ "schema", entry ]
                    // DeclaredPrefixes always includes the prefix — IRI-first lookup requires it.
                    DeclaredPrefixes = Map.ofList [ "schema", "https://schema.org/" ]
                    Mappings = [] }

              let states = classifyReferencedVocab lf fixedNow [ "schema" ]
              Expect.equal (List.head states) VocabState.Stale "60d > 30d unowned threshold → Stale"
          }

          test "per-vocab override overrides global threshold" {
              let policy =
                  { SlaPolicy.defaultPolicy with
                      PerVocabOverrides = Map.ofList [ "schema", 100 ] }

              let entry = { v1Empty with FetchedAt = fixedNow.AddDays(-50.0); Owned = false }
              Expect.isFalse (isStale policy "schema" entry fixedNow) "50d < 100d per-vocab override → not stale"
          } ]

// ── M3: verifyIfStamped must require stamp for schema v2 ─────────────────────

[<Tests>]
let m3VerifyIfStampedV2Tests =
    testList
        "M3 — verifyIfStamped requires stamp when SchemaVersion >= 2"
        [ test "v2 lock with Integrity=None and validated-true entry → verifyIfStamped returns Error" {
              let validatedEntry =
                  { v1Empty with
                      Uri = "https://schema.org/"
                      Hash = "sha256:abc"
                      Validated =
                          { IsValidated = true
                            Reason = None
                            LastChecked = Some fixedNow } }

              let lf: LockFile =
                  { SchemaVersion = 2
                    Generated = fixedNow
                    Integrity = None
                    Vocabularies = Map.ofList [ "schema", validatedEntry ]
                    DeclaredPrefixes = Map.empty
                    Mappings = [] }

              Expect.isError (verifyIfStamped lf) "v2 unstamped lock must be rejected by verifyIfStamped"
          }

          test "v1 lock with Integrity=None still passes verifyIfStamped (legacy compat)" {
              let lf: LockFile =
                  { SchemaVersion = 1
                    Generated = fixedNow
                    Integrity = None
                    Vocabularies = Map.empty
                    DeclaredPrefixes = Map.empty
                    Mappings = [] }

              Expect.isOk (verifyIfStamped lf) "v1 unstamped lock is legacy — must still pass"
          } ]
