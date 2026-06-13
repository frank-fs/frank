namespace Frank.Semantic

open System

/// Computation expression builder for declaring vocabulary alignments.
/// Evaluates eagerly to a VocabularyRegistry value at CE construction time.
type VocabularyBuilder() =

    member _.Yield(_: unit) = VocabularyRegistry.empty

    /// Register a prefix mapping. Duplicate names with different URIs raise.
    [<CustomOperation("prefix")>]
    member _.Prefix(state: VocabularyRegistry, name: string, uri: string) : VocabularyRegistry =
        let baseUri = Uri(uri)

        match Map.tryFind name state.Prefixes with
        | Some existing when existing = baseUri -> state
        | Some _ -> invalidOp $"Prefix '{name}' is already declared with a different URI."
        | None ->
            { state with
                Prefixes = Map.add name baseUri state.Prefixes }

    /// Declare a prefix as in-scope for IRI resolution. Duplicate entries raise.
    [<CustomOperation("using")>]
    member _.Using(state: VocabularyRegistry, prefix: string) : VocabularyRegistry =
        if Set.contains prefix state.Using then
            invalidOp $"Prefix '{prefix}' is already in the using set."

        { state with
            Using = Set.add prefix state.Using }

    /// Map a type to an owl:equivalentClass IRI. IRI must use a declared prefix.
    [<CustomOperation("equivalentClass")>]
    member _.EquivalentClass(state: VocabularyRegistry, type': Type, iri: string) : VocabularyRegistry =
        let resolved = VocabularyRegistry.resolveIri state.Prefixes iri
        let key = VocabularyRegistry.keyOf type'

        match Map.tryFind key state.EquivalentClasses with
        | Some existing when existing = resolved -> state
        | Some _ -> invalidOp $"EquivalentClass for '{type'.Name}' is already declared with a different IRI."
        | None ->
            { state with
                EquivalentClasses = Map.add key resolved state.EquivalentClasses }

    /// Map a type to an rdfs:seeAlso IRI. IRI must use a declared prefix.
    [<CustomOperation("seeAlso")>]
    member _.SeeAlso(state: VocabularyRegistry, type': Type, iri: string) : VocabularyRegistry =
        let resolved = VocabularyRegistry.resolveIri state.Prefixes iri
        let key = VocabularyRegistry.keyOf type'
        let existing = state.SeeAlso |> Map.tryFind key |> Option.defaultValue []

        { state with
            SeeAlso = Map.add key (existing @ [ resolved ]) state.SeeAlso }

    /// Map a field of a type to an rdfs:seeAlso IRI. IRI must use a declared prefix.
    [<CustomOperation("fieldSeeAlso")>]
    member _.FieldSeeAlso(state: VocabularyRegistry, type': Type, fieldName: string, iri: string) : VocabularyRegistry =
        let resolved = VocabularyRegistry.resolveIri state.Prefixes iri
        let key = VocabularyRegistry.fieldKeyOf type' fieldName
        let existing = state.FieldSeeAlso |> Map.tryFind key |> Option.defaultValue []

        { state with
            FieldSeeAlso = Map.add key (existing @ [ resolved ]) state.FieldSeeAlso }

    /// Map a type to a PROV-O class for provenance typing.
    [<CustomOperation("provClass")>]
    member _.ProvClass(state: VocabularyRegistry, type': Type, provOClass: ProvOClass) : VocabularyRegistry =
        let key = VocabularyRegistry.keyOf type'

        match Map.tryFind key state.ProvClasses with
        | Some existing when existing = provOClass -> state
        | Some _ -> invalidOp $"ProvClass for '{type'.Name}' is already declared with a different class."
        | None ->
            { state with
                ProvClasses = Map.add key provOClass state.ProvClasses }

    /// Add a regex constraint for a field of a type.
    [<CustomOperation("constrainPattern")>]
    member _.ConstrainPattern
        (state: VocabularyRegistry, type': Type, fieldName: string, pattern: string)
        : VocabularyRegistry =
        let key = VocabularyRegistry.fieldKeyOf type' fieldName

        match Map.tryFind key state.ConstraintPatterns with
        | Some existing when existing = pattern -> state
        | Some _ ->
            invalidOp $"ConstraintPattern for '{type'.Name}.{fieldName}' is already declared with a different pattern."
        | None ->
            { state with
                ConstraintPatterns = Map.add key pattern state.ConstraintPatterns }

    /// Deep-union another registry into the current one. Raises on conflicts per field semantics.
    [<CustomOperation("include")>]
    member _.Include(state: VocabularyRegistry, other: VocabularyRegistry) : VocabularyRegistry =
        VocabularyRegistry.include' state other

[<AutoOpen>]
module VocabularyBuilderExtensions =

    /// The vocabulary computation expression builder.
    let vocabulary = VocabularyBuilder()
