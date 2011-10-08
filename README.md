# Frank

Frank is an agent-based, resource-oriented wrapper for the [WCF Web API](http://wcf.codeplex.com/).
Frank is developed in [F#](http://fsharp.net).

## Goals

1. Provide a simple, to-the-metal framework for quickly building web applications and services without a lot of hassle. Frank runs on top of [ASP.NET](http://asp.net/) or can be self-hosted using the `HttpServiceHost` in Web API.
2. Leverage F#'s `MailboxProcessor<'a>`, or agents, to support isolated resource handling asynchronously. This should also allow for scaling resources individually later using [fracture-io](http://github.com/fractureio/fracture).

## Usage

### Define an app

As F# encourages a functional approach to application development, you'll find the same approach works very well for Frank.

    open System
    open System.Collections.Generic
    open System.Net
    open System.Net.Http
    open System.ServiceModel
    open Microsoft.ApplicationServer.Http
    open Frank

    let private main args =

A `GET` handler that always returns "Howdy!".

      let howdy request = new HttpResponseMessage<string>("Howdy!") :> HttpResponseMessage

A simple `POST`-based echo handler that returns the same input as it received.

      let echo (request : HttpRequestMessage) =
        let body = request.Content.ReadAsString()
        let response = new HttpResponseMessage<string>(body, HttpStatusCode.OK) :> HttpResponseMessage
        response

A compositional approach to type mapping and handler design.
Here, the actual function shows clearly that we are really using
the `id` function to return the very same result.

      let echo2Core = id

The `echo2MapFrom` maps the incoming request to a value that can be used
within the actual computation, or `echo2Core` in this example.

      let echo2MapFrom (request : HttpRequestMessage) = request.Content.ReadAsString()

The `echo2MapTo` maps the outgoing message body to an HTTP response.

      let echo2MapTo body = new HttpResponseMessage<_>(body, HttpStatusCode.OK) :> HttpResponseMessage

This `echo2` is the same in principle as `echo` above, except that the
logic for the message transform deals only with the concrete types
about which it cares and isn't bothered by the transformations.

      let echo2 = echo2Core >> echo2MapTo << echo2MapFrom 

Create a `Resource` instance at the root of the site that responds to `GET` and `POST`.

      let resource = Resource("", [ get howdy; post echo2 ])

The `frank` function creates a `WebApiConfiguration` instance based on our resources.
These will be mounted at the `baseUri`. The `frank` function creates a `DelegatingHandler`
that will intercept all incoming traffic and route it to our resources.

      let config = frank [| resource |]
      let baseUri = "http://localhost:1000/"

Create a self-hosted service host using an `EmptyService`, as the service
won't actually do anything.

      let host = new HttpServiceHost(typeof<EmptyService>, config, [| baseUri |])
      host.Open()

      printfn "Host open.  Hit enter to exit..."
      printfn "Use a web browser and go to %A or do it right and get fiddler!" baseUri
      System.Console.Read() |> ignore

      host.Close()

    [<EntryPoint>]
    let entryPoint args =
      main args
      0

### Define a middleware

Middlewares follow a Russian-doll model for wrapping resource handlers with additional functionality.
Frank middlewares take a function of type `HttpRequestMessage -> HttpResponseMessage` and return the same.
In the context of Frank, middlewares wrap the `ProcessRequest` messages passed to the resource agent.
The `Frank.Middleware` module defines several, simple middlewares, such as the `log` middleware that
intercepts logs incoming requests and the time taken to respond:

    let log app = fun (request : HttpRequestMessage) -> 
      let sw = System.Diagnostics.Stopwatch.StartNew()
      let response = app request
      printfn "Received a %A request from %A. Responded in %i ms."
              request.Method.Method request.RequestUri.PathAndQuery sw.ElapsedMilliseconds
      sw.Reset()
      response

NOTE: Middlewares are currently being re-inserted into the Frank processing pipeline to make them easier to use.

### Extend a resource with extra, common functionality.

    // Extending an agent directly:
    agent |> Extend.withOptions

    // Extend through the Resource api
    resource.Extend(Extend.withOptions)

## Team

* [Ryan Riley](http://codemav.com/panesofglass)

