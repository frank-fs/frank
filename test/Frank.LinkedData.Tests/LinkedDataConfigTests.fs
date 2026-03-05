module Frank.LinkedData.Tests.LinkedDataConfigTests

open System.Reflection
open Expecto
open Frank.LinkedData

[<Tests>]
let tests =
    testList "LinkedDataConfig" [
        testCase "loadConfig returns Ok for valid assembly" <| fun _ ->
            let assembly = Assembly.GetExecutingAssembly()
            let result = LinkedDataConfig.loadConfig assembly
            Expect.isOk result "Expected Ok result from loadConfig"
            let config = Result.defaultWith (fun _ -> failwith "unreachable") result
            Expect.equal config.BaseUri "http://example.org/api" "BaseUri should match manifest"
            Expect.isGreaterThan config.OntologyGraph.Triples.Count 0
                "Ontology graph should have triples"

        testCase "loadConfig propagates GraphLoader error" <| fun _ ->
            let assembly = typeof<int>.Assembly
            let result = LinkedDataConfig.loadConfig assembly
            Expect.isError result "Expected Error result for assembly without resources"
    ]
