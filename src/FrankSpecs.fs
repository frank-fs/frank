module FrankSpecs
open System
open System.Collections.Generic
open System.Text
open Frack
open Frank
open NaturalSpec

module Fakes =
  open System.Collections.Specialized
  open System.IO
  open System.Web

  let getRequest m (url:Uri) (errors:StringBuilder) =
    seq { yield ("HTTP_METHOD", Str m)
          yield ("SCRIPT_NAME", Str (url.AbsolutePath |> getPathParts |> fst))
          yield ("PATH_INFO", Str (url.AbsolutePath |> getPathParts |> snd))
          yield ("QUERY_STRING", Str (url.Query.TrimStart('?')))
          yield ("CONTENT_TYPE", Str "text/plain")
          yield ("CONTENT_LENGTH", Int 5)
          yield ("SERVER_NAME", Str url.Host)
          yield ("SERVER_PORT", Str (url.Port.ToString()))
          yield! [|("HTTP_TEST", Str "value");("REQUEST_METHOD", Str m)|]
          yield ("url_scheme", Str url.Scheme)
          yield ("errors", Err (TextWriter.Synchronized(new StringWriter(errors))))
          yield ("input", Inp (TextReader.Synchronized(new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("Howdy")) :> Stream))))
          yield ("version", Ver [|0;1|] )
        } |> dict

//module RouteSpecs =

module FrankAppSpecs =
  open Fakes

  [<Scenario>]
  let ``When creating a Frank applicaion, it should accept a sequence of routes``() =
    let ``creating an app`` handlers =
      printMethod ""
      FrankApp handlers
    Given [| get "/" (fun _ -> Object(Str("Hello world!"))) |]
    |> When ``creating an app``
    |> It should be (fun app -> app.GetType() = typeof<FrankApp>)
    |> Verify

  [<Scenario>]
  let ``When creating a Frank application, it should respond with Hello world!``() =
    let helloworld = FrankApp [ get "/" (fun _ -> Object(Str("Hello world!"))) ] 
    let ``invoking an hello world app`` (app:FrankApp) =
      printMethod ""
      let url = Uri("http://wizardsofsmart.net/") 
      let request = getRequest "GET" url (StringBuilder()) 
      let status, hdrs, value = app.Invoke(request)
      value |> Seq.head |> ByteString.toString
    Given helloworld
    |> When ``invoking an hello world app``
    |> It should equal "Hello world!" 
    |> Verify
