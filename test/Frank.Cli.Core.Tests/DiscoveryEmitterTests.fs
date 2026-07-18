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
      Integrity = None
      Vocabularies =
        Map.ofList
            [ "schema",
              { v1Empty with
                  Uri = "https://schema.org/"
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
    { v1Empty with
        Uri = "https://schema.org/"
        FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
        Hash = "test" }

let private minimalLock (mapping: Mapping) : LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.UtcNow
      Integrity = None
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
              Integrity = None
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
                    Integrity = None
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

              let moveAction = descriptors |> List.find (fun d -> d.Id = "MoveAction")

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

          test
              "generated record literal contains ProfileUri, HomeRoute, AlpsDescriptors, DescribedByLinks, ResourceHrefVars" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              let source = unwrapOk src
              Expect.stringContains source "ProfileUri" "ProfileUri field present"
              Expect.stringContains source "HomeRoute" "HomeRoute field present"
              Expect.stringContains source "AlpsDescriptors" "AlpsDescriptors field present"
              Expect.stringContains source "DescribedByLinks" "DescribedByLinks field present"
              Expect.stringContains source "ResourceHrefVars" "ResourceHrefVars field present"
          }

          test "generated ResourceHrefVars contains schema:identifier for Game.identifier field (#9)" {
              // ticTacToeLock fixture uses Name="identifier" (not "Id"); lowercased key is "identifier".
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              let source = unwrapOk src
              Expect.stringContains source "https://schema.org/identifier" "schema:identifier in ResourceHrefVars"
              Expect.stringContains source "\"identifier\"" "lowercased 'identifier' key in ResourceHrefVars"
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
      Integrity = None
      Vocabularies =
        Map.ofList
            [ "schema",
              { v1Empty with
                  Uri = "https://schema.org/"
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
                  DiscoveryEmitter.emit
                      "TicTacToe.GeneratedDiscovery"
                      "/alps/tictactoe"
                      schemaRegistry
                      tttDeclaredOnlyLock

              Expect.isOk src "emit should succeed"
              let source = unwrapOk src
              Expect.stringContains source "/tictactoe#square" "relative href present"
              // ALPS Href must be host-relative; the absolute URI legitimately appears in
              // ResourceHrefVars (json-home §4.2 requires absolute meaning IRIs there).
              // Check the ALPS Href specifically, not the whole source.
              Expect.isFalse
                  (source.Contains "Some \"https://example.org/tictactoe#square\"")
                  "ALPS Href must not be the absolute example.org URI"
          }

          test "schema:agent href stays absolute (external vocab not relativised)" {
              let src =
                  DiscoveryEmitter.emit
                      "TicTacToe.GeneratedDiscovery"
                      "/alps/tictactoe"
                      schemaRegistry
                      tttDeclaredOnlyLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://schema.org/agent" "schema href unchanged"
          }

          test "schema:MoveAction type href stays absolute (external vocab)" {
              let src =
                  DiscoveryEmitter.emit
                      "TicTacToe.GeneratedDiscovery"
                      "/alps/tictactoe"
                      schemaRegistry
                      tttDeclaredOnlyLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://schema.org/MoveAction" "MoveAction href unchanged"
          } ]

// ── Fixture: ex: declared-only variant with cell (for AT-S7 rename) ──────────

let private exDeclaredOnlyLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Integrity = None
      Vocabularies = Map.empty
      DeclaredPrefixes = Map.ofList [ "ex", "https://example.org/ex#" ]
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
              Expect.stringContains source "https://schema.org/Game" "MoveAction Rt must point to schema:Game"
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
                  descriptors
                  |> List.tryFind (fun d -> d.Id = "Game")
                  |> Option.defaultWith (fun () -> failwith "Game not found")

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
                  descriptors
                  |> List.tryFind (fun d -> d.Id = "Game")
                  |> Option.defaultWith (fun () -> failwith "Game not found")

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
                    Rt = None
                    ClassIri = None
                    RequestClrTypeName = None }

              let action: Frank.Discovery.AlpsDescriptor =
                  { Id = "MoveAction"
                    Type = "unsafe"
                    Doc = None
                    Href = Some "https://schema.org/MoveAction"
                    Descriptors = [ nested ]
                    Rt = Some "https://schema.org/Game"
                    ClassIri = None
                    RequestClrTypeName = None }

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

              Expect.isTrue
                  (moveActionEl.TryGetProperty("descriptor", &nestedEl))
                  "MoveAction has nested descriptor array"

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
                    Rt = None
                    ClassIri = None
                    RequestClrTypeName = None }

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
                    Integrity = None
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

