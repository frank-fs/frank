// SimpleServer is an example of using Frack as a F# script.
#I @"..\lib\FSharp"
#I @"..\src\Frack\bin\Debug"
#I @"..\src\Frack.HttpListener\bin\Debug"
#r "System.Core.dll"
#r "System.Net.dll"
#r "System.Web.dll"
#r "System.Web.Abstractions.dll"
#r "Frack.Collections.dll"
#r "frack.dll"
#r "Frack.HttpListener.dll"

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Threading
open Frack
open Frack.Collections
open Frack.Middleware
open Frack.Hosting.HttpListener

printfn "Creating server ..."

let cts = new CancellationTokenSource()

// Define the application function.
let app = Owin.FromAsync(fun request -> async { 
  return "200 OK", dict [("Content_Type", "text/plain" )], seq { yield "Howdy!"B :> obj } })

// Set up and start an HttpListener
HttpListener.Start("http://localhost:9191/", app, cts.Token)
printfn "Listening on http://localhost:9191/ ..."

Thread.Sleep(60 * 1000)
cts.Cancel()
printfn "Stopped listening."