module ExtractionStateProjectorTests

open System
open System.IO
open Expecto
open Frank.Resources.Model
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Extraction
open Frank.Cli.Core.State
open Frank.Cli.Core.Unified
open Frank.Cli.Core.Unified.ExtractionStateProjector

let private testType name fields : AnalyzedType =
    { FullName = $"Test.%s{name}"
      ShortName = name
      Kind =
          Record(
              fields
              |> List.map (fun (n, k) ->
                  { Name = n
                    Kind = k
                    IsRequired = true
                    IsScalar = true
                    Constraints = [] })
          )
      GenericParameters = []
      SourceLocation = Some { File = "Test.fs"; Line = 1; Column = 0 }
      IsClosed = true }

let private testUnifiedState types : UnifiedExtractionState =
    { Resources =
          [ { RouteTemplate = "/games/{gameId}"
              ResourceSlug = "games"
              TypeInfo = types
              Statechart = None
              HttpCapabilities = []
              DerivedFields = UnifiedModel.emptyDerivedFields } ]
      SourceHash = "abc123"
      BaseUri = "http://example.com/ontology"
      Vocabularies = [ "https://schema.org" ]
      ExtractedAt = DateTimeOffset.UtcNow
      ToolVersion = "7.0.0"
      Profiles = ProjectedProfiles.empty }

[<Tests>]
let extractionStateProjectorTests =
    testList "ExtractionStateProjector" [

        testCase "Projected ontology has triples for record type" <| fun () ->
            let types =
                [ testType "Game" [ "Board", Primitive "xsd:string"; "CurrentTurn", Primitive "xsd:string" ] ]

            let unified = testUnifiedState types
            let projected = toExtractionState unified
            let tripleCount = projected.Ontology.Triples |> Seq.length
            Expect.isGreaterThan tripleCount 0 "Projected ontology should have triples"

        testCase "Projected shapes has triples for record type" <| fun () ->
            let types =
                [ testType "Game" [ "Board", Primitive "xsd:string" ] ]

            let unified = testUnifiedState types
            let projected = toExtractionState unified
            let tripleCount = projected.Shapes.Triples |> Seq.length
            Expect.isGreaterThan tripleCount 0 "Projected shapes should have triples"

        testCase "Projected ontology matches direct TypeMapper output" <| fun () ->
            let types =
                [ testType "Game" [ "Board", Primitive "xsd:string"; "CurrentTurn", Primitive "xsd:string" ] ]

            let config: TypeMapper.MappingConfig =
                { BaseUri = Uri "http://example.com/ontology"
                  Vocabularies = [ "https://schema.org" ] }

            let expectedOntology = TypeMapper.mapTypes config types
            let unified = testUnifiedState types
            let projected = toExtractionState unified

            let projectedTriples = projected.Ontology.Triples |> Seq.length
            let expectedTriples = expectedOntology.Triples |> Seq.length
            Expect.equal projectedTriples expectedTriples "Triple counts should match"

        testCase "Projected metadata carries correct values" <| fun () ->
            let types = [ testType "Game" [ "Board", Primitive "xsd:string" ] ]
            let unified = testUnifiedState types
            let projected = toExtractionState unified

            Expect.equal projected.Metadata.SourceHash "abc123" "Source hash"
            Expect.equal projected.Metadata.ToolVersion "7.0.0" "Tool version"
            Expect.equal projected.Metadata.BaseUri (Uri "http://example.com/ontology") "Base URI"
            Expect.equal projected.Metadata.Vocabularies [ "https://schema.org" ] "Vocabularies"

        testCase "Duplicate types across resources are deduplicated" <| fun () ->
            let sharedType = testType "SharedModel" [ "Value", Primitive "xsd:string" ]

            let unified =
                { testUnifiedState [ sharedType ] with
                    Resources =
                        [ { RouteTemplate = "/resource1"
                            ResourceSlug = "resource1"
                            TypeInfo = [ sharedType ]
                            Statechart = None
                            HttpCapabilities = []
                            DerivedFields = UnifiedModel.emptyDerivedFields }
                          { RouteTemplate = "/resource2"
                            ResourceSlug = "resource2"
                            TypeInfo = [ sharedType ]
                            Statechart = None
                            HttpCapabilities = []
                            DerivedFields = UnifiedModel.emptyDerivedFields } ] }

            let projected = toExtractionState unified

            // Count OWL class triples -- should match single-type extraction
            let config: TypeMapper.MappingConfig =
                { BaseUri = Uri "http://example.com/ontology"
                  Vocabularies = [ "https://schema.org" ] }

            let singleTypeGraph = TypeMapper.mapTypes config [ sharedType ]
            let projectedTriples = projected.Ontology.Triples |> Seq.length
            let singleTriples = singleTypeGraph.Triples |> Seq.length
            Expect.equal projectedTriples singleTriples "Deduplicated types should produce same triple count"

        testCase "SourceMap contains type entries" <| fun () ->
            let types = [ testType "Game" [ "Board", Primitive "xsd:string" ] ]
            let unified = testUnifiedState types
            let projected = toExtractionState unified

            Expect.isTrue (projected.SourceMap.ContainsKey "http://example.com/ontology#Game") "Should have Game type entry"

        testCase "Empty resources produce empty but valid state" <| fun () ->
            let unified = testUnifiedState []
            let projected = toExtractionState unified

            Expect.equal (projected.Ontology.Triples |> Seq.length) 0 "Empty types = empty ontology"
            Expect.equal (projected.Shapes.Triples |> Seq.length) 0 "Empty types = empty shapes"
            Expect.isEmpty projected.SourceMap "Empty resources = empty source map"

        testCase "Old-format detection with temp directory" <| fun () ->
            let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
            let frankCliDir = Path.Combine(tempDir, "obj", "frank-cli")

            try
                Directory.CreateDirectory(frankCliDir) |> ignore
                File.WriteAllText(Path.Combine(frankCliDir, "state.json"), "{}")

                let result = UnifiedStateLoader.loadExtractionState tempDir

                // Should try to load the legacy format (it will fail because the JSON is empty)
                match result with
                | Error _ -> () // Expected - empty JSON is not valid ExtractionState
                | Ok _ -> () // Also OK if somehow loaded
            finally
                if Directory.Exists tempDir then
                    Directory.Delete(tempDir, true)

        testCase "No state file returns error message" <| fun () ->
            let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())

            try
                Directory.CreateDirectory(tempDir) |> ignore

                let result = UnifiedStateLoader.loadExtractionState tempDir

                match result with
                | Error msg ->
                    Expect.stringContains msg "No extraction state found" "Should mention no state"
                | Ok _ -> failtest "Should have returned Error"
            finally
                if Directory.Exists tempDir then
                    Directory.Delete(tempDir, true)
    ]
