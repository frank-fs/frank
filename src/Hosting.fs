(* # Frank Hosting for Web API

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2011, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)
module Frank.Hosting.WebApi

// ## Web API Hosting

// Open namespaces for Web API support.
open System.Net.Http
open System.ServiceModel
open Frank

[<ServiceContract>]
type FrankApi() =
  [<OperationContract>]
  member x.Invoke() = ()

let configure app =
  Microsoft.ApplicationServer.Http.WebApiConfiguration(
    useMethodPrefixForHttpMethod = false,
    MessageHandlerFactory = (fun () -> seq { yield FrankHandler.Create app }))
