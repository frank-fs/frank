namespace Frank.Discovery

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Frank.Builder

/// Middleware that serves a pre-computed JSON Home document at GET /
/// when the client sends Accept: application/json-home (exact match).
/// Positioned before routing to avoid route conflicts with user-defined root resources.
///
/// Receives EndpointDataSource via DI constructor injection. Lazily computes the
/// JSON Home document on first request (after all endpoints are finalized) and
/// caches the result forever.
type JsonHomeMiddleware(next: RequestDelegate, dataSource: EndpointDataSource) =

    static let jsonHomeMediaType = "application/json-home"

    // Lazy computation: built on first request, cached forever
    let mutable cachedJson: string = null
    let mutable cachedDescribedByUrl: string option = None
    let lockObj = obj ()

    let ensureComputed () =
        if isNull cachedJson then
            lock lockObj (fun () ->
                if isNull cachedJson then
                    let metadata: JsonHomeMetadata option = None
                    let assemblyName =
                        let asm = System.Reflection.Assembly.GetEntryAssembly()
                        if isNull asm then "Frank" else asm.GetName().Name
                    let input = JsonHomeProjection.project dataSource metadata assemblyName
                    cachedDescribedByUrl <- input.DescribedByUrl
                    cachedJson <- JsonHomeDocument.build input)

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
        if ctx.Request.Method <> HttpMethods.Get then
            next.Invoke(ctx)
        elif ctx.Request.Path.Value <> "/" then
            next.Invoke(ctx)
        elif not (isJsonHomeAccept ctx) then
            next.Invoke(ctx)
        else
            ensureComputed ()

            ctx.Response.StatusCode <- 200
            ctx.Response.ContentType <- jsonHomeMediaType
            ctx.Response.Headers["Vary"] <- "Accept"
            ctx.Response.Headers["Cache-Control"] <- "max-age=3600"

            match cachedDescribedByUrl with
            | Some url ->
                ctx.Response.Headers.Append("Link", $"<{url}>; rel=\"describedby\"")
            | None -> ()

            ctx.Response.WriteAsync(cachedJson)
