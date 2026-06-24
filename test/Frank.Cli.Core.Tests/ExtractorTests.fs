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

type Move =
    | XMove of position: SquarePosition
    | OMove of SquarePosition

and SquarePosition = { Row: int; Col: int }
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

              match order.Shape with
              | TypeShape.Record fields ->
                  let names = fields |> List.map (fun f -> f.Name)
                  Expect.contains names "Total" "Total field"
                  Expect.contains names "LineItems" "LineItems field"
              | TypeShape.Union _ -> failwith "Order should be a Record"
          }

          test "Total field TypeName contains decimal" {
              let result = Extractor.extractTypeInfosFromSource recordSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let order = types |> List.find (fun t -> t.LocalName = "Order")

              match order.Shape with
              | TypeShape.Record fields ->
                  let total = fields |> List.find (fun f -> f.Name = "Total")
                  Expect.stringContains total.TypeName "decimal" "Total TypeName"
              | TypeShape.Union _ -> failwith "Order should be a Record"
          }

          test "LineItems field TypeName mentions OrderLine" {
              let result = Extractor.extractTypeInfosFromSource recordSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let order = types |> List.find (fun t -> t.LocalName = "Order")

              match order.Shape with
              | TypeShape.Record fields ->
                  let li = fields |> List.find (fun f -> f.Name = "LineItems")
                  Expect.stringContains li.TypeName "OrderLine" "LineItems TypeName mentions OrderLine"
              | TypeShape.Union _ -> failwith "Order should be a Record"
          } ]

// ── AT2: discriminated unions ─────────────────────────────────────────────────

