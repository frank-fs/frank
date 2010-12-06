namespace HelloAspNet.App

open System
open System.Web
open System.Web.Routing
open Frack
open Owin.Hosting.AspNet
open Owin.Middleware

type Global() =
    inherit System.Web.HttpApplication() 

    static member RegisterRoutes(routes:RouteCollection) =
        let app = Application(fun request ->
          ("200 OK",
           (dict [| ("Content-Type", seq { yield "text/plain" }); ("Content-Length", seq { yield "14" }) |]),
           "Hello ASP.NET!"))
        // Uses the head middleware.
        // Try using Fiddler and perform a HEAD request.
        routes.Add(new Route("{*path}", new OwinRouteHandler(head app))) 

    member x.Start() =
        Global.RegisterRoutes(RouteTable.Routes)

