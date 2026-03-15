module Frank.Validation.Tests.ShapeDerivationTests

open System
open Expecto
open Frank.Validation

// ──────────────────────────────────────────────
// Test domain types
// ──────────────────────────────────────────────

type SimpleRecord =
    { Name: string
      Age: int
      Email: string option }

type Address =
    { Street: string
      City: string
      ZipCode: string }

type NestedRecord =
    { Customer: SimpleRecord; OrderId: int }

type PaymentMethod =
    | CreditCard
    | BankTransfer
    | Crypto

type Shape =
    | Circle of radius: float
    | Rectangle of width: float * height: float

type TreeNode =
    { Value: string
      Children: TreeNode list }

type PagedResult<'T> =
    { Items: 'T list
      TotalCount: int
      Page: int }

type RecordWithGuid = { Id: Guid; Name: string }

type RecordWithCollections =
    { Tags: string list
      Scores: int[]
      Notes: string option }

type OrderStatus =
    | Submitted
    | Processing
    | Shipped
    | Delivered
    | Cancelled

type OrderWithDu = { OrderId: int; Status: OrderStatus }

// ──────────────────────────────────────────────
// Tests
// ──────────────────────────────────────────────

[<Tests>]
let helperTests =
    testList
        "ShapeBuilder helpers"
        [ testCase "isOptionType detects option"
          <| fun _ ->
              Expect.isTrue (ShapeBuilder.isOptionType typeof<string option>) ""
              Expect.isFalse (ShapeBuilder.isOptionType typeof<string>) ""
              Expect.isFalse (ShapeBuilder.isOptionType typeof<int>) ""

          testCase "unwrapOptionType unwraps option"
          <| fun _ ->
              Expect.equal (ShapeBuilder.unwrapOptionType typeof<string option>) (Some typeof<string>) ""
              Expect.isNone (ShapeBuilder.unwrapOptionType typeof<string>) ""

          testCase "isCollectionType detects lists, arrays, seq"
          <| fun _ ->
              Expect.isTrue (ShapeBuilder.isCollectionType typeof<string list>) "list"
              Expect.isTrue (ShapeBuilder.isCollectionType typeof<int[]>) "array"
              Expect.isTrue (ShapeBuilder.isCollectionType typeof<ResizeArray<string>>) "ResizeArray"
              Expect.isFalse (ShapeBuilder.isCollectionType typeof<string>) "string is not collection"
              Expect.isFalse (ShapeBuilder.isCollectionType typeof<int>) "int is not collection"

          testCase "getCollectionElementType extracts element type"
          <| fun _ ->
              Expect.equal (ShapeBuilder.getCollectionElementType typeof<string list>) typeof<string> "list"
              Expect.equal (ShapeBuilder.getCollectionElementType typeof<int[]>) typeof<int> "array"

          testCase "isDerivableType accepts records and DUs"
          <| fun _ ->
              Expect.isTrue (ShapeBuilder.isDerivableType typeof<SimpleRecord>) "record"
              Expect.isTrue (ShapeBuilder.isDerivableType typeof<PaymentMethod>) "DU"
              Expect.isFalse (ShapeBuilder.isDerivableType typeof<string>) "string"
              Expect.isFalse (ShapeBuilder.isDerivableType typeof<int>) "int"
              Expect.isFalse (ShapeBuilder.isDerivableType null) "null" ]

[<Tests>]
let simpleRecordTests =
    testList
        "ShapeBuilder - simple record"
        [ testCase "derives correct number of properties"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<SimpleRecord>
              Expect.equal (List.length shape.Properties) 3 "Should have 3 properties"

          testCase "derives correct property names"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<SimpleRecord>
              let names = shape.Properties |> List.map (fun p -> p.Path)
              Expect.equal names [ "Name"; "Age"; "Email" ] "Property names should match"

          testCase "derives correct datatypes"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<SimpleRecord>
              let nameP = shape.Properties |> List.find (fun p -> p.Path = "Name")
              let ageP = shape.Properties |> List.find (fun p -> p.Path = "Age")
              Expect.equal nameP.Datatype (Some XsdString) "Name should be XsdString"
              Expect.equal ageP.Datatype (Some XsdInteger) "Age should be XsdInteger"

          testCase "required fields have minCount 1"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<SimpleRecord>
              let nameP = shape.Properties |> List.find (fun p -> p.Path = "Name")
              let ageP = shape.Properties |> List.find (fun p -> p.Path = "Age")
              Expect.equal nameP.MinCount 1 "Name minCount"
              Expect.equal ageP.MinCount 1 "Age minCount"

          testCase "option fields have minCount 0"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<SimpleRecord>
              let emailP = shape.Properties |> List.find (fun p -> p.Path = "Email")
              Expect.equal emailP.MinCount 0 "Email minCount should be 0"
              Expect.equal emailP.MaxCount (Some 1) "Email maxCount should be Some 1"
              Expect.equal emailP.Datatype (Some XsdString) "Email should unwrap to XsdString"

          testCase "shape is closed for records"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<SimpleRecord>
              Expect.isTrue shape.Closed "Record shapes should be closed"

          testCase "NodeShapeUri follows expected pattern"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<SimpleRecord>
              let uriStr = shape.NodeShapeUri.ToString()
              Expect.stringContains uriStr "urn:frank:shape:" "Should start with URN prefix"
              Expect.stringContains uriStr "SimpleRecord" "Should contain the type name" ]

