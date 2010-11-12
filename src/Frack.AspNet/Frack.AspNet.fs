namespace Frack
module AspNet =
  open System
  open System.Collections.Generic
  open System.IO
  open System.Text
  open System.Web
  open Frack

  /// Writes the Frack response to the ASP.NET response.
  let write (out:HttpResponseBase) (response:int * IDictionary<string,string> * bytestring) =
    let status, headers, body = response
    out.StatusCode <- status
    headers |> Dict.toSeq
            |> Seq.iter out.Headers.Add
    body    |> ByteString.transfer out.OutputStream 
    out.Close()

  type System.Web.HttpContext with
    /// Extends System.Web.HttpContext with a method to transform it into a System.Web.HttpContextBase
    member this.ToContextBase() = System.Web.HttpContextWrapper(this)

  type System.Web.HttpContextBase with
    /// Creates an environment variable <see cref="HttpContextBase"/>.
    member this.ToFrackEnvironment() : Environment =
      seq { yield ("HTTP_METHOD", Str this.Request.HttpMethod)
            yield ("SCRIPT_NAME", Str (this.Request.Url.AbsolutePath |> getPathParts |> fst))
            yield ("PATH_INFO", Str (this.Request.Url.AbsolutePath |> getPathParts |> snd))
            yield ("QUERY_STRING", Str (this.Request.Url.Query.TrimStart('?')))
            yield ("CONTENT_TYPE", Str this.Request.ContentType)
            yield ("CONTENT_LENGTH", Int this.Request.ContentLength)
            yield ("SERVER_NAME", Str this.Request.Url.Host)
            yield ("SERVER_PORT", Str (this.Request.Url.Port.ToString()))
            yield! this.Request.Headers.AsEnumerable() |> Seq.map (fun (k,v) -> (k, Str v))
            yield ("url_scheme", Str this.Request.Url.Scheme)
            yield ("errors", Err ByteString.empty)
            yield ("input", Inp (if this.Request.InputStream = null then ByteString.empty else this.Request.InputStream.ToByteString()))
            yield ("version", Ver [|0;1|] )
          } |> dict

  /// Defines a System.Web.Routing.IRouteHandler for hooking up Frack applications.
  type FrackRouteHandler(app:App) =
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
              let env = contextBase.ToFrackEnvironment()
              app env |> write contextBase.Response }