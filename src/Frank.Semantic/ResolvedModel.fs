namespace Frank.Semantic

open System
open Frank.Semantic.LockFile

type ResolvedField =
    { Name: string
      Iri: Uri option
      SeeAlso: Uri list
      ConstraintPattern: string option }

type ResolvedResource =
    { FSharpType: string
      LocalName: string
      GenericArity: int
      ClassIri: Uri option
      EquivalentClass: Uri option
      SeeAlso: Uri list
      ProvClass: ProvOClass option
      Fields: ResolvedField list }

type ResolvedModel =
    { Prefixes: Map<string, Uri>
      Using: Set<string>
      Resources: ResolvedResource list }

module ResolvedModel =

    let private parseLocalName (fsharpType: string) : string * int =
        let segment =
            match fsharpType.LastIndexOf('.') with
            | -1 -> fsharpType
            | idx -> fsharpType.[idx + 1 ..]

        match segment.IndexOf('`') with
        | -1 -> segment, 0
        | backtickIdx ->
            let name = segment.[.. backtickIdx - 1]
            let arityStr = segment.[backtickIdx + 1 ..]

            match Int32.TryParse(arityStr) with
            | true, n -> name, n
            | false, _ -> segment, 0

    let private resolveOptionalIri
        (prefixes: Map<string, Uri>)
        (context: string)
        (iri: string option)
        : Result<Uri option, string> =
        match iri with
        | None -> Ok None
        | Some s ->
            try
                Ok(Some(VocabularyRegistry.resolveIri prefixes s))
            with :? InvalidOperationException as ex ->
                Error $"type '{context}': {ex.Message}"

    let private buildField
        (prefixes: Map<string, Uri>)
        (registry: VocabularyRegistry)
        (fsharpType: string)
        (f: FieldMapping)
        : Result<ResolvedField, string> =
        match resolveOptionalIri prefixes fsharpType f.Iri with
        | Error e -> Error e
        | Ok iri ->
            let seeAlso =
                registry.FieldSeeAlso
                |> Map.tryFind (fsharpType, f.Name)
                |> Option.defaultValue []

            let pattern = registry.ConstraintPatterns |> Map.tryFind (fsharpType, f.Name)

            Ok
                { Name = f.Name
                  Iri = iri
                  SeeAlso = seeAlso
                  ConstraintPattern = pattern }

    let private buildFields
        (prefixes: Map<string, Uri>)
        (registry: VocabularyRegistry)
        (fsharpType: string)
        (fields: FieldMapping list)
        : Result<ResolvedField list, string> =
        let folder acc f =
            match acc with
            | Error e -> Error e
            | Ok xs ->
                match buildField prefixes registry fsharpType f with
                | Error e -> Error e
                | Ok rf -> Ok(xs @ [ rf ])

        List.fold folder (Ok []) fields

    let private buildResource
        (prefixes: Map<string, Uri>)
        (registry: VocabularyRegistry)
        (m: Mapping)
        : Result<ResolvedResource, string> =
        let localName, genericArity = parseLocalName m.FSharpType

        match resolveOptionalIri prefixes m.FSharpType m.Iri with
        | Error e -> Error e
        | Ok classIri ->
            match buildFields prefixes registry m.FSharpType m.Fields with
            | Error e -> Error e
            | Ok fields ->
                let equivalentClass = registry.EquivalentClasses |> Map.tryFind m.FSharpType

                let seeAlso = registry.SeeAlso |> Map.tryFind m.FSharpType |> Option.defaultValue []

                let provClass = registry.ProvClasses |> Map.tryFind m.FSharpType

                Ok
                    { FSharpType = m.FSharpType
                      LocalName = localName
                      GenericArity = genericArity
                      ClassIri = classIri
                      EquivalentClass = equivalentClass
                      SeeAlso = seeAlso
                      ProvClass = provClass
                      Fields = fields }

    let private buildResources
        (prefixes: Map<string, Uri>)
        (registry: VocabularyRegistry)
        (mappings: Mapping list)
        : Result<ResolvedResource list, string> =
        let folder acc m =
            match acc with
            | Error e -> Error e
            | Ok xs ->
                match buildResource prefixes registry m with
                | Error e -> Error e
                | Ok r -> Ok(xs @ [ r ])

        List.fold folder (Ok []) mappings

    let private checkLocalNameCollisions (resources: ResolvedResource list) : Result<unit, string> =
        let withClassIri = resources |> List.filter (fun r -> r.ClassIri.IsSome)

        let folder (seen: Map<string, string>) (r: ResolvedResource) =
            match seen with
            | _ when Map.containsKey r.LocalName seen ->
                let existing = Map.find r.LocalName seen

                Error
                    $"resources '{existing}' and '{r.FSharpType}' share local name '{r.LocalName}'; cannot generate a distinct DU case"
            | _ -> Ok(Map.add r.LocalName r.FSharpType seen)

        let result =
            List.fold
                (fun acc r ->
                    match acc with
                    | Error e -> Error e
                    | Ok seen -> folder seen r)
                (Ok Map.empty)
                withClassIri

        result |> Result.map (fun _ -> ())

    let build (registry: VocabularyRegistry) (lock: LockFile) : Result<ResolvedModel, string> =
        let prefixes = registry.Prefixes

        match buildResources prefixes registry lock.Mappings with
        | Error e -> Error e
        | Ok resources ->
            match checkLocalNameCollisions resources with
            | Error e -> Error e
            | Ok() ->
                Ok
                    { Prefixes = prefixes
                      Using = registry.Using
                      Resources = resources }
