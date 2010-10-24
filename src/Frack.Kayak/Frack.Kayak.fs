namespace Frack
module Kayak =
  open System
  open System.IO
  open System.Text
  open Kayak

  /// Writes the Frack response to the ASP.NET response.
  let write (out:Kayak.IKayakServerResponse) (response:Frack.Response) =
    let status, headers, body = response
    out.StatusCode <- status
    headers |> Dict.toSeq
            |> Seq.iter out.Headers.Add
    body    |> Seq.iter (ByteString.transfer out.Body) 

  type IKayakContext with
    /// Creates an environment variable from an <see cref="HttpListenerContext"/>.
    member this.ToFrackEnvironment() : Frack.Environment =
      let url = Uri(this.Request.RequestUri)
      seq { yield ("HTTP_METHOD", Str this.Request.Verb)
            yield ("SCRIPT_NAME", Str (url.AbsolutePath |> getPathParts |> fst))
            yield ("PATH_INFO", Str (url.AbsolutePath |> getPathParts |> snd))
            yield ("QUERY_STRING", Str (url.Query.TrimStart('?')))
            yield ("CONTENT_TYPE", Str (this.Request.Headers.Get("CONTENT_TYPE")))
            yield ("CONTENT_LENGTH", Int (this.Request.Headers.GetContentLength())) 
            yield ("SERVER_NAME", Str url.Host)
            yield ("SERVER_PORT", Str (url.Port.ToString()))
            for header in this.Request.Headers do yield (header.Name, Str header.Value)
            yield ("url_scheme", Str url.Scheme)
            yield ("errors", Err ByteString.empty)
            yield ("input", Inp (if this.Request.Body = null then ByteString.empty else this.Request.Body.ToByteString()))
            yield ("version", Ver [|0;1|] )
          } |> dict
