(* # Frank Hosting for Web API

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2011, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)

namespace System.Web.Http

open System.Web.Http
open Frank

type NoopController() =
  inherit ApiController()

type RouteData =
  { Controller: string }

[<AutoOpen>]
module WebApi =
  type HttpConfiguration with
    member x.Register app =
      // TODO: Expose the routes in order to hook them into the Routing infrastructure?
      HttpRouteCollectionExtensions.MapHttpRoute(x.Routes, "Frank", "") |> ignore
      x.MessageHandlers.Add(FrankHandler.Start app) 
