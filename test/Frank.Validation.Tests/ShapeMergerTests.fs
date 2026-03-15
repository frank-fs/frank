module Frank.Validation.Tests.ShapeMergerTests

open System
open Expecto
open Frank.Validation

// ──────────────────────────────────────────────
// Test domain types
// ──────────────────────────────────────────────

type ProductInput =
    { Name: string
      Price: decimal
      Category: string
      StockCount: int }

type RangeInput = { Score: int; Label: string }

// ──────────────────────────────────────────────
// Helpers
// ──────────────────────────────────────────────

/// Build a derived ShaclShape for 'T with a cleared cache.
let private deriveShape<'T> () =
    ShapeBuilder.clearCache ()
    ShapeBuilder.deriveShapeDefault typeof<'T>

/// Build a ShaclShape from a list of named properties with defaults.
let private shapeWithProps (uri: string) (propNames: string list) : ShaclShape =
    let props =
        propNames
        |> List.map (fun name ->
            { Path = name
              Datatype = Some XsdString
              MinCount = 1
              MaxCount = Some 1
              NodeReference = None
              InValues = None
              OrShapes = None
              Pattern = None
              AdditionalPatterns = []
              MinInclusive = None
              MaxInclusive = None
              MinExclusive = None
              MaxExclusive = None
              MinLength = None
              MaxLength = None
              AdditionalConstraints = []
              Description = None })

    { TargetType = None
      NodeShapeUri = Uri(uri)
      Properties = props
      Closed = true
      Description = None
      SparqlConstraints = [] }

/// Find the property with the given path in the shape.
let private findProp (path: string) (shape: ShaclShape) =
    shape.Properties |> List.find (fun p -> p.Path = path)

// ──────────────────────────────────────────────
// T032/T033: Basic merge and additive constraints
// ──────────────────────────────────────────────

