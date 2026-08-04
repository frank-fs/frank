namespace Frank.Validation

open System

/// CE sugar over ShapeSpecFunctions, mirroring Frank.Provenance's ProvBuilder: the constructor takes
/// the already-built initial value; Yield/Zero return it unchanged; every operation is one line of
/// addConstraint (or a direct field update for severity/message).
[<AutoOpen>]
module ShapeBuilderModule =
    [<Sealed>]
    type PropertyShapeBuilder =
        new: initial: PropertyShapeSpec -> PropertyShapeBuilder
        member Yield: 'a -> PropertyShapeSpec
        member Zero: unit -> PropertyShapeSpec
        member Run: p: PropertyShapeSpec -> PropertyShapeSpec

        [<CustomOperation("datatype")>]
        member Datatype: PropertyShapeSpec * XsdDatatype -> PropertyShapeSpec

        [<CustomOperation("ofClass")>]
        member OfClass: PropertyShapeSpec * Uri -> PropertyShapeSpec

        [<CustomOperation("nodeKind")>]
        member NodeKindOp: PropertyShapeSpec * NodeKind -> PropertyShapeSpec

        [<CustomOperation("minCount")>]
        member MinCount: PropertyShapeSpec * int -> PropertyShapeSpec

        [<CustomOperation("maxCount")>]
        member MaxCount: PropertyShapeSpec * int -> PropertyShapeSpec

        [<CustomOperation("minLength")>]
        member MinLength: PropertyShapeSpec * int -> PropertyShapeSpec

        [<CustomOperation("maxLength")>]
        member MaxLength: PropertyShapeSpec * int -> PropertyShapeSpec

        [<CustomOperation("minExclusive")>]
        member MinExclusive: PropertyShapeSpec * Frank.Rdf.Literal -> PropertyShapeSpec

        [<CustomOperation("minInclusive")>]
        member MinInclusive: PropertyShapeSpec * Frank.Rdf.Literal -> PropertyShapeSpec

        [<CustomOperation("maxExclusive")>]
        member MaxExclusive: PropertyShapeSpec * Frank.Rdf.Literal -> PropertyShapeSpec

        [<CustomOperation("maxInclusive")>]
        member MaxInclusive: PropertyShapeSpec * Frank.Rdf.Literal -> PropertyShapeSpec

        [<CustomOperation("pattern")>]
        member Pattern: PropertyShapeSpec * string -> PropertyShapeSpec

        [<CustomOperation("patternWithFlags")>]
        member PatternWithFlags: PropertyShapeSpec * string * string -> PropertyShapeSpec

        [<CustomOperation("languageIn")>]
        member LanguageIn: PropertyShapeSpec * NonEmptyList<string> -> PropertyShapeSpec

        [<CustomOperation("uniqueLang")>]
        member UniqueLang: PropertyShapeSpec * bool -> PropertyShapeSpec

        [<CustomOperation("equalsPath")>]
        member EqualsPath: PropertyShapeSpec * Uri -> PropertyShapeSpec

        [<CustomOperation("disjoint")>]
        member Disjoint: PropertyShapeSpec * Uri -> PropertyShapeSpec

        [<CustomOperation("lessThan")>]
        member LessThan: PropertyShapeSpec * Uri -> PropertyShapeSpec

        [<CustomOperation("lessThanOrEquals")>]
        member LessThanOrEquals: PropertyShapeSpec * Uri -> PropertyShapeSpec

        [<CustomOperation("node")>]
        member NodeOp: PropertyShapeSpec * ShapeDecl -> PropertyShapeSpec

        [<CustomOperation("qualifiedValueShape")>]
        member QualifiedValueShape: PropertyShapeSpec * ShapeDecl * int option * int option * bool -> PropertyShapeSpec

        [<CustomOperation("hasValue")>]
        member HasValue: PropertyShapeSpec * Frank.Rdf.Value -> PropertyShapeSpec

        [<CustomOperation("allowedValues")>]
        member AllowedValues: PropertyShapeSpec * NonEmptyList<Frank.Rdf.Value> -> PropertyShapeSpec

        [<CustomOperation("sparqlConstraint")>]
        member SparqlConstraintOp: PropertyShapeSpec * SparqlConstraint -> PropertyShapeSpec

        [<CustomOperation("severity")>]
        member SeverityOp: PropertyShapeSpec * Severity -> PropertyShapeSpec

        [<CustomOperation("message")>]
        member MessageOp: PropertyShapeSpec * string -> PropertyShapeSpec

    /// `property path { ... } = PropertyShapeBuilder(ofPath path) { ... }`.
    val property: path: PropertyPath -> PropertyShapeBuilder

    [<Sealed>]
    type ShapeBuilder =
        new: initial: ShapeDecl -> ShapeBuilder
        member Yield: 'a -> ShapeDecl
        member Zero: unit -> ShapeDecl
        member Run: d: ShapeDecl -> ShapeDecl

        [<CustomOperation("properties")>]
        member Properties: ShapeDecl * PropertyShapeSpec list -> ShapeDecl

        [<CustomOperation("closed")>]
        member Closed: ShapeDecl * ignoredProperties: Uri list -> ShapeDecl

        [<CustomOperation("severity")>]
        member SeverityOp: ShapeDecl * Severity -> ShapeDecl

        [<CustomOperation("message")>]
        member MessageOp: ShapeDecl * string -> ShapeDecl

    /// `shape targets { ... } = ShapeBuilder(recordShape targets []) { ... }`.
    val shape: targets: TargetSpec list -> ShapeBuilder
