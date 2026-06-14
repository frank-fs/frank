namespace Frank.Discovery

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Frank.Builder

/// Extensions adding static semantic discovery (JSON Home, ALPS, OPTIONS/Allow,
/// Link rel=describedby) to the Frank WebHostBuilder CE. Consumes a DiscoveryConfig
/// (the MSBuild-generated GeneratedDiscovery module) plus endpoint metadata.
[<AutoOpen>]
module DiscoveryExtensions =

    type WebHostBuilder with

        [<CustomOperation("useDiscoveryWith")>]
        member _.UseDiscoveryWith(spec: WebHostSpec, config: DiscoveryConfig) : WebHostSpec =
            let addServices (services: IServiceCollection) =
                services.AddSingleton<DiscoveryConfig>(config) |> ignore
                spec.Services services

            let addMiddleware (app: IApplicationBuilder) =
                let configured = spec.Middleware app
                configured.UseMiddleware<DiscoveryMiddleware.DiscoveryMiddleware>() |> ignore
                configured

            { spec with
                Services = addServices
                Middleware = addMiddleware }

/// Extends ResourceBuilder with a `relation` operation that stamps
/// ResourceRelationMetadata onto every endpoint built by the resource CE block.
/// Frank.Discovery adds this operation; Frank core is unchanged.
[<AutoOpen>]
module ResourceRelationExtensions =

    type ResourceBuilder with

        /// Stamp the vocabulary IRI as ResourceRelationMetadata on every endpoint
        /// produced by this resource block. The discovery middleware reads this at
        /// runtime to build the JSON Home directory — no static HomeResources needed.
        [<CustomOperation("relation")>]
        member _.Relation(spec: ResourceSpec, iri: string) : ResourceSpec =
            if System.String.IsNullOrWhiteSpace iri then
                invalidArg (nameof iri) "relation IRI must not be empty"

            ResourceBuilder.AddMetadata(
                spec,
                fun (b: EndpointBuilder) -> b.Metadata.Add({ Relation = iri }: ResourceRelationMetadata)
            )
