namespace Frank.Validation

open System
open System.IO
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open VDS.RDF
open VDS.RDF.JsonLd
open VDS.RDF.Parsing

module private JsonLdBody =

    let isLdJson (ctx: HttpContext) =
        let ct = ctx.Request.ContentType

        not (String.IsNullOrEmpty ct)
        && ct.StartsWith("application/ld+json", StringComparison.OrdinalIgnoreCase)

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

    let parseToGraph (loader: ContextLoader) (body: string) : Result<IGraph, exn> =
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
        use store = new TripleStore()
        store.Add(graph) |> ignore
        let sb = StringBuilder()
        use sw = new System.IO.StringWriter(sb)
        let writer = VDS.RDF.Writing.JsonLdWriter()
        writer.Save(store :> ITripleStore, sw :> TextWriter)
        sb.ToString()

type ValidationMiddleware(next: RequestDelegate, config: ValidationConfig, logger: ILogger<ValidationMiddleware>) =

    do
        if isNull (box config.Shapes) then
            invalidArg (nameof config) "ValidationConfig.Shapes must not be null"

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
                    ctx.Response.StatusCode <- 400
                    ctx.Response.ContentType <- "text/plain"
                    do! ctx.Response.WriteAsync("Invalid JSON-LD body")
                | Ok data ->
                    use _ = data
                    let report = Validator.validate config.Shapes data

                    if report.Conforms then
                        logger.LogDebug("ValidationMiddleware: body conforms, passing through")
                        do! next.Invoke ctx
                    else
                        logger.LogDebug("ValidationMiddleware: body does not conform, returning 422")
                        let responseBody = JsonLdBody.serializeReportJsonLd report.Normalised
                        ctx.Response.StatusCode <- 422
                        ctx.Response.ContentType <- "application/ld+json"
                        do! ctx.Response.WriteAsync(responseBody)
            }