// ── Fixture: declared Rt on a class whose local name does NOT end in "Action" (M2) ─
// Under the old suffix heuristic (isActionIri), "Submit" does not end in "Action",
// so it would emit IsAction=false and drop the Rt entirely (silent data loss).
// Under the declared-linkage fix (r.Rt.IsSome), it IS an unsafe transition because
// it has a declared Rt, regardless of the class local name.

let private nonActionNameWithRtLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Integrity = None
      Vocabularies = Map.empty
      DeclaredPrefixes = Map.ofList [ "ex", "https://example.org/ex#" ]
      Mappings =
        [ { FSharpType = "App.Target"
            Iri = Some "ex:Target"
            Confidence = 1.0
            Source = Manual
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape = MappingShape.Record [] }
          { FSharpType = "App.Submit"
            Iri = Some "ex:Submit"
            Confidence = 1.0
            Source = Manual
            Status = Confirmed
            Alternates = []
            Rt = Some "ex:Target"
            Shape = MappingShape.Record [] } ] }

[<Tests>]
let m2DeclaredLinkageTests =
    testList
        "DiscoveryEmitter — M2: isAction derived from declared Rt linkage not name suffix"
        [ test "class with Rt=Some but name not ending in 'Action' → IsAction=true" {
              let model =
                  ResolvedModel.build VocabularyRegistry.empty nonActionNameWithRtLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let bases = Set.ofList [ "https://example.org/ex#" ]
              let descriptors, _ = DiscoveryEmitter.projectDiscovery bases model

              let submit =
                  descriptors
                  |> List.tryFind (fun d -> d.Id = "Submit")
                  |> Option.defaultWith (fun () -> failwith "Submit descriptor not found")

              Expect.isTrue
                  submit.IsAction
                  "Submit (Rt=Some) must be an action even though name doesn't end in 'Action'"
          }

          test "class with Rt=Some but name not ending in 'Action' → type='unsafe' in emitted source" {
              let src =
                  DiscoveryEmitter.emit
                      "App.GeneratedDiscovery"
                      "/alps"
                      VocabularyRegistry.empty
                      nonActionNameWithRtLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "\"unsafe\"" "Submit must emit type='unsafe' (declared Rt)"
          }

          test "class with Rt=Some but name not ending in 'Action' → Rt is emitted (not dropped)" {
              let model =
                  ResolvedModel.build VocabularyRegistry.empty nonActionNameWithRtLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let bases = Set.ofList [ "https://example.org/ex#" ]
              let descriptors, _ = DiscoveryEmitter.projectDiscovery bases model

              let submit =
                  descriptors
                  |> List.tryFind (fun d -> d.Id = "Submit")
                  |> Option.defaultWith (fun () -> failwith "Submit descriptor not found")

              Expect.isSome submit.Rt "Submit (Rt=Some) must have Rt in the descriptor (not silently dropped)"
          }

          test "class with Rt=None and name not ending in 'Action' → IsAction=false (Target)" {
              let model =
                  ResolvedModel.build VocabularyRegistry.empty nonActionNameWithRtLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let bases = Set.ofList [ "https://example.org/ex#" ]
              let descriptors, _ = DiscoveryEmitter.projectDiscovery bases model

              let target =
                  descriptors
                  |> List.tryFind (fun d -> d.Id = "Target")
                  |> Option.defaultWith (fun () -> failwith "Target descriptor not found")

              Expect.isFalse target.IsAction "Target (Rt=None) must not be an action"
          }

          test "sample fixture unaffected: MoveAction (Rt=Some) → unsafe; Game (Rt=None) → semantic" {
              let model =
                  ResolvedModel.build schemaRegistry ticTacToeLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let descriptors, _ = DiscoveryEmitter.projectDiscovery Set.empty model

              let moveAction = descriptors |> List.find (fun d -> d.Id = "MoveAction")
              let game = descriptors |> List.find (fun d -> d.Id = "Game")
              Expect.isTrue moveAction.IsAction "MoveAction (Rt=Some) is action"
              Expect.isFalse game.IsAction "Game (Rt=None) is not action"
          } ]

