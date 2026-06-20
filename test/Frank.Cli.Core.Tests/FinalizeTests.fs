module Frank.Cli.Core.Tests.FinalizeTests

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
      Shape = MappingShape.Record [] }

let private mappingWithFields fsType status (fields: FieldMapping list) : Mapping =
    { FSharpType = fsType
      Iri = None
      Confidence = 0.5
      Source = Convention
      Status = status
      Alternates = []
      Shape = MappingShape.Record fields }

let private field name status : FieldMapping =
    { Name = name
      Iri = None
      Confidence = 0.5
      Source = Convention
      Status = status }

let private lockWith (mappings: Mapping list) : LockFile.LockFile =
    { SchemaVersion = 1
      Generated = System.DateTimeOffset.UtcNow
      Vocabularies = Map.empty
      Mappings = mappings }

// ── Finalize tests ────────────────────────────────────────────────────────────

[<Tests>]
let finalizeTests =
    testList
        "Finalize.run"
        [ test "all-decided: no Proposed or Unresolved remain after finalize" {
              let lf =
                  lockWith [ mapping "A" Confirmed; mapping "B" Proposed; mapping "C" Unresolved ]

              let result, _ = Finalize.run lf

              for m in result.Mappings do
                  Expect.isFalse (m.Status = Proposed) $"expected no Proposed, got {m.FSharpType}"
                  Expect.isFalse (m.Status = Unresolved) $"expected no Unresolved, got {m.FSharpType}"

              let a = result.Mappings |> List.find (fun m -> m.FSharpType = "A")
              let b = result.Mappings |> List.find (fun m -> m.FSharpType = "B")
              let c = result.Mappings |> List.find (fun m -> m.FSharpType = "C")
              Expect.equal a.Status Confirmed "Confirmed mapping stays Confirmed"
              Expect.equal b.Status Excluded "Proposed becomes Excluded"

              Expect.equal
                  b.Source
                  Convention
                  "Proposed source preserved (finalize is zero-judgment; behavior-correction from Source=Manual)"

              Expect.equal c.Status Excluded "Unresolved becomes Excluded"

              Expect.equal
                  c.Source
                  Convention
                  "Unresolved source preserved (finalize is zero-judgment; behavior-correction from Source=Manual)"
          }

          test "field decided: Proposed and Unresolved fields on a Confirmed mapping become Excluded" {
              let fields = [ field "x" Confirmed; field "y" Proposed; field "z" Unresolved ]

              let lf = lockWith [ mappingWithFields "T" Confirmed fields ]
              let result, _ = Finalize.run lf
              let m = result.Mappings |> List.head
              let fs = MappingShape.payloadFields m.Shape
              let x = fs |> List.find (fun f -> f.Name = "x")
              let y = fs |> List.find (fun f -> f.Name = "y")
              let z = fs |> List.find (fun f -> f.Name = "z")
              Expect.equal x.Status Confirmed "Confirmed field stays Confirmed"
              Expect.equal y.Status Excluded "Proposed field becomes Excluded"

              Expect.equal
                  y.Source
                  Convention
                  "Proposed field source preserved (finalize is zero-judgment; behavior-correction from Source=Manual)"

              Expect.equal z.Status Excluded "Unresolved field becomes Excluded"

              Expect.equal
                  z.Source
                  Convention
                  "Unresolved field source preserved (finalize is zero-judgment; behavior-correction from Source=Manual)"
          }

          test "summary counts match result state and input already-decided count" {
              let lf =
                  lockWith
                      [ mapping "A" Confirmed
                        mapping "B" Excluded
                        mapping "C" Proposed
                        mapping "D" Unresolved ]

              let result, summary = Finalize.run lf

              let expectedConfirmed =
                  result.Mappings |> List.filter (fun m -> m.Status = Confirmed) |> List.length

              let expectedExcluded =
                  result.Mappings |> List.filter (fun m -> m.Status = Excluded) |> List.length

              Expect.equal summary.Confirmed expectedConfirmed "Confirmed count matches result"
              Expect.equal summary.Excluded expectedExcluded "Excluded count matches result"
              Expect.equal summary.AlreadyDecided 2 "AlreadyDecided = Confirmed + Excluded in input (A + B)"
          }

          test "provenance honesty: Proposed+Convention mapping demoted to Excluded with Source=Convention (not Manual)" {
              let conventionProposed: Mapping =
                  { FSharpType = "MyApp.Product"
                    Iri = None
                    Confidence = 0.5
                    Source = Convention
                    Status = Proposed
                    Alternates = []
                    Shape =
                      MappingShape.Record
                          [ { Name = "Price"
                              Iri = None
                              Confidence = 0.5
                              Source = Convention
                              Status = Proposed } ] }

              let lf = lockWith [ conventionProposed ]
              let result, _ = Finalize.run lf
              let m = result.Mappings |> List.head
              Expect.equal m.Status Excluded "Proposed mapping becomes Excluded"

              Expect.equal
                  m.Source
                  Convention
                  "Source must remain Convention (finalize does not claim Manual authorship)"

              let f = MappingShape.payloadFields m.Shape |> List.head
              Expect.equal f.Status Excluded "Proposed field becomes Excluded"
              Expect.equal f.Source Convention "Field source must remain Convention"
          }

          test "idempotent: running twice produces identical mappings" {
              let lf =
                  lockWith
                      [ mapping "A" Confirmed
                        mapping "B" Proposed
                        mapping "C" Unresolved
                        mapping "D" Excluded ]

              let lf2, _ = Finalize.run lf
              let lf3, _ = Finalize.run lf2
              Expect.equal lf3.Mappings lf2.Mappings "second run is a no-op"
          } ]
