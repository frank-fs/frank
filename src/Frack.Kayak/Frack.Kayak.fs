namespace Frack
module Kayak =
  open System
  open System.IO
  open System.Text
  open Kayak

  type IKayakContext with
    /// Creates an environment variable from an <see cref="HttpListenerContext"/>.
    member this.ToFrackEnv(errors:StringBuilder) =
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
            yield ("errors", Err (TextWriter.Synchronized(new StringWriter(errors))))
            yield ("input", Inp (TextReader.Synchronized(new StreamReader(this.Request.Body))))
            yield ("version", Ver [|0;1|] )
          } |> dict
