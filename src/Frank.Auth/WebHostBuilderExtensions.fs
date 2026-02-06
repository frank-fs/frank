namespace Frank.Auth

open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =
    type WebHostBuilder with
        [<CustomOperation("useAuthentication")>]
        member _.UseAuthentication(spec: WebHostSpec, configure: AuthenticationBuilder -> AuthenticationBuilder) : WebHostSpec =
            { spec with
                Services = spec.Services >> fun services ->
                    configure (services.AddAuthentication()) |> ignore
                    services
                Middleware = spec.Middleware >> fun app ->
                    app.UseAuthentication() }

        [<CustomOperation("useAuthorization")>]
        member _.UseAuthorization(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Services = spec.Services >> fun services ->
                    services.AddAuthorization() |> ignore
                    services
                Middleware = spec.Middleware >> fun app ->
                    app.UseAuthorization() }

        [<CustomOperation("authorizationPolicy")>]
        member _.AuthorizationPolicy(spec: WebHostSpec, name: string, configure: AuthorizationPolicyBuilder -> unit) : WebHostSpec =
            { spec with
                Services = spec.Services >> fun services ->
                    services.AddAuthorization(fun options ->
                        options.AddPolicy(name, fun policy ->
                            configure policy)) |> ignore
                    services }
