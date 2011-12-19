namespace FSharpMVC3.Core

open System
open System.Net
open System.Net.Http
open System.Web.Routing
open Frank
open Frank.Hosting

type Global() =
    inherit System.Web.HttpApplication()

    let formatters = [| new Microsoft.ApplicationServer.Http.PlainTextFormatter() :> Formatting.MediaTypeFormatter
                        new Formatting.XmlMediaTypeFormatter() :> Formatting.MediaTypeFormatter
                        new Formatting.JsonMediaTypeFormatter() :> Formatting.MediaTypeFormatter |]

    // Respond with a plain text "Hello, world!"
    let helloWorld = mapWithConneg formatters (fun _ _ -> "Hello, world!")

    // Respond with the request content, if any.
    let echo = mapWithConneg formatters (fun _ content -> content)
    
    let resource = route "/" (get helloWorld <|> post echo)

    // Mount the app and add a middleware to support HEAD requests.
    let app : HttpApplication = merge [ resource ] |> Middleware.head
    
    let config = WebApi.configure app
    let baseUri = "http://localhost:1000/"

    member x.Start() =
        RouteTable.Routes.MapServiceRoute<WebApi.FrankApi>("", config)
