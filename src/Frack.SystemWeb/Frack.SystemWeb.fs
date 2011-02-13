namespace Frack.Hosting

[<System.Runtime.CompilerServices.Extension>]
module SystemWeb =
  open System
  open System.Collections.Generic
  open System.IO
  open System.Text
  open System.Web
  open System.Web.Routing
  open Frack
  open Frack.Collections

  [<System.Runtime.CompilerServices.Extension>]
  [<Microsoft.FSharp.Core.CompiledName("ToOwinRequest")>]
  let toOwinRequest(context:System.Web.HttpContextBase) =
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
    member context.ToOwinRequest() = toOwinRequest context 

  [<System.Runtime.CompilerServices.Extension>]
  [<Microsoft.FSharp.Core.CompiledName("Reply")>]
  let reply(response: HttpResponseBase, status, headers: IDictionary<string, string>, body: seq<obj>) =
    let statusCode, statusDescription = splitStatus status
    response.StatusCode <- statusCode
    response.StatusDescription <- statusDescription
    if headers.ContainsKey("Content-Length") then
      response.ContentType <- headers.["Content-Length"]
//    headers |> Dict.toSeq |> Seq.iter (fun (k, v) -> response.Headers.Add(k, v))
    let output = response.OutputStream
    ByteString.write output body

  type HttpResponseBase with
    member response.Reply(status, headers, body) = reply(response, status, headers, body)