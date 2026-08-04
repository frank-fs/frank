namespace Frank.Validation

open System
open Frank.Rdf

/// A non-empty list -- illegal-empty-list states unrepresentable, e.g. for sh:in / sh:languageIn /
/// logical-combinator members, which SHACL requires to be non-empty.
type NonEmptyList<'T> = { Head: 'T; Tail: 'T list }

module NonEmptyList =
    val ofList: items: 'T list -> NonEmptyList<'T> option
    val toList: nel: NonEmptyList<'T> -> 'T list

/// The closed set of xsd datatypes Frank maps to sh:datatype. RequireQualifiedAccess means
/// XsdDatatype.Integer, not a redundant XsdInteger -- see the design doc's naming note.
[<Struct; RequireQualifiedAccess>]
type XsdDatatype =
    | Integer
    | Long
    | Decimal
    | Double
    | Boolean
    | String
    | DateTime

/// sh:nodeKind's five permitted values.
[<Struct; RequireQualifiedAccess>]
type NodeKind =
    | BlankNode
    | Iri
    | Literal
    | BlankNodeOrIri
    | BlankNodeOrLiteral
    | IriOrLiteral

/// sh:severity's three permitted values.
[<Struct; RequireQualifiedAccess>]
type Severity =
    | Violation
    | Warning
    | Info

/// sh:targetClass / sh:targetNode / sh:targetSubjectsOf / sh:targetObjectsOf.
[<RequireQualifiedAccess>]
type TargetSpec =
    | Class of Uri
    | Node of Node
    | SubjectsOf of Uri
    | ObjectsOf of Uri

/// sh:path -- not always a single predicate. The full SHACL property-path grammar.
[<RequireQualifiedAccess>]
type PropertyPath =
    | Predicate of Uri
    | Inverse of PropertyPath
    | Sequence of NonEmptyList<PropertyPath>
    | Alternative of NonEmptyList<PropertyPath>
    | ZeroOrMore of PropertyPath
    | OneOrMore of PropertyPath
    | ZeroOrOne of PropertyPath

/// An author-supplied SPARQL **SELECT** query as a SHACL-SPARQL constraint (sh:sparql). The query
/// text is written by the shape's author (a developer), never derived from request input.
///
/// SEMANTICS: `$this` is pre-bound to the focus node, and every result ROW the query returns is
/// reported as a violation -- so a CONFORMING focus node is one the query returns no rows for. Write
/// the query to select what is WRONG, not what is right.
///
/// ```fsharp
/// { Query = "SELECT $this WHERE { $this <https://schema.org/position> ?p . FILTER (?p <= 0) }"
///   Message = Some "position must be positive"
///   Prefixes = [] }
/// ```
///
/// SELECT ONLY -- an `ASK { ... }` query is REJECTED (an InvalidOperationException out of
/// `Shacl.toShapesGraph`, at shape-authoring time). SHACL's sh:sparql is SELECT-based by definition
/// (§5.2); `sh:ask` belongs to `sh:SPARQLAskValidator` inside a custom `sh:ConstraintComponent`
/// (§6.2.3.2), which this package does not emit, and dotNetRDF maps sh:sparql to its SELECT
/// validator unconditionally. Invert an ASK to get the equivalent SELECT: an `ASK { P }` that must
/// hold becomes `SELECT $this WHERE { FILTER NOT EXISTS { P } }`.
///
/// `Prefixes` are rendered as `PREFIX name: <uri>` lines prepended to `Query`. The whole text must
/// parse as a SPARQL SELECT: `Shacl.toShapesGraph` parses it and raises at shape-build time if it
/// does not, rather than letting a typo fail every request to the guarded resource.
type SparqlConstraint =
    { Query: string
      Message: string option
      Prefixes: (string * string) list }

/// Every SHACL Core property constraint component this package supports, plus sh:sparql. A total DU:
/// each case only carries what SHACL itself requires for that constraint.
[<RequireQualifiedAccess>]
type PropertyConstraint =
    | Class of Uri
    | Datatype of XsdDatatype
    | NodeKind of NodeKind
    | MinCount of int
    | MaxCount of int
    | MinExclusive of Literal
    | MinInclusive of Literal
    | MaxExclusive of Literal
    | MaxInclusive of Literal
    | MinLength of int
    | MaxLength of int
    | Pattern of pattern: string * flags: string option
    | LanguageIn of NonEmptyList<string>
    | UniqueLang of bool
    | Equals of Uri
    | Disjoint of Uri
    | LessThan of Uri
    | LessThanOrEquals of Uri
    | Node of ShapeDecl
    | QualifiedValueShape of shape: ShapeDecl * minCount: int option * maxCount: int option * disjoint: bool
    | HasValue of Value
    | AllowedValues of NonEmptyList<Value>
    | Sparql of SparqlConstraint

/// A single sh:PropertyShape: a path plus its constraints.
and PropertyShapeSpec =
    { Path: PropertyPath
      Constraints: PropertyConstraint list
      Severity: Severity option
      Message: string option }

/// A single sh:NodeShape: zero or more targets (empty is valid -- a shape referenced only via
/// sh:node/sh:qualifiedValueShape), its property shapes, and its own closedness/severity/message.
and NodeShapeSpec =
    { Targets: TargetSpec list
      Properties: PropertyShapeSpec list
      Closed: bool
      IgnoredProperties: Uri list
      Severity: Severity option
      Message: string option }

/// A total DU over every top-level SHACL shape form this package supports.
and ShapeDecl =
    | RecordShape of NodeShapeSpec
    | EnumShape of targetClass: Uri * cases: NonEmptyList<Uri>
    | And of NonEmptyList<ShapeDecl>
    | Or of NonEmptyList<ShapeDecl>
    | Not of ShapeDecl
    | Xone of NonEmptyList<ShapeDecl>
