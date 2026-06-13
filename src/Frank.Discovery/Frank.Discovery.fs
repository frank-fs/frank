namespace Frank.Discovery

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Frank.Builder

/// Extensions adding static semantic discovery (JSON Home, ALPS, OPTIONS/Allow,
/// Link rel=describedby) to the Frank WebHostBuilder CE. Consumes a DiscoveryConfig
/// (hand-authored now; generated from GeneratedDiscovery later) plus endpoint metadata.
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
