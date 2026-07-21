module Frank.Cli.Core.Tests.VocabWarningTests

open System
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core

// ── Shared fixtures ───────────────────────────────────────────────────────────

let private emptyOracle: Accept.TermOracle =
    { Classes = Set.empty
      Properties = Set.empty
      Individuals = Set.empty
      CoveredBases = [] }

let private fixedNow = DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero)

let private confirmedSchemaEntry =
    { v1Empty with
        Uri = "https://schema.org/"
        Validated =
            { IsValidated = true
              Reason = None
              LastChecked = None }
        FetchedAt = fixedNow.AddDays(-5.0)
        Hash = "sha256:abc" }

// Entry for a type MoveRequest with a Position field referencing ttt:square
let private moveRequestJson =
    """{ "schemaVersion": 1, "resolved": [
         { "fsharpType": "App.MoveRequest", "iri": "schema:MoveAction", "shape": "record",
           "fields": [ { "name": "Position", "iri": "ttt:square" } ] } ] }"""

let private moveRequestMapping: Mapping =
    { FSharpType = "App.MoveRequest"
      Iri = None
      Confidence = 0.0
      Source = Convention
      Status = Unresolved
      Alternates = []
      Rt = None
      Shape =
        MappingShape.Record
            [ { Name = "Position"
                Iri = None
                Confidence = 0.0
                Source = Convention
                Status = Unresolved } ] }

// AT1 lock: "ttt" declared but NOT in Vocabularies → Undereferenceable
let private at1Lock: LockFile =
    { SchemaVersion = 2
      Generated = fixedNow
      Integrity = None
      Vocabularies = Map.ofList [ "schema", confirmedSchemaEntry ]
      DeclaredPrefixes =
        Map.ofList
            [ "schema", "https://schema.org/"
              "ttt", "https://example.org/tictactoe#" ]
      Mappings = [ moveRequestMapping ] }

// AT2 lock: "ttt" is in Vocabularies and Confirmed
let private confirmedTttEntry =
    { v1Empty with
        Uri = "https://example.org/tictactoe#"
        Validated =
            { IsValidated = true
              Reason = None
              LastChecked = None }
        FetchedAt = fixedNow.AddDays(-5.0)
        Hash = "sha256:def" }

let private at2Lock: LockFile =
    { at1Lock with
        Vocabularies =
          Map.ofList
              [ "schema", confirmedSchemaEntry
                "ttt", confirmedTttEntry ] }

// AT4 lock: only schema (Confirmed), no ttt
let private schemaOnlyJson =
    """{ "schemaVersion": 1, "resolved": [
         { "fsharpType": "App.Order", "iri": "schema:Order", "shape": "record",
           "fields": [ { "name": "Total", "iri": "schema:price" } ] } ] }"""

let private at4Lock: LockFile =
    { SchemaVersion = 2
      Generated = fixedNow
      Integrity = None
      Vocabularies = Map.ofList [ "schema", confirmedSchemaEntry ]
      DeclaredPrefixes = Map.ofList [ "schema", "https://schema.org/" ]
      Mappings =
        [ { FSharpType = "App.Order"
            Iri = None
            Confidence = 0.0
            Source = Convention
            Status = Unresolved
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "Total"
                      Iri = None
                      Confidence = 0.0
                      Source = Convention
                      Status = Unresolved } ] } ] }

// ── AT5 non-warn state fixtures ───────────────────────────────────────────────

// LocallyServedUnconfirmed: Owned=true, Validated=false, not stale
let private localEntry =
    { v1Empty with
        Uri = "https://local.example.org/"
        Owned = true
        Validated = { IsValidated = false; Reason = None; LastChecked = None }
        FetchedAt = fixedNow.AddDays(-1.0)
        Hash = "sha256:local" }

// Proposed: Owned=false, Validated=false, not stale
let private proposedEntry =
    { v1Empty with
        Uri = "https://prop.example.org/"
        Owned = false
        Validated = { IsValidated = false; Reason = None; LastChecked = None }
        FetchedAt = fixedNow.AddDays(-1.0)
        Hash = "sha256:prop" }

// Stale: FetchedAt 60 days ago — exceeds 30-day default SLA
let private staleEntry =
    { v1Empty with
        Uri = "https://stale.example.org/"
        Owned = false
        Validated = { IsValidated = false; Reason = None; LastChecked = None }
        FetchedAt = fixedNow.AddDays(-60.0)
        Hash = "sha256:stale" }