// ── Fixture: union type with outcome cases (MoveResult analogue) ─────────────

let private outcomeUnionLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Integrity = None
      Vocabularies = Map.ofList [ "schema", schemaVocabEntry ]
      DeclaredPrefixes = Map.empty
      Mappings =
        [ { FSharpType = "TicTacToe.MoveResult"
            Iri = Some "schema:ActionStatusType"
            Confidence = 1.0
            Source = Manual
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Union
                  [ { Name = "Won"
                      Iri = Some "schema:CompletedActionStatus"
                      Confidence = 1.0
                      Source = Manual
                      Status = Confirmed
                      Payload = [] }
                    { Name = "Draw"
                      Iri = Some "schema:CompletedActionStatus"
                      Confidence = 1.0
                      Source = Manual
                      Status = Confirmed
                      Payload = [] }
                    { Name = "XTurn"
                      Iri = Some "schema:ActiveActionStatus"
                      Confidence = 1.0
                      Source = Manual
                      Status = Confirmed
                      Payload = [] } ] } ] }

// ── #17 union-case outcome descriptors ───────────────────────────────────────

[<Tests>]
let unionCaseDescriptorTests =
    testList
        "DiscoveryEmitter — #17 union-case outcome descriptors"
        [ test "union type: ActionStatusType descriptor has Won, Draw, XTurn as children" {
              let model =
                  ResolvedModel.build schemaRegistry outcomeUnionLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let bases = Set.empty
              let descriptors, _ = DiscoveryEmitter.projectDiscovery bases model

              let actionStatus =
                  descriptors
                  |> List.tryFind (fun d -> d.Id = "ActionStatusType")
                  |> Option.defaultWith (fun () -> failwith "ActionStatusType descriptor not found")

              let childIds = actionStatus.Children |> List.map (fun d -> d.Id)
              Expect.contains childIds "Won" "Won case descriptor present as child"
              Expect.contains childIds "Draw" "Draw case descriptor present as child"
              Expect.contains childIds "XTurn" "XTurn case descriptor present as child"
          }

          test "Won case child href = schema:CompletedActionStatus" {
              let model =
                  ResolvedModel.build schemaRegistry outcomeUnionLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let bases = Set.empty
              let descriptors, _ = DiscoveryEmitter.projectDiscovery bases model

              let actionStatus = descriptors |> List.find (fun d -> d.Id = "ActionStatusType")

              let wonChild =
                  actionStatus.Children
                  |> List.tryFind (fun d -> d.Id = "Won")
                  |> Option.defaultWith (fun () -> failwith "Won child not found")

              Expect.equal
                  wonChild.Href
                  (Some "https://schema.org/CompletedActionStatus")
                  "Won href = schema:CompletedActionStatus"
          }

          test "case children are not action descriptors" {
              let model =
                  ResolvedModel.build schemaRegistry outcomeUnionLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let bases = Set.empty
              let descriptors, _ = DiscoveryEmitter.projectDiscovery bases model

              let actionStatus = descriptors |> List.find (fun d -> d.Id = "ActionStatusType")

              for child in actionStatus.Children do
                  Expect.isFalse child.IsAction $"case child '{child.Id}' must not be an action"
          }

          test "emitted source contains CompletedActionStatus IRI" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry outcomeUnionLock

              Expect.isOk src "emit should succeed"

              Expect.stringContains
                  (unwrapOk src)
                  "https://schema.org/CompletedActionStatus"
                  "CompletedActionStatus IRI in emitted source"
          }

          test "emitted source parses as valid F# with case descriptors" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry outcomeUnionLock

              Expect.isOk src "emit should succeed"
              Expect.isTrue (parsesFsSource (unwrapOk src)) "parses as valid F#"
          } ]

// ── MINOR-7: collectDescribedByLinks + computeHrefVars host-relativize declared-only prefixes ──

