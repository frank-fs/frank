namespace HelloAspNet.App

open System
open System.Web
open System.Web.Routing
open Owin
open Frack
open Frack.Middleware
open Frack.Hosting.AspNet

type Global() =
  inherit System.Web.HttpApplication() 

  static member RegisterRoutes(routes:RouteCollection) =
    // Echo the request body contents back to the sender. 
    // Use Fiddler to post a message and see it return.
    let app = Application(fun (request:IRequest) -> async {
      let! body = request.AsyncReadBody(2 <<< 16)
      return Response("200 OK",
                      (dict [| ("Content-Length", seq { yield body.Length.ToString() }) |]),
                      seq { yield "Howdy!\r\n"B; yield body }) :> IResponse })
    // Uses the head middleware.
    // Try using Fiddler and perform a HEAD request.
    routes.MapFrackRoute("{*path}", head app)

  member x.Start() =
    Global.RegisterRoutes(RouteTable.Routes)

