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

/// An additional custom SHACL predicate/value pair to emit on a property shape.
type CustomShaclPair = { PredicateUri: Uri; Value: obj }

/// A SHACL property shape corresponding to a single field of an F# record.
type PropertyShape =
    {
        Path: string
        Datatype: XsdDatatype option
        MinCount: int
        MaxCount: int option
        NodeReference: Uri option
        InValues: string list option
        OrShapes: Uri list option
        Pattern: string option
        /// Additional sh:pattern values beyond the first (AND semantics — all must match).
        AdditionalPatterns: string list
        MinInclusive: obj option
        MaxInclusive: obj option
        MinExclusive: obj option
        MaxExclusive: obj option
        MinLength: int option
        MaxLength: int option
        /// Raw predicate/value pairs for custom SHACL constraints not modelled as first-class fields.
        AdditionalConstraints: CustomShaclPair list
        Description: string option
    }

/// A SPARQL SELECT constraint to attach to a NodeShape for cross-field validation.
type NodeSparqlConstraint = { Query: string }

/// A SHACL NodeShape derived from an F# type definition.
/// TargetType is None for shapes loaded from serialized Turtle (ShapeLoader),
/// and Some for shapes derived at runtime via reflection (ShapeBuilder).
type ShaclShape =
    {
        TargetType: Type option
        NodeShapeUri: Uri
        Properties: PropertyShape list
        Closed: bool
        Description: string option
        /// SPARQL SELECT constraints attached to the NodeShape for cross-field validation.
        SparqlConstraints: NodeSparqlConstraint list
    }

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
