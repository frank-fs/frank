(* # Frank Self-Host Sample

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2010-2011, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)

#r "System.ServiceModel"
#r "System.ServiceModel.Web"
#r @"..\packages\FSharpx.Core.1.3.111030\lib\FSharpx.Core.dll"
#r @"..\packages\FSharpx.Core.1.3.111030\lib\FSharpx.Http.dll"
#r @"..\packages\HttpClient.0.5.0\lib\40\Microsoft.Net.Http.dll"
#r @"..\packages\HttpClient.0.5.0\lib\40\Microsoft.Net.Http.Formatting.dll"
#r @"..\packages\JsonValue.0.5.0\lib\40\Microsoft.Json.dll"
#r @"..\packages\WebApi.0.5.0\lib\40-Full\Microsoft.Runtime.Serialization.Internal.dll"
#r @"..\packages\WebApi.0.5.0\lib\40-Full\Microsoft.ServiceModel.Internal.dll"
#r @"..\packages\WebApi.0.5.0\lib\40-Full\Microsoft.Server.Common.dll"
#r @"..\packages\WebApi.0.5.0\lib\40-Full\Microsoft.ApplicationServer.Http.dll"
#r @"..\packages\WebApi.Enhancements.0.5.0\lib\40-Full\Microsoft.ApplicationServer.HttpEnhancements.dll"
#r @"..\packages\ImpromptuInterface.5.6.2\lib\net40\ImpromptuInterface.dll"
#r @"..\packages\ImpromptuInterface.FSharp.1.1.0\lib\net40\ImpromptuInterface.FSharp.dll"
#load @"..\src\Frank.fs"
#load @"..\src\Middleware.fs"
#load @"..\src\Hosting.fs"

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.ServiceModel
open Microsoft.ApplicationServer.Http
open Frank
open Frank.Hosting.Wcf

// A `GET` handler that always returns "Howdy!".
let howdy request content =
  respond HttpStatusCode.OK noHeaders <| Some "Howdy!"

// A simple `POST`-based echo handler that returns the same input as it received.
let echo (request: HttpRequestMessage) content =
  respond HttpStatusCode.OK (``Content-Type`` "text/plain") <| Some(request.Content.ReadAsString())

// A compositional approach to type mapping and handler design.
// Here, the actual function shows clearly that we are really using
// the `id` function to return the very same result.
let echo2Core = id

// The `echo2MapFrom` reads the incoming HTTP request and "deserializes" the body.
let echo2MapFrom (request: HttpRequestMessage) = request.Content.ReadAsString()

// The `echo2MapTo` maps the outgoing message body to an HTTP response.
let echo2MapTo body = fun _ -> 
  respond HttpStatusCode.OK (``Content-Type`` "text/plain") <| Some body

// This `echo2` is the same in principle as `echo` above, except that the
// logic for the message transform deals only with the concrete types
// about which it cares and isn't bothered by the transformations.
let echo2 = echo2MapFrom >> echo2Core >> echo2MapTo

// TODO: Show why compositionality is a good thing. Above, one may get the impression
// that it merely leads to yet more lines of code.

// Create a `HttpResource` instance at the root of the site that responds to `GET` and `POST`.
let resource = routeWithMethodMapping "" [ get howdy; post echo2 ]

// The `frank` function creates a `WebApiConfiguration` instance based on our resources.
// These will be mounted at the `baseUri`. The `frank` function creates a `DelegatingHandler`
// that will intercept all incoming traffic and route it to our resources.
let config = frankWebApi [| resource |]

let baseUri = "http://localhost:1000/"
// Create a self-hosted service host using an `EmptyService`, as the service
// won't actually do anything.
let host = new HttpServiceHost(typeof<EmptyService>, config, [| baseUri |])
host.Open()

printfn "Host open.  Hit enter to exit..."
printfn "Use a web browser and go to %A or do it right and get fiddler!" baseUri
System.Console.Read() |> ignore

host.Close()
