(* # Frank Hello World Sample

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

let helloWorld request =
  async.Return <| HttpResponseMessage.ReplyTo(request, "Hello, world!")

let config = WebApi.configure helloWorld
let baseUri = "http://localhost:1000/"
let host = new Microsoft.ApplicationServer.Http.HttpServiceHost(typeof<WebApi.FrankApi>, config, [| baseUri |])
host.Open()

printfn "Host open for one minute..."
printfn "Use a web browser and go to %s or do it right and get fiddler!" baseUri

host.Close()
