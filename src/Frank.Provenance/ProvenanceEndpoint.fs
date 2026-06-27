module Frank.Provenance.ProvenanceEndpoint

open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives

let private writeProblemJson (ctx: HttpContext) (title: string) (detail: string) : Task =
    ctx.Response.StatusCode <- 400
    ctx.Response.ContentType <- "application/problem+json"
    let opts = JsonWriterOptions(Indented = false)
    use outStream = new MemoryStream()
    use jsonWriter = new Utf8JsonWriter(outStream, opts)
    jsonWriter.WriteStartObject()
    jsonWriter.WriteString("type", "about:blank")
    jsonWriter.WriteString("title", title)
    jsonWriter.WriteNumber("status", 400)
    jsonWriter.WriteString("detail", detail)
    jsonWriter.WriteEndObject()
    jsonWriter.Flush()
    let body = Encoding.UTF8.GetString(outStream.ToArray())
    ctx.Response.WriteAsync(body)

let handle (store: IProvenanceStore) (ctx: HttpContext) : Task =
    if isNull (box store) then
        invalidArg (nameof store) "store must not be null"

    let resource = ctx.Request.Query.["resource"]

    if StringValues.IsNullOrEmpty resource then
        writeProblemJson ctx "Missing required query parameter" "provenance query requires a 'resource' parameter"
    else
        let records = store.QueryByResource(resource.ToString())
        ctx.Response.StatusCode <- 200
        ctx.Response.ContentType <- "application/ld+json"
        ctx.Response.WriteAsync(ProvenanceGraph.listToJsonLd records)
