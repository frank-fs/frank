(* # Frank Self-Host Sample

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2010-2011, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)
#r "System"
#r "System.Core"
#r "System.ServiceModel"
#r "System.ServiceModel.Web"
#r @"..\packages\FSharpx.Core.1.5.67\lib\40\FSharpx.Core.dll"
#r @"..\packages\FSharpx.Http.1.5.67\lib\40\FSharpx.Http.dll"
#r @"..\packages\System.Json.4.0.20126.16343\lib\net40\System.Json.dll"
#r @"..\packages\System.Net.Http.2.0.20126.16343\lib\net40\System.Net.Http.dll"
#r @"..\packages\System.Net.Http.2.0.20126.16343\lib\net40\System.Net.Http.WebRequest.dll"
#r @"..\packages\System.Net.Http.Formatting.4.0.20126.16343\lib\net40\System.Net.Http.Formatting.dll"
#r @"..\packages\AspNetWebApi.Core.4.0.20126.16343\lib\net40\System.Web.Http.dll"
#r @"..\packages\System.Web.Http.Common.4.0.20126.16343\lib\net40\System.Web.Http.Common.dll"
#r @"..\packages\AspNetWebApi.SelfHost.4.0.20126.16343\lib\net40\System.Web.Http.SelfHost.dll"
#r @"..\packages\ImpromptuInterface.5.6.7\lib\net40\ImpromptuInterface.dll"
#r @"..\packages\ImpromptuInterface.FSharp.1.1.0\lib\net40\ImpromptuInterface.FSharp.dll"
#load @"..\src\System.Net.Http.fs"
#load @"..\src\System.Web.Http.fs"
#load @"..\src\Frank.fs"
#load @"..\src\Middleware.fs"

open System
open System.Net
open System.Net.Http
open System.Web.Http
open System.Web.Http.SelfHost
open Frank

// Respond with the request content, if any.
let echo (request: HttpRequestMessage) = async {
    let! content = request.Content.AsyncReadAsString()
    return respond HttpStatusCode.OK <| new StringContent(content) <| ``Content-Type`` "text/plain"
}

// Create an application from an `HttpResource` that only responds to `POST` requests.
// Try sending a GET or other method to see a `405 Method Not Allowed` response.
// Also note that this application only responds to the root uri. A `404 Not Found`
// response should be returned for any other uri.
let resource = routeResource "/" [ post echo ]
let app = merge [ resource ]

let baseUri = "http://127.0.0.1:1000"
let config = new HttpSelfHostConfiguration(baseUri)
config.Register app

let server = new HttpSelfHostServer(config)
server.OpenAsync().Wait()

Console.WriteLine("Running on " + baseUri)
Console.WriteLine("Press any key to stop.")
Console.ReadKey() |> ignore

server.CloseAsync().Wait()
