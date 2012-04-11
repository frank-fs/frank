(* # Frank Hello World Sample

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
#r @"..\packages\FSharpx.Core.1.4.120213\lib\FSharpx.Core.dll"
#r @"..\packages\FSharpx.Core.1.4.120213\lib\FSharpx.Http.dll"
#r @"..\packages\System.Net.Http.2.0.20126.16343\lib\net40\System.Net.Http.dll"
#r @"..\packages\System.Net.Http.2.0.20126.16343\lib\net40\System.Net.Http.WebRequest.dll"
#r @"..\packages\System.Net.Http.Formatting.4.0.20126.16343\lib\net40\System.Net.Http.Formatting.dll"
#r @"..\packages\AspNetWebApi.Core.4.0.20126.16343\lib\net40\System.Web.Http.dll"
#r @"..\packages\System.Web.Http.Common.4.0.20126.16343\lib\net40\System.Web.Http.Common.dll"
#r @"..\packages\AspNetWebApi.SelfHost.4.0.20126.16343\lib\net40\System.Web.Http.SelfHost.dll"
#load @"..\src\System.Net.Http.fs"
#load @"..\src\Frank.fs"
#load @"..\src\Middleware.fs"
#load @"..\src\System.Web.Http.fs"

open System
open System.Net
open System.Net.Http
open System.Web.Http
open System.Web.Http.SelfHost
open Frank

let helloWorld request = async {
  return respond HttpStatusCode.OK (new StringContent("Hello, world!")) ignore
}

let baseUri = "http://127.0.0.1:1000"
let config = new HttpSelfHostConfiguration(baseUri)
config.Register helloWorld

let server = new HttpSelfHostServer(config)
server.OpenAsync().Wait()

Console.WriteLine("Running on " + baseUri)
Console.WriteLine("Press any key to stop.")
Console.ReadKey() |> ignore

server.CloseAsync().Wait()
