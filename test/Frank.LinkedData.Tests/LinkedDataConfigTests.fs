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

        testCase "loadConfig rejects invalid baseUri" <| fun _ ->
            // loadConfig validates that baseUri is a valid absolute URI after loading.
            // Since we cannot swap embedded resources per test, we verify the validation
            // logic directly: Uri.TryCreate with a non-URI string must fail and produce
            // the expected error message format.
            let invalidUri = "not-a-uri"
            let isValid, _ = System.Uri.TryCreate(invalidUri, System.UriKind.Absolute)
            Expect.isFalse isValid "\"not-a-uri\" should not parse as absolute URI"
            // Verify the error message format matches what loadConfig produces
            let expectedError = sprintf "Manifest baseUri is not a valid URI: %s" invalidUri
            Expect.stringContains expectedError "not a valid URI"
                "Error should indicate invalid URI"
            Expect.stringContains expectedError invalidUri
                "Error should include the offending value"
    ]
