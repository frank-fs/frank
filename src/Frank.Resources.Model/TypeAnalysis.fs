namespace Frank.Resources.Model

/// Source code location for a type definition.
type SourceLocation =
    { File: string; Line: int; Column: int }

type FieldKind =
    | Primitive of XsdType: string
    | Guid
    | Optional of Inner: FieldKind
    | Collection of Element: FieldKind
    | Reference of TypeName: string

type ConstraintAttribute =
    | PatternAttr of Regex: string
    | MinInclusiveAttr of Value: obj
    | MaxInclusiveAttr of Value: obj
    | MinLengthAttr of Length: int
    | MaxLengthAttr of Length: int

type AnalyzedField =
    { Name: string
      Kind: FieldKind
      IsRequired: bool
      IsScalar: bool
      Constraints: ConstraintAttribute list }

type DuCase =
    { Name: string
      Fields: AnalyzedField list }

type TypeKind =
    | Record of Fields: AnalyzedField list
    | DiscriminatedUnion of Cases: DuCase list
    | Enum of Values: string list

type AnalyzedType =
    { FullName: string
      ShortName: string
      Kind: TypeKind
      GenericParameters: string list
      SourceLocation: SourceLocation option
      IsClosed: bool }