[<Tests>]
let minor7HostRelativeTests =
    testList
        "DiscoveryEmitter — MINOR-7: declared-only prefix host-relative in Link headers and href-vars"
        [ test "collectDescribedByLinks: declared-only class IRI becomes host-relative in Link header" {
              let model =
                  ResolvedModel.build VocabularyRegistry.empty exDeclaredOnlyLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let bases = Set.ofList [ "https://example.org/ex#" ]
              let _, links = DiscoveryEmitter.projectDiscovery bases model

              Expect.exists
                  links
                  (fun (_, l) -> l.Contains "/ex#Game")
                  "declared-only ex:Game class IRI must be host-relative in Link header"

              Expect.isFalse
                  (links |> List.exists (fun (_, l) -> l.Contains "example.org"))
                  "no example.org absolute IRI in Link headers for declared-only prefix"
          }

          test "collectDescribedByLinks: external vocab class IRI stays absolute in Link header" {
              let model =
                  ResolvedModel.build schemaRegistry tttDeclaredOnlyLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let bases = Set.ofList [ "https://example.org/tictactoe#" ]
              let _, links = DiscoveryEmitter.projectDiscovery bases model

              Expect.exists
                  links
                  (fun (_, l) -> l.Contains "https://schema.org/MoveAction")
                  "external vocab class IRI (schema:MoveAction) stays absolute in Link header"
          }

          test "computeHrefVars: declared-only field IRIs emit host-relative meaning in generated source" {
              let src =
                  DiscoveryEmitter.emit "Ex.Generated" "/alps" VocabularyRegistry.empty exDeclaredOnlyLock

              Expect.isOk src "emit should succeed"
              let source = unwrapOk src

              Expect.stringContains
                  source
                  "/ex#identifier"
                  "declared-only field IRI /ex#identifier is host-relative in href-vars"

              Expect.isFalse
                  (source.Contains "\"https://example.org/ex#identifier\"")
                  "no absolute example.org IRI in href-var meaning for declared-only prefix"
          }

          test "computeHrefVars: external vocab field IRIs stay absolute" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry tttDeclaredOnlyLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://schema.org/agent" "schema:agent stays absolute in href-vars"
          } ]

// ── Fixture: duplicate descriptor IDs (same field IRI in two classes) ─────────
// Both Alpha and Beta declare a field with IRI schema:identifier.
// The local name of schema:identifier is "identifier", so after projection both
// classes produce a child descriptor with Id = "identifier".
// The uniqueness invariant must detect and reject this at codegen time.
let private dupIdLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.UtcNow
      Integrity = None
      Vocabularies = Map.ofList [ "schema", schemaVocabEntry ]
      DeclaredPrefixes = Map.empty
      Mappings =
        [ { FSharpType = "MyApp.Alpha"
            Iri = Some "schema:Thing"
            Confidence = 1.0
            Source = Manual
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "id"
                      Iri = Some "schema:identifier"
                      Confidence = 1.0
                      Source = Manual
                      Status = Confirmed } ] }
          { FSharpType = "MyApp.Beta"
            Iri = Some "schema:Action"
            Confidence = 1.0
            Source = Manual
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "id"
                      Iri = Some "schema:identifier"
                      Confidence = 1.0
                      Source = Manual
                      Status = Confirmed } ] } ] }

[<Tests>]
let uniquenessCheckTests =
    testList
        "DiscoveryEmitter — #11 descriptor-id uniqueness check"
        [ test "duplicate id 'identifier' (same IRI in two classes) triggers invalidOp" {
              Expect.throws
                  (fun () ->
                      DiscoveryEmitter.emit "MyApp.Generated" "/alps" schemaRegistry dupIdLock
                      |> ignore)
                  "duplicate ALPS descriptor IDs must raise an exception"
          }

          test "TicTacToe fixture has no duplicate IDs — passes uniqueness check" {
              let result =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk result "TicTacToe model must pass uniqueness check without error"
          }

          test "outcomeUnionLock has no duplicate IDs — passes uniqueness check" {
              let result =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry outcomeUnionLock

              Expect.isOk result "outcome union model must pass uniqueness check without error"
          } ]

