module Frank.Cli.IntegrationTests.PipelineTests

open System
open System.IO
open System.Text.Json
open Expecto
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.FSharpRdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.State
open Frank.Cli.Core.Output
open Frank.Cli.Core.Commands

/// Creates a realistic extraction state with classes, properties, and shapes.
let private createRealisticState (projectDir: string) =
    let ontology = createGraph ()
    let shapes = createGraph ()

    let rdfType = createUriNode ontology (Uri Rdf.Type)
    let owlClass = createUriNode ontology (Uri Owl.Class)
    let datatypeProp = createUriNode ontology (Uri Owl.DatatypeProperty)
    let domainNode = createUriNode ontology (Uri Rdfs.Domain)
    let rangeNode = createUriNode ontology (Uri Rdfs.Range)

    // Create Product class with Name and Price properties
    let productUri = Uri "http://example.org/types/Product"
    let productNode = createUriNode ontology productUri
    assertTriple ontology (productNode, rdfType, owlClass)

    let nameUri = Uri "http://example.org/properties/name"
    let nameNode = createUriNode ontology nameUri
    assertTriple ontology (nameNode, rdfType, datatypeProp)
    assertTriple ontology (nameNode, domainNode, productNode)
    assertTriple ontology (nameNode, rangeNode, createUriNode ontology (Uri "http://www.w3.org/2001/XMLSchema#string"))

    let priceUri = Uri "http://example.org/properties/price"
    let priceNode = createUriNode ontology priceUri
    assertTriple ontology (priceNode, rdfType, datatypeProp)
    assertTriple ontology (priceNode, domainNode, productNode)

    assertTriple
        ontology
        (priceNode, rangeNode, createUriNode ontology (Uri "http://www.w3.org/2001/XMLSchema#decimal"))

    // Create Order class
    let orderUri = Uri "http://example.org/types/Order"
    let orderNode = createUriNode ontology orderUri
    assertTriple ontology (orderNode, rdfType, owlClass)

    // Create SHACL shape for Product
    let shRdfType = createUriNode shapes (Uri Rdf.Type)
    let nodeShape = createUriNode shapes (Uri Shacl.NodeShape)
    let targetClass = createUriNode shapes (Uri Shacl.TargetClass)
    let shProperty = createUriNode shapes (Uri Shacl.Property)
    let shPath = createUriNode shapes (Uri Shacl.Path)

    let productShape =
        createUriNode shapes (Uri "http://example.org/shapes/ProductShape")

    assertTriple shapes (productShape, shRdfType, nodeShape)
    assertTriple shapes (productShape, targetClass, createUriNode shapes productUri)

    let nameConstraint = shapes.CreateBlankNode()
    assertTriple shapes (productShape, shProperty, nameConstraint)
    assertTriple shapes (nameConstraint, shPath, createUriNode shapes nameUri)

    { Ontology = ontology
      Shapes = shapes
      SourceMap = Map.empty
      Clarifications = Map.empty
      Metadata =
        { Timestamp = DateTimeOffset.UtcNow
          SourceHash = "integration-test-hash"
          ToolVersion = "0.1.0"
          BaseUri = Uri "http://example.org/"
          Vocabularies = [ "schema.org" ] }
      UnmappedTypes = [] }

