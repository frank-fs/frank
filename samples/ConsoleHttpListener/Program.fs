module Frack.Samples.ConsoleHttpListener
open System
open System.IO
open System.Net
open System.Text
open System.Web
open Frack

let app (env:Environment) =
  ( 200, Map.ofList [("Content_Type","text/plain");("Content_Length","5")], seq { yield "Howdy" } )

let listener = new HttpListener()
let prefix = "http://localhost:9191/"
listener.Prefixes.Add(prefix)
printfn "Listening on %s" prefix 
listener.Start()

let context = listener.GetContext()
let response = context.Response
let output = response.OutputStream

let errors, env = Env.create << HttpContextWrapper.createFromHttpListenerContext <| context
printfn "Received a %s request" env.HTTP_METHOD 
let status, hdrs, body = app env

response.StatusCode <- status
hdrs |> Map.iter (fun name value -> response.AddHeader(name, value))
body |> Seq.map  (fun value -> Encoding.UTF8.GetBytes(value))
     |> Seq.iter (fun bytes -> for b in bytes do output.WriteByte(b))
output.Close()
listener.Close()