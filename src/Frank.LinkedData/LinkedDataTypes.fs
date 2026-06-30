namespace Frank.LinkedData

open VDS.RDF

/// Configuration consumed by LinkedDataMiddleware.
/// Graph: pre-built RDF graph; used when GraphFactory is None.
/// JsonLdContext: the EXTERNAL @context JSON string (e.g. {"@context":["https://schema.org"]})
///   referenced verbatim in JSON-LD responses — never extracted from predicate URIs.
/// GraphFactory: when Some, called per-request with the request origin (scheme://host)
///   to build an origin-resolved IGraph. Set this for app-owned vocabularies whose
///   term IRIs must reflect the actual deployed host rather than a hardcoded placeholder.
///   Graph is ignored when GraphFactory is Some.
type LinkedDataConfig =
    { Graph: IGraph
      JsonLdContext: string
      GraphFactory: (string -> IGraph) option }
