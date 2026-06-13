namespace Frank.Provenance

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Frank.Builder
open Frank.Provenance.ProvenanceStore
open Frank.Provenance.ProvenanceMiddleware

[<AutoOpen>]
module ProvenanceExtensions =

    type WebHostBuilder with

        [<CustomOperation("useProvenanceWith")>]
        member _.UseProvenanceWith(spec: WebHostSpec, config: ProvenanceConfig) : WebHostSpec =
            let addServices (services: IServiceCollection) =
                services.AddSingleton<ProvenanceConfig>(config) |> ignore
                services.AddSingleton<ProvenanceStore>(config.Store) |> ignore
                spec.Services(services)

            let addMiddleware (app: IApplicationBuilder) =
                let configured = spec.Middleware(app)

                match app with
                | :? IEndpointRouteBuilder as router ->
                    router.MapGet(
                        config.TraceEndpointBase + "/{activityId}",
                        fun (ctx: HttpContext) ->
                            task {
                                let activityId = ctx.Request.RouteValues.["activityId"] :?> string
                                let! activity = config.Store.Get(activityId)

                                match activity with
                                | None -> ctx.Response.StatusCode <- 404
                                | Some a ->
                                    ctx.Response.ContentType <- "application/ld+json"
                                    do! ctx.Response.WriteAsync(ProvenanceStore.serializeActivity a)
                            }
                            :> System.Threading.Tasks.Task
                    )
                    |> ignore
                | _ -> ()

                configured.UseMiddleware<ProvenanceMiddleware>()

            { spec with
                Services = addServices
                Middleware = addMiddleware }

        [<CustomOperation("useProvenance")>]
        member this.UseProvenance(spec: WebHostSpec) : WebHostSpec =
            let asm = System.Reflection.Assembly.GetEntryAssembly()

            let provClasses, typeIris =
                if isNull asm then
                    Map.empty, Map.empty
                else
                    match asm.GetType("GeneratedProvenance") with
                    | null -> Map.empty, Map.empty
                    | t ->
                        let provClassMap =
                            match t.GetProperty("provClasses") with
                            | null -> Map.empty
                            | p -> p.GetValue(null) :?> Map<string, Frank.Semantic.ProvOClass>

                        let typeIriMap =
                            match t.GetProperty("typeIris") with
                            | null -> Map.empty
                            | p -> p.GetValue(null) :?> Map<string, string>

                        provClassMap, typeIriMap

            let config =
                { ProvenanceConfig.Default with
                    ProvClasses = provClasses
                    TypeIris = typeIris }

            this.UseProvenanceWith(spec, config)
