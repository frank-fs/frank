module Frank.Cli.Core.Tests.DiscoveryEmitterTests

open System
open Expecto
open FsCheck
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core

// ── Test fixtures ─────────────────────────────────────────────────────────────

let private schemaPrefix = Uri("https://schema.org/")

let private schemaRegistry: VocabularyRegistry =
    { VocabularyRegistry.empty with
        Prefixes = Map.ofList [ "schema", schemaPrefix ] }

let private ticTacToeLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Vocabularies =
        Map.ofList
            [ "schema",
              { Uri = "https://schema.org/"
                FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                Hash = "sha256:test" } ]
      DeclaredPrefixes = Map.empty
      Mappings =
        [ { FSharpType = "TicTacToe.Game"
            Iri = Some "schema:Game"
            Confidence = 1.0
            Source = Convention
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "identifier"
                      Iri = Some "schema:identifier"
                      Confidence = 1.0
                      Source = Convention
                      Status = Confirmed }
                    { Name = "status"
                      Iri = None
                      Confidence = 0.0
                      Source = Convention
                      Status = Unresolved } ] }
          { FSharpType = "TicTacToe.Move"
            Iri = Some "schema:MoveAction"
            Confidence = 0.9
            Source = Convention
            Status = Confirmed
            Alternates = []
            Rt = Some "schema:Game"
            Shape =
              MappingShape.Record
                  [ { Name = "rowIndex"
                      Iri = Some "schema:rowIndex"
                      Confidence = 0.8
                      Source = Convention
                      Status = Confirmed }
                    { Name = "columnIndex"
                      Iri = Some "schema:columnIndex"
                      Confidence = 0.8
                      Source = Convention
                      Status = Confirmed }
                    { Name = "agent"
                      Iri = Some "schema:agent"
                      Confidence = 1.0
                      Source = Convention
                      Status = Confirmed } ] } ] }

let private schemaVocabEntry: VocabularyEntry =
    { Uri = "https://schema.org/"
      FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Hash = "test" }

let private minimalLock (mapping: Mapping) : LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.UtcNow
      Vocabularies = Map.ofList [ "schema", schemaVocabEntry ]
      DeclaredPrefixes = Map.empty
      Mappings = [ mapping ] }

let private singleTypeLock: LockFile =
    minimalLock
        { FSharpType = "My.Foo"
          Iri = Some "schema:Foo"
          Confidence = 1.0
          Source = Convention
          Status = Confirmed
          Alternates = []
          Rt = None
          Shape =
            MappingShape.Record
                [ { Name = "bar"
                    Iri = Some "schema:bar"
                    Confidence = 1.0
                    Source = Convention
                    Status = Confirmed } ] }

// ── Result helpers ────────────────────────────────────────────────────────────

let private unwrapOk (r: Result<string, string>) : string =
    match r with
    | Ok s -> s
    | Error e -> failwith $"Expected Ok but got Error: {e}"

// ── FCS parse helper (in-process, no child processes) ────────────────────────

let private parsesFsSource (source: string) : bool =
    let checker = FSharp.Compiler.CodeAnalysis.FSharpChecker.Create()
    let sourceText = FSharp.Compiler.Text.SourceText.ofString source

    let opts =
        { FSharp.Compiler.CodeAnalysis.FSharpParsingOptions.Default with
            SourceFiles = [| "Generated.fs" |] }

    let result =
        checker.ParseFile("Generated.fs", sourceText, opts) |> Async.RunSynchronously

    not result.ParseHadErrors

// ── Generators for FsCheck properties ────────────────────────────────────────

let private genMappingSource = Gen.elements [ Convention; Llm; Manual ]

let private genMappingStatus = Gen.elements [ Confirmed; Proposed; Unresolved ]

let private genFieldWithSchemaIri =
    gen {
        let! name =
            Arb.generate<NonEmptyString>
            |> Gen.map (fun (NonEmptyString s) -> s.Replace(":", ""))

        let! hasIri = Gen.elements [ true; false ]

        let iri = if hasIri then Some $"schema:{name}" else None

        let! confidence = Gen.choose (0, 100) |> Gen.map (fun n -> float n / 100.0)
        let! source = genMappingSource
        let! status = genMappingStatus

        return
            { Name = name
              Iri = iri
              Confidence = confidence
              Source = source
              Status = status }
    }

