namespace Frank.Discovery

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =
    type WebHostBuilder with

        /// Registers the JSON Home middleware at GET / with content negotiation.
        /// Positioned before routing to avoid route conflicts with user-defined
        /// root resources. The middleware lazily computes the home document on
        /// first request from EndpointDataSource and optional JsonHomeMetadata
        /// (contributed by other packages via DI).
        /// Registers a default empty JsonHomeMetadata if none is already in DI.
        [<CustomOperation("useJsonHome")>]
        member _.UseJsonHome(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Services =
                    spec.Services
                    >> fun services ->
                        services.TryAddSingleton<JsonHomeMetadata>(JsonHomeMetadata.Empty)
                        services
                BeforeRoutingMiddleware =
                    spec.BeforeRoutingMiddleware
                    >> fun app ->
                        app.UseMiddleware<JsonHomeMiddleware>() |> ignore
                        app }
