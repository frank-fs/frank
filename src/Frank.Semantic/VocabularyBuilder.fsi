namespace Frank.Semantic

open System

/// Computation expression builder for declaring vocabulary alignments.
/// Evaluates eagerly to a VocabularyRegistry value at CE construction time.
type VocabularyBuilder =
    new: unit -> VocabularyBuilder

    member Yield: unit: unit -> VocabularyRegistry

    /// Register a prefix mapping. Duplicate names with different URIs raise.
    [<CustomOperation("prefix")>]
    member Prefix: state: VocabularyRegistry * name: string * uri: string -> VocabularyRegistry

    /// Declare a prefix as in-scope for IRI resolution. Duplicate entries raise.
    [<CustomOperation("using")>]
    member Using: state: VocabularyRegistry * prefix: string -> VocabularyRegistry

    /// Map a type to an owl:equivalentClass IRI. IRI must use a declared prefix.
    [<CustomOperation("equivalentClass")>]
    member EquivalentClass: state: VocabularyRegistry * type': Type * iri: string -> VocabularyRegistry

    /// Map a type to an rdfs:seeAlso IRI. IRI must use a declared prefix.
    [<CustomOperation("seeAlso")>]
    member SeeAlso: state: VocabularyRegistry * type': Type * iri: string -> VocabularyRegistry

    /// Map a field of a type to an rdfs:seeAlso IRI. IRI must use a declared prefix.
    [<CustomOperation("fieldSeeAlso")>]
    member FieldSeeAlso: state: VocabularyRegistry * type': Type * fieldName: string * iri: string -> VocabularyRegistry

    /// Map a type to a PROV-O class for provenance typing.
    [<CustomOperation("provClass")>]
    member ProvClass: state: VocabularyRegistry * type': Type * provOClass: ProvOClass -> VocabularyRegistry

    /// Add a regex constraint for a field of a type.
    [<CustomOperation("constrainPattern")>]
    member ConstrainPattern:
        state: VocabularyRegistry * type': Type * fieldName: string * pattern: string -> VocabularyRegistry

    /// Deep-union another registry into the current one. Raises on conflicts per field semantics.
    [<CustomOperation("include")>]
    member Include: state: VocabularyRegistry * other: VocabularyRegistry -> VocabularyRegistry

[<AutoOpen>]
module VocabularyBuilderExtensions =

    /// The vocabulary computation expression builder.
    val vocabulary: VocabularyBuilder
