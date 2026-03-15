module Frank.Validation.Tests.ShapeLoaderTests

open System
open System.IO
open System.Reflection
open Expecto
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Writing
open Frank.Validation

// ──────────────────────────────────────────────
// Test domain types (all at module level)
// ──────────────────────────────────────────────

type BasicRecord =
    { Name: string; Age: int; Score: float }

type RecordWithOptional = { Id: int; Label: string option }

type RecordWithGuidId = { Id: Guid; Name: string }

type SimpleStatus =
    | Active
    | Inactive
    | Pending

type RecordWithStatus = { Id: int; Status: SimpleStatus }

type PayloadUnion =
    | TextPayload of content: string
    | NumberPayload of value: int

type RecordWithPayload = { Id: int; Payload: PayloadUnion }

type ClosedRecord = { X: string; Y: int }

// ──────────────────────────────────────────────
// Helpers
// ──────────────────────────────────────────────

/// Serialize a ShapesGraph's underlying IGraph to a Turtle string.
let private serializeToTurtle (shapesGraph: VDS.RDF.Shacl.ShapesGraph) : string =
    let graph = (shapesGraph :> IGraph)
    use ms = new MemoryStream()
    let writer = CompressingTurtleWriter()
    use sw = new StreamWriter(ms, leaveOpen = true)
    writer.Save(graph, sw)
    sw.Flush()
    ms.Position <- 0L
    use sr = new StreamReader(ms)
    sr.ReadToEnd()

/// Parse a Turtle string into a dotNetRdf IGraph.
let private parseTurtle (turtle: string) : IGraph =
    let g = new Graph()
    let parser = TurtleParser()
    use sr = new StringReader(turtle)
    parser.Load(g, sr)
    g

/// Derive a shape, build its ShapesGraph, serialize to Turtle, parse back, and load shapes.
let private roundTrip<'T> () : ShaclShape list =
    ShapeBuilder.clearCache ()
    let shape = ShapeBuilder.deriveShapeDefault typeof<'T>
    let shapesGraph = ShapeGraphBuilder.buildShapesGraph shape
    let turtle = serializeToTurtle shapesGraph
    let graph = parseTurtle turtle
    ShapeLoader.loadFromGraph graph

/// Find a shape whose URI contains the given fragment.
let private findShapeContaining (fragment: string) (shapes: ShaclShape list) =
    shapes |> List.tryFind (fun s -> s.NodeShapeUri.ToString().Contains(fragment))

// ──────────────────────────────────────────────
// T070 tests
// ──────────────────────────────────────────────

[<Tests>]
let basicRecordTests =
    testList
        "ShapeLoader - basic record (3 properties)"
        [ testCase "loads expected number of properties"
          <| fun _ ->
              let shapes = roundTrip<BasicRecord> ()
              let mainShape = findShapeContaining "BasicRecord" shapes
              Expect.isSome mainShape "Should find BasicRecord shape"
              Expect.equal (List.length mainShape.Value.Properties) 3 "Should have 3 properties"

          testCase "properties have correct paths"
          <| fun _ ->
              let shapes = roundTrip<BasicRecord> ()
              let mainShape = (findShapeContaining "BasicRecord" shapes).Value
              let paths = mainShape.Properties |> List.map (fun p -> p.Path) |> List.sort
              Expect.equal paths [ "Age"; "Name"; "Score" ] "Paths should match field names"

          testCase "required fields have minCount 1"
          <| fun _ ->
              let shapes = roundTrip<BasicRecord> ()
              let mainShape = (findShapeContaining "BasicRecord" shapes).Value
              let nameP = mainShape.Properties |> List.find (fun p -> p.Path = "Name")
              let ageP = mainShape.Properties |> List.find (fun p -> p.Path = "Age")
              Expect.equal nameP.MinCount 1 "Name minCount"
              Expect.equal ageP.MinCount 1 "Age minCount"

          testCase "datatypes are preserved"
          <| fun _ ->
              let shapes = roundTrip<BasicRecord> ()
              let mainShape = (findShapeContaining "BasicRecord" shapes).Value
              let nameP = mainShape.Properties |> List.find (fun p -> p.Path = "Name")
              let ageP = mainShape.Properties |> List.find (fun p -> p.Path = "Age")
              let scoreP = mainShape.Properties |> List.find (fun p -> p.Path = "Score")
              Expect.equal nameP.Datatype (Some XsdString) "Name should be XsdString"
              Expect.equal ageP.Datatype (Some XsdInteger) "Age should be XsdInteger"
              Expect.equal scoreP.Datatype (Some XsdDouble) "Score should be XsdDouble"

          testCase "shape is closed"
          <| fun _ ->
              let shapes = roundTrip<BasicRecord> ()
              let mainShape = (findShapeContaining "BasicRecord" shapes).Value
              Expect.isTrue mainShape.Closed "Loaded record shape should be closed" ]

