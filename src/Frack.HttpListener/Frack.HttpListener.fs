namespace Frack
module HttpListener =
  open System
  open System.Collections.Generic
  open System.IO
  open System.Net
  open System.Text
  open Frack

  /// Writes the Frack response to the HttpListener response.
  let write (out:HttpListenerResponse) (response:int * IDictionary<string,string> * bytestring) =
    let status, headers, body = response
    out.StatusCode <- status
    headers |> Dict.toSeq
            |> Seq.iter out.Headers.Add
    body    |> ByteString.transfer out.OutputStream 

  type System.Net.HttpListenerContext with
    /// Creates an environment variable from an <see cref="HttpListenerContext"/>.
    member this.ToFrackEnvironment() : Environment =
      seq { yield ("HTTP_METHOD", Str this.Request.HttpMethod)
            yield ("SCRIPT_NAME", Str (this.Request.Url.AbsolutePath |> getPathParts |> fst))
            yield ("PATH_INFO", Str (this.Request.Url.AbsolutePath |> getPathParts |> snd))
            yield ("QUERY_STRING", Str (this.Request.Url.Query.TrimStart('?')))
            yield ("CONTENT_TYPE", Str this.Request.ContentType)
            yield ("CONTENT_LENGTH", Int (Convert.ToInt32(this.Request.ContentLength64))) 
            yield ("SERVER_NAME", Str this.Request.Url.Host)
            yield ("SERVER_PORT", Str (this.Request.Url.Port.ToString()))
            yield! this.Request.Headers.AsEnumerable() |> Seq.map (fun (k,v) -> (k, Str v))
            yield ("url_scheme", Str this.Request.Url.Scheme)
            yield ("errors", Err ByteString.empty)
            yield ("input", Inp (if this.Request.InputStream = null then ByteString.empty else this.Request.InputStream.ToByteString()))
            yield ("version", Ver [|0;1|] )
          } |> dict