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

/// A convention-matching outcome a vocabulary author would not expect from reading
/// their own declarations, surfaced explicitly instead of only implicitly via
/// Status/Confidence.
type ConventionDiagnostic =
    /// ConventionEngine.applyExplicitClass collapsed a type's own ClassIri onto a
    /// declared equivalentClass target because the type had no independently CONFIRMED
    /// convention match of its own (Unresolved, or only a fuzzy/Proposed candidate —
    /// a Proposed guess was never asserted as the type's identity, so it doesn't count as
    /// one). Without this, the collapse is silent: an author who declared
    /// `equivalentClass typeof<Foo> "schema:Bar"` expecting Foo to keep its own class
    /// identity AND gain a genuine owl:equivalentClass link to Bar instead gets neither —
    /// Foo's ClassIri becomes Bar's IRI outright, and the resulting equivalentClass field
    /// then collapses to a no-op (see #425, ResolvedModel.buildResolvedResource).
    | EquivalentClassCollapse of FSharpType: string * ExplicitIri: string
    /// ConventionEngine.buildTermMap excluded a local name from VocabTerms because it maps
    /// to more than one distinct absolute IRI (e.g. schema:identifier and dct:identifier
    /// both normalize to "identifier"). Without this, the drop is silent: every type that
    /// would have convention-matched against that name instead degrades to Unresolved with
    /// no record of why (see buildTermMap).
    | AmbiguousLocalNameDropped of Category: string * LocalName: string * Iris: string list

module ConventionEngine =

    /// Jaro-Winkler similarity between two strings. Result in [0.0, 1.0].
    /// Null inputs treated as empty strings.
    val jaroWinkler: s: string -> t: string -> float

    /// PascalCase type/field name → lowercase tokens, with known suffixes stripped.
    val normalizeTokens: name: string -> string list

    /// Join normalized tokens with a space (used for full-string JW comparison of attr values).
    val canonicalName: name: string -> string

    /// Extract class, property, and individual local names from a vocabulary IGraph,
    /// plus an AmbiguousLocalNameDropped diagnostic for each local name excluded because
    /// it maps to more than one distinct IRI.
    /// Recognized typings:
    ///   Classes    — rdfs:Class, schema:Class, owl:Class, rdfs:Datatype
    ///   Properties — rdf:Property, schema:Property, owl:ObjectProperty, owl:DatatypeProperty
    ///   Individuals — owl:NamedIndividual, skos:Concept, or any subject S where
    ///                 S rdf:type C and C is a known class (enumeration member pattern).
    ///                 A subject already in Classes or Properties is never re-bucketed here.
    /// Keys are lowercase local names; values are absolute IRI strings.
    val extractVocabTermsDetailed: graph: IGraph -> VocabTerms * ConventionDiagnostic list

    /// Extract class, property, and individual local names from a vocabulary IGraph.
    /// Thin wrapper over extractVocabTermsDetailed for callers that don't need the
    /// ambiguous-local-name diagnostic channel.
    /// A local name that maps to more than one distinct IRI is excluded (ambiguous).
    val extractVocabTerms: graph: IGraph -> VocabTerms

    /// Extract absolute IRI sets per term category from a vocabulary IGraph.
    /// Unlike extractVocabTerms, there is NO local-name deduplication: both
    /// http://a/identifier and http://b/identifier are kept even though they share
    /// the local name "identifier". Term-existence identity is the absolute IRI.
    val extractTermIris: graph: IGraph -> VocabTermIris

    /// Score a TypeInfo against in-scope vocabulary terms and emit a candidate Mapping,
    /// plus any ConventionDiagnostics raised while scoring — currently only
    /// EquivalentClassCollapse, when applyExplicitClass silently collapsed the type's
    /// ClassIri onto a declared equivalentClass target (see ConventionDiagnostic).
    /// Pure: takes pre-extracted VocabTerms and VocabularyRegistry as data — no I/O.
    val scoreDetailed:
        terms: VocabTerms -> registry: VocabularyRegistry -> typeInfo: TypeInfo -> Mapping * ConventionDiagnostic list

    /// Score a TypeInfo against in-scope vocabulary terms and emit a candidate Mapping.
    /// Thin wrapper over scoreDetailed for callers that don't need the diagnostic channel.
    /// Pure: takes pre-extracted VocabTerms and VocabularyRegistry as data — no I/O.
    val score: terms: VocabTerms -> registry: VocabularyRegistry -> typeInfo: TypeInfo -> Mapping
