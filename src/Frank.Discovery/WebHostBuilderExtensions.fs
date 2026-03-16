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

        /// Convenience: registers both OPTIONS discovery and Link header middlewares.
        [<CustomOperation("useDiscovery")>]
        member _.UseDiscovery(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Middleware =
                    spec.Middleware
                    >> fun app ->
                        app.UseMiddleware<OptionsDiscoveryMiddleware>()
                            .UseMiddleware<LinkHeaderMiddleware>()
                        |> ignore
                        app }
