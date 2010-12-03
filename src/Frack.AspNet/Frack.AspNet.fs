namespace Frack
module AspNet =
  open System
  open System.Collections.Generic
  open System.IO
  open System.Text
  open System.Web
  open Owin
  open Frack
  open Frack.Extensions

  /// Writes the Frack response to the ASP.NET response.
  let write (out:HttpResponseBase) (response:IResponse) =
    let statusCode, statusDescription = splitStatus response.Status
    out.StatusCode <- statusCode
    out.StatusDescription <- statusDescription
    // TODO: Fix ASP.NET headers issue.
    //response.Headers |> Dict.toSeq |> Seq.iter (fun (k,v) -> v |> Seq.iter (fun v' -> out.Headers.Add(k,v')))
    response.GetBody() :?> bytestring |> ByteString.transfer out.OutputStream 

  type System.Web.HttpContext with
    /// Extends System.Web.HttpContext with a method to transform it into a System.Web.HttpContextBase
    member this.ToContextBase() = System.Web.HttpContextWrapper(this)

  type System.Web.HttpContextBase with
    /// Creates an environment variable <see cref="HttpContextBase"/>.
    member this.ToOwinRequest() =
      let headers = new Dictionary<string, seq<string>>() :> IDictionary<string, seq<string>>
      this.Request.Headers.AsEnumerable() |> Seq.iter (fun (k,v) -> headers.Add(k, seq { yield v }))
      let items = new Dictionary<string, obj>() :> IDictionary<string, obj>
      items.["url_scheme"] <- this.Request.Url.Scheme
      items.["host"] <- this.Request.Url.Host
      items.["server_port"] <- this.Request.Url.Port
      Request.fromAsync this.Request.HttpMethod
                        (this.Request.Url.AbsolutePath + "?" + this.Request.Url.Query) 
                        headers  items (this.Request.InputStream.AsyncRead)

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
              |> write contextBase.Response }