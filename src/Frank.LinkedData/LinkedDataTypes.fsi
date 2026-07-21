namespace Frank.LinkedData

open Microsoft.AspNetCore.Http
open VDS.RDF

/// Configuration consumed by LinkedDataMiddleware.
/// Graph: pre-built RDF graph; used when GraphFactory is None.
/// JsonLdContext: the EXTERNAL @context JSON string (e.g. {"@context":["https://schema.org"]})
///   referenced verbatim in JSON-LD responses — never extracted from predicate URIs.
///   Every namespace prefix on the graph whose IRI is NOT under the response's own origin
///   (schema.org, rdf, rdfs, owl, etc.) is served ONLY via this field, not inlined — so it
///   must list a document for every such prefix, or that prefix's compact IRIs are served
///   undefined (see #394).
/// GraphFactory: when Some, called per-request with the HttpContext to build a graph.
///   Provides access to the request origin AND route values (e.g. path id), enabling
///   per-resource instance graphs that host-resolve app-owned term IRIs.
///   Graph is ignored when GraphFactory is Some.
/// VocabularyUri: when Some, LinkedDataMiddleware appends a `Link: <uri>; rel="describedby"`
///   header to every RDF-negotiated response for this endpoint (#420). Enables the two-hop
///   discovery path — an instance's own class-level facts (e.g. rdfs:seeAlso/owl:equivalentClass)
///   live at the referenced vocabulary document, never duplicated into the instance body.
///   A relative reference (e.g. "/vocabulary") is valid per RFC 8288 and is resolved by the
///   client against the response's own request URI.
type LinkedDataConfig =
    { Graph: IGraph
      JsonLdContext: string
      GraphFactory: (HttpContext -> IGraph) option
      VocabularyUri: string option }

    /// Baseline with no vocabulary describedby link and no per-request graph factory.
    static member Empty: LinkedDataConfig
