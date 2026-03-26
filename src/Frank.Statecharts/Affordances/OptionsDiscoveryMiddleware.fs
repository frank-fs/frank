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

/// Result of attempting to resolve state-aware affordance data.
[<RequireQualifiedAccess>]
type AffordanceResolution =
    /// Pre-computed affordance data found for the resolved state.
    | Found of PreComputedAffordance
    /// No statechart metadata on this resource — use route-level fallback.
    | NoStatechart
    /// Statechart exists but state could not be resolved — return 404.
    | StateNotResolved

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

    /// Cached parsed templates for route matching. Lazy-initialized on first request
    /// to avoid per-request TemplateParser/TemplateMatcher allocations (FW-1).
    let parsedEndpoints =
        lazy
            (dataSource.Endpoints
             |> Seq.choose (fun ep ->
                 match ep with
                 | :? RouteEndpoint as re when not (isNull re.RoutePattern.RawText) ->
                     let template = TemplateParser.Parse(re.RoutePattern.RawText)
                     let matcher = TemplateMatcher(template, RouteValueDictionary())
                     Some(re, matcher)
                 | _ -> None)
             |> Seq.toList)

    /// Find all RouteEndpoints whose route pattern matches the given request path.
    /// Uses cached TemplateMatcher instances initialized lazily at first request.
    let findSiblingEndpoints (requestPath: string) =
        let pathString = PathString(requestPath)

        parsedEndpoints.Value
        |> List.choose (fun (re, matcher) ->
            let routeValues = RouteValueDictionary()

            if matcher.TryMatch(pathString, routeValues) then
                Some(re, routeValues)
            else
                None)

    /// Try to resolve state-aware affordances from the pre-computed lookup.
    /// Returns Found when statechart metadata is present and the state resolves.
    /// Returns NoStatechart when no statechart metadata exists (use route-level fallback).
    /// Returns StateNotResolved when statechart exists but state can't be resolved (404).
    let tryResolveStateAwareAffordance
        (ctx: HttpContext)
        (siblings: RouteEndpoint list)
        (routeValues: RouteValueDictionary)
        : Task<AffordanceResolution> =
        task {
            if isNull affordanceLookup || affordanceLookup.Count = 0 then
                return AffordanceResolution.NoStatechart
            else
                // Check if any sibling has StateMachineMetadata
                let stateMeta =
                    siblings
                    |> List.tryPick (fun ep ->
                        let meta = ep.Metadata.GetMetadata<StateMachineMetadata>()

                        if obj.ReferenceEquals(meta, null) then None else Some meta)

                match stateMeta with
                | None -> return AffordanceResolution.NoStatechart
                | Some meta ->
                    // Populate route values on the context so ResolveInstanceId can read them
                    for kv in routeValues do
                        ctx.Request.RouteValues.[kv.Key] <- kv.Value

                    let instanceId = meta.ResolveInstanceId ctx
                    let! stateKey = meta.GetCurrentStateKey ctx.RequestServices ctx instanceId

                    let routeTemplate = (List.head siblings).RoutePattern.RawText
                    let compositeKey = AffordanceMap.lookupKey routeTemplate stateKey

                    match affordanceLookup.TryGetValue(compositeKey) with
                    | true, preComputed -> return AffordanceResolution.Found preComputed
                    | false, _ -> return AffordanceResolution.StateNotResolved
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
                        let! resolution = tryResolveStateAwareAffordance ctx siblings routeValues

                        match resolution with
                        | AffordanceResolution.Found preComputed ->
                            // State-aware response from pre-computed affordance data
                            ctx.Response.StatusCode <- 204
                            ctx.Response.Headers["Allow"] <- preComputed.AllowHeaderValue
                            ctx.Response.Headers["Link"] <- preComputed.LinkHeaderValues
                        | AffordanceResolution.StateNotResolved ->
                            // Statechart exists but state could not be resolved — 404 (F-4)
                            ctx.Response.StatusCode <- 404
                        | AffordanceResolution.NoStatechart ->
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

                            ctx.Response.StatusCode <- 204

                            let allowValue = methods |> Set.toSeq |> String.concat ", "
                            ctx.Response.Headers["Allow"] <- allowValue

                            // Emit Link headers only for non-parameterized routes (F-2).
                            // Parameterized routes contain {}, which are invalid URI chars per RFC 3986.
                            let routePattern = (List.head siblings).RoutePattern.RawText

                            if not (routePattern.Contains("{")) then
                                let path =
                                    if routePattern.StartsWith("/") then
                                        routePattern
                                    else
                                        "/" + routePattern

                                for mt in mediaTypes do
                                    let linkValue =
                                        sprintf "<%s>; rel=\"%s\"; type=\"%s\"" path mt.Rel mt.MediaType

                                    ctx.Response.Headers.Append("Link", linkValue)
            }
            :> Task
