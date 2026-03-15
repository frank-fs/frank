module Frank.Validation.Tests.TypeTests

open System
open Expecto
open Frank.Validation

[<Tests>]
let xsdDatatypeTests =
    testList
        "XsdDatatype"
        [ testCase "each case can be constructed and pattern matched"
          <| fun _ ->
              let cases =
                  [ XsdString
                    XsdInteger
                    XsdLong
                    XsdDouble
                    XsdDecimal
                    XsdBoolean
                    XsdDateTimeStamp
                    XsdDateTime
                    XsdDate
                    XsdTime
                    XsdDuration
                    XsdAnyUri
                    XsdBase64Binary ]

              Expect.equal (List.length cases) 13 "Should have 13 built-in cases"

          testCase "Custom case carries a Uri"
          <| fun _ ->
              let uri = Uri("http://example.org/custom-type")
              let dt = Custom uri

              match dt with
              | Custom u -> Expect.equal u uri "Custom should carry the Uri"
              | _ -> failtest "Expected Custom case" ]

[<Tests>]
let propertyShapeTests =
    testList
        "PropertyShape"
        [ testCase "required field with datatype"
          <| fun _ ->
              let ps =
                  { Path = "name"
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
                    Description = None }

              Expect.equal ps.MinCount 1 "Required field should have MinCount 1"
              Expect.equal ps.Datatype (Some XsdString) "Should have XsdString datatype"

          testCase "optional field"
          <| fun _ ->
              let ps =
                  { Path = "middleName"
                    Datatype = Some XsdString
                    MinCount = 0
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
                    Description = None }

              Expect.equal ps.MinCount 0 "Optional field should have MinCount 0"

          testCase "field with sh:in constraint"
          <| fun _ ->
              let ps =
                  { Path = "status"
                    Datatype = Some XsdString
                    MinCount = 1
                    MaxCount = Some 1
                    NodeReference = None
                    InValues = Some [ "Active"; "Inactive"; "Pending" ]
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

              Expect.isSome ps.InValues "Should have InValues"
              Expect.equal (ps.InValues.Value |> List.length) 3 "Should have 3 allowed values"

          testCase "nested field with NodeReference"
          <| fun _ ->
              let nodeUri = Uri("urn:frank:shape:Address")

              let ps =
                  { Path = "address"
                    Datatype = None
                    MinCount = 1
                    MaxCount = Some 1
                    NodeReference = Some nodeUri
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
                    Description = None }

              Expect.isNone ps.Datatype "Nested field should have no datatype"
              Expect.isSome ps.NodeReference "Should have a NodeReference"
              Expect.equal ps.NodeReference.Value nodeUri "NodeReference should match" ]

/// Helper to build a minimal PropertyShape for use in ShaclShape constructions.
let private minimalProp path datatype =
    { Path = path
      Datatype = datatype
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
      Description = None }

[<Tests>]
let shaclShapeTests =
    testList
        "ShaclShape"
        [ testCase "shape with multiple properties"
          <| fun _ ->
              let shape =
                  { TargetType = None
                    NodeShapeUri = Uri("urn:frank:shape:Customer")
                    Properties = [ minimalProp "name" (Some XsdString); minimalProp "age" (Some XsdInteger) ]
                    Closed = true
                    Description = Some "Customer shape"
                    SparqlConstraints = [] }

              Expect.equal (List.length shape.Properties) 2 "Should have 2 properties"
              Expect.isTrue shape.Closed "Records should be closed by default"
              Expect.isSome shape.Description "Should have a description"

          testCase "TargetType is None for loaded shapes"
          <| fun _ ->
              let shape =
                  { TargetType = None
                    NodeShapeUri = Uri("urn:frank:shape:Loaded")
                    Properties = []
                    Closed = false
                    Description = None
                    SparqlConstraints = [] }

              Expect.isNone shape.TargetType "Loaded shapes should have TargetType = None"

          testCase "TargetType is Some for derived shapes"
          <| fun _ ->
              let shape =
                  { TargetType = Some typeof<string>
                    NodeShapeUri = Uri("urn:frank:shape:Derived")
                    Properties = []
                    Closed = false
                    Description = None
                    SparqlConstraints = [] }

              Expect.isSome shape.TargetType "Derived shapes should have TargetType = Some _" ]

[<Tests>]
let validationReportTests =
    testList
        "ValidationReport"
        [ testCase "conforming report"
          <| fun _ ->
              let report =
                  { Conforms = true
                    Results = []
                    ShapeUri = Uri("urn:frank:shape:Customer") }

              Expect.isTrue report.Conforms "Should conform"
              Expect.isEmpty report.Results "Should have no results"

          testCase "non-conforming report with multiple results"
          <| fun _ ->
              let results =
                  [ { FocusNode = "request"
                      ResultPath = "name"
                      Value = None
                      SourceConstraint = "sh:minCount"
                      Message = "Field 'name' is required"
                      Severity = Violation }
                    { FocusNode = "request"
                      ResultPath = "age"
                      Value = Some(box "abc")
                      SourceConstraint = "sh:datatype"
                      Message = "Field 'age' must be an integer"
                      Severity = Violation }
                    { FocusNode = "request"
                      ResultPath = "email"
                      Value = Some(box "not-an-email")
                      SourceConstraint = "sh:pattern"
                      Message = "Field 'email' does not match required pattern"
                      Severity = Warning } ]

              let report =
                  { Conforms = false
                    Results = results
                    ShapeUri = Uri("urn:frank:shape:Customer") }

              Expect.isFalse report.Conforms "Should not conform"
              Expect.equal (List.length report.Results) 3 "Should have 3 results"

              for r in report.Results do
                  Expect.isNotEmpty r.FocusNode "FocusNode should not be empty"
                  Expect.isNotEmpty r.ResultPath "ResultPath should not be empty"
                  Expect.isNotEmpty r.SourceConstraint "SourceConstraint should not be empty"
                  Expect.isNotEmpty r.Message "Message should not be empty" ]

[<Tests>]
let constraintKindTests =
    testList
        "ConstraintKind"
        [ testCase "each variant can be constructed and matched"
          <| fun _ ->
              let constraints =
                  [ PatternConstraint "^[a-z]+$"
                    MinInclusiveConstraint(box 0)
                    MaxInclusiveConstraint(box 100)
                    MinExclusiveConstraint(box 0)
                    MaxExclusiveConstraint(box 100)
                    MinLengthConstraint 1
                    MaxLengthConstraint 255
                    InValuesConstraint [ "A"; "B"; "C" ]
                    SparqlConstraint "ASK { ?this :endDate ?end . ?this :startDate ?start . FILTER(?end > ?start) }"
                    CustomShaclConstraint(Uri("http://example.org/constraint"), box "value") ]

              Expect.equal (List.length constraints) 10 "Should have 10 constraint kinds"

              for c in constraints do
                  match c with
                  | PatternConstraint r -> Expect.isNotEmpty r "regex should not be empty"
                  | MinInclusiveConstraint v -> Expect.isNotNull v "value should not be null"
                  | MaxInclusiveConstraint v -> Expect.isNotNull v "value should not be null"
                  | MinExclusiveConstraint v -> Expect.isNotNull v "value should not be null"
                  | MaxExclusiveConstraint v -> Expect.isNotNull v "value should not be null"
                  | MinLengthConstraint l -> Expect.isGreaterThanOrEqual l 0 "length should be >= 0"
                  | MaxLengthConstraint l -> Expect.isGreaterThan l 0 "length should be > 0"
                  | InValuesConstraint vs -> Expect.isNonEmpty vs "values should not be empty"
                  | SparqlConstraint q -> Expect.isNotEmpty q "query should not be empty"
                  | CustomShaclConstraint(u, v) ->
                      Expect.isNotNull (box u) "uri should not be null"
                      Expect.isNotNull v "value should not be null" ]

[<Tests>]
let validationMarkerTests =
    testList
        "ValidationMarker"
        [ testCase "marker with no custom constraints"
          <| fun _ ->
              let marker =
                  { ShapeUri = Uri("urn:test:shape:string")
                    CustomConstraints = []
                    ResolverConfig = None }

              Expect.isEmpty marker.CustomConstraints "Should have no custom constraints"
              Expect.isNone marker.ResolverConfig "Should have no resolver config"

          testCase "marker with custom constraints"
          <| fun _ ->
              let marker =
                  { ShapeUri = Uri("urn:test:shape:string")
                    CustomConstraints =
                      [ { PropertyPath = "email"
                          Constraint = PatternConstraint "^[^@]+@[^@]+$" }
                        { PropertyPath = "age"
                          Constraint = MinInclusiveConstraint(box 0) } ]
                    ResolverConfig = None }

              Expect.equal (List.length marker.CustomConstraints) 2 "Should have 2 custom constraints"

          testCase "marker with ShapeResolverConfig"
          <| fun _ ->
              let baseShape =
                  { TargetType = None
                    NodeShapeUri = Uri("urn:frank:shape:Order")
                    Properties = []
                    Closed = true
                    Description = None
                    SparqlConstraints = [] }

              let adminShape =
                  { baseShape with
                      Description = Some "Admin order shape" }

              let config =
                  { BaseShape = baseShape
                    Overrides =
                      [ { RequiredClaim = ("role", [ "admin" ])
                          Shape = adminShape } ] }

              let marker =
                  { ShapeUri = Uri("urn:test:shape:order")
                    CustomConstraints = []
                    ResolverConfig = Some config }

              Expect.isSome marker.ResolverConfig "Should have resolver config"
              let rc = marker.ResolverConfig.Value
              Expect.equal (List.length rc.Overrides) 1 "Should have 1 override"
              let (claimType, claimValues) = rc.Overrides.[0].RequiredClaim
              Expect.equal claimType "role" "Claim type should be 'role'"
              Expect.equal claimValues [ "admin" ] "Claim values should be ['admin']" ]
