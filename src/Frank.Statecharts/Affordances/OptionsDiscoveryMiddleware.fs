namespace Frank.Affordances

open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Template
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Primitives
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

    /// Cached parsed route templates for matching. RouteTemplate is immutable and safe
    /// for concurrent access. TemplateMatcher is created per-request because its internal
    /// state is not guaranteed thread-safe.
    let parsedTemplates =
        lazy
            (dataSource.Endpoints
             |> Seq.choose (fun ep ->
                 match ep with
                 | :? RouteEndpoint as re when not (isNull re.RoutePattern.RawText) ->
                     let template = TemplateParser.Parse(re.RoutePattern.RawText)
                     Some(re, template)
                 | _ -> None)
             |> Seq.toArray)

    /// Pre-computed route-level fallback data (Allow header + Link header values).
    /// Keyed by route template, computed lazily on first request.
    let routeLevelCache =
        lazy
            (let cache =
                Dictionary<string, struct (StringValues * StringValues)>(System.StringComparer.Ordinal)

             let grouped =
                 parsedTemplates.Value |> Array.groupBy (fun (re, _) -> re.RoutePattern.RawText)

             for routeTemplate, endpoints in grouped do
                 let siblings = endpoints |> Array.map fst

                 let methods =
                     siblings
                     |> Seq.collect (fun ep ->
                         let meta = ep.Metadata.GetMetadata<HttpMethodMetadata>()

                         if isNull meta then
                             Seq.empty
                         else
                             meta.HttpMethods :> seq<_>)
                     |> Set.ofSeq
                     |> Set.add "HEAD"
                     |> Set.add "OPTIONS"

                 let allowValue = StringValues(System.String.Join(", ", methods))

                 let mediaTypes =
                     siblings
                     |> Seq.collect (fun ep ->
                         ep.Metadata
                         |> Seq.choose (fun m ->
                             match m with
                             | :? DiscoveryMediaType as d -> Some d
                             | _ -> None))
                     |> Seq.distinctBy (fun mt -> mt.MediaType, mt.Rel)
                     |> Seq.toArray

                 let linkValues =
                     if routeTemplate.Contains("{") then
                         StringValues.Empty
                     else
                         let path =
                             if routeTemplate.StartsWith("/") then
                                 routeTemplate
                             else
                                 "/" + routeTemplate

                         StringValues(
                             mediaTypes
                             |> Array.map (fun mt -> sprintf "<%s>; rel=\"%s\"; type=\"%s\"" path mt.Rel mt.MediaType)
                         )

                 cache.[routeTemplate] <- struct (allowValue, linkValues)

             cache)

    let findSiblingEndpoints (requestPath: string) =
        let pathString = PathString(requestPath)

        parsedTemplates.Value
        |> Array.choose (fun (re, template) ->
            let matcher = TemplateMatcher(template, RouteValueDictionary())
            let routeValues = RouteValueDictionary()

            if matcher.TryMatch(pathString, routeValues) then
                Some(re, routeValues)
            else
                None)
        |> Array.toList

    let tryResolveStateAwareAffordance
        (ctx: HttpContext)
        (siblings: RouteEndpoint list)
        (routeValues: RouteValueDictionary)
        : Task<AffordanceResolution> =
        task {
            if affordanceLookup.Count = 0 then
                return AffordanceResolution.NoStatechart
            else
                let stateMeta =
                    siblings
                    |> List.tryPick (fun ep ->
                        let meta = ep.Metadata.GetMetadata<StateMachineMetadata>()
                        if obj.ReferenceEquals(meta, null) then None else Some meta)

                match stateMeta with
                | None -> return AffordanceResolution.NoStatechart
                | Some meta ->
                    // Populate route values so ResolveInstanceId can read them.
                    // Safe: this path always terminates (204/404), never calls next.
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

    /// Resolve URI template parameters in pre-computed Link headers using route values.
    let resolveTemplateLinks (ctx: HttpContext) (values: StringValues) =
        let routeValues = ctx.Request.RouteValues

        let resolved =
            values.ToArray()
            |> Array.map (fun link ->
                let mutable result = link

                for kv in routeValues do
                    result <- result.Replace(sprintf "{%s}" kv.Key, string kv.Value)

                result)

        StringValues(resolved)

    member _.Invoke(ctx: HttpContext) : Task =
        if not (HttpMethods.IsOptions(ctx.Request.Method)) then
            next.Invoke(ctx)
        elif ctx.Request.Headers.ContainsKey("Access-Control-Request-Method") then
            logger.LogDebug("CORS preflight detected for {Path}, passing through", ctx.Request.Path)
            next.Invoke(ctx)
        else
            task {
                let matches = findSiblingEndpoints (ctx.Request.Path.Value)

                if List.isEmpty matches then
                    do! next.Invoke(ctx)
                else
                    let siblings = matches |> List.map fst
                    let routeValues = matches |> List.head |> snd

                    let hasExplicitOptions =
                        siblings
                        |> List.exists (fun ep ->
                            let meta = ep.Metadata.GetMetadata<HttpMethodMetadata>()
                            not (isNull meta) && meta.HttpMethods |> Seq.exists (fun m -> m = "OPTIONS"))

                    if hasExplicitOptions then
                        logger.LogDebug("Explicit OPTIONS handler found for {Path}, passing through", ctx.Request.Path)
                        do! next.Invoke(ctx)
                    else
                        let! resolution = tryResolveStateAwareAffordance ctx siblings routeValues

                        match resolution with
                        | AffordanceResolution.Found preComputed ->
                            ctx.Response.StatusCode <- 204
                            ctx.Response.Headers["Allow"] <- preComputed.AllowHeaderValue

                            if preComputed.HasTemplateLinks then
                                ctx.Response.Headers["Link"] <- resolveTemplateLinks ctx preComputed.LinkHeaderValues
                            else
                                ctx.Response.Headers["Link"] <- preComputed.LinkHeaderValues
                        | AffordanceResolution.StateNotResolved ->
                            // Resource exists but current state could not be determined.
                            // 404 per Fielding: returning superset Allow is misleading.
                            ctx.Response.StatusCode <- 404
                            ctx.Response.ContentType <- "text/plain"

                            do!
                                ctx.Response.WriteAsync(
                                    "Resource state could not be resolved. The URI is valid but the instance state is unknown."
                                )
                        | AffordanceResolution.NoStatechart ->
                            let routeTemplate = (List.head siblings).RoutePattern.RawText

                            let struct (allowValue, linkValues) =
                                match routeLevelCache.Value.TryGetValue(routeTemplate) with
                                | true, cached -> cached
                                | false, _ -> struct (StringValues.Empty, StringValues.Empty)

                            ctx.Response.StatusCode <- 204
                            ctx.Response.Headers["Allow"] <- allowValue

                            if linkValues.Count > 0 then
                                ctx.Response.Headers["Link"] <- linkValues
            }
            :> Task
