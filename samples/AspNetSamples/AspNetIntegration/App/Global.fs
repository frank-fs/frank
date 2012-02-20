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
    let helloWorld request =
      async.Return <| HttpResponseMessage.ReplyTo(request, new StringContent(@"<!doctype html>
<meta charset=utf-8>
<title>Hello</title>
<p>Hello, world!
<form action=""/"" method=""post"">
<input type=""hidden"" name=""text"" value=""testing"">
<input type=""submit"">", System.Text.Encoding.UTF8, "text/html"), ``Content-Type`` "text/html")

    // Respond with the request content, if any.
    let echo = negotiateMediaType formatters <| fun request -> request.Content.AsyncReadAsString()
    
    let resource = route "/" (get helloWorld <|> post echo)

    // Mount the app and add a middleware to support HEAD requests.
    let app = merge [ resource ] //|> Middleware.head
    
    let config = WebApi.configure app

    member x.Start() =
        
