module Frank.Cli.Core.Tests.LinkedDataEmitterTests

open System
open Expecto
open FsCheck
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core

// ── Test fixtures ─────────────────────────────────────────────────────────────

let private schemaPrefix = Uri("https://schema.org/")
let private wikidataPrefix = Uri("https://www.wikidata.org/wiki/")

let private schemaRegistry: VocabularyRegistry =
    { VocabularyRegistry.empty with
        Prefixes = Map.ofList [ "schema", schemaPrefix; "wikidata", wikidataPrefix ]
        Using = Set.ofList [ "schema" ]
        SeeAlso =
            Map.ofList
                [ "TicTacToe.Game", [ Uri("https://www.wikidata.org/wiki/Q11907") ]
                  "TicTacToe.Move", [ Uri("https://www.wikidata.org/wiki/Q2140226") ] ]
        EquivalentClasses = Map.ofList [ "TicTacToe.Game", Uri("https://schema.org/GamePlayMode") ] }

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
      Mappings = [ mapping ] }

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
            SourceFiles = [| "GeneratedLinkedData.fs" |] }

    let result =
        checker.ParseFile("GeneratedLinkedData.fs", sourceText, opts)
        |> Async.RunSynchronously

    not result.ParseHadErrors

// ── Generators for FsCheck properties ────────────────────────────────────────

let private genMappingSource = Gen.elements [ Convention; Llm; Manual ]

let private genMappingStatus = Gen.elements [ Confirmed; Proposed; Unresolved ]

let private genFieldWithSchemaIri =
    gen {
        let! name =
            Arb.generate<NonEmptyString>
            |> Gen.map (fun (NonEmptyString s) -> s.Replace(":", "").Replace(".", ""))

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
        let! localName =
            Arb.generate<NonEmptyString>
            |> Gen.map (fun (NonEmptyString s) -> s.Replace(":", "").Replace(".", ""))

        let fsType = "My." + localName
        let! hasIri = Gen.elements [ true; false ]
        let iri = if hasIri then Some $"schema:{localName}" else None
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
              Mappings = mappings }
    }

// Generate a registry with random SeeAlso sets for types appearing in a lock.
let private genRegistryWithSeeAlso (mappings: Mapping list) =
    gen {
        let typesWithIri =
            mappings |> List.choose (fun m -> m.Iri |> Option.map (fun _ -> m.FSharpType))

        let! seeAlsoEntries =
            typesWithIri
            |> List.map (fun fsType ->
                gen {
                    let! count = Gen.choose (0, 2)

                    let! uris =
                        Gen.listOfLength
                            count
                            (gen {
                                let! n = Gen.choose (1, 999)
                                return Uri($"https://example.org/q{n}")
                            })

                    return fsType, uris
                })
            |> Gen.sequence

        let seeAlsoMap =
            seeAlsoEntries |> List.filter (fun (_, uris) -> uris <> []) |> Map.ofList

        return
            { VocabularyRegistry.empty with
                Prefixes = Map.ofList [ "schema", Uri("https://schema.org/") ]
                Using = Set.ofList [ "schema" ]
                SeeAlso = seeAlsoMap }
    }

// Generate a registry with random EquivalentClass entries.
let private genRegistryWithEquivClass (mappings: Mapping list) =
    gen {
        let typesWithIri =
            mappings |> List.choose (fun m -> m.Iri |> Option.map (fun _ -> m.FSharpType))

        let! equivEntries =
            typesWithIri
            |> List.map (fun fsType ->
                gen {
                    let! hasEquiv = Arb.generate<bool>

                    if hasEquiv then
                        let! n = Gen.choose (1, 999)
                        return Some(fsType, Uri($"https://owl.example.org/c{n}"))
                    else
                        return None
                })
            |> Gen.sequence

        let equivMap = equivEntries |> List.choose id |> Map.ofList

        return
            { VocabularyRegistry.empty with
                Prefixes = Map.ofList [ "schema", Uri("https://schema.org/") ]
                Using = Set.ofList [ "schema" ]
                EquivalentClasses = equivMap }
    }

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let noUrnFrankTests =
    testList
        "LinkedDataEmitter — no urn:frank:"
        [ testPropertyWithConfig
              { FsCheckConfig.defaultConfig with
                  maxTest = 50 }
              "emitted source never contains 'urn:frank:'"
              (Prop.forAll (Arb.fromGen genLockWithSchemaIris) (fun lock ->
                  match LinkedDataEmitter.emit "Test.Generated" schemaRegistry lock with
                  | Error _ -> true
                  | Ok src -> not (src.Contains("urn:frank:")))) ]

