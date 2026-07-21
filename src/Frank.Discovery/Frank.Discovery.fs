namespace Frank.Discovery

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Frank.Builder

/// Validates href-template variables in the JSON Home document by running the existing
/// serializer check before the app starts serving. Reuses homeResourcesFromEndpoints and
/// JsonHomeSerializer.serialize — the existing invalidOp fires on an unresolved variable,
/// naming it. Register via DI as IStartupValidator (done automatically by useDiscoveryWith).
type HrefVarsValidator(config: DiscoveryConfig) =
    interface IStartupValidator with
        member _.Validate(ds) =
            DiscoveryMiddleware.homeResourcesFromEndpoints
                config.ResourceHrefVars
                (DiscoveryMiddleware.classIriHrefMap config.AlpsDescriptors)
                ds
            |> JsonHomeSerializer.serialize
            |> ignore

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
                services.AddSingleton<IStartupValidator>(HrefVarsValidator(config)) |> ignore
                // #411: DiscoveryMiddleware's ALPS Type correlation reads the narrow
                // ResourceEndpointDataSource that WebHostBuilder.Run itself registers as a
                // DI singleton at Run()-time — nothing to register here. Frank.Discovery has
                // no ApiExplorer/Microsoft.AspNetCore.OpenApi dependency.
                spec.Services services

            let addMiddleware (app: IApplicationBuilder) =
                let configured = spec.Middleware app
                configured.UseMiddleware<DiscoveryMiddleware.DiscoveryMiddleware>() |> ignore
                configured

            { spec with
                Services = addServices
                Middleware = addMiddleware }

        [<CustomOperation("useDiscovery")>]
        member this.UseDiscovery(spec: WebHostSpec) : WebHostSpec =
            let assemblies = System.AppDomain.CurrentDomain.GetAssemblies()

            match GeneratedDiscoveryResolver.resolveGeneratedConfig assemblies with
            | Ok config -> this.UseDiscoveryWith(spec, config)
            | Error msg -> invalidOp msg

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