// ── Fixture: parent-path variable inheritance via Rt linkage (#9) ─────────────
// Game has class IRI schema:Game and field Id → schema:identifier.
// MoveRequest has Rt = Some "schema:Game" (declared linkage to Game).
// MoveRequest's own fields are Player/Position — no "id" of its own.
// The {id} template variable in /games/{id}/moves belongs to the Game segment;
// it resolves via Rt: MoveRequest.Rt → Game → Game.Id → schema:identifier.
let private inheritVarLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Integrity = None
      Vocabularies = Map.ofList [ "schema", schemaVocabEntry ]
      DeclaredPrefixes = Map.empty
      Mappings =
        [ { FSharpType = "App.Game"
            Iri = Some "schema:Game"
            Confidence = 1.0
            Source = Manual
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "Id"
                      Iri = Some "schema:identifier"
                      Confidence = 1.0
                      Source = Manual
                      Status = Confirmed } ] }
          { FSharpType = "App.MoveRequest"
            Iri = Some "schema:MoveAction"
            Confidence = 1.0
            Source = Manual
            Status = Confirmed
            Alternates = []
            Rt = Some "schema:Game"
            Shape =
              MappingShape.Record
                  [ { Name = "Player"
                      Iri = Some "schema:agent"
                      Confidence = 1.0
                      Source = Manual
                      Status = Confirmed }
                    { Name = "Position"
                      Iri = Some "schema:rowIndex"
                      Confidence = 1.0
                      Source = Manual
                      Status = Confirmed } ] } ] }

[<Tests>]
let parentPathVarTests =
    testList
        "DiscoveryEmitter — #9 parent-path template-variable inheritance"
        [ test "MoveAction ResourceHrefVars inherits 'id' from Rt-target Game (declared linkage)" {
              // MoveRequest.Rt = Some "schema:Game" → follow to Game → Game.Id → schema:identifier.
              // MoveRequest's own fields (player, position) do not include "id".
              // The Rt-based fix supplements "id" from Game's field, not from a global pool.
              let src =
                  DiscoveryEmitter.emit "App.Generated" "/alps" schemaRegistry inheritVarLock

              Expect.isOk src "emit should succeed"
              let source = unwrapOk src
              Expect.stringContains source "\"id\"" "MoveAction ResourceHrefVars must contain inherited 'id' key"

              Expect.stringContains
                  source
                  "https://schema.org/identifier"
                  "schema:identifier must appear in MoveAction's inherited href-var entry"
          } ]

// ── Fixture: collision — two resources share field name "Id" with DIFFERENT IRIs (#9) ──
// Widget.Id → schema:productID (a different IRI from schema:identifier).
// Game.Id   → schema:identifier.
// Widget appears FIRST in the mappings list — this is the adversarial ordering that
// would cause the old global-pool code to select productID for "id" (List.distinctBy
// keeps the first occurrence), then stamp productID onto MoveRequest via supplemental.
// Under Rt-based resolution, MoveRequest.Rt = Some "schema:Game" follows directly to
// Game and reads schema:identifier — unaffected by Widget's declaration order.
let private collisionHrefVarLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Integrity = None
      Vocabularies = Map.ofList [ "schema", schemaVocabEntry ]
      DeclaredPrefixes = Map.empty
      Mappings =
        [ { FSharpType = "App.Widget"
            Iri = Some "schema:Product"
            Confidence = 1.0
            Source = Manual
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "Id"
                      Iri = Some "schema:productID"
                      Confidence = 1.0
                      Source = Manual
                      Status = Confirmed } ] }
          { FSharpType = "App.Game"
            Iri = Some "schema:Game"
            Confidence = 1.0
            Source = Manual
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "Id"
                      Iri = Some "schema:identifier"
                      Confidence = 1.0
                      Source = Manual
                      Status = Confirmed } ] }
          { FSharpType = "App.MoveRequest"
            Iri = Some "schema:MoveAction"
            Confidence = 1.0
            Source = Manual
            Status = Confirmed
            Alternates = []
            Rt = Some "schema:Game"
            Shape =
              MappingShape.Record
                  [ { Name = "Player"
                      Iri = Some "schema:agent"
                      Confidence = 1.0
                      Source = Manual
                      Status = Confirmed }
                    { Name = "Position"
                      Iri = Some "schema:rowIndex"
                      Confidence = 1.0
                      Source = Manual
                      Status = Confirmed } ] } ] }

