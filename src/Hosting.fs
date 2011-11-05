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

[<ServiceContract>]
type EmptyService() =
  [<OperationContract>]
  member x.Invoke() = ()

type FrankHandler() =
  inherit DelegatingHandler()
  static member Create(resource : Resource) =
    { new FrankHandler() with
        override this.SendAsync(request, cancellationToken) =
          resource.ProcessRequestAsync(request, cancellationToken) } :> DelegatingHandler

let frankWebApi (resources : #seq<#Resource>) =
  // TODO: Auto-wire routes based on the passed-in resources.
  let routes = resources |> Seq.map (fun r -> (r.Path, r.ProcessRequestAsync))

  WebApiConfiguration(
    useMethodPrefixForHttpMethod = false,
    MessageHandlerFactory = (fun () -> Seq.map FrankHandler.Create resources))
  