[<Tests>]
let nestedRecordTests =
    testList
        "ShapeBuilder - nested record"
        [ testCase "nested record field has sh:node reference"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<NestedRecord>
              let customerP = shape.Properties |> List.find (fun p -> p.Path = "Customer")
              Expect.isSome customerP.NodeReference "Customer should have NodeReference"
              Expect.isNone customerP.Datatype "Customer should have no datatype"

          testCase "nested shape URI is valid"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<NestedRecord>
              let customerP = shape.Properties |> List.find (fun p -> p.Path = "Customer")
              let nodeRef = customerP.NodeReference.Value
              Expect.stringContains (nodeRef.ToString()) "SimpleRecord" "Should reference SimpleRecord"

          testCase "OrderId field has correct datatype"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<NestedRecord>
              let orderIdP = shape.Properties |> List.find (fun p -> p.Path = "OrderId")
              Expect.equal orderIdP.Datatype (Some XsdInteger) "OrderId should be XsdInteger"
              Expect.equal orderIdP.MinCount 1 "OrderId should be required" ]

[<Tests>]
let simpleDuTests =
    testList
        "ShapeBuilder - simple DU (enum)"
        [ testCase "simple DU produces sh:in constraint on field"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<OrderWithDu>
              let statusP = shape.Properties |> List.find (fun p -> p.Path = "Status")

              Expect.isSome statusP.InValues "Status should have InValues"

              let values = statusP.InValues.Value
              Expect.equal (List.length values) 5 "Should have 5 enum values"
              Expect.contains values "Submitted" "Should contain Submitted"
              Expect.contains values "Processing" "Should contain Processing"
              Expect.contains values "Shipped" "Should contain Shipped"
              Expect.contains values "Delivered" "Should contain Delivered"
              Expect.contains values "Cancelled" "Should contain Cancelled"

          testCase "PaymentMethod DU produces 3 values"
          <| fun _ ->
              ShapeBuilder.clearCache ()

              let duResult = ShapeBuilder.deriveDuConstraint 5 Set.empty typeof<PaymentMethod>

              match duResult with
              | ShapeBuilder.InValues values ->
                  Expect.equal (List.length values) 3 "Should have 3 cases"
                  Expect.contains values "CreditCard" ""
                  Expect.contains values "BankTransfer" ""
                  Expect.contains values "Crypto" ""
              | _ -> failtest "Expected InValues" ]

[<Tests>]
let payloadDuTests =
    testList
        "ShapeBuilder - payload DU"
        [ testCase "payload DU produces sh:or constraint"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let duResult = ShapeBuilder.deriveDuConstraint 5 Set.empty typeof<Shape>

              match duResult with
              | ShapeBuilder.OrShapes uris -> Expect.equal (List.length uris) 2 "Should have 2 case shapes"
              | _ -> failtest "Expected OrShapes"

          testCase "payload DU shape has description"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<Shape>
              Expect.isSome shape.Description "Payload DU should have description"
              Expect.stringContains shape.Description.Value "Payload DU" "" ]

[<Tests>]
let recursiveTypeTests =
    testList
        "ShapeBuilder - recursive types"
        [ testCase "recursive type does not cause infinite loop"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<TreeNode>
              Expect.isNotNull (box shape) "Should produce a shape"
              Expect.equal (List.length shape.Properties) 2 "Should have Value and Children"

          testCase "Value field has correct datatype"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<TreeNode>
              let valueP = shape.Properties |> List.find (fun p -> p.Path = "Value")
              Expect.equal valueP.Datatype (Some XsdString) "Value should be XsdString"

          testCase "Children field has sh:node reference back to TreeNode"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<TreeNode>
              let childrenP = shape.Properties |> List.find (fun p -> p.Path = "Children")
              Expect.isSome childrenP.NodeReference "Children should have NodeReference"

              let nodeRef = childrenP.NodeReference.Value
              Expect.stringContains (nodeRef.ToString()) "TreeNode" "Should reference TreeNode"

          testCase "Children field is collection"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<TreeNode>
              let childrenP = shape.Properties |> List.find (fun p -> p.Path = "Children")
              Expect.isNone childrenP.MaxCount "Collection maxCount should be None (unbounded)"

          testCase "depth limit produces truncated shape"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShape 1 Set.empty typeof<TreeNode>
              Expect.isNotNull (box shape) "Should produce a shape" ]

