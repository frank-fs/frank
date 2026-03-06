module Frank.Cli.Core.Tests.CompileCommandTests

open System
open System.IO
open System.Text.Json
open Expecto
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.FSharpRdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.State
open Frank.Cli.Core.Commands.CompileCommand

let private createTestState (ontologySetup: IGraph -> unit) (shapesSetup: IGraph -> unit) =
    let ontology = createGraph ()
    ontologySetup ontology
    let shapes = createGraph ()
    shapesSetup shapes

    { Ontology = ontology
      Shapes = shapes
      SourceMap = Map.empty
      Clarifications = Map.empty
      Metadata =
        { Timestamp = DateTimeOffset.UtcNow
          SourceHash = "abc123"
          ToolVersion = "0.1.0"
          BaseUri = Uri "http://example.org/"
          Vocabularies = [ "schema.org" ] }
      UnmappedTypes = [] }

[<Tests>]
let tests =
    testList
        "CompileCommand"
        [ testCase "generates ontology, shapes, and manifest files"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  let setupOntology (graph: IGraph) =
                      let rdfType = createUriNode graph (Uri Rdf.Type)
                      let owlClass = createUriNode graph (Uri Owl.Class)
                      let productNode = createUriNode graph (Uri "http://example.org/types/Product")
                      assertTriple graph (productNode, rdfType, owlClass)

                  let setupShapes (graph: IGraph) =
                      let rdfType = createUriNode graph (Uri Rdf.Type)
                      let nodeShape = createUriNode graph (Uri Shacl.NodeShape)
                      let shapeNode = createUriNode graph (Uri "http://example.org/shapes/ProductShape")
                      assertTriple graph (shapeNode, rdfType, nodeShape)

                  let state = createTestState setupOntology setupShapes

                  // Save state to the default path
                  let statePath = ExtractionState.defaultStatePath tempDir
                  ExtractionState.save statePath state |> ignore

                  let outputDir = Path.Combine(tempDir, "output")
                  let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")
                  let result = execute fakeProjectPath (Some outputDir)

                  match result with
                  | Error e -> failtest $"Expected Ok but got Error: {e}"
                  | Ok r ->
                      // Verify files exist
                      Expect.isTrue (File.Exists r.OntologyPath) "ontology.owl.xml should exist"
                      Expect.isTrue (File.Exists r.ShapesPath) "shapes.shacl.ttl should exist"
                      Expect.isTrue (File.Exists r.ManifestPath) "manifest.json should exist"

                      // Verify ontology is valid XML
                      let ontologyContent = File.ReadAllText r.OntologyPath
                      Expect.stringContains ontologyContent "Product" "Ontology should contain Product"

                      // Verify shapes is valid Turtle
                      let shapesContent = File.ReadAllText r.ShapesPath
                      Expect.stringContains shapesContent "ProductShape" "Shapes should contain ProductShape"

                      // Verify manifest is valid JSON
                      let manifestContent = File.ReadAllText r.ManifestPath
                      let doc = JsonDocument.Parse manifestContent
                      let root = doc.RootElement
                      Expect.equal (root.GetProperty("version").GetString()) "0.1.0" "Version should match"

                      Expect.equal
                          (root.GetProperty("baseUri").GetString())
                          "http://example.org/"
                          "BaseUri should match"

                      Expect.equal (root.GetProperty("sourceHash").GetString()) "abc123" "SourceHash should match"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          testCase "uses default output dir when none specified"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  let state = createTestState ignore ignore

                  let statePath = ExtractionState.defaultStatePath tempDir
                  ExtractionState.save statePath state |> ignore

                  let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")
                  let result = execute fakeProjectPath None

                  match result with
                  | Error e -> failtest $"Expected Ok but got Error: {e}"
                  | Ok r ->
                      // Default output dir should be obj/frank-cli/
                      let expectedDir = Path.Combine(tempDir, "obj", "frank-cli")
                      Expect.stringContains r.OntologyPath expectedDir "Should use default output dir"
                      Expect.isTrue (File.Exists r.OntologyPath) "ontology file should exist"
                      Expect.isTrue (File.Exists r.ShapesPath) "shapes file should exist"
                      Expect.isTrue (File.Exists r.ManifestPath) "manifest file should exist"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          testCase "returns error when state file missing"
          <| fun _ ->
              let result = execute "/nonexistent/Test.fsproj" None

              match result with
              | Error msg -> Expect.stringContains msg "No extraction state found" "Should mention missing state"
              | Ok _ -> failtest "Expected error for missing state" ]
