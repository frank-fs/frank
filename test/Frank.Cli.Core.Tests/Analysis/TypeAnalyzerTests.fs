module Frank.Cli.Core.Tests.Analysis.TypeAnalyzerTests

open System.IO
open Expecto
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Text
open Frank.Resources.Model
open Frank.Cli.Core.Analysis

let private checker = FSharpChecker.Create()

let private checkSource (source: string) =
    async {
        let tmpFile =
            Path.Combine(Path.GetTempPath(), $"frank_test_{System.Guid.NewGuid():N}.fsx")

        try
            File.WriteAllText(tmpFile, source)
            let sourceText = SourceText.ofString source

            let! options, _ =
                checker.GetProjectOptionsFromScript(
                    tmpFile,
                    sourceText,
                    assumeDotNetFramework = false,
                    useSdkRefs = true
                )

            let! projectResults = checker.ParseAndCheckProject(options)
            return projectResults
        finally
            if File.Exists tmpFile then
                File.Delete tmpFile
    }

/// Compile multiple F# source files as a project (not script).
/// This gives FCS full attribute resolution, unlike .fsx mode.
let private checkProjectSources (sources: (string * string) list) =
    async {
        let uid = System.Guid.NewGuid().ToString("N")
        let tmpDir = Path.Combine(Path.GetTempPath(), $"frank_proj_{uid}")
        Directory.CreateDirectory(tmpDir) |> ignore

        try
            let filePaths =
                sources
                |> List.mapi (fun i (name, content) ->
                    let path = Path.Combine(tmpDir, name)
                    File.WriteAllText(path, content)
                    path)

            // Get SDK refs from a dummy script to reuse system assembly references
            let dummyFile = Path.Combine(tmpDir, "dummy.fsx")
            File.WriteAllText(dummyFile, "()")
            let dummyText = SourceText.ofString "()"

            let! scriptOpts, _ =
                checker.GetProjectOptionsFromScript(
                    dummyFile,
                    dummyText,
                    assumeDotNetFramework = false,
                    useSdkRefs = true
                )

            // Build project args: reuse script's OtherOptions (assembly refs) + our source files
            let args =
                [| yield "--noframework"
                   yield "--targetprofile:netcore"
                   yield! scriptOpts.OtherOptions |> Array.filter (fun o -> o.StartsWith("-r:"))
                   yield! filePaths |]

            let projectOptions =
                checker.GetProjectOptionsFromCommandLineArgs($"frank_proj_{uid}.fsproj", args)

            let! projectResults = checker.ParseAndCheckProject(projectOptions)
            return projectResults
        finally
            try
                Directory.Delete(tmpDir, true)
            with _ ->
                ()
    }