[<Tests>]
let genericTypeTests =
    testList
        "ShapeBuilder - generic types"
        [ testCase "generic type produces concrete shape"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<PagedResult<SimpleRecord>>
              Expect.isNotNull (box shape) "Should produce a shape"
              Expect.equal (List.length shape.Properties) 3 "Should have Items, TotalCount, Page"

          testCase "generic type URI contains type arguments"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<PagedResult<SimpleRecord>>
              let uriStr = shape.NodeShapeUri.ToString()
              Expect.stringContains uriStr "PagedResult" "Should contain base type name"
              Expect.stringContains uriStr "SimpleRecord" "Should contain type argument"

          testCase "Items property is a collection with node reference"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<PagedResult<SimpleRecord>>
              let itemsP = shape.Properties |> List.find (fun p -> p.Path = "Items")
              Expect.isNone itemsP.MaxCount "Items should be unbounded collection"
              Expect.isSome itemsP.NodeReference "Items should reference SimpleRecord"

          testCase "TotalCount and Page are integers"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<PagedResult<SimpleRecord>>
              let totalP = shape.Properties |> List.find (fun p -> p.Path = "TotalCount")
              let pageP = shape.Properties |> List.find (fun p -> p.Path = "Page")
              Expect.equal totalP.Datatype (Some XsdInteger) "TotalCount"
              Expect.equal pageP.Datatype (Some XsdInteger) "Page" ]

[<Tests>]
let guidTests =
    testList
        "ShapeBuilder - Guid handling"
        [ testCase "Guid field has pattern constraint"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<RecordWithGuid>
              let idP = shape.Properties |> List.find (fun p -> p.Path = "Id")
              Expect.isSome idP.Pattern "Guid should have pattern"
              Expect.stringContains idP.Pattern.Value "[0-9a-fA-F]" "Should be UUID regex"
              Expect.equal idP.Datatype (Some XsdString) "Guid maps to XsdString" ]

[<Tests>]
let collectionTests =
    testList
        "ShapeBuilder - collection types (FR-020)"
        [ testCase "list field has unbounded maxCount"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<RecordWithCollections>
              let tagsP = shape.Properties |> List.find (fun p -> p.Path = "Tags")
              Expect.isNone tagsP.MaxCount "list should have no maxCount (unbounded)"
              Expect.equal tagsP.MinCount 1 "Required list has minCount 1"
              Expect.equal tagsP.Datatype (Some XsdString) "list<string> element type"

          testCase "array field has unbounded maxCount"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let shape = ShapeBuilder.deriveShapeDefault typeof<RecordWithCollections>
              let scoresP = shape.Properties |> List.find (fun p -> p.Path = "Scores")
              Expect.isNone scoresP.MaxCount "array should have no maxCount"
              Expect.equal scoresP.Datatype (Some XsdInteger) "int[] element type" ]

[<Tests>]
let nonDerivableTypeTests =
    testList
        "ShapeBuilder - non-derivable types"
        [ testCase "isDerivableType excludes HttpContext"
          <| fun _ ->
              Expect.isFalse
                  (ShapeBuilder.isDerivableType typeof<Microsoft.AspNetCore.Http.HttpContext>)
                  "HttpContext should not be derivable"

          testCase "isDerivableType excludes CancellationToken"
          <| fun _ ->
              Expect.isFalse
                  (ShapeBuilder.isDerivableType typeof<System.Threading.CancellationToken>)
                  "CancellationToken should not be derivable" ]

[<Tests>]
let buildNodeShapeUriTests =
    testList
        "UriConventions.buildNodeShapeUriFromType"
        [ testCase "simple type URI"
          <| fun _ ->
              let uri = UriConventions.buildNodeShapeUriFromType typeof<SimpleRecord>
              let uriStr = uri.ToString()
              Expect.stringContains uriStr "urn:frank:shape:" "Should use URN prefix"
              Expect.stringContains uriStr "SimpleRecord" "Should contain type name"

          testCase "generic type URI includes type parameters"
          <| fun _ ->
              let uri = UriConventions.buildNodeShapeUriFromType typeof<PagedResult<SimpleRecord>>
              let uriStr = uri.ToString()
              Expect.stringContains uriStr "PagedResult" "Should contain base type"
              Expect.stringContains uriStr "SimpleRecord" "Should contain type argument" ]
