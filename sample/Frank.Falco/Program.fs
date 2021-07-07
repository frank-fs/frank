module Frank.Falco.App

open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Diagnostics
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Logging
open Falco
open Frank.Builder
open Frank.Falco.Extensions

// ---------------------------------
// Models
// ---------------------------------

type Message = { Text : string }

// ---------------------------------
// Views
// ---------------------------------

module Views =
    open Falco.Markup
    
    let layout (content: XmlNode list) =
        Elem.html [] [
            Elem.head [] [
                Elem.title []  [ Text.raw "Frank.Falco" ]
                Elem.link [ Attr.rel  "stylesheet"; Attr.type' "text/css"; Attr.href "/main.css" ]
            ]
            Elem.body [] content
        ]

    let partial () =
        Elem.h1 [] [ Text.raw "Frank.Falco" ]

    let index (model : Message) =
        [
            partial()
            Elem.p [] [ Text.raw model.Text ]
        ] |> layout

// ---------------------------------
// Web app
// ---------------------------------

let indexHandler (name : string) : HttpHandler =    
    let greetings = sprintf "Hello %s, from Falco!" name
    let model     = { Text = greetings }
    let view      = Views.index model
    Response.ofHtml view

let helloWorld =
    resource "/" {
        name "Hello World"
        get (indexHandler "world")
    }

let indexRouteHandler : HttpHandler =
    let routeBinder (r : RouteCollectionReader) =
        r.GetString "name" "world"

    Request.mapRoute routeBinder indexHandler

let helloName =
    resource "/hello/{name}" {
        name "Hello Name"
        get indexRouteHandler
    }

// ---------------------------------
// Error handler
// ---------------------------------

let errorHandler : HttpHandler =
    fun ctx ->
        let exceptionHandlerFeature = ctx.Features.Get<IExceptionHandlerPathFeature>();
        let ex = exceptionHandlerFeature.Error

        let logger = ctx.GetLogger "exception handler"
        logger.LogError(ex, "An unhandled exception has occurred while executing the request.")

        Response.withStatusCode 500 ctx
        |> Response.ofPlainText ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureCors (builder: CorsPolicyBuilder) =
    builder.WithOrigins("http://localhost:8080").AllowAnyMethod().AllowAnyHeader()
    |> ignore

let configureLogging (builder: ILoggingBuilder) =
    builder.AddFilter(fun l -> l.Equals LogLevel.Error).AddConsole().AddDebug()

[<EntryPoint>]
let main args =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot = Path.Combine(contentRoot, "WebRoot")

    webHost args {
        configure (fun bldr -> bldr.UseKestrel().UseContentRoot(contentRoot).UseIISIntegration().UseWebRoot(webRoot))
        logging configureLogging
        useCors configureCors
        
        service (fun services -> services.AddFalco())

        plugWhen isDevelopment DeveloperExceptionPageExtensions.UseDeveloperExceptionPage
        plugWhenNot isDevelopment (FalcoExtensions.UseFalcoExceptionHandler errorHandler)

        plug HttpsPolicyBuilderExtensions.UseHttpsRedirection
        plug StaticFileExtensions.UseStaticFiles

        resource helloWorld
        resource helloName
    }

    0