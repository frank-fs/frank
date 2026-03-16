namespace Frank.Discovery

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.Logging
open Frank.Builder

/// Middleware that generates implicit OPTIONS responses for Frank resources.
/// Builds an Allow header from all HTTP methods registered for the matched route
/// and aggregates DiscoveryMediaType entries from endpoint metadata.
type OptionsDiscoveryMiddleware(next: RequestDelegate, dataSource: EndpointDataSource, logger: ILogger<OptionsDiscoveryMiddleware>) =

    /// Find all RouteEndpoints whose route pattern matches the given request path.
    /// Uses simple string matching against RoutePattern.RawText for literal routes,
    /// since Frank resources use literal route templates (e.g., "/items", "/health").
    ///
    /// Design decision: We match by comparing ctx.Request.Path against RoutePattern.RawText
    /// rather than using ctx.GetEndpoint(). For OPTIONS requests without an explicit OPTIONS
    /// handler, ASP.NET Core routing does not match an endpoint (GetEndpoint() returns null)
    /// because no route is registered for the OPTIONS method. Path-based matching against
    /// the EndpointDataSource is therefore the correct approach for implicit OPTIONS handling.
    let findSiblingEndpoints (requestPath: string) =
        let path = requestPath.TrimStart('/')
        dataSource.Endpoints
        |> Seq.choose (fun ep ->
            match ep with
            | :? RouteEndpoint as re ->
                let rawText = re.RoutePattern.RawText
                if not (isNull rawText) then
                    let normalizedPattern = rawText.TrimStart('/')
                    if normalizedPattern = path then Some re else None
                else
                    None
            | _ -> None)
        |> Seq.toList

    member _.Invoke(ctx: HttpContext) : Task =
        // (a) Not an OPTIONS request -> pass through
        if not (HttpMethods.IsOptions(ctx.Request.Method)) then
            next.Invoke(ctx)
        // (b) CORS preflight -> pass through
        elif ctx.Request.Headers.ContainsKey("Access-Control-Request-Method") then
            logger.LogDebug("CORS preflight detected for {Path}, passing through", ctx.Request.Path)
            next.Invoke(ctx)
        else
            // Find siblings by matching request path against route patterns
            let siblings = findSiblingEndpoints (ctx.Request.Path.Value)

            // No matching endpoints -> pass through
            if List.isEmpty siblings then
                next.Invoke(ctx)
            else
                // (e) Check if any sibling has an explicit OPTIONS handler
                let hasExplicitOptions =
                    siblings
                    |> List.exists (fun ep ->
                        let meta = ep.Metadata.GetMetadata<HttpMethodMetadata>()
                        not (isNull meta) && meta.HttpMethods |> Seq.exists (fun m -> m = "OPTIONS"))

                if hasExplicitOptions then
                    // Explicit handler takes precedence -- let routing handle it.
                    // The endpoint may already be set by routing; if so, just call next.
                    logger.LogDebug("Explicit OPTIONS handler found for {Path}, passing through", ctx.Request.Path)
                    next.Invoke(ctx)
                else
                    // (i)-(j) Collect HTTP methods and add OPTIONS
                    let methods =
                        siblings
                        |> Seq.collect (fun ep ->
                            let meta = ep.Metadata.GetMetadata<HttpMethodMetadata>()

                            if isNull meta then
                                Seq.empty
                            else
                                meta.HttpMethods :> seq<_>)
                        |> Set.ofSeq
                        |> Set.add "OPTIONS"

                    // (k)-(l) Collect and deduplicate DiscoveryMediaType entries
                    let mediaTypes =
                        siblings
                        |> Seq.collect (fun ep ->
                            ep.Metadata
                            |> Seq.choose (fun m ->
                                match m with
                                | :? DiscoveryMediaType as d -> Some d
                                | _ -> None))
                        |> Seq.distinctBy (fun mt -> mt.MediaType, mt.Rel)
                        |> Seq.toList

                    // (m) Set response status code to 200
                    ctx.Response.StatusCode <- 200

                    // (n) Set Allow header with sorted methods for determinism
                    let allowValue = methods |> Set.toSeq |> String.concat ", "
                    ctx.Response.Headers["Allow"] <- allowValue

                    // (o) Emit Link headers for each DiscoveryMediaType (RFC 8288)
                    let routePattern = (List.head siblings).RoutePattern.RawText
                    let path = if routePattern.StartsWith("/") then routePattern else "/" + routePattern

                    for mt in mediaTypes do
                        ctx.Response.Headers.Append("Link", $"<{path}>; rel=\"{mt.Rel}\"; type=\"{mt.MediaType}\"")

                    // (p) Return with empty body
                    Task.CompletedTask
