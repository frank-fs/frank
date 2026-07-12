module Frank.Discovery.DiscoveryMiddleware

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Template
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Primitives

/// Build JSON Home resource entries from live endpoints.
/// Endpoints carrying ResourceRelationMetadata contribute to one merged entry per
/// (Relation, Href) pair — a resource with both GET and POST produces a single entry
/// with allow ⊇ {GET, HEAD, OPTIONS, POST}. HEAD is added when GET is present;
/// OPTIONS is always added (RFC 7231 §7.4.1).
/// Returns a list that may contain multiple entries with the same Relation when two
/// distinct hrefs share a relation IRI; caller is responsible for deduplication.
/// resourceHrefVars maps each relation IRI to its template-variable meaning IRIs.
let homeResourcesFromEndpoints
    (resourceHrefVars: Map<string, Map<string, string>>)
    (dataSource: EndpointDataSource)
    : JsonHomeResource list =
    let addHead (methods: string list) =
        if List.contains "GET" methods && not (List.contains "HEAD" methods) then
            "HEAD" :: methods
        else
            methods

    let addOptions (methods: string list) =
        if not (List.contains "OPTIONS" methods) then
            "OPTIONS" :: methods
        else
            methods

    dataSource.Endpoints
    |> Seq.choose (fun ep ->
        match ep with
        | :? RouteEndpoint as re ->
            let relBox = ep.Metadata.GetMetadata<ResourceRelationMetadata>() |> box

            if relBox = null then
                None
            else
                let relMeta = relBox |> unbox<ResourceRelationMetadata>

                match ep.Metadata.GetMetadata<HttpMethodMetadata>() with
                | null -> None
                | methodMeta -> Some(relMeta.Relation, re.RoutePattern.RawText, methodMeta.HttpMethods |> Seq.toList)
        | _ -> None)
    |> Seq.groupBy (fun (relation, href, _) -> (relation, href))
    |> Seq.map (fun ((relation, href), entries) ->
        let allMethods =
            entries
            |> Seq.collect (fun (_, _, methods) -> methods)
            |> Seq.distinct
            |> Seq.toList
            |> addHead
            |> addOptions
            |> List.sort

        let varMeanings =
            resourceHrefVars |> Map.tryFind relation |> Option.defaultValue Map.empty

        { Relation = relation
          Href = href
          Allow = allMethods
          HrefVars = varMeanings })
    |> Seq.toList

/// Static discovery for the application:
///  - OPTIONS → `Allow` (methods from matching endpoints + HEAD + OPTIONS) + `Link rel="describedby"`
///  - GET ProfileUri → ALPS profile (application/alps+json)
///  - GET HomeRoute with `Accept: application/json-home` → JSON Home directory
/// Anything else falls through. Runs after UseRouting, before endpoint execution.
type DiscoveryMiddleware
    (
        next: RequestDelegate,
        config: DiscoveryConfig,
        endpointDataSource: EndpointDataSource,
        logger: ILogger<DiscoveryMiddleware>
    ) =

    // Dedup by relation at the middleware boundary where the logger lives (Holzmann 14:
    // surface side-effects at the call site). JSON Home 'resources' is keyed by relation
    // IRI — one entry per relation per spec. Two distinct hrefs sharing a relation IRI is
    // a configuration error; first-registered href wins with a LogWarning.
    // Computed once via Lazy<_> (F3: endpoint set is fixed after startup).
    let buildHomeResources () =
        let all = homeResourcesFromEndpoints config.ResourceHrefVars endpointDataSource

        all
        |> List.groupBy (fun r -> r.Relation)
        |> List.map (fun (_, rs) ->
            if rs.Length > 1 then
                let kept = rs.Head

                for dropped in rs.Tail do
                    logger.LogWarning(
                        "DiscoveryMiddleware: relation '{Relation}' registered with multiple hrefs — keeping '{KeptHref}', dropping '{DroppedHref}'. Register each resource with a unique relation IRI.",
                        kept.Relation,
                        kept.Href,
                        dropped.Href
                    )

                kept
            else
                rs.Head)

    let cachedHomeResources = lazy (buildHomeResources ())

    let methodsForPath (requestPath: string) =
        let pathString = PathString(requestPath)

        endpointDataSource.Endpoints
        |> Seq.choose (fun ep ->
            match ep with
            | :? RouteEndpoint as re ->
                let raw = re.RoutePattern.RawText
                let pattern = if raw.StartsWith('/') then raw.TrimStart('/') else raw
                // TemplateMatcher is not thread-safe — construct per request.
                let matcher = TemplateMatcher(TemplateParser.Parse(pattern), RouteValueDictionary())

                if matcher.TryMatch(pathString, RouteValueDictionary()) then
                    match ep.Metadata.GetMetadata<HttpMethodMetadata>() with
                    | null -> None
                    | meta -> Some(meta.HttpMethods |> Seq.toList)
                else
                    None
            | _ -> None)
        |> Seq.concat
        |> Seq.distinct
        |> Seq.toList

    let handleOptions (ctx: HttpContext) : Task =
        let methods = methodsForPath ctx.Request.Path.Value

        let methods =
            if List.contains "GET" methods && not (List.contains "HEAD" methods) then
                "HEAD" :: methods
            else
                methods

        // RFC 7231 §7.4.1: OPTIONS is always handled by the server, so always advertise it.
        let methods =
            if not (List.contains "OPTIONS" methods) then
                "OPTIONS" :: methods
            else
                methods

        if not methods.IsEmpty then
            ctx.Response.Headers.["Allow"] <- StringValues(methods |> List.sort |> List.toArray)

        let profileLink = sprintf "<%s>; rel=\"describedby\"" config.ProfileUri
        ctx.Response.Headers.Append("Link", profileLink)

        for link in config.DescribedByLinks do
            ctx.Response.Headers.Append("Link", link)

        ctx.Response.StatusCode <- 200
        Task.CompletedTask

    let acceptsJsonHome (ctx: HttpContext) =
        match ctx.Request.Headers.TryGetValue "Accept" with
        | true, v -> v.ToString().Contains "application/json-home"
        | _ -> false

    member _.Invoke(ctx: HttpContext) : Task =
        let path = ctx.Request.Path.Value
        let isGet = HttpMethods.IsGet ctx.Request.Method

        if HttpMethods.IsOptions ctx.Request.Method then
            handleOptions ctx
        elif isGet && path = config.ProfileUri then
            ctx.Response.ContentType <- "application/alps+json"
            ctx.Response.WriteAsync(AlpsSerializer.serialize config.AlpsDescriptors)
        elif isGet && path = config.HomeRoute && acceptsJsonHome ctx then
            ctx.Response.Headers.Append("Vary", "Accept")
            ctx.Response.ContentType <- "application/json-home"
            ctx.Response.WriteAsync(JsonHomeSerializer.serialize cachedHomeResources.Value)
        else
            next.Invoke ctx
