namespace Frank.Cli.Core.Analysis

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text

type FieldKind =
    | Primitive of xsdType: string
    | Optional of inner: FieldKind
    | Collection of element: FieldKind
    | Reference of typeName: string

type AnalyzedField = { Name: string; Kind: FieldKind; IsRequired: bool }
type DuCase = { Name: string; Fields: AnalyzedField list }

type TypeKind =
    | Record of fields: AnalyzedField list
    | DiscriminatedUnion of cases: DuCase list
    | Enum of values: string list

type AnalyzedType = {
    FullName: string
    ShortName: string
    Kind: TypeKind
    GenericParameters: string list
    SourceLocation: SourceLocation option
}

module TypeAnalyzer =

    let private tryGetFullName (td: FSharpEntity) =
        try Some td.FullName with _ -> None

    let rec mapFieldType (fsharpType: FSharpType) : FieldKind =
        if fsharpType.HasTypeDefinition then
            let td = fsharpType.TypeDefinition
            let fullNameOpt = tryGetFullName td
            // Check generic wrapper types by DisplayName first (works for abbreviations like option, list)
            match td.DisplayName with
            | "option" | "Option" when fsharpType.GenericArguments.Count > 0 ->
                Optional(mapFieldType fsharpType.GenericArguments.[0])
            | "list" | "List" when (fullNameOpt |> Option.exists (fun n -> n.Contains("FSharp"))) && fsharpType.GenericArguments.Count > 0 ->
                Collection(mapFieldType fsharpType.GenericArguments.[0])
            | "Set" when (fullNameOpt |> Option.exists (fun n -> n.Contains("FSharp"))) && fsharpType.GenericArguments.Count > 0 ->
                Collection(mapFieldType fsharpType.GenericArguments.[0])
            | "seq" when fsharpType.GenericArguments.Count > 0 ->
                Collection(mapFieldType fsharpType.GenericArguments.[0])
            | _ ->
                match fullNameOpt with
                | Some fullName ->
                    match fullName with
                    | "System.String" -> Primitive "xsd:string"
                    | "System.Int32" -> Primitive "xsd:integer"
                    | "System.Int64" -> Primitive "xsd:long"
                    | "System.Double" -> Primitive "xsd:double"
                    | "System.Single" -> Primitive "xsd:float"
                    | "System.Boolean" -> Primitive "xsd:boolean"
                    | "System.DateTime" | "System.DateTimeOffset" -> Primitive "xsd:dateTime"
                    | "System.Guid" -> Primitive "xsd:string"
                    | "System.Decimal" -> Primitive "xsd:double"
                    | _ when td.IsArrayType && fsharpType.GenericArguments.Count > 0 ->
                        Collection(mapFieldType fsharpType.GenericArguments.[0])
                    | _ -> Reference td.DisplayName
                | None ->
                    // FullName not available -- try resolving abbreviation
                    if fsharpType.IsAbbreviation then
                        mapFieldType fsharpType.AbbreviatedType
                    else
                        Reference td.DisplayName
        elif fsharpType.IsAbbreviation then
            mapFieldType fsharpType.AbbreviatedType
        else
            Reference(fsharpType.Format(FSharpDisplayContext.Empty))

    let private makeField (name: string) (fsharpType: FSharpType) : AnalyzedField =
        let kind = mapFieldType fsharpType
        let isRequired =
            match kind with
            | Optional _ -> false
            | _ -> true
        { Name = name; Kind = kind; IsRequired = isRequired }

    let private entityToSourceLocation (entity: FSharpEntity) : SourceLocation option =
        try
            let r = entity.DeclarationLocation
            Some { File = r.FileName; Line = r.StartLine; Column = r.StartColumn }
        with _ ->
            None

    let rec collectEntities (entity: FSharpEntity) : AnalyzedType list =
        let nested =
            try
                entity.NestedEntities
                |> Seq.collect collectEntities
                |> Seq.toList
            with _ -> []

        let entityFullName = tryGetFullName entity |> Option.defaultValue entity.DisplayName

        if entity.DisplayName.StartsWith("<") then
            nested // skip compiler-generated
        elif entity.IsFSharpUnion then
            let cases =
                entity.UnionCases
                |> Seq.map (fun uc ->
                    { Name = uc.Name
                      Fields =
                        uc.Fields
                        |> Seq.map (fun f -> makeField f.Name f.FieldType)
                        |> Seq.toList })
                |> Seq.toList
            { FullName = entityFullName
              ShortName = entity.DisplayName
              Kind = DiscriminatedUnion cases
              GenericParameters =
                entity.GenericParameters
                |> Seq.map (fun p -> p.Name)
                |> Seq.toList
              SourceLocation = entityToSourceLocation entity }
            :: nested
        elif entity.IsFSharpRecord then
            let fields =
                entity.FSharpFields
                |> Seq.map (fun f -> makeField f.Name f.FieldType)
                |> Seq.toList
            { FullName = entityFullName
              ShortName = entity.DisplayName
              Kind = Record fields
              GenericParameters =
                entity.GenericParameters
                |> Seq.map (fun p -> p.Name)
                |> Seq.toList
              SourceLocation = entityToSourceLocation entity }
            :: nested
        elif entity.IsEnum then
            let values =
                entity.FSharpFields
                |> Seq.filter (fun f -> f.Name <> "value__")
                |> Seq.map (fun f -> f.Name)
                |> Seq.toList
            { FullName = entityFullName
              ShortName = entity.DisplayName
              Kind = Enum values
              GenericParameters = []
              SourceLocation = entityToSourceLocation entity }
            :: nested
        else
            nested

    /// Analyze types from FCS check results
    let analyzeTypes (checkResults: FSharpCheckProjectResults) : AnalyzedType list =
        checkResults.AssemblySignature.Entities
        |> Seq.collect collectEntities
        |> Seq.toList
