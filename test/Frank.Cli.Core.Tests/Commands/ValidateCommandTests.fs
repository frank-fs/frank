module Frank.Cli.Core.Tests.ValidateCommandTests

open System
open System.Collections.Generic
open System.IO
open Expecto
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.State
open Frank.Cli.Core.Commands.ValidateCommand

let private createTestState
    (ontologySetup: IGraph -> unit)
    (shapesSetup: IGraph -> unit)
    (unmappedTypes: UnmappedType list)
    =
    let ontology = createGraph ()
    ontologySetup ontology
    let shapes = createGraph ()
    shapesSetup shapes

    { Ontology = ontology
      Shapes = shapes
      SourceMap = Dictionary<Uri, SourceLocation>()
      Clarifications = Map.empty
      Metadata =
        { Timestamp = DateTimeOffset.UtcNow
          SourceHash = "abc123"
          ToolVersion = "0.1.0"
          BaseUri = Uri "http://example.org/"
          Vocabularies = [ "schema.org" ] }
      UnmappedTypes = unmappedTypes }

[<Tests>]
let tests =
    testList "ValidateCommand" [
        testCase "valid state with no issues" <| fun _ ->
            let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
            Directory.CreateDirectory(tempDir) |> ignore

            try
                let setupOntology (graph: IGraph) =
                    let rdfType = createUriNode graph (Uri Rdf.Type)
                    let owlClass = createUriNode graph (Uri Owl.Class)
                    let domainNode = createUriNode graph (Uri Rdfs.Domain)
                    let datatypeProp = createUriNode graph (Uri Owl.DatatypeProperty)

                    // Create Product class
                    let productNode = createUriNode graph (Uri "http://example.org/types/Product")
                    assertTriple graph (productNode, rdfType, owlClass)

                    // Create a property with domain pointing to Product
                    let nameNode = createUriNode graph (Uri "http://example.org/properties/name")
                    assertTriple graph (nameNode, rdfType, datatypeProp)
                    assertTriple graph (nameNode, domainNode, productNode)

                let setupShapes (graph: IGraph) =
                    let rdfType = createUriNode graph (Uri Rdf.Type)
                    let nodeShape = createUriNode graph (Uri Shacl.NodeShape)
                    let targetClass = createUriNode graph (Uri Shacl.TargetClass)

                    let shapeNode = createUriNode graph (Uri "http://example.org/shapes/ProductShape")
                    assertTriple graph (shapeNode, rdfType, nodeShape)
                    assertTriple graph (shapeNode, targetClass, createUriNode graph (Uri "http://example.org/types/Product"))

                let state = createTestState setupOntology setupShapes []

                let statePath = ExtractionState.defaultStatePath tempDir
                ExtractionState.save statePath state |> ignore

                let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")
                let result = execute fakeProjectPath

                match result with
                | Error e -> failtest $"Expected Ok but got Error: {e}"
                | Ok r ->
                    Expect.isTrue r.IsValid "Should be valid"
                    Expect.equal r.Issues.Length 0 "Should have no issues"
                    Expect.floatClose Accuracy.medium r.CoveragePercent 100.0 "Coverage should be 100%"
            finally
                if Directory.Exists tempDir then
                    Directory.Delete(tempDir, true)

        testCase "warns when class has no properties" <| fun _ ->
            let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
            Directory.CreateDirectory(tempDir) |> ignore

            try
                let setupOntology (graph: IGraph) =
                    let rdfType = createUriNode graph (Uri Rdf.Type)
                    let owlClass = createUriNode graph (Uri Owl.Class)

                    // Class with no properties
                    let orphanNode = createUriNode graph (Uri "http://example.org/types/Orphan")
                    assertTriple graph (orphanNode, rdfType, owlClass)

                let state = createTestState setupOntology ignore []

                let statePath = ExtractionState.defaultStatePath tempDir
                ExtractionState.save statePath state |> ignore

                let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")
                let result = execute fakeProjectPath

                match result with
                | Error e -> failtest $"Expected Ok but got Error: {e}"
                | Ok r ->
                    Expect.isTrue r.IsValid "Warnings should not make it invalid"
                    let warnings = r.Issues |> List.filter (fun i -> i.Severity = "warning")
                    Expect.isGreaterThanOrEqual warnings.Length 1 "Should have at least 1 warning about no properties"
                    Expect.stringContains warnings.[0].Message "no properties" "Should mention no properties"
            finally
                if Directory.Exists tempDir then
                    Directory.Delete(tempDir, true)

        testCase "errors when shape targets non-existent class" <| fun _ ->
            let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
            Directory.CreateDirectory(tempDir) |> ignore

            try
                let setupShapes (graph: IGraph) =
                    let rdfType = createUriNode graph (Uri Rdf.Type)
                    let nodeShape = createUriNode graph (Uri Shacl.NodeShape)
                    let targetClass = createUriNode graph (Uri Shacl.TargetClass)

                    let shapeNode = createUriNode graph (Uri "http://example.org/shapes/GhostShape")
                    assertTriple graph (shapeNode, rdfType, nodeShape)
                    // Target a class that doesn't exist in the ontology
                    assertTriple graph (shapeNode, targetClass, createUriNode graph (Uri "http://example.org/types/Ghost"))

                let state = createTestState ignore setupShapes []

                let statePath = ExtractionState.defaultStatePath tempDir
                ExtractionState.save statePath state |> ignore

                let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")
                let result = execute fakeProjectPath

                match result with
                | Error e -> failtest $"Expected Ok but got Error: {e}"
                | Ok r ->
                    Expect.isFalse r.IsValid "Should be invalid due to error"
                    let errors = r.Issues |> List.filter (fun i -> i.Severity = "error")
                    Expect.isGreaterThanOrEqual errors.Length 1 "Should have at least 1 error"
                    Expect.stringContains errors.[0].Message "does not exist" "Should mention non-existent class"
            finally
                if Directory.Exists tempDir then
                    Directory.Delete(tempDir, true)

        testCase "warns for unmapped types and computes coverage" <| fun _ ->
            let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
            Directory.CreateDirectory(tempDir) |> ignore

            try
                let setupOntology (graph: IGraph) =
                    let rdfType = createUriNode graph (Uri Rdf.Type)
                    let owlClass = createUriNode graph (Uri Owl.Class)
                    let domainNode = createUriNode graph (Uri Rdfs.Domain)
                    let datatypeProp = createUriNode graph (Uri Owl.DatatypeProperty)

                    let productNode = createUriNode graph (Uri "http://example.org/types/Product")
                    assertTriple graph (productNode, rdfType, owlClass)

                    let nameNode = createUriNode graph (Uri "http://example.org/properties/name")
                    assertTriple graph (nameNode, rdfType, datatypeProp)
                    assertTriple graph (nameNode, domainNode, productNode)

                let unmapped =
                    [ { TypeName = "MyApp.Widget"
                        Reason = "no mapping"
                        Location = { File = "Widget.fs"; Line = 5; Column = 0 } } ]

                let state = createTestState setupOntology ignore unmapped

                let statePath = ExtractionState.defaultStatePath tempDir
                ExtractionState.save statePath state |> ignore

                let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")
                let result = execute fakeProjectPath

                match result with
                | Error e -> failtest $"Expected Ok but got Error: {e}"
                | Ok r ->
                    // 1 mapped class, 1 unmapped type => 50% coverage
                    Expect.floatClose Accuracy.medium r.CoveragePercent 50.0 "Coverage should be 50%"
                    let unmappedWarnings =
                        r.Issues
                        |> List.filter (fun i -> i.Message.Contains("Widget"))
                    Expect.isGreaterThanOrEqual unmappedWarnings.Length 1 "Should warn about unmapped Widget"
            finally
                if Directory.Exists tempDir then
                    Directory.Delete(tempDir, true)

        testCase "returns error when state file missing" <| fun _ ->
            let result = execute "/nonexistent/Test.fsproj"

            match result with
            | Error msg ->
                Expect.stringContains msg "not found" "Should mention file not found"
            | Ok _ ->
                failtest "Expected error for missing state"
    ]