let private singlePrefixLock (prefix: string) (iri: string) (entry: VocabularyEntry) : LockFile =
    { SchemaVersion = 2
      Generated = fixedNow
      Integrity = None
      Vocabularies = Map.ofList [ prefix, entry ]
      DeclaredPrefixes = Map.ofList [ prefix, iri ]
      Mappings =
        [ { FSharpType = $"App.Thing"
            Iri = None
            Confidence = 0.0
            Source = Convention
            Status = Unresolved
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "X"
                      Iri = None
                      Confidence = 0.0
                      Source = Convention
                      Status = Unresolved } ] } ] }

let private singlePrefixJson (prefix: string) =
    $"""{{ "schemaVersion": 1, "resolved": [
         {{ "fsharpType": "App.Thing", "iri": "{prefix}:Thing", "shape": "record",
           "fields": [ {{ "name": "X", "iri": "{prefix}:x" }} ] }} ] }}"""

// ── AT6: aliasing ─────────────────────────────────────────────────────────────

// "sdo" maps to "https://schema.org/" — same IRI stored under key "schema" in Vocabs
// "unpub" maps to "https://example.org/private#" — NOT in Vocabs
let private at6Lock: LockFile =
    { SchemaVersion = 2
      Generated = fixedNow
      Integrity = None
      Vocabularies = Map.ofList [ "schema", confirmedSchemaEntry ]
      DeclaredPrefixes =
        Map.ofList
            [ "sdo", "https://schema.org/"
              "unpub", "https://example.org/private#" ]
      Mappings =
        [ { FSharpType = "App.Aliased"
            Iri = None
            Confidence = 0.0
            Source = Convention
            Status = Unresolved
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "X"
                      Iri = None
                      Confidence = 0.0
                      Source = Convention
                      Status = Unresolved } ] } ] }

let private at6Json =
    """{ "schemaVersion": 1, "resolved": [
         { "fsharpType": "App.Aliased", "iri": "sdo:Thing", "shape": "record",
           "fields": [ { "name": "X", "iri": "unpub:something" } ] } ] }"""

// ── AT6b: reverse (URI mismatch) ─────────────────────────────────────────────

// "ttt" key IS in Vocabularies, but entry.Uri ≠ DeclaredPrefixes["ttt"]
let private mismatchedEntry =
    { v1Empty with
        Uri = "https://DIFFERENT.org/#"
        Validated = { IsValidated = true; Reason = None; LastChecked = None }
        FetchedAt = fixedNow.AddDays(-1.0)
        Hash = "sha256:mismatch" }

let private at6bLock: LockFile =
    { SchemaVersion = 2
      Generated = fixedNow
      Integrity = None
      Vocabularies = Map.ofList [ "ttt", mismatchedEntry ]
      DeclaredPrefixes = Map.ofList [ "ttt", "https://example.org/tictactoe#" ]
      Mappings = [ moveRequestMapping ] }

// Use only "ttt" prefix (resolves via DeclaredPrefixes → can be merged)
let private at6bJson =
    """{ "schemaVersion": 1, "resolved": [
         { "fsharpType": "App.MoveRequest", "iri": "ttt:MoveRequest", "shape": "record",
           "fields": [ { "name": "Position", "iri": "ttt:square" } ] } ] }"""

// ── AT7: five-state lock ──────────────────────────────────────────────────────

