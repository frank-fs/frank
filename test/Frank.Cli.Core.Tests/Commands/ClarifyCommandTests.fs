module Frank.Cli.Core.Tests.ClarifyCommandTests

open System
open System.IO
open Expecto
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.State
open Frank.Cli.Core.Commands.ClarifyCommand

let private createTestState
    (unmappedTypes: UnmappedType list)
    (extraOntologySetup: IGraph -> unit)
    (clarifications: Map<string, string>)
    =
    let ontology = createGraph ()
    extraOntologySetup ontology

    let shapes = createGraph ()

    { Ontology = ontology
      Shapes = shapes
      SourceMap = Map.empty
      Clarifications = clarifications
      Metadata =
        { Timestamp = DateTimeOffset.UtcNow
          SourceHash = ""
          ToolVersion = "0.1.0"
          BaseUri = Uri "http://example.org/"
          Vocabularies = [ "schema.org" ] }
      UnmappedTypes = unmappedTypes }

[<Tests>]
let tests =
    testList
        "ClarifyCommand"
        [ testCase "generates unmapped-type questions with stable IDs"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  let state =
                      createTestState
                          [ { TypeName = "MyApp.Widget"
                              Reason = "no mapping"
                              Location =
                                { File = "Widget.fs"
                                  Line = 5
                                  Column = 0 } }
                            { TypeName = "MyApp.Gadget"
                              Reason = "generic type"
                              Location =
                                { File = "Gadget.fs"
                                  Line = 12
                                  Column = 0 } } ]
                          ignore
                          Map.empty

                  let statePath = ExtractionState.defaultStatePath tempDir
                  let saveResult = ExtractionState.save statePath state
                  Expect.isOk saveResult "Save should succeed"

                  // ClarifyCommand expects projectPath; we use a fake .fsproj in tempDir
                  let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")
                  let result = execute fakeProjectPath

                  match result with
                  | Error e -> failtest $"Expected Ok but got Error: {e}"
                  | Ok clarifyResult ->
                      let unmappedQuestions =
                          clarifyResult.Questions |> List.filter (fun q -> q.Category = "unmapped-type")

                      Expect.equal unmappedQuestions.Length 2 "Should have 2 unmapped-type questions"

                      Expect.stringContains
                          unmappedQuestions.[0].QuestionText
                          "Widget"
                          "First question should mention Widget"

                      Expect.stringContains
                          unmappedQuestions.[1].QuestionText
                          "Gadget"
                          "Second question should mention Gadget"

                      Expect.equal unmappedQuestions.[0].Options.Length 3 "Should have 3 options"
                      // Verify stable slug-based IDs
                      Expect.equal
                          unmappedQuestions.[0].Id
                          "unmapped-type-MyApp-Widget"
                          "ID should be slug-based with type name"

                      Expect.equal
                          unmappedQuestions.[1].Id
                          "unmapped-type-MyApp-Gadget"
                          "ID should be slug-based with type name"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          testCase "generates open-or-closed questions for DU types"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  let setupOntology (graph: IGraph) =
                      let rdfType = createUriNode graph (Uri Rdf.Type)
                      let owlClass = createUriNode graph (Uri Owl.Class)
                      let subClassOf = createUriNode graph (Uri Rdfs.SubClassOf)

                      // Create Status as owl:Class
                      let statusNode = createUriNode graph (Uri "http://example.org/types/Status")
                      assertTriple graph (statusNode, rdfType, owlClass)

                      // Create Active as owl:Class subClassOf Status
                      let activeNode = createUriNode graph (Uri "http://example.org/types/Active")
                      assertTriple graph (activeNode, rdfType, owlClass)
                      assertTriple graph (activeNode, subClassOf, statusNode)

                      // Create Inactive as owl:Class subClassOf Status
                      let inactiveNode = createUriNode graph (Uri "http://example.org/types/Inactive")
                      assertTriple graph (inactiveNode, rdfType, owlClass)
                      assertTriple graph (inactiveNode, subClassOf, statusNode)

                  let state = createTestState [] setupOntology Map.empty

                  let statePath = ExtractionState.defaultStatePath tempDir
                  let saveResult = ExtractionState.save statePath state
                  Expect.isOk saveResult "Save should succeed"

                  let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")
                  let result = execute fakeProjectPath

                  match result with
                  | Error e -> failtest $"Expected Ok but got Error: {e}"
                  | Ok clarifyResult ->
                      let openClosedQuestions =
                          clarifyResult.Questions |> List.filter (fun q -> q.Category = "open-or-closed")

                      Expect.isGreaterThanOrEqual
                          openClosedQuestions.Length
                          1
                          "Should have at least 1 open-or-closed question"

                      Expect.stringContains openClosedQuestions.[0].QuestionText "Status" "Should mention Status"
                      // Verify stable slug-based ID
                      Expect.stringContains
                          openClosedQuestions.[0].Id
                          "open-or-closed-Status"
                          "ID should contain type name"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          testCase "generates missing-relationship questions"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  let setupOntology (graph: IGraph) =
                      let rdfType = createUriNode graph (Uri Rdf.Type)
                      let owlClass = createUriNode graph (Uri Owl.Class)

                      // Create Order and OrderItem as classes — OrderItem contains "Order" in its name
                      // but no owl:ObjectProperty links them
                      let orderNode = createUriNode graph (Uri "http://example.org/types/Order")
                      assertTriple graph (orderNode, rdfType, owlClass)

                      let orderItemNode = createUriNode graph (Uri "http://example.org/types/OrderItem")
                      assertTriple graph (orderItemNode, rdfType, owlClass)

                  let state = createTestState [] setupOntology Map.empty

                  let statePath = ExtractionState.defaultStatePath tempDir
                  let saveResult = ExtractionState.save statePath state
                  Expect.isOk saveResult "Save should succeed"

                  let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")
                  let result = execute fakeProjectPath

                  match result with
                  | Error e -> failtest $"Expected Ok but got Error: {e}"
                  | Ok clarifyResult ->
                      let missingRelQuestions =
                          clarifyResult.Questions
                          |> List.filter (fun q -> q.Category = "missing-relationship")

                      Expect.isGreaterThanOrEqual
                          missingRelQuestions.Length
                          1
                          "Should detect missing relationship between Order and OrderItem"

                      let q = missingRelQuestions.[0]
                      Expect.stringContains q.Id "missing-relationship-" "ID should start with missing-relationship-"
                      Expect.isTrue (q.QuestionText.Contains("Order")) "Should mention Order"
                      Expect.equal q.Options.Length 3 "Should have 3 options"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          testCase "resolvedCount reflects clarifications already answered"
          <| fun _ ->
              let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(tempDir) |> ignore

              try
                  let state =
                      createTestState
                          [ { TypeName = "MyApp.Foo"
                              Reason = "no mapping"
                              Location =
                                { File = "Foo.fs"
                                  Line = 1
                                  Column = 0 } } ]
                          ignore
                          (Map.ofList [ ("unmapped-type-MyApp-Foo", "ignore") ])

                  let statePath = ExtractionState.defaultStatePath tempDir
                  let saveResult = ExtractionState.save statePath state
                  Expect.isOk saveResult "Save should succeed"

                  let fakeProjectPath = Path.Combine(tempDir, "Test.fsproj")
                  let result = execute fakeProjectPath

                  match result with
                  | Error e -> failtest $"Expected Ok but got Error: {e}"
                  | Ok clarifyResult -> Expect.equal clarifyResult.ResolvedCount 1 "Should have 1 resolved question"
              finally
                  if Directory.Exists tempDir then
                      Directory.Delete(tempDir, true)

          testCase "execute returns error when state file missing"
          <| fun _ ->
              let result = execute "/nonexistent/Test.fsproj"

              match result with
              | Error msg -> Expect.stringContains msg "not found" "Should mention file not found"
              | Ok _ -> failtest "Expected error for missing state" ]