let private genMappingWithSchemaIri =
    gen {
        let! fsType =
            Arb.generate<NonEmptyString>
            |> Gen.map (fun (NonEmptyString s) -> "My." + s.Replace(":", "").Replace(".", ""))

        let! hasIri = Gen.elements [ true; false ]

        let iri = if hasIri then Some $"schema:{fsType}" else None

        let! confidence = Gen.choose (0, 100) |> Gen.map (fun n -> float n / 100.0)
        let! source = genMappingSource
        let! status = genMappingStatus
        let! fieldCount = Gen.choose (0, 3)
        let! fields = Gen.listOfLength fieldCount genFieldWithSchemaIri

        return
            { FSharpType = fsType
              Iri = iri
              Confidence = confidence
              Source = source
              Status = status
              Alternates = []
              Rt = None
              Shape = MappingShape.Record fields }
    }

let private genLockWithSchemaIris =
    gen {
        let! count = Gen.choose (1, 3)
        let! mappings = Gen.listOfLength count genMappingWithSchemaIri

        return
            { SchemaVersion = 1
              Generated = DateTimeOffset.UtcNow
              Vocabularies = Map.ofList [ "schema", schemaVocabEntry ]
              DeclaredPrefixes = Map.empty
              Mappings = mappings }
    }

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let badModuleNameTests =
    testList
        "DiscoveryEmitter — bad module name returns Error"
        [ test "empty module name returns Error not exception" {
              let result = DiscoveryEmitter.emit "" "/alps" schemaRegistry ticTacToeLock
              Expect.isError result "empty module name must return Error"
          }

          test "dotless module name returns Error not exception" {
              let result =
                  DiscoveryEmitter.emit "NoNamespace" "/alps" schemaRegistry ticTacToeLock

              Expect.isError result "dotless module name must return Error"
          }

          test "valid qualified name still produces Ok" {
              let result =
                  DiscoveryEmitter.emit "TicTacToe.GeneratedDiscovery" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk result "qualified name must succeed"
          } ]

[<Tests>]
let noUrnFrankTests =
    testList
        "DiscoveryEmitter — no urn:frank:"
        [ testPropertyWithConfig
              { FsCheckConfig.defaultConfig with
                  maxTest = 50 }
              "emitted source never contains 'urn:frank:'"
              (Prop.forAll (Arb.fromGen genLockWithSchemaIris) (fun lock ->
                  match DiscoveryEmitter.emit "Test.Generated" "/alps" schemaRegistry lock with
                  | Error _ -> true
                  | Ok src -> not (src.Contains("urn:frank:")))) ]

[<Tests>]
let vocabIriTests =
    testList
        "DiscoveryEmitter — vocab IRIs present in TicTacToe fixture"
        [ test "schema.org/Game present" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://schema.org/Game" "Game IRI"
          }

          test "schema.org/MoveAction present" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://schema.org/MoveAction" "MoveAction IRI"
          }

          test "schema.org/rowIndex present" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://schema.org/rowIndex" "rowIndex IRI"
          }

          test "schema.org/columnIndex present" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://schema.org/columnIndex" "columnIndex IRI"
          }

          test "schema.org/agent present" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://schema.org/agent" "agent IRI"
          }

          test "schema.org/identifier present" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://schema.org/identifier" "identifier IRI"
          } ]

[<Tests>]
let parsesTests =
    testList
        "DiscoveryEmitter — emitted source parses as valid F#"
        [ test "TicTacToe fixture parses" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.isTrue (parsesFsSource (unwrapOk src)) "should parse as valid F#"
          }

          test "single-type lock parses" {
              let src = DiscoveryEmitter.emit "My.Generated" "/alps" schemaRegistry singleTypeLock
              Expect.isOk src "emit should succeed"
              Expect.isTrue (parsesFsSource (unwrapOk src)) "should parse as valid F#"
          } ]