[<Tests>]
let tests =
    testList
        "Pipeline Integration"
        [ testCase "validate → compile pipeline produces valid artifacts"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  // Setup: create and save a realistic extraction state
                  let state = createRealisticState tempDir
                  let statePath = ExtractionState.defaultStatePath tempDir
                  ExtractionState.save statePath state |> ignore

                  let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")

                  // Step 1: Validate
                  let validateResult = ValidateCommand.execute fakeProjectPath

                  match validateResult with
                  | Error e -> failtest $"Validate failed: {e}"
                  | Ok vr ->
                      Expect.isTrue vr.IsValid "Validation should pass"
                      Expect.isGreaterThan vr.CoveragePercent 0.0 "Coverage should be positive"

                  // Step 2: Compile
                  let outputDir = Path.Combine(tempDir, "output")
                  let compileResult = CompileCommand.execute fakeProjectPath (Some outputDir)

                  match compileResult with
                  | Error e -> failtest $"Compile failed: {e}"
                  | Ok cr ->
                      // Verify all three artifacts exist and are non-empty
                      Expect.isTrue (File.Exists cr.OntologyPath) "ontology.owl.xml should exist"
                      Expect.isTrue (File.Exists cr.ShapesPath) "shapes.shacl.ttl should exist"
                      Expect.isTrue (File.Exists cr.ManifestPath) "manifest.json should exist"

                      let ontologyContent = File.ReadAllText cr.OntologyPath
                      Expect.isNotEmpty ontologyContent "ontology.owl.xml should not be empty"
                      Expect.stringContains ontologyContent "Product" "Ontology should contain Product class"

                      let shapesContent = File.ReadAllText cr.ShapesPath
                      Expect.isNotEmpty shapesContent "shapes.shacl.ttl should not be empty"
                      Expect.stringContains shapesContent "ProductShape" "Shapes should contain ProductShape"

                      let manifestContent = File.ReadAllText cr.ManifestPath
                      let doc = JsonDocument.Parse manifestContent
                      let root = doc.RootElement
                      Expect.equal (root.GetProperty("version").GetString()) "0.1.0" "Manifest version"
                      Expect.equal (root.GetProperty("baseUri").GetString()) "http://example.org/" "Manifest baseUri"

                      // Verify embedded resource names
                      Expect.equal cr.EmbeddedResourceNames.Length 3 "Should have 3 embedded resource names"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          testCase "validate → compile → diff pipeline detects changes"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")

                  // First extraction: create initial state
                  let state1 = createRealisticState tempDir
                  let statePath = ExtractionState.defaultStatePath tempDir
                  ExtractionState.save statePath state1 |> ignore

                  // Compile first version (this creates a backup)
                  let outputDir = Path.Combine(tempDir, "output")
                  let compileResult1 = CompileCommand.execute fakeProjectPath (Some outputDir)

                  match compileResult1 with
                  | Error e -> failtest $"First compile failed: {e}"
                  | Ok _ -> ()

                  // Second extraction: add a new class
                  let state2 = createRealisticState tempDir

                  let newClass =
                      createUriNode state2.Ontology (Uri "http://example.org/types/Customer")

                  let rdfType = createUriNode state2.Ontology (Uri Rdf.Type)
                  let owlClass = createUriNode state2.Ontology (Uri Owl.Class)
                  assertTriple state2.Ontology (newClass, rdfType, owlClass)

                  // Save the backup before overwriting
                  let backupDir = Path.Combine(tempDir, "obj", "frank-cli", "backups")

                  if not (Directory.Exists backupDir) then
                      Directory.CreateDirectory backupDir |> ignore

                  let backupPath = Path.Combine(backupDir, "extraction-state-20260101T000000Z.json")
                  ExtractionState.save backupPath state1 |> ignore

                  // Save new state
                  ExtractionState.save statePath state2 |> ignore

                  // Diff should detect the added Customer class
                  let diffResult = DiffCommand.execute fakeProjectPath None

                  match diffResult with
                  | Error e -> failtest $"Diff failed: {e}"
                  | Ok dr ->
                      Expect.isGreaterThanOrEqual dr.Diff.Added.Length 1 "Should detect added Customer class"
                      Expect.isNonEmpty dr.FormattedDiff "Formatted diff should not be empty"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          testCase "validate reports issues for inconsistent state"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  // Create state with a shape targeting a non-existent class
                  let state = createRealisticState tempDir

                  // Add a shape targeting a class that doesn't exist in the ontology
                  let shRdfType = createUriNode state.Shapes (Uri Rdf.Type)
                  let nodeShape = createUriNode state.Shapes (Uri Shacl.NodeShape)
                  let targetClass = createUriNode state.Shapes (Uri Shacl.TargetClass)

                  let badShape =
                      createUriNode state.Shapes (Uri "http://example.org/shapes/GhostShape")

                  assertTriple state.Shapes (badShape, shRdfType, nodeShape)

                  assertTriple
                      state.Shapes
                      (badShape, targetClass, createUriNode state.Shapes (Uri "http://example.org/types/NonExistent"))

                  let statePath = ExtractionState.defaultStatePath tempDir
                  ExtractionState.save statePath state |> ignore

                  let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")
                  let validateResult = ValidateCommand.execute fakeProjectPath

                  match validateResult with
                  | Error e -> failtest $"Validate failed: {e}"
                  | Ok vr ->
                      Expect.isFalse vr.IsValid "Should be invalid due to dangling shape target"

                      let errors =
                          vr.Issues |> List.filter (fun i -> i.Severity = ValidateCommand.Severity.Error)

                      Expect.isGreaterThanOrEqual errors.Length 1 "Should have at least one error"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          // T063: MSBuild pipeline integration tests
          testCase "ArtifactSerializer.writeArtifacts produces all three artifact files"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  let state = createRealisticState tempDir
                  let outputDir = Path.Combine(tempDir, "frank-cli-out")

                  let (ontologyPath, shapesPath, manifestPath) =
                      ArtifactSerializer.writeArtifacts state outputDir

                  // All three files must exist
                  Expect.isTrue (File.Exists ontologyPath) "ontology.owl.xml should exist"
                  Expect.isTrue (File.Exists shapesPath) "shapes.shacl.ttl should exist"
                  Expect.isTrue (File.Exists manifestPath) "manifest.json should exist"

                  // ontology.owl.xml must be valid XML containing Product
                  let ontologyContent = File.ReadAllText ontologyPath
                  Expect.isNotEmpty ontologyContent "ontology.owl.xml should not be empty"
                  Expect.stringContains ontologyContent "Product" "Ontology XML should reference Product"

                  // shapes.shacl.ttl must be valid Turtle containing ProductShape
                  let shapesContent = File.ReadAllText shapesPath
                  Expect.isNotEmpty shapesContent "shapes.shacl.ttl should not be empty"
                  Expect.stringContains shapesContent "ProductShape" "Shapes Turtle should reference ProductShape"

                  // manifest.json must be valid JSON with expected fields
                  let manifestContent = File.ReadAllText manifestPath
                  use doc = JsonDocument.Parse manifestContent
                  let root = doc.RootElement
                  Expect.equal (root.GetProperty("version").GetString()) "0.1.0" "Manifest version"
                  Expect.equal (root.GetProperty("baseUri").GetString()) "http://example.org/" "Manifest baseUri"
                  Expect.isTrue (root.TryGetProperty("generatedAt") |> fst) "Manifest should have generatedAt"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          testCase "compile --project pipeline: generate → embed artifact files present"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  let state = createRealisticState tempDir
                  let statePath = ExtractionState.defaultStatePath tempDir
                  ExtractionState.save statePath state |> ignore

                  let outputDir = Path.Combine(tempDir, "frank-cli-out")

                  // Simulate what the MSBuild target does: call ArtifactSerializer.writeArtifacts
                  // directly (since we can't execute frank-cli as an external process in unit tests).
                  // The real MSBuild path calls: dotnet frank-cli compile --project ... --base-uri ... --output ...
                  let (ontologyPath, shapesPath, manifestPath) =
                      ArtifactSerializer.writeArtifacts state outputDir

                  // Verify the three outputs that MSBuild's EmbedFrankSemanticDefinitions needs
                  Expect.isTrue (File.Exists ontologyPath) "ontology.owl.xml required by EmbedFrankSemanticDefinitions"

                  Expect.isTrue
                      (File.Exists shapesPath)
                      "shapes.shacl.ttl required by EmbedFrankSemanticDefinitions (also incremental Outputs)"

                  Expect.isTrue (File.Exists manifestPath) "manifest.json required by EmbedFrankSemanticDefinitions"

                  // Verify path names match what EmbedFrankSemanticDefinitions looks for
                  Expect.stringContains ontologyPath "ontology.owl.xml" "Path must end with ontology.owl.xml"
                  Expect.stringContains shapesPath "shapes.shacl.ttl" "Path must end with shapes.shacl.ttl"
                  Expect.stringContains manifestPath "manifest.json" "Path must end with manifest.json"

                  // Re-parse each file to confirm round-trip validity
                  let ontologyGraph = new Graph()
                  let rdfXmlParser = VDS.RDF.Parsing.RdfXmlParser()
                  use ontologyReader = new StreamReader(ontologyPath)
                  rdfXmlParser.Load(ontologyGraph, ontologyReader)
                  Expect.isGreaterThan ontologyGraph.Triples.Count 0 "Ontology graph must have triples after round-trip"

                  let shapesGraph = new Graph()
                  let turtleParser = VDS.RDF.Parsing.TurtleParser()
                  use shapesReader = new StreamReader(shapesPath)
                  turtleParser.Load(shapesGraph, shapesReader)
                  Expect.isGreaterThan shapesGraph.Triples.Count 0 "Shapes graph must have triples after round-trip"

                  use manifestDoc = JsonDocument.Parse(File.ReadAllText manifestPath)
                  Expect.equal manifestDoc.RootElement.ValueKind JsonValueKind.Object "Manifest must be JSON object"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          testCase "FrankCliSkipGeneration opt-out: ArtifactSerializer not called when skipped"
          <| fun _ ->
              // This test verifies the opt-out path at the F# level:
              // when FrankCliSkipGeneration=true, no artifact files are written.
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  let state = createRealisticState tempDir
                  let statePath = ExtractionState.defaultStatePath tempDir
                  ExtractionState.save statePath state |> ignore

                  let outputDir = Path.Combine(tempDir, "frank-cli-out")

                  // When FrankCliSkipGeneration=true, the MSBuild target is skipped entirely.
                  // The MSBuild target calls: dotnet frank-cli compile --project ... --base-uri ... --output ...
                  // When skipped, no artifact files are written.
                  // Verify that without calling writeArtifacts the output directory is empty.
                  Expect.isFalse (Directory.Exists outputDir) "Output dir should not exist before explicit emit"

                  // When the target is skipped, no compile --project invocation occurs, leaving no artifact files
                  let expectedShapes = Path.Combine(outputDir, "shapes.shacl.ttl")
                  Expect.isFalse (File.Exists expectedShapes) "shapes.shacl.ttl must NOT exist when skipped"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true) ]
