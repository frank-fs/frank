namespace Frank

open System
open System.Runtime.ExceptionServices
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

/// Middleware that evaluates conditional request headers (If-None-Match, If-Match)
/// against ETag-enabled endpoints and returns 304/412 short-circuit responses.
type ConditionalRequestMiddleware
    (next: RequestDelegate, cache: ETagCache, logger: ILogger<ConditionalRequestMiddleware>) =

    /// Computes an ETag via the metadata's compute closure, quotes it to wire format,
    /// logging and re-raising on failure.
    member private _.TryComputeETag
        (compute: ETagContext -> Task<string option>, etagContext: ETagContext, resourceKey: string)
        =
        task {
            try
                let! raw = compute etagContext
                return raw |> Option.map ETagFormat.quote
            with ex ->
                logger.LogError(ex, "Error computing ETag for resource {ResourceKey}", resourceKey)
                ExceptionDispatchInfo.Capture(ex).Throw()
                return None // unreachable but satisfies compiler
        }

    member this.Invoke(ctx: HttpContext) : Task =
        task {
            // 1. Get the matched endpoint
            let endpoint = ctx.GetEndpoint()

            if isNull endpoint then
                do! next.Invoke(ctx)
            else

                // 2. Check for ETagMetadata -- if absent, pass through (zero overhead).
                // Metadata presence IS the capability check -- no separate provider resolution.
                let etagMetadata = endpoint.Metadata.GetMetadata<ETagMetadata>()

                if isNull (box etagMetadata) then
                    do! next.Invoke(ctx)
                else
                    let instanceId = etagMetadata.ResolveInstanceId ctx
                    // Path alone is not a unique resource key -- a query-string-identified
                    // resource (e.g. /provenance?resource=<uri>) would otherwise collide with
                    // every other query string on the same path, caching one resource's ETag
                    // under a key a completely different resource then reads back (#426).
                    // Derived from instanceId (the endpoint's own declared identity), not the
                    // raw query string, so an unrelated query param can never bust the cache
                    // for a resource whose instanceId is unchanged.
                    let resourceKey = ctx.Request.Path.Value + "|" + instanceId

                    let etagContext =
                        { InstanceId = instanceId
                          HttpContext = ctx }

                    // Get current ETag (from cache or computed fresh), in wire format (quoted)
                    let! cachedETag = cache.GetETag(resourceKey) |> Async.StartAsTask

                    let! currentETag =
                        match cachedETag with
                        | Some etag -> Task.FromResult(Some etag)
                        | None ->
                            task {
                                let! computed = this.TryComputeETag(etagMetadata.Compute, etagContext, resourceKey)

                                match computed with
                                | Some etag ->
                                    cache.SetETag(resourceKey, etag)
                                    return Some etag
                                | None -> return None
                            }

                    let method = ctx.Request.Method
                    let isGetOrHead = HttpMethods.IsGet(method) || HttpMethods.IsHead(method)

                    let isMutation =
                        HttpMethods.IsPost(method)
                        || HttpMethods.IsPut(method)
                        || HttpMethods.IsDelete(method)

                    let mutable shortCircuited = false

                    // If-None-Match evaluation (GET/HEAD) -> 304 Not Modified
                    if isGetOrHead then
                        let ifNoneMatch = ctx.Request.Headers.IfNoneMatch.ToString()

                        if
                            not (String.IsNullOrWhiteSpace(ifNoneMatch))
                            && ETagComparison.anyMatch currentETag ifNoneMatch
                        then
                            ctx.Response.StatusCode <- StatusCodes.Status304NotModified

                            match currentETag with
                            | Some etag -> ctx.Response.Headers.ETag <- etag
                            | None -> ()

                            shortCircuited <- true

                    // If-Match evaluation (POST/PUT/DELETE) -> 412 Precondition Failed
                    if not shortCircuited && isMutation then
                        let ifMatch = ctx.Request.Headers.IfMatch.ToString()

                        if
                            not (String.IsNullOrWhiteSpace(ifMatch))
                            && not (ETagComparison.anyMatch currentETag ifMatch)
                        then
                            ctx.Response.StatusCode <- StatusCodes.Status412PreconditionFailed
                            shortCircuited <- true

                    if not shortCircuited then
                        // Set ETag header before handler for GET/HEAD if we have it.
                        // NOTE: The ETag header must be set before the response body is written,
                        // because ASP.NET Core sends headers on the first body write (or flush).
                        if isGetOrHead then
                            match currentETag with
                            | Some etag -> ctx.Response.Headers.ETag <- etag
                            | None -> ()

                        // Proceed with the handler
                        do! next.Invoke(ctx)

                        // After handler execution
                        let statusCode = ctx.Response.StatusCode

                        // For GET/HEAD without a pre-existing ETag, cache the computed value
                        if isGetOrHead && statusCode >= 200 && statusCode < 300 && currentETag.IsNone then
                            let! freshETag = this.TryComputeETag(etagMetadata.Compute, etagContext, resourceKey)

                            match freshETag with
                            | Some etag -> cache.SetETag(resourceKey, etag)
                            | None -> ()

                        // Invalidate cache after successful mutations and compute fresh ETag for cache
                        if isMutation && statusCode >= 200 && statusCode < 300 then
                            cache.Invalidate(resourceKey)
                            let! newETag = this.TryComputeETag(etagMetadata.Compute, etagContext, resourceKey)

                            match newETag with
                            | Some etag -> cache.SetETag(resourceKey, etag)
                            | None -> ()
        }

/// DI and middleware registration extensions for conditional request handling.
[<AutoOpen>]
module ConditionalRequestMiddlewareExtensions =

    type IServiceCollection with

        /// Register the ETag cache as a singleton service.
        member services.AddETagCache(?maxEntries: int) : IServiceCollection =
            let max = defaultArg maxEntries 10_000

            services.AddSingleton<ETagCache>(fun sp ->
                let logger = sp.GetRequiredService<ILogger<ETagCache>>()
                new ETagCache(max, logger))

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
    let useConditionalRequests (app: IApplicationBuilder) =
        app.UseMiddleware<ConditionalRequestMiddleware>()
