namespace Frank.LinkedData

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Routing
open VDS.RDF
open Frank.Builder

[<AutoOpen>]
module LinkedDataExtensions =

    type WebHostBuilder with

        [<CustomOperation("useLinkedDataWith")>]
        member _.UseLinkedDataWith(spec: WebHostSpec, _config: LinkedDataConfig) : WebHostSpec =
            let addMiddleware (app: IApplicationBuilder) =
                let configured = spec.Middleware app
                configured.UseMiddleware<LinkedDataMiddleware>() |> ignore
                configured

            { spec with Middleware = addMiddleware }

        [<CustomOperation("useLinkedData")>]
        member _.UseLinkedData(spec: WebHostSpec) : WebHostSpec =
            let addMiddleware (app: IApplicationBuilder) =
                let configured = spec.Middleware app
                configured.UseMiddleware<LinkedDataMiddleware>() |> ignore
                configured

            { spec with Middleware = addMiddleware }

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
        member _.LinkedDataGraph(spec: ResourceSpec, graph: IGraph, jsonLdContext: string) : ResourceSpec =
            if isNull (box graph) then
                invalidArg (nameof graph) "graph must not be null"

            if System.String.IsNullOrWhiteSpace jsonLdContext then
                invalidArg (nameof jsonLdContext) "jsonLdContext must not be null or whitespace"

            ResourceBuilder.AddMetadata(
                spec,
                fun (b: EndpointBuilder) ->
                    b.Metadata.Add(
                        { Graph = graph
                          JsonLdContext = jsonLdContext
                          GraphFactory = None }
                        : LinkedDataConfig
                    )
            )

        /// Stamp a full LinkedDataConfig (including an optional GraphFactory) as endpoint metadata.
        /// Use GraphFactory when term IRIs must reflect the actual deployed host (e.g. app-owned vocab).
        [<CustomOperation("linkedDataGraphWith")>]
        member _.LinkedDataGraphWith(spec: ResourceSpec, ldConfig: LinkedDataConfig) : ResourceSpec =
            if ldConfig.GraphFactory.IsNone && isNull (box ldConfig.Graph) then
                invalidArg (nameof ldConfig) "ldConfig.Graph must not be null when GraphFactory is None"

            ResourceBuilder.AddMetadata(spec, fun (b: EndpointBuilder) -> b.Metadata.Add(ldConfig: LinkedDataConfig))
