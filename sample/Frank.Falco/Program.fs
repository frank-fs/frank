module Frank.Falco.App

open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.Logging
open Falco
open Frank.Builder
open Frank.Falco.Extensions

// ---------------------------------
// Models
// ---------------------------------

type Message =
    {
        Text : string
    }

// ---------------------------------
// Views
// ---------------------------------

module Views =
    open Falco.ViewEngine

    let layout (content: XmlNode list) =
        html [] [
            head [] [
                title []  [ Text "Frank.Falco" ]
                link [ _rel  "stylesheet"
                       _type "text/css"
                       _href "/main.css" ]
            ]
            body [] content
        ]

    let partial () =
        h1 [] [ Text "Frank.Falco" ]

    let index (model : Message) =
        [
            partial()
            p [] [ Text model.Text ]
        ] |> layout

// ---------------------------------
// Web app
// ---------------------------------

let indexHandler (name : string) =
    let greetings = sprintf "Hello %s, from Falco!" name
    let model     = { Text = greetings }
    let view      = Views.index model
    htmlOut view

let helloWorld =
    resource "/" {
        name "Hello World"
        get (indexHandler "world")
    }

let helloName =
    resource "/hello/{name}" {
        name "Hello Name"
        get (fun next ctx ->
            let name = ctx.GetRouteValue("name") |> string
            indexHandler name next ctx)
    }

// ---------------------------------
// Config and Main
// ---------------------------------

let configureCors (builder:CorsPolicyBuilder) =
    builder.WithOrigins("http://localhost:8080")
           .AllowAnyMethod()
           .AllowAnyHeader()
           |> ignore

let configureLogging (builder:ILoggingBuilder) =
    builder.AddFilter(fun l -> l.Equals LogLevel.Error)
           .AddConsole()
           .AddDebug()

[<EntryPoint>]
let main args =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot     = Path.Combine(contentRoot, "WebRoot")

    webHost args {
        configure (fun bldr ->
            bldr.UseKestrel()
                .UseContentRoot(contentRoot)
                .UseIISIntegration()
                .UseWebRoot(webRoot))
        logging configureLogging
        useCors configureCors

        plugWhen isDevelopment DeveloperExceptionPageExtensions.UseDeveloperExceptionPage

        plug HttpsPolicyBuilderExtensions.UseHttpsRedirection
        plug StaticFileExtensions.UseStaticFiles

        resource helloWorld
        resource helloName
    }

    0