namespace HelloAspNet.App

open System
open System.Collections.Generic
open System.Web
open System.Web.Routing
open Frack
open Frack.Middleware
open Frack.Hosting.AspNet

type Global() =
  inherit System.Web.HttpApplication() 

  static member RegisterRoutes(routes:RouteCollection) =
    // Echo the request body contents back to the sender. 
    // Use Fiddler to post a message and see it return.
    let app (request: IDictionary<string, obj>) = async {
      let! body = request?RequestBody :?> Async<ArraySegment<byte>> |> Stream.readToEnd
      let greeting = "Howdy!\r\n"
      return "200 OK", dict [("Content-Type", "text/html")],
             Sequence(seq { yield (Str greeting)
                            yield (Bytes body) }) }

    // Uses the head middleware.
    // Try using Fiddler and perform a HEAD request.
    routes.MapFrackRoute("{*path}", app)

  member x.Start() =
    Global.RegisterRoutes(RouteTable.Routes)

