namespace Frank.LinkedData

open VDS.RDF
open Frank.Builder

[<AutoOpen>]
module LinkedDataExtensions =

    type WebHostBuilder with

        [<CustomOperation("useLinkedDataWith")>]
        member UseLinkedDataWith: spec: WebHostSpec * _config: LinkedDataConfig -> WebHostSpec

        [<CustomOperation("useLinkedData")>]
        member UseLinkedData: spec: WebHostSpec -> WebHostSpec

/// Extends ResourceBuilder with a `linkedDataGraph` operation that stamps
/// LinkedDataConfig onto every endpoint built by the resource CE block.
/// The LinkedDataMiddleware reads this at runtime to serve that endpoint's own
/// graph instead of the global DI-registered graph — no plugBeforeRouting needed.
[<AutoOpen>]
module ResourceLinkedDataExtensions =

    type ResourceBuilder with

        /// Stamp a pre-built RDF graph and JSON-LD context as LinkedDataConfig
        /// metadata on every endpoint produced by this resource block.
        /// LinkedDataMiddleware only serves RDF for endpoints that carry this metadata;
        /// endpoints without it pass through to the downstream handler.
        [<CustomOperation("linkedDataGraph")>]
        member LinkedDataGraph: spec: ResourceSpec * graph: IGraph * jsonLdContext: string -> ResourceSpec

        /// Stamp a full LinkedDataConfig (including an optional GraphFactory) as endpoint metadata.
        /// Use GraphFactory when term IRIs must reflect the actual deployed host (e.g. app-owned vocab).
        [<CustomOperation("linkedDataGraphWith")>]
        member LinkedDataGraphWith: spec: ResourceSpec * ldConfig: LinkedDataConfig -> ResourceSpec
