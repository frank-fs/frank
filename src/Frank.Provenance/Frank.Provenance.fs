namespace Frank.Provenance

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Patterns
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open Microsoft.Extensions.Logging
open Frank
open Frank.Builder

[<AutoOpen>]
module ProvenanceExtensions =

    let private buildGetEndpoint
        (pattern: string)
        (name: string)
        (handler: RequestDelegate)
        (extraMetadata: obj list)
        : Endpoint =
        let builder = RouteEndpointBuilder(handler, RoutePatternFactory.Parse pattern, 0)
        builder.DisplayName <- name
        builder.Metadata.Add(HttpMethodMetadata [| "GET" |])

        for m in extraMetadata do
            builder.Metadata.Add m

        builder.Build()

    /// #426: the ETagMetadata compute closure re-invokes ProvenanceEndpoint's SAME
    /// resolution functions (resolveLineageGraph, via computeLineageETag) the handler uses
    /// on its 200 path — no re-derivation of resource/origin resolution in a second copy.
    let private buildProvenanceEndpoint () : Endpoint =
        let etagMetadata =
            ETagMetadata(
                (fun (ctx: HttpContext) -> ctx.Request.Query.["resource"].ToString()),
                (fun (etagContext: ETagContext) ->
                    let store =
                        etagContext.HttpContext.RequestServices.GetRequiredService<IProvenanceStore>()

                    ProvenanceEndpoint.computeLineageETag store etagContext)
            )

        buildGetEndpoint
            "/provenance"
            "GET Provenance"
            (RequestDelegate(fun ctx ->
                let store = ctx.RequestServices.GetRequiredService<IProvenanceStore>()
                let config = ctx.RequestServices.GetRequiredService<ProvenanceConfig>()
                ProvenanceEndpoint.handle store config ctx))
            [ box etagMetadata ]

    /// #426: the ETagMetadata compute closure re-invokes ProvenanceEndpoint's SAME
    /// per-node dispatch (resolveNodeGraph, via computeNodeETag) that handleNode's 200
    /// path uses — the entity-/activity dispatch and index/lineage checks live in exactly
    /// one place, so the middleware's 304 decision can never drift from what the handler
    /// would actually serve.
    let private buildPerNodeEndpoint () : Endpoint =
        let etagMetadata =
            ETagMetadata(
                ProvenanceEndpoint.resolveNodeId,
                (fun (etagContext: ETagContext) ->
                    let store =
                        etagContext.HttpContext.RequestServices.GetRequiredService<IProvenanceStore>()

                    ProvenanceEndpoint.computeNodeETag store etagContext)
            )

        buildGetEndpoint
            "/provenance/{nodeId}"
            "GET Provenance Node"
            (RequestDelegate(fun ctx ->
                let store = ctx.RequestServices.GetRequiredService<IProvenanceStore>()
                let config = ctx.RequestServices.GetRequiredService<ProvenanceConfig>()
                ProvenanceEndpoint.handleNode store config ctx))
            [ box etagMetadata ]

    // Adds the provenance middleware, conditional-request middleware and both endpoints to
    // the spec, and centralizes the DI concerns every caller needs: the caller-supplied
    // `configureServices` (config/store registration, which differs between useProvenanceWith
    // and useProvenance) plus the ETagCache registration ConditionalRequestMiddleware needs --
    // shared here instead of repeated at both call sites (#426 follow-up).
    let private addProvenanceMiddlewareAndEndpoint
        (configureServices: IServiceCollection -> unit)
        (spec: WebHostSpec)
        : WebHostSpec =
        let addMiddleware (app: IApplicationBuilder) =
            let configured = spec.Middleware app
            // R10 (#426): useConditionalRequests is registered INNER to (after)
            // ProvenanceMiddleware, so ProvenanceMiddleware's OnStarting-registered
            // has_provenance Link header survives a 304 short-circuit -- see
            // Frank.useConditionalRequests's doc comment for the ordering contract.
            configured.UseMiddleware<ProvenanceMiddleware>() |> useConditionalRequests

        let addServices (services: IServiceCollection) =
            configureServices services
            // ConditionalRequestMiddleware (wired via useConditionalRequests above) needs
            // ETagCache resolvable from DI.
            services.AddETagCache() |> ignore
            spec.Services services

        { spec with
            Endpoints = Array.append spec.Endpoints [| buildProvenanceEndpoint (); buildPerNodeEndpoint () |]
            Middleware = addMiddleware
            Services = addServices }

    type WebHostBuilder with

        [<CustomOperation("useProvenanceWith")>]
        member _.UseProvenanceWith(spec: WebHostSpec, config: ProvenanceConfig) : WebHostSpec =
            let configureServices (services: IServiceCollection) =
                // AddSingleton (last-wins) is intentional: explicit caller config must override auto-loaded defaults.
                services.AddSingleton<ProvenanceConfig>(config) |> ignore

                services.TryAddSingleton<IProvenanceStore>(fun sp ->
                    let logger =
                        sp.GetRequiredService<ILoggerFactory>().CreateLogger("Frank.Provenance")

                    new MailboxProcessorProvenanceStore(config.StoreConfig, logger) :> IProvenanceStore)
                |> ignore

            spec |> addProvenanceMiddlewareAndEndpoint configureServices

        [<CustomOperation("useProvenance")>]
        member _.UseProvenance(spec: WebHostSpec) : WebHostSpec =
            let configureServices (services: IServiceCollection) =
                services.TryAddSingleton<ProvenanceConfig>(fun _ ->
                    match
                        GeneratedProvenanceResolver.resolveGeneratedConfig (
                            System.AppDomain.CurrentDomain.GetAssemblies()
                        )
                    with
                    | Ok c -> c
                    | Error m -> invalidOp m)
                |> ignore

                services.TryAddSingleton<IProvenanceStore>(fun sp ->
                    let cfg = sp.GetRequiredService<ProvenanceConfig>()

                    let logger =
                        sp.GetRequiredService<ILoggerFactory>().CreateLogger("Frank.Provenance")

                    new MailboxProcessorProvenanceStore(cfg.StoreConfig, logger) :> IProvenanceStore)
                |> ignore

            spec |> addProvenanceMiddlewareAndEndpoint configureServices
