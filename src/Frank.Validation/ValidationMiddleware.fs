module Frank.Validation.ValidationMiddleware

open System
open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Parsing.Handlers
open VDS.RDF.Shacl

/// Middleware that validates application/ld+json request bodies against a ShapesGraph.
/// Passes through all other content types without validation.
type ValidationMiddleware(next: RequestDelegate, shapesGraph: ShapesGraph) =

    member _.Invoke(ctx: HttpContext) : Task =
        let contentType = ctx.Request.ContentType
        let isLinkedData =
            not (String.IsNullOrEmpty contentType)
            && contentType.Contains("application/ld+json")

        if not isLinkedData then
            next.Invoke(ctx)
        else
            task {
                ctx.Request.EnableBuffering()

                use reader = new StreamReader(ctx.Request.Body, leaveOpen = true)
                let! bodyText = reader.ReadToEndAsync()
                ctx.Request.Body.Position <- 0L

                let dataGraph = new Graph()
                let parseOk =
                    try
                        let parser = JsonLdParser()
                        use bodyStream = new MemoryStream(Text.Encoding.UTF8.GetBytes(bodyText))
                        use sr = new StreamReader(bodyStream)
                        let handler = GraphHandler(dataGraph)
                        parser.Load(handler, sr)
                        true
                    with _ ->
                        false

                if not parseOk then
                    ctx.Response.StatusCode <- 400
                    do! ctx.Response.WriteAsync("Invalid JSON-LD body")
                else
                    let report = shapesGraph.Validate(dataGraph)

                    if report.Conforms then
                        do! next.Invoke(ctx)
                    else
                        ctx.Response.StatusCode <- 422
                        ctx.Response.ContentType <- "application/problem+json"
                        let reportJson = ValidationReport.serialize report
                        do! ctx.Response.WriteAsync(reportJson)
            }
            :> Task
