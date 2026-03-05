module Frank.Cli.Core.Tests.DiffCommandTests

open System
open System.Collections.Generic
open System.IO
open Expecto
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.State
open Frank.Cli.Core.Commands.DiffCommand

let private createTestState (ontologySetup: IGraph -> unit) =
    let ontology = createGraph ()
    ontologySetup ontology
    let shapes = createGraph ()

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
      UnmappedTypes = [] }

[<Tests>]
let tests =
    testList "DiffCommand" [
        testCase "detects added triples" <| fun _ ->
            let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
            Directory.CreateDirectory(tempDir) |> ignore

            try
                let oldState = createTestState ignore

                let newState =
                    createTestState (fun graph ->
                        let rdfType = createUriNode graph (Uri Rdf.Type)
                        let owlClass = createUriNode graph (Uri Owl.Class)
                        let productNode = createUriNode graph (Uri "http://example.org/types/Product")
                        assertTriple graph (productNode, rdfType, owlClass))

                let oldPath = Path.Combine(tempDir, "old-state.json")
                let newPath = Path.Combine(tempDir, "new-state.json")
                ExtractionState.save oldPath oldState |> ignore
                ExtractionState.save newPath newState |> ignore

                let result = execute oldPath newPath

                match result with
                | Error e -> failtest $"Expected Ok but got Error: {e}"
                | Ok r ->
                    Expect.isGreaterThanOrEqual r.Diff.Added.Length 1 "Should detect added triples"
                    Expect.equal r.Diff.Removed.Length 0 "Should have no removed triples"
                    Expect.isNonEmpty r.FormattedDiff "Formatted diff should not be empty"
            finally
                if Directory.Exists tempDir then
                    Directory.Delete(tempDir, true)

        testCase "detects removed triples" <| fun _ ->
            let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
            Directory.CreateDirectory(tempDir) |> ignore

            try
                let oldState =
                    createTestState (fun graph ->
                        let rdfType = createUriNode graph (Uri Rdf.Type)
                        let owlClass = createUriNode graph (Uri Owl.Class)
                        let productNode = createUriNode graph (Uri "http://example.org/types/Product")
                        assertTriple graph (productNode, rdfType, owlClass))

                let newState = createTestState ignore

                let oldPath = Path.Combine(tempDir, "old-state.json")
                let newPath = Path.Combine(tempDir, "new-state.json")
                ExtractionState.save oldPath oldState |> ignore
                ExtractionState.save newPath newState |> ignore

                let result = execute oldPath newPath

                match result with
                | Error e -> failtest $"Expected Ok but got Error: {e}"
                | Ok r ->
                    Expect.equal r.Diff.Added.Length 0 "Should have no added triples"
                    Expect.isGreaterThanOrEqual r.Diff.Removed.Length 1 "Should detect removed triples"
            finally
                if Directory.Exists tempDir then
                    Directory.Delete(tempDir, true)

        testCase "no changes produces empty diff" <| fun _ ->
            let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
            Directory.CreateDirectory(tempDir) |> ignore

            try
                let setupOntology (graph: IGraph) =
                    let rdfType = createUriNode graph (Uri Rdf.Type)
                    let owlClass = createUriNode graph (Uri Owl.Class)
                    let productNode = createUriNode graph (Uri "http://example.org/types/Product")
                    assertTriple graph (productNode, rdfType, owlClass)

                let state1 = createTestState setupOntology
                let state2 = createTestState setupOntology

                let oldPath = Path.Combine(tempDir, "old-state.json")
                let newPath = Path.Combine(tempDir, "new-state.json")
                ExtractionState.save oldPath state1 |> ignore
                ExtractionState.save newPath state2 |> ignore

                let result = execute oldPath newPath

                match result with
                | Error e -> failtest $"Expected Ok but got Error: {e}"
                | Ok r ->
                    Expect.equal r.Diff.Added.Length 0 "No additions"
                    Expect.equal r.Diff.Removed.Length 0 "No removals"
                    Expect.equal r.Diff.Modified.Length 0 "No modifications"
                    Expect.stringContains r.FormattedDiff "No changes" "Should say no changes"
            finally
                if Directory.Exists tempDir then
                    Directory.Delete(tempDir, true)

        testCase "returns error when old state file missing" <| fun _ ->
            let result = execute "/nonexistent/old.json" "/nonexistent/new.json"

            match result with
            | Error msg ->
                Expect.stringContains msg "not found" "Should mention file not found"
            | Ok _ ->
                failtest "Expected error for missing state"
    ]
