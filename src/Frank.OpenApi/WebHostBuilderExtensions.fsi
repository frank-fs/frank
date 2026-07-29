namespace Frank.OpenApi

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.OpenApi
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =

    /// Appends a `Link: <...>; rel="service-desc"; type="application/json"` header
    /// (RFC 8631) to every response, advertising the OpenAPI document. Composed into
    /// `WebHostSpec.BeforeRoutingMiddleware`, not `Middleware` -- `UseRouting()` matches
    /// endpoints globally, once, and the first `EndpointMiddleware` encountered in the
    /// pipeline dispatches whatever matched regardless of which `UseEndpoints()` call
    /// registered it, without calling `next()`. Middleware placed anywhere in `Middleware`
    /// (even before this module's own `UseEndpoints` call) can still be bypassed by an
    /// earlier `UseEndpoints()` call composed in by a different package or by `plug`;
    /// `BeforeRoutingMiddleware` runs before `UseRouting()` even executes, so no
    /// `UseEndpoints()` call anywhere can ever short-circuit it.
    ///
    /// Uses `Response.OnStarting` rather than a direct header write, so the header
    /// survives `UseExceptionHandler` regenerating the response after an unhandled
    /// exception (`Response.Clear()` does not clear already-registered `OnStarting`
    /// callbacks -- verified empirically). It can still be skipped if the app registers
    /// its own short-circuiting gate (e.g. `plugBeforeRouting` for a maintenance-mode
    /// check) *ahead* of `useOpenApi` in the same `webHost { }` block and that gate
    /// doesn't call `next()` -- that's inherent to middleware ordering (nothing can run
    /// after a gate that never hands off control) and isn't specific to this feature.
    val addServiceDescLinkHeader : app:IApplicationBuilder -> IApplicationBuilder

    type WebHostBuilder with
        [<CustomOperation("useOpenApi")>]
        member UseOpenApi : spec:WebHostSpec -> WebHostSpec

        [<CustomOperation("useOpenApi")>]
        member UseOpenApi : spec:WebHostSpec * configure:(OpenApiOptions -> unit) -> WebHostSpec
