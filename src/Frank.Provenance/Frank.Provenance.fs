namespace Frank.Provenance

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Patterns
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open Microsoft.Extensions.Logging
open Frank.Builder

[<AutoOpen>]
module ProvenanceExtensions =

    let private buildProvenanceEndpoint () : Endpoint =
        let handler =
            RequestDelegate(fun ctx ->
                ProvenanceEndpoint.handle (ctx.RequestServices.GetRequiredService<IProvenanceStore>()) ctx)

        let builder =
            RouteEndpointBuilder(handler, RoutePatternFactory.Parse "/provenance", 0)

        builder.DisplayName <- "GET Provenance"
        builder.Metadata.Add(HttpMethodMetadata [| "GET" |])
        builder.Build()

    let private wireMiddlewareAndEndpoint
        (spec: WebHostSpec)
        (addServices: IServiceCollection -> IServiceCollection)
        : WebHostSpec =
        let addMiddleware (app: IApplicationBuilder) =
            let configured = spec.Middleware app
            configured.UseMiddleware<ProvenanceMiddleware>() |> ignore
            configured

        { spec with
            Endpoints = Array.append spec.Endpoints [| buildProvenanceEndpoint () |]
            Services = addServices
            Middleware = addMiddleware }

    type WebHostBuilder with

        [<CustomOperation("useProvenanceWith")>]
        member _.UseProvenanceWith(spec: WebHostSpec, config: ProvenanceConfig) : WebHostSpec =
            let addServices (services: IServiceCollection) =
                // AddSingleton (last-wins) is intentional: explicit caller config must override auto-loaded defaults.
                services.AddSingleton<ProvenanceConfig>(config) |> ignore

                services.TryAddSingleton<IProvenanceStore>(fun sp ->
                    let logger =
                        sp.GetRequiredService<ILoggerFactory>().CreateLogger("Frank.Provenance")

                    new MailboxProcessorProvenanceStore(config.StoreConfig, logger) :> IProvenanceStore)

                spec.Services services

            wireMiddlewareAndEndpoint spec addServices

        [<CustomOperation("useProvenance")>]
        member _.UseProvenance(spec: WebHostSpec) : WebHostSpec =
            let addServices (services: IServiceCollection) =
                services.TryAddSingleton<ProvenanceConfig>(fun _ ->
                    match
                        GeneratedProvenanceResolver.resolveGeneratedConfig (
                            System.AppDomain.CurrentDomain.GetAssemblies()
                        )
                    with
                    | Ok c -> c
                    | Error m -> invalidOp m)

                services.TryAddSingleton<IProvenanceStore>(fun sp ->
                    let cfg = sp.GetRequiredService<ProvenanceConfig>()

                    let logger =
                        sp.GetRequiredService<ILoggerFactory>().CreateLogger("Frank.Provenance")

                    new MailboxProcessorProvenanceStore(cfg.StoreConfig, logger) :> IProvenanceStore)

                spec.Services services

            wireMiddlewareAndEndpoint spec addServices
