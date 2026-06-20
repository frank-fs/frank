namespace Frank.Semantic

open System
open Frank.Semantic.LockFile

type ResolvedField =
    { Name: string
      Iri: Uri option
      SeeAlso: Uri list
      ConstraintPattern: string option }

type ResolvedCase =
    { CaseName: string
      Iri: Uri
      IsNullary: bool }

type ResolvedResource =
    { FSharpType: string
      LocalName: string
      GenericArity: int
      ClassIri: Uri option
      EquivalentClass: Uri option
      SeeAlso: Uri list
      ProvClass: ProvOClass option
      Fields: ResolvedField list
      Cases: ResolvedCase list
      UnionCaseCount: int }

type ResolvedModel =
    { Prefixes: Map<string, Uri>
      Using: Set<string>
      Resources: ResolvedResource list }

module ResolvedModel =

    // F# reserved keywords that cannot be bare DU case names without backticks.
    let private fsharpKeywords =
        Set.ofList
            [ "abstract"
              "and"
              "as"
              "assert"
              "base"
              "begin"
              "class"
              "default"
              "delegate"
              "do"
              "done"
              "downcast"
              "downto"
              "elif"
              "else"
              "end"
              "exception"
              "extern"
              "false"
              "finally"
              "fixed"
              "for"
              "fun"
              "function"
              "global"
              "if"
              "in"
              "inherit"
              "inline"
              "interface"
              "internal"
              "lazy"
              "let"
              "match"
              "member"
              "module"
              "mutable"
              "namespace"
              "new"
              "not"
              "null"
              "of"
              "open"
              "or"
              "override"
              "private"
              "public"
              "rec"
              "return"
              "select"
              "static"
              "struct"
              "then"
              "to"
              "true"
              "try"
              "type"
              "upcast"
              "use"
              "val"
              "void"
              "when"
              "while"
              "with"
              "yield" ]

    let private isValidIdentifier (name: string) : bool =
        if String.IsNullOrEmpty name then
            false
        elif not (Char.IsLetter(name.[0]) || name.[0] = '_') then
            false
        else
            name |> Seq.forall (fun c -> Char.IsLetterOrDigit c || c = '_')

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

    // Fix 8: one total traversal combinator, O(n) via cons + List.rev.
    let private traverseResult (f: 'a -> Result<'b, 'e>) (xs: 'a list) : Result<'b list, 'e> =
        let folder acc x =
            match acc with
            | Error e -> Error e
            | Ok ys ->
                match f x with
                | Error e -> Error e
                | Ok y -> Ok(y :: ys)

        match List.fold folder (Ok []) xs with
        | Error e -> Error e
        | Ok ys -> Ok(List.rev ys)

    /// Build a prefix map from the lock's vocabularies block.
    /// Each key is the vocabulary prefix (e.g. "schema"); each value is its base Uri.
    let private lockPrefixes (vocabularies: Map<string, VocabularyEntry>) : Map<string, Uri> =
        vocabularies |> Map.map (fun _ entry -> Uri(entry.Uri))

    let private buildField
        (prefixes: Map<string, Uri>)
        (registry: VocabularyRegistry)
        (fsharpType: string)
        (f: FieldMapping)
        : Result<ResolvedField, string> =
        match VocabularyRegistry.tryResolveIri prefixes f.Iri with
        | Error e -> Error $"type '{fsharpType}', field '{f.Name}': {e}"
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
        traverseResult (buildField prefixes registry fsharpType) fields

    let private buildCase
        (prefixes: Map<string, Uri>)
        (fsharpType: string)
        (c: CaseMapping)
        : Result<ResolvedCase option, string> =
        match VocabularyRegistry.tryResolveIri prefixes c.Iri with
        | Ok(Some iri) ->
            Ok(
                Some
                    { CaseName = c.Name
                      Iri = iri
                      IsNullary = c.Payload.IsEmpty }
            )
        | Ok None ->
            // Confirmed but Iri=None: the lock is internally inconsistent (e.g. hand-edited).
            // `accept` would have rejected this — surface as hard Error, not a silent drop.
            Error $"type '{fsharpType}', case '{c.Name}': status is confirmed but no IRI is set (lock inconsistency)"
        | Error _ ->
            // IRI present but prefix unresolvable: defense-in-depth drop.
            // `accept` already validates Confirmed IRIs against declared prefixes, so
            // this is a last-resort guard — degrade to the generated `| _ ->` wildcard
            // rather than aborting codegen for the whole module.
            Ok None

    let private buildCases
        (prefixes: Map<string, Uri>)
        (fsharpType: string)
        (cs: CaseMapping list)
        : Result<ResolvedCase list, string> =
        let confirmed = cs |> List.filter (fun c -> c.Status = Confirmed)

        confirmed
        |> traverseResult (buildCase prefixes fsharpType)
        |> Result.map (List.choose id)

    let private buildResolvedResource
        (prefixes: Map<string, Uri>)
        (registry: VocabularyRegistry)
        (m: Mapping)
        (localName: string)
        (genericArity: int)
        (unionCaseCount: int)
        (cases: ResolvedCase list)
        : Result<ResolvedResource, string> =
        match VocabularyRegistry.tryResolveIri prefixes m.Iri with
        | Error e -> Error $"type '{m.FSharpType}': {e}"
        | Ok classIri ->
            let includedFields =
                MappingShape.activePayloadFields m.Shape
                |> List.filter (fun f -> f.Status <> Excluded)

            match buildFields prefixes registry m.FSharpType includedFields with
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
                      Fields = fields
                      Cases = cases
                      UnionCaseCount = unionCaseCount }

    let private buildResource
        (prefixes: Map<string, Uri>)
        (registry: VocabularyRegistry)
        (m: Mapping)
        : Result<ResolvedResource, string> =
        let localName, genericArity = parseLocalName m.FSharpType

        let unionCaseCount =
            match m.Shape with
            | MappingShape.Union cs -> cs.Length
            | MappingShape.Record _ -> 0

        let casesResult =
            match m.Shape with
            | MappingShape.Union cs -> buildCases prefixes m.FSharpType cs
            | MappingShape.Record _ -> Ok []

        match casesResult with
        | Error e -> Error e
        | Ok cases -> buildResolvedResource prefixes registry m localName genericArity unionCaseCount cases

    let private buildResources
        (prefixes: Map<string, Uri>)
        (registry: VocabularyRegistry)
        (mappings: Mapping list)
        : Result<ResolvedResource list, string> =
        traverseResult (buildResource prefixes registry) mappings

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

    // Fix 4: reject reserved keywords and non-identifier local names for class-mapped resources.
    let private checkReservedKeywords (resources: ResolvedResource list) : Result<unit, string> =
        let withClassIri = resources |> List.filter (fun r -> r.ClassIri.IsSome)

        let check (r: ResolvedResource) =
            if Set.contains (r.LocalName.ToLowerInvariant()) fsharpKeywords then
                Error
                    $"resource '{r.FSharpType}' has local name '{r.LocalName}' which is an F# reserved keyword; rename the type or use a backtick alias in your vocabulary"
            elif not (isValidIdentifier r.LocalName) then
                Error
                    $"resource '{r.FSharpType}' has local name '{r.LocalName}' which is not a valid F# identifier; rename the type"
            else
                Ok()

        let folder acc r =
            match acc with
            | Error e -> Error e
            | Ok() -> check r

        List.fold folder (Ok()) withClassIri

    let build (registry: VocabularyRegistry) (lock: LockFile) : Result<ResolvedModel, string> =
        let lockIriPrefixes = lockPrefixes lock.Vocabularies
        let included = lock.Mappings |> List.filter (fun m -> m.Status <> Excluded)

        match buildResources lockIriPrefixes registry included with
        | Error e -> Error e
        | Ok resources ->
            match checkLocalNameCollisions resources with
            | Error e -> Error e
            | Ok() ->
                match checkReservedKeywords resources with
                | Error e -> Error e
                | Ok() ->
                    Ok
                        { Prefixes = registry.Prefixes
                          Using = registry.Using
                          Resources = resources }
