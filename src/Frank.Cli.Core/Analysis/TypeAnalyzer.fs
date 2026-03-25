namespace Frank.Cli.Core.Analysis

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text
open Frank.Resources.Model
open Frank.Cli.Core.Shared

module TypeAnalyzer =

    let internal tryGetFullName (td: FSharpEntity) =
        tryFcs None (fun () -> Some td.FullName)

    let rec mapFieldType (fsharpType: FSharpType) : FieldKind =
        if fsharpType.HasTypeDefinition then
            let td = fsharpType.TypeDefinition
            let fullNameOpt = tryGetFullName td
            // Check generic wrapper types by DisplayName first (works for abbreviations like option, list)
            match td.DisplayName with
            | "option"
            | "Option" when fsharpType.GenericArguments.Count > 0 ->
                Optional(mapFieldType fsharpType.GenericArguments.[0])
            | "list"
            | "List" when
                (fullNameOpt |> Option.exists (fun n -> n.Contains("FSharp")))
                && fsharpType.GenericArguments.Count > 0
                ->
                Collection(mapFieldType fsharpType.GenericArguments.[0])
            | "Set" when
                (fullNameOpt |> Option.exists (fun n -> n.Contains("FSharp")))
                && fsharpType.GenericArguments.Count > 0
                ->
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
                    | "System.DateTime"
                    | "System.DateTimeOffset" -> Primitive "xsd:dateTime"
                    | "System.DateOnly" -> Primitive "xsd:date"
                    | "System.TimeOnly" -> Primitive "xsd:time"
                    | "System.TimeSpan" -> Primitive "xsd:duration"
                    | "System.Uri" -> Primitive "xsd:anyURI"
                    | "System.Guid" -> Guid
                    | "System.Decimal" -> Primitive "xsd:decimal"
                    | _ when td.IsArrayType && fsharpType.GenericArguments.Count > 0 ->
                        let elem = fsharpType.GenericArguments.[0]

                        let isByte =
                            if elem.HasTypeDefinition then
                                let dn = elem.TypeDefinition.DisplayName

                                let fn = tryFcs "" (fun () -> elem.TypeDefinition.FullName)

                                fn = "System.Byte" || dn = "Byte" || dn = "byte"
                            else
                                false

                        if isByte then
                            Primitive "xsd:base64Binary"
                        else
                            Collection(mapFieldType elem)
                    | _ -> Reference td.DisplayName
                | None ->
                    // FullName not available -- check for array type before trying abbreviation
                    if td.IsArrayType && fsharpType.GenericArguments.Count > 0 then
                        let elem = fsharpType.GenericArguments.[0]

                        let isByte =
                            if elem.HasTypeDefinition then
                                let dn = elem.TypeDefinition.DisplayName

                                let fn = tryFcs "" (fun () -> elem.TypeDefinition.FullName)

                                fn = "System.Byte" || dn = "Byte" || dn = "byte"
                            else
                                false

                        if isByte then
                            Primitive "xsd:base64Binary"
                        else
                            Collection(mapFieldType elem)
                    elif fsharpType.IsAbbreviation then
                        mapFieldType fsharpType.AbbreviatedType
                    else
                        Reference td.DisplayName
        elif fsharpType.IsAbbreviation then
            mapFieldType fsharpType.AbbreviatedType
        else
            Reference(fsharpType.Format(FSharpDisplayContext.Empty))

    let internal extractConstraintAttributes (field: FSharpField) : ConstraintAttribute list =
        tryFcs [] (fun () ->
            Seq.append field.FieldAttributes field.PropertyAttributes
            |> Seq.choose (fun attr ->
                let attrName = tryFcs "" (fun () -> attr.AttributeType.DisplayName)

                match attrName with
                | "PatternAttribute"
                | "Pattern" ->
                    match attr.ConstructorArguments |> Seq.tryHead with
                    | Some(_, (:? string as regex)) -> Some(PatternAttr regex)
                    | _ -> None
                | "MinInclusiveAttribute"
                | "MinInclusive" ->
                    match attr.ConstructorArguments |> Seq.tryHead with
                    | Some(_, value) -> Some(MinInclusiveAttr value)
                    | _ -> None
                | "MaxInclusiveAttribute"
                | "MaxInclusive" ->
                    match attr.ConstructorArguments |> Seq.tryHead with
                    | Some(_, value) -> Some(MaxInclusiveAttr value)
                    | _ -> None
                | "MinLengthAttribute"
                | "MinLength" ->
                    match attr.ConstructorArguments |> Seq.tryHead with
                    | Some(_, (:? int as n)) -> Some(MinLengthAttr n)
                    | _ -> None
                | "MaxLengthAttribute"
                | "MaxLength" ->
                    match attr.ConstructorArguments |> Seq.tryHead with
                    | Some(_, (:? int as n)) -> Some(MaxLengthAttr n)
                    | _ -> None
                | _ -> None)
            |> Seq.toList)

    let internal makeField (name: string) (fsharpType: FSharpType) : AnalyzedField =
        let kind = mapFieldType fsharpType

        let isRequired =
            match kind with
            | Optional _ -> false
            | _ -> true

        let isScalar =
            match kind with
            | Collection _ -> false
            | _ -> true

        { Name = name
          Kind = kind
          IsRequired = isRequired
          IsScalar = isScalar
          Constraints = [] }

    let internal makeFieldFromFSharpField (field: FSharpField) : AnalyzedField =
        let baseField = makeField field.Name field.FieldType
        let constraints = extractConstraintAttributes field

        { baseField with
            Constraints = constraints }

    let internal entityToSourceLocation (entity: FSharpEntity) : SourceLocation option =
        tryFcs None (fun () ->
            let r = entity.DeclarationLocation

            Some
                { File = r.FileName
                  Line = r.StartLine
                  Column = r.StartColumn })

    let rec collectEntities (entity: FSharpEntity) : AnalyzedType list =
        let nested =
            tryFcs [] (fun () -> entity.NestedEntities |> Seq.collect collectEntities |> Seq.toList)

        let entityFullName = tryGetFullName entity |> Option.defaultValue entity.DisplayName

        if entity.DisplayName.StartsWith("<") then
            nested // skip compiler-generated
        elif entity.IsFSharpUnion then
            let cases =
                entity.UnionCases
                |> Seq.map (fun uc ->
                    { Name = uc.Name
                      Fields = uc.Fields |> Seq.map (fun f -> makeField f.Name f.FieldType) |> Seq.toList })
                |> Seq.toList

            { FullName = entityFullName
              ShortName = entity.DisplayName
              Kind = DiscriminatedUnion cases
              GenericParameters = entity.GenericParameters |> Seq.map (fun p -> p.Name) |> Seq.toList
              SourceLocation = entityToSourceLocation entity
              IsClosed = false }
            :: nested
        elif entity.IsFSharpRecord then
            let fields = entity.FSharpFields |> Seq.map makeFieldFromFSharpField |> Seq.toList

            { FullName = entityFullName
              ShortName = entity.DisplayName
              Kind = Record fields
              GenericParameters = entity.GenericParameters |> Seq.map (fun p -> p.Name) |> Seq.toList
              SourceLocation = entityToSourceLocation entity
              IsClosed = true }
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
              SourceLocation = entityToSourceLocation entity
              IsClosed = false }
            :: nested
        else
            nested

    /// Analyze types from FCS check results
    let analyzeTypes (checkResults: FSharpCheckProjectResults) : AnalyzedType list =
        checkResults.AssemblySignature.Entities
        |> Seq.collect collectEntities
        |> Seq.toList
