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
      Fields = [] }

let private lockWith (mappings: Mapping list) : LockFile.LockFile =
    { SchemaVersion = 1
      Generated = System.DateTimeOffset.UtcNow
      Vocabularies = Map.empty
      Mappings = mappings }

// ── AT5: status format ────────────────────────────────────────────────────────

[<Tests>]
let at5StatusTests =
    testList
        "AT5 - Status.format"
        [ test "3 confirmed + 2 proposed + 1 unresolved produces correct counts" {
              let lf =
                  lockWith
                      [ mapping "A" Confirmed
                        mapping "B" Confirmed
                        mapping "C" Confirmed
                        mapping "D" Proposed
                        mapping "E" Proposed
                        mapping "F" Unresolved ]

              let output = Status.format lf
              Expect.stringContains output "Confirmed:  3" "confirmed count"
              Expect.stringContains output "Proposed:   2" "proposed count"
              Expect.stringContains output "Unresolved: 1" "unresolved count"
          }

          test "empty lock produces all-zero counts" {
              let lf = lockWith []
              let output = Status.format lf
              Expect.stringContains output "Confirmed:  0" "confirmed zero"
              Expect.stringContains output "Proposed:   0" "proposed zero"
              Expect.stringContains output "Unresolved: 0" "unresolved zero"
          } ]
