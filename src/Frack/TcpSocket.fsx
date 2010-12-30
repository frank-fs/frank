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
#load "AsyncSeq.fs"
#load "ByteString.fs"
open Frack

[<AutoOpen>]
module Tcp =
  type TcpListener with
    member listener.AsyncAcceptSocket() = Async.FromBeginEnd(listener.BeginAcceptSocket, listener.EndAcceptSocket)

[<AutoOpen>]
module Http =
  open System.Collections.Generic

  let parse (stream:Stream) = async {
    let env = Dictionary<string, obj>() :> IDictionary<string, obj>

    // Testing out using AsyncSeq and bytes.
//    let! chunks = stream |> ASeq.readInBlocks 1024 |> ASeq.toSeq
//    let bytes = chunks |> Seq.concat |> Seq.toArray
//    printfn "Received request of %d bytes." bytes.Length
//    env.Add("owin.input", bytes)

    // Working StreamReader approach
    let inp = new StreamReader(stream)
    let rec loop line =
      if line = "" then ()
      else // Parse the line and append it to the env dictionary via active patterns.
        ()
    // This will read at least the HTTP version line.
    loop (inp.ReadLine())
    env.Add("owin.input", stream)

    return env }

  let send (stream:Stream) keepAlive (response:Response) = async {
    let status, headers, body = response
    let body = body |> ByteString.getBytes
    printfn "Writing response of %d bytes." body.Length
    let out = new StreamWriter(stream)
    fprintfn out "HTTP/1.1 %s" status 
    fprintfn out "Date: %s" (DateTime.Now.ToUniversalTime().ToString("R"))
    fprintfn out "Server: Frack/0.8"
    if keepAlive then
      fprintfn out "Connection: Keep-Alive"
      fprintfn out "Keep-Alive: timeout=15, max=100"
    fprintfn out "Content-Length: %d" body.Length
    headers |> Seq.iter (fun (KeyValue(k,v)) -> fprintfn out "%s: %s" k v)
    fprintfn out ""
    out.Flush()
    do! stream.AsyncWrite(body, 0, body.Length) }

type Server() =
  static member Start(app:Application, ?ipAddress, ?port, ?keepAlive) =
    let ipAddress  = defaultArg ipAddress IPAddress.Any
    let port       = defaultArg port 80
    let keepAlive  = defaultArg keepAlive true
    let endPoint   = IPEndPoint(ipAddress, port)
    let listener   = new TcpListener(endPoint)
    let cts        = new CancellationTokenSource()

    let rec loop() = async {
      printfn "Waiting for a connection on port %d ..." port
      let! socket = listener.AsyncAcceptSocket()
      // TODO: Is it possible to get away from a stream? An AsyncSeq, perhaps? http://fssnip.net/1k
      use stream = new NetworkStream(socket)
      let sw = Stopwatch.StartNew()
      try
        try
          let! request  = parse stream
          let! response = app request
          do!  response |> send stream keepAlive
        with e -> printfn "An error occurred:\r\n%s\r\n%s" e.Message e.StackTrace
      finally
        socket.Shutdown(SocketShutdown.Both)
        socket.Close()
      sw.Stop()
      printfn "Completed response in %d ms." sw.ElapsedMilliseconds
      return! loop() }

    listener.Start()
    Async.Start(loop(), cancellationToken = cts.Token)
    { new IDisposable with
        member this.Dispose() = cts.Cancel()
                                listener.Stop() }

// Sample:
let app = fun _ -> async {
  return ("200 OK", dict [("Content-Type", "text/plain")], seq { yield "Hello world!"B :> obj }) }
let disposable = Server.Start(app, port = 8090)
disposable.Dispose()