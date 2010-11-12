namespace HelloAspNet.App

open System
open System.Web
open System.Web.Routing
open Frack
open Frack.AspNet

type Global() =
    inherit System.Web.HttpApplication() 

    static member RegisterRoutes(routes:RouteCollection) =
        let app env = (200,
                       dict [| ("Content-Type","text/plain");("Content-Length","14") |],
                       ByteString.fromString "Hello ASP.NET!")
        routes.Add(new Route("{*path}", new FrackRouteHandler(app))) 

    member x.Start() =
        Global.RegisterRoutes(RouteTable.Routes)

