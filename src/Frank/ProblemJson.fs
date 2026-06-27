namespace Frank

open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

/// RFC 9457 Problem Details for HTTP APIs.
module ProblemJson =

    let private buildBody (typeUri: string) (title: string) (status: int) (detail: string) : string =
        let opts = JsonWriterOptions(Indented = false)
        use outStream = new MemoryStream()
        use writer = new Utf8JsonWriter(outStream, opts)
        writer.WriteStartObject()
        writer.WriteString("type", typeUri)
        writer.WriteString("title", title)
        writer.WriteNumber("status", status)
        writer.WriteString("detail", detail)
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(outStream.ToArray())

    /// Write an RFC 9457 problem+json response to ctx.
    /// If typeUri is "about:blank" the title SHOULD be the status reason phrase.
    let write (ctx: HttpContext) (status: int) (typeUri: string) (title: string) (detail: string) : Task =
        ctx.Response.StatusCode <- status
        ctx.Response.ContentType <- "application/problem+json"
        let body = buildBody typeUri title status detail
        ctx.Response.WriteAsync(body)
