module Frank.Cli.Core.Tests.JsonOutputTests

open System.Text.Json
open Expecto
open Frank.Cli.Core.Commands.ExtractCommand
open Frank.Cli.Core.Commands.ClarifyCommand
open Frank.Cli.Core.State
open Frank.Cli.Core.Output.JsonOutput

[<Tests>]
let tests =
    testList "JsonOutput" [
        testCase "formatExtractResult produces valid JSON with nested summaries and ok status" <| fun _ ->
            let result : ExtractResult =
                { OntologySummary =
                    { ClassCount = 5
                      PropertyCount = 12
                      AlignedCount = 3
                      UnalignedCount = 9 }
                  ShapesSummary =
                    { ShapeCount = 5
                      ConstraintCount = 15 }
                  UnmappedTypes =
                    [ { TypeName = "MyApp.Foo"
                        Reason = "no rule"
                        Location = { File = "Foo.fs"; Line = 10; Column = 0 } } ]
                  StateFilePath = "/tmp/state.json" }

            let json = formatExtractResult result
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            Expect.equal (root.GetProperty("status").GetString()) "ok" "status should be ok"

            // Verify nested ontologySummary
            let ontSummary = root.GetProperty("ontologySummary")
            Expect.equal (ontSummary.GetProperty("classCount").GetInt32()) 5 "classCount should be 5"
            Expect.equal (ontSummary.GetProperty("propertyCount").GetInt32()) 12 "propertyCount should be 12"
            Expect.equal (ontSummary.GetProperty("alignedCount").GetInt32()) 3 "alignedCount should be 3"
            Expect.equal (ontSummary.GetProperty("unalignedCount").GetInt32()) 9 "unalignedCount should be 9"

            // Verify nested shapesSummary
            let shapesSummary = root.GetProperty("shapesSummary")
            Expect.equal (shapesSummary.GetProperty("shapeCount").GetInt32()) 5 "shapeCount should be 5"
            Expect.equal (shapesSummary.GetProperty("constraintCount").GetInt32()) 15 "constraintCount should be 15"

            Expect.equal (root.GetProperty("stateFilePath").GetString()) "/tmp/state.json" "stateFilePath"

            let unmapped = root.GetProperty("unmappedTypes")
            Expect.equal (unmapped.GetArrayLength()) 1 "Should have 1 unmapped type"
            Expect.equal (unmapped.[0].GetProperty("typeName").GetString()) "MyApp.Foo" "typeName"

        testCase "formatClarifyResult produces valid JSON with questions and ok status" <| fun _ ->
            let result : ClarifyResult =
                { Questions =
                    [ { Id = "unmapped-type-Foo"
                        Category = "unmapped-type"
                        QuestionText = "Map it?"
                        Context = {| SourceType = "Foo"; Location = Some "Foo.fs:10" |}
                        Options =
                          [ {| Label = "yes"; Impact = "will map" |}
                            {| Label = "no"; Impact = "will skip" |} ] } ]
                  ResolvedCount = 0
                  TotalCount = 1 }

            let json = formatClarifyResult result
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            Expect.equal (root.GetProperty("status").GetString()) "ok" "status should be ok"
            Expect.equal (root.GetProperty("resolvedCount").GetInt32()) 0 "resolvedCount"
            Expect.equal (root.GetProperty("totalCount").GetInt32()) 1 "totalCount"

            let questions = root.GetProperty("questions")
            Expect.equal (questions.GetArrayLength()) 1 "Should have 1 question"
            Expect.equal (questions.[0].GetProperty("id").GetString()) "unmapped-type-Foo" "question id"
            Expect.equal (questions.[0].GetProperty("category").GetString()) "unmapped-type" "category"

            let options = questions.[0].GetProperty("options")
            Expect.equal (options.GetArrayLength()) 2 "Should have 2 options"

        testCase "formatError produces valid JSON error object" <| fun _ ->
            let json = formatError "something went wrong"
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            Expect.equal (root.GetProperty("status").GetString()) "error" "status should be error"
            Expect.equal (root.GetProperty("message").GetString()) "something went wrong" "message"

        testCase "formatExtractResult round-trips key fields" <| fun _ ->
            let original : ExtractResult =
                { OntologySummary =
                    { ClassCount = 3
                      PropertyCount = 7
                      AlignedCount = 2
                      UnalignedCount = 5 }
                  ShapesSummary =
                    { ShapeCount = 3
                      ConstraintCount = 8 }
                  UnmappedTypes = []
                  StateFilePath = "/some/path" }

            let json = formatExtractResult original
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            let ontSummary = root.GetProperty("ontologySummary")
            Expect.equal (ontSummary.GetProperty("classCount").GetInt32()) original.OntologySummary.ClassCount "ClassCount"
            Expect.equal (ontSummary.GetProperty("propertyCount").GetInt32()) original.OntologySummary.PropertyCount "PropertyCount"

            let shapesSummary = root.GetProperty("shapesSummary")
            Expect.equal (shapesSummary.GetProperty("shapeCount").GetInt32()) original.ShapesSummary.ShapeCount "ShapeCount"

            Expect.equal (root.GetProperty("stateFilePath").GetString()) original.StateFilePath "StateFilePath"
    ]
