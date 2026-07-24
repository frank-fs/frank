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
        next: RequestDelegate * cache: ETagCache * logger: ILogger<ConditionalRequestMiddleware> ->
            ConditionalRequestMiddleware

    member Invoke: ctx: HttpContext -> Task

/// DI and middleware registration extensions for conditional request handling.
[<AutoOpen>]
module ConditionalRequestMiddlewareExtensions =

    type IServiceCollection with

        /// Register the ETag cache as a singleton service.
        member AddETagCache: ?maxEntries: int -> IServiceCollection

    /// Structural enforcement of the R10 ordering contract (#426/#467): call this BEFORE
    /// registering a Link-header-emitting middleware (e.g. LinkedDataMiddleware,
    /// ProvenanceMiddleware). If useConditionalRequests has already been registered on this
    /// same IApplicationBuilder, the caller is being registered too late -- inner to it, the
    /// wrong order -- and this throws immediately at app-startup/configuration time instead of
    /// silently dropping the caller's Link header on a future 304/412 short-circuit.
    val guardAgainstInnerLinkMiddleware: app: IApplicationBuilder -> middlewareName: string -> unit

    /// Register the conditional request middleware in the ASP.NET Core pipeline.
    /// Must be called after UseRouting() -- use via `plug useConditionalRequests`.
    ///
    /// Ordering contract (R10, #426): any middleware that emits Link headers (e.g.
    /// describedby, prov) for an ETag-enabled endpoint MUST be registered OUTER to
    /// (before) this middleware. A 304/412 short-circuit here skips everything registered
    /// INNER to (after) it, including the terminal handler -- but headers already appended
    /// to ctx.Response.Headers, or callbacks already registered via ctx.Response.OnStarting,
    /// by an OUTER middleware before it called next.Invoke survive the short-circuit intact,
    /// because ASP.NET Core flushes the response headers (running any OnStarting callbacks)
    /// regardless of which middleware produced the final status code. Existing precedent:
    /// LinkedDataMiddleware appends its `describedby` Link header directly to
    /// ctx.Response.Headers before calling next.Invoke
    /// (src/Frank.LinkedData/LinkedDataMiddleware.fs ~line 349-354); ProvenanceMiddleware
    /// registers its `has_provenance` Link header via ctx.Response.OnStarting before calling
    /// next.Invoke (src/Frank.Provenance/ProvenanceMiddleware.fs ~line 259-266). Both
    /// patterns survive being wrapped by this middleware, as long as they are registered
    /// outer to it.
    ///
    /// Sets a marker on app.Properties so a Link-emitting middleware registered afterwards can
    /// call guardAgainstInnerLinkMiddleware and be caught structurally (#467) instead of
    /// relying solely on this doc comment.
    val useConditionalRequests: app: IApplicationBuilder -> IApplicationBuilder
