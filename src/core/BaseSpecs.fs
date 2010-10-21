module BaseSpecs
open System
open System.Collections.Specialized
open System.IO
open System.Text
open System.Web
open Frack

let url = new Uri("http://wizardsofsmart.net/something/awesome?name=test&why=how")

let getRequest m (errors:StringBuilder) =
  seq { yield ("HTTP_METHOD", Str m)
        yield ("SCRIPT_NAME", Str (url.AbsolutePath |> getPathParts |> fst))
        yield ("PATH_INFO", Str (url.AbsolutePath |> getPathParts |> snd))
        yield ("QUERY_STRING", Str (url.Query.TrimStart('?')))
        yield ("CONTENT_TYPE", Str "text/plain")
        yield ("CONTENT_LENGTH", Int 5)
        yield ("SERVER_NAME", Str url.Host)
        yield ("SERVER_PORT", Str (url.Port.ToString()))
        yield! [|("HTTP_TEST", Str "value");("REQUEST_METHOD", Str "GET")|]
        yield ("url_scheme", Str url.Scheme)
        yield ("errors", Err (TextWriter.Synchronized(new StringWriter(errors))))
        yield ("input", Inp (TextReader.Synchronized(new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("Howdy")) :> Stream))))
        yield ("version", Ver [|0;1|] )
      } |> dict

let getUtility m = getRequest m (StringBuilder())
let hdrs = dict [| ("Content_Type","text/plain");("Content_Length","5") |] 
let body = seq { yield ByteString.fromString "Howdy" } 
let app = App(fun request -> ( 200, hdrs, body ))