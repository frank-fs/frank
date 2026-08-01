namespace Frank.Rdf

[<AutoOpen>]
module Rdf =
    /// Resolves a CURIE ("prefix:local") against declared prefixes, or passes an absolute IRI through
    /// unchanged. A declared prefix always takes priority over "is this already a well-formed URI" --
    /// see the comment on the .fs implementation for why the other order is a real bug, not a style choice.
    /// Raises if the text before the colon isn't a declared prefix and the whole string isn't a
    /// well-formed absolute URI either. Raises if there's no colon at all.
    val internal resolveIri: prefixes: (string * string) list -> s: string -> string

    /// Raises if the same prefix name appears more than once with different URIs.
    val internal validatePrefixes: prefixes: (string * string) list -> unit
