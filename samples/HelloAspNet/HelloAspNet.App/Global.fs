namespace HelloAspNet.App

open System
open System.Web
open System.Web.Routing
open Frack
open Frack.AspNet

type Global() =
    inherit System.Web.HttpApplication() 

    static member RegisterRoutes(routes:RouteCollection) =
        let app = Application.fromAsync (fun request -> async {
          return Response.create "200 OK"
                                 (dict [| ("Content-Type", seq { yield "text/plain" })
                                          ("Content-Length", seq { yield "14" }) |])
                                 (fun () -> "Hello ASP.NET!"B :> System.Collections.IEnumerable) })
        routes.Add(new Route("{*path}", new FrackRouteHandler(app))) 

    member x.Start() =
        Global.RegisterRoutes(RouteTable.Routes)

