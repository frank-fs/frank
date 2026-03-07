module Frank.Cli.Core.Tests.ExtractCommandTests

open System
open System.IO
open Expecto
open VDS.RDF
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.FSharpRdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Extraction
open Frank.Cli.Core.State
open Frank.Cli.Core.Commands.ExtractCommand

[<Tests>]
let tests =
    testList
        "ExtractCommand"
        [ testCase "execute returns error for non-existent project"
          <| fun _ ->
              let result =
                  execute "/nonexistent/path/project.fsproj" (Uri "http://example.org/") [ "schema.org" ]
                  |> Async.RunSynchronously

              match result with
              | Error msg -> Expect.stringContains msg "not found" "Should mention file not found"
              | Ok _ -> failtest "Expected error for non-existent project"

          testCase "state save and load round-trips correctly"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  let statePath = Path.Combine(tempDir, "state.json")
                  let ontology = createGraph ()
                  let rdfType = createUriNode ontology (Uri Rdf.Type)
                  let classNode = createUriNode ontology (Uri "http://example.org/types/Product")
                  assertTriple ontology (classNode, rdfType, createUriNode ontology (Uri Owl.Class))

                  let shapes = createGraph ()
                  let shapeNode = createUriNode shapes (Uri "http://example.org/shapes/ProductShape")

                  assertTriple
                      shapes
                      (shapeNode, createUriNode shapes (Uri Rdf.Type), createUriNode shapes (Uri Shacl.NodeShape))

                  let state: ExtractionState =
                      { Ontology = ontology
                        Shapes = shapes
                        SourceMap = Map.empty
                        Clarifications = Map.ofList [ ("q1", "answer1") ]
                        Metadata =
                          { Timestamp = DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
                            SourceHash = "abc123"
                            ToolVersion = "0.1.0"
                            BaseUri = Uri "http://example.org/"
                            Vocabularies = [ "schema.org" ] }
                        UnmappedTypes =
                          [ { TypeName = "MyApp.Foo"
                              Reason = "no mapping rule"
                              Location =
                                { File = "Foo.fs"
                                  Line = 10
                                  Column = 4 } } ] }

                  let saveResult = ExtractionState.save statePath state
                  Expect.isOk saveResult "Save should succeed"

                  let loadResult = ExtractionState.load statePath
                  Expect.isOk loadResult "Load should succeed"

                  let loaded = Result.defaultWith (fun _ -> failtest "unreachable") loadResult

                  Expect.equal loaded.UnmappedTypes.Length 1 "Should have 1 unmapped type"
                  Expect.equal loaded.UnmappedTypes.[0].TypeName "MyApp.Foo" "TypeName should match"
                  Expect.equal loaded.Clarifications.["q1"] "answer1" "Clarification should match"
                  Expect.equal loaded.Metadata.ToolVersion "0.1.0" "ToolVersion should match"

                  // Verify ontology graph triples
                  let classCount =
                      loaded.Ontology.Triples
                      |> Seq.filter (fun t ->
                          match t.Object with
                          | :? IUriNode as on -> on.Uri = Uri Owl.Class
                          | _ -> false)
                      |> Seq.length

                  Expect.equal classCount 1 "Ontology should have 1 owl:Class triple"

                  // Verify shapes graph triples
                  let shapeCount =
                      loaded.Shapes.Triples
                      |> Seq.filter (fun t ->
                          match t.Object with
                          | :? IUriNode as on -> on.Uri = Uri Shacl.NodeShape
                          | _ -> false)
                      |> Seq.length

                  Expect.equal shapeCount 1 "Shapes should have 1 sh:NodeShape triple"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          testCase "orchestration calls each step exactly once in order"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  let callLog = ResizeArray<string>()

                  // Create minimal stubs that record call order
                  let emptyGraph () = createGraph ()

                  let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")

                  let fakePipeline: ExtractPipeline =
                      { LoadProject =
                          fun _path ->
                              async {
                                  callLog.Add("LoadProject")
                                  // We need a LoadedProject. We can't easily create FSharpCheckProjectResults
                                  // without a real compiler, so we'll use a minimal approach.
                                  // Return an error to test the first step, or create a mock.
                                  // For sequence testing, we need the pipeline to proceed through all steps.
                                  // We'll create a stub that returns a LoadedProject with empty data.
                                  return Error "mock-not-needed"
                              }
                        AnalyzeAst =
                          fun _inputs ->
                              callLog.Add("AnalyzeAst")
                              []
                        AnalyzeTypes =
                          fun _checkResults ->
                              callLog.Add("AnalyzeTypes")
                              []
                        MapTypes =
                          fun _config _types ->
                              callLog.Add("MapTypes")
                              emptyGraph ()
                        MapRoutes =
                          fun _config _resources _types ->
                              callLog.Add("MapRoutes")
                              emptyGraph ()
                        MapCapabilities =
                          fun _config _resources ->
                              callLog.Add("MapCapabilities")
                              emptyGraph ()
                        GenerateShapes =
                          fun _config _types ->
                              callLog.Add("GenerateShapes")
                              emptyGraph ()
                        AlignVocabularies =
                          fun _config graph ->
                              callLog.Add("AlignVocabularies")
                              graph
                        SaveState =
                          fun _path _state ->
                              callLog.Add("SaveState")
                              Ok() }

                  // Since LoadProject returns Error, pipeline will stop after step 1.
                  // To test the full sequence, we need LoadProject to succeed.
                  // But LoadedProject requires FSharpCheckProjectResults which is hard to mock.
                  // We'll use Unchecked.defaultof for the check results since our mock AnalyzeTypes ignores them.
                  let fullPipeline =
                      { fakePipeline with
                          LoadProject =
                              fun _path ->
                                  async {
                                      callLog.Add("LoadProject")

                                      return
                                          Ok
                                              { ProjectPath = fakeProjectPath
                                                ParsedFiles = []
                                                CheckResults = Unchecked.defaultof<FSharpCheckProjectResults> }
                                  } }

                  let result =
                      executeWithPipeline fullPipeline fakeProjectPath (Uri "http://example.org/") [ "schema.org" ]
                      |> Async.RunSynchronously

                  Expect.isOk result "Pipeline should succeed with mocked steps"

                  let expectedOrder =
                      [ "LoadProject"
                        "AnalyzeAst"
                        "AnalyzeTypes"
                        "MapTypes"
                        "MapRoutes"
                        "MapCapabilities"
                        "GenerateShapes"
                        "AlignVocabularies"
                        "SaveState" ]

                  Expect.equal
                      (callLog |> Seq.toList)
                      expectedOrder
                      "Steps should be called exactly once in the documented order"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true) ]
