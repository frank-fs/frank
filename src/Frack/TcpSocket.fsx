//namespace Frack.Hosting
open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Sockets
open System.Text
open System.Threading

#I "..\..\lib\FSharp"
#r "FSharp.PowerPack.dll"
#load "Owin.fs"
#load "Socket.fs"
#load "AsyncSeq.fs"
#load "ByteString.fs"
#load "Http.fs"
#load "Server.fs"
open Frack

// Sample:
let app = fun _ -> async {
  return ("200 OK", dict [("Content-Type", "text/plain")], seq { yield "Hello world!"B :> obj }) }
let disposable = Server.Start(app, port = 8090)
Thread.Sleep(60 * 1000)
printfn "Closing ..."
// Shutdown the server with the disposable.
disposable.Dispose()