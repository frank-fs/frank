namespace Frank.Affordances

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Frank.Builder

/// Middleware that appends RFC 8288 Link headers to 2xx GET/HEAD responses
/// from endpoints with DiscoveryMediaType metadata.
/// Zero overhead for unmarked resources: metadata check happens before calling next.
type LinkHeaderMiddleware(next: RequestDelegate, logger: ILogger<LinkHeaderMiddleware>) =

    member _.Invoke(ctx: HttpContext) : Task =
        // (a) Get matched endpoint
        let endpoint = ctx.GetEndpoint()

        // (b) No endpoint matched -> pass through (zero overhead)
        if isNull endpoint then
            next.Invoke(ctx)
        else
            // (c) Collect DiscoveryMediaType entries from endpoint metadata
            let mediaTypes =
                endpoint.Metadata
                |> Seq.choose (fun m ->
                    match m with
                    | :? DiscoveryMediaType as d -> Some d
                    | _ -> None)
                |> Seq.toList

            // (d) No DiscoveryMediaType entries -> pass through (zero overhead, SC-007)
            if List.isEmpty mediaTypes then
                next.Invoke(ctx)
            // (e) Not GET or HEAD -> pass through (Link headers only on GET/HEAD)
            elif not (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method)) then
                next.Invoke(ctx)
            else
                // Register an OnStarting callback to add Link headers just before
                // the response begins streaming. At that point the status code is set
                // but headers can still be modified.
                ctx.Response.OnStarting(
                    fun state ->
                        let httpCtx = state :?> HttpContext

                        let isSuccess =
                            httpCtx.Response.StatusCode >= 200 && httpCtx.Response.StatusCode < 300

                        if isSuccess then
                            // Deduplicate by (MediaType, Rel) tuple
                            let dedupedMediaTypes =
                                mediaTypes |> Seq.distinctBy (fun mt -> mt.MediaType, mt.Rel) |> Seq.toList

                            let requestPath = httpCtx.Request.Path.Value

                            for mt in dedupedMediaTypes do
                                let linkValue =
                                    sprintf "<%s>; rel=\"%s\"; type=\"%s\"" requestPath mt.Rel mt.MediaType

                                httpCtx.Response.Headers.Append("Link", linkValue)

                                logger.LogDebug(
                                    "Added Link header for {Path}: rel={Rel}, type={MediaType}",
                                    requestPath,
                                    mt.Rel,
                                    mt.MediaType
                                )
                        // (i) If status is not 2xx -> do nothing (FR-010)
                        Task.CompletedTask
                    , ctx :> obj
                )

                // (f) Call next to execute the handler
                next.Invoke(ctx)
