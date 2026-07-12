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

    let private buildGetEndpoint (pattern: string) (name: string) (handler: RequestDelegate) : Endpoint =
        let builder = RouteEndpointBuilder(handler, RoutePatternFactory.Parse pattern, 0)
        builder.DisplayName <- name
        builder.Metadata.Add(HttpMethodMetadata [| "GET" |])
        builder.Build()

    let private buildProvenanceEndpoint () : Endpoint =
        buildGetEndpoint
            "/provenance"
            "GET Provenance"
            (RequestDelegate(fun ctx ->
                let store = ctx.RequestServices.GetRequiredService<IProvenanceStore>()
                let config = ctx.RequestServices.GetRequiredService<ProvenanceConfig>()
                ProvenanceEndpoint.handle store config ctx))

    let private buildPerNodeEndpoint () : Endpoint =
        buildGetEndpoint
            "/provenance/{nodeId}"
            "GET Provenance Node"
            (RequestDelegate(fun ctx ->
                let store = ctx.RequestServices.GetRequiredService<IProvenanceStore>()
                let config = ctx.RequestServices.GetRequiredService<ProvenanceConfig>()
                ProvenanceEndpoint.handleNode store config ctx))

    // Adds the provenance middleware and both endpoints to the spec; caller sets Services separately.
    // Kept as a named function (not inlined) to avoid duplicating the addMiddleware body in two CE members.
    let private addProvenanceMiddlewareAndEndpoint (spec: WebHostSpec) : WebHostSpec =
        let addMiddleware (app: IApplicationBuilder) =
            let configured = spec.Middleware app
            configured.UseMiddleware<ProvenanceMiddleware>() |> ignore
            configured

        { spec with
            Endpoints = Array.append spec.Endpoints [| buildProvenanceEndpoint (); buildPerNodeEndpoint () |]
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

            { addProvenanceMiddlewareAndEndpoint spec with
                Services = addServices }

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

            { addProvenanceMiddlewareAndEndpoint spec with
                Services = addServices }