[<Tests>]
let contextTests =
    testList
        "LinkedDataEmitter — @context contains external base URIs"
        [ test "using 'schema' → jsonLdContext contains https://schema.org" {
              let src =
                  LinkedDataEmitter.emit "TicTacToe.GeneratedLinkedData" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://schema.org" "@context has schema.org base"
          }

          test "jsonLdContext does not contain urn:" {
              let src =
                  LinkedDataEmitter.emit "TicTacToe.GeneratedLinkedData" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.isFalse ((unwrapOk src).Contains("urn:")) "no urn: in @context"
          }

          testPropertyWithConfig
              { FsCheckConfig.defaultConfig with
                  maxTest = 50 }
              "for any lock with schema IRIs, context contains external URI for each using prefix"
              (Prop.forAll (Arb.fromGen genLockWithSchemaIris) (fun lock ->
                  match LinkedDataEmitter.emit "Test.Generated" schemaRegistry lock with
                  | Error _ -> true
                  | Ok src -> src.Contains("https://schema.org"))) ]

[<Tests>]
let seeAlsoCompletenessTests =
    testList
        "LinkedDataEmitter — seeAlso completeness"
        [ test "TicTacToe.Game seeAlso wikidata:Q11907 present" {
              let src =
                  LinkedDataEmitter.emit "TicTacToe.GeneratedLinkedData" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://www.wikidata.org/wiki/Q11907" "Game seeAlso IRI"
          }

          test "TicTacToe.Move seeAlso wikidata:Q2140226 present" {
              let src =
                  LinkedDataEmitter.emit "TicTacToe.GeneratedLinkedData" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://www.wikidata.org/wiki/Q2140226" "Move seeAlso IRI"
          }

          test "seeAlso uses rdfs:seeAlso predicate" {
              let src =
                  LinkedDataEmitter.emit "TicTacToe.GeneratedLinkedData" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "rdfs:seeAlso" "rdfs:seeAlso predicate"
          }

          testPropertyWithConfig
              { FsCheckConfig.defaultConfig with
                  maxTest = 30 }
              "all seeAlso IRIs in registry appear in emitted source for types in lock"
              (Prop.forAll (Arb.fromGen genLockWithSchemaIris) (fun lock ->
                  gen { return! genRegistryWithSeeAlso lock.Mappings }
                  |> Gen.sample 0 1
                  |> List.head
                  |> fun reg ->
                      match LinkedDataEmitter.emit "Test.Generated" reg lock with
                      | Error _ -> true
                      | Ok src ->
                          let typesInLock = lock.Mappings |> List.map (fun m -> m.FSharpType) |> Set.ofList

                          reg.SeeAlso
                          |> Map.forall (fun fsType uris ->
                              if not (Set.contains fsType typesInLock) then
                                  true
                              else
                                  uris |> List.forall (fun u -> src.Contains(u.AbsoluteUri))))) ]

