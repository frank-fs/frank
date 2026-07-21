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
type LinkedDataConfig =
    { Graph: IGraph
      JsonLdContext: string
      GraphFactory: (HttpContext -> IGraph) option }

    /// Baseline with no per-request graph factory.
    static member Empty: LinkedDataConfig

/// App-wide vocabulary document route (mirrors DiscoveryConfig.HomeRoute/ProfileUri — set
/// once per app, not per-resource). When Some, LinkedDataMiddleware emits a
/// `Link: <route>; rel="describedby"` header on every safe-method (GET/HEAD) response for
/// every endpoint carrying LinkedDataConfig metadata, regardless of negotiated
/// representation — covering codegen-generated resources identically to hand-wired ones,
/// since this is no longer a per-resource opt-in field (#420 expert-review follow-up).
type LinkedDataVocabularyConfig =
    { VocabularyRoute: string option }

    static member None: LinkedDataVocabularyConfig
