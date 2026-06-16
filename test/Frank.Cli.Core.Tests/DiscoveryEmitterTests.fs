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
      Mappings =
        [ { FSharpType = "TicTacToe.Game"
            Iri = Some "schema:Game"
            Confidence = 1.0
            Source = Convention
            Status = Confirmed
            Alternates = []
            Fields =
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
            Fields =
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
      Mappings = [ mapping ] }

let private singleTypeLock: LockFile =
    minimalLock
        { FSharpType = "My.Foo"
          Iri = Some "schema:Foo"
          Confidence = 1.0
          Source = Convention
          Status = Confirmed
          Alternates = []
          Fields =
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
              Fields = fields }
    }

let private genLockWithSchemaIris =
    gen {
        let! count = Gen.choose (1, 3)
        let! mappings = Gen.listOfLength count genMappingWithSchemaIri

        return
            { SchemaVersion = 1
              Generated = DateTimeOffset.UtcNow
              Vocabularies = Map.ofList [ "schema", schemaVocabEntry ]
              Mappings = mappings }
    }

// ── Tests ─────────────────────────────────────────────────────────────────────

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
                  |> List.collect (fun m -> m.Fields)
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
                        Fields = [] }

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
                        Fields = [] }

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
