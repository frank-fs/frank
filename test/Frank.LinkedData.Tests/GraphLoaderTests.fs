module Frank.LinkedData.Tests.GraphLoaderTests

open System.Reflection
open Expecto
open Frank.LinkedData.Rdf

[<Tests>]
let tests =
    testList "GraphLoader" [
        testCase "loads all three resources successfully" <| fun _ ->
            let assembly = Assembly.GetExecutingAssembly()
            let result = GraphLoader.load assembly
            Expect.isOk result "Expected Ok result from GraphLoader.load"
            let semantics = Result.defaultWith (fun _ -> failwith "unreachable") result
            Expect.isGreaterThan semantics.OntologyGraph.Triples.Count 0
                "Ontology graph should have triples"
            Expect.isGreaterThan semantics.ShapesGraph.Triples.Count 0
                "Shapes graph should have triples"
            Expect.equal semantics.Manifest.Version "1.0.0" "Manifest version"
            Expect.equal semantics.Manifest.BaseUri "http://example.org/api" "Manifest baseUri"

        testCase "returns descriptive error when manifest resource is absent" <| fun _ ->
            let assembly = typeof<int>.Assembly
            let result = GraphLoader.load assembly
            Expect.isError result "Expected Error result for assembly without resources"
            match result with
            | Error msg ->
                Expect.stringContains msg "Frank.Semantic.manifest.json"
                    "Error should mention the missing resource name"
                Expect.stringContains msg (assembly.GetName().Name)
                    "Error should mention the assembly name"
            | _ -> failwith "unreachable"
    ]
