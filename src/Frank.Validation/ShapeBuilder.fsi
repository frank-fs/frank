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
        member inline Run: p: PropertyShapeSpec -> PropertyShapeSpec

        [<CustomOperation("datatype")>]
        member inline Datatype: PropertyShapeSpec * XsdDatatype -> PropertyShapeSpec

        [<CustomOperation("ofClass")>]
        member inline OfClass: PropertyShapeSpec * Uri -> PropertyShapeSpec

        [<CustomOperation("nodeKind")>]
        member inline NodeKindOp: PropertyShapeSpec * NodeKind -> PropertyShapeSpec

        [<CustomOperation("minCount")>]
        member inline MinCount: PropertyShapeSpec * int -> PropertyShapeSpec

        [<CustomOperation("maxCount")>]
        member inline MaxCount: PropertyShapeSpec * int -> PropertyShapeSpec

        [<CustomOperation("minLength")>]
        member inline MinLength: PropertyShapeSpec * int -> PropertyShapeSpec

        [<CustomOperation("maxLength")>]
        member inline MaxLength: PropertyShapeSpec * int -> PropertyShapeSpec

        [<CustomOperation("minExclusive")>]
        member inline MinExclusive: PropertyShapeSpec * Frank.Rdf.Literal -> PropertyShapeSpec

        [<CustomOperation("minInclusive")>]
        member inline MinInclusive: PropertyShapeSpec * Frank.Rdf.Literal -> PropertyShapeSpec

        [<CustomOperation("maxExclusive")>]
        member inline MaxExclusive: PropertyShapeSpec * Frank.Rdf.Literal -> PropertyShapeSpec

        [<CustomOperation("maxInclusive")>]
        member inline MaxInclusive: PropertyShapeSpec * Frank.Rdf.Literal -> PropertyShapeSpec

        [<CustomOperation("pattern")>]
        member inline Pattern: PropertyShapeSpec * string -> PropertyShapeSpec

        [<CustomOperation("patternWithFlags")>]
        member inline PatternWithFlags: PropertyShapeSpec * string * string -> PropertyShapeSpec

        [<CustomOperation("languageIn")>]
        member inline LanguageIn: PropertyShapeSpec * NonEmptyList<string> -> PropertyShapeSpec

        [<CustomOperation("uniqueLang")>]
        member inline UniqueLang: PropertyShapeSpec * bool -> PropertyShapeSpec

        [<CustomOperation("equalsPath")>]
        member inline EqualsPath: PropertyShapeSpec * Uri -> PropertyShapeSpec

        [<CustomOperation("disjoint")>]
        member inline Disjoint: PropertyShapeSpec * Uri -> PropertyShapeSpec

        [<CustomOperation("lessThan")>]
        member inline LessThan: PropertyShapeSpec * Uri -> PropertyShapeSpec

        [<CustomOperation("lessThanOrEquals")>]
        member inline LessThanOrEquals: PropertyShapeSpec * Uri -> PropertyShapeSpec

        [<CustomOperation("node")>]
        member inline NodeOp: PropertyShapeSpec * ShapeDecl -> PropertyShapeSpec

        [<CustomOperation("qualifiedValueShape")>]
        member inline QualifiedValueShape: PropertyShapeSpec * ShapeDecl * int option * int option * bool -> PropertyShapeSpec

        [<CustomOperation("hasValue")>]
        member inline HasValue: PropertyShapeSpec * Frank.Rdf.Value -> PropertyShapeSpec

        [<CustomOperation("allowedValues")>]
        member inline AllowedValues: PropertyShapeSpec * NonEmptyList<Frank.Rdf.Value> -> PropertyShapeSpec

        [<CustomOperation("sparqlConstraint")>]
        member inline SparqlConstraintOp: PropertyShapeSpec * SparqlConstraint -> PropertyShapeSpec

        [<CustomOperation("severity")>]
        member inline SeverityOp: PropertyShapeSpec * Severity -> PropertyShapeSpec

        [<CustomOperation("message")>]
        member inline MessageOp: PropertyShapeSpec * string -> PropertyShapeSpec

    /// `property path { ... } = PropertyShapeBuilder(ofPath path) { ... }`.
    val property: path: PropertyPath -> PropertyShapeBuilder

    [<Sealed>]
    type ShapeBuilder =
        new: initial: ShapeDecl -> ShapeBuilder
        member Yield: 'a -> ShapeDecl
        member Zero: unit -> ShapeDecl
        member inline Run: d: ShapeDecl -> ShapeDecl

        [<CustomOperation("properties")>]
        member inline Properties: ShapeDecl * PropertyShapeSpec list -> ShapeDecl

        [<CustomOperation("closed")>]
        member inline Closed: ShapeDecl * ignoredProperties: Uri list -> ShapeDecl

        [<CustomOperation("severity")>]
        member inline SeverityOp: ShapeDecl * Severity -> ShapeDecl

        [<CustomOperation("message")>]
        member inline MessageOp: ShapeDecl * string -> ShapeDecl

    /// `shape targets { ... } = ShapeBuilder(recordShape targets []) { ... }`.
    val shape: targets: TargetSpec list -> ShapeBuilder
