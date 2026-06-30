namespace Frank.LinkedData

open VDS.RDF

/// Configuration consumed by LinkedDataMiddleware.
/// Graph: pre-built RDF graph to serve.
/// JsonLdContext: the EXTERNAL @context JSON string (e.g. {"@context":["https://schema.org"]})
/// referenced verbatim in JSON-LD responses — never extracted from predicate URIs.
/// RelativeBase: when Some "https://example.org", the middleware strips that scheme+host
/// from IRI references in the serialized output, making app-owned vocab terms root-relative
/// so @base=<request-origin> resolves them to <origin>/path#fragment.
type LinkedDataConfig =
    { Graph: IGraph
      JsonLdContext: string
      RelativeBase: string option }
