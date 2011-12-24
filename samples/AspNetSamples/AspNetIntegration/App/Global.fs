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

    // Respond with a web page containing "Hello, world!" and a form submission to use the POST method of the resource.
    let helloWorld _ _ =
      respond HttpStatusCode.OK (``Content-Type`` "text/html")
      <| Str @"<!doctype html>
<meta charset=utf-8>
<title>Hello</title>
<p>Hello, world!
<form action=""/"" method=""post"">
<input type=""hidden"" name=""text"" value=""testing"">
<input type=""submit"">"

    // Respond with the request content, if any.
    let echo = mapWithConneg formatters <| fun _ stream -> 
      use reader = new System.IO.StreamReader(stream)
      reader.ReadToEnd()
    
    let resource = route "/" (get helloWorld <|> post echo)

    // Mount the app and add a middleware to support HEAD requests.
    let app = merge [ resource ] //|> Middleware.head
    
    let config = WebApi.configure app

    member x.Start() =
        RouteTable.Routes.MapServiceRoute<WebApi.FrankApi>("", config)
