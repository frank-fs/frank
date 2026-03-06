module Frank.LinkedData.Sample.Tests.PipelineTests

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open Expecto
open VDS.RDF
open VDS.RDF.Parsing
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.FSharpRdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.State
open Frank.Cli.Core.Commands

/// Creates an extraction state matching what frank-cli extract would produce
/// for the Frank.LinkedData.Sample project's Product domain model.
let private createProductExtractionState () =
    let ontology = createGraph ()
    let shapes = createGraph ()

    let rdfType = createUriNode ontology (Uri Rdf.Type)
    let owlClass = createUriNode ontology (Uri Owl.Class)
    let datatypeProp = createUriNode ontology (Uri Owl.DatatypeProperty)
    let domainNode = createUriNode ontology (Uri Rdfs.Domain)
    let rangeNode = createUriNode ontology (Uri Rdfs.Range)
    let baseUri = "http://example.org/api"

    // Product class
    let productUri = Uri $"{baseUri}/types/Product"
    let productNode = createUriNode ontology productUri
    assertTriple ontology (productNode, rdfType, owlClass)

    // ProductCategory class
    let categoryUri = Uri $"{baseUri}/types/ProductCategory"
    let categoryNode = createUriNode ontology categoryUri
    assertTriple ontology (categoryNode, rdfType, owlClass)

    // Properties: Name, Price, InStock, Category, Id
    let props =
        [ "Name", "http://www.w3.org/2001/XMLSchema#string"
          "Price", "http://www.w3.org/2001/XMLSchema#decimal"
          "InStock", "http://www.w3.org/2001/XMLSchema#boolean"
          "Id", "http://www.w3.org/2001/XMLSchema#integer" ]

    for (name, rangeUri) in props do
        let propUri = Uri $"{baseUri}/properties/Product/{name}"
        let propNode = createUriNode ontology propUri
        assertTriple ontology (propNode, rdfType, datatypeProp)
        assertTriple ontology (propNode, domainNode, productNode)
        assertTriple ontology (propNode, rangeNode, createUriNode ontology (Uri rangeUri))

    // Category as object property
    let objectProp = createUriNode ontology (Uri Owl.ObjectProperty)
    let catPropUri = Uri $"{baseUri}/properties/Product/Category"
    let catPropNode = createUriNode ontology catPropUri
    assertTriple ontology (catPropNode, rdfType, objectProp)
    assertTriple ontology (catPropNode, domainNode, productNode)
    assertTriple ontology (catPropNode, rangeNode, categoryNode)

    // SHACL shapes
    let shRdfType = createUriNode shapes (Uri Rdf.Type)
    let nodeShape = createUriNode shapes (Uri Shacl.NodeShape)
    let targetClass = createUriNode shapes (Uri Shacl.TargetClass)
    let shProperty = createUriNode shapes (Uri Shacl.Property)
    let shPath = createUriNode shapes (Uri Shacl.Path)

    let productShape = createUriNode shapes (Uri $"{baseUri}/shapes/ProductShape")
    assertTriple shapes (productShape, shRdfType, nodeShape)
    assertTriple shapes (productShape, targetClass, createUriNode shapes productUri)

    for (name, _) in props do
        let propConstraint = shapes.CreateBlankNode()
        assertTriple shapes (productShape, shProperty, propConstraint)
        assertTriple shapes (propConstraint, shPath, createUriNode shapes (Uri $"{baseUri}/properties/Product/{name}"))

    { Ontology = ontology
      Shapes = shapes
      SourceMap = Dictionary<Uri, SourceLocation>()
      Clarifications = Map.empty
      Metadata =
        { Timestamp = DateTimeOffset.UtcNow
          SourceHash = "sample-hash"
          ToolVersion = "0.1.0"
          BaseUri = Uri $"{baseUri}/"
          Vocabularies = [ "schema.org" ] }
      UnmappedTypes = [] }

