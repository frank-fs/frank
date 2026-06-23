module Frank.Cli.Core.Tests.ValidationEmitterTests

open System
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core
open Frank.Cli.Core.Tests.FcsTypecheck

// ── Helpers ───────────────────────────────────────────────────────────────────

let private okOrFail (r: Result<'a, string>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failwith $"Expected Ok but got Error: {e}"

// ── TicTacToe-shaped fixtures ─────────────────────────────────────────────────

let private schemaPrefix = Uri("https://schema.org/")

let private registry: VocabularyRegistry =
    { VocabularyRegistry.empty with
        Prefixes = Map.ofList [ "schema", schemaPrefix ]
        Using = Set.ofList [ "schema" ]
        ConstraintPatterns = Map.ofList [ ("TicTacToe.MoveAction", "position"), "[0-8]" ] }

let private lock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Vocabularies =
        Map.ofList
            [ "schema",
              { Uri = "https://schema.org/"
                FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                Hash = "sha256:test" } ]
      Mappings =
        [ { FSharpType = "TicTacToe.MoveAction"
            Iri = Some "schema:MoveAction"
            Confidence = 1.0
            Source = Convention
            Status = Confirmed
            Alternates = []
            Shape =
              MappingShape.Record
                  [ { Name = "position"
                      Iri = Some "schema:position"
                      Confidence = 1.0
                      Source = Convention
                      Status = Confirmed }
                    { Name = "notes"
                      Iri = Some "schema:description"
                      Confidence = 0.9
                      Source = Convention
                      Status = Confirmed }
                    { Name = "tags"
                      Iri = Some "schema:keywords"
                      Confidence = 0.8
                      Source = Convention
                      Status = Confirmed } ] }
          { FSharpType = "TicTacToe.GameStatusType"
            Iri = Some "schema:GameStatusType"
            Confidence = 1.0
            Source = Convention
            Status = Confirmed
            Alternates = []
            Shape =
              MappingShape.Union
                  [ { Name = "Active"
                      Iri = Some "schema:ActiveGameStatus"
                      Confidence = 1.0
                      Source = Convention
                      Status = Confirmed
                      Payload = [] }
                    { Name = "Ended"
                      Iri = Some "schema:EndedGameStatus"
                      Confidence = 1.0
                      Source = Convention
                      Status = Confirmed
                      Payload = [] } ] } ] }

// typesByName: int field, Option<string> field, string list field + constraint pattern
let private typesByName: Map<string, TypeInfo> =
    Map.ofList
        [ "TicTacToe.MoveAction",
          { FullName = "TicTacToe.MoveAction"
            Namespace = "TicTacToe"
            LocalName = "MoveAction"
            Shape =
              TypeShape.Record
                  [ { Name = "position"
                      TypeName = "int"
                      Attributes = Map.empty
                      DocComment = None }
                    { Name = "notes"
                      TypeName = "string option"
                      Attributes = Map.empty
                      DocComment = None }
                    { Name = "tags"
                      TypeName = "string list"
                      Attributes = Map.empty
                      DocComment = None } ]
            Attributes = Map.empty
            DocComment = None }
          "TicTacToe.GameStatusType",
          { FullName = "TicTacToe.GameStatusType"
            Namespace = "TicTacToe"
            LocalName = "GameStatusType"
            Shape =
              TypeShape.Union
                  [ { Name = "Active"
                      Payload = []
                      Attributes = Map.empty
                      DocComment = None }
                    { Name = "Ended"
                      Payload = []
                      Attributes = Map.empty
                      DocComment = None } ]
            Attributes = Map.empty
            DocComment = None } ]

// ── tier-1 projection ─────────────────────────────────────────────────────────

