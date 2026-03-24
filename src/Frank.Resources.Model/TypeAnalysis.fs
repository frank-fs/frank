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

/// Convention-based mapping from transition event names to SHACL shape URIs.
/// Uses urn:frank:shape:{encoded-type} (no assembly segment). This differs from
/// Frank.Validation.UriConventions which includes an assembly segment — the assembly
/// is unavailable during static analysis / CLI generation.
module ShapeUri =

    /// Build a SHACL shape URI for a type full name.
    let buildShapeUri (fullName: string) : string =
        let encoded = System.Uri.EscapeDataString(fullName)
        sprintf "urn:frank:shape:%s" encoded

    /// Build a map from event names to shape URIs by matching DU case names
    /// (with payload fields) and record short names against analyzed types.
    let resolveEventShapeMap (typeInfo: AnalyzedType list) : Map<string, string> =
        typeInfo
        |> List.collect (fun t ->
            match t.Kind with
            | DiscriminatedUnion cases ->
                cases
                |> List.filter (fun c -> not c.Fields.IsEmpty)
                |> List.map (fun c ->
                    let shapeFullName = sprintf "%s.%s" t.FullName c.Name
                    (c.Name, buildShapeUri shapeFullName))
            | Record _ -> [ (t.ShortName, buildShapeUri t.FullName) ]
            | Enum _ -> [])
        |> Map.ofList
