(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"

(**
Introducing your project
========================

Say more

*)
#r "Newtonsoft.Json"
#r "System.Net.Http"
#r "System.Net.Http.Formatting"
#r "System.Net.Http.WebRequest"
#r "System.Web.Http"
#r "FSharpx.Core"
#r "Frank.dll"

(**
Some more info
*)
open System
open System.Net
open System.Net.Http
open System.Web.Http
open System.Web.Http.HttpResource
open System.Web.Routing
open Frank

type WebApiApplication() =
    inherit System.Web.HttpApplication()

    let formatters = [| new Formatting.JsonMediaTypeFormatter() :> Formatting.MediaTypeFormatter
                        new Formatting.XmlMediaTypeFormatter() :> Formatting.MediaTypeFormatter |]

    // Respond with a web page containing "Hello, world!" and a form submission to use the POST method of the resource.
    let helloWorld request = 
      respond HttpStatusCode.OK
      <| ``Content-Type`` "text/html"
      <| Some(Formatted (@"<!doctype html>
<meta charset=utf-8>
<title>Hello</title>
<p>Hello, world!
<form action=""/"" method=""post"">
<input type=""hidden"" name=""text"" value=""testing"">
<input type=""submit"">", System.Text.Encoding.UTF8, "text/html"))
      <| request
      |> async.Return

    // Respond with the request content, if any.
    let echo = runConneg formatters <| fun request -> request.Content.AsyncReadAsString()
    
    let resource = route "/" (get helloWorld <|> post echo)
    
    member x.Start() =
        GlobalConfiguration.Configuration |> register [resource]
