namespace Frank.Discovery

open Microsoft.AspNetCore.Builder
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =
    type WebHostBuilder with

        /// Registers the OPTIONS discovery middleware. Endpoints respond to
        /// OPTIONS with an Allow header listing registered HTTP methods and
        /// aggregated DiscoveryMediaType information.
        [<CustomOperation("useOptionsDiscovery")>]
        member _.UseOptionsDiscovery(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Middleware =
                    spec.Middleware
                    >> fun app ->
                        app.UseMiddleware<OptionsDiscoveryMiddleware>() |> ignore
                        app }

        /// Registers the Link header middleware. Responses to GET/HEAD requests
        /// from endpoints with DiscoveryMediaType metadata will include
        /// RFC 8288 Link headers (on 2xx responses only).
        [<CustomOperation("useLinkHeaders")>]
        member _.UseLinkHeaders(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Middleware =
                    spec.Middleware
                    >> fun app ->
                        app.UseMiddleware<LinkHeaderMiddleware>() |> ignore
                        app }

        /// Registers OPTIONS discovery and Link header middlewares (without JSON Home).
        /// Responses lack API-level discovery — clients must know the root URL.
        /// For the full discovery bundle including JSON Home, use useDiscovery.
        [<CustomOperation("useDiscoveryHeaders")>]
        member _.UseDiscoveryHeaders(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Middleware =
                    spec.Middleware
                    >> fun app ->
                        app.UseMiddleware<OptionsDiscoveryMiddleware>()
                            .UseMiddleware<LinkHeaderMiddleware>()
                        |> ignore
                        app }

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

        /// Registers all three discovery middlewares: OPTIONS responses, Link headers,
        /// and JSON Home at the root. This is the recommended default.
        /// JSON Home middleware runs before routing (to avoid root route conflicts);
        /// OPTIONS and Link header middlewares run after routing.
        [<CustomOperation("useDiscovery")>]
        member this.UseDiscovery(spec: WebHostSpec) : WebHostSpec =
            // UseJsonHome → BeforeRoutingMiddleware; UseDiscoveryHeaders → Middleware.
            // Order here is documentation, not semantics — they write to different pipeline slots.
            spec |> this.UseJsonHome |> this.UseDiscoveryHeaders
