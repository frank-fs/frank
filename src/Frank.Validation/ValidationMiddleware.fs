namespace Frank.Validation

open System
open System.IO
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Microsoft.Net.Http.Headers
open VDS.RDF
open VDS.RDF.JsonLd
open VDS.RDF.Parsing

module private JsonLdBody =

    let isLdJson (ctx: HttpContext) =
        let ct = ctx.Request.ContentType

        match MediaTypeHeaderValue.TryParse(ct) with
        | true, parsed -> parsed.MediaType.Equals("application/ld+json", StringComparison.OrdinalIgnoreCase)
        | _ -> false

    let readBody (ctx: HttpContext) : Task<string> =
        task {
            use reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen = true)
            return! reader.ReadToEndAsync()
        }

    let mergeGraphs (store: TripleStore) : IGraph =
        let merged = new Graph() :> IGraph

        for g in store.Graphs do
            merged.Merge(g) |> ignore

        merged

    let parseToGraph (loader: JsonLdDocumentLoader) (body: string) : Result<IGraph, exn> =
        let options = JsonLdProcessorOptions()
        options.DocumentLoader <- loader
        let parser = JsonLdParser(options)

        try
            use store = new TripleStore()
            use reader = new StringReader(body)
            parser.Load(store :> ITripleStore, reader)
            Ok(mergeGraphs store)
        with ex ->
            Error ex

    let serializeReportJsonLd (graph: IGraph) : string =
        Frank.Semantic.RdfSerialization.serializeGraphJsonLd graph

module private ValidationRespond =

    let respond400 (ctx: HttpContext) : Task =
        ctx.Response.StatusCode <- 400
        ctx.Response.ContentType <- "text/plain"
        ctx.Response.WriteAsync("Invalid JSON-LD body")

    let respond422 (reportJsonLd: string) (ctx: HttpContext) : Task =
        ctx.Response.StatusCode <- 422
        ctx.Response.ContentType <- "application/ld+json"
        ctx.Response.WriteAsync(reportJsonLd)

type ValidationMiddleware(next: RequestDelegate, config: ValidationConfig, logger: ILogger<ValidationMiddleware>) =

    do
        if isNull (box config.Shapes) then
            invalidArg (nameof config) "ValidationConfig.Shapes must not be null"

        if isNull (box config.ContextLoader) then
            invalidArg (nameof config) "ValidationConfig.ContextLoader must not be null"

    let validateAndRespond (ctx: HttpContext) (data: IGraph) : Task =
        use _ = data
        let report = Validator.validate config.Shapes data

        if report.Conforms then
            logger.LogDebug("ValidationMiddleware: body conforms, passing through")
            next.Invoke ctx
        else
            logger.LogDebug("ValidationMiddleware: body does not conform, returning 422")
            ValidationRespond.respond422 (JsonLdBody.serializeReportJsonLd report.Normalised) ctx

    member _.Invoke(ctx: HttpContext) : Task =
        if not (JsonLdBody.isLdJson ctx) then
            next.Invoke ctx
        else
            task {
                ctx.Request.EnableBuffering()
                let! body = JsonLdBody.readBody ctx
                ctx.Request.Body.Position <- 0L

                match JsonLdBody.parseToGraph config.ContextLoader body with
                | Error ex ->
                    logger.LogDebug(ex, "ValidationMiddleware: failed to parse ld+json body")
                    do! ValidationRespond.respond400 ctx
                | Ok data -> do! validateAndRespond ctx data
            }
