namespace Frank.LinkedData

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open VDS.RDF
open Frank.Builder

[<AutoOpen>]
module LinkedDataExtensions =

    /// Registers the default (no describedby route) LinkedDataVocabularyConfig singleton —
    /// required so LinkedDataMiddleware's constructor resolves from DI even when
    /// useLinkedDataVocabulary is never called. `useLinkedDataVocabulary` (below) overrides
    /// this by registering a second singleton after this one; DI resolves the LAST
    /// registration for a single-instance request, so ordering in the CE block matters —
    /// mirrors DiscoveryConfig's DI-singleton pattern (Frank.Discovery.fs's addServices).
    let private addDefaultVocabularyConfig (services: IServiceCollection) =
        services.AddSingleton<LinkedDataVocabularyConfig>(LinkedDataVocabularyConfig.None)
        |> ignore

        services

    type WebHostBuilder with

        [<CustomOperation("useLinkedDataWith")>]
        member _.UseLinkedDataWith(spec: WebHostSpec, _config: LinkedDataConfig) : WebHostSpec =
            let addServices (services: IServiceCollection) =
                spec.Services services |> addDefaultVocabularyConfig

            let addMiddleware (app: IApplicationBuilder) =
                let configured = spec.Middleware app
                configured.UseMiddleware<LinkedDataMiddleware>() |> ignore
                configured

            { spec with
                Services = addServices
                Middleware = addMiddleware }

        [<CustomOperation("useLinkedData")>]
        member _.UseLinkedData(spec: WebHostSpec) : WebHostSpec =
            let addServices (services: IServiceCollection) =
                spec.Services services |> addDefaultVocabularyConfig

            let addMiddleware (app: IApplicationBuilder) =
                let configured = spec.Middleware app
                configured.UseMiddleware<LinkedDataMiddleware>() |> ignore
                configured

            { spec with
                Services = addServices
                Middleware = addMiddleware }

        /// App-wide vocabulary document route (mirrors useDiscoveryWith's HomeRoute/ProfileUri
        /// singleton pattern) — set once per app, applies to every endpoint carrying
        /// LinkedDataConfig metadata. Call AFTER useLinkedData/useLinkedDataWith in the CE
        /// block so this registration is the one DI resolves (#420 expert-review follow-up).
        [<CustomOperation("useLinkedDataVocabulary")>]
        member _.UseLinkedDataVocabulary(spec: WebHostSpec, vocabularyRoute: string) : WebHostSpec =
            if System.String.IsNullOrWhiteSpace vocabularyRoute then
                invalidArg (nameof vocabularyRoute) "vocabularyRoute must not be null or whitespace"

            let addServices (services: IServiceCollection) =
                let configured = spec.Services services

                configured.AddSingleton<LinkedDataVocabularyConfig>({ VocabularyRoute = Some vocabularyRoute })
                |> ignore

                configured

            { spec with Services = addServices }

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
                        { LinkedDataConfig.Empty with
                            Graph = graph
                            JsonLdContext = jsonLdContext }
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
