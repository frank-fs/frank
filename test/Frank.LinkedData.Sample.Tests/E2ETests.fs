module Frank.LinkedData.Sample.Tests.E2ETests

open System
open System.IO
open System.Text.Json
open Expecto
open VDS.RDF
open VDS.RDF.Parsing
open Frank.Cli.Core.Commands
open Frank.Cli.Core.State

/// Resolves the sample project path relative to the test assembly location.
let private findSampleProject () =
    // Walk up from bin/Debug/net10.0 to the repo root, then into sample/
    let assemblyDir = AppContext.BaseDirectory

    let rec findRoot (dir: string) =
        if File.Exists(Path.Combine(dir, "Frank.sln")) then
            Some dir
        else
            let parent = Directory.GetParent(dir)
            if isNull parent then None else findRoot parent.FullName

    match findRoot assemblyDir with
    | None -> failwith "Could not find repository root (Frank.sln)"
    | Some root ->
        let fsproj =
            Path.Combine(root, "sample", "Frank.LinkedData.Sample", "Frank.LinkedData.Sample.fsproj")

        if not (File.Exists fsproj) then
            failwith $"Sample project not found at: {fsproj}"

        fsproj

[<Tests>]
let tests =
    testList
        "E2E"
        [ testCase "frank extract → compile against sample project"
          <| fun _ ->
              let fsproj = findSampleProject ()
              let projectDir = Path.GetDirectoryName fsproj

              // Clean any prior state
              let frankCliDir = Path.Combine(projectDir, "obj", "frank")

              if Directory.Exists frankCliDir then
                  Directory.Delete(frankCliDir, true)

              // Step 1: Extract
              let extractResult =
                  ExtractCommand.execute fsproj (Uri "http://example.org/api/") [ "schema.org" ]
                  |> Async.RunSynchronously

              match extractResult with
              | Error e -> failtest $"Extract failed: {e}"
              | Ok er ->
                  Expect.isGreaterThan er.OntologySummary.ClassCount 0 "Should extract at least one class (Product)"
                  Expect.isGreaterThan er.OntologySummary.PropertyCount 0 "Should extract properties"
                  Expect.isTrue (File.Exists er.StateFilePath) "State file should be written"

              // Step 2: Validate
              let validateResult = ValidateCommand.execute fsproj

              match validateResult with
              | Error e -> failtest $"Validate failed: {e}"
              | Ok vr -> Expect.isGreaterThan vr.CoveragePercent 0.0 "Coverage should be positive"

              // Step 3: Compile
              let compileResult = CompileCommand.execute fsproj None

              match compileResult with
              | Error e -> failtest $"Compile failed: {e}"
              | Ok cr ->
                  // Verify ontology
                  Expect.isTrue (File.Exists cr.OntologyPath) "ontology.owl.xml should exist"
                  let ontologyGraph = new Graph()
                  let rdfXmlParser = RdfXmlParser()
                  use reader = new StreamReader(cr.OntologyPath)
                  rdfXmlParser.Load(ontologyGraph, reader)
                  Expect.isGreaterThan ontologyGraph.Triples.Count 0 "Ontology should have triples"

                  // Verify shapes
                  Expect.isTrue (File.Exists cr.ShapesPath) "shapes.shacl.ttl should exist"
                  let shapesGraph = new Graph()
                  let turtleParser = TurtleParser()
                  use reader2 = new StreamReader(cr.ShapesPath)
                  turtleParser.Load(shapesGraph, reader2)
                  // Shapes may be empty if no shapes were generated; just verify parseable
                  ()

                  // Verify manifest
                  Expect.isTrue (File.Exists cr.ManifestPath) "manifest.json should exist"
                  let manifestContent = File.ReadAllText cr.ManifestPath
                  let doc = JsonDocument.Parse manifestContent
                  let root = doc.RootElement
                  Expect.isNotEmpty (root.GetProperty("version").GetString()) "Manifest version"

                  Expect.equal
                      (root.GetProperty("baseUri").GetString())
                      "http://example.org/api/"
                      "Manifest baseUri should match extract input"

                  Expect.isTrue (root.TryGetProperty("sourceHash") |> fst) "Manifest should have sourceHash field"

                  // Verify embedded resource names
                  Expect.equal cr.EmbeddedResourceNames.Length 3 "Should list 3 embedded resource names" ]