[<Tests>]
let descriptorCountTests =
    testList
        "DiscoveryEmitter — descriptor count"
        [ test "TicTacToe: 2 types + fields with IRIs only" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              let source = unwrapOk src

              let allTypes =
                  ticTacToeLock.Mappings |> List.filter (fun m -> m.Iri.IsSome) |> List.length

              let allFields =
                  ticTacToeLock.Mappings
                  |> List.collect (fun m -> MappingShape.payloadFields m.Shape)
                  |> List.filter (fun f -> f.Iri.IsSome)
                  |> List.length

              Expect.isTrue (allTypes > 0) "has type-level IRIs"
              Expect.isTrue (allFields > 0) "has field-level IRIs"
              Expect.isTrue (source.Length > 0) "non-empty source"
          }

          test "single-type 1-field: emits type + field descriptors" {
              let src = DiscoveryEmitter.emit "My.Generated" "/alps" schemaRegistry singleTypeLock

              Expect.isOk src "emit should succeed"
              let source = unwrapOk src
              Expect.stringContains source "https://schema.org/Foo" "type IRI Foo"
              Expect.stringContains source "https://schema.org/bar" "field IRI bar"
          } ]

[<Tests>]
let describedByTests =
    testList
        "DiscoveryEmitter — DescribedBy links"
        [ test "TicTacToe: vocabulary links use rel=type (not describedby)" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              // Emitted F# source contains escaped quotes: rel=\"type\"
              Expect.stringContains (unwrapOk src) "rel=\\\"type\\\"" "vocabulary links carry rel=type"

              Expect.isFalse
                  ((unwrapOk src).Contains("rel=\\\"describedby\\\""))
                  "vocabulary links do not use describedby"
          }

          test "no urn:frank in describedby links" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.isFalse ((unwrapOk src).Contains("urn:frank")) "no urn:frank in describedby"
          } ]

[<Tests>]
let prefixResolutionTests =
    testList
        "DiscoveryEmitter — prefix resolution"
        [ test "unknown prefix returns Error" {
              let noRegistry = VocabularyRegistry.empty

              let lockWithUnknown =
                  minimalLock
                      { FSharpType = "My.Foo"
                        Iri = Some "unknown:Foo"
                        Confidence = 1.0
                        Source = Convention
                        Status = Confirmed
                        Alternates = []
                        Rt = None
                        Shape = MappingShape.Record [] }

              let result = DiscoveryEmitter.emit "My.Generated" "/alps" noRegistry lockWithUnknown
              Expect.isError result "unknown prefix must return Error"
          }

          test "schema:Foo resolves to https://schema.org/Foo" {
              let src = DiscoveryEmitter.emit "My.Generated" "/alps" schemaRegistry singleTypeLock
              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://schema.org/Foo" "resolved Foo IRI"
          }

          test "mapping with None IRI is skipped without error" {
              let lockWithNoneIri =
                  minimalLock
                      { FSharpType = "My.Unresolved"
                        Iri = None
                        Confidence = 0.0
                        Source = Convention
                        Status = Unresolved
                        Alternates = []
                        Rt = None
                        Shape = MappingShape.Record [] }

              let result =
                  DiscoveryEmitter.emit "My.Generated" "/alps" schemaRegistry lockWithNoneIri

              Expect.isOk result "None IRI mapping emits without error"
          } ]

[<Tests>]
let determinismTests =
    testList
        "DiscoveryEmitter — round-trip determinism"
        [ test "same lock emitted twice yields identical source" {
              let src1 =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              let src2 =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.equal src1 src2 "deterministic output"
          }

          test "single-type lock is deterministic" {
              let src1 =
                  DiscoveryEmitter.emit "My.Generated" "/alps" schemaRegistry singleTypeLock

              let src2 =
                  DiscoveryEmitter.emit "My.Generated" "/alps" schemaRegistry singleTypeLock

              Expect.equal src1 src2 "deterministic output"
          }

          testPropertyWithConfig
              { FsCheckConfig.defaultConfig with
                  maxTest = 20 }
              "emit same lock twice always identical"
              (Prop.forAll (Arb.fromGen genLockWithSchemaIris) (fun lock ->
                  let r1 = DiscoveryEmitter.emit "Test.Generated" "/alps" schemaRegistry lock
                  let r2 = DiscoveryEmitter.emit "Test.Generated" "/alps" schemaRegistry lock
                  r1 = r2)) ]

