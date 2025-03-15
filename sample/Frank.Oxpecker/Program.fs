open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Oxpecker
open Oxpecker.ViewEngine
open Oxpecker.ViewEngine.Aria
open Oxpecker.Htmx
open FSharp.Control
open Frank.Builder
open Frank.Oxpecker.Extensions

// ---------------------------------
// Web app
// ---------------------------------

let handler0: EndpointHandler =
    fun (ctx: HttpContext) ->
        let name = ctx.TryGetRouteValue("name") |> Option.defaultValue "world"
        ctx.WriteText($"Hello, %s{name}")

let streamingJson: EndpointHandler =
    fun (ctx: HttpContext) ->
        let values =
            taskSeq {
                for i in 1..10 do
                    do! Task.Delay(500)
                    yield {| Id = i; Name = $"Name {i}" |}
            }

        jsonChunked values ctx

let streamingHtml1: EndpointHandler =
    fun (ctx: HttpContext) ->
        let html =
            html () {
                head () {
                    script (src = "https://unpkg.com/htmx.org@1.9.12")
                    script (src = "https://unpkg.com/htmx.ext...chunked-transfer@1.0.4/dist/index.js") // workaround, see https://github.com/bigskysoftware/htmx/issues/1911
                }

                body (style = "width: 800px; margin: 0 auto", hxExt = "chunked-transfer") {
                    h1 (style = "text-align: center; color: blue") { "HTML Streaming example" }
                    h2 (hxGet = "/streamHtml2", hxTarget = "this", hxTrigger = "load")
                }
            }

        htmlView html ctx

let streamingHtml2: EndpointHandler =
    fun (ctx: HttpContext) ->
        let values =
            taskSeq {
                for ch in "Hello world using Oxpecker streaming!" do
                    do! Task.Delay(20)
                    ch |> string |> raw
            }

        htmlChunked values ctx

let home =
    resource "/" {
        name "streamHtml1"

        get streamingHtml1
    }

let streamHtml2 =
    resource "streamHtml2" {
        name "Streaming HTML 2"

        get streamingHtml2
    }

let streamJson =
    resource "streamJson" {
        name "Streaming JSON"

        get streamingJson
    }

let helloName =
    resource "hello/{name}" {
        name "Hello"

        get handler0
    }

// ---------------------------------
// Error Handling
// ---------------------------------

let errorView errorCode (errorText: string) =
    html () {
        body (style = "width: 800px; margin: 0 auto") {
            h1 (style = "text-align: center; color: red") { raw $"Error <i>%d{errorCode}</i>" }
            p(ariaErrorMessage = "err1").on ("click", "console.log('clicked on error')") { errorText }
        }
    }

let notFoundHandler (ctx: HttpContext) =
    let logger = ctx.GetLogger()
    logger.LogWarning("Unhandled 404 error")
    ctx.Response.StatusCode <- 404
    ctx.WriteHtmlView(errorView 404 "Page not found!")

let errorHandler (ctx: HttpContext) (next: RequestDelegate) =
    task {
        try
            return! next.Invoke(ctx)
        with
        | :? ModelBindException
        | :? RouteParseException as ex ->
            let logger = ctx.GetLogger()
            logger.LogWarning(ex, "Unhandled 400 error")
            ctx.SetStatusCode StatusCodes.Status400BadRequest
            return! ctx.WriteHtmlView(errorView 400 (string ex))
        | ex ->
            let logger = ctx.GetLogger()
            logger.LogWarning(ex, "Unhandled 500 error")
            ctx.SetStatusCode StatusCodes.Status500InternalServerError
            return! ctx.WriteHtmlView(errorView 500 (string ex))
    }
    :> Task

// ---------------------------------
// Config and Main
// ---------------------------------

let configureCors (builder: CorsPolicyBuilder) =
    builder.WithOrigins([| "http://localhost:5017"; "https://unpkg.com" |]).AllowAnyMethod().AllowAnyHeader()
    |> ignore

let configureLogging (builder: ILoggingBuilder) = builder.AddConsole().AddDebug()

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults

        logging configureLogging
        service (fun services -> services.AddOxpecker())

        useCors configureCors
        useErrorHandler errorHandler

        plug HttpsPolicyBuilderExtensions.UseHttpsRedirection

        resource home
        resource helloName
        resource streamHtml2
        resource streamJson
    }

    0
