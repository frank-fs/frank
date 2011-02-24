namespace Frack.Hosting

[<System.Runtime.CompilerServices.Extension>]
module AspNet =
  open System
  open System.Collections.Generic
  open System.IO
  open System.Text
  open System.Web
  open System.Web.Routing
  open Frack
  open Frack.Collections
  open Frack.Hosting.SystemWeb

  [<System.Runtime.CompilerServices.Extension>]
  [<Microsoft.FSharp.Core.CompiledName("ToContextBase")>]
  let toContextBase(context) = System.Web.HttpContextWrapper(context)

  type System.Web.HttpContext with
    /// Extends System.Web.HttpContext with a method to transform it into a System.Web.HttpContextBase
    member context.ToContextBase() = toContextBase(context)

  type OwinHttpHandler(app: IDictionary<string, obj> -> Async<string * IDictionary<string, string> * seq<obj>>) =
    interface System.Web.IHttpHandler with
      /// Since this is a pure function, it can be reused as often as desired.
      member this.IsReusable = true
      /// Process an incoming request. 
      member this.ProcessRequest(context) =
        let ctx = context.ToContextBase()
        let request = ctx.ToOwinRequest()
        let econt e = printfn "%A" e
        Async.StartWithContinuations(app request, ctx.Response.Reply, econt, econt)

  /// Defines a System.Web.Routing.IRouteHandler for hooking up Frack applications.
  type OwinRouteHandler(app) =
    interface Routing.IRouteHandler with
      /// Get the IHttpHandler for the Frack application.
      /// The RequestContext is not used in this case,
      /// but could be used instead of the context passed to the handler
      /// or checked here to ensure the request is valid.
      member this.GetHttpHandler(context) = OwinHttpHandler app :> IHttpHandler

  [<System.Runtime.CompilerServices.Extension>]
  [<Microsoft.FSharp.Core.CompiledName("MapFrackRoute")>]
  let mapFrackRoute(routes: RouteCollection, path, app) =
    routes.Add(new Route(path, new OwinRouteHandler(app))) 

  type System.Web.Routing.RouteCollection with
    member routes.MapFrackRoute(path, app) = mapFrackRoute(routes, path, app)