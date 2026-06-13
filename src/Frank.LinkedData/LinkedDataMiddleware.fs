module Frank.LinkedData.LinkedDataMiddleware

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open VDS.RDF

let private supportedTypes =
    [ "application/ld+json", RdfSerializer.JsonLd
      "text/turtle", RdfSerializer.Turtle
      "application/rdf+xml", RdfSerializer.RdfXml ]

/// Configuration for the linked data serving middleware.
type LinkedDataConfig =
    { Graph: IGraph; JsonLdContext: string }

/// Middleware that intercepts requests with RDF Accept headers and serves
/// the resource graph in the requested format. Returns 406 when Accept is
/// explicitly set to an unsupported type (not empty, not *, not text/html).
type LinkedDataMiddleware(next: RequestDelegate, config: LinkedDataConfig) =

    member _.Invoke(ctx: HttpContext) : Task =
        let acceptHeader =
            if ctx.Request.Headers.ContainsKey("Accept") then
                ctx.Request.Headers["Accept"].ToString()
            else
                ""

        let matchedFormat =
            supportedTypes
            |> List.tryFind (fun (mediaType, _) -> acceptHeader.Contains(mediaType))

        match matchedFormat with
        | Some(mediaType, format) ->
            task {
                let serialized = RdfSerializer.serialize format config.Graph
                ctx.Response.StatusCode <- 200
                ctx.Response.ContentType <- mediaType
                do! ctx.Response.WriteAsync(serialized)
            }
            :> Task
        | None ->
            let hasExplicitUnsupportedAccept =
                not (String.IsNullOrEmpty acceptHeader)
                && acceptHeader <> "*/*"
                && not (acceptHeader.Contains("text/html"))
                && not (acceptHeader.Contains("*/*"))

            if hasExplicitUnsupportedAccept then
                ctx.Response.StatusCode <- 406
                Task.CompletedTask
            else
                next.Invoke(ctx)