[<Tests>]
let excludedMappingTests =
    testList
        "DiscoveryEmitter — Excluded mappings generate nothing"
        [ test "excluded mapping IRI absent; confirmed mapping IRI present" {
              let twoMappingLock: LockFile =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.UtcNow
                    Vocabularies = Map.ofList [ "schema", schemaVocabEntry ]
                    DeclaredPrefixes = Map.empty
                    Mappings =
                      [ { FSharpType = "MyApp.Game"
                          Iri = Some "schema:Game"
                          Confidence = 1.0
                          Source = Convention
                          Status = Confirmed
                          Alternates = []
                          Rt = None
                          Shape = MappingShape.Record [] }
                        { FSharpType = "MyApp.Player"
                          Iri = Some "schema:Player"
                          Confidence = 0.9
                          Source = Convention
                          Status = Excluded
                          Alternates = []
                          Rt = None
                          Shape = MappingShape.Record [] } ] }

              let result =
                  DiscoveryEmitter.emit "MyApp.Generated" "/alps" schemaRegistry twoMappingLock

              Expect.isOk result "emit should succeed"
              let source = unwrapOk result
              Expect.stringContains source "https://schema.org/Game" "confirmed Game IRI present"
              Expect.isFalse (source.Contains("https://schema.org/Player")) "excluded Player IRI absent"
          } ]

[<Tests>]
let projectionTests =
    testList
        "DiscoveryEmitter — typed projection (tier 1)"
        [ test "projectDiscovery: MoveAction top-level; rowIndex nested as child" {
              let model =
                  ResolvedModel.build schemaRegistry ticTacToeLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith $"Expected Ok but got Error: {e}"

              let descriptors, links = DiscoveryEmitter.projectDiscovery Set.empty model

              // MoveAction is at the top level
              Expect.contains (descriptors |> List.map (fun d -> d.Id)) "MoveAction" "MoveAction top-level present"

              let moveAction =
                  descriptors |> List.find (fun d -> d.Id = "MoveAction")

              // MoveAction's href is present
              Expect.equal moveAction.Href (Some "https://schema.org/MoveAction") "MoveAction href"

              // rowIndex is NESTED inside MoveAction, not flat
              Expect.contains
                  (moveAction.Children |> List.map (fun d -> d.Href))
                  (Some "https://schema.org/rowIndex")
                  "rowIndex nested under MoveAction"

              // rowIndex is NOT at the top-level flat list
              Expect.isFalse
                  (descriptors |> List.exists (fun d -> d.Id = "rowIndex"))
                  "rowIndex must not be a flat top-level descriptor"

              Expect.isNonEmpty links "describedBy links present"
          } ]

[<Tests>]
let compileGateTests =
    testList
        "DiscoveryEmitter — compile gate (tier 3)"
        [ test "emitted GeneratedDiscovery compiles against Frank.Discovery types (tier 3)" {
              let src =
                  DiscoveryEmitter.emit "Probe.GeneratedDiscovery" "/alps/tictactoe" schemaRegistry ticTacToeLock
                  |> function
                      | Ok s -> s
                      | Error e -> failwith $"Expected Ok but got Error: {e}"

              let assemblies = [ typeof<Frank.Discovery.DiscoveryConfig>.Assembly ]

              let diagnostics = FcsTypecheck.typecheckAgainstRealAssemblies src assemblies
              Expect.isEmpty diagnostics $"emitted Discovery module compiles cleanly; errors: {diagnostics}"
          } ]

[<Tests>]
let homeResourcesAbsentTests =
    testList
        "DiscoveryEmitter — HomeResources absent from generated source"
        [ test "generated source does not contain HomeResources field" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.isFalse ((unwrapOk src).Contains "HomeResources") "HomeResources absent"
          }

          test "generated record literal contains ProfileUri, HomeRoute, AlpsDescriptors, DescribedByLinks" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              let source = unwrapOk src
              Expect.stringContains source "ProfileUri" "ProfileUri field present"
              Expect.stringContains source "HomeRoute" "HomeRoute field present"
              Expect.stringContains source "AlpsDescriptors" "AlpsDescriptors field present"
              Expect.stringContains source "DescribedByLinks" "DescribedByLinks field present"
          }

          test "generated source parses as valid F# without HomeResources" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.isTrue (parsesFsSource (unwrapOk src)) "parses as valid F#"
          } ]

// ── Fixture: declared-only prefix (ttt: in DeclaredPrefixes, NOT in Vocabularies) ──
// tttRegistry reuses schemaRegistry — ttt: resolution comes from lock.DeclaredPrefixes, not registry.Prefixes

