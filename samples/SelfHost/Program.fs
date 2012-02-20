// Learn more about F# at http://fsharp.net

open System
open System.Net.Http
open System.Web.Http
open System.Web.Http.SelfHost
open Frank

let formatters = [| new Formatting.JsonMediaTypeFormatter() :> Formatting.MediaTypeFormatter
                    new Formatting.XmlMediaTypeFormatter() :> Formatting.MediaTypeFormatter |]

// Respond with a web page containing "Hello, world!" and a form submission to use the POST method of the resource.
let helloWorld request = async {
  return HttpResponseMessage.ReplyTo(request, new StringContent(@"<!doctype html>
<meta charset=utf-8>
<title>Hello</title>
<p>Hello, world!
<form action=""/"" method=""post"">
<input type=""hidden"" name=""text"" value=""testing"">
<input type=""submit"">", System.Text.Encoding.UTF8, "text/html"), ``Content-Type`` "text/html")
}

// Respond with the request content, if any.
let echo = negotiateMediaType formatters <| fun request -> request.Content.AsyncReadAsString()

let resource = route "/" (get helloWorld <|> post echo)

// Mount the app and add a middleware to support HEAD requests.
let app = merge [ resource ] //|> Middleware.head

let baseUri = "http://127.0.0.1:1000"
let config = new HttpSelfHostConfiguration(baseUri)
config.Register app

let server = new HttpSelfHostServer(config)
server.OpenAsync().Wait()

Console.WriteLine("Running on " + baseUri)
Console.WriteLine("Press any key to stop.")
Console.ReadKey() |> ignore

server.CloseAsync().Wait()
