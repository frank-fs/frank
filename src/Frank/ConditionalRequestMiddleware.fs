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
    (
        next: RequestDelegate,
        cache: ETagCache,
        providerFactory: IETagProviderFactory,
        logger: ILogger<ConditionalRequestMiddleware>
    ) =

    /// Computes an ETag via the provider, quotes it to wire format, logging and re-raising on failure.
    member private _.TryComputeETag(provider: IETagProvider, instanceId: string, resourceKey: string) =
        task {
            try
                let! raw = provider.ComputeETag(instanceId)
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

                // 2. Check for ETagMetadata -- if absent, pass through (zero overhead)
                let etagMetadata = endpoint.Metadata.GetMetadata<ETagMetadata>()

                if isNull (box etagMetadata) then
                    do! next.Invoke(ctx)
                else

                    // 3. Resolve provider
                    match providerFactory.CreateProvider(endpoint) with
                    | None -> do! next.Invoke(ctx)
                    | Some provider ->
                        let instanceId = etagMetadata.ResolveInstanceId ctx
                        let resourceKey = ctx.Request.Path.Value

                        // Get current ETag (from cache or computed fresh), in wire format (quoted)
                        let! cachedETag = cache.GetETag(resourceKey) |> Async.StartAsTask

                        let! currentETag =
                            match cachedETag with
                            | Some etag -> Task.FromResult(Some etag)
                            | None ->
                                task {
                                    let! computed = this.TryComputeETag(provider, instanceId, resourceKey)

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
                                let! freshETag = this.TryComputeETag(provider, instanceId, resourceKey)

                                match freshETag with
                                | Some etag -> cache.SetETag(resourceKey, etag)
                                | None -> ()

                            // Invalidate cache after successful mutations and compute fresh ETag for cache
                            if isMutation && statusCode >= 200 && statusCode < 300 then
                                cache.Invalidate(resourceKey)
                                let! newETag = this.TryComputeETag(provider, instanceId, resourceKey)

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
    let useConditionalRequests (app: IApplicationBuilder) =
        app.UseMiddleware<ConditionalRequestMiddleware>()
