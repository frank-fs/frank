namespace Frank.Discovery

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.Logging
open Frank.Builder

/// Middleware that serves a pre-computed JSON Home document at GET or HEAD /
/// when the client sends Accept: application/json-home (exact match).
/// Positioned before routing to avoid route conflicts with user-defined root resources.
///
/// Receives EndpointDataSource via DI constructor injection. Lazily computes the
/// JSON Home document on first request (after all endpoints are finalized) and
/// caches the result forever.
type JsonHomeMiddleware
    (
        next: RequestDelegate,
        dataSource: EndpointDataSource,
        serviceProvider: System.IServiceProvider,
        logger: ILogger<JsonHomeMiddleware>
    ) =

    static let jsonHomeMediaType = "application/json-home"

    // Lazy computation: thread-safe via Lazy<T> (ExecutionAndPublication mode).
    // Built on first request after all endpoints are finalized, cached forever.
    let computed =
        lazy
            (let metadata =
                match serviceProvider.GetService(typeof<JsonHomeMetadata>) with
                | :? JsonHomeMetadata as m -> Some m
                | _ -> None

             let assemblyName =
                 let asm = System.Reflection.Assembly.GetEntryAssembly()
                 if isNull asm then "Frank" else asm.GetName().Name

             let result = JsonHomeProjection.project dataSource metadata assemblyName

             if result.UsedFallback then
                 logger.LogWarning(
                     "No resources are designated as entry points via 'entryPoint'. "
                     + "All non-internal resources will appear in the JSON Home document. "
                     + "Use the 'entryPoint' CE operation on resources that should be discoverable."
                 )

             let linkHeader =
                 result.Input.DescribedByUrl
                 |> Option.map (fun url -> $"<{url}>; rel=\"describedby\"")

             linkHeader, JsonHomeDocument.build result.Input)

    let isJsonHomeAccept (ctx: HttpContext) =
        let acceptHeaders = ctx.Request.GetTypedHeaders().Accept

        if isNull acceptHeaders || acceptHeaders.Count = 0 then
            false
        else
            acceptHeaders
            |> Seq.exists (fun header ->
                not header.MatchesAllTypes
                && not header.MatchesAllSubTypes
                && header.MediaType.Equals(jsonHomeMediaType, System.StringComparison.OrdinalIgnoreCase))

    member _.Invoke(ctx: HttpContext) : Task =
        let isGet = HttpMethods.IsGet(ctx.Request.Method)
        let isHead = HttpMethods.IsHead(ctx.Request.Method)

        if not isGet && not isHead then
            next.Invoke(ctx)
        elif ctx.Request.Path.Value <> "/" then
            next.Invoke(ctx)
        elif not (isJsonHomeAccept ctx) then
            next.Invoke(ctx)
        else
            let linkHeader, json = computed.Value

            ctx.Response.StatusCode <- 200
            ctx.Response.ContentType <- jsonHomeMediaType
            ctx.Response.Headers["Vary"] <- "Accept"
            ctx.Response.Headers["Cache-Control"] <- "max-age=3600"

            match linkHeader with
            | Some link -> ctx.Response.Headers.Append("Link", link)
            | None -> ()

            if isHead then
                Task.CompletedTask
            else
                ctx.Response.WriteAsync(json)