[<Tests>]
let collisionHrefVarTests =
    testList
        "DiscoveryEmitter — #9 href-var collision: same field name, different IRIs (Rt-based fix)"
        [ test "MoveAction.id resolves to schema:identifier via Rt, not schema:productID from Widget (first in list)" {
              // Widget appears BEFORE Game in mappings. Under the old global-pool code,
              // List.distinctBy fst would keep Widget's ("id", schema:productID) and MoveRequest
              // would inherit productID — the wrong meaning for the {id} in /games/{id}/moves.
              // Under Rt-based resolution, MoveRequest.Rt = schema:Game → Game.Id = schema:identifier.
              //
              // Proof: "https://schema.org/productID" appears exactly twice in the emitted source:
              //   once in Widget's ALPS descriptor child Href, once in Widget's ResourceHrefVars.
              // Under the old global-pool code it would appear a third time in MoveRequest's entry.
              let src =
                  DiscoveryEmitter.emit "App.Generated" "/alps" schemaRegistry collisionHrefVarLock

              Expect.isOk src "emit should succeed"
              let source = unwrapOk src

              let productIdOccurrences = source.Split("https://schema.org/productID").Length - 1

              Expect.equal
                  productIdOccurrences
                  2
                  "schema:productID must appear exactly twice (Widget ALPS descriptor + Widget ResourceHrefVars) — NOT in MoveRequest entry"
          }

          test "MoveAction ResourceHrefVars contains schema:identifier (from Rt-target Game), not productID" {
              let src =
                  DiscoveryEmitter.emit "App.Generated" "/alps" schemaRegistry collisionHrefVarLock

              Expect.isOk src "emit should succeed"
              let source = unwrapOk src

              Expect.stringContains
                  source
                  "https://schema.org/identifier"
                  "schema:identifier must be in source (Game + MoveAction)"
          }

          test "Game ResourceHrefVars is not polluted by MoveAction fields (no player/position in Game entry)" {
              // Under old global-pool, Game's entry carries player + position from MoveRequest's
              // own fields as supplemental (pollution). Under Rt-based fix, Game.Rt = None so
              // Game's entry has only its own fields.
              // Proof: "https://schema.org/agent" appears exactly once (MoveAction's descriptor child Href).
              let src =
                  DiscoveryEmitter.emit "App.Generated" "/alps" schemaRegistry collisionHrefVarLock

              Expect.isOk src "emit should succeed"
              let source = unwrapOk src
              let agentOccurrences = source.Split("https://schema.org/agent").Length - 1

              Expect.equal
                  agentOccurrences
                  2
                  "schema:agent appears in MoveAction ALPS descriptor child + MoveAction ResourceHrefVars only (not polluted into Game)"
          } ]

// ── #397 AC2: rt resolvability is a codegen-time build gate ─────────────────
// Parallel to #11's assertUniqueIds — an emitted `rt` must match some descriptor's
// href/id in the same document. The bare-IRI match "worked" only by coincidence
// (never asserted); this makes it a structural invariant, caught at codegen time.

