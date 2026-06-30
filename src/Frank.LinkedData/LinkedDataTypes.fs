namespace Frank.LinkedData

open Microsoft.AspNetCore.Http
open VDS.RDF

/// Configuration consumed by LinkedDataMiddleware.
/// Graph: pre-built RDF graph; used when GraphFactory is None.
/// JsonLdContext: the EXTERNAL @context JSON string (e.g. {"@context":["https://schema.org"]})
///   referenced verbatim in JSON-LD responses — never extracted from predicate URIs.
/// GraphFactory: when Some, called per-request with the HttpContext to build a graph.
///   Provides access to the request origin AND route values (e.g. path id), enabling
///   per-resource instance graphs that host-resolve app-owned term IRIs.
///   Graph is ignored when GraphFactory is Some.
type LinkedDataConfig =
    { Graph: IGraph
      JsonLdContext: string
      GraphFactory: (HttpContext -> IGraph) option }
