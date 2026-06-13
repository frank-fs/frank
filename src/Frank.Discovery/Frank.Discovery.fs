namespace Frank.Discovery

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Frank.Builder

/// Extensions adding ALPS/JSON-Home/OPTIONS discovery to the Frank WebHostBuilder CE.
[<AutoOpen>]
module DiscoveryExtensions =

    /// Extract the vocabulary IRI from a link header value `<https://...>; rel="describedby"`.
    let private extractIri (link: string) =
        let s = link.IndexOf('<') + 1
        let e = link.IndexOf('>')
        if s > 0 && e > s then link.[s .. e - 1] else link

    /// Build JSON Home resources from the DiscoveryConfig.
    /// Each entry maps the first describedby IRI for a type to a placeholder href.
    let private buildJsonHomeResources (config: DiscoveryConfig) =
        config.DescribedByLinks
        |> Map.toList
        |> List.map (fun (typeName, links) ->
            let rel =
                match links with
                | link :: _ -> extractIri link
                | [] -> typeName

            { JsonHomeSerializer.Relation = rel
              JsonHomeSerializer.Href = "/" + typeName.ToLowerInvariant()
              JsonHomeSerializer.Allow = [ "GET"; "HEAD" ] })

    type WebHostBuilder with

        /// Register ALPS/JSON-Home/OPTIONS discovery middleware with explicit configuration.
        [<CustomOperation("useDiscoveryWith")>]
        member _.UseDiscoveryWith(spec: WebHostSpec, config: DiscoveryConfig) : WebHostSpec =
            let addServices (services: IServiceCollection) =
                services.AddSingleton<DiscoveryConfig>(config) |> ignore
                spec.Services(services)

            let addMiddleware (app: IApplicationBuilder) =
                let configured = spec.Middleware(app)

                configured.UseMiddleware<DiscoveryMiddleware.OptionsEnricherMiddleware>()
                |> ignore

                // Register ALPS and JSON Home endpoints on the WebApplication.
                let webApp = app :?> WebApplication

                webApp.MapGet(
                    config.ProfileBaseUri + "/{slug}",
                    Func<HttpContext, System.Threading.Tasks.Task>(fun ctx ->
                        task {
                            ctx.Response.ContentType <- "application/alps+json"
                            do! ctx.Response.WriteAsync(AlpsSerializer.serialize config.AlpsDescriptors)
                        }
                        :> System.Threading.Tasks.Task)
                )
                |> ignore

                webApp.MapGet(
                    config.HomeRoute,
                    Func<HttpContext, System.Threading.Tasks.Task>(fun ctx ->
                        task {
                            let resources = buildJsonHomeResources config
                            ctx.Response.ContentType <- "application/json-home+json"
                            do! ctx.Response.WriteAsync(JsonHomeSerializer.serialize resources)
                        }
                        :> System.Threading.Tasks.Task)
                )
                |> ignore

                configured

            { spec with
                Services = addServices
                Middleware = addMiddleware }

        /// Register ALPS/JSON-Home/OPTIONS discovery middleware using DiscoveryConfig.Default.
        /// Use useDiscoveryWith for explicit configuration.
        [<CustomOperation("useDiscovery")>]
        member this.UseDiscovery(spec: WebHostSpec) : WebHostSpec =
            this.UseDiscoveryWith(spec, DiscoveryConfig.Default)
