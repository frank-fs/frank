namespace Frank.Statecharts

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =
    type WebHostBuilder with

        /// Register a stateful resource directly from a StatefulResourceResult.
        /// Avoids the need to access .Resource on the result — pit of success.
        [<CustomOperation("resource")>]
        member __.Resource(spec: WebHostSpec, result: StatefulResourceResult) : WebHostSpec =
            { spec with
                Endpoints = Array.append spec.Endpoints result.Resource.Endpoints }

        /// Register Frank.Statecharts middleware.
        /// Middleware runs after routing, before endpoint execution.
        /// Recommended order: useAuthentication -> useStatecharts -> (LinkedData)
        [<CustomOperation("useStatecharts")>]
        member _.UseStatecharts(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Middleware = spec.Middleware >> fun app -> app.UseMiddleware<StateMachineMiddleware>() }

        /// Register Frank.Statecharts middleware with custom service configuration.
        /// The configureStore callback registers stores and other services.
        member _.UseStatecharts
            (spec: WebHostSpec, configureStore: IServiceCollection -> IServiceCollection)
            : WebHostSpec =
            { spec with
                Services = spec.Services >> configureStore
                Middleware = spec.Middleware >> fun app -> app.UseMiddleware<StateMachineMiddleware>() }