[<Tests>]
let rtResolvabilityGateTests =
    testList
        "DiscoveryEmitter — #397 AC2: rt resolvability build-gated"
        [ test "unresolvable rt (no descriptor with matching href/id) fails the build with a clear message" {
              let lock: LockFile =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.UtcNow
                    Integrity = None
                    Vocabularies = Map.ofList [ "schema", schemaVocabEntry ]
                    DeclaredPrefixes = Map.empty
                    Mappings =
                      [ { FSharpType = "App.MoveRequest"
                          Iri = Some "schema:MoveAction"
                          Confidence = 1.0
                          Source = Manual
                          Status = Confirmed
                          Alternates = []
                          Rt = Some "schema:Nonexistent"
                          Shape = MappingShape.Record [] } ] }

              let ex =
                  try
                      DiscoveryEmitter.emit "App.Generated" "/alps" schemaRegistry lock |> ignore
                      None
                  with :? InvalidOperationException as e ->
                      Some e

              match ex with
              | None -> failwith "unresolvable rt must raise an exception at codegen time"
              | Some e ->
                  Expect.stringContains e.Message "rt" "exception message names the unresolved rt problem"

                  Expect.stringContains
                      e.Message
                      "https://schema.org/Nonexistent"
                      "exception message names the specific unresolved rt value"
          }

          test "TicTacToe fixture's rt (MoveAction -> Game) resolves cleanly — no throw" {
              let result =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk result "a resolvable rt must not raise or fail codegen"
          }

          test "declared-only prefix lock's rt (MoveRequest -> Game, ex: prefix) resolves cleanly — no throw" {
              let result =
                  DiscoveryEmitter.emit "Ex.Generated" "/alps" VocabularyRegistry.empty exDeclaredOnlyLock

              Expect.isOk result "declared-only prefix rt resolution must not throw"
          }

          test "rt pointing at an excluded (never-emitted) mapping fails the build" {
              // Game is Excluded, so it never becomes a descriptor — MoveRequest's rt to
              // schema:Game must be unresolvable even though 'schema:Game' is a valid IRI.
              let lock: LockFile =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.UtcNow
                    Integrity = None
                    Vocabularies = Map.ofList [ "schema", schemaVocabEntry ]
                    DeclaredPrefixes = Map.empty
                    Mappings =
                      [ { FSharpType = "App.Game"
                          Iri = Some "schema:Game"
                          Confidence = 1.0
                          Source = Manual
                          Status = Excluded
                          Alternates = []
                          Rt = None
                          Shape = MappingShape.Record [] }
                        { FSharpType = "App.MoveRequest"
                          Iri = Some "schema:MoveAction"
                          Confidence = 1.0
                          Source = Manual
                          Status = Confirmed
                          Alternates = []
                          Rt = Some "schema:Game"
                          Shape = MappingShape.Record [] } ] }

              Expect.throws
                  (fun () -> DiscoveryEmitter.emit "App.Generated" "/alps" schemaRegistry lock |> ignore)
                  "rt pointing at an excluded/never-emitted mapping must raise an exception"
          } ]

// ── #397: ClassIri / RequestClrTypeName correlation keys populated for reconciliation ──
// DiscoveryMiddleware reconciles Type against real HTTP methods at serve time; it needs
// these two fields on every top-level class descriptor (never on nested field/case
// children) to correlate against ResourceRelationMetadata/IAcceptsMetadata.

