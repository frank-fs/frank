namespace FSharpMVC3.Core

open System
open System.Net
open System.Net.Http
open System.Web.Routing
open Frank
open Frank.Hosting

type Global() =
    inherit System.Web.HttpApplication() 

    // Respond with a plain text "Hello, world!"
    let helloWorld _ _ = respond HttpStatusCode.OK (``Content-Type`` "text/plain") <| Some("Hello, world!")

    // Respond with the request content, if any.
    let echo _ (content: HttpContent) =
      respond HttpStatusCode.OK (``Content-Type`` "text/plain") <| Some(content.ReadAsString())
    
    let resource = route "/" (get helloWorld <|> post echo)

    // Mount the app and add a middleware to support HEAD requests.
    let app : HttpApplication = mountWithDefaults [ resource ] |> Middleware.head
    
    let config = WebApi.configure app
    let baseUri = "http://localhost:1000/"

    member x.Start() =
        RouteTable.Routes.MapServiceRoute<WebApi.FrankApi>("", config)