let private fiveStateLock: LockFile =
    { SchemaVersion = 2
      Generated = fixedNow
      Integrity = None
      Vocabularies =
        Map.ofList
            [ "conf",
              { v1Empty with
                  Uri = "https://conf.example.org/"
                  Validated = { IsValidated = true; Reason = None; LastChecked = None }
                  FetchedAt = fixedNow.AddDays(-1.0)
                  Hash = "sha256:conf" }
              "prop",
              { v1Empty with
                  Uri = "https://prop.example.org/"
                  Owned = false
                  Validated = { IsValidated = false; Reason = None; LastChecked = None }
                  FetchedAt = fixedNow.AddDays(-1.0)
                  Hash = "sha256:prop" }
              "local",
              { v1Empty with
                  Uri = "https://local.example.org/"
                  Owned = true
                  Validated = { IsValidated = false; Reason = None; LastChecked = None }
                  FetchedAt = fixedNow.AddDays(-1.0)
                  Hash = "sha256:local" }
              "stale",
              { v1Empty with
                  Uri = "https://stale.example.org/"
                  Owned = false
                  Validated = { IsValidated = false; Reason = None; LastChecked = None }
                  FetchedAt = fixedNow.AddDays(-60.0)
                  Hash = "sha256:stale" } ]
      DeclaredPrefixes =
        Map.ofList
            [ "conf", "https://conf.example.org/"
              "prop", "https://prop.example.org/"
              "under", "https://under.example.org/"
              "local", "https://local.example.org/"
              "stale", "https://stale.example.org/" ]
      Mappings =
        [ { FSharpType = "App.Conf"
            Iri = None
            Confidence = 0.0
            Source = Convention
            Status = Unresolved
            Alternates = []
            Rt = None
            Shape = MappingShape.Record [ { Name = "X"; Iri = None; Confidence = 0.0; Source = Convention; Status = Unresolved } ] }
          { FSharpType = "App.Prop"
            Iri = None
            Confidence = 0.0
            Source = Convention
            Status = Unresolved
            Alternates = []
            Rt = None
            Shape = MappingShape.Record [ { Name = "X"; Iri = None; Confidence = 0.0; Source = Convention; Status = Unresolved } ] }
          { FSharpType = "App.Under"
            Iri = None
            Confidence = 0.0
            Source = Convention
            Status = Unresolved
            Alternates = []
            Rt = None
            Shape = MappingShape.Record [ { Name = "X"; Iri = None; Confidence = 0.0; Source = Convention; Status = Unresolved } ] }
          { FSharpType = "App.Local"
            Iri = None
            Confidence = 0.0
            Source = Convention
            Status = Unresolved
            Alternates = []
            Rt = None
            Shape = MappingShape.Record [ { Name = "X"; Iri = None; Confidence = 0.0; Source = Convention; Status = Unresolved } ] }
          { FSharpType = "App.Stale"
            Iri = None
            Confidence = 0.0
            Source = Convention
            Status = Unresolved
            Alternates = []
            Rt = None
            Shape = MappingShape.Record [ { Name = "X"; Iri = None; Confidence = 0.0; Source = Convention; Status = Unresolved } ] } ] }

let private fiveStateJson =
    """{ "schemaVersion": 1, "resolved": [
         { "fsharpType": "App.Conf",  "iri": "conf:Thing",  "shape": "record", "fields": [ { "name": "X", "iri": "conf:x" } ] },
         { "fsharpType": "App.Prop",  "iri": "prop:Thing",  "shape": "record", "fields": [ { "name": "X", "iri": "prop:x" } ] },
         { "fsharpType": "App.Under", "iri": "under:Thing", "shape": "record", "fields": [ { "name": "X", "iri": "under:x" } ] },
         { "fsharpType": "App.Local", "iri": "local:Thing", "shape": "record", "fields": [ { "name": "X", "iri": "local:x" } ] },
         { "fsharpType": "App.Stale", "iri": "stale:Thing", "shape": "record", "fields": [ { "name": "X", "iri": "stale:x" } ] } ] }"""

// ── item1-regression fixture ─────────────────────────────────────────────────

// "voc" is in Vocabularies (keyed by "voc") but NOT in DeclaredPrefixes.
// buildPrefixMap includes it via Vocabularies so the CURIE "voc:Thing" resolves and survives partitioning.
// lookupEntry keys on DeclaredPrefixes first → None → Undereferenceable; the None → None guard fires.
let private vocOnlyInVocabsEntry =
    { v1Empty with
        Uri = "https://vocab.example.org/"
        FetchedAt = fixedNow.AddDays(-1.0)
        Hash = "sha256:voconly" }

let private at1RegressionMapping: Mapping =
    { FSharpType = "App.Thing"
      Iri = None
      Confidence = 0.0
      Source = Convention
      Status = Unresolved
      Alternates = []
      Rt = None
      Shape =
        MappingShape.Record
            [ { Name = "X"
                Iri = None
                Confidence = 0.0
                Source = Convention
                Status = Unresolved } ] }

let private at1RegressionLock: LockFile =
    { SchemaVersion = 2
      Generated = fixedNow
      Integrity = None
      Vocabularies = Map.ofList [ "voc", vocOnlyInVocabsEntry ]
      DeclaredPrefixes = Map.empty
      Mappings = [ at1RegressionMapping ] }