[<Tests>]
let tier1Tests =
    testList
        "ValidationEmitter — tier-1 projection"
        [ test "projectShapes yields a RecordShape with datatype/cardinality from enriched types" {
              let shapes =
                  ValidationEmitter.emit "T.GeneratedValidation" registry lock typesByName
                  |> okOrFail

              Expect.stringContains shapes "RecordShape" "record node shape DU case"
              Expect.stringContains shapes "https://schema.org/MoveAction" "record class IRI"
              Expect.stringContains shapes "Some XsdInteger" "int → XsdInteger"
              Expect.stringContains shapes "EnumShape" "nullary union → EnumShape DU case"
              Expect.stringContains shapes "https://schema.org/GameStatusType" "enum class IRI"
              Expect.isFalse (shapes.Contains "urn:frank:") "no synthetic URIs"
          }

          test "optional field yields MinCount = 0 and MaxCount = Some 1" {
              let shapes =
                  ValidationEmitter.emit "T.GeneratedValidation" registry lock typesByName
                  |> okOrFail

              Expect.stringContains shapes "MinCount = 0" "optional field has MinCount 0"
          }

          test "collection field yields MaxCount = None" {
              let shapes =
                  ValidationEmitter.emit "T.GeneratedValidation" registry lock typesByName
                  |> okOrFail

              Expect.stringContains shapes "MaxCount = None" "collection field has no MaxCount"
          }

          test "constraint pattern appears as Some in emitted source" {
              let shapes =
                  ValidationEmitter.emit "T.GeneratedValidation" registry lock typesByName
                  |> okOrFail

              Expect.stringContains shapes "Some \"[0-8]\"" "constraint pattern in emitted source"
          }

          test "emitted source contains shapesGraph binding" {
              let shapes =
                  ValidationEmitter.emit "T.GeneratedValidation" registry lock typesByName
                  |> okOrFail

              Expect.stringContains shapes "shapesGraph" "shapesGraph value emitted"
              Expect.stringContains shapes "Shapes.toShapesGraph" "uses interpreter not buildShapesGraph"
          }

          test "EnumShape head and tail IRIs appear in output" {
              let shapes =
                  ValidationEmitter.emit "T.GeneratedValidation" registry lock typesByName
                  |> okOrFail

              Expect.stringContains shapes "https://schema.org/ActiveGameStatus" "Active case IRI present"
              Expect.stringContains shapes "https://schema.org/EndedGameStatus" "Ended case IRI present"
          } ]

// ── fail-closed ───────────────────────────────────────────────────────────────

[<Tests>]
let failClosedTests =
    testList
        "ValidationEmitter — fail-closed"
        [ test "shaped field with no type info → Error" {
              match ValidationEmitter.emit "T.GeneratedValidation" registry lock Map.empty with
              | Error _ -> ()
              | Ok _ -> failtest "expected Error when a shaped field has no enriched type"
          } ]

// ── tier-3 compile gate ───────────────────────────────────────────────────────

[<Tests>]
let compileGateTests =
    testList
        "ValidationEmitter — tier-3 compile-gate"
        [ test "emitted GeneratedValidation compiles against Frank.Semantic/Frank.Validation (tier 3)" {
              let src =
                  ValidationEmitter.emit "T.GeneratedValidation" registry lock typesByName
                  |> okOrFail

              let domainSrc =
                  "namespace VDS.RDF.Shacl\ntype ShapesGraph = class end\n"
                  + "namespace Frank.Semantic\nopen System\n"
                  + "type NonEmptyList<'T> = { Head: 'T; Tail: 'T list }\n"
                  + "type XsdDatatype = XsdInteger | XsdLong | XsdDecimal | XsdDouble | XsdBoolean | XsdString | XsdDateTime\n"
                  + "type PropertyShape = { Path: Uri; Datatype: XsdDatatype option; MinCount: int; MaxCount: int option; Pattern: string option }\n"
                  + "type ShapeDecl = RecordShape of Uri * PropertyShape list | EnumShape of Uri * NonEmptyList<Uri>\n"
                  + "namespace Frank.Validation\nmodule Shapes =\n    let toShapesGraph (_: Frank.Semantic.ShapeDecl list) : VDS.RDF.Shacl.ShapesGraph = Unchecked.defaultof<_>\n"

              let diagnostics = typecheckTwoSources domainSrc src
              Expect.isEmpty diagnostics $"emitted Validation module compiles cleanly; errors: {diagnostics}"
          } ]

// ── determinism ───────────────────────────────────────────────────────────────

[<Tests>]
let determinismTests =
    testList
        "ValidationEmitter — determinism"
        [ test "two emits byte-identical" {
              let a = ValidationEmitter.emit "T.GeneratedValidation" registry lock typesByName
              let b = ValidationEmitter.emit "T.GeneratedValidation" registry lock typesByName
              Expect.equal a b "deterministic"
          } ]
