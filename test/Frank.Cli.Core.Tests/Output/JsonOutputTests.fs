module Frank.Cli.Core.Tests.JsonOutputTests

open System.Text.Json
open Expecto
open Frank.Cli.Core.Output.JsonOutput

[<Tests>]
let tests =
    testList "JsonOutput" [
        testCase "formatExtractResult produces valid JSON with expected fields" <| fun _ ->
            let result : ExtractResultJson =
                { Status = "success"
                  ClassCount = 5
                  PropertyCount = 12
                  ShapeCount = 5
                  UnmappedTypes =
                    [ { TypeName = "MyApp.Foo"; Reason = "no rule"; File = "Foo.fs"; Line = 10 } ]
                  StateFilePath = "/tmp/state.json" }

            let json = formatExtractResult result
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            Expect.equal (root.GetProperty("status").GetString()) "success" "status should be success"
            Expect.equal (root.GetProperty("classCount").GetInt32()) 5 "classCount should be 5"
            Expect.equal (root.GetProperty("propertyCount").GetInt32()) 12 "propertyCount should be 12"
            Expect.equal (root.GetProperty("shapeCount").GetInt32()) 5 "shapeCount should be 5"
            Expect.equal (root.GetProperty("stateFilePath").GetString()) "/tmp/state.json" "stateFilePath"

            let unmapped = root.GetProperty("unmappedTypes")
            Expect.equal (unmapped.GetArrayLength()) 1 "Should have 1 unmapped type"
            Expect.equal (unmapped.[0].GetProperty("typeName").GetString()) "MyApp.Foo" "typeName"

        testCase "formatClarifyResult produces valid JSON with questions" <| fun _ ->
            let result : ClarifyResultJson =
                { Status = "success"
                  Questions =
                    [ { Id = "q1"
                        Category = "unmapped-type"
                        QuestionText = "Map it?"
                        Context = { SourceType = "Foo"; Location = "Foo.fs:10" }
                        Options =
                          [ { Label = "yes"; Impact = "will map" }
                            { Label = "no"; Impact = "will skip" } ] } ]
                  ResolvedCount = 0
                  TotalCount = 1 }

            let json = formatClarifyResult result
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            Expect.equal (root.GetProperty("status").GetString()) "success" "status"
            Expect.equal (root.GetProperty("resolvedCount").GetInt32()) 0 "resolvedCount"
            Expect.equal (root.GetProperty("totalCount").GetInt32()) 1 "totalCount"

            let questions = root.GetProperty("questions")
            Expect.equal (questions.GetArrayLength()) 1 "Should have 1 question"
            Expect.equal (questions.[0].GetProperty("id").GetString()) "q1" "question id"
            Expect.equal (questions.[0].GetProperty("category").GetString()) "unmapped-type" "category"

            let options = questions.[0].GetProperty("options")
            Expect.equal (options.GetArrayLength()) 2 "Should have 2 options"

        testCase "formatError produces valid JSON error object" <| fun _ ->
            let json = formatError "something went wrong"
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            Expect.equal (root.GetProperty("status").GetString()) "error" "status should be error"
            Expect.equal (root.GetProperty("message").GetString()) "something went wrong" "message"

        testCase "formatExtractResult round-trips through deserialization" <| fun _ ->
            let original : ExtractResultJson =
                { Status = "success"
                  ClassCount = 3
                  PropertyCount = 7
                  ShapeCount = 3
                  UnmappedTypes = []
                  StateFilePath = "/some/path" }

            let json = formatExtractResult original
            let options = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
            let deserialized = JsonSerializer.Deserialize<ExtractResultJson>(json, options)

            Expect.equal deserialized.ClassCount original.ClassCount "ClassCount"
            Expect.equal deserialized.PropertyCount original.PropertyCount "PropertyCount"
            Expect.equal deserialized.ShapeCount original.ShapeCount "ShapeCount"
            Expect.equal deserialized.StateFilePath original.StateFilePath "StateFilePath"
    ]
