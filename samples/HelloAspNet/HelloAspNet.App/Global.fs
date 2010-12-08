namespace HelloAspNet.App

open System
open System.Web
open System.Web.Routing
open Frack
open Frack.Middleware
open Frack.Hosting.AspNet

type Global() =
    inherit System.Web.HttpApplication() 

    static member RegisterRoutes(routes:RouteCollection) =
        let app = Application(fun request ->
          ("200 OK",
           (dict [| ("Content-Type", seq { yield "text/plain" }); ("Content-Length", seq { yield "14" }) |]),
           "Hello ASP.NET!"))
        // Uses the head middleware.
        // Try using Fiddler and perform a HEAD request.
        routes.MapFrackRoute("{*path}", app)

    member x.Start() =
        Global.RegisterRoutes(RouteTable.Routes)

