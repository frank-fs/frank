namespace Frank.Semantic

open System

/// Non-empty by construction — an empty/orphaned sh:in list is unrepresentable.
type NonEmptyList<'T> = { Head: 'T; Tail: 'T list }

module NonEmptyList =
    let ofList =
        function
        | [] -> None
        | x :: xs -> Some { Head = x; Tail = xs }

    let toList n = n.Head :: n.Tail

/// Closed set of xsd datatypes Frank maps F# primitives to. An arbitrary datatype IRI is unrepresentable.
type XsdDatatype =
    | XsdInteger
    | XsdLong
    | XsdDecimal
    | XsdDouble
    | XsdBoolean
    | XsdString
    | XsdDateTime

/// One property constraint. Path required (no pathless property shape).
type PropertyShape =
    { Path: Uri
      Datatype: XsdDatatype option
      MinCount: int
      MaxCount: int option
      Pattern: string option }

/// A node shape is EITHER a record OR a nullary-union enum — never both, never neither.
type ShapeDecl =
    | RecordShape of targetClass: Uri * properties: PropertyShape list
    | EnumShape of targetClass: Uri * cases: NonEmptyList<Uri>