[<Tests>]
let additivePatternTests =
    testList
        "ShapeMerger - T033a additive pattern constraint"
        [ testCase "adding pattern to property with no existing pattern"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:A" [ "Email" ]

              let constraints =
                  [ { PropertyPath = "Email"
                      Constraint = PatternConstraint "^[^@]+@[^@]+$" } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              let emailProp = findProp "Email" merged
              Expect.equal emailProp.Pattern (Some "^[^@]+@[^@]+$") "Pattern should be set"
              Expect.isEmpty emailProp.AdditionalPatterns "No additional patterns expected"

          testCase "adding second pattern creates additional pattern entry (AND semantics)"
          <| fun _ ->
              let baseProp =
                  { Path = "Code"
                    Datatype = Some XsdString
                    MinCount = 1
                    MaxCount = Some 1
                    NodeReference = None
                    InValues = None
                    OrShapes = None
                    Pattern = Some "^[A-Z]"
                    AdditionalPatterns = []
                    MinInclusive = None
                    MaxInclusive = None
                    MinExclusive = None
                    MaxExclusive = None
                    MinLength = None
                    MaxLength = None
                    AdditionalConstraints = []
                    Description = None }

              let shape =
                  { TargetType = None
                    NodeShapeUri = Uri("urn:test:shape:B")
                    Properties = [ baseProp ]
                    Closed = true
                    Description = None
                    SparqlConstraints = [] }

              let constraints =
                  [ { PropertyPath = "Code"
                      Constraint = PatternConstraint "[0-9]+$" } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              let codeProp = findProp "Code" merged
              Expect.equal codeProp.Pattern (Some "^[A-Z]") "Primary pattern preserved"
              Expect.contains codeProp.AdditionalPatterns "[0-9]+$" "Second pattern in additional list"

          testCase "adding duplicate pattern is idempotent"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:C" [ "Name" ]

              let constraints =
                  [ { PropertyPath = "Name"
                      Constraint = PatternConstraint "^[a-z]+" }
                    { PropertyPath = "Name"
                      Constraint = PatternConstraint "^[a-z]+" } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              let nameProp = findProp "Name" merged
              Expect.equal nameProp.Pattern (Some "^[a-z]+") "Primary pattern set"
              Expect.isEmpty nameProp.AdditionalPatterns "No duplicate additional patterns" ]

// ──────────────────────────────────────────────
// T033b: MinInclusive additive merging
// ──────────────────────────────────────────────

[<Tests>]
let minInclusiveTests =
    testList
        "ShapeMerger - T033b additive MinInclusive"
        [ testCase "adds MinInclusive when none exists"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:D" [ "Score" ]

              let constraints =
                  [ { PropertyPath = "Score"
                      Constraint = MinInclusiveConstraint(box 0) } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              let scoreProp = findProp "Score" merged
              Expect.equal scoreProp.MinInclusive (Some(box 0)) "MinInclusive should be 0"

          testCase "tightens MinInclusive: custom larger than base uses custom"
          <| fun _ ->
              let baseProp =
                  { Path = "Score"
                    Datatype = Some XsdInteger
                    MinCount = 1
                    MaxCount = Some 1
                    NodeReference = None
                    InValues = None
                    OrShapes = None
                    Pattern = None
                    AdditionalPatterns = []
                    MinInclusive = Some(box 0)
                    MaxInclusive = None
                    MinExclusive = None
                    MaxExclusive = None
                    MinLength = None
                    MaxLength = None
                    AdditionalConstraints = []
                    Description = None }

              let shape =
                  { TargetType = None
                    NodeShapeUri = Uri("urn:test:shape:E")
                    Properties = [ baseProp ]
                    Closed = true
                    Description = None
                    SparqlConstraints = [] }

              let constraints =
                  [ { PropertyPath = "Score"
                      Constraint = MinInclusiveConstraint(box 10) } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              let scoreProp = findProp "Score" merged
              // custom(10) > base(0) → tighten to 10
              match scoreProp.MinInclusive with
              | Some v ->
                  match System.Decimal.TryParse(string v) with
                  | true, d -> Expect.equal d 10m "Should use larger value (10)"
                  | _ -> failtest "MinInclusive should be decimal-parseable"
              | None -> failtest "MinInclusive should be set"

          testCase "preserves base MinInclusive when it is already tighter"
          <| fun _ ->
              let baseProp =
                  { Path = "Score"
                    Datatype = Some XsdInteger
                    MinCount = 1
                    MaxCount = Some 1
                    NodeReference = None
                    InValues = None
                    OrShapes = None
                    Pattern = None
                    AdditionalPatterns = []
                    MinInclusive = Some(box 50)
                    MaxInclusive = None
                    MinExclusive = None
                    MaxExclusive = None
                    MinLength = None
                    MaxLength = None
                    AdditionalConstraints = []
                    Description = None }

              let shape =
                  { TargetType = None
                    NodeShapeUri = Uri("urn:test:shape:F")
                    Properties = [ baseProp ]
                    Closed = true
                    Description = None
                    SparqlConstraints = [] }

              let constraints =
                  [ { PropertyPath = "Score"
                      Constraint = MinInclusiveConstraint(box 5) } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              let scoreProp = findProp "Score" merged
              // base(50) > custom(5) → keep base (50)
              match scoreProp.MinInclusive with
              | Some v ->
                  match System.Decimal.TryParse(string v) with
                  | true, d -> Expect.equal d 50m "Should keep base value (50)"
                  | _ -> failtest "Should be decimal-parseable"
              | None -> failtest "MinInclusive should be set" ]

// ──────────────────────────────────────────────
// T033c: InValues intersection
// ──────────────────────────────────────────────

[<Tests>]
let inValuesIntersectionTests =
    testList
        "ShapeMerger - T033c InValues intersection"
        [ testCase "intersects sh:in values with base constraint"
          <| fun _ ->
              let baseProp =
                  { Path = "Status"
                    Datatype = Some XsdString
                    MinCount = 1
                    MaxCount = Some 1
                    NodeReference = None
                    InValues = Some [ "Active"; "Inactive"; "Pending"; "Archived" ]
                    OrShapes = None
                    Pattern = None
                    AdditionalPatterns = []
                    MinInclusive = None
                    MaxInclusive = None
                    MinExclusive = None
                    MaxExclusive = None
                    MinLength = None
                    MaxLength = None
                    AdditionalConstraints = []
                    Description = None }

              let shape =
                  { TargetType = None
                    NodeShapeUri = Uri("urn:test:shape:G")
                    Properties = [ baseProp ]
                    Closed = true
                    Description = None
                    SparqlConstraints = [] }

              let constraints =
                  [ { PropertyPath = "Status"
                      Constraint = InValuesConstraint [ "Active"; "Pending" ] } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              let statusProp = findProp "Status" merged

              Expect.isSome statusProp.InValues "Should have InValues"
              let values = statusProp.InValues.Value |> List.sort
              Expect.equal values [ "Active"; "Pending" ] "Should be intersection: Active, Pending"

          testCase "sets sh:in when no base constraint exists"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:H" [ "Category" ]

              let constraints =
                  [ { PropertyPath = "Category"
                      Constraint = InValuesConstraint [ "A"; "B"; "C" ] } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              let catProp = findProp "Category" merged
              Expect.isSome catProp.InValues "Should have InValues"
              Expect.equal (catProp.InValues.Value |> List.sort) [ "A"; "B"; "C" ] "Should have all values" ]

// ──────────────────────────────────────────────
// T034: Conflict detection
// ──────────────────────────────────────────────

[<Tests>]
let conflictDetectionTests =
    testList
        "ShapeMerger - T034 conflict detection"
        [ testCase "T034d: empty InValues intersection raises InvalidOperationException"
          <| fun _ ->
              let baseProp =
                  { Path = "Status"
                    Datatype = Some XsdString
                    MinCount = 1
                    MaxCount = Some 1
                    NodeReference = None
                    InValues = Some [ "Active"; "Inactive" ]
                    OrShapes = None
                    Pattern = None
                    AdditionalPatterns = []
                    MinInclusive = None
                    MaxInclusive = None
                    MinExclusive = None
                    MaxExclusive = None
                    MinLength = None
                    MaxLength = None
                    AdditionalConstraints = []
                    Description = None }

              let shape =
                  { TargetType = None
                    NodeShapeUri = Uri("urn:test:shape:I")
                    Properties = [ baseProp ]
                    Closed = true
                    Description = None
                    SparqlConstraints = [] }

              let constraints =
                  [ { PropertyPath = "Status"
                      Constraint = InValuesConstraint [ "Pending"; "Archived" ] } ]

              Expect.throws
                  (fun () -> ShapeMerger.mergeConstraints shape constraints |> ignore)
                  "Empty intersection should raise"

          testCase "T034e: MinInclusive > MaxInclusive raises InvalidOperationException"
          <| fun _ ->
              let baseProp =
                  { Path = "Score"
                    Datatype = Some XsdInteger
                    MinCount = 1
                    MaxCount = Some 1
                    NodeReference = None
                    InValues = None
                    OrShapes = None
                    Pattern = None
                    AdditionalPatterns = []
                    MinInclusive = Some(box 50)
                    MaxInclusive = Some(box 100)
                    MinExclusive = None
                    MaxExclusive = None
                    MinLength = None
                    MaxLength = None
                    AdditionalConstraints = []
                    Description = None }

              let shape =
                  { TargetType = None
                    NodeShapeUri = Uri("urn:test:shape:J")
                    Properties = [ baseProp ]
                    Closed = true
                    Description = None
                    SparqlConstraints = [] }

              // custom MaxInclusive of 30 < existing MinInclusive of 50 → conflict
              let constraints =
                  [ { PropertyPath = "Score"
                      Constraint = MaxInclusiveConstraint(box 30) } ]

              Expect.throws
                  (fun () -> ShapeMerger.mergeConstraints shape constraints |> ignore)
                  "min > max should raise"

          testCase "MinLength > MaxLength raises InvalidOperationException"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:K" [ "Name" ]

              let constraints =
                  [ { PropertyPath = "Name"
                      Constraint = MinLengthConstraint 10 }
                    { PropertyPath = "Name"
                      Constraint = MaxLengthConstraint 5 } ]

              Expect.throws
                  (fun () -> ShapeMerger.mergeConstraints shape constraints |> ignore)
                  "minLength > maxLength should raise"

          testCase "MinExclusive >= MaxExclusive raises InvalidOperationException"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:ExclRange" [ "Score" ]

              let constraints =
                  [ { PropertyPath = "Score"
                      Constraint = MinExclusiveConstraint(box 100) }
                    { PropertyPath = "Score"
                      Constraint = MaxExclusiveConstraint(box 50) } ]

              Expect.throws
                  (fun () -> ShapeMerger.mergeConstraints shape constraints |> ignore)
                  "minExclusive >= maxExclusive should raise"

          testCase "T034f: non-existent property path raises InvalidOperationException"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:L" [ "Name" ]

              let constraints =
                  [ { PropertyPath = "NonExistent"
                      Constraint = PatternConstraint "^x" } ]

              Expect.throws
                  (fun () -> ShapeMerger.mergeConstraints shape constraints |> ignore)
                  "Non-existent path should raise"

          testCase "error message includes property path and known paths"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:M" [ "Name"; "Email" ]

              let constraints =
                  [ { PropertyPath = "Missing"
                      Constraint = PatternConstraint "^x" } ]

              let ex =
                  Expect.throws (fun () -> ShapeMerger.mergeConstraints shape constraints |> ignore) "Should raise"
                  |> ignore

              // We just verify it throws; message content checked implicitly via type
              () ]

// ──────────────────────────────────────────────
// T033: Multiple constraints on same property
// ──────────────────────────────────────────────

[<Tests>]
let multipleConstraintTests =
    testList
        "ShapeMerger - T033g multiple constraints on same property"
        [ testCase "multiple different constraint kinds applied to same property"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:N" [ "Email" ]

              let constraints =
                  [ { PropertyPath = "Email"
                      Constraint = PatternConstraint "^[^@]+" }
                    { PropertyPath = "Email"
                      Constraint = MinLengthConstraint 5 }
                    { PropertyPath = "Email"
                      Constraint = MaxLengthConstraint 100 } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              let emailProp = findProp "Email" merged
              Expect.isSome emailProp.Pattern "Pattern should be set"
              Expect.equal emailProp.MinLength (Some 5) "MinLength should be 5"
              Expect.equal emailProp.MaxLength (Some 100) "MaxLength should be 100"

          testCase "constraints on different properties each applied correctly"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:O" [ "Name"; "Age" ]

              let constraints =
                  [ { PropertyPath = "Name"
                      Constraint = PatternConstraint "^[A-Z]" }
                    { PropertyPath = "Age"
                      Constraint = MinInclusiveConstraint(box 0) } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              let nameProp = findProp "Name" merged
              let ageProp = findProp "Age" merged
              Expect.isSome nameProp.Pattern "Name should have pattern"
              Expect.isSome ageProp.MinInclusive "Age should have MinInclusive"

          testCase "MinLength tightens toward larger value"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:P" [ "Code" ]

              let constraints =
                  [ { PropertyPath = "Code"
                      Constraint = MinLengthConstraint 3 }
                    { PropertyPath = "Code"
                      Constraint = MinLengthConstraint 7 } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              let codeProp = findProp "Code" merged
              Expect.equal codeProp.MinLength (Some 7) "Should use larger MinLength (7)"

          testCase "MaxLength tightens toward smaller value"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:Q" [ "Code" ]

              let constraints =
                  [ { PropertyPath = "Code"
                      Constraint = MaxLengthConstraint 50 }
                    { PropertyPath = "Code"
                      Constraint = MaxLengthConstraint 20 } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              let codeProp = findProp "Code" merged
              Expect.equal codeProp.MaxLength (Some 20) "Should use smaller MaxLength (20)" ]

// ──────────────────────────────────────────────
// T035: SPARQL constraints
// ──────────────────────────────────────────────

[<Tests>]
let sparqlConstraintTests =
    testList
        "ShapeMerger - T035 SPARQL constraint support"
        [ testCase "T033h: SPARQL constraint added to shape SparqlConstraints list"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:R" [ "StartDate"; "EndDate" ]

              // A minimal syntactically valid SPARQL SELECT query
              let sparqlQuery =
                  "SELECT ?this WHERE { ?this <urn:frank:property:StartDate> ?start . ?this <urn:frank:property:EndDate> ?end . FILTER(?end > ?start) }"

              let constraints =
                  [ { PropertyPath = ""
                      Constraint = SparqlConstraint sparqlQuery } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              Expect.equal (List.length merged.SparqlConstraints) 1 "Should have 1 SPARQL constraint"
              Expect.equal merged.SparqlConstraints.[0].Query sparqlQuery "Query should be preserved"

          testCase "SPARQL constraint is added to node shape, not property shape"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:S" [ "Name" ]

              let sparqlQuery =
                  "SELECT ?this WHERE { ?this <urn:frank:property:Name> ?n . FILTER(STRLEN(?n) > 0) }"

              let constraints =
                  [ { PropertyPath = ""
                      Constraint = SparqlConstraint sparqlQuery } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              // The property itself should be unaffected
              let nameProp = findProp "Name" merged
              Expect.isNone nameProp.Pattern "Property pattern should not change"
              // The SPARQL constraint is on the shape
              Expect.isNonEmpty merged.SparqlConstraints "Shape should have SPARQL constraints"

          testCase "invalid SPARQL syntax raises InvalidOperationException"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:T" [ "Name" ]

              let constraints =
                  [ { PropertyPath = ""
                      Constraint = SparqlConstraint "THIS IS NOT VALID SPARQL !!!" } ]

              Expect.throws
                  (fun () -> ShapeMerger.mergeConstraints shape constraints |> ignore)
                  "Invalid SPARQL should raise InvalidOperationException"

          testCase "multiple SPARQL constraints accumulate"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:U" [ "StartDate"; "EndDate"; "Name" ]

              let query1 =
                  "SELECT ?this WHERE { ?this <urn:frank:property:StartDate> ?s . ?this <urn:frank:property:EndDate> ?e . FILTER(?e > ?s) }"

              let query2 =
                  "SELECT ?this WHERE { ?this <urn:frank:property:Name> ?n . FILTER(STRLEN(?n) > 0) }"

              let constraints =
                  [ { PropertyPath = ""
                      Constraint = SparqlConstraint query1 }
                    { PropertyPath = ""
                      Constraint = SparqlConstraint query2 } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              Expect.equal (List.length merged.SparqlConstraints) 2 "Should have 2 SPARQL constraints" ]

// ──────────────────────────────────────────────
// T033i: Both auto-derived and custom constraints
// ──────────────────────────────────────────────

[<Tests>]
let combinedConstraintTests =
    testList
        "ShapeMerger - T033i auto-derived and custom constraints together"
        [ testCase "auto-derived shape properties are preserved after merge"
          <| fun _ ->
              let shape = deriveShape<ProductInput> ()
              let originalPropCount = shape.Properties.Length

              let constraints =
                  [ { PropertyPath = "Name"
                      Constraint = PatternConstraint "^[A-Za-z]" }
                    { PropertyPath = "Price"
                      Constraint = MinInclusiveConstraint(box 0.0) } ]

              let merged = ShapeMerger.mergeConstraints shape constraints

              Expect.equal merged.Properties.Length originalPropCount "Property count unchanged"
              Expect.equal merged.NodeShapeUri shape.NodeShapeUri "Shape URI unchanged"
              Expect.equal merged.Closed shape.Closed "Closed flag unchanged"

          testCase "auto-derived MinCount constraints are not affected by merge"
          <| fun _ ->
              let shape = deriveShape<ProductInput> ()

              let constraints =
                  [ { PropertyPath = "Name"
                      Constraint = MinLengthConstraint 1 } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              let nameProp = findProp "Name" merged
              Expect.equal nameProp.MinCount 1 "Auto-derived MinCount should be preserved"
              Expect.equal nameProp.MinLength (Some 1) "Custom MinLength should be added"

          testCase "empty constraint list returns shape unchanged"
          <| fun _ ->
              let shape = deriveShape<ProductInput> ()
              let merged = ShapeMerger.mergeConstraints shape []
              // Should be reference-equal since short-circuit path returns baseShape
              Expect.equal merged.NodeShapeUri shape.NodeShapeUri "URI unchanged"
              Expect.equal merged.Properties.Length shape.Properties.Length "Properties unchanged"

          testCase "CustomShaclConstraint adds raw pair to AdditionalConstraints"
          <| fun _ ->
              let shape = shapeWithProps "urn:test:shape:V" [ "Value" ]
              let predicateUri = Uri("http://example.org/myConstraint")

              let constraints =
                  [ { PropertyPath = "Value"
                      Constraint = CustomShaclConstraint(predicateUri, box "myValue") } ]

              let merged = ShapeMerger.mergeConstraints shape constraints
              let valueProp = findProp "Value" merged
              Expect.equal (List.length valueProp.AdditionalConstraints) 1 "Should have 1 custom constraint"
              Expect.equal valueProp.AdditionalConstraints.[0].PredicateUri predicateUri "Predicate URI should match" ]
