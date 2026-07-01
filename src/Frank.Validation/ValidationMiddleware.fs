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
open VDS.RDF.Shacl.Validation
open Frank.Semantic

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

    let respond400 (detail: string) (ctx: HttpContext) : Task =
        Frank.ProblemJson.write ctx 400 "about:blank" "Bad Request" detail

    let respond422 (reportJsonLd: string) (ctx: HttpContext) : Task =
        ctx.Response.StatusCode <- 422
        ctx.Response.ContentType <- "application/ld+json; profile=\"http://www.w3.org/ns/shacl#\""
        let linkValue = "<http://www.w3.org/ns/shacl#>; rel=\"describedby\""
        ctx.Response.Headers.Append("Link", StringValues(linkValue))
        ctx.Response.WriteAsync(reportJsonLd)

module private HostRelative =

    let private resolveProps (props: (Uri * string * string option) list) (origin: string) : ShapeDecl list =
        props
        |> List.map (fun (classUri, relPath, pattern) ->
            RecordShape(
                classUri,
                [ { Path = Uri(origin + relPath)
                    Datatype = None
                    MinCount = 1
                    MaxCount = Some 1
                    Pattern = pattern } ]
            ))

    let validateDynamic (props: (Uri * string * string option) list) (origin: string) (data: IGraph) : Report option =
        if props.IsEmpty then
            None
        else
            use sg = Shapes.toShapesGraph (resolveProps props origin)
            Some(Validator.validate sg data)

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
        let staticReport = Validator.validate config.Shapes data

        if not staticReport.Conforms then
            logger.LogDebug("ValidationMiddleware: static shapes reject body, returning 422")
            ValidationRespond.respond422 (JsonLdBody.serializeReportJsonLd staticReport.Normalised) ctx
        else
            let origin = $"{ctx.Request.Scheme}://{ctx.Request.Host}"

            let dynReport =
                HostRelative.validateDynamic config.HostRelativeProperties origin data

            match dynReport with
            | None ->
                logger.LogDebug("ValidationMiddleware: body conforms, passing through")
                next.Invoke ctx
            | Some r when r.Conforms ->
                logger.LogDebug("ValidationMiddleware: body conforms, passing through")
                next.Invoke ctx
            | Some r ->
                logger.LogDebug("ValidationMiddleware: host-relative shapes reject body, returning 422")
                ValidationRespond.respond422 (JsonLdBody.serializeReportJsonLd r.Normalised) ctx

    member _.InvokeAsync(ctx: HttpContext) : Task =
        if not (JsonLdBody.isLdJson ctx) then
            next.Invoke ctx
        else
            task {
                Frank.RequestBodyBuffer.enable config.MaxBodyBytes ctx.Request

                let! bodyOpt =
                    task {
                        try
                            let! body = JsonLdBody.readBody ctx
                            return Some body
                        with :? IOException as ex ->
                            logger.LogDebug(ex, "ValidationMiddleware: body exceeded MaxBodyBytes limit")
                            return None
                    }

                match bodyOpt with
                | None -> do! Frank.RequestBodyBuffer.respond413 ctx
                | Some body ->
                    ctx.Request.Body.Position <- 0L

                    match JsonLdBody.parseToGraph config.ContextLoader body with
                    | Error ex ->
                        logger.LogDebug(ex, "ValidationMiddleware: failed to parse ld+json body")
                        do! ValidationRespond.respond400 ex.Message ctx
                    | Ok data -> do! validateAndRespond ctx data
            }
