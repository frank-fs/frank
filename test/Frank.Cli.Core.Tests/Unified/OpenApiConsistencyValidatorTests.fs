module OpenApiConsistencyValidatorTests

open System.Text.Json
open Expecto
open Frank.Resources.Model
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Unified.OpenApiConsistencyValidator
open Frank.Statecharts.Validation

let private parseSchemas (json: string) =
    JsonDocument.Parse(json).RootElement

let private makeRecordType name fields =
    { FullName = $"Test.%s{name}"
      ShortName = name
      Kind =
          Record(
              fields
              |> List.map (fun (n, k) ->
                  { Name = n
                    Kind = k
                    IsRequired = true
                    IsScalar = true
                    Constraints = [] })
          )
      GenericParameters = []
      SourceLocation = None
      IsClosed = true }

[<Tests>]
let openApiConsistencyValidatorTests =
    testList "OpenApiConsistencyValidator" [

        testCase "Consistent types produce zero discrepancies" <| fun () ->
            let types = [
                makeRecordType "Game" [
                    "Board", Primitive "xsd:string"
                    "CurrentTurn", Primitive "xsd:string"
                ]
            ]

            let schemas = parseSchemas """
            {
                "Game": {
                    "type": "object",
                    "properties": {
                        "board": { "type": "string" },
                        "currentTurn": { "type": "string" }
                    }
                }
            }
            """

            let result = validate types schemas
            Expect.isTrue result.IsConsistent "Should be consistent"
            Expect.isEmpty result.Discrepancies "Should have no discrepancies"
            Expect.equal result.CheckedTypes 1 "Checked 1 type"

        testCase "Unmapped F# field is reported" <| fun () ->
            let types = [
                makeRecordType "Game" [
                    "Board", Primitive "xsd:string"
                    "InternalState", Primitive "xsd:string"
                ]
            ]

            let schemas = parseSchemas """
            {
                "Game": {
                    "type": "object",
                    "properties": {
                        "board": { "type": "string" }
                    }
                }
            }
            """

            let result = validate types schemas
            Expect.isFalse result.IsConsistent "Should not be consistent"
            let unmapped =
                result.Discrepancies
                |> List.choose (fun d ->
                    match d with
                    | UnmappedField(t, f) -> Some(t, f)
                    | _ -> None)
            Expect.equal unmapped [ ("Game", "internalState") ] "Should report unmapped field"

        testCase "Orphan OpenAPI property is reported" <| fun () ->
            let types = [
                makeRecordType "Game" [
                    "Board", Primitive "xsd:string"
                ]
            ]

            let schemas = parseSchemas """
            {
                "Game": {
                    "type": "object",
                    "properties": {
                        "board": { "type": "string" },
                        "score": { "type": "integer" }
                    }
                }
            }
            """

            let result = validate types schemas
            Expect.isFalse result.IsConsistent "Should not be consistent"
            let orphans =
                result.Discrepancies
                |> List.choose (fun d ->
                    match d with
                    | OrphanProperty(t, p) -> Some(t, p)
                    | _ -> None)
            Expect.equal orphans [ ("Game", "score") ] "Should report orphan property"

        testCase "Type mismatch is reported" <| fun () ->
            let types = [
                makeRecordType "Game" [
                    "Score", Primitive "xsd:integer"
                ]
            ]

            let schemas = parseSchemas """
            {
                "Game": {
                    "type": "object",
                    "properties": {
                        "score": { "type": "string" }
                    }
                }
            }
            """

            let result = validate types schemas
            Expect.isFalse result.IsConsistent "Should not be consistent"
            let mismatches =
                result.Discrepancies
                |> List.choose (fun d ->
                    match d with
                    | TypeMismatch(t, f, exp, act) -> Some(t, f, exp, act)
                    | _ -> None)
            Expect.equal mismatches [ ("Game", "score", "integer", "string") ] "Should report type mismatch"

        testCase "camelCase normalization prevents false positives" <| fun () ->
            let types = [
                makeRecordType "Game" [
                    "CurrentTurn", Primitive "xsd:string"
                    "GameBoard", Primitive "xsd:string"
                ]
            ]

            let schemas = parseSchemas """
            {
                "Game": {
                    "type": "object",
                    "properties": {
                        "currentTurn": { "type": "string" },
                        "gameBoard": { "type": "string" }
                    }
                }
            }
            """

            let result = validate types schemas
            Expect.isTrue result.IsConsistent "PascalCase F# fields should match camelCase schema"
            Expect.isEmpty result.Discrepancies "No discrepancies with correct casing"

        testCase "Option type maps to same base type (no mismatch)" <| fun () ->
            let types = [
                makeRecordType "Game" [
                    "Winner", Optional(Primitive "xsd:string")
                ]
            ]

            let schemas = parseSchemas """
            {
                "Game": {
                    "type": "object",
                    "properties": {
                        "winner": { "type": "string", "nullable": true }
                    }
                }
            }
            """

            let result = validate types schemas
            Expect.isTrue result.IsConsistent "Option<string> should match string type"

        testCase "Collection type maps to array" <| fun () ->
            let types = [
                makeRecordType "Game" [
                    "Moves", Collection(Primitive "xsd:string")
                ]
            ]

            let schemas = parseSchemas """
            {
                "Game": {
                    "type": "object",
                    "properties": {
                        "moves": { "type": "array", "items": { "type": "string" } }
                    }
                }
            }
            """

            let result = validate types schemas
            Expect.isTrue result.IsConsistent "Collection should match array type"

        testCase "ValidationReport from discrepancies has correct counts" <| fun () ->
            let result =
                { Discrepancies = [ UnmappedField("Game", "secret"); OrphanProperty("Game", "extra") ]
                  CheckedTypes = 1
                  CheckedProperties = 4
                  IsConsistent = false }

            let report = toValidationReport result
            Expect.equal report.TotalFailures 2 "Should have 2 failures"
            Expect.equal report.TotalChecks 5 "Should have 5 total checks (1 type + 4 properties)"

        testCase "ValidationReport for consistent result shows pass" <| fun () ->
            let result =
                { Discrepancies = []
                  CheckedTypes = 2
                  CheckedProperties = 6
                  IsConsistent = true }

            let report = toValidationReport result
            Expect.equal report.TotalFailures 0 "Should have 0 failures"
            Expect.equal report.TotalChecks 8 "Should have 8 total checks"
            Expect.isTrue (report.Checks |> List.exists (fun c -> c.Status = Pass)) "Should have a pass check"
    ]