[<Tests>]
let optionalFieldTests =
    testList
        "ShapeLoader - optional field (minCount 0)"
        [ testCase "optional field has minCount 0"
          <| fun _ ->
              let shapes = roundTrip<RecordWithOptional> ()
              let mainShape = (findShapeContaining "RecordWithOptional" shapes).Value
              let labelP = mainShape.Properties |> List.find (fun p -> p.Path = "Label")
              Expect.equal labelP.MinCount 0 "Optional Label should have minCount 0"

          testCase "required field has minCount 1"
          <| fun _ ->
              let shapes = roundTrip<RecordWithOptional> ()
              let mainShape = (findShapeContaining "RecordWithOptional" shapes).Value
              let idP = mainShape.Properties |> List.find (fun p -> p.Path = "Id")
              Expect.equal idP.MinCount 1 "Required Id should have minCount 1" ]

[<Tests>]
let guidFieldTests =
    testList
        "ShapeLoader - Guid field (pattern constraint)"
        [ testCase "Guid field has pattern constraint after round-trip"
          <| fun _ ->
              let shapes = roundTrip<RecordWithGuidId> ()
              let mainShape = (findShapeContaining "RecordWithGuidId" shapes).Value
              let idP = mainShape.Properties |> List.find (fun p -> p.Path = "Id")
              Expect.isSome idP.Pattern "Guid field should have pattern constraint"
              Expect.stringContains idP.Pattern.Value "[0-9a-fA-F]" "Should be UUID regex" ]

[<Tests>]
let simpleDuTests =
    testList
        "ShapeLoader - simple DU (sh:in)"
        [ testCase "simple DU field has sh:in values after round-trip"
          <| fun _ ->
              let shapes = roundTrip<RecordWithStatus> ()
              let mainShape = (findShapeContaining "RecordWithStatus" shapes).Value
              let statusP = mainShape.Properties |> List.find (fun p -> p.Path = "Status")
              Expect.isSome statusP.InValues "Status should have sh:in values"
              let values = statusP.InValues.Value
              Expect.equal (List.length values) 3 "Should have 3 enum values"
              Expect.contains values "Active" "Should contain Active"
              Expect.contains values "Inactive" "Should contain Inactive"
              Expect.contains values "Pending" "Should contain Pending" ]

[<Tests>]
let payloadDuTests =
    testList
        "ShapeLoader - payload DU (sh:or)"
        [ testCase "payload DU field has sh:or references after round-trip"
          <| fun _ ->
              let shapes = roundTrip<RecordWithPayload> ()
              let mainShape = (findShapeContaining "RecordWithPayload" shapes).Value
              let payloadP = mainShape.Properties |> List.find (fun p -> p.Path = "Payload")
              Expect.isSome payloadP.OrShapes "Payload should have sh:or shapes"
              let orUris = payloadP.OrShapes.Value
              Expect.equal (List.length orUris) 2 "Should have 2 case shape URIs" ]

[<Tests>]
let closedShapeTests =
    testList
        "ShapeLoader - closed shape"
        [ testCase "sh:closed true is preserved"
          <| fun _ ->
              let shapes = roundTrip<ClosedRecord> ()
              let mainShape = (findShapeContaining "ClosedRecord" shapes).Value
              Expect.isTrue mainShape.Closed "Closed record shape should round-trip as closed" ]

[<Tests>]
let targetTypeTests =
    testList
        "ShapeLoader - TargetType is None for all loaded shapes"
        [ testCase "all loaded shapes have TargetType = None"
          <| fun _ ->
              let shapes = roundTrip<BasicRecord> ()

              for shape in shapes do
                  Expect.isNone shape.TargetType
                  <| sprintf "Shape %O should have TargetType = None" shape.NodeShapeUri ]

[<Tests>]
let missingResourceTests =
    testList
        "ShapeLoader - missing embedded resource"
        [ testCase "loadFromAssembly raises for missing resource"
          <| fun _ ->
              // The test assembly does not contain Frank.Semantic.shapes.shacl.ttl
              let testAssembly = Assembly.GetExecutingAssembly()

              Expect.throws
                  (fun () -> ShapeLoader.loadFromAssembly testAssembly |> ignore)
                  "Should raise for missing embedded resource" ]

