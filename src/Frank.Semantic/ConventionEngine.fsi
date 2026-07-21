namespace Frank.Semantic

open VDS.RDF

/// Extracted class/property/individual local names from a vocabulary IGraph.
/// Keys are lowercase local names; values are absolute IRI strings.
type VocabTerms =
    { Classes: Map<string, string>
      Properties: Map<string, string>
      Individuals: Map<string, string> }

/// Absolute IRI sets per term category, with NO local-name deduplication.
/// Used for term-existence checking in the accept oracle: identity is the absolute
/// IRI, never the local name. Two properties sharing a local name across different
/// namespaces (e.g. schema:identifier and dct:identifier) are both valid and must
/// both appear here — unlike VocabTerms which drops ambiguous local names to prevent
/// wrong-namespace convention matches.
type VocabTermIris =
    { ClassIris: Set<string>
      PropertyIris: Set<string>
      IndividualIris: Set<string> }

/// Diagnostic emitted when ConventionEngine.applyExplicitClass collapses a type's own
/// ClassIri onto a declared equivalentClass target because the type had no independent
/// convention match of its own. Without this notice the collapse is silent: an author who
/// declared `equivalentClass typeof<Foo> "schema:Bar"` expecting Foo to keep its own class
/// identity AND gain a genuine owl:equivalentClass link to Bar instead gets neither — Foo's
/// ClassIri becomes Bar's IRI outright, and the resulting equivalentClass field then
/// collapses to a no-op (see #425, ResolvedModel.buildResolvedResource).
type EquivalentClassNotice =
    { FSharpType: string
      ExplicitIri: string }

module ConventionEngine =

    /// Jaro-Winkler similarity between two strings. Result in [0.0, 1.0].
    /// Null inputs treated as empty strings.
    val jaroWinkler: s: string -> t: string -> float

    /// PascalCase type/field name → lowercase tokens, with known suffixes stripped.
    val normalizeTokens: name: string -> string list

    /// Join normalized tokens with a space (used for full-string JW comparison of attr values).
    val canonicalName: name: string -> string

    /// Extract class, property, and individual local names from a vocabulary IGraph.
    /// Recognized typings:
    ///   Classes    — rdfs:Class, schema:Class, owl:Class, rdfs:Datatype
    ///   Properties — rdf:Property, schema:Property, owl:ObjectProperty, owl:DatatypeProperty
    ///   Individuals — owl:NamedIndividual, skos:Concept, or any subject S where
    ///                 S rdf:type C and C is a known class (enumeration member pattern).
    ///                 A subject already in Classes or Properties is never re-bucketed here.
    /// Keys are lowercase local names; values are absolute IRI strings.
    /// A local name that maps to more than one distinct IRI is excluded (ambiguous).
    val extractVocabTerms: graph: IGraph -> VocabTerms

    /// Extract absolute IRI sets per term category from a vocabulary IGraph.
    /// Unlike extractVocabTerms, there is NO local-name deduplication: both
    /// http://a/identifier and http://b/identifier are kept even though they share
    /// the local name "identifier". Term-existence identity is the absolute IRI.
    val extractTermIris: graph: IGraph -> VocabTermIris

    /// Score a TypeInfo against in-scope vocabulary terms and emit a candidate Mapping,
    /// plus an EquivalentClassNotice when applyExplicitClass silently collapsed the type's
    /// ClassIri onto a declared equivalentClass target (see EquivalentClassNotice).
    /// Pure: takes pre-extracted VocabTerms and VocabularyRegistry as data — no I/O.
    val scoreDetailed:
        terms: VocabTerms ->
        registry: VocabularyRegistry ->
        typeInfo: TypeInfo ->
            Mapping * EquivalentClassNotice option

    /// Score a TypeInfo against in-scope vocabulary terms and emit a candidate Mapping.
    /// Thin wrapper over scoreDetailed for callers that don't need the notice channel.
    /// Pure: takes pre-extracted VocabTerms and VocabularyRegistry as data — no I/O.
    val score: terms: VocabTerms -> registry: VocabularyRegistry -> typeInfo: TypeInfo -> Mapping