[<Tests>]
let tests =
    testList
        "Pipeline"
        [ testCase "extract → validate → compile produces valid artifacts"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  // Simulate extract: save state
                  let state = createProductExtractionState ()
                  let statePath = ExtractionState.defaultStatePath tempDir
                  ExtractionState.save statePath state |> ignore

                  let fakeProjectPath = Path.Combine(tempDir, "Sample.fsproj")

                  // Step 1: Validate
                  let validateResult = ValidateCommand.execute fakeProjectPath

                  match validateResult with
                  | Error e -> failtest $"Validate failed: {e}"
                  | Ok vr -> Expect.isGreaterThan vr.CoveragePercent 0.0 "Coverage should be positive"

                  // Step 2: Compile
                  let compileResult = CompileCommand.execute fakeProjectPath None

                  match compileResult with
                  | Error e -> failtest $"Compile failed: {e}"
                  | Ok cr ->
                      // Verify all three artifact files exist and are non-empty
                      Expect.isTrue (File.Exists cr.OntologyPath) "ontology.owl.xml should exist"
                      Expect.isTrue (File.Exists cr.ShapesPath) "shapes.shacl.ttl should exist"
                      Expect.isTrue (File.Exists cr.ManifestPath) "manifest.json should exist"

                      Expect.isGreaterThan (FileInfo(cr.OntologyPath).Length) 0L "ontology.owl.xml should not be empty"
                      Expect.isGreaterThan (FileInfo(cr.ShapesPath).Length) 0L "shapes.shacl.ttl should not be empty"
                      Expect.isGreaterThan (FileInfo(cr.ManifestPath).Length) 0L "manifest.json should not be empty"

                      // Verify ontology contains Product class
                      let ontologyContent = File.ReadAllText cr.OntologyPath
                      Expect.stringContains ontologyContent "Product" "Ontology should reference Product"

                      // Verify shapes contain ProductShape
                      let shapesContent = File.ReadAllText cr.ShapesPath
                      Expect.stringContains shapesContent "ProductShape" "Shapes should reference ProductShape"

                      // Verify manifest is valid JSON with required fields
                      let manifestContent = File.ReadAllText cr.ManifestPath
                      let doc = JsonDocument.Parse manifestContent
                      let root = doc.RootElement
                      Expect.isNotEmpty (root.GetProperty("version").GetString()) "Manifest should have version"
                      Expect.isNotEmpty (root.GetProperty("baseUri").GetString()) "Manifest should have baseUri"
                      Expect.isNotEmpty (root.GetProperty("sourceHash").GetString()) "Manifest should have sourceHash"
                      Expect.isNotEmpty (root.GetProperty("generatedAt").GetString()) "Manifest should have generatedAt"

                      // Verify embedded resource names
                      Expect.equal cr.EmbeddedResourceNames.Length 3 "Should list 3 embedded resource names"

                      Expect.contains
                          cr.EmbeddedResourceNames
                          "Frank.Semantic.ontology.owl.xml"
                          "Should include ontology resource name"

                      Expect.contains
                          cr.EmbeddedResourceNames
                          "Frank.Semantic.shapes.shacl.ttl"
                          "Should include shapes resource name"

                      Expect.contains
                          cr.EmbeddedResourceNames
                          "Frank.Semantic.manifest.json"
                          "Should include manifest resource name"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          testCase "compiled ontology round-trips through RDF parser"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  let state = createProductExtractionState ()
                  let statePath = ExtractionState.defaultStatePath tempDir
                  ExtractionState.save statePath state |> ignore

                  let fakeProjectPath = Path.Combine(tempDir, "Sample.fsproj")
                  let compileResult = CompileCommand.execute fakeProjectPath None

                  match compileResult with
                  | Error e -> failtest $"Compile failed: {e}"
                  | Ok cr ->
                      // Parse ontology with dotNetRdf
                      let ontologyGraph = new Graph()
                      let rdfXmlParser = RdfXmlParser()
                      use reader = new StreamReader(cr.OntologyPath)
                      rdfXmlParser.Load(ontologyGraph, reader)
                      Expect.isGreaterThan ontologyGraph.Triples.Count 0 "Parsed ontology should have triples"

                      // Parse shapes with dotNetRdf
                      let shapesGraph = new Graph()
                      let turtleParser = TurtleParser()
                      use reader2 = new StreamReader(cr.ShapesPath)
                      turtleParser.Load(shapesGraph, reader2)
                      Expect.isGreaterThan shapesGraph.Triples.Count 0 "Parsed shapes should have triples"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          testCase "validate → compile → diff detects changes"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  let fakeProjectPath = Path.Combine(tempDir, "Sample.fsproj")

                  // First round: create and compile initial state
                  let state1 = createProductExtractionState ()
                  let statePath = ExtractionState.defaultStatePath tempDir
                  ExtractionState.save statePath state1 |> ignore
                  CompileCommand.execute fakeProjectPath None |> ignore

                  // Save backup before modifying state
                  let backupDir = Path.Combine(tempDir, "obj", "frank-cli", "backups")

                  if not (Directory.Exists backupDir) then
                      Directory.CreateDirectory backupDir |> ignore

                  let backupPath = Path.Combine(backupDir, "extraction-state-20260101T000000Z.json")
                  ExtractionState.save backupPath state1 |> ignore

                  // Second round: add a new class (Customer)
                  let state2 = createProductExtractionState ()

                  let newClass =
                      createUriNode state2.Ontology (Uri "http://example.org/api/types/Customer")

                  let rdfType = createUriNode state2.Ontology (Uri Rdf.Type)
                  let owlClass = createUriNode state2.Ontology (Uri Owl.Class)
                  assertTriple state2.Ontology (newClass, rdfType, owlClass)
                  ExtractionState.save statePath state2 |> ignore

                  // Diff should detect added Customer class
                  let diffResult = DiffCommand.execute fakeProjectPath None

                  match diffResult with
                  | Error e -> failtest $"Diff failed: {e}"
                  | Ok dr ->
                      Expect.isGreaterThanOrEqual dr.Diff.Added.Length 1 "Should detect added Customer class"
                      Expect.isNonEmpty dr.FormattedDiff "Formatted diff should not be empty"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true) ]