[<Tests>]
let equivalentClassCompletenessTests =
    testList
        "LinkedDataEmitter — equivalentClass completeness"
        [ test "TicTacToe.Game equivalentClass schema:GamePlayMode present" {
              let src =
                  LinkedDataEmitter.emit "TicTacToe.GeneratedLinkedData" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://schema.org/GamePlayMode" "equivalentClass IRI"
          }

          test "owl:equivalentClass predicate present" {
              let src =
                  LinkedDataEmitter.emit "TicTacToe.GeneratedLinkedData" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "owl:equivalentClass" "owl:equivalentClass predicate"
          }

          testPropertyWithConfig
              { FsCheckConfig.defaultConfig with
                  maxTest = 30 }
              "all equivalentClass IRIs in registry appear in emitted source for types in lock"
              (Prop.forAll (Arb.fromGen genLockWithSchemaIris) (fun lock ->
                  gen { return! genRegistryWithEquivClass lock.Mappings }
                  |> Gen.sample 0 1
                  |> List.head
                  |> fun reg ->
                      match LinkedDataEmitter.emit "Test.Generated" reg lock with
                      | Error _ -> true
                      | Ok src ->
                          let typesInLock = lock.Mappings |> List.map (fun m -> m.FSharpType) |> Set.ofList

                          reg.EquivalentClasses
                          |> Map.forall (fun fsType equivUri ->
                              if not (Set.contains fsType typesInLock) then
                                  true
                              else
                                  src.Contains(equivUri.AbsoluteUri)))) ]

[<Tests>]
let subjectCorrectnessTests =
    testList
        "LinkedDataEmitter — subject is lock-mapped vocab IRI"
        [ test "Game subject is schema.org/Game not urn:frank:" {
              let src =
                  LinkedDataEmitter.emit "TicTacToe.GeneratedLinkedData" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              let text = unwrapOk src
              Expect.stringContains text "https://schema.org/Game" "Game vocab IRI appears as subject"
              Expect.isFalse (text.Contains("urn:frank:")) "no urn:frank: anywhere"
          }

          test "Move subject is schema.org/MoveAction" {
              let src =
                  LinkedDataEmitter.emit "TicTacToe.GeneratedLinkedData" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "https://schema.org/MoveAction" "MoveAction vocab IRI"
          }

          test "field subjects are resolved field IRIs" {
              let src =
                  LinkedDataEmitter.emit "TicTacToe.GeneratedLinkedData" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              let text = unwrapOk src
              Expect.stringContains text "https://schema.org/identifier" "identifier field IRI"
              Expect.stringContains text "https://schema.org/rowIndex" "rowIndex field IRI"
          }

          test "rdf:type owl:Class assertion present" {
              let src =
                  LinkedDataEmitter.emit "TicTacToe.GeneratedLinkedData" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "owl:Class" "owl:Class type assertion"
          }

          test "rdf:type rdf:Property assertion for fields" {
              let src =
                  LinkedDataEmitter.emit "TicTacToe.GeneratedLinkedData" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "rdf:Property" "rdf:Property type assertion for fields"
          }

          test "rdfs:domain assertion links field to class" {
              let src =
                  LinkedDataEmitter.emit "TicTacToe.GeneratedLinkedData" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.stringContains (unwrapOk src) "rdfs:domain" "rdfs:domain assertion"
          } ]

[<Tests>]
let determinismTests =
    testList
        "LinkedDataEmitter — determinism"
        [ test "TicTacToe fixture emitted twice is byte-identical" {
              let src1 =
                  LinkedDataEmitter.emit "TicTacToe.GeneratedLinkedData" schemaRegistry ticTacToeLock

              let src2 =
                  LinkedDataEmitter.emit "TicTacToe.GeneratedLinkedData" schemaRegistry ticTacToeLock

              Expect.equal src1 src2 "deterministic output"
          }

          testPropertyWithConfig
              { FsCheckConfig.defaultConfig with
                  maxTest = 20 }
              "emit same inputs twice always produces identical result"
              (Prop.forAll (Arb.fromGen genLockWithSchemaIris) (fun lock ->
                  let r1 = LinkedDataEmitter.emit "Test.Generated" schemaRegistry lock
                  let r2 = LinkedDataEmitter.emit "Test.Generated" schemaRegistry lock
                  r1 = r2)) ]