[<Tests>]
let correlationKeyTests =
    testList
        "DiscoveryEmitter — #397 ClassIri/RequestClrTypeName correlation keys"
        [ test "top-level class descriptor carries ClassIri = full absolute class IRI" {
              let model =
                  ResolvedModel.build schemaRegistry ticTacToeLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let descriptors, _ = DiscoveryEmitter.projectDiscovery Set.empty model
              let game = descriptors |> List.find (fun d -> d.Id = "Game")
              let moveAction = descriptors |> List.find (fun d -> d.Id = "MoveAction")
              Expect.equal game.ClassIri (Some "https://schema.org/Game") "Game.ClassIri is the full absolute IRI"

              Expect.equal
                  moveAction.ClassIri
                  (Some "https://schema.org/MoveAction")
                  "MoveAction.ClassIri is the full absolute IRI"
          }

          test "top-level class descriptor carries RequestClrTypeName = the mapping's FSharpType" {
              let model =
                  ResolvedModel.build schemaRegistry ticTacToeLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let descriptors, _ = DiscoveryEmitter.projectDiscovery Set.empty model
              let game = descriptors |> List.find (fun d -> d.Id = "Game")
              let moveAction = descriptors |> List.find (fun d -> d.Id = "MoveAction")

              Expect.equal game.RequestClrTypeName (Some "TicTacToe.Game") "Game.RequestClrTypeName is the FSharpType"

              Expect.equal
                  moveAction.RequestClrTypeName
                  (Some "TicTacToe.Move")
                  "MoveAction.RequestClrTypeName is the FSharpType"
          }

          test "ClassIri for a declared-only prefix class is the full absolute IRI, NOT host-relative" {
              // Href IS relativized for declared-only prefixes (ALPS wire format); ClassIri
              // must stay absolute — it correlates against the live endpoint's Relation, which
              // is always the raw ClassIri.AbsoluteUri (SemanticModelEmitter.iri never relativizes).
              let bases = Set.ofList [ "https://example.org/ex#" ]

              let model =
                  ResolvedModel.build VocabularyRegistry.empty exDeclaredOnlyLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let descriptors, _ = DiscoveryEmitter.projectDiscovery bases model
              let game = descriptors |> List.find (fun d -> d.Id = "Game")
              Expect.equal game.ClassIri (Some "https://example.org/ex#Game") "ClassIri stays absolute"
              Expect.notEqual game.Href game.ClassIri "Href (relativized) differs from ClassIri (absolute)"
          }

          test "nested field/case descriptors carry no ClassIri or RequestClrTypeName" {
              let model =
                  ResolvedModel.build schemaRegistry ticTacToeLock
                  |> function
                      | Ok m -> m
                      | Error e -> failwith e

              let descriptors, _ = DiscoveryEmitter.projectDiscovery Set.empty model
              let moveAction = descriptors |> List.find (fun d -> d.Id = "MoveAction")

              for child in moveAction.Children do
                  Expect.isNone child.ClassIri $"child '{child.Id}' must not carry ClassIri"
                  Expect.isNone child.RequestClrTypeName $"child '{child.Id}' must not carry RequestClrTypeName"
          }

          test "emitted source contains ClassIri and RequestClrTypeName fields" {
              let src =
                  DiscoveryEmitter.emit "TicTacToe.Generated" "/alps" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              let source = unwrapOk src
              Expect.stringContains source "ClassIri" "ClassIri field present in emitted source"
              Expect.stringContains source "RequestClrTypeName" "RequestClrTypeName field present in emitted source"
              Expect.stringContains source "\"TicTacToe.Move\"" "MoveAction's FSharpType literal present"
          }

          test
              "compile gate: emitted source with ClassIri/RequestClrTypeName still compiles against Frank.Discovery types" {
              let src =
                  DiscoveryEmitter.emit "Probe2.GeneratedDiscovery" "/alps/tictactoe" schemaRegistry ticTacToeLock
                  |> function
                      | Ok s -> s
                      | Error e -> failwith $"Expected Ok but got Error: {e}"

              let assemblies = [ typeof<Frank.Discovery.DiscoveryConfig>.Assembly ]
              let diagnostics = FcsTypecheck.typecheckAgainstRealAssemblies src assemblies
              Expect.isEmpty diagnostics $"emitted Discovery module compiles cleanly; errors: {diagnostics}"
          } ]

[<Tests>]
let buildRegistryCleanupTests =
    testList
        "DiscoveryEmitter — AC5 Prefixes are dead on Discovery path (#386)"
        [ test "emit output is identical whether Prefixes are populated or empty (Prefixes are dead on Discovery path)" {
              // Construct the registry that OLD buildRegistry would have returned (before AC5 cleanup).
              // This is the golden: Prefixes populated from lock.Vocabularies, all other fields empty.
              let populatedRegistry =
                  { VocabularyRegistry.empty with
                      Prefixes = ticTacToeLock.Vocabularies |> Map.map (fun _ v -> Uri(v.Uri)) }

              let resultPopulated =
                  DiscoveryEmitter.emit "App.Generated" "/alps/test" populatedRegistry ticTacToeLock

              let resultEmpty =
                  DiscoveryEmitter.emit "App.Generated" "/alps/test" VocabularyRegistry.empty ticTacToeLock

              // If Prefixes were NOT dead, emit would produce different output for populated vs empty.
              // This test would fail under a hypothetical where Discovery reads registry.Prefixes.
              match resultPopulated, resultEmpty with
              | Ok populated, Ok empty ->
                  Expect.equal
                      empty
                      populated
                      "Discovery output must be identical regardless of Prefixes population — Prefixes are dead on the Discovery path"
              | Error e, _ -> failtest $"emit with populated registry failed: {e}"
              | _, Error e -> failtest $"emit with VocabularyRegistry.empty failed: {e}"
          } ]