let private tttDeclaredOnlyLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Vocabularies =
        Map.ofList
            [ "schema",
              { Uri = "https://schema.org/"
                FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                Hash = "sha256:test" } ]
      DeclaredPrefixes = Map.ofList [ "ttt", "https://example.org/tictactoe#" ]
      Mappings =
        [ { FSharpType = "TicTacToe.MoveAction"
            Iri = Some "schema:MoveAction"
            Confidence = 1.0
            Source = Convention
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "square"
                      Iri = Some "ttt:square"
                      Confidence = 1.0
                      Source = Convention
                      Status = Confirmed }
                    { Name = "agent"
                      Iri = Some "schema:agent"
                      Confidence = 1.0
                      Source = Convention
                      Status = Confirmed } ] } ] }

[<Tests>]
let relativeHrefTests =
    testList
        "DiscoveryEmitter — declared-only prefix emits relative href (item #6)"
        [ test "ttt:square href is host-relative /tictactoe#square — NOT absolute example.org" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.GeneratedDiscovery" "/alps/tictactoe" schemaRegistry tttDeclaredOnlyLock

              Expect.isOk src "emit should succeed"
              let source = unwrapOk src
              Expect.stringContains source "/tictactoe#square" "relative href present"
              Expect.isFalse (source.Contains "example.org/tictactoe#square") "absolute example.org href absent"
          }

          test "schema:agent href stays absolute (external vocab not relativised)" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.GeneratedDiscovery" "/alps/tictactoe" schemaRegistry tttDeclaredOnlyLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://schema.org/agent" "schema href unchanged"
          }

          test "schema:MoveAction type href stays absolute (external vocab)" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.GeneratedDiscovery" "/alps/tictactoe" schemaRegistry tttDeclaredOnlyLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://schema.org/MoveAction" "MoveAction href unchanged"
          } ]

// ── Fixture: ex: declared-only variant with cell (for AT-S7 rename) ──────────

let private exDeclaredOnlyLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Vocabularies = Map.empty
      DeclaredPrefixes =
        Map.ofList
            [ "ex", "https://example.org/ex#" ]
      Mappings =
        [ { FSharpType = "TicTacToe.Model.Game"
            Iri = Some "ex:Game"
            Confidence = 1.0
            Source = Manual
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "Id"
                      Iri = Some "ex:identifier"
                      Confidence = 1.0
                      Source = Manual
                      Status = Confirmed } ] }
          { FSharpType = "TicTacToe.Model.MoveRequest"
            Iri = Some "ex:MoveAction"
            Confidence = 1.0
            Source = Manual
            Status = Confirmed
            Alternates = []
            Rt = Some "ex:Game"
            Shape =
              MappingShape.Record
                  [ { Name = "Position"
                      Iri = Some "ex:cell"
                      Confidence = 1.0
                      Source = Manual
                      Status = Confirmed }
                    { Name = "Player"
                      Iri = Some "ex:agent"
                      Confidence = 1.0
                      Source = Manual
                      Status = Confirmed } ] } ] }

// ── #4 AC1: ALPS nesting — MoveAction contains its field descriptors ────────

