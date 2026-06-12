module Frank.Cli.Core.Tests.ExtractorTests

open Expecto
open Frank.Cli.Core
open Frank.Cli.Core.Tests.Helpers

[<Tests>]
let at1RecordsExtract =
    testCase "AT1: records extract correctly" (fun () ->
        let source =
            """namespace MyApp
type OrderLine = { Sku: string; Qty: int }
type Order = { Total: decimal; LineItems: OrderLine list }
"""
        let fsproj = createTempProject [ "Types.fs", source ]

        try
            let types = Extractor.extractTypeInfos fsproj

            let order =
                types |> List.tryFind (fun t -> t.LocalName = "Order")

            Expect.isSome order "Order type should be found"
            let order = order.Value
            Expect.equal order.FullName "MyApp.Order" "FullName should be namespace-qualified"
            Expect.equal order.Namespace "MyApp" "Namespace should be MyApp"

            let fieldNames = order.Fields |> List.map (fun f -> f.Name)
            Expect.contains fieldNames "Total" "Should have Total field"
            Expect.contains fieldNames "LineItems" "Should have LineItems field"

            let total = order.Fields |> List.find (fun f -> f.Name = "Total")
            Expect.equal total.TypeName "decimal" "Total should be decimal"
        finally
            deleteTempProject fsproj)

[<Tests>]
let at2DuExtract =
    testCase "AT2: discriminated unions extract" (fun () ->
        let source =
            """namespace MyApp
type Status =
    | Pending
    | Shipped of trackingNumber: string
"""
        let fsproj = createTempProject [ "Status.fs", source ]

        try
            let types = Extractor.extractTypeInfos fsproj

            let status =
                types |> List.tryFind (fun t -> t.LocalName = "Status")

            Expect.isSome status "Status DU should be found"
            let status = status.Value
            let caseNames = status.Fields |> List.map (fun f -> f.Name)
            Expect.contains caseNames "Pending" "Should have Pending case"
            Expect.contains caseNames "Shipped" "Should have Shipped case"

            let shipped =
                status.Fields |> List.find (fun f -> f.Name = "Shipped")

            Expect.equal shipped.TypeName "string" "Shipped case should have string field type"
        finally
            deleteTempProject fsproj)

[<Tests>]
let at3AttributesPreserved =
    testCase "AT3: attributes preserved" (fun () ->
        let source =
            """namespace MyApp
open System.Text.Json.Serialization
type Order =
    { [<JsonPropertyName("total_paid")>] Total: decimal }
"""
        let fsproj = createTempProject [ "Order.fs", source ]

        try
            let types = Extractor.extractTypeInfos fsproj

            let order =
                types |> List.tryFind (fun t -> t.LocalName = "Order")

            Expect.isSome order "Order type should be found"
            let order = order.Value
            let total = order.Fields |> List.find (fun f -> f.Name = "Total")

            Expect.isTrue
                (total.Attributes.ContainsKey("JsonPropertyName"))
                "Should have JsonPropertyName attribute"

            Expect.equal
                total.Attributes.["JsonPropertyName"]
                "total_paid"
                "JsonPropertyName value should be total_paid"
        finally
            deleteTempProject fsproj)

[<Tests>]
let at4DocCommentsPreserved =
    testCase "AT4: doc comments preserved" (fun () ->
        let source =
            """namespace MyApp
/// The customer who placed the order.
type Order = { Customer: string }
"""
        let fsproj = createTempProject [ "Order.fs", source ]

        try
            let types = Extractor.extractTypeInfos fsproj

            let order =
                types |> List.tryFind (fun t -> t.LocalName = "Order")

            Expect.isSome order "Order type should be found"
            let order = order.Value
            Expect.isSome order.DocComment "DocComment should be Some"

            let doc = order.DocComment.Value
            Expect.isTrue (doc.Contains("customer who placed the order")) "Doc comment should contain description"
        finally
            deleteTempProject fsproj)

[<Tests>]
let at5CrossProjectSkipped =
    testCase "AT5: cross-project references skipped" (fun () ->
        // Use a type from System.Text.Json (NuGet/framework) — should not appear in results
        let source =
            """namespace MyApp
open System.Text.Json
type Config = { JsonOptions: JsonSerializerOptions option; Name: string }
"""
        let fsproj = createTempProject [ "Config.fs", source ]

        try
            let types = Extractor.extractTypeInfos fsproj

            // Only MyApp.Config should appear — not JsonSerializerOptions
            let externalType =
                types
                |> List.tryFind (fun t -> t.LocalName = "JsonSerializerOptions")

            Expect.isNone externalType "JsonSerializerOptions should not appear in results"

            let config =
                types |> List.tryFind (fun t -> t.LocalName = "Config")

            Expect.isSome config "Config type should be found"
        finally
            deleteTempProject fsproj)
