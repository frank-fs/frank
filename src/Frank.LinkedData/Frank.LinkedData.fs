namespace Frank.LinkedData

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open VDS.RDF
open Frank.Builder

[<AutoOpen>]
module LinkedDataExtensions =

    type WebHostBuilder with

        [<CustomOperation("useLinkedDataWith")>]
        member _.UseLinkedDataWith(spec: WebHostSpec, config: LinkedDataConfig) : WebHostSpec =
            let addServices (services: IServiceCollection) =
                services.AddSingleton<LinkedDataConfig>(config) |> ignore
                spec.Services services

            let addMiddleware (app: IApplicationBuilder) =
                let configured = spec.Middleware app
                configured.UseMiddleware<LinkedDataMiddleware>() |> ignore
                configured

            { spec with
                Services = addServices
                Middleware = addMiddleware }

        [<CustomOperation("useLinkedData")>]
        member this.UseLinkedData(spec: WebHostSpec) : WebHostSpec =
            let assemblies = System.AppDomain.CurrentDomain.GetAssemblies()

            match GeneratedLinkedDataResolver.resolveGeneratedConfig assemblies with
            | Ok config ->
                let addServices (services: IServiceCollection) =
                    services.TryAddSingleton<LinkedDataConfig>(config)
                    spec.Services services

                let addMiddleware (app: IApplicationBuilder) =
                    let configured = spec.Middleware app
                    configured.UseMiddleware<LinkedDataMiddleware>() |> ignore
                    configured

                { spec with
                    Services = addServices
                    Middleware = addMiddleware }
            | Error msg -> invalidOp msg

/// Extends ResourceBuilder with a `linkedDataGraph` operation that stamps
/// LinkedDataConfig onto every endpoint built by the resource CE block.
/// The LinkedDataMiddleware reads this at runtime to serve that endpoint's own
/// graph instead of the global DI-registered graph — no plugBeforeRouting needed.
[<AutoOpen>]
module ResourceLinkedDataExtensions =

    type ResourceBuilder with

        /// Stamp a per-resource RDF graph and JSON-LD context as LinkedDataConfig
        /// metadata on every endpoint produced by this resource block.
        /// LinkedDataMiddleware reads this at request time and serves the endpoint's
        /// own graph; endpoints without this metadata fall back to the global config.
        [<CustomOperation("linkedDataGraph")>]
        member _.LinkedDataGraph(spec: ResourceSpec, graph: IGraph, jsonLdContext: string) : ResourceSpec =
            if isNull (box graph) then
                invalidArg (nameof graph) "graph must not be null"

            if System.String.IsNullOrWhiteSpace jsonLdContext then
                invalidArg (nameof jsonLdContext) "jsonLdContext must not be null or whitespace"

            ResourceBuilder.AddMetadata(
                spec,
                fun (b: EndpointBuilder) ->
                    b.Metadata.Add({ Graph = graph; JsonLdContext = jsonLdContext }: LinkedDataConfig)
            )
