namespace Frank.Validation

open System

/// XSD datatype assigned to a SHACL property shape, derived from F# CLR type.
type XsdDatatype =
    | XsdString
    | XsdInteger
    | XsdLong
    | XsdDouble
    | XsdDecimal
    | XsdBoolean
    | XsdDateTimeStamp
    | XsdDateTime
    | XsdDate
    | XsdTime
    | XsdDuration
    | XsdAnyUri
    | XsdBase64Binary
    | Custom of Uri

/// Severity level for a validation result.
type ValidationSeverity =
    | Violation
    | Warning
    | Info

/// A SHACL property shape corresponding to a single field of an F# record.
type PropertyShape =
    { Path: string
      Datatype: XsdDatatype option
      MinCount: int
      MaxCount: int option
      NodeReference: Uri option
      InValues: string list option
      OrShapes: Uri list option
      Pattern: string option
      MinInclusive: obj option
      MaxInclusive: obj option
      Description: string option }

/// A SHACL NodeShape derived from an F# type definition.
/// TargetType is None for shapes loaded from serialized Turtle (ShapeLoader),
/// and Some for shapes derived at runtime via reflection (ShapeBuilder).
type ShaclShape =
    { TargetType: Type option
      NodeShapeUri: Uri
      Properties: PropertyShape list
      Closed: bool
      Description: string option }

/// A single constraint violation within a ValidationReport.
type ValidationResult =
    { FocusNode: string
      ResultPath: string
      Value: obj option
      SourceConstraint: string
      Message: string
      Severity: ValidationSeverity }

/// A W3C SHACL ValidationReport produced when request data violates constraints.
type ValidationReport =
    { Conforms: bool
      Results: ValidationResult list
      ShapeUri: Uri }
