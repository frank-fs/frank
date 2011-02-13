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

  type System.Web.HttpContext with
    /// Extends System.Web.HttpContext with a method to transform it into a System.Web.HttpContextBase
    member context.ToContextBase() = System.Web.HttpContextWrapper(context)

  [<System.Runtime.CompilerServices.Extension>]
  let ToOwinRequest(context:System.Web.HttpContextBase) =
    let request = context.Request
    let stream = request.InputStream
    let owinRequest = Dictionary<string, obj>() :> IDictionary<string, obj>
    owinRequest.Add("RequestMethod", request.HttpMethod)
    owinRequest.Add("RequestUri", request.Url.PathAndQuery)
    owinRequest.Add("RequestHeaders", request.Headers.AsEnumerable() |> Seq.iter (fun (k, v) -> owinRequest.Add(k, v)))
    owinRequest.Add("url_scheme", request.Url.Scheme)
    owinRequest.Add("host", request.Url.Host)
    owinRequest.Add("server_port", request.Url.Port)
    owinRequest.Add("RequestBody", Request.chunk stream)
    owinRequest

  type System.Web.HttpContextBase with
    /// Creates an OWIN request variable from an HttpContextBase.
    member context.ToOwinRequest() = ToOwinRequest context 

  [<System.Runtime.CompilerServices.Extension>]
  let Reply(response: HttpResponseBase, status, headers: IDictionary<string, string>, body: seq<obj>) =
    let statusCode, statusDescription = splitStatus status
    response.StatusCode <- statusCode
    response.StatusDescription <- statusDescription
    if headers.ContainsKey("Content-Length") then
      response.ContentType <- headers.["Content-Length"]
//    headers |> Dict.toSeq |> Seq.iter (fun (k, v) -> response.Headers.Add(k, v))
    let output = response.OutputStream
    ByteString.write output body

  type HttpResponseBase with
    member response.Reply(status, headers, body) = Reply(response, status, headers, body)

  type OwinHttpHandler (app: Action<IDictionary<string, obj>, Action<string, IDictionary<string, string>, seq<obj>>, Action<exn>>) =
    let app = app |> Owin.ToAsync
    interface System.Web.IHttpHandler with
      /// Since this is a pure function, it can be reused as often as desired.
      member this.IsReusable = true
      /// Process an incoming request. 
      member this.ProcessRequest(context) =
        let contextBase = context.ToContextBase()
        let request = contextBase.ToOwinRequest()
        let response = contextBase.Response
        let econt e = printfn "%A" e
        let runApp = app request
        Async.StartWithContinuations(runApp, response.Reply, econt, econt)

  /// Defines a System.Web.Routing.IRouteHandler for hooking up Frack applications.
  type OwinRouteHandler(app) =
    interface Routing.IRouteHandler with
      /// Get the IHttpHandler for the Frack application.
      /// The RequestContext is not used in this case,
      /// but could be used instead of the context passed to the handler
      /// or checked here to ensure the request is valid.
      member this.GetHttpHandler(context) = OwinHttpHandler app :> IHttpHandler

  [<System.Runtime.CompilerServices.Extension>]
  let MapFrackRoute(routes: RouteCollection, path: string, app: Action<_,_,_>) =
    routes.Add(new Route(path, new OwinRouteHandler(app))) 

  type System.Web.Routing.RouteCollection with
    member routes.MapFrackRoute(path, app) = MapFrackRoute(routes, path, app)