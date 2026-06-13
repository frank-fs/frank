module Frank.Discovery.DiscoveryMiddleware

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing

/// Intercepts OPTIONS requests. Collects allowed methods from all registered
/// endpoints whose route pattern matches the request path, then adds Allow and
/// Link headers and returns 200.
///
/// EndpointDataSource is injected via constructor so ASP.NET Core provides the
/// composite data source (populated by app.MapGet/resource CE calls).
type OptionsEnricherMiddleware(next: RequestDelegate, config: DiscoveryConfig, endpointDataSource: EndpointDataSource) =

    member _.Invoke(ctx: HttpContext) : Task =
        if not (ctx.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase)) then
            next.Invoke(ctx)
        else
            task {
                let requestPath = ctx.Request.Path.Value

                let methods =
                    endpointDataSource.Endpoints
                    |> Seq.choose (fun (ep: Endpoint) ->
                        match ep with
                        | :? RouteEndpoint as re ->
                            // TemplateParser expects no leading '/'; PathString requires one.
                            let rawText = re.RoutePattern.RawText

                            let pattern =
                                if rawText.StartsWith('/') then
                                    rawText.TrimStart('/')
                                else
                                    rawText

                            let pathString = PathString(requestPath)

                            let matcher =
                                Microsoft.AspNetCore.Routing.Template.TemplateMatcher(
                                    Microsoft.AspNetCore.Routing.Template.TemplateParser.Parse(pattern),
                                    RouteValueDictionary()
                                )

                            let values = RouteValueDictionary()

                            if matcher.TryMatch(pathString, values) then
                                let meta = ep.Metadata.GetMetadata<HttpMethodMetadata>()

                                if isNull (box meta) then
                                    None
                                else
                                    Some(meta.HttpMethods |> Seq.toList)
                            else
                                None
                        | _ -> None)
                    |> Seq.concat
                    |> Seq.distinct
                    |> Seq.toList

                // RFC 9110 §9.3.2: HEAD must be available wherever GET is.
                let methods =
                    if methods |> List.contains "GET" && not (methods |> List.contains "HEAD") then
                        "HEAD" :: methods
                    else
                        methods

                if not methods.IsEmpty then
                    ctx.Response.Headers["Allow"] <- String.concat ", " (methods |> List.sort)

                let profileUri = config.ProfileBaseUri + "/profile"
                ctx.Response.Headers.Append("Link", $"<{profileUri}>; rel=\"profile\"")

                for KeyValue(_, links) in config.DescribedByLinks do
                    for link in links do
                        ctx.Response.Headers.Append("Link", link)

                ctx.Response.StatusCode <- 200
            }
            :> Task
