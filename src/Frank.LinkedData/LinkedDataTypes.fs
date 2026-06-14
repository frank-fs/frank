namespace Frank.LinkedData

open VDS.RDF

/// Configuration consumed by LinkedDataMiddleware.
/// Graph: pre-built RDF graph to serve.
/// JsonLdContext: the EXTERNAL @context JSON string (e.g. {"@context":["https://schema.org"]})
/// referenced verbatim in JSON-LD responses — never extracted from predicate URIs.
type LinkedDataConfig =
    { Graph: IGraph; JsonLdContext: string }
