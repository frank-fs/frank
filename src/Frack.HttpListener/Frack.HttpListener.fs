namespace Frack
module HttpListener =
  open System
  open System.Collections.Generic
  open System.IO
  open System.Net
  open System.Text
  open Owin
  open Frack
  open Frack.Extensions

  /// Writes the Frack response to the HttpListener response.
  let write (out:HttpListenerResponse) (response:IResponse) =
    out.StatusCode <- int response.Status
    response.Headers |> Dict.toSeq |> Seq.iter (fun (k,v) -> v |> Seq.iter (fun v' -> out.Headers.Add(k,v')))
    response.GetBody() :?> bytestring |> ByteString.transfer out.OutputStream 
    out.Close()

  type System.Net.HttpListenerContext with
    /// Creates an environment variable <see cref="HttpContextBase"/>.
    member this.ToOwinRequest() =
      let headers = new Dictionary<string, seq<string>>() :> IDictionary<string, seq<string>>
      this.Request.Headers.AsEnumerable() |> Seq.iter (fun (k,v) -> headers.Add(k, seq { yield v }))
      let items = new Dictionary<string, obj>() :> IDictionary<string, obj>
      items.["url_scheme"] <- this.Request.Url.Scheme
      items.["server_name"] <- this.Request.Url.Host
      items.["server_port"] <- this.Request.Url.Port
      Request.fromAsync this.Request.HttpMethod
                        (this.Request.Url.AbsolutePath + "?" + this.Request.Url.Query) 
                        headers  items (this.Request.InputStream.AsyncRead)