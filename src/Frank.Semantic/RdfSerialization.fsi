namespace Frank.Semantic

open VDS.RDF
open Newtonsoft.Json.Linq

/// RDF-to-JSON-LD serialization helpers shared by the content-negotiating middleware
/// packages (Frank.LinkedData, Frank.Provenance, Frank.Validation). Codegen/interop
/// plumbing — not part of Frank.Semantic's public API surface; visible only to those
/// three consumers via InternalsVisibleTo (#392).
module internal RdfSerialization =

    /// RDFS namespace ("http://www.w3.org/2000/01/rdf-schema#") — the single shared binding
    /// for the consumers above, so it is never re-hardcoded as a duplicate string literal.
    [<Literal>]
    val RdfsNamespace: string

    val serializeGraphJsonLd: graph: IGraph -> string

    /// Compact the graph's JSON-LD representation against the given context object.
    /// Returns the compacted JSON-LD as a compact (non-indented) string.
    val compactWithContext: graph: IGraph -> ctx: JObject -> string

    /// Compact the graph's JSON-LD representation against a context built from the given
    /// prefix pairs and @base IRI. Returns the compacted JSON-LD as a string.
    val compactGraphJsonLd: graph: IGraph -> prefixPairs: (string * string) list -> base': string -> string

    val serializeGraphJsonLdWithContext: graph: IGraph -> contextJson: string -> string
