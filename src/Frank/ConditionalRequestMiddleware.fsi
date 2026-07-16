namespace Frank

open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

/// Middleware that evaluates conditional request headers (If-None-Match, If-Match)
/// against ETag-enabled endpoints and returns 304/412 short-circuit responses.
type ConditionalRequestMiddleware =
    new:
        next: RequestDelegate *
        cache: ETagCache *
        providerFactory: IETagProviderFactory *
        logger: ILogger<ConditionalRequestMiddleware> ->
            ConditionalRequestMiddleware

    member Invoke: ctx: HttpContext -> Task

/// DI and middleware registration extensions for conditional request handling.
[<AutoOpen>]
module ConditionalRequestMiddlewareExtensions =

    type IServiceCollection with

        /// Register the ETag cache as a singleton service.
        member AddETagCache: ?maxEntries: int -> IServiceCollection

    /// Register the conditional request middleware in the ASP.NET Core pipeline.
    /// Must be called after UseRouting() -- use via `plug useConditionalRequests`.
    val useConditionalRequests: app: IApplicationBuilder -> IApplicationBuilder
