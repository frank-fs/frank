module Frank.Cli.Core.Tests.StatusTests

open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabClassifier
open Frank.Cli.Core

// ── Helpers ───────────────────────────────────────────────────────────────────

let private mapping fsType status : Mapping =
    { FSharpType = fsType
      Iri = None
      Confidence = 0.5
      Source = Convention
      Status = status
      Alternates = []
      Rt = None
      Shape = MappingShape.Record [] }

let private lockWith (mappings: Mapping list) : LockFile.LockFile =
    { SchemaVersion = 1
      Generated = System.DateTimeOffset.UtcNow
      Integrity = None
      Vocabularies = Map.empty
      DeclaredPrefixes = Map.empty
      Mappings = mappings }

// ── AT5: status format ────────────────────────────────────────────────────────

[<Tests>]
let at5StatusTests =
    testList
        "AT5 - Status.format"
        [ test "3 confirmed + 2 proposed + 1 unresolved + 2 excluded produces correct counts" {
              let lf =
                  lockWith
                      [ mapping "A" Confirmed
                        mapping "B" Confirmed
                        mapping "C" Confirmed
                        mapping "D" Proposed
                        mapping "E" Proposed
                        mapping "F" Unresolved
                        mapping "G" Excluded
                        mapping "H" Excluded ]

              let output = Status.format System.DateTimeOffset.UtcNow None lf
              Expect.stringContains output "Confirmed:  3" "confirmed count"
              Expect.stringContains output "Proposed:   2" "proposed count"
              Expect.stringContains output "Unresolved: 1" "unresolved count"
              Expect.stringContains output "Excluded:   2" "excluded count"
          }

          test "empty lock produces all-zero counts" {
              let lf = lockWith []
              let output = Status.format System.DateTimeOffset.UtcNow None lf
              Expect.stringContains output "Confirmed:  0" "confirmed zero"
              Expect.stringContains output "Proposed:   0" "proposed zero"
              Expect.stringContains output "Unresolved: 0" "unresolved zero"
              Expect.stringContains output "Excluded:   0" "excluded zero"
          } ]

// ── AT6: formatByPackage ──────────────────────────────────────────────────────

let private mappingWith fsType iri status : Mapping =
    { FSharpType = fsType
      Iri = iri
      Confidence = 1.0
      Source = Convention
      Status = status
      Alternates = []
      Rt = None
      Shape = MappingShape.Record [] }

[<Tests>]
let formatByPackageTests =
    testList
        "AT6 - Status.formatByPackage"
        [ test "two namespaces produce separate blocks" {
              let lf =
                  lockWith
                      [ mappingWith "MyApp.Orders.Order" (Some "schema:Order") Confirmed
                        mappingWith "MyApp.Catalog.Product" (Some "schema:Product") Proposed ]

              let output = Status.formatByPackage System.DateTimeOffset.UtcNow None lf
              Expect.stringContains output "MyApp.Orders" "orders block"
              Expect.stringContains output "MyApp.Catalog" "catalog block"
          }

          test "per-namespace confirmed count is correct" {
              let lf =
                  lockWith
                      [ mappingWith "MyApp.Orders.Order" None Confirmed
                        mappingWith "MyApp.Orders.LineItem" None Confirmed
                        mappingWith "MyApp.Catalog.Product" None Proposed ]

              let output = Status.formatByPackage System.DateTimeOffset.UtcNow None lf
              Expect.stringContains output "MyApp.Orders" "orders block"
              Expect.stringContains output "MyApp.Catalog" "catalog block"
              Expect.stringContains output "Confirmed:  2" "two confirmed in orders"
              Expect.stringContains output "Proposed:   1" "one proposed in catalog"
          }

          test "vocab usage shown for namespace" {
              let lf =
                  { lockWith
                        [ mappingWith "MyApp.Orders.Game" (Some "schema:Game") Confirmed
                          mappingWith "MyApp.Orders.Result" (Some "schema:result") Confirmed ] with
                      DeclaredPrefixes = Map.ofList [ "schema", "https://schema.org/" ] }

              let output = Status.formatByPackage System.DateTimeOffset.UtcNow None lf
              Expect.stringContains output "schema (2)" "two schema terms"
          }

          test "AC3 plain format unchanged" {
              let lf = lockWith [ mapping "A" Confirmed; mapping "B" Proposed ]

              let output = Status.format System.DateTimeOffset.UtcNow None lf

              Expect.equal
                  output
                  "Confirmed:  1\nProposed:   1\nUnresolved: 0\nExcluded:   0"
                  "byte-identical to prior format"
          }

          test "namespace (global) shown for unqualified types" {
              let lf = lockWith [ mappingWith "Game" None Proposed ]
              let output = Status.formatByPackage System.DateTimeOffset.UtcNow None lf
              Expect.stringContains output "(global)" "global namespace shown"
          }

          test "no vocab line when no IRIs present" {
              let lf = lockWith [ mappingWith "MyApp.Orders.Foo" None Unresolved ]
              let output = Status.formatByPackage System.DateTimeOffset.UtcNow None lf
              Expect.stringContains output "MyApp.Orders" "namespace shown"
              Expect.isFalse (output.Contains "vocabs:") "no vocabs line when none"
          } ]

