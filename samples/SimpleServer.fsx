// SimpleServer is an example of using Frack as a F# script.
// <see href="http://blogs.msdn.com/b/chrsmith/archive/2008/09/12/scripting-in-f.aspx" />
#I @"..\lib\FSharp"
#I @"..\build"
#r "mscorlib.dll"
#r "System.Core.dll"
#r "System.Net.dll"
#r "System.Web.dll"
#r "System.Web.Abstractions.dll"
#r "FSharp.Core.dll"
#r "owin.dll"
#r "Owin.Extensions.dll"
#r "frack.dll"
#r "Frack.HttpListener.dll"

open System
open System.IO
open System.Net
open System.Threading
open Owin
open Owin.Extensions
open Frack
open Frack.HttpListener

let cts = new CancellationTokenSource()

// Set up and start an HttpListener
HttpListener.Start(
  "http://localhost:9191/",
  Application(fun request -> 
    ("200 OK", (dict [("Content_Type", seq { yield "text/plain" });("Content_Length", seq { yield "6" })]), "Howdy!")),
  cts.Token)

cts.Cancel()