[<Tests>]
let nestingTests =
    testList
        "DiscoveryEmitter — #4 ALPS nesting (AC1)"
        [ test "MoveAction descriptor type is unsafe (not semantic)" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              let source = unwrapOk src
              // In the generated F# record literal, the MoveAction entry must carry Type = "unsafe"
              Expect.stringContains source "\"unsafe\"" "MoveAction descriptor type must be unsafe"
          }

          test "MoveAction has Rt pointing to schema:Game (return type)" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              let source = unwrapOk src
              // The MoveAction descriptor Rt must point to the schema:Game IRI
              Expect.stringContains
                  source
                  "https://schema.org/Game"
                  "MoveAction Rt must point to schema:Game"
              Expect.stringContains source "Rt = Some" "MoveAction must have Rt = Some ..."
          }

          test "field descriptors nested under MoveAction — no flat-sibling rowIndex at top level" {
              let model =
                  ResolvedModel.build schemaRegistry ticTacToeLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let bases = Set.empty
              let descriptors, _ = DiscoveryEmitter.projectDiscovery bases model
              // Top-level descriptors should only be class descriptors (Game, MoveAction)
              // Field descriptors (rowIndex, columnIndex, agent) are Children, not top-level
              let topLevelIds = descriptors |> List.map (fun d -> d.Id)
              Expect.isFalse (List.contains "rowIndex" topLevelIds) "rowIndex must not be top-level"
              Expect.isFalse (List.contains "columnIndex" topLevelIds) "columnIndex must not be top-level"
              Expect.isFalse (List.contains "agent" topLevelIds) "agent must not be top-level (nested under MoveAction)"
          }

          test "MoveAction descriptor has children with rowIndex, columnIndex, agent" {
              let model =
                  ResolvedModel.build schemaRegistry ticTacToeLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let bases = Set.empty
              let descriptors, _ = DiscoveryEmitter.projectDiscovery bases model

              let moveAction =
                  descriptors
                  |> List.tryFind (fun d -> d.Id = "MoveAction")
                  |> Option.defaultWith (fun () -> failwith "MoveAction descriptor not found")

              let childIds = moveAction.Children |> List.map (fun d -> d.Id)
              Expect.contains childIds "rowIndex" "rowIndex child present"
              Expect.contains childIds "columnIndex" "columnIndex child present"
              Expect.contains childIds "agent" "agent child present"
          }

          test "MoveAction IsAction = true; Game IsAction = false" {
              let model =
                  ResolvedModel.build schemaRegistry ticTacToeLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let bases = Set.empty
              let descriptors, _ = DiscoveryEmitter.projectDiscovery bases model

              let game =
                  descriptors |> List.tryFind (fun d -> d.Id = "Game") |> Option.defaultWith (fun () -> failwith "Game not found")

              let moveAction =
                  descriptors
                  |> List.tryFind (fun d -> d.Id = "MoveAction")
                  |> Option.defaultWith (fun () -> failwith "MoveAction not found")

              Expect.isFalse game.IsAction "Game is not an action"
              Expect.isTrue moveAction.IsAction "MoveAction is an action"
          }

          test "MoveAction Rt = Some schema:Game href; Game Rt = None" {
              let model =
                  ResolvedModel.build schemaRegistry ticTacToeLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let bases = Set.empty
              let descriptors, _ = DiscoveryEmitter.projectDiscovery bases model

              let game =
                  descriptors |> List.tryFind (fun d -> d.Id = "Game") |> Option.defaultWith (fun () -> failwith "Game not found")

              let moveAction =
                  descriptors
                  |> List.tryFind (fun d -> d.Id = "MoveAction")
                  |> Option.defaultWith (fun () -> failwith "MoveAction not found")

              Expect.isNone game.Rt "Game has no Rt"
              Expect.equal moveAction.Rt (Some "https://schema.org/Game") "MoveAction Rt = schema:Game"
          }

          test "ex:cell lock: MoveAction has child cell (not square)" {
              let bases = Set.ofList [ "https://example.org/ex#" ]
              let model =
                  ResolvedModel.build VocabularyRegistry.empty exDeclaredOnlyLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let descriptors, _ = DiscoveryEmitter.projectDiscovery bases model

              let moveAction =
                  descriptors
                  |> List.tryFind (fun d -> d.Id = "MoveAction")
                  |> Option.defaultWith (fun () -> failwith "MoveAction not found")

              let childIds = moveAction.Children |> List.map (fun d -> d.Id)
              Expect.contains childIds "cell" "cell child present (renamed from square)"
              Expect.isFalse (List.contains "square" childIds) "square must NOT be present (renamed to cell)"
          }

          test "AlpsSerializer emits nested descriptor array under MoveAction" {
              let nested: Frank.Discovery.AlpsDescriptor =
                  { Id = "square"
                    Type = "semantic"
                    Doc = None
                    Href = Some "/ttt#square"
                    Descriptors = []
                    Rt = None }

              let action: Frank.Discovery.AlpsDescriptor =
                  { Id = "MoveAction"
                    Type = "unsafe"
                    Doc = None
                    Href = Some "https://schema.org/MoveAction"
                    Descriptors = [ nested ]
                    Rt = Some "https://schema.org/Game" }

              let json = Frank.Discovery.AlpsSerializer.serialize [ action ]
              use doc = System.Text.Json.JsonDocument.Parse json
              let mutable alpsEl = Unchecked.defaultof<System.Text.Json.JsonElement>
              let mutable descEl = Unchecked.defaultof<System.Text.Json.JsonElement>

              let found =
                  doc.RootElement.TryGetProperty("alps", &alpsEl)
                  && alpsEl.TryGetProperty("descriptor", &descEl)

              Expect.isTrue found "ALPS descriptor array present"

              let moveActionEl =
                  descEl.EnumerateArray()
                  |> Seq.tryFind (fun d ->
                      let mutable idEl = Unchecked.defaultof<System.Text.Json.JsonElement>
                      d.TryGetProperty("id", &idEl) && idEl.GetString() = "MoveAction")
                  |> Option.defaultWith (fun () -> failwith "MoveAction not in ALPS")

              let mutable nestedEl = Unchecked.defaultof<System.Text.Json.JsonElement>
              Expect.isTrue (moveActionEl.TryGetProperty("descriptor", &nestedEl)) "MoveAction has nested descriptor array"
              let mutable rtEl = Unchecked.defaultof<System.Text.Json.JsonElement>
              Expect.isTrue (moveActionEl.TryGetProperty("rt", &rtEl)) "MoveAction has rt property"
              Expect.equal (rtEl.GetString()) "https://schema.org/Game" "rt = schema:Game"
          }

          test "AlpsSerializer: leaf descriptors have no descriptor array (clean output)" {
              let leaf: Frank.Discovery.AlpsDescriptor =
                  { Id = "Game"
                    Type = "semantic"
                    Doc = None
                    Href = Some "https://schema.org/Game"
                    Descriptors = []
                    Rt = None }

              let json = Frank.Discovery.AlpsSerializer.serialize [ leaf ]
              use doc = System.Text.Json.JsonDocument.Parse json
              let mutable alpsEl = Unchecked.defaultof<System.Text.Json.JsonElement>
              let mutable descEl = Unchecked.defaultof<System.Text.Json.JsonElement>

              doc.RootElement.TryGetProperty("alps", &alpsEl) |> ignore
              alpsEl.TryGetProperty("descriptor", &descEl) |> ignore

              let gameEl =
                  descEl.EnumerateArray()
                  |> Seq.tryFind (fun d ->
                      let mutable idEl = Unchecked.defaultof<System.Text.Json.JsonElement>
                      d.TryGetProperty("id", &idEl) && idEl.GetString() = "Game")
                  |> Option.defaultWith (fun () -> failwith "Game not in ALPS")

              let mutable nestedEl = Unchecked.defaultof<System.Text.Json.JsonElement>
              Expect.isFalse (gameEl.TryGetProperty("descriptor", &nestedEl)) "leaf Game has no nested descriptor array"
              let mutable rtEl = Unchecked.defaultof<System.Text.Json.JsonElement>
              Expect.isFalse (gameEl.TryGetProperty("rt", &rtEl)) "leaf Game has no rt"
          }

          // ── Ordering-regression: ItemList declared BEFORE Game (the real rt target) ──
          // Under the old positional heuristic, Rt would resolve to ItemList (first non-action
          // non-union record in declaration order). Under the declared-linkage fix, Rt resolves
          // to Game because the action's Rt field names it explicitly regardless of order.
          test "Rt resolves from declared linkage not declaration order: ItemList before Game" {
              let orderingRegressionLock: LockFile =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                    Vocabularies = Map.ofList [ "schema", schemaVocabEntry ]
                    DeclaredPrefixes = Map.empty
                    Mappings =
                      [ { FSharpType = "MyApp.ItemList"
                          Iri = Some "schema:ItemList"
                          Confidence = 1.0
                          Source = Manual
                          Status = Confirmed
                          Alternates = []
                          Rt = None
                          Shape = MappingShape.Record [] }
                        { FSharpType = "MyApp.Game"
                          Iri = Some "schema:Game"
                          Confidence = 1.0
                          Source = Manual
                          Status = Confirmed
                          Alternates = []
                          Rt = None
                          Shape = MappingShape.Record [] }
                        { FSharpType = "MyApp.MoveAction"
                          Iri = Some "schema:MoveAction"
                          Confidence = 1.0
                          Source = Manual
                          Status = Confirmed
                          Alternates = []
                          Rt = Some "schema:Game"
                          Shape = MappingShape.Record [] } ] }

              let model =
                  ResolvedModel.build schemaRegistry orderingRegressionLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let descriptors, _ = DiscoveryEmitter.projectDiscovery Set.empty model

              let moveAction =
                  descriptors
                  |> List.tryFind (fun d -> d.Id = "MoveAction")
                  |> Option.defaultWith (fun () -> failwith "MoveAction not found")

              // Must be Game — NOT ItemList (which would appear under the old positional heuristic
              // because ItemList is declared first in declaration order).
              Expect.equal
                  moveAction.Rt
                  (Some "https://schema.org/Game")
                  "Rt must point to declared Game, not first-declared ItemList"
          } ]
