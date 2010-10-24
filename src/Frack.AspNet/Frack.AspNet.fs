namespace Frack
module AspNet =
  open System
  open System.IO
  open System.Text
  open System.Web

  /// Writes the Frack response to the ASP.NET response.
  let write (out:HttpResponseBase) (response:Frack.Response) =
    let status, headers, body = response
    out.StatusCode <- status
    headers |> Dict.toSeq
            |> Seq.iter out.Headers.Add
    body    |> Seq.iter (ByteString.transfer out.OutputStream) 
    out.Close()

  type System.Web.HttpContext with
    /// Extends System.Web.HttpContext with a method to transform it into a System.Web.HttpContextBase
    member this.ToContextBase() = System.Web.HttpContextWrapper(this)

  type System.Web.HttpContextBase with
    /// Creates an environment variable <see cref="HttpContextBase"/>.
    member this.ToFrackEnvironment() : Frack.Environment =
      seq { yield ("HTTP_METHOD", Str this.Request.HttpMethod)
            yield ("SCRIPT_NAME", Str (this.Request.Url.AbsolutePath |> getPathParts |> fst))
            yield ("PATH_INFO", Str (this.Request.Url.AbsolutePath |> getPathParts |> snd))
            yield ("QUERY_STRING", Str (this.Request.Url.Query.TrimStart('?')))
            yield ("CONTENT_TYPE", Str this.Request.ContentType)
            yield ("CONTENT_LENGTH", Int this.Request.ContentLength)
            yield ("SERVER_NAME", Str this.Request.Url.Host)
            yield ("SERVER_PORT", Str (this.Request.Url.Port.ToString()))
            yield! this.Request.Headers.AsEnumerable()
            yield ("url_scheme", Str this.Request.Url.Scheme)
            yield ("errors", Err ByteString.empty)
            yield ("input", Inp (if this.Request.InputStream = null then ByteString.empty else this.Request.InputStream.ToByteString()))
            yield ("version", Ver [|0;1|] )
          } |> dict
