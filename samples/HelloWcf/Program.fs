(* # Frank Self-Host Sample

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2010-2011, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.ServiceModel
open Microsoft.ApplicationServer.Http
open Frank

let private main args =

  // A `GET` handler that always returns "Howdy!".
  let howdy request = new HttpResponseMessage<string>("Howdy!") :> HttpResponseMessage

  // A simple `POST`-based echo handler that returns the same input as it received.
  let echo (request : HttpRequestMessage) =
    let body = request.Content.ReadAsString()
    let response = new HttpResponseMessage<string>(body, HttpStatusCode.OK) :> HttpResponseMessage
    response

  // A compositional approach to type mapping and handler design.
  // Here, the actual function shows clearly that we are really using
  // the `id` function to return the very same result.
  let echo2Core = id
  // The `echo2MapFrom` maps the incoming request to a value that can be used
  // within the actual computation, or `echo2Core` in this example.
  let echo2MapFrom (request : HttpRequestMessage) = request.Content.ReadAsString()
  // The `echo2MapTo` maps the outgoing message body to an HTTP response.
  let echo2MapTo body = new HttpResponseMessage<_>(body, HttpStatusCode.OK) :> HttpResponseMessage
  // This `echo2` is the same in principle as `echo` above, except that the
  // logic for the message transform deals only with the concrete types
  // about which it cares and isn't bothered by the transformations.
  let echo2 = echo2Core >> echo2MapTo << echo2MapFrom 

  // Create a `Resource` instance at the root of the site that responds to `GET` and `POST`.
  let resource = Resource("", [ get howdy; post echo2 ])
  // The `frank` function creates a `WebApiConfiguration` instance based on our resources.
  // These will be mounted at the `baseUri`. The `frank` function creates a `DelegatingHandler`
  // that will intercept all incoming traffic and route it to our resources.
  let config = frank [| resource |]

  let baseUri = "http://localhost:1000/"
  // Create a self-hosted service host using an `EmptyService`, as the service
  // won't actually do anything.
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
