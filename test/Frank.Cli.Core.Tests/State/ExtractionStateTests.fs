module Frank.Cli.Core.Tests.ExtractionStateTests

open System
open System.IO
open Expecto
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.FSharpRdf
open Frank.Cli.Core.State

[<Tests>]
let tests =
    testList
        "ExtractionState"
        [ testCase "save and load roundtrip preserves triple count"
          <| fun _ ->
              let ontology = createGraph ()
              let s = createUriNode ontology (Uri "http://example.org/Person")

              let rdfType =
                  createUriNode ontology (Uri "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")

              let owlClass = createUriNode ontology (Uri "http://www.w3.org/2002/07/owl#Class")
              assertTriple ontology (s, rdfType, owlClass)

              let label =
                  createUriNode ontology (Uri "http://www.w3.org/2000/01/rdf-schema#label")

              let labelVal = createLiteralNode ontology "Person" None
              assertTriple ontology (s, label, labelVal)

              let comment =
                  createUriNode ontology (Uri "http://www.w3.org/2000/01/rdf-schema#comment")

              let commentVal = createLiteralNode ontology "A person entity" None
              assertTriple ontology (s, comment, commentVal)

              let shapes = createGraph ()

              let state =
                  { Ontology = ontology
                    Shapes = shapes
                    SourceMap =
                      Map.ofList
                          [ "http://example.org/Person",
                            { File = "Models.fs"
                              Line = 10
                              Column = 5 } ]
                    Clarifications = Map.ofList [ "Person.name", "Use schema:name" ]
                    Metadata =
                      { Timestamp = DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
                        SourceHash = "abc123"
                        ToolVersion = "1.0.0"
                        BaseUri = Uri "http://example.org/"
                        Vocabularies = [ "schema"; "rdfs" ] }
                    UnmappedTypes =
                      [ { TypeName = "CustomWidget"
                          Reason = "No vocabulary mapping"
                          Location =
                            { File = "Widgets.fs"
                              Line = 3
                              Column = 1 } } ] }

              let tmpPath =
                  Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "state.json")

              try
                  let saveResult = ExtractionState.save tmpPath state
                  Expect.isOk saveResult "Save should succeed"

                  let loadResult = ExtractionState.load tmpPath
                  Expect.isOk loadResult "Load should succeed"

                  let loaded = Result.defaultWith (fun _ -> failtest "unreachable") loadResult
                  Expect.equal (loaded.Ontology.Triples.Count) 3 "Should have 3 triples in ontology"
                  Expect.equal (loaded.Shapes.Triples.Count) 0 "Shapes should be empty"
              finally
                  let dir = Path.GetDirectoryName(tmpPath)

                  if Directory.Exists(dir) then
                      Directory.Delete(dir, true)

          testCase "save and load roundtrip preserves metadata"
          <| fun _ ->
              let state =
                  { Ontology = createGraph ()
                    Shapes = createGraph ()
                    SourceMap = Map.empty
                    Clarifications = Map.empty
                    Metadata =
                      { Timestamp = DateTimeOffset(2026, 3, 5, 12, 0, 0, TimeSpan.Zero)
                        SourceHash = "def456"
                        ToolVersion = "2.0.0"
                        BaseUri = Uri "http://api.example.com/"
                        Vocabularies = [ "hydra"; "shacl"; "owl" ] }
                    UnmappedTypes = [] }

              let tmpPath =
                  Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "state.json")

              try
                  ExtractionState.save tmpPath state |> ignore

                  let loaded =
                      ExtractionState.load tmpPath
                      |> Result.defaultWith (fun _ -> failtest "unreachable")

                  Expect.equal loaded.Metadata.Timestamp state.Metadata.Timestamp "Timestamp should match"
                  Expect.equal loaded.Metadata.BaseUri state.Metadata.BaseUri "BaseUri should match"
                  Expect.equal loaded.Metadata.Vocabularies state.Metadata.Vocabularies "Vocabularies should match"
                  Expect.equal loaded.Metadata.SourceHash "def456" "SourceHash should match"
                  Expect.equal loaded.Metadata.ToolVersion "2.0.0" "ToolVersion should match"
              finally
                  let dir = Path.GetDirectoryName(tmpPath)

                  if Directory.Exists(dir) then
                      Directory.Delete(dir, true)

          testCase "save and load roundtrip preserves SourceMap with multiple entries"
          <| fun _ ->
              let state =
                  { Ontology = createGraph ()
                    Shapes = createGraph ()
                    SourceMap =
                      Map.ofList
                          [ "http://example.org/Person",
                            { File = "Models.fs"
                              Line = 10
                              Column = 5 }
                            "http://example.org/Order",
                            { File = "Orders.fs"
                              Line = 25
                              Column = 0 }
                            "http://example.org/Product",
                            { File = "Models.fs"
                              Line = 42
                              Column = 8 } ]
                    Clarifications = Map.empty
                    Metadata =
                      { Timestamp = DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
                        SourceHash = "map-test"
                        ToolVersion = "1.0.0"
                        BaseUri = Uri "http://example.org/"
                        Vocabularies = [ "schema" ] }
                    UnmappedTypes = [] }

              let tmpPath =
                  Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "state.json")

              try
                  ExtractionState.save tmpPath state |> ignore

                  let loaded =
                      ExtractionState.load tmpPath
                      |> Result.defaultWith (fun _ -> failtest "unreachable")

                  Expect.equal loaded.SourceMap.Count 3 "Should have 3 SourceMap entries"

                  Expect.equal
                      loaded.SourceMap.["http://example.org/Person"].File
                      "Models.fs"
                      "Person file should match"

                  Expect.equal loaded.SourceMap.["http://example.org/Person"].Line 10 "Person line should match"
                  Expect.equal loaded.SourceMap.["http://example.org/Person"].Column 5 "Person column should match"
                  Expect.equal loaded.SourceMap.["http://example.org/Order"].File "Orders.fs" "Order file should match"
                  Expect.equal loaded.SourceMap.["http://example.org/Order"].Line 25 "Order line should match"

                  Expect.equal
                      loaded.SourceMap.["http://example.org/Product"].File
                      "Models.fs"
                      "Product file should match"

                  Expect.equal loaded.SourceMap.["http://example.org/Product"].Column 8 "Product column should match"
              finally
                  let dir = Path.GetDirectoryName(tmpPath)

                  if Directory.Exists(dir) then
                      Directory.Delete(dir, true)

          testCase "load on non-existent path returns Error"
          <| fun _ ->
              let result = ExtractionState.load "/nonexistent/path/state.json"
              Expect.isError result "Should return Error for missing file"

          testCase "defaultStatePath returns expected path"
          <| fun _ ->
              let path = ExtractionState.defaultStatePath "/my/project"

              Expect.stringEnds
                  path
                  (Path.Combine("obj", "frank", "state.json"))
                  "Should end with obj/frank/state.json" ]
