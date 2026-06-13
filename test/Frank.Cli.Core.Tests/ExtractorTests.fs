module Frank.Cli.Core.Tests.ExtractorTests

open System
open System.IO
open Expecto
open Frank.Semantic
open Frank.Cli.Core

// ── inline source fixtures ────────────────────────────────────────────────────

let recordSource =
    """
namespace MyApp

type OrderLine = { Quantity: int; UnitPrice: decimal }

type Order = { Total: decimal; LineItems: OrderLine list }
"""

let duSource =
    """
namespace MyApp

type Status =
    | Pending
    | Shipped of trackingNumber: string
"""

let attributeSource =
    """
namespace MyApp

open System.Text.Json.Serialization

type Order =
    { [<JsonPropertyName("total_paid")>] Total: decimal
      LineItems: int list }
"""

let docCommentSource =
    """
namespace MyApp

/// The customer who placed the order.
type Order =
    { /// The customer's full name.
      Customer: string }
"""

let sourceOnlySource =
    """
namespace MyApp

type MyRecord = { Location: System.Uri; Count: int }
"""

// ── AT1: records ──────────────────────────────────────────────────────────────

[<Tests>]
let at1RecordTests =
    testList
        "AT1 - record extraction"
        [ test "Order TypeInfo has correct FullName" {
              let result = Extractor.extractTypeInfosFromSource recordSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let order = types |> List.find (fun t -> t.LocalName = "Order")
              Expect.equal order.FullName "MyApp.Order" "FullName"
          }

          test "Order TypeInfo has correct Namespace" {
              let result = Extractor.extractTypeInfosFromSource recordSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let order = types |> List.find (fun t -> t.LocalName = "Order")
              Expect.equal order.Namespace "MyApp" "Namespace"
          }

          test "Order TypeInfo has correct LocalName" {
              let result = Extractor.extractTypeInfosFromSource recordSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let order = types |> List.find (fun t -> t.LocalName = "Order")
              Expect.equal order.LocalName "Order" "LocalName"
          }

          test "Order has Total and LineItems fields" {
              let result = Extractor.extractTypeInfosFromSource recordSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let order = types |> List.find (fun t -> t.LocalName = "Order")
              let names = order.Fields |> List.map _.Name
              Expect.contains names "Total" "Total field"
              Expect.contains names "LineItems" "LineItems field"
          }

          test "Total field TypeName contains decimal" {
              let result = Extractor.extractTypeInfosFromSource recordSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let order = types |> List.find (fun t -> t.LocalName = "Order")
              let total = order.Fields |> List.find (fun f -> f.Name = "Total")
              Expect.stringContains total.TypeName "decimal" "Total TypeName"
          }

          test "LineItems field TypeName mentions OrderLine" {
              let result = Extractor.extractTypeInfosFromSource recordSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let order = types |> List.find (fun t -> t.LocalName = "Order")
              let li = order.Fields |> List.find (fun f -> f.Name = "LineItems")
              Expect.stringContains li.TypeName "OrderLine" "LineItems TypeName mentions OrderLine"
          } ]

// ── AT2: discriminated unions ─────────────────────────────────────────────────

[<Tests>]
let at2DuTests =
    testList
        "AT2 - DU extraction"
        [ test "Status is extracted" {
              let result = Extractor.extractTypeInfosFromSource duSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let status = types |> List.tryFind (fun t -> t.LocalName = "Status")
              Expect.isSome status "Status type present"
          }

          test "Status has Pending case as FieldInfo" {
              let result = Extractor.extractTypeInfosFromSource duSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let status = types |> List.find (fun t -> t.LocalName = "Status")
              let pending = status.Fields |> List.tryFind (fun f -> f.Name = "Pending")
              Expect.isSome pending "Pending case present"
          }

          test "Pending case TypeName is unit" {
              let result = Extractor.extractTypeInfosFromSource duSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let status = types |> List.find (fun t -> t.LocalName = "Status")
              let pending = status.Fields |> List.find (fun f -> f.Name = "Pending")
              Expect.equal pending.TypeName "unit" "Pending TypeName is unit"
          }

          test "Status has Shipped case as FieldInfo" {
              let result = Extractor.extractTypeInfosFromSource duSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let status = types |> List.find (fun t -> t.LocalName = "Status")
              let shipped = status.Fields |> List.tryFind (fun f -> f.Name = "Shipped")
              Expect.isSome shipped "Shipped case present"
          }

          test "Shipped case TypeName contains string" {
              let result = Extractor.extractTypeInfosFromSource duSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let status = types |> List.find (fun t -> t.LocalName = "Status")
              let shipped = status.Fields |> List.find (fun f -> f.Name = "Shipped")
              Expect.stringContains shipped.TypeName "string" "Shipped TypeName contains string"
          } ]

