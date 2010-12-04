namespace HelloAspNet.App

open System
open System.Web
open System.Web.Routing
open Frack
open Frack.AspNet

type Global() =
    inherit System.Web.HttpApplication() 

    static member RegisterRoutes(routes:RouteCollection) =
        let app = Application.FromAsync(fun request -> async {
          return Response.Create("200 OK",
                                 (dict [| ("Content-Type", seq { yield "text/plain" })
                                          ("Content-Length", seq { yield "14" }) |]),
                                 "Hello ASP.NET!"B |> Seq.map (fun b -> b :> obj) )})
        routes.Add(new Route("{*path}", new FrackRouteHandler(app))) 

    member x.Start() =
        Global.RegisterRoutes(RouteTable.Routes)