[<Tests>]
let parsesTests =
    testList
        "LinkedDataEmitter — emitted source parses as valid F#"
        [ test "TicTacToe fixture parses" {
              let src =
                  LinkedDataEmitter.emit "TicTacToe.GeneratedLinkedData" schemaRegistry ticTacToeLock

              Expect.isOk src "emit should succeed"
              Expect.isTrue (parsesFsSource (unwrapOk src)) "should parse as valid F#"
          }

          test "minimal single-type lock parses" {
              let lock =
                  minimalLock
                      { FSharpType = "My.Foo"
                        Iri = Some "schema:Foo"
                        Confidence = 1.0
                        Source = Convention
                        Status = Confirmed
                        Alternates = []
                        Shape =
                          MappingShape.Record
                              [ { Name = "bar"
                                  Iri = Some "schema:bar"
                                  Confidence = 1.0
                                  Source = Convention
                                  Status = Confirmed } ] }

              let reg =
                  { VocabularyRegistry.empty with
                      Prefixes = Map.ofList [ "schema", Uri("https://schema.org/") ]
                      Using = Set.ofList [ "schema" ] }

              let src = LinkedDataEmitter.emit "My.GeneratedLinkedData" reg lock
              Expect.isOk src "emit should succeed"
              Expect.isTrue (parsesFsSource (unwrapOk src)) "should parse as valid F#"
          } ]

[<Tests>]
let excludedMappingTests =
    testList
        "LinkedDataEmitter — Excluded mappings generate nothing"
        [ test "excluded mapping IRI absent; confirmed mapping IRI present" {
              let twoMappingLock: LockFile =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.UtcNow
                    Vocabularies = Map.ofList [ "schema", schemaVocabEntry ]
                    Mappings =
                      [ { FSharpType = "MyApp.Game"
                          Iri = Some "schema:Game"
                          Confidence = 1.0
                          Source = Convention
                          Status = Confirmed
                          Alternates = []
                          Shape = MappingShape.Record [] }
                        { FSharpType = "MyApp.Player"
                          Iri = Some "schema:Player"
                          Confidence = 0.9
                          Source = Convention
                          Status = Excluded
                          Alternates = []
                          Shape = MappingShape.Record [] } ] }

              let reg =
                  { VocabularyRegistry.empty with
                      Prefixes = Map.ofList [ "schema", Uri("https://schema.org/") ]
                      Using = Set.ofList [ "schema" ] }

              let result = LinkedDataEmitter.emit "MyApp.GeneratedLinkedData" reg twoMappingLock
              Expect.isOk result "emit should succeed"
              let source = unwrapOk result
              Expect.stringContains source "https://schema.org/Game" "confirmed Game IRI present"
              Expect.isFalse (source.Contains("https://schema.org/Player")) "excluded Player IRI absent"
          } ]

[<Tests>]
let prefixResolutionTests =
    testList
        "LinkedDataEmitter — prefix resolution"
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
                        Shape = MappingShape.Record [] }

              let result = LinkedDataEmitter.emit "My.Generated" noRegistry lockWithUnknown
              Expect.isError result "unknown prefix must return Error"
          }

          test "mapping with None IRI is skipped without error" {
              let reg =
                  { VocabularyRegistry.empty with
                      Prefixes = Map.ofList [ "schema", Uri("https://schema.org/") ]
                      Using = Set.ofList [ "schema" ] }

              let lockWithNoneIri =
                  minimalLock
                      { FSharpType = "My.Unresolved"
                        Iri = None
                        Confidence = 0.0
                        Source = Convention
                        Status = Unresolved
                        Alternates = []
                        Shape = MappingShape.Record [] }

              let result = LinkedDataEmitter.emit "My.Generated" reg lockWithNoneIri
              Expect.isOk result "None IRI mapping emits without error"
          } ]
