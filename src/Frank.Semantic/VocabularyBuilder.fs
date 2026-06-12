namespace Frank.Semantic

open System
open System.Collections.Generic
open System.Collections.ObjectModel

module private IriParser =
    /// Resolves a CURIE string ("prefix:local") to a URI using the declared prefixes.
    /// Raises InvalidOperationException if the prefix is not declared.
    let resolve (prefixes: Map<string, Uri>) (curie: string) : Uri =
        let idx = curie.IndexOf(':')

        if idx <= 0 then
            invalidOp $"'{curie}' is not a valid CURIE — expected 'prefix:local' form"

        let prefix = curie.Substring(0, idx)
        let local = curie.Substring(idx + 1)

        match prefixes |> Map.tryFind prefix with
        | None -> invalidOp $"Prefix '{prefix}' is not declared in this vocabulary block"
        | Some baseUri -> Uri(baseUri.ToString() + local)

/// Mutable accumulator used internally during CE evaluation.
[<NoEquality; NoComparison>]
type VocabularySpec =
    { mutable Prefixes: Map<string, Uri>
      mutable Using: Set<string>
      mutable EquivalentClasses: Dictionary<Type, Uri>
      mutable SeeAlso: Dictionary<Type, Uri list>
      mutable FieldSeeAlso: Dictionary<(Type * string), Uri list>
      mutable ProvClasses: Dictionary<Type, ProvOClass>
      mutable ConstraintPatterns: Dictionary<(Type * string), string> }

    static member Empty =
        { Prefixes = Map.empty
          Using = Set.empty
          EquivalentClasses = Dictionary<Type, Uri>()
          SeeAlso = Dictionary<Type, Uri list>()
          FieldSeeAlso = Dictionary<(Type * string), Uri list>()
          ProvClasses = Dictionary<Type, ProvOClass>()
          ConstraintPatterns = Dictionary<(Type * string), string>() }

    member spec.ToRegistry() : VocabularyRegistry =
        { Prefixes = spec.Prefixes
          Using = spec.Using
          EquivalentClasses = ReadOnlyDictionary<Type, Uri>(spec.EquivalentClasses)
          SeeAlso = ReadOnlyDictionary<Type, Uri list>(spec.SeeAlso)
          FieldSeeAlso = ReadOnlyDictionary<(Type * string), Uri list>(spec.FieldSeeAlso)
          ProvClasses = ReadOnlyDictionary<Type, ProvOClass>(spec.ProvClasses)
          ConstraintPatterns = ReadOnlyDictionary<(Type * string), string>(spec.ConstraintPatterns) }

    member spec.Clone() =
        { Prefixes = spec.Prefixes
          Using = spec.Using
          EquivalentClasses = Dictionary<Type, Uri>(spec.EquivalentClasses)
          SeeAlso = Dictionary<Type, Uri list>(spec.SeeAlso)
          FieldSeeAlso = Dictionary<(Type * string), Uri list>(spec.FieldSeeAlso)
          ProvClasses = Dictionary<Type, ProvOClass>(spec.ProvClasses)
          ConstraintPatterns = Dictionary<(Type * string), string>(spec.ConstraintPatterns) }

[<Sealed>]
type VocabularyBuilder() =

    member _.Yield(_) = VocabularySpec.Empty

    member _.Run(spec: VocabularySpec) = spec.ToRegistry()

    [<CustomOperation("prefix")>]
    member _.Prefix(spec: VocabularySpec, name: string, iri: string) =
        if String.IsNullOrWhiteSpace name then
            invalidArg (nameof name) "Prefix name must not be empty"

        let next = spec.Clone()
        next.Prefixes <- next.Prefixes |> Map.add name (Uri(iri))
        next

    [<CustomOperation("using")>]
    member _.Using(spec: VocabularySpec, name: string) =
        let next = spec.Clone()
        next.Using <- next.Using |> Set.add name
        next

    [<CustomOperation("equivalentClass")>]
    member _.EquivalentClass(spec: VocabularySpec, t: Type, curie: string) =
        let uri = IriParser.resolve spec.Prefixes curie
        let next = spec.Clone()
        next.EquivalentClasses.[t] <- uri
        next

    [<CustomOperation("seeAlso")>]
    member _.SeeAlso(spec: VocabularySpec, t: Type, curie: string) =
        let uri = IriParser.resolve spec.Prefixes curie
        let next = spec.Clone()
        let existing = if next.SeeAlso.ContainsKey(t) then next.SeeAlso.[t] else []
        next.SeeAlso.[t] <- existing @ [ uri ]
        next

    [<CustomOperation("fieldSeeAlso")>]
    member _.FieldSeeAlso(spec: VocabularySpec, t: Type, field: string, curie: string) =
        let uri = IriParser.resolve spec.Prefixes curie
        let key = (t, field)
        let next = spec.Clone()

        let existing =
            if next.FieldSeeAlso.ContainsKey(key) then
                next.FieldSeeAlso.[key]
            else
                []

        next.FieldSeeAlso.[key] <- existing @ [ uri ]
        next

    [<CustomOperation("provClass")>]
    member _.ProvClass(spec: VocabularySpec, t: Type, prov: ProvOClass) =
        let next = spec.Clone()
        next.ProvClasses.[t] <- prov
        next

    [<CustomOperation("constrainPattern")>]
    member _.ConstrainPattern(spec: VocabularySpec, t: Type, field: string, pattern: string) =
        let next = spec.Clone()
        next.ConstraintPatterns.[(t, field)] <- pattern
        next

    [<CustomOperation("extend")>]
    member _.Include(spec: VocabularySpec, other: VocabularyRegistry) =
        let mergePrefixes acc (name, uri) =
            match acc |> Map.tryFind name with
            | Some existing when existing <> uri ->
                invalidOp $"Prefix conflict for '{name}': existing '{existing}' vs included '{uri}'"
            | _ -> acc |> Map.add name uri

        let mergedPrefixes =
            other.Prefixes |> Map.toSeq |> Seq.fold mergePrefixes spec.Prefixes

        let next = spec.Clone()
        next.Prefixes <- mergedPrefixes
        next.Using <- spec.Using + other.Using

        for KeyValue(t, uri) in other.EquivalentClasses do
            next.EquivalentClasses.[t] <- uri

        for KeyValue(t, uris) in other.SeeAlso do
            let existing = if next.SeeAlso.ContainsKey(t) then next.SeeAlso.[t] else []
            next.SeeAlso.[t] <- existing @ uris

        for KeyValue(key, uris) in other.FieldSeeAlso do
            let existing =
                if next.FieldSeeAlso.ContainsKey(key) then
                    next.FieldSeeAlso.[key]
                else
                    []

            next.FieldSeeAlso.[key] <- existing @ uris

        for KeyValue(t, prov) in other.ProvClasses do
            next.ProvClasses.[t] <- prov

        for KeyValue(key, pattern) in other.ConstraintPatterns do
            next.ConstraintPatterns.[key] <- pattern

        next

[<AutoOpen>]
module VocabularyBuilderModule =
    let vocabulary = VocabularyBuilder()
