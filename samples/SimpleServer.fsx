/// SimpleServer is an example of using Frack as a F# script.
/// <see href="http://blogs.msdn.com/b/chrsmith/archive/2008/09/12/scripting-in-f.aspx" />
#I @"..\lib\FSharp"
#I @"..\build"
#r "mscorlib.dll"
#r "System.Core.dll"
#r "System.Net.dll"
#r "System.Web.dll"
#r "System.Web.Abstractions.dll"
#r "FSharp.Core.dll"
#r "owin.dll"
#r "frack.dll"
#r "Frack.HttpListener.dll"

open System
open System.IO
open System.Net
open Frack
open Frack.HttpListener

// Simple Frack app
let app = Application(fun request -> 
  ("200 OK", (dict [("Content_Type", seq { yield "text/plain" });("Content_Length", seq { yield "6" })]), "Howdy!"))

// Set up and start an HttpListener
let listener = new HttpListener()
let prefix = "http://localhost:9191/"
listener.Prefixes.Add(prefix)
printfn "Listening on %s" prefix 
listener.Start()
let context = listener.GetContext()

// This is where Frack takes over.
let request = context.ToOwinRequest()
printfn "Received a %s request" 
app request |> write context.Response

listener.Close()