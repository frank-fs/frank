module Frank.Validation.Tests.ValidatorTests

open System
open System.Text.Json
open Expecto
open Frank.Validation

// ──────────────────────────────────────────────
// Test domain types
// ──────────────────────────────────────────────

type PersonInput = { Name: string; Age: int }

type PersonWithOptional =
    { Name: string
      Age: int
      Email: string option }

type StatusEnum =
    | Active
    | Inactive
    | Pending

type OrderWithStatus = { OrderId: int; Status: StatusEnum }

// ──────────────────────────────────────────────
// Helpers
// ──────────────────────────────────────────────

/// Build a shapes graph from a type.
let private buildShapes<'T> () =
    ShapeDerivation.clearCache ()
    let shape = ShapeDerivation.deriveShapeDefault typeof<'T>
    let sg = ShapeGraphBuilder.buildShapesGraph shape
    sg, shape

/// Build a data graph from a JSON string using the given shape.
let private buildDataFromJson (shape: ShaclShape) (json: string) =
    let doc = JsonDocument.Parse(json)
    DataGraphBuilder.buildFromJsonBody shape (doc.RootElement.Clone())

/// Run validation end-to-end: derive shape, build shapes graph, parse JSON, build data graph, validate.
let private validateJson<'T> (json: string) =
    let sg, shape = buildShapes<'T> ()
    use dataGraph = buildDataFromJson shape json
    Validator.validate sg shape.NodeShapeUri dataGraph

// ──────────────────────────────────────────────
// T020: Validator tests
// ──────────────────────────────────────────────

[<Tests>]
let shapesGraphConstructionTests =
    testList
        "ShapeGraphBuilder"
        [ testCase "builds a ShapesGraph from a simple record shape"
          <| fun _ ->
              let sg, _shape = buildShapes<PersonInput> ()
              Expect.isNotNull (box sg) "ShapesGraph should not be null"

          testCase "builds a ShapesGraph from a record with optional field"
          <| fun _ ->
              let sg, _shape = buildShapes<PersonWithOptional> ()
              Expect.isNotNull (box sg) "ShapesGraph should not be null"

          testCase "builds a ShapesGraph from a record with DU field"
          <| fun _ ->
              let sg, _shape = buildShapes<OrderWithStatus> ()
              Expect.isNotNull (box sg) "ShapesGraph should not be null" ]

[<Tests>]
let validDataTests =
    testList
        "Validator - valid data conformance"
        [ testCase "valid JSON with all required fields conforms"
          <| fun _ ->
              let report = validateJson<PersonInput> """{"Name":"Alice","Age":30}"""
              Expect.isTrue report.Conforms "Should conform with all required fields"
              Expect.isEmpty report.Results "Should have no violations"

          testCase "valid JSON with optional field present conforms"
          <| fun _ ->
              let report =
                  validateJson<PersonWithOptional> """{"Name":"Alice","Age":30,"Email":"a@b.com"}"""

              Expect.isTrue report.Conforms "Should conform with optional field present"

          testCase "valid JSON with optional field absent conforms"
          <| fun _ ->
              let report = validateJson<PersonWithOptional> """{"Name":"Alice","Age":30}"""
              Expect.isTrue report.Conforms "Should conform with optional field absent" ]

[<Tests>]
let missingFieldTests =
    testList
        "Validator - missing required field"
        [ testCase "missing required field produces violation"
          <| fun _ ->
              let report = validateJson<PersonInput> """{"Age":30}"""
              Expect.isFalse report.Conforms "Should not conform with missing required field"
              Expect.isNonEmpty report.Results "Should have at least one violation"

          testCase "missing all required fields produces multiple violations"
          <| fun _ ->
              let report = validateJson<PersonInput> """{}"""
              Expect.isFalse report.Conforms "Should not conform with empty object"
              Expect.isGreaterThanOrEqual report.Results.Length 2 "Should have at least 2 violations" ]

[<Tests>]
let wrongDatatypeTests =
    testList
        "Validator - wrong datatype"
        [ testCase "string value for integer field produces violation"
          <| fun _ ->
              let report = validateJson<PersonInput> """{"Name":"Alice","Age":"thirty"}"""
              Expect.isFalse report.Conforms "String for int should not conform"
              Expect.isNonEmpty report.Results "Should have violation" ]

[<Tests>]
let inConstraintTests =
    testList
        "Validator - sh:in constraint"
        [ testCase "valid enum value conforms"
          <| fun _ ->
              let report = validateJson<OrderWithStatus> """{"OrderId":1,"Status":"Active"}"""
              Expect.isTrue report.Conforms "Valid enum value should conform"

          testCase "invalid enum value produces violation"
          <| fun _ ->
              let report = validateJson<OrderWithStatus> """{"OrderId":1,"Status":"Unknown"}"""
              Expect.isFalse report.Conforms "Invalid enum value should not conform"
              Expect.isNonEmpty report.Results "Should have violation for sh:in" ]

[<Tests>]
let optionalFieldTests =
    testList
        "Validator - optional field absent"
        [ testCase "optional field absent with minCount 0 conforms"
          <| fun _ ->
              let report = validateJson<PersonWithOptional> """{"Name":"Alice","Age":25}"""
              Expect.isTrue report.Conforms "Optional field absent should conform"

          testCase "optional field null with minCount 0 conforms"
          <| fun _ ->
              let report =
                  validateJson<PersonWithOptional> """{"Name":"Alice","Age":25,"Email":null}"""

              Expect.isTrue report.Conforms "Optional field null should conform" ]

[<Tests>]
let multipleViolationTests =
    testList
        "Validator - multiple violations"
        [ testCase "multiple issues produce multiple results"
          <| fun _ ->
              // Missing Name (required), Age is wrong type
              let report = validateJson<PersonInput> """{"Age":"notAnInt"}"""
              Expect.isFalse report.Conforms "Should not conform"
              Expect.isGreaterThanOrEqual report.Results.Length 2 "Should have multiple violations" ]
