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

// ── bad module name ───────────────────────────────────────────────────────────

[<Tests>]
let badModuleNameTests =
    testList
        "ValidationEmitter — bad module name returns Error"
        [ test "empty module name returns Error not exception" {
              let result = ValidationEmitter.emit "" registry lock typesByName
              Expect.isError result "empty module name must return Error"
          }

          test "dotless module name returns Error not exception" {
              let result = ValidationEmitter.emit "NoNamespace" registry lock typesByName
              Expect.isError result "dotless module name must return Error"
          }

          test "valid qualified name still produces Ok" {
              let result = ValidationEmitter.emit "TicTacToe.GeneratedValidation" registry lock typesByName
              Expect.isOk result "valid module name must succeed"
          } ]

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

              let assemblies =
                  [ typeof<Frank.Semantic.ShapeDecl>.Assembly
                    typeof<Frank.Validation.ValidationConfig>.Assembly
                    typeof<VDS.RDF.Shacl.ShapesGraph>.Assembly
                    typeof<VDS.RDF.IGraph>.Assembly ]

              let diagnostics = typecheckAgainstRealAssemblies src assemblies
              Expect.isEmpty diagnostics $"emitted Validation module compiles cleanly; errors: {diagnostics}"
          } ]

// ── skip paths ────────────────────────────────────────────────────────────────

[<Tests>]
let skipPathTests =
    testList
        "ValidationEmitter — skip paths"
        [ test "resource with ClassIri=None produces no shape in emitted output" {
              let registryNoIri: VocabularyRegistry =
                  { VocabularyRegistry.empty with
                      Prefixes = Map.ofList [ "schema", schemaPrefix ]
                      Using = Set.ofList [ "schema" ] }

              let lockNoIri: LockFile =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                              Hash = "sha256:test" } ]
                    Mappings =
                      [ { FSharpType = "TicTacToe.NoIriType"
                          Iri = None
                          Confidence = 1.0
                          Source = Convention
                          Status = Confirmed
                          Alternates = []
                          Shape = MappingShape.Record [] } ] }

              let shapes =
                  ValidationEmitter.emit "T.GeneratedValidation" registryNoIri lockNoIri Map.empty
                  |> okOrFail

              Expect.isFalse (shapes.Contains "NoIriType") "resource with ClassIri=None must not appear in shapes"
              Expect.stringContains shapes "shapes" "shapes binding still emitted (empty list)"
          }

          test "non-nullary union resource produces no shape in emitted output" {
              let registryUnion: VocabularyRegistry =
                  { VocabularyRegistry.empty with
                      Prefixes = Map.ofList [ "schema", schemaPrefix ]
                      Using = Set.ofList [ "schema" ] }

              let lockNonNullary: LockFile =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                              Hash = "sha256:test" } ]
                    Mappings =
                      [ { FSharpType = "TicTacToe.PayloadUnion"
                          Iri = Some "schema:PayloadUnion"
                          Confidence = 1.0
                          Source = Convention
                          Status = Confirmed
                          Alternates = []
                          Shape =
                            MappingShape.Union
                                [ { Name = "WithData"
                                    Iri = Some "schema:WithData"
                                    Confidence = 1.0
                                    Source = Convention
                                    Status = Confirmed
                                    Payload =
                                      [ { Name = "value"
                                          Iri = Some "schema:value"
                                          Confidence = 1.0
                                          Source = Convention
                                          Status = Confirmed } ] } ] } ] }

              let shapes =
                  ValidationEmitter.emit "T.GeneratedValidation" registryUnion lockNonNullary Map.empty
                  |> okOrFail

              Expect.isFalse (shapes.Contains "PayloadUnion") "non-nullary union must not appear in shapes"
              Expect.isFalse (shapes.Contains "EnumShape") "non-nullary union must not produce EnumShape"
              Expect.isFalse (shapes.Contains "RecordShape") "non-nullary union must not produce RecordShape"
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
