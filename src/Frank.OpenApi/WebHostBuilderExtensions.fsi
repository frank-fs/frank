namespace Frank.OpenApi

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.OpenApi
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =

    /// Appends a `Link: <...>; rel="service-desc"; type="application/json"` header
    /// (RFC 8631) to every response, advertising the OpenAPI document. Must run
    /// before this module's own `UseEndpoints` call in the middleware pipeline --
    /// EndpointMiddleware is terminal for any endpoint ASP.NET Core's routing has
    /// already matched, so middleware placed after a UseEndpoints call never runs
    /// for matched requests, only for 404s.
    val addServiceDescLinkHeader : app:IApplicationBuilder -> IApplicationBuilder

    type WebHostBuilder with
        [<CustomOperation("useOpenApi")>]
        member UseOpenApi : spec:WebHostSpec -> WebHostSpec

        [<CustomOperation("useOpenApi")>]
        member UseOpenApi : spec:WebHostSpec * configure:(OpenApiOptions -> unit) -> WebHostSpec