[<Tests>]
let tests =
    testList
        "TypeAnalyzer"
        [
          // ---------------------------------------------------------------
          // Pre-existing tests (backwards compatibility)
          // ---------------------------------------------------------------

          testCaseAsync "record with primitive fields"
          <| async {
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

          testCaseAsync "discriminated union"
          <| async {
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

          testCaseAsync "optional and list fields"
          <| async {
              let source =
                  """
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

          testCaseAsync "reference type field"
          <| async {
              let source =
                  """
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

          // ---------------------------------------------------------------
          // T051: New primitive type mappings
          // ---------------------------------------------------------------

          testCaseAsync "DateOnly maps to xsd:date"
          <| async {
              let source =
                  """
open System
type Event = { Id: int; OccursOn: DateOnly }
"""

              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let event = types |> List.tryFind (fun t -> t.ShortName = "Event")
              Expect.isSome event "Should find Event type"

              match event.Value.Kind with
              | Record fields ->
                  let field = fields |> List.find (fun f -> f.Name = "OccursOn")
                  Expect.equal field.Kind (Primitive "xsd:date") "DateOnly should map to xsd:date"
              | _ -> failwith "Event should be a Record"
          }

          testCaseAsync "TimeOnly maps to xsd:time"
          <| async {
              let source =
                  """
open System
type Schedule = { Id: int; StartsAt: TimeOnly }
"""

              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let schedule = types |> List.tryFind (fun t -> t.ShortName = "Schedule")
              Expect.isSome schedule "Should find Schedule type"

              match schedule.Value.Kind with
              | Record fields ->
                  let field = fields |> List.find (fun f -> f.Name = "StartsAt")
                  Expect.equal field.Kind (Primitive "xsd:time") "TimeOnly should map to xsd:time"
              | _ -> failwith "Schedule should be a Record"
          }

          testCaseAsync "TimeSpan maps to xsd:duration"
          <| async {
              let source =
                  """
open System
type Task = { Id: int; Duration: TimeSpan }
"""

              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let task = types |> List.tryFind (fun t -> t.ShortName = "Task")
              Expect.isSome task "Should find Task type"

              match task.Value.Kind with
              | Record fields ->
                  let field = fields |> List.find (fun f -> f.Name = "Duration")
                  Expect.equal field.Kind (Primitive "xsd:duration") "TimeSpan should map to xsd:duration"
              | _ -> failwith "Task should be a Record"
          }

          testCaseAsync "Uri maps to xsd:anyURI"
          <| async {
              let source =
                  """
open System
type Link = { Id: int; Href: Uri }
"""

              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let link = types |> List.tryFind (fun t -> t.ShortName = "Link")
              Expect.isSome link "Should find Link type"

              match link.Value.Kind with
              | Record fields ->
                  let field = fields |> List.find (fun f -> f.Name = "Href")
                  Expect.equal field.Kind (Primitive "xsd:anyURI") "Uri should map to xsd:anyURI"
              | _ -> failwith "Link should be a Record"
          }

          testCaseAsync "Guid maps to Guid FieldKind (not Primitive xsd:string)"
          <| async {
              let source =
                  """
open System
type Entity = { Id: Guid; Name: string }
"""

              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let entity = types |> List.tryFind (fun t -> t.ShortName = "Entity")
              Expect.isSome entity "Should find Entity type"

              match entity.Value.Kind with
              | Record fields ->
                  let field = fields |> List.find (fun f -> f.Name = "Id")
                  Expect.equal field.Kind FieldKind.Guid "System.Guid should map to FieldKind.Guid"
                  // Ensure it is NOT Primitive "xsd:string"
                  Expect.notEqual field.Kind (Primitive "xsd:string") "Guid should not be Primitive xsd:string"
              | _ -> failwith "Entity should be a Record"
          }

          testCaseAsync "Decimal maps to xsd:decimal (not xsd:double)"
          <| async {
              let source =
                  """
type Price = { Amount: decimal; Currency: string }
"""

              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let price = types |> List.tryFind (fun t -> t.ShortName = "Price")
              Expect.isSome price "Should find Price type"

              match price.Value.Kind with
              | Record fields ->
                  let field = fields |> List.find (fun f -> f.Name = "Amount")
                  Expect.equal field.Kind (Primitive "xsd:decimal") "decimal should map to xsd:decimal"
                  Expect.notEqual field.Kind (Primitive "xsd:double") "decimal should not map to xsd:double"
              | _ -> failwith "Price should be a Record"
          }

          testCaseAsync "byte array maps to xsd:base64Binary (not Collection)"
          <| async {
              let source =
                  """
type Document = { Id: int; Content: byte array }
"""

              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let doc = types |> List.tryFind (fun t -> t.ShortName = "Document")
              Expect.isSome doc "Should find Document type"

              match doc.Value.Kind with
              | Record fields ->
                  let field = fields |> List.find (fun f -> f.Name = "Content")
                  Expect.equal field.Kind (Primitive "xsd:base64Binary") "byte[] should map to xsd:base64Binary"
                  // Ensure it is NOT a Collection
                  match field.Kind with
                  | Collection _ -> failwith "byte[] should not map to Collection"
                  | _ -> ()
              | _ -> failwith "Document should be a Record"
          }

          testCaseAsync "record with all new primitive types maps correctly"
          <| async {
              let source =
                  """
open System
type RichRecord = {
    Date: DateOnly
    Time: TimeOnly
    Duration: TimeSpan
    WebAddress: Uri
    UniqueId: Guid
    Price: decimal
    Payload: byte array
}
"""

              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let t = types |> List.tryFind (fun t -> t.ShortName = "RichRecord")
              Expect.isSome t "Should find RichRecord type"

              match t.Value.Kind with
              | Record fields ->
                  let findField name =
                      fields |> List.find (fun f -> f.Name = name)

                  Expect.equal (findField "Date").Kind (Primitive "xsd:date") "DateOnly -> xsd:date"
                  Expect.equal (findField "Time").Kind (Primitive "xsd:time") "TimeOnly -> xsd:time"
                  Expect.equal (findField "Duration").Kind (Primitive "xsd:duration") "TimeSpan -> xsd:duration"
                  Expect.equal (findField "WebAddress").Kind (Primitive "xsd:anyURI") "Uri -> xsd:anyURI"
                  Expect.equal (findField "UniqueId").Kind FieldKind.Guid "Guid -> FieldKind.Guid"
                  Expect.equal (findField "Price").Kind (Primitive "xsd:decimal") "decimal -> xsd:decimal"
                  Expect.equal (findField "Payload").Kind (Primitive "xsd:base64Binary") "byte[] -> xsd:base64Binary"
              | _ -> failwith "RichRecord should be a Record"
          }

          // ---------------------------------------------------------------
          // T052: IsScalar flag
          // ---------------------------------------------------------------

          testCaseAsync "scalar field has IsScalar = true"
          <| async {
              let source =
                  """
type Item = { Name: string; Quantity: int }
"""

              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let item = types |> List.tryFind (fun t -> t.ShortName = "Item")
              Expect.isSome item "Should find Item type"

              match item.Value.Kind with
              | Record fields ->
                  let nameField = fields |> List.find (fun f -> f.Name = "Name")
                  Expect.isTrue nameField.IsScalar "string field should have IsScalar = true"
                  let qtyField = fields |> List.find (fun f -> f.Name = "Quantity")
                  Expect.isTrue qtyField.IsScalar "int field should have IsScalar = true"
              | _ -> failwith "Item should be a Record"
          }

          testCaseAsync "collection field has IsScalar = false"
          <| async {
              let source =
                  """
type Catalog = { Name: string; Tags: string list; Scores: int[] }
"""

              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let catalog = types |> List.tryFind (fun t -> t.ShortName = "Catalog")
              Expect.isSome catalog "Should find Catalog type"

              match catalog.Value.Kind with
              | Record fields ->
                  let tagsField = fields |> List.find (fun f -> f.Name = "Tags")
                  Expect.isFalse tagsField.IsScalar "string list field should have IsScalar = false"
              | _ -> failwith "Catalog should be a Record"
          }

          testCaseAsync "optional field has IsScalar = true"
          <| async {
              let source =
                  """
type Profile = { Name: string; Bio: string option }
"""

              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let profile = types |> List.tryFind (fun t -> t.ShortName = "Profile")
              Expect.isSome profile "Should find Profile type"

              match profile.Value.Kind with
              | Record fields ->
                  let bioField = fields |> List.find (fun f -> f.Name = "Bio")
                  Expect.isTrue bioField.IsScalar "string option field should have IsScalar = true"
              | _ -> failwith "Profile should be a Record"
          }

          testCaseAsync "seq collection field has IsScalar = false"
          <| async {
              let source =
                  """
type Feed = { Title: string; Items: string seq }
"""

              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let feed = types |> List.tryFind (fun t -> t.ShortName = "Feed")
              Expect.isSome feed "Should find Feed type"

              match feed.Value.Kind with
              | Record fields ->
                  let itemsField = fields |> List.find (fun f -> f.Name = "Items")
                  Expect.isFalse itemsField.IsScalar "string seq field should have IsScalar = false"
              | _ -> failwith "Feed should be a Record"
          }

          // ---------------------------------------------------------------
          // T052: IsClosed flag
          // ---------------------------------------------------------------

          testCaseAsync "record type has IsClosed = true"
          <| async {
              let source =
                  """
type Address = { Street: string; City: string }
"""

              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let address = types |> List.tryFind (fun t -> t.ShortName = "Address")
              Expect.isSome address "Should find Address type"
              Expect.isTrue address.Value.IsClosed "Record type should have IsClosed = true"
          }

          testCaseAsync "discriminated union type has IsClosed = false"
          <| async {
              let source =
                  """
type Shape = Circle of radius: float | Rectangle of width: float * height: float
"""

              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let shape = types |> List.tryFind (fun t -> t.ShortName = "Shape")
              Expect.isSome shape "Should find Shape type"
              Expect.isFalse shape.Value.IsClosed "DU type should have IsClosed = false"
          }

          testCaseAsync "enum type has IsClosed = false"
          <| async {
              let source =
                  """
type Color = Red = 0 | Green = 1 | Blue = 2
"""

              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let color = types |> List.tryFind (fun t -> t.ShortName = "Color")
              Expect.isSome color "Should find Color type"
              Expect.isFalse color.Value.IsClosed "Enum type should have IsClosed = false"
          }

          testCaseAsync "both record and DU in same source have correct IsClosed flags"
          <| async {
              let source =
                  """
type Status = Active | Inactive
type Account = { Id: int; Status: Status }
"""

              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let status = types |> List.tryFind (fun t -> t.ShortName = "Status")
              Expect.isSome status "Should find Status type"
              Expect.isFalse status.Value.IsClosed "DU Status should have IsClosed = false"
              let account = types |> List.tryFind (fun t -> t.ShortName = "Account")
              Expect.isSome account "Should find Account type"
              Expect.isTrue account.Value.IsClosed "Record Account should have IsClosed = true"
          }

          // ---------------------------------------------------------------
          // T053: Constraint attribute extraction
          // Uses a real .fsproj fixture (Fixtures/Fixtures.fsproj) compiled via
          // ProjectLoader for full attribute resolution — FCS script mode
          // does not expose FieldAttributes on record fields.
          // ---------------------------------------------------------------

          testCaseAsync "field with no attributes has empty Constraints list"
          <| async {
              let source = "type Product = { Id: int; Name: string }"
              let! projectResults = checkSource source
              let types = TypeAnalyzer.analyzeTypes projectResults
              let product = types |> List.tryFind (fun t -> t.ShortName = "Product")
              Expect.isSome product "Should find Product type"

              match product.Value.Kind with
              | Record fields ->
                  let nameField = fields |> List.find (fun f -> f.Name = "Name")
                  Expect.isEmpty nameField.Constraints "Field with no attributes should have empty Constraints"
              | _ -> failwith "Product should be a Record"
          }

          testCaseAsync "constraint attributes extracted from fixture project"
          <| async {
              let fixturesPath =
                  Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "Fixtures.fsproj"))

              let! result = ProjectLoader.loadProject fixturesPath

              let checkResults =
                  match result with
                  | Ok p -> p.CheckResults
                  | Error e -> failwith $"Failed to load fixtures project: {e}"

              let types = TypeAnalyzer.analyzeTypes checkResults

              let findRecord name =
                  types |> List.find (fun t -> t.ShortName = name)

              let getFields t =
                  match t.Kind with
                  | Record fields -> fields
                  | _ -> failwith $"{t.ShortName} should be a Record"

              // PhoneRecord: Pattern attribute
              let phoneFields = findRecord "PhoneRecord" |> getFields
              let phoneField = phoneFields |> List.find (fun f -> f.Name = "PhoneNumber")

              let hasPattern =
                  phoneField.Constraints
                  |> List.exists (fun c ->
                      match c with
                      | PatternAttr _ -> true
                      | _ -> false)

              Expect.isTrue hasPattern "PhoneNumber should have PatternAttr"
              let nameField = phoneFields |> List.find (fun f -> f.Name = "Name")
              Expect.isEmpty nameField.Constraints "Name field should have no constraints"

              // UserRecord: MinLength attribute
              let userFields = findRecord "UserRecord" |> getFields
              let usernameField = userFields |> List.find (fun f -> f.Name = "Username")

              let minLength =
                  usernameField.Constraints
                  |> List.tryPick (fun c ->
                      match c with
                      | MinLengthAttr n -> Some n
                      | _ -> None)

              Expect.isSome minLength "Username should have MinLengthAttr"
              Expect.equal minLength.Value 3 "MinLength value should be 3"

              // PostRecord: MaxLength attribute
              let postFields = findRecord "PostRecord" |> getFields
              let bodyField = postFields |> List.find (fun f -> f.Name = "Body")

              let maxLength =
                  bodyField.Constraints
                  |> List.tryPick (fun c ->
                      match c with
                      | MaxLengthAttr n -> Some n
                      | _ -> None)

              Expect.isSome maxLength "Body should have MaxLengthAttr"
              Expect.equal maxLength.Value 280 "MaxLength value should be 280"

              // RatingRecord: MinInclusive + MaxInclusive
              let ratingFields = findRecord "RatingRecord" |> getFields
              let scoreField = ratingFields |> List.find (fun f -> f.Name = "Score")

              let hasMinInclusive =
                  scoreField.Constraints
                  |> List.exists (fun c ->
                      match c with
                      | MinInclusiveAttr _ -> true
                      | _ -> false)

              let hasMaxInclusive =
                  scoreField.Constraints
                  |> List.exists (fun c ->
                      match c with
                      | MaxInclusiveAttr _ -> true
                      | _ -> false)

              Expect.isTrue hasMinInclusive "Score should have MinInclusiveAttr"
              Expect.isTrue hasMaxInclusive "Score should have MaxInclusiveAttr"

              // UsernameRecord: multiple attributes
              let unFields = findRecord "UsernameRecord" |> getFields
              let unField = unFields |> List.find (fun f -> f.Name = "Username")
              Expect.isGreaterThanOrEqual unField.Constraints.Length 2 "Username should have at least 2 constraints"

              let hasMinLen =
                  unField.Constraints
                  |> List.exists (fun c ->
                      match c with
                      | MinLengthAttr _ -> true
                      | _ -> false)

              let hasMaxLen =
                  unField.Constraints
                  |> List.exists (fun c ->
                      match c with
                      | MaxLengthAttr _ -> true
                      | _ -> false)

              Expect.isTrue hasMinLen "Username should have MinLengthAttr"
              Expect.isTrue hasMaxLen "Username should have MaxLengthAttr"
          } ]
