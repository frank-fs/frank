namespace Frank.Discovery

open Microsoft.AspNetCore.Builder
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =
    type WebHostBuilder with

        /// Registers the JSON Home middleware at GET / with content negotiation.
        /// Positioned before routing to avoid route conflicts with user-defined
        /// root resources. The middleware lazily computes the home document on
        /// first request from EndpointDataSource and optional JsonHomeMetadata
        /// (contributed by other packages via DI).
        [<CustomOperation("useJsonHome")>]
        member _.UseJsonHome(spec: WebHostSpec) : WebHostSpec =
            { spec with
                BeforeRoutingMiddleware =
                    spec.BeforeRoutingMiddleware
                    >> fun app ->
                        app.UseMiddleware<JsonHomeMiddleware>() |> ignore
                        app }
