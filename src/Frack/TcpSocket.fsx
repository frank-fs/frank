//namespace Frack.Hosting
open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Text
open System.Threading

#load "Owin.fs"
#load "ByteString.fs"
open Frack

[<AutoOpen>]
module TcpListenerEx =
  type TcpListener with
    member listener.AsyncAcceptSocket() = Async.FromBeginEnd(listener.BeginAcceptSocket, listener.EndAcceptSocket)

type Server(ipAddress:IPAddress, port, handler:Application) =
  let endPoint = IPEndPoint(ipAddress, port)
  let bufferSize = 2 <<< 16
  new (ipAddress, port, handler) = new Server(IPAddress.Parse(ipAddress), port, handler)
  new (port, handler) = new Server(IPAddress.Any, port, handler)

  member this.Start(?keepAlive) =
    let keepAlive = defaultArg keepAlive true
    let cts = new CancellationTokenSource()
    let listener = new TcpListener(endPoint)

    let parse segment = dict [ ("input", segment :> obj) ]

    let handleRequest socket = async {
      use stream = new NetworkStream(socket)
      let buffer = Array.zeroCreate<byte> bufferSize
      let! bytesRead = stream.AsyncRead(buffer)
      printfn "Received request of %d bytes." bytesRead
      let segment = ArraySegment<byte>(buffer, 0, bytesRead)
      let request = parse segment
      let! (status, headers, body) = handler request
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
      do! stream.AsyncWrite(body, 0, body.Length)
      socket.Shutdown(SocketShutdown.Both)
      socket.Close() }

    let rec loop() = async {
      printfn "Waiting for a connection on port %d ..." port
      let! socket = listener.AsyncAcceptSocket()
      try do! handleRequest socket
      with e -> printfn "An error occurred:\r\n%s\r\n%s" e.Message e.StackTrace
      return! loop() }

    listener.Start()
    Async.Start(loop(), cancellationToken = cts.Token)
    { new IDisposable with
        member this.Dispose() = cts.Cancel()
                                listener.Stop() }

// Sample:
let server = Server(8090, fun _ -> async {
  return ("200 OK", dict [("Content-Type", "text/plain")], seq { yield "Hello world!"B :> obj }) })
let disposable = server.Start()
disposable.Dispose()