[<Tests>]
let roundTripTests =
    testList
        "ShapeLoader - round-trip: derive -> serialize -> parse -> load"
        [ testCase "BasicRecord round-trips with matching property count"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let original = ShapeBuilder.deriveShapeDefault typeof<BasicRecord>
              let shapesGraph = ShapeGraphBuilder.buildShapesGraph original
              let turtle = serializeToTurtle shapesGraph
              let graph = parseTurtle turtle
              let loaded = ShapeLoader.loadFromGraph graph
              let loadedMain = (findShapeContaining "BasicRecord" loaded).Value

              Expect.equal (List.length loadedMain.Properties) (List.length original.Properties)
              <| "Property count should be preserved"

          testCase "BasicRecord round-trips with matching property paths"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let original = ShapeBuilder.deriveShapeDefault typeof<BasicRecord>
              let shapesGraph = ShapeGraphBuilder.buildShapesGraph original
              let turtle = serializeToTurtle shapesGraph
              let graph = parseTurtle turtle
              let loaded = ShapeLoader.loadFromGraph graph

              let loadedMain =
                  loaded |> List.find (fun s -> s.NodeShapeUri = original.NodeShapeUri)

              let originalPaths = original.Properties |> List.map (fun p -> p.Path) |> List.sort
              let loadedPaths = loadedMain.Properties |> List.map (fun p -> p.Path) |> List.sort
              Expect.equal loadedPaths originalPaths "Property paths should be preserved"

          testCase "BasicRecord round-trips with matching NodeShapeUri"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let original = ShapeBuilder.deriveShapeDefault typeof<BasicRecord>
              let shapesGraph = ShapeGraphBuilder.buildShapesGraph original
              let turtle = serializeToTurtle shapesGraph
              let graph = parseTurtle turtle
              let loaded = ShapeLoader.loadFromGraph graph

              let loadedMain =
                  loaded |> List.find (fun s -> s.NodeShapeUri = original.NodeShapeUri)

              Expect.equal loadedMain.NodeShapeUri original.NodeShapeUri "NodeShapeUri should match"

          testCase "BasicRecord round-trips with correct datatypes"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let original = ShapeBuilder.deriveShapeDefault typeof<BasicRecord>
              let shapesGraph = ShapeGraphBuilder.buildShapesGraph original
              let turtle = serializeToTurtle shapesGraph
              let graph = parseTurtle turtle
              let loaded = ShapeLoader.loadFromGraph graph

              let loadedMain =
                  loaded |> List.find (fun s -> s.NodeShapeUri = original.NodeShapeUri)

              for origProp in original.Properties do
                  let loadedProp =
                      loadedMain.Properties |> List.find (fun p -> p.Path = origProp.Path)

                  Expect.equal loadedProp.Datatype origProp.Datatype
                  <| sprintf "Datatype for %s should match" origProp.Path

          testCase "BasicRecord round-trips with correct minCount values"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let original = ShapeBuilder.deriveShapeDefault typeof<BasicRecord>
              let shapesGraph = ShapeGraphBuilder.buildShapesGraph original
              let turtle = serializeToTurtle shapesGraph
              let graph = parseTurtle turtle
              let loaded = ShapeLoader.loadFromGraph graph

              let loadedMain =
                  loaded |> List.find (fun s -> s.NodeShapeUri = original.NodeShapeUri)

              for origProp in original.Properties do
                  let loadedProp =
                      loadedMain.Properties |> List.find (fun p -> p.Path = origProp.Path)

                  Expect.equal loadedProp.MinCount origProp.MinCount
                  <| sprintf "MinCount for %s should match" origProp.Path

          testCase "BasicRecord round-trips with correct maxCount values"
          <| fun _ ->
              ShapeBuilder.clearCache ()
              let original = ShapeBuilder.deriveShapeDefault typeof<BasicRecord>
              let shapesGraph = ShapeGraphBuilder.buildShapesGraph original
              let turtle = serializeToTurtle shapesGraph
              let graph = parseTurtle turtle
              let loaded = ShapeLoader.loadFromGraph graph

              let loadedMain =
                  loaded |> List.find (fun s -> s.NodeShapeUri = original.NodeShapeUri)

              for origProp in original.Properties do
                  let loadedProp =
                      loadedMain.Properties |> List.find (fun p -> p.Path = origProp.Path)

                  Expect.equal loadedProp.MaxCount origProp.MaxCount
                  <| sprintf "MaxCount for %s should match" origProp.Path ]
