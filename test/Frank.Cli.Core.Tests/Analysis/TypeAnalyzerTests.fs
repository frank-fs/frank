module Frank.Cli.Core.Tests.Analysis.TypeAnalyzerTests

open System.IO
open Expecto
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Text
open Frank.Cli.Core.Analysis

let private checker = FSharpChecker.Create()

let private checkSource (source: string) =
    async {
        let tmpFile = Path.Combine(Path.GetTempPath(), $"frank_test_{System.Guid.NewGuid():N}.fsx")
        try
            File.WriteAllText(tmpFile, source)
            let sourceText = SourceText.ofString source
            let! options, _ =
                checker.GetProjectOptionsFromScript(tmpFile, sourceText, assumeDotNetFramework = false, useSdkRefs = true)
            let! projectResults = checker.ParseAndCheckProject(options)
            return projectResults
        finally
            if File.Exists tmpFile then File.Delete tmpFile
    }

[<Tests>]
let tests =
    testList "TypeAnalyzer" [
        testCaseAsync "record with primitive fields" <| async {
            let source = "type Product = { Id: int; Name: string; IsAvailable: bool }"
            let! projectResults = checkSource source
            let types = TypeAnalyzer.analyzeTypes projectResults
            let product = types |> List.tryFind (fun t -> t.ShortName = "Product")
            Expect.isSome product "Should find Product type"
            let p = product.Value
            match p.Kind with
            | Record fields ->
                Expect.equal fields.Length 3 "Should have 3 fields"
                let idField = fields |> List.find (fun f -> f.Name = "Id")
                Expect.equal idField.Kind (Primitive "xsd:integer") "Id should be xsd:integer"
                let nameField = fields |> List.find (fun f -> f.Name = "Name")
                Expect.equal nameField.Kind (Primitive "xsd:string") "Name should be xsd:string"
                let availField = fields |> List.find (fun f -> f.Name = "IsAvailable")
                Expect.equal availField.Kind (Primitive "xsd:boolean") "IsAvailable should be xsd:boolean"
            | _ -> failwith "Product should be a Record"
        }

        testCaseAsync "discriminated union" <| async {
            let source = "type Status = Active | Inactive"
            let! projectResults = checkSource source
            let types = TypeAnalyzer.analyzeTypes projectResults
            let status = types |> List.tryFind (fun t -> t.ShortName = "Status")
            Expect.isSome status "Should find Status type"
            match status.Value.Kind with
            | DiscriminatedUnion cases ->
                Expect.equal cases.Length 2 "Should have 2 cases"
                Expect.equal cases.[0].Name "Active" "First case should be Active"
                Expect.equal cases.[1].Name "Inactive" "Second case should be Inactive"
                Expect.isEmpty cases.[0].Fields "Active should have no fields"
                Expect.isEmpty cases.[1].Fields "Inactive should have no fields"
            | _ -> failwith "Status should be a DiscriminatedUnion"
        }

        testCaseAsync "optional and list fields" <| async {
            let source = """
type Customer = {
    Name: string
    Email: string option
    Tags: string list
}
"""
            let! projectResults = checkSource source
            let types = TypeAnalyzer.analyzeTypes projectResults
            let customer = types |> List.tryFind (fun t -> t.ShortName = "Customer")
            Expect.isSome customer "Should find Customer type"
            match customer.Value.Kind with
            | Record fields ->
                let emailField = fields |> List.find (fun f -> f.Name = "Email")
                Expect.equal emailField.Kind (Optional(Primitive "xsd:string")) "Email should be Optional string"
                Expect.isFalse emailField.IsRequired "Email should not be required"
                let tagsField = fields |> List.find (fun f -> f.Name = "Tags")
                Expect.equal tagsField.Kind (Collection(Primitive "xsd:string")) "Tags should be Collection string"
                Expect.isTrue tagsField.IsRequired "Tags should be required"
            | _ -> failwith "Customer should be a Record"
        }

        testCaseAsync "reference type field" <| async {
            let source = """
type Address = { Street: string; City: string }
type Person = { Name: string; Home: Address }
"""
            let! projectResults = checkSource source
            let types = TypeAnalyzer.analyzeTypes projectResults
            let person = types |> List.tryFind (fun t -> t.ShortName = "Person")
            Expect.isSome person "Should find Person type"
            match person.Value.Kind with
            | Record fields ->
                let homeField = fields |> List.find (fun f -> f.Name = "Home")
                Expect.equal homeField.Kind (Reference "Address") "Home should be Reference Address"
            | _ -> failwith "Person should be a Record"
        }
    ]
