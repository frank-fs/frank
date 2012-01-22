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
#r @"..\packages\HttpClient.0.6.0\lib\40\System.Net.Http.dll"
#r @"..\packages\HttpClient.0.6.0\lib\40\Microsoft.Net.Http.Formatting.dll"
#r @"..\packages\WebApi.0.6.0\lib\40-Full\Microsoft.Runtime.Serialization.Internal.dll"
#r @"..\packages\WebApi.0.6.0\lib\40-Full\Microsoft.ServiceModel.Internal.dll"
#r @"..\packages\WebApi.0.6.0\lib\40-Full\Microsoft.Server.Common.dll"
#r @"..\packages\WebApi.0.6.0\lib\40-Full\Microsoft.ApplicationServer.Http.dll"
#r @"..\packages\WebApi.Enhancements.0.6.0\lib\40-Full\Microsoft.ApplicationServer.HttpEnhancements.dll"
#load @"..\src\System.Net.Http.fs"
#load @"..\src\Frank.fs"
#load @"..\src\Hosting.fs"

open System.Net
open System.Net.Http
open Frank
open Frank.Hosting

// Respond with the request content, if any.
let echo (request: HttpRequestMessage) =
  async.Return <| HttpResponseMessage.ReplyTo(request, request.Content, ``Content-Type`` "text/plain")

// Create an application from an `HttpResource` that only responds to `POST` requests.
// Try sending a GET or other method to see a `405 Method Not Allowed` response.
// Also note that this application only responds to the root uri. A `404 Not Found`
// response should be returned for any other uri.
let resource = routeWithMethodMapping "/" [ post echo ]
let app : HttpApplication = merge [ resource ]

let config = WebApi.configure app
let baseUri = "http://localhost:1000/"
let host = new Microsoft.ApplicationServer.Http.HttpServiceHost(typeof<WebApi.FrankApi>, config, [| baseUri |])
host.Open()

printfn "Host open.  Hit enter to exit..."
printfn "Use a web browser and go to %A or do it right and get fiddler!" baseUri
System.Console.Read() |> ignore

host.Close()
