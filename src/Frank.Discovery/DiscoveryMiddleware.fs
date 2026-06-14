module Frank.Discovery.DiscoveryMiddleware

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Template
open Microsoft.Extensions.Primitives

/// Build JSON Home resource entries from live endpoints at request time.
/// Endpoints carrying ResourceRelationMetadata contribute one entry each; all
/// others are skipped. HEAD is added when GET is present, matching OPTIONS logic.
let homeResourcesFromEndpoints (dataSource: EndpointDataSource) : JsonHomeResource list =
    let addHead (methods: string list) =
        if List.contains "GET" methods && not (List.contains "HEAD" methods) then
            "HEAD" :: methods
        else
            methods

    let toResource (re: RouteEndpoint) (meta: ResourceRelationMetadata) (methodMeta: HttpMethodMetadata) =
        let allow = methodMeta.HttpMethods |> Seq.toList |> addHead |> List.sort

        { Relation = meta.Relation
          Href = re.RoutePattern.RawText
          Allow = allow }

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
                | methodMeta -> Some(toResource re relMeta methodMeta)
        | _ -> None)
    |> Seq.toList

/// Static discovery for the application:
///  - OPTIONS → `Allow` (methods from matching endpoints) + `Link rel="describedby"`
///  - GET ProfileUri → ALPS profile (application/alps+json)
///  - GET HomeRoute with `Accept: application/json-home` → JSON Home directory
/// Anything else falls through. Runs after UseRouting, before endpoint execution.
type DiscoveryMiddleware(next: RequestDelegate, config: DiscoveryConfig, endpointDataSource: EndpointDataSource) =

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
            ctx.Response.ContentType <- "application/json-home+json"
            ctx.Response.WriteAsync(JsonHomeSerializer.serialize (homeResourcesFromEndpoints endpointDataSource))
        else
            next.Invoke ctx
