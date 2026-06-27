namespace Frank.LinkedData

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
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
