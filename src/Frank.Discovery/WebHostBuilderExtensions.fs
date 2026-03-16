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
