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
#r "frack.dll"
#r "Frack.Extensions.dll"
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
let app env = 
  (200, dict [("Content_Type","text/plain");("Content_Length","6")], ByteString.fromString "Howdy!")

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
//app env |> write context.Response
printEnvironment app env |> write context.Response

listener.Close()