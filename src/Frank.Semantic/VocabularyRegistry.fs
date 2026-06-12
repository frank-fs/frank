namespace Frank.Semantic

open System
open System.Collections.Generic
open System.Collections.ObjectModel

/// PROV-O class classifications for type-level provenance annotations.
type ProvOClass =
    | Activity
    | Entity
    | Agent

/// Resolved record of all semantic alignments declared in a vocabulary CE.
[<NoEquality; NoComparison>]
type VocabularyRegistry =
    { Prefixes: Map<string, Uri>
      Using: Set<string>
      EquivalentClasses: ReadOnlyDictionary<Type, Uri>
      SeeAlso: ReadOnlyDictionary<Type, Uri list>
      FieldSeeAlso: ReadOnlyDictionary<(Type * string), Uri list>
      ProvClasses: ReadOnlyDictionary<Type, ProvOClass>
      ConstraintPatterns: ReadOnlyDictionary<(Type * string), string> }
