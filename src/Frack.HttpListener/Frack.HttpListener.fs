namespace Frack
module HttpListener =
  open System
  open System.IO
  open System.Text

  type System.Net.HttpListenerContext with
    /// Creates an environment variable from an <see cref="HttpListenerContext"/>.
    member this.ToFrackRequest(errors:StringBuilder) =
      seq { yield ("HTTP_METHOD", Str this.Request.HttpMethod)
            yield ("SCRIPT_NAME", Str (this.Request.Url.AbsolutePath |> getPathParts |> fst))
            yield ("PATH_INFO", Str (this.Request.Url.AbsolutePath |> getPathParts |> snd))
            yield ("QUERY_STRING", Str (this.Request.Url.Query.TrimStart('?')))
            yield ("CONTENT_TYPE", Str this.Request.ContentType)
            yield ("CONTENT_LENGTH", Int (Convert.ToInt32(this.Request.ContentLength64))) 
            yield ("SERVER_NAME", Str this.Request.Url.Host)
            yield ("SERVER_PORT", Str (this.Request.Url.Port.ToString()))
            yield! this.Request.Headers.AsEnumerable()
            yield ("url_scheme", Str this.Request.Url.Scheme)
            yield ("errors", Err (TextWriter.Synchronized(new StringWriter(errors))))
            yield ("input", Inp (TextReader.Synchronized(new StreamReader(this.Request.InputStream))))
            yield ("version", Ver [|0;1|] )
          } |> dict
