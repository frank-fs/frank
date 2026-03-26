namespace Frank.Affordances

open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Template
open Microsoft.Extensions.Logging
open Frank.Builder
open Frank.Resources.Model
open Frank.Statecharts

/// Middleware that generates implicit OPTIONS responses for Frank resources.
/// Builds an Allow header from all HTTP methods registered for the matched route
/// and aggregates DiscoveryMediaType entries from endpoint metadata.
/// When an affordance lookup is provided and the resource has statechart metadata,
/// returns state-aware Allow and Link headers from the pre-computed affordance data.
type OptionsDiscoveryMiddleware
    (
        next: RequestDelegate,
        dataSource: EndpointDataSource,
        logger: ILogger<OptionsDiscoveryMiddleware>,
        affordanceLookup: Dictionary<string, PreComputedAffordance>
    ) =

    /// Find all RouteEndpoints whose route pattern matches the given request path.
    /// Uses ASP.NET Core's TemplateMatcher to support both literal and parameterized
    /// routes (e.g., "/games/{gameId}").
    ///
    /// Design decision: We match by comparing ctx.Request.Path against RoutePattern.RawText
    /// rather than using ctx.GetEndpoint(). For OPTIONS requests without an explicit OPTIONS
    /// handler, ASP.NET Core routing does not match an endpoint (GetEndpoint() returns null)
    /// because no route is registered for the OPTIONS method. Path-based matching against
    /// the EndpointDataSource is therefore the correct approach for implicit OPTIONS handling.
    let findSiblingEndpoints (requestPath: string) =
        let pathString = PathString(requestPath)

        dataSource.Endpoints
        |> Seq.choose (fun ep ->
            match ep with
            | :? RouteEndpoint as re ->
                let rawText = re.RoutePattern.RawText

                if not (isNull rawText) then
                    let template = TemplateParser.Parse(rawText)
                    let matcher = TemplateMatcher(template, RouteValueDictionary())
                    let routeValues = RouteValueDictionary()

                    if matcher.TryMatch(pathString, routeValues) then
                        Some(re, routeValues)
                    else
                        None
                else
                    None
            | _ -> None)
        |> Seq.toList

    /// Try to resolve state-aware affordances from the pre-computed lookup.
    /// Returns Some(preComputed) when statechart metadata is present and the state can be resolved.
    let tryResolveStateAwareAffordance
        (ctx: HttpContext)
        (siblings: RouteEndpoint list)
        (routeValues: RouteValueDictionary)
        : Task<PreComputedAffordance option> =
        task {
            if isNull affordanceLookup then
                return None
            else
                // Check if any sibling has StateMachineMetadata
                let stateMeta =
                    siblings
                    |> List.tryPick (fun ep ->
                        let meta = ep.Metadata.GetMetadata<StateMachineMetadata>()

                        if obj.ReferenceEquals(meta, null) then None else Some meta)

                match stateMeta with
                | None -> return None
                | Some meta ->
                    // Populate route values on the context so ResolveInstanceId can read them
                    for kv in routeValues do
                        ctx.Request.RouteValues.[kv.Key] <- kv.Value

                    let instanceId = meta.ResolveInstanceId ctx
                    let! stateKey = meta.GetCurrentStateKey ctx.RequestServices ctx instanceId

                    let routeTemplate = (List.head siblings).RoutePattern.RawText
                    let compositeKey = AffordanceMap.lookupKey routeTemplate stateKey

                    match affordanceLookup.TryGetValue(compositeKey) with
                    | true, preComputed -> return Some preComputed
                    | false, _ -> return None
        }

    /// Collect HTTP methods from sibling endpoints using HttpMethodMetadata (route-level fallback).
    let collectRouteLevelMethods (siblings: RouteEndpoint list) =
        siblings
        |> Seq.collect (fun ep ->
            let meta = ep.Metadata.GetMetadata<HttpMethodMetadata>()

            if isNull meta then
                Seq.empty
            else
                meta.HttpMethods :> seq<_>)
        |> Set.ofSeq
        |> Set.add "OPTIONS"

    /// Secondary constructor for backward compatibility when no affordance lookup is configured.
    new(next: RequestDelegate, dataSource: EndpointDataSource, logger: ILogger<OptionsDiscoveryMiddleware>) =
        OptionsDiscoveryMiddleware(next, dataSource, logger, null)

    member _.Invoke(ctx: HttpContext) : Task =
        // (a) Not an OPTIONS request -> pass through
        if not (HttpMethods.IsOptions(ctx.Request.Method)) then
            next.Invoke(ctx)
        // (b) CORS preflight -> pass through
        elif ctx.Request.Headers.ContainsKey("Access-Control-Request-Method") then
            logger.LogDebug("CORS preflight detected for {Path}, passing through", ctx.Request.Path)
            next.Invoke(ctx)
        else
            task {
                // Find siblings by matching request path against route patterns
                let matches = findSiblingEndpoints (ctx.Request.Path.Value)

                // No matching endpoints -> pass through
                if List.isEmpty matches then
                    do! next.Invoke(ctx)
                else
                    let siblings = matches |> List.map fst
                    let routeValues = matches |> List.head |> snd

                    // (e) Check if any sibling has an explicit OPTIONS handler
                    let hasExplicitOptions =
                        siblings
                        |> List.exists (fun ep ->
                            let meta = ep.Metadata.GetMetadata<HttpMethodMetadata>()
                            not (isNull meta) && meta.HttpMethods |> Seq.exists (fun m -> m = "OPTIONS"))

                    if hasExplicitOptions then
                        // Explicit handler takes precedence -- let routing handle it.
                        logger.LogDebug("Explicit OPTIONS handler found for {Path}, passing through", ctx.Request.Path)

                        do! next.Invoke(ctx)
                    else
                        // Try state-aware affordance lookup first
                        let! stateAwareAffordance = tryResolveStateAwareAffordance ctx siblings routeValues

                        match stateAwareAffordance with
                        | Some preComputed ->
                            // State-aware response from pre-computed affordance data
                            ctx.Response.StatusCode <- 200
                            ctx.Response.Headers["Allow"] <- preComputed.AllowHeaderValue
                            ctx.Response.Headers["Link"] <- preComputed.LinkHeaderValues
                        | None ->
                            // Fall back to route-level: collect all HttpMethodMetadata methods
                            let methods = collectRouteLevelMethods siblings

                            // Collect and deduplicate DiscoveryMediaType entries
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

                            ctx.Response.StatusCode <- 200

                            let allowValue = methods |> Set.toSeq |> String.concat ", "
                            ctx.Response.Headers["Allow"] <- allowValue

                            // Emit Link headers for each DiscoveryMediaType (RFC 8288)
                            let routePattern = (List.head siblings).RoutePattern.RawText

                            let path =
                                if routePattern.StartsWith("/") then
                                    routePattern
                                else
                                    "/" + routePattern

                            for mt in mediaTypes do
                                ctx.Response.Headers.Append(
                                    "Link",
                                    $"<{path}>; rel=\"{mt.Rel}\"; type=\"{mt.MediaType}\""
                                )
            }
            :> Task
