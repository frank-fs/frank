namespace Frank.Semantic

open System

/// Authored semantic alignments declared via the vocabulary CE.
/// Type-keyed maps use FullName for F# Map key compatibility.
type VocabularyRegistry =
    { Prefixes: Map<string, Uri>
      Using: Set<string>
      EquivalentClasses: Map<string, Uri>
      SeeAlso: Map<string, Uri list>
      FieldSeeAlso: Map<string * string, Uri list>
      ProvClasses: Map<string, ProvOClass>
      ConstraintPatterns: Map<string * string, string> }

module VocabularyRegistry =

    let empty: VocabularyRegistry =
        { Prefixes = Map.empty
          Using = Set.empty
          EquivalentClasses = Map.empty
          SeeAlso = Map.empty
          FieldSeeAlso = Map.empty
          ProvClasses = Map.empty
          ConstraintPatterns = Map.empty }

    let private typeKey (t: Type) =
        match t.FullName with
        | null -> t.AssemblyQualifiedName
        | k -> k

    let private fieldKey (t: Type) (fieldName: string) = (typeKey t, fieldName)

    /// Resolve a prefixed IRI string (e.g. "schema:Order") to a Uri using known prefixes.
    /// Raises InvalidOperationException if the prefix is not declared.
    let resolveIri (prefixes: Map<string, Uri>) (iri: string) : Uri =
        match iri.IndexOf(':') with
        | -1 -> Uri(iri)
        | idx ->
            let prefix = iri.[.. idx - 1]
            let local = iri.[idx + 1 ..]

            match Map.tryFind prefix prefixes with
            | None ->
                invalidOp $"Unknown prefix '{prefix}' in IRI '{iri}'. Declare it with: prefix \"{prefix}\" \"<uri>\""
            | Some baseUri -> Uri(baseUri.AbsoluteUri + local)

    /// Merge two maps, raising on conflicting values; identical-value duplicates absorbed.
    let private mergeMap<'K, 'V when 'K: comparison and 'V: equality>
        (field: string)
        (a: Map<'K, 'V>)
        (b: Map<'K, 'V>)
        : Map<'K, 'V> =
        Map.fold
            (fun acc key value ->
                match Map.tryFind key acc with
                | None -> Map.add key value acc
                | Some existing when existing = value -> acc
                | Some _ ->
                    invalidOp
                        $"Conflicting values for key in '{field}' during include. Use identical values to absorb duplicates.")
            a
            b

    /// Merge two prefix maps, raising on duplicate key with different value.
    let private mergePrefixes (a: Map<string, Uri>) (b: Map<string, Uri>) : Map<string, Uri> =
        Map.fold
            (fun acc key value ->
                match Map.tryFind key acc with
                | None -> Map.add key value acc
                | Some existing when existing = value -> acc
                | Some _ -> invalidOp $"Conflicting prefix '{key}' during include. Each prefix must be declared once.")
            a
            b

    /// Merge two Using sets, raising on duplicate entry.
    let private mergeUsing (a: Set<string>) (b: Set<string>) : Set<string> =
        Set.fold
            (fun acc entry ->
                if Set.contains entry acc then
                    invalidOp $"Conflicting 'using' entry '{entry}' during include. Each prefix must be used once."
                else
                    Set.add entry acc)
            a
            b

    /// Deep-union two registries. Raises on conflicting keys per field semantics.
    let include' (base': VocabularyRegistry) (other: VocabularyRegistry) : VocabularyRegistry =
        { Prefixes = mergePrefixes base'.Prefixes other.Prefixes
          Using = mergeUsing base'.Using other.Using
          EquivalentClasses = mergeMap "EquivalentClasses" base'.EquivalentClasses other.EquivalentClasses
          SeeAlso = mergeMap "SeeAlso" base'.SeeAlso other.SeeAlso
          FieldSeeAlso = mergeMap "FieldSeeAlso" base'.FieldSeeAlso other.FieldSeeAlso
          ProvClasses = mergeMap "ProvClasses" base'.ProvClasses other.ProvClasses
          ConstraintPatterns = mergeMap "ConstraintPatterns" base'.ConstraintPatterns other.ConstraintPatterns }

    /// Look up EquivalentClass by Type.
    let tryFindEquivalentClass (t: Type) (r: VocabularyRegistry) =
        Map.tryFind (typeKey t) r.EquivalentClasses

    /// Look up SeeAlso by Type.
    let tryFindSeeAlso (t: Type) (r: VocabularyRegistry) = Map.tryFind (typeKey t) r.SeeAlso

    /// Look up FieldSeeAlso by Type and field name.
    let tryFindFieldSeeAlso (t: Type) (fieldName: string) (r: VocabularyRegistry) =
        Map.tryFind (fieldKey t fieldName) r.FieldSeeAlso

    /// Look up ProvClass by Type.
    let tryFindProvClass (t: Type) (r: VocabularyRegistry) = Map.tryFind (typeKey t) r.ProvClasses

    /// Look up ConstraintPattern by Type and field name.
    let tryFindConstraintPattern (t: Type) (fieldName: string) (r: VocabularyRegistry) =
        Map.tryFind (fieldKey t fieldName) r.ConstraintPatterns

    /// Expose type key for use in builder without leaking internals.
    let internal keyOf = typeKey
    let internal fieldKeyOf = fieldKey
