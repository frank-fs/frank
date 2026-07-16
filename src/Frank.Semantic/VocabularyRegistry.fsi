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

    val empty: VocabularyRegistry

    /// Resolve a prefixed IRI string (e.g. "schema:Order") to a Uri using known prefixes.
    /// Raises InvalidOperationException if the prefix is not declared.
    val resolveIri: prefixes: Map<string, Uri> -> iri: string -> Uri

    /// Deep-union two registries. Raises on conflicting keys per field semantics.
    val include': base': VocabularyRegistry -> other: VocabularyRegistry -> VocabularyRegistry

    /// Total version of resolveIri: returns Ok(Some uri) for CURIE inputs (prefix:local),
    /// Ok None for None, Error for unknown prefix or non-CURIE input.
    /// Only CURIE form is accepted; bare names without a colon are rejected with Error.
    val tryResolveIri: prefixes: Map<string, Uri> -> iri: string option -> Result<Uri option, string>

    /// Look up EquivalentClass by Type.
    val tryFindEquivalentClass: t: Type -> r: VocabularyRegistry -> Uri option

    /// Look up SeeAlso by Type.
    val tryFindSeeAlso: t: Type -> r: VocabularyRegistry -> Uri list option

    /// Look up FieldSeeAlso by Type and field name.
    val tryFindFieldSeeAlso: t: Type -> fieldName: string -> r: VocabularyRegistry -> Uri list option

    /// Look up ProvClass by Type.
    val tryFindProvClass: t: Type -> r: VocabularyRegistry -> ProvOClass option

    /// Look up ConstraintPattern by Type and field name.
    val tryFindConstraintPattern: t: Type -> fieldName: string -> r: VocabularyRegistry -> string option

    /// Expose type key for use in builder without leaking internals.
    val internal keyOf: Type -> string
    val internal fieldKeyOf: Type -> string -> (string * string)
