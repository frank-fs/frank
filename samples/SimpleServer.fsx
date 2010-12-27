// SimpleServer is an example of using Frack as a F# script.
// <see href="http://blogs.msdn.com/b/chrsmith/archive/2008/09/12/scripting-in-f.aspx" />
#I @"..\lib\FSharp"
#I @"..\src\Frack\bin\Debug"
#I @"..\src\Frack.HttpListener\bin\Debug"
#r "System.Core.dll"
#r "System.Net.dll"
#r "System.Web.dll"
#r "System.Web.Abstractions.dll"
#r "owin.dll"
#r "frack.dll"
#r "Frack.HttpListener.dll"

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Threading
open Frack
open Frack.Middleware
open Frack.Hosting.HttpListener

printfn "Creating server ..."

let cts = new CancellationTokenSource()

// Define the application function.
let appAsync = fun request -> async { 
  return Response.Create("200 OK",
                         (dict [("Content_Type", seq { yield "text/plain" });("Content_Length", seq { yield "6" })]),
                         "Howdy!") }

// Define the application.
let app = Application(appAsync)

// Set up and start an HttpListener
HttpListener.Start("http://localhost:9191/", app, cts.Token)
printfn "Listening on http://localhost:9191/ ..."

let stopListening() =
  app.Stop()
  cts.Cancel()
  printfn "Stopped listening."

System.Console.ReadKey() |> ignore
stopListening()
