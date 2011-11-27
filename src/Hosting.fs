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
open Microsoft.ApplicationServer.Http
open Frank

let startAsTask (app: HttpApplication) (request, cancelationToken) =
  Async.StartAsTask(async.Return(app request request.Content), cancellationToken = cancelationToken)

[<ServiceContract>]
type FrankApi() =
  [<OperationContract>]
  member x.Invoke() = ()

type FrankHandler() =
  inherit DelegatingHandler()
  static member Create(app) =
    let app = startAsTask app
    { new FrankHandler() with
        override this.SendAsync(request, cancelationToken) =
          app(request, cancelationToken) } :> DelegatingHandler

let configure app =
  WebApiConfiguration(
    useMethodPrefixForHttpMethod = false,
    MessageHandlerFactory = (fun () -> seq { yield FrankHandler.Create app }))