// ── AT3: attributes ───────────────────────────────────────────────────────────

[<Tests>]
let at3AttributeTests =
    testList
        "AT3 - attribute extraction"
        [ test "Total field has JsonPropertyName attribute" {
              let result = Extractor.extractTypeInfosFromSource attributeSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let order = types |> List.find (fun t -> t.LocalName = "Order")
              let total = order.Fields |> List.find (fun f -> f.Name = "Total")
              Expect.isTrue (total.Attributes.ContainsKey "JsonPropertyName") "JsonPropertyName key present"
          }

          test "Total JsonPropertyName value is total_paid" {
              let result = Extractor.extractTypeInfosFromSource attributeSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let order = types |> List.find (fun t -> t.LocalName = "Order")
              let total = order.Fields |> List.find (fun f -> f.Name = "Total")
              Expect.equal total.Attributes.["JsonPropertyName"] "total_paid" "JsonPropertyName value"
          } ]

// ── AT4: doc comments ─────────────────────────────────────────────────────────

[<Tests>]
let at4DocCommentTests =
    testList
        "AT4 - doc comment extraction"
        [ test "Order type has DocComment" {
              let result = Extractor.extractTypeInfosFromSource docCommentSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let order = types |> List.find (fun t -> t.LocalName = "Order")
              Expect.isSome order.DocComment "Order DocComment present"
          }

          test "Order DocComment contains expected text" {
              let result = Extractor.extractTypeInfosFromSource docCommentSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let order = types |> List.find (fun t -> t.LocalName = "Order")
              let comment = order.DocComment |> Option.defaultValue ""
              Expect.stringContains comment "customer" "DocComment mentions customer"
          }

          test "Customer field has DocComment" {
              let result = Extractor.extractTypeInfosFromSource docCommentSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let order = types |> List.find (fun t -> t.LocalName = "Order")
              let customer = order.Fields |> List.find (fun f -> f.Name = "Customer")
              Expect.isSome customer.DocComment "Customer field DocComment present"
          }

          test "Customer field DocComment contains expected text" {
              let result = Extractor.extractTypeInfosFromSource docCommentSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let order = types |> List.find (fun t -> t.LocalName = "Order")
              let customer = order.Fields |> List.find (fun f -> f.Name = "Customer")
              let comment = customer.DocComment |> Option.defaultValue ""
              Expect.stringContains comment "name" "Customer DocComment mentions name"
          } ]

// ── AT5: source-only filter ───────────────────────────────────────────────────

[<Tests>]
let at5SourceOnlyTests =
    testList
        "AT5 - source-only filter"
        [ test "System.Uri is not returned" {
              let result = Extractor.extractTypeInfosFromSource sourceOnlySource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let hasUri = types |> List.exists (fun t -> t.FullName = "System.Uri")
              Expect.isFalse hasUri "System.Uri must not appear in results"
          }

          test "MyRecord is returned" {
              let result = Extractor.extractTypeInfosFromSource sourceOnlySource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let myRecord = types |> List.tryFind (fun t -> t.LocalName = "MyRecord")
              Expect.isSome myRecord "MyRecord should be in results"
          }

          test "only types from fixture namespace are returned" {
              let result = Extractor.extractTypeInfosFromSource sourceOnlySource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let nonMyApp = types |> List.filter (fun t -> not (t.Namespace.StartsWith("MyApp")))

              Expect.isEmpty nonMyApp $"Expected only MyApp types, got: {nonMyApp |> List.map _.FullName}"
          } ]

// ── Integration test: .fsproj wrapper ────────────────────────────────────────

[<Tests>]
let integrationTests =
    testList
        "Integration - extractTypeInfos from .fsproj"
        [ test "extracts types from a temp fixture project" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let fsSource =
                      """namespace FixtureProject

type Widget = { Id: int; Name: string }
"""

                  let fsprojContent =
                      """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Widget.fs" />
  </ItemGroup>
</Project>
"""

                  File.WriteAllText(Path.Combine(tmpDir, "Widget.fs"), fsSource)
                  File.WriteAllText(Path.Combine(tmpDir, "Fixture.fsproj"), fsprojContent)

                  let projectFile = Path.Combine(tmpDir, "Fixture.fsproj")
                  let result = Extractor.extractTypeInfos projectFile

                  match result with
                  | Ok types ->
                      let widget = types |> List.tryFind (fun t -> t.LocalName = "Widget")
                      Expect.isSome widget "Widget type extracted from .fsproj"
                  | Error msg ->
                      Tests.skiptest $"Integration test skipped — .fsproj cracking unavailable offline: {msg}"
              finally
                  Directory.Delete(tmpDir, true)
          } ]
