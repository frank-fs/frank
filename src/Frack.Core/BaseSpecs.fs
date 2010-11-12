module BaseSpecs
open System
open System.Collections.Specialized
open System.IO
open System.Text
open System.Web
open Frack

let getEnv m =
  seq { yield ("HTTP_METHOD", Str m)
        yield ("SCRIPT_NAME", Str "/something")
        yield ("PATH_INFO", Str "/awesome")
        yield ("QUERY_STRING", Str "name=test&why=how")
        yield ("CONTENT_TYPE", Str "text/plain")
        yield ("CONTENT_LENGTH", Int 5)
        yield ("SERVER_NAME", Str "wizardsofsmart.net")
        yield ("SERVER_PORT", Str "80")
        yield! [|("HTTP_TEST", Str "value");("REQUEST_METHOD", Str "GET")|]
        yield ("url_scheme", Str "http")
        yield ("errors", Err ByteString.empty)
        yield ("input", Inp (ByteString.fromString "Howdy"))
        yield ("version", Ver [|0;1|] )
      } |> dict

let hdrs = dict [| ("Content_Type","text/plain");("Content_Length","5") |] 
let body = ByteString.fromString "Howdy"
let app = fun request -> ( 200, hdrs, body )