[<Tests>]
let at2DuTests =
    testList
        "AT2 - DU extraction (sum-aware)"
        [ test "Status is a Union shape" {
              let types = Expect.wantOk (Extractor.extractTypeInfosFromSource duSource) "extract"

              let status = types |> List.find (fun t -> t.LocalName = "Status")

              match status.Shape with
              | TypeShape.Union cases ->
                  let names = cases |> List.map (fun c -> c.Name)
                  Expect.contains names "Pending" "Pending case"
                  Expect.contains names "Shipped" "Shipped case"
              | TypeShape.Record _ -> failwith "Status should be a Union"
          }

          test "nullary case has empty payload" {
              let types = Expect.wantOk (Extractor.extractTypeInfosFromSource duSource) "extract"

              let status = types |> List.find (fun t -> t.LocalName = "Status")

              match status.Shape with
              | TypeShape.Union cases ->
                  let pending = cases |> List.find (fun c -> c.Name = "Pending")
                  Expect.isEmpty pending.Payload "Pending has no payload"
              | TypeShape.Record _ -> failwith "Status should be a Union"
          }

          test "labeled payload uses the label as field name" {
              let types = Expect.wantOk (Extractor.extractTypeInfosFromSource duSource) "extract"

              let status = types |> List.find (fun t -> t.LocalName = "Status")

              match status.Shape with
              | TypeShape.Union cases ->
                  let shipped = cases |> List.find (fun c -> c.Name = "Shipped")
                  let f = shipped.Payload |> List.exactlyOne
                  Expect.equal f.Name "trackingNumber" "label is the field name"
                  Expect.stringContains f.TypeName "string" "payload type"
              | TypeShape.Record _ -> failwith "Status should be a Union"
          }

          test "record type is a Record shape" {
              let types = Expect.wantOk (Extractor.extractTypeInfosFromSource duSource) "extract"

              let sq = types |> List.find (fun t -> t.LocalName = "SquarePosition")

              match sq.Shape with
              | TypeShape.Record fields ->
                  let names = fields |> List.map (fun f -> f.Name)
                  Expect.contains names "Row" "Row field"
                  Expect.contains names "Col" "Col field"
              | TypeShape.Union _ -> failwith "SquarePosition should be a Record"
          }

          test "unlabeled payload falls back to the payload type name" {
              let types = Expect.wantOk (Extractor.extractTypeInfosFromSource duSource) "extract"

              let move = types |> List.find (fun t -> t.LocalName = "Move")

              match move.Shape with
              | TypeShape.Union cases ->
                  let omove = cases |> List.find (fun c -> c.Name = "OMove")
                  let f = omove.Payload |> List.exactlyOne
                  Expect.equal f.Name "SquarePosition" "unlabeled payload → type name, not 'Item'"
                  Expect.stringContains f.TypeName "SquarePosition" "payload TypeName"
              | TypeShape.Record _ -> failwith "Move should be a Union"
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

              match order.Shape with
              | TypeShape.Record fields ->
                  let total = fields |> List.find (fun f -> f.Name = "Total")
                  Expect.isTrue (total.Attributes.ContainsKey "JsonPropertyName") "JsonPropertyName key present"
              | TypeShape.Union _ -> failwith "Order should be a Record"
          }

          test "Total JsonPropertyName value is total_paid" {
              let result = Extractor.extractTypeInfosFromSource attributeSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let order = types |> List.find (fun t -> t.LocalName = "Order")

              match order.Shape with
              | TypeShape.Record fields ->
                  let total = fields |> List.find (fun f -> f.Name = "Total")
                  Expect.equal total.Attributes.["JsonPropertyName"] "total_paid" "JsonPropertyName value"
              | TypeShape.Union _ -> failwith "Order should be a Record"
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

              match order.Shape with
              | TypeShape.Record fields ->
                  let customer = fields |> List.find (fun f -> f.Name = "Customer")
                  Expect.isSome customer.DocComment "Customer field DocComment present"
              | TypeShape.Union _ -> failwith "Order should be a Record"
          }

          test "Customer field DocComment contains expected text" {
              let result = Extractor.extractTypeInfosFromSource docCommentSource
              let types = Expect.wantOk result "extractTypeInfosFromSource should succeed"
              let order = types |> List.find (fun t -> t.LocalName = "Order")

              match order.Shape with
              | TypeShape.Record fields ->
                  let customer = fields |> List.find (fun f -> f.Name = "Customer")
                  let comment = customer.DocComment |> Option.defaultValue ""
                  Expect.stringContains comment "name" "Customer DocComment mentions name"
              | TypeShape.Union _ -> failwith "Order should be a Record"
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

// ── AT6: extractTypeInfosFromSources ─────────────────────────────────────────

let private sdkRefs () =
    let checker = FSharp.Compiler.CodeAnalysis.FSharpChecker.Create()
    let src = FSharp.Compiler.Text.SourceText.ofString "let x = 1"

    let opts, _ =
        checker.GetProjectOptionsFromScript(
            "/tmp/frank_sdk_probe.fsx",
            src,
            assumeDotNetFramework = false,
            useSdkRefs = true
        )
        |> Async.RunSynchronously

    opts.OtherOptions
    |> Array.choose (fun o ->
        if o.StartsWith("-r:", StringComparison.Ordinal) then
            Some(o.[3..])
        else
            None)

[<Tests>]
let at6SourceSetTests =
    testList
        "AT6 - extractTypeInfosFromSources"
        [ test "Move.position field TypeName is int" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let src =
                      """namespace MultiSrc

type Move = { position: int; notes: string option; tags: string list }
"""

                  let file = Path.Combine(tmpDir, "Move.fs")
                  File.WriteAllText(file, src)

                  let refs = sdkRefs ()
                  let result = Extractor.extractTypeInfosFromSources [| file |] refs
                  let types = Expect.wantOk result "extractTypeInfosFromSources should succeed"
                  let move = types |> List.find (fun t -> t.LocalName = "Move")

                  match move.Shape with
                  | TypeShape.Record fields ->
                      let pos = fields |> List.find (fun f -> f.Name = "position")
                      Expect.equal pos.TypeName "int" "position TypeName"
                  | TypeShape.Union _ -> failwith "Move should be a Record"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "Move.notes field TypeName is string option" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let src =
                      """namespace MultiSrc

type Move = { position: int; notes: string option; tags: string list }
"""

                  let file = Path.Combine(tmpDir, "Move.fs")
                  File.WriteAllText(file, src)

                  let refs = sdkRefs ()
                  let result = Extractor.extractTypeInfosFromSources [| file |] refs
                  let types = Expect.wantOk result "extractTypeInfosFromSources should succeed"
                  let move = types |> List.find (fun t -> t.LocalName = "Move")

                  match move.Shape with
                  | TypeShape.Record fields ->
                      let notes = fields |> List.find (fun f -> f.Name = "notes")
                      Expect.equal notes.TypeName "string option" "notes TypeName"
                  | TypeShape.Union _ -> failwith "Move should be a Record"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "Move.tags field TypeName is string list" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let src =
                      """namespace MultiSrc

type Move = { position: int; notes: string option; tags: string list }
"""

                  let file = Path.Combine(tmpDir, "Move.fs")
                  File.WriteAllText(file, src)

                  let refs = sdkRefs ()
                  let result = Extractor.extractTypeInfosFromSources [| file |] refs
                  let types = Expect.wantOk result "extractTypeInfosFromSources should succeed"
                  let move = types |> List.find (fun t -> t.LocalName = "Move")

                  match move.Shape with
                  | TypeShape.Record fields ->
                      let tags = fields |> List.find (fun f -> f.Name = "tags")
                      Expect.equal tags.TypeName "string list" "tags TypeName"
                  | TypeShape.Union _ -> failwith "Move should be a Record"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "types from second file are extracted" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let src1 =
                      """namespace MultiSrc

type Move = { position: int; notes: string option; tags: string list }
"""

                  let src2 =
                      """namespace MultiSrc

type Board = { size: int; label: string }
"""

                  let file1 = Path.Combine(tmpDir, "Move.fs")
                  let file2 = Path.Combine(tmpDir, "Board.fs")
                  File.WriteAllText(file1, src1)
                  File.WriteAllText(file2, src2)

                  let refs = sdkRefs ()
                  let result = Extractor.extractTypeInfosFromSources [| file1; file2 |] refs
                  let types = Expect.wantOk result "extractTypeInfosFromSources should succeed"
                  let board = types |> List.tryFind (fun t -> t.LocalName = "Board")
                  Expect.isSome board "Board type from second file extracted"
              finally
                  Directory.Delete(tmpDir, true)
          }

          test "returns Error for missing source file" {
              let result =
                  Extractor.extractTypeInfosFromSources [| "/nonexistent/path/Missing.fs" |] [||]

              Expect.isError result "should return Error for missing file"
          }

          test "raises invalidArg for empty sourceFiles" {
              Expect.throws
                  (fun () -> Extractor.extractTypeInfosFromSources [||] [||] |> ignore)
                  "empty sourceFiles must throw"
          }

          test "returns Error when FCS project check has critical errors (bad assembly ref)" {
              let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let src =
                      """namespace BrokenSrc

type Broken = { Field: int }
"""

                  let file = Path.Combine(tmpDir, "Broken.fs")
                  File.WriteAllText(file, src)

                  let badRefs = [| "/nonexistent/path/DoesNotExist.dll" |]
                  let result = Extractor.extractTypeInfosFromSources [| file |] badRefs
                  Expect.isError result "critical FCS errors must not silently return Ok"
              finally
                  Directory.Delete(tmpDir, true)
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
