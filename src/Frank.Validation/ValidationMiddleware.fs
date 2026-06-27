namespace Frank.Validation

open System
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Primitives
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

    let private shaclContext = """{"@context":{"sh":"http://www.w3.org/ns/shacl#"}}"""

    let serializeReportJsonLd (graph: IGraph) : string =
        Frank.Semantic.RdfSerialization.serializeGraphJsonLdWithContext graph shaclContext

module private ValidationRespond =

    let private writeProblemJson (ctx: HttpContext) (status: int) (title: string) (detail: string) : Task =
        ctx.Response.StatusCode <- status
        ctx.Response.ContentType <- "application/problem+json"
        let opts = JsonWriterOptions(Indented = false)
        use outStream = new System.IO.MemoryStream()
        use jsonWriter = new Utf8JsonWriter(outStream, opts)
        jsonWriter.WriteStartObject()
        jsonWriter.WriteString("type", "about:blank")
        jsonWriter.WriteString("title", title)
        jsonWriter.WriteNumber("status", status)
        jsonWriter.WriteString("detail", detail)
        jsonWriter.WriteEndObject()
        jsonWriter.Flush()
        let body = System.Text.Encoding.UTF8.GetString(outStream.ToArray())
        ctx.Response.WriteAsync(body)

    let respond400 (detail: string) (ctx: HttpContext) : Task =
        writeProblemJson ctx 400 "Invalid JSON-LD body" detail

    let respond413 (ctx: HttpContext) : Task =
        writeProblemJson ctx 413 "Payload Too Large" "Request body exceeds the configured maximum size"

    let respond422 (reportJsonLd: string) (ctx: HttpContext) : Task =
        ctx.Response.StatusCode <- 422
        ctx.Response.ContentType <- "application/ld+json; profile=\"http://www.w3.org/ns/shacl#\""
        let linkValue = "<http://www.w3.org/ns/shacl#>; rel=\"describedby\""
        ctx.Response.Headers.Append("Link", StringValues(linkValue))
        ctx.Response.WriteAsync(reportJsonLd)

type ValidationMiddleware(next: RequestDelegate, config: ValidationConfig, logger: ILogger<ValidationMiddleware>) =

    do
        if isNull (box config.Shapes) then
            invalidArg (nameof config) "ValidationConfig.Shapes must not be null"

        if isNull (box config.ContextLoader) then
            invalidArg (nameof config) "ValidationConfig.ContextLoader must not be null"

        if config.MaxBodyBytes <= 0L then
            invalidArg (nameof config) "ValidationConfig.MaxBodyBytes must be positive"

    let validateAndRespond (ctx: HttpContext) (data: IGraph) : Task =
        use _ = data
        let report = Validator.validate config.Shapes data

        if report.Conforms then
            logger.LogDebug("ValidationMiddleware: body conforms, passing through")
            next.Invoke ctx
        else
            logger.LogDebug("ValidationMiddleware: body does not conform, returning 422")
            ValidationRespond.respond422 (JsonLdBody.serializeReportJsonLd report.Normalised) ctx

    member _.InvokeAsync(ctx: HttpContext) : Task =
        if not (JsonLdBody.isLdJson ctx) then
            next.Invoke ctx
        else
            task {
                ctx.Request.EnableBuffering(config.MaxBodyBytes)

                try
                    let! body = JsonLdBody.readBody ctx
                    ctx.Request.Body.Position <- 0L

                    match JsonLdBody.parseToGraph config.ContextLoader body with
                    | Error ex ->
                        logger.LogDebug(ex, "ValidationMiddleware: failed to parse ld+json body")
                        do! ValidationRespond.respond400 ex.Message ctx
                    | Ok data -> do! validateAndRespond ctx data
                with :? IOException as ex ->
                    logger.LogDebug(ex, "ValidationMiddleware: body exceeded MaxBodyBytes limit")
                    do! ValidationRespond.respond413 ctx
            }
