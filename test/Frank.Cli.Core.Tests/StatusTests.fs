module Frank.Cli.Core.Tests.StatusTests

open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
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

              let output = Status.format lf
              Expect.stringContains output "Confirmed:  3" "confirmed count"
              Expect.stringContains output "Proposed:   2" "proposed count"
              Expect.stringContains output "Unresolved: 1" "unresolved count"
              Expect.stringContains output "Excluded:   2" "excluded count"
          }

          test "empty lock produces all-zero counts" {
              let lf = lockWith []
              let output = Status.format lf
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

              let output = Status.formatByPackage lf
              Expect.stringContains output "MyApp.Orders" "orders block"
              Expect.stringContains output "MyApp.Catalog" "catalog block"
          }

          test "per-namespace confirmed count is correct" {
              let lf =
                  lockWith
                      [ mappingWith "MyApp.Orders.Order" None Confirmed
                        mappingWith "MyApp.Orders.LineItem" None Confirmed
                        mappingWith "MyApp.Catalog.Product" None Proposed ]

              let output = Status.formatByPackage lf
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

              let output = Status.formatByPackage lf
              Expect.stringContains output "schema (2)" "two schema terms"
          }

          test "AC3 plain format unchanged" {
              let lf = lockWith [ mapping "A" Confirmed; mapping "B" Proposed ]

              let output = Status.format lf

              Expect.equal
                  output
                  "Confirmed:  1\nProposed:   1\nUnresolved: 0\nExcluded:   0"
                  "byte-identical to prior format"
          }

          test "namespace (global) shown for unqualified types" {
              let lf = lockWith [ mappingWith "Game" None Proposed ]
              let output = Status.formatByPackage lf
              Expect.stringContains output "(global)" "global namespace shown"
          }

          test "no vocab line when no IRIs present" {
              let lf = lockWith [ mappingWith "MyApp.Orders.Foo" None Unresolved ]
              let output = Status.formatByPackage lf
              Expect.stringContains output "MyApp.Orders" "namespace shown"
              Expect.isFalse (output.Contains "vocabs:") "no vocabs line when none"
          } ]
