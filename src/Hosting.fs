(* # Frank

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2011, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)
module Frank.Hosting.Wcf

// ## Web API Hosting

// Open namespaces for Web API support.
open System.Net.Http
open System.ServiceModel
open Microsoft.ApplicationServer.Http
open Frank

type HttpResource with
  member x.SendAsync(request, cancelationToken) =
    Async.StartAsTask(async.Return(x.Invoke request request.Content), cancellationToken = cancelationToken)

[<ServiceContract>]
type EmptyService() =
  [<OperationContract>]
  member x.Invoke() = ()

type FrankHandler() =
  inherit DelegatingHandler()
  static member Create(resource: HttpResource) =
    { new FrankHandler() with
        override this.SendAsync(request, cancelationToken) =
          resource.SendAsync(request, cancelationToken) } :> DelegatingHandler

let frankWebApi (resources : #seq<HttpResource>) =
  // TODO: Auto-wire routes based on the passed-in resources.
  let routes = resources |> Seq.map (fun r -> (r.Uri, r.SendAsync))

  WebApiConfiguration(
    useMethodPrefixForHttpMethod = false,
    MessageHandlerFactory = (fun () -> Seq.map FrankHandler.Create resources))
  
