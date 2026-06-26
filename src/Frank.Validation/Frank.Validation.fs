namespace Frank.Validation

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Frank.Builder

[<AutoOpen>]
module ValidationExtensions =

    type WebHostBuilder with

        [<CustomOperation("useValidationWith")>]
        member _.UseValidationWith(spec: WebHostSpec, config: ValidationConfig) : WebHostSpec =
            let addServices (services: IServiceCollection) =
                services.AddSingleton<ValidationConfig>(config) |> ignore
                spec.Services services

            let addMiddleware (app: IApplicationBuilder) =
                let configured = spec.Middleware app
                configured.UseMiddleware<ValidationMiddleware>() |> ignore
                configured

            { spec with
                Services = addServices
                Middleware = addMiddleware }

        [<CustomOperation("useValidation")>]
        member this.UseValidation(spec: WebHostSpec) : WebHostSpec =
            let assemblies = System.AppDomain.CurrentDomain.GetAssemblies()

            match GeneratedValidationResolver.resolveGeneratedConfig assemblies with
            | Ok config -> this.UseValidationWith(spec, config)
            | Error msg -> invalidOp msg
