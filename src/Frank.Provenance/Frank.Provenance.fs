namespace Frank.Provenance

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open Microsoft.Extensions.Logging
open Frank.Builder

[<AutoOpen>]
module ProvenanceExtensions =

    let private wireMiddlewareAndEndpoint
        (spec: WebHostSpec)
        (addServices: IServiceCollection -> IServiceCollection)
        : WebHostSpec =
        let addMiddleware (app: IApplicationBuilder) =
            let configured = spec.Middleware app
            configured.UseMiddleware<ProvenanceMiddleware>() |> ignore

            configured.Map(
                PathString "/provenance",
                System.Action<IApplicationBuilder>(fun branch ->
                    branch.Run(
                        RequestDelegate(fun ctx ->
                            ProvenanceEndpoint.handle (ctx.RequestServices.GetRequiredService<IProvenanceStore>()) ctx)
                    ))
            )
            |> ignore

            configured

        { spec with
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
            match
                GeneratedProvenanceResolver.resolveGeneratedConfig (System.AppDomain.CurrentDomain.GetAssemblies())
            with
            | Error msg -> invalidOp msg
            | Ok config ->
                let addServices (services: IServiceCollection) =
                    services.TryAddSingleton<ProvenanceConfig>(config)

                    services.TryAddSingleton<IProvenanceStore>(fun sp ->
                        let logger =
                            sp.GetRequiredService<ILoggerFactory>().CreateLogger("Frank.Provenance")

                        new MailboxProcessorProvenanceStore(config.StoreConfig, logger) :> IProvenanceStore)

                    spec.Services services

                wireMiddlewareAndEndpoint spec addServices