let private at1RegressionJson =
    """{ "schemaVersion": 1, "resolved": [
         { "fsharpType": "App.Thing", "iri": "voc:Thing", "shape": "record",
           "fields": [ { "name": "X", "iri": "voc:x" } ] } ] }"""

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let vocabWarningTests =
    testList
        "VocabWarning AT1–AT8"
        [ // AT1: accept, Undereferenceable, names IRI + field
          test "AT1: accept warns on Undereferenceable ttt — names prefix, IRI, type, field" {
              let doc = Expect.wantOk (Accept.parseResolved moveRequestJson) "parse"
              let _, summary = Accept.apply at1Lock doc Manual emptyOracle

              Expect.isNonEmpty summary.Warnings "AT1: must have at least one warning"

              let w =
                  summary.Warnings
                  |> List.tryFind (fun w -> w.Prefix = "ttt")
                  |> Option.defaultWith (fun () -> failwith "AT1: no warning for ttt prefix")

              Expect.equal w.Prefix "ttt" "AT1: prefix is ttt"
              Expect.equal w.Iri "https://example.org/tictactoe#" "AT1: IRI is exact namespace IRI"
              Expect.equal (w.Location |> Option.map (fun l -> l.Type)) (Some "MoveRequest") "AT1: type is MoveRequest (simple name)"
              Expect.equal (w.Location |> Option.bind (fun l -> l.Field)) (Some "Position") "AT1: field is Position"
              Expect.equal w.State VocabState.Undereferenceable "AT1: state is typed VocabState"
              Expect.isFalse (w.Iri = w.Prefix) "AT1: IRI must differ from bare prefix"
              Expect.isNonEmpty w.Hint "AT1: hint contains a concrete host-it step"
          }

          // AT2: accept, Confirmed, silent
          test "AT2: accept is silent when ttt is Confirmed" {
              let doc = Expect.wantOk (Accept.parseResolved moveRequestJson) "parse"
              let _, summary = Accept.apply at2Lock doc Manual emptyOracle
              let tttWarnings = summary.Warnings |> List.filter (fun w -> w.Prefix = "ttt")
              Expect.isEmpty tttWarnings "AT2: no warning when ttt is Confirmed"
          }

          // AT3: status, dedicated Warnings section (stdout)
          test "AT3: status format has dedicated Warnings section for Undereferenceable ttt" {
              let output = Status.format fixedNow None at1Lock
              Expect.stringContains output "Warnings:" "AT3: Warnings: section header present"
              Expect.stringContains output "https://example.org/tictactoe#" "AT3: Warnings section includes resolved IRI"
              Expect.stringContains output "publish" "AT3: Warnings section includes host-it hint"
              // Inline vocab table retained
              Expect.stringContains output "Vocabularies:" "AT3: inline Vocabularies table retained"
              // Both appear
              Expect.isTrue
                  (output.IndexOf("Vocabularies:") < output.IndexOf("Warnings:"))
                  "AT3: Vocabularies table appears before Warnings section"
          }

          // AT4: accept, only fetched schema, silent
          test "AT4: accept with only fetched schema vocabulary — no warning" {
              let doc = Expect.wantOk (Accept.parseResolved schemaOnlyJson) "parse"
              let _, summary = Accept.apply at4Lock doc Manual emptyOracle
              Expect.isEmpty summary.Warnings "AT4: no warning when only fetched schema vocab is used"
          }

          // AT5a: LocallyServedUnconfirmed stays silent
          test "AT5a: LocallyServedUnconfirmed does NOT trigger host-it warning (accept)" {
              let lock = singlePrefixLock "ns" localEntry.Uri localEntry
              let json = singlePrefixJson "ns"
              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Manual emptyOracle
              let nsWarnings = summary.Warnings |> List.filter (fun w -> w.Prefix = "ns")
              Expect.isEmpty nsWarnings "AT5a: LocallyServedUnconfirmed must not warn"
          }

          test "AT5a: LocallyServedUnconfirmed does NOT appear in status Warnings section" {
              let lock = singlePrefixLock "ns" localEntry.Uri localEntry
              let output = Status.format fixedNow None lock
              Expect.isFalse (output.Contains "Warnings:") "AT5a: no Warnings section for LocallyServedUnconfirmed"
          }

          // AT5b: Proposed stays silent
          test "AT5b: Proposed does NOT trigger host-it warning (accept)" {
              let lock = singlePrefixLock "ns" proposedEntry.Uri proposedEntry
              let json = singlePrefixJson "ns"
              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Manual emptyOracle
              let nsWarnings = summary.Warnings |> List.filter (fun w -> w.Prefix = "ns")
              Expect.isEmpty nsWarnings "AT5b: Proposed must not warn"
          }

          test "AT5b: Proposed does NOT appear in status Warnings section" {
              let lock = singlePrefixLock "ns" proposedEntry.Uri proposedEntry
              let output = Status.format fixedNow None lock
              Expect.isFalse (output.Contains "Warnings:") "AT5b: no Warnings section for Proposed"
          }

          // AT5c: Stale stays silent in #377's warning surface
          test "AT5c: Stale does NOT trigger host-it warning (accept)" {
              let lock = singlePrefixLock "ns" staleEntry.Uri staleEntry
              let json = singlePrefixJson "ns"
              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply lock doc Manual emptyOracle
              let nsWarnings = summary.Warnings |> List.filter (fun w -> w.Prefix = "ns")
              Expect.isEmpty nsWarnings "AT5c: Stale must not warn in #377 warning surface"
          }

          test "AT5c: Stale does NOT appear in status Warnings section" {
              let lock = singlePrefixLock "ns" staleEntry.Uri staleEntry
              let output = Status.format fixedNow None lock
              Expect.isFalse (output.Contains "Warnings:") "AT5c: no Warnings section for Stale"
          }

          // AT6: convergence, over-flag direction (IRI-identity beats prefix key)
          test "AT6: aliased prefix (sdo → confirmed schema IRI) is NOT warned; unpub IS warned (vacuity guard)" {
              let doc = Expect.wantOk (Accept.parseResolved at6Json) "parse"
              let _, summary = Accept.apply at6Lock doc Manual emptyOracle

              let sdoWarnings = summary.Warnings |> List.filter (fun w -> w.Prefix = "sdo")
              Expect.isEmpty sdoWarnings "AT6: sdo (aliased to confirmed schema IRI) must NOT warn"

              let unpubWarnings = summary.Warnings |> List.filter (fun w -> w.Prefix = "unpub")
              Expect.isNonEmpty unpubWarnings "AT6: unpub (genuinely unpublished) MUST warn"
          }

          // AT6b: reverse direction — kills the prefilter escape
          test "AT6b: prefix key in Vocabularies but entry.Uri ≠ DeclaredPrefixes value → MUST warn" {
              let doc = Expect.wantOk (Accept.parseResolved at6bJson) "parse"
              let _, summary = Accept.apply at6bLock doc Manual emptyOracle

              let tttWarnings = summary.Warnings |> List.filter (fun w -> w.Prefix = "ttt")

              Expect.isNonEmpty
                  tttWarnings
                  "AT6b: classifier must flag ttt as Undereferenceable when entry.Uri ≠ declared IRI"
          }

          // AT7: cross-surface agreement
          test "AT7: accept warn-set == status Warnings-section set — only Undereferenceable (under)" {
              let doc = Expect.wantOk (Accept.parseResolved fiveStateJson) "parse"
              let _, summary = Accept.apply fiveStateLock doc Manual emptyOracle

              let acceptWarnPrefixes =
                  summary.Warnings |> List.map (fun w -> w.Prefix) |> Set.ofList

              Expect.equal
                  acceptWarnPrefixes
                  (Set.ofList [ "under" ])
                  "AT7: accept warns only on Undereferenceable prefix"

              let statusOutput = Status.format fixedNow None fiveStateLock

              Expect.isTrue (statusOutput.Contains "Warnings:") "AT7: status has Warnings section"

              let warningsIdx = statusOutput.IndexOf("Warnings:")
              let warningsText = statusOutput.[warningsIdx..]

              Expect.isTrue (warningsText.Contains "under") "AT7: Warnings section contains under"
              Expect.isFalse (warningsText.Contains "conf") "AT7: Warnings section does NOT contain conf"
              Expect.isFalse (warningsText.Contains "prop") "AT7: Warnings section does NOT contain prop"
              Expect.isFalse (warningsText.Contains "local") "AT7: Warnings section does NOT contain local"
              Expect.isFalse (warningsText.Contains "stale") "AT7: Warnings section does NOT contain stale"
          }

          // AT8: warned IRI is the dereference target (exact string match)
          test "AT8: warned IRI equals the classifier-resolved namespace IRI (exact string, not prefix)" {
              let doc = Expect.wantOk (Accept.parseResolved moveRequestJson) "parse"
              let _, summary = Accept.apply at1Lock doc Manual emptyOracle

              let w =
                  summary.Warnings
                  |> List.tryFind (fun w -> w.Prefix = "ttt")
                  |> Option.defaultWith (fun () -> failwith "AT8: no warning for ttt")

              let classifierIri = Map.find "ttt" at1Lock.DeclaredPrefixes
              Expect.equal w.Iri classifierIri "AT8: warned IRI == classifier-resolved namespace IRI"
              Expect.equal w.Iri "https://example.org/tictactoe#" "AT8: exact IRI string match"
              Expect.notEqual w.Iri w.Prefix "AT8: IRI is not just the prefix label"
          }

          // item2-RED: field expressed as full absolute IRI still warns when namespace is Undereferenceable
          test "item2-RED: accept warns when field IRI is full absolute IRI under Undereferenceable namespace" {
              let json =
                  """{ "schemaVersion": 1, "resolved": [
                       { "fsharpType": "App.MoveRequest", "iri": "schema:MoveAction", "shape": "record",
                         "fields": [ { "name": "Position", "iri": "https://example.org/tictactoe#square" } ] } ] }"""

              let doc = Expect.wantOk (Accept.parseResolved json) "parse"
              let _, summary = Accept.apply at1Lock doc Manual emptyOracle
              let tttWarnings = summary.Warnings |> List.filter (fun w -> w.Prefix = "ttt")
              Expect.isNonEmpty tttWarnings "item2: accept must warn when absolute IRI references Undereferenceable namespace"
          }

          // item3-RED: field under tictactoe-extra namespace must not be attributed to tictactoe namespace
          test "item3-RED: tictactoe-extra IRI is not mis-attributed to tictactoe namespace (boundary guard)" {
              let lock: LockFile =
                  { SchemaVersion = 2
                    Generated = fixedNow
                    Integrity = None
                    Vocabularies = Map.empty
                    DeclaredPrefixes = Map.ofList [ "ttt", "https://example.org/tictactoe" ] // no trailing #
                    Mappings =
                      [ { FSharpType = "App.MoveRequest"
                          Iri = None
                          Confidence = 0.0
                          Source = Convention
                          Status = Unresolved
                          Alternates = []
                          Rt = None
                          Shape =
                            MappingShape.Record
                                [ { Name = "Position"
                                    Iri = Some "https://example.org/tictactoe-extra#square"
                                    Confidence = 0.0
                                    Source = Convention
                                    Status = Unresolved } ] } ] }

              let warnings = Status.getWarnings fixedNow None lock
              let tttW = warnings |> List.find (fun w -> w.Prefix = "ttt")
              // Position's IRI is under tictactoe-EXTRA, not tictactoe → must NOT attribute to it
              let tttField = tttW.Location |> Option.bind (fun l -> l.Field)
              Expect.notEqual tttField (Some "Position") "item3: tictactoe-extra IRI must not attribute to ttt"
          }

          // item4-RED: hint must not include trailing # in the deref target for hash namespaces
          test "item4-RED: hint strips trailing # from hash-namespace deref target" {
              let doc = Expect.wantOk (Accept.parseResolved moveRequestJson) "parse"
              let _, summary = Accept.apply at1Lock doc Manual emptyOracle

              let tttW =
                  summary.Warnings
                  |> List.tryFind (fun w -> w.Prefix = "ttt")
                  |> Option.defaultWith (fun () -> failwith "item4: no ttt warning")

              Expect.isFalse
                  (tttW.Hint.Contains("tictactoe# as"))
                  "item4: hint must not carry # before 'as dereferenceable'" }

          // item1-regression: CURIE prefix in Vocabularies-only reaches the guard and never emits bare-prefix iri
          test "item1-regression: CURIE prefix in Vocabularies-only reaches the guard and never emits a bare-prefix iri" {
              let doc = Expect.wantOk (Accept.parseResolved at1RegressionJson) "parse"
              let _, summary = Accept.apply at1RegressionLock doc Manual emptyOracle
              // Guard path: buildPrefixMap includes "voc" via Vocabularies → CURIE resolves → survives
              // partitioning; lookupEntry checks DeclaredPrefixes → None → Undereferenceable → guard fires.
              // Old Option.defaultValue prefix emitted { Iri = "voc" } — vocWarnings was non-empty (RED).
              let vocWarnings = summary.Warnings |> List.filter (fun w -> w.Prefix = "voc")
              Expect.isEmpty vocWarnings "item1-regression: Vocabularies-only prefix must not produce any warning"
              let bareIriWarnings = summary.Warnings |> List.filter (fun w -> w.Iri = w.Prefix)
              Expect.isEmpty bareIriWarnings "item1-regression: no warning may carry a bare prefix label as its IRI" } ]
