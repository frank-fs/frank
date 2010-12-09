namespace Frack.Hosting

[<System.Runtime.CompilerServices.Extension>]
module AspNet =
  open System
  open System.Collections.Generic
  open System.IO
  open System.Text
  open System.Web
  open System.Web.Routing
  open Owin
  open Frack

  [<System.Runtime.CompilerServices.Extension>]
  let Reply(r:IResponse, response:HttpResponseBase) = 
    if r.Headers.ContainsKey("Content-Length") then
      response.ContentType <- Seq.head(r.Headers.["Content-Length"])
    let statusCode, statusDescription = SplitStatus r.Status
    response.StatusCode <- statusCode
    response.StatusDescription <- statusDescription
    // TODO: Fix ASP.NET headers issue.
    //r.Headers |> Dict.toSeq |> Seq.iter (fun (k,v) -> v |> Seq.iter (fun v' -> response.Headers.Add(k,v')))
    let output = response.OutputStream
    r.WriteToStream output

  type System.Web.HttpResponseBase with
    /// Writes the Frack response to the ASP.NET response.
    member response.Reply(r:IResponse) = Reply(r, response)

  type System.Web.HttpContext with
    /// Extends System.Web.HttpContext with a method to transform it into a System.Web.HttpContextBase
    member context.ToContextBase() = System.Web.HttpContextWrapper(context)

  [<System.Runtime.CompilerServices.Extension>]
  let ToOwinRequest(context:System.Web.HttpContextBase) =
    let request = context.Request
    let headers = new Dictionary<string, seq<string>>() :> IDictionary<string, seq<string>>
    request.Headers.AsEnumerable() |> Seq.iter (fun (k,v) -> headers.Add(k, seq { yield v }))
    let items = new Dictionary<string, obj>() :> IDictionary<string, obj>
    items.["url_scheme"] <- request.Url.Scheme
    items.["host"] <- request.Url.Host
    items.["server_port"] <- request.Url.Port
    Request.Create(request.HttpMethod,
                   (request.Url.AbsolutePath + "?" + request.Url.Query),
                   headers, items, (fun (b,o,c) -> request.InputStream.AsyncRead(b,o,c)))

  type System.Web.HttpContextBase with
    /// Creates an environment variable <see cref="HttpContextBase"/>.
    member context.ToOwinRequest() = ToOwinRequest context 

  /// Defines a System.Web.Routing.IRouteHandler for hooking up Frack applications.
  type FrackRouteHandler(app:IApplication) =
    interface Routing.IRouteHandler with
      /// Get the IHttpHandler for the Frack application.
      /// The RequestContext is not used in this case,
      /// but could be used instead of the context passed to the handler.
      member this.GetHttpHandler(context) =
        { new System.Web.IHttpHandler with
            /// Since this is a pure function, it can be reused as often as desired.
            member this.IsReusable = true
            /// Process an incoming request. 
            member this.ProcessRequest(context:HttpContext) =
              let contextBase = context.ToContextBase()
              let req = contextBase.ToOwinRequest()
              app.AsyncInvoke(req)
              //|> Async.Catch // <- This would allow a choice of returning response or writing out errors.
              |> Async.RunSynchronously
              |> contextBase.Response.Reply }

  [<System.Runtime.CompilerServices.Extension>]
  let MapFrackRoute(routes:RouteCollection, path:string, app:IApplication) =
    routes.Add(new Route(path, new FrackRouteHandler(app))) 

  type System.Web.Routing.RouteCollection with
    member routes.MapFrackRoute(path, app) = MapFrackRoute(routes, path, app)