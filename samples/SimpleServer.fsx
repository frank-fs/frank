/// SimpleServer is an example of using Frack as a F# script.
/// <see href="http://blogs.msdn.com/b/chrsmith/archive/2008/09/12/scripting-in-f.aspx" />
#I @"..\lib\FSharp"
#I @"..\src\core\bin\Debug"
#I @"..\src\Frack.HttpListener\bin\Debug"
#r "mscorlib.dll"
#r "System.Core.dll"
#r "System.Net.dll"
#r "System.Web.dll"
#r "System.Web.Abstractions.dll"
#r "FSharp.Core.dll"
#r "frack.dll"
#r "Frack.HttpListener.dll"

open System
open System.IO
open System.Net
open System.Text
open System.Web
open Frack
open Frack.Middlewares
open Frack.HttpListener

// Simple Frack app
let app = App(fun env ->
  (200, dict [("Content_Type","text/plain");("Content_Length","5")], seq { yield ByteString.fromString "Howdy" }))

// Simple Frack app with middleware
let midApp = printEnvironment app

// Set up and start an HttpListener
let listener = new HttpListener()
let prefix = "http://localhost:9191/"
listener.Prefixes.Add(prefix)
printfn "Listening on %s" prefix 
listener.Start()
let context = listener.GetContext()

// This is where Frack takes over.
let env = context.ToFrackEnvironment()
printfn "Received a %s request" (read env?HTTP_METHOD)
//app.Invoke(env) |> write context.Response
midApp.Invoke(env) |> write context.Response
// Done.

listener.Close()
