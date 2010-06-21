/// SimpleServer is an example of using Frack as a F# script.
/// <see href="http://blogs.msdn.com/b/chrsmith/archive/2008/09/12/scripting-in-f.aspx" />
#I @"..\lib\FSharp"
#I @"..\src\frack\bin\Debug"
#r "mscorlib.dll"
#r "System.Core.dll"
#r "System.Net.dll"
#r "System.Web.dll"
#r "System.Web.Abstractions.dll"
#r "FSharp.Core.dll"
#r "frack.dll"

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
