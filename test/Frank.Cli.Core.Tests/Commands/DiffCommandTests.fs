module Frank.Cli.Core.Tests.DiffCommandTests

open System
open System.IO
open Expecto
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.FSharpRdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.State
open Frank.Cli.Core.Commands.DiffCommand

let private createTestState (ontologySetup: IGraph -> unit) =
    let ontology = createGraph ()
    ontologySetup ontology
    let shapes = createGraph ()

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
        "DiffCommand"
        [ testCase "detects added triples"
          <| fun _ ->
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

                  // Save current state at the default path
                  let statePath = ExtractionState.defaultStatePath tempDir
                  ExtractionState.save statePath newState |> ignore

                  // Save old state as a backup
                  let backupDir = Path.Combine(tempDir, "obj", "frank-cli", "backups")
                  Directory.CreateDirectory(backupDir) |> ignore
                  let backupPath = Path.Combine(backupDir, "extraction-state-20260101T000000Z.json")
                  ExtractionState.save backupPath oldState |> ignore

                  let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")
                  let result = execute fakeProjectPath None

                  match result with
                  | Error e -> failtest $"Expected Ok but got Error: {e}"
                  | Ok r ->
                      Expect.isGreaterThanOrEqual r.Diff.Added.Length 1 "Should detect added triples"
                      Expect.equal r.Diff.Removed.Length 0 "Should have no removed triples"
                      Expect.isNonEmpty r.FormattedDiff "Formatted diff should not be empty"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          testCase "detects removed triples with explicit --previous"
          <| fun _ ->
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

                  // Save current state at the default path
                  let statePath = ExtractionState.defaultStatePath tempDir
                  ExtractionState.save statePath newState |> ignore

                  // Save old state at an explicit path
                  let previousPath = Path.Combine(tempDir, "previous-state.json")
                  ExtractionState.save previousPath oldState |> ignore

                  let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")
                  let result = execute fakeProjectPath (Some previousPath)

                  match result with
                  | Error e -> failtest $"Expected Ok but got Error: {e}"
                  | Ok r ->
                      Expect.equal r.Diff.Added.Length 0 "Should have no added triples"
                      Expect.isGreaterThanOrEqual r.Diff.Removed.Length 1 "Should detect removed triples"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          testCase "no changes produces empty diff"
          <| fun _ ->
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

                  let statePath = ExtractionState.defaultStatePath tempDir
                  ExtractionState.save statePath state2 |> ignore

                  let previousPath = Path.Combine(tempDir, "previous-state.json")
                  ExtractionState.save previousPath state1 |> ignore

                  let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")
                  let result = execute fakeProjectPath (Some previousPath)

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

          testCase "no previous state returns empty diff without error"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  let state = createTestState ignore
                  let statePath = ExtractionState.defaultStatePath tempDir
                  ExtractionState.save statePath state |> ignore

                  let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")
                  let result = execute fakeProjectPath None

                  match result with
                  | Error e -> failtest $"Expected Ok but got Error: {e}"
                  | Ok r ->
                      Expect.equal r.Diff.Added.Length 0 "No additions"
                      Expect.equal r.Diff.Removed.Length 0 "No removals"
                      Expect.equal r.Diff.Modified.Length 0 "No modifications"
                      Expect.stringContains r.FormattedDiff "No previous state" "Should mention no previous state"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          testCase "returns error when current state file missing"
          <| fun _ ->
              let result = execute "/nonexistent/Test.fsproj" None

              match result with
              | Error msg -> Expect.stringContains msg "not found" "Should mention file not found"
              | Ok _ -> failtest "Expected error for missing state"

          testCase "auto-detects latest backup"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  let oldState = createTestState ignore

                  let newerOldState =
                      createTestState (fun graph ->
                          let rdfType = createUriNode graph (Uri Rdf.Type)
                          let owlClass = createUriNode graph (Uri Owl.Class)
                          let node = createUriNode graph (Uri "http://example.org/types/Older")
                          assertTriple graph (node, rdfType, owlClass))

                  let newState =
                      createTestState (fun graph ->
                          let rdfType = createUriNode graph (Uri Rdf.Type)
                          let owlClass = createUriNode graph (Uri Owl.Class)
                          let node = createUriNode graph (Uri "http://example.org/types/Older")
                          assertTriple graph (node, rdfType, owlClass)
                          let node2 = createUriNode graph (Uri "http://example.org/types/Newer")
                          assertTriple graph (node2, rdfType, owlClass))

                  let statePath = ExtractionState.defaultStatePath tempDir
                  ExtractionState.save statePath newState |> ignore

                  let backupDir = Path.Combine(tempDir, "obj", "frank-cli", "backups")
                  Directory.CreateDirectory(backupDir) |> ignore

                  // Older backup
                  ExtractionState.save (Path.Combine(backupDir, "extraction-state-20260101T000000Z.json")) oldState
                  |> ignore

                  // Newer backup (should be picked)
                  ExtractionState.save (Path.Combine(backupDir, "extraction-state-20260301T000000Z.json")) newerOldState
                  |> ignore

                  let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")
                  let result = execute fakeProjectPath None

                  match result with
                  | Error e -> failtest $"Expected Ok but got Error: {e}"
                  | Ok r ->
                      // Should diff against the newer backup (which has Older but not Newer)
                      Expect.isGreaterThanOrEqual r.Diff.Added.Length 1 "Should detect added Newer class"
                      Expect.equal r.Diff.Removed.Length 0 "Should have no removed triples"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true) ]
