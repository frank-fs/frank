namespace Frank.Validation

open System
open Frank.Rdf

type NonEmptyList<'T> = { Head: 'T; Tail: 'T list }

module NonEmptyList =
    let ofList (items: 'T list) : NonEmptyList<'T> option =
        match items with
        | [] -> None
        | head :: tail -> Some { Head = head; Tail = tail }

    let toList (nel: NonEmptyList<'T>) : 'T list = nel.Head :: nel.Tail

[<Struct; RequireQualifiedAccess>]
type XsdDatatype =
    | Integer
    | Long
    | Decimal
    | Double
    | Boolean
    | String
    | DateTime

[<Struct; RequireQualifiedAccess>]
type NodeKind =
    | BlankNode
    | Iri
    | Literal
    | BlankNodeOrIri
    | BlankNodeOrLiteral
    | IriOrLiteral

[<Struct; RequireQualifiedAccess>]
type Severity =
    | Violation
    | Warning
    | Info

[<Struct; RequireQualifiedAccess>]
type TargetSpec =
    | Class of classUri: Uri
    | Node of node: Node
    | SubjectsOf of subjectsOfUri: Uri
    | ObjectsOf of objectsOfUri: Uri

[<RequireQualifiedAccess>]
type PropertyPath =
    | Predicate of Uri
    | Inverse of PropertyPath
    | Sequence of NonEmptyList<PropertyPath>
    | Alternative of NonEmptyList<PropertyPath>
    | ZeroOrMore of PropertyPath
    | OneOrMore of PropertyPath
    | ZeroOrOne of PropertyPath

type SparqlConstraint =
    { Query: string
      Message: string option
      Prefixes: (string * string) list }

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

and PropertyShapeSpec =
    { Path: PropertyPath
      Constraints: PropertyConstraint list
      Severity: Severity option
      Message: string option }

and NodeShapeSpec =
    { Targets: TargetSpec list
      Properties: PropertyShapeSpec list
      Closed: bool
      IgnoredProperties: Uri list
      Severity: Severity option
      Message: string option }

and ShapeDecl =
    | RecordShape of NodeShapeSpec
    | EnumShape of targetClass: Uri * cases: NonEmptyList<Uri>
    | And of NonEmptyList<ShapeDecl>
    | Or of NonEmptyList<ShapeDecl>
    | Not of ShapeDecl
    | Xone of NonEmptyList<ShapeDecl>
