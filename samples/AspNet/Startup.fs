﻿namespace AspNet

open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Owin
open Microsoft.Owin
open Frank

type Routes = Root

type Startup() =
    let formatters = [| new Formatting.JsonMediaTypeFormatter() :> Formatting.MediaTypeFormatter
                        new Formatting.XmlMediaTypeFormatter() :> Formatting.MediaTypeFormatter |]

    // Respond with a web page containing "Hello, world!" and a form submission to use the POST method of the resource.
    let helloWorld request = async {
      return respond HttpStatusCode.OK
             <| ``Content-Type`` "text/html"
             <| Some(Formatted (@"<!doctype html>
<meta charset=utf-8>
<title>Hello</title>
<p>Hello, world!
<form action=""/"" method=""post"">
<input type=""hidden"" name=""text"" value=""testing"">
<input type=""submit"">", System.Text.Encoding.UTF8, "text/html"))
             <| request
    }

    // Respond with the request content, if any.
    let echo =
        runConneg formatters <| fun request ->
            Async.AwaitTask <| request.Content.ReadAsStringAsync()

    // Define the route specification, create the `ResourceManager`, and set the resource handlers.
    let spec = RouteLeaf(Root, "", [HttpMethod.Get; HttpMethod.Post])
    let app = new ResourceManager<Routes>(spec)
    let resource = app.[Root]
    do resource.SetHandler <| get helloWorld
    do resource.SetHandler <| post echo

    // Alternatively, we could create the Resource directly,
    // but this loses the advantages of the ResourceManager:
    //     let resource = new Resource("/", [get helloWorld; post echo])
    // However, b/c this also subscribes to a stream of request+output,
    // the developer is free to ignore ResourceManager and manage
    // resources directly with `Observable.filter (...) >> Observable.subscribe`.
    
    member x.Configuration(app: IAppBuilder) =
        app.Use(Func<IOwinContext, Func<Task>, Task>(fun ctx h ->
            // TODO: Map IOwinContext to something Frank can handle.
            // TODO: Use dyfrig.
            // TODO: Subscribe app to a stream of requests and output streams.
            h.Invoke() // NOTE: This is only temporary. This is effectively a call/cc call.
        ))
