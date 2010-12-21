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
      let greeting = "Howdy!\r\n"B
      let! body = request.AsyncReadBody(2 <<< 16)
      let length = greeting.Length + body.Length
      return Response.Create("200 OK",
                             (dict [("Content-Length", seq { yield length.ToString() })]),
                             seq { yield greeting; yield body }) })
    // Uses the head middleware.
    // Try using Fiddler and perform a HEAD request.
    routes.MapFrackRoute("{*path}", app |> (log >> head))

  member x.Start() =
    Global.RegisterRoutes(RouteTable.Routes)