// ── A-C10: Status surface agrees with classifier ──────────────────────────────

let private fixedNow = System.DateTimeOffset(2026, 7, 9, 12, 0, 0, System.TimeSpan.Zero)

let private lockWithVocabs (vocabs: Map<string, VocabularyEntry>) (prefixes: Map<string, string>) : LockFile =
    { SchemaVersion = 2
      Generated = fixedNow
      Integrity = None
      Vocabularies = vocabs
      DeclaredPrefixes = prefixes
      Mappings = [] }

[<Tests>]
let ac10StatusSurfaceTests =
    testList
        "A-C10: Status surface agrees with classifier"
        [ test "confirmed vocab: status format shows Confirmed" {
              let confirmedEntry =
                  { v1Empty with
                      Uri = "https://schema.org/"
                      FetchedAt = fixedNow.AddDays(-5.0)
                      Hash = "sha256:abc"
                      Validated =
                          { IsValidated = true
                            Reason = None
                            LastChecked = Some fixedNow } }

              let lf =
                  lockWithVocabs
                      (Map.ofList [ "schema", confirmedEntry ])
                      (Map.ofList [ "schema", "https://schema.org/" ])

              let output = Status.format fixedNow None lf
              Expect.stringContains output "  schema: Confirmed" "vocab section shows schema: Confirmed"
          }

          test "A-C10: classifier state equals status surface state for Confirmed prefix" {
              let confirmedEntry =
                  { v1Empty with
                      Uri = "https://schema.org/"
                      FetchedAt = fixedNow.AddDays(-5.0)
                      Hash = "sha256:abc"
                      Validated =
                          { IsValidated = true
                            Reason = None
                            LastChecked = Some fixedNow } }

              let lf =
                  lockWithVocabs
                      (Map.ofList [ "schema", confirmedEntry ])
                      (Map.ofList [ "schema", "https://schema.org/" ])

              let states = classifyReferencedVocab lf fixedNow None [ "schema" ]
              Expect.equal (List.head states) VocabState.Confirmed "classifier: schema is Confirmed"

              let output = Status.format fixedNow None lf
              Expect.stringContains output "  schema: Confirmed" "status surface agrees with classifier: schema: Confirmed"
          }

          test "undereferenceable vocab: status format shows Undereferenceable" {
              let lf =
                  lockWithVocabs Map.empty (Map.ofList [ "ex", "https://example.org/" ])

              let output = Status.format fixedNow None lf
              Expect.stringContains output "ex" "status output mentions ex"
              Expect.stringContains output "Undereferenceable" "status output shows Undereferenceable"
          } ]

// ── #419 AC4: status distinguishes declared-only owned vs. genuinely-external prefixes ──

[<Tests>]
let issue419StatusTests =
    testList
        "#419 AC4: frank semantic status distinguishes owned-unfetched from external-unfetched"
        [ test "declared-only, never-fetched, app-owned prefix reports LocallyServedUnconfirmed" {
              let ownedLock =
                  lockWithVocabs Map.empty (Map.ofList [ "ttt", "https://example.org/tictactoe#" ])

              let output = Status.format fixedNow (Some "https://example.org") ownedLock

              Expect.stringContains output "  ttt: LocallyServedUnconfirmed" "owned-unfetched prefix reports LocallyServedUnconfirmed"
          }

          test "declared-only, never-fetched, genuinely external prefix (Wikidata) reports Undereferenceable" {
              let externalLock =
                  lockWithVocabs Map.empty (Map.ofList [ "wd", "https://www.wikidata.org/entity/" ])

              let output = Status.format fixedNow (Some "https://example.org") externalLock

              Expect.stringContains output "  wd: Undereferenceable" "external-unfetched prefix reports Undereferenceable"
          }

          test "owned-unfetched and external-unfetched fixtures produce different message text" {
              let ownedLock =
                  lockWithVocabs Map.empty (Map.ofList [ "ttt", "https://example.org/tictactoe#" ])

              let externalLock =
                  lockWithVocabs Map.empty (Map.ofList [ "wd", "https://www.wikidata.org/entity/" ])

              let ownedOutput = Status.format fixedNow (Some "https://example.org") ownedLock
              let externalOutput = Status.format fixedNow (Some "https://example.org") externalLock

              Expect.isFalse (ownedOutput.Contains "Undereferenceable") "owned-unfetched must not say Undereferenceable"
              Expect.isFalse (externalOutput.Contains "LocallyServedUnconfirmed") "external-unfetched must not say LocallyServedUnconfirmed"

              // Undereferenceable additionally gets a Warnings: host-it hint; LocallyServedUnconfirmed does not.
              Expect.isTrue (externalOutput.Contains "Warnings:") "external-unfetched gets a Warnings section"
              Expect.isFalse (ownedOutput.Contains "Warnings:") "owned-unfetched does not get a Warnings section"
          } ]
