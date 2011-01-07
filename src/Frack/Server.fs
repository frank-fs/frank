namespace Frack
open System
open System.Diagnostics
open System.Net
open System.Net.Sockets
open System.Threading

type Server() =
  static member Start(app, ?ipAddress, ?port, ?baseUri) =
    let ipAddress  = defaultArg ipAddress IPAddress.Any
    let port       = defaultArg port 80
    let baseUri    = defaultArg baseUri "/"
    let cts        = new CancellationTokenSource()
    let endPoint   = IPEndPoint(ipAddress, port)
    let listener   = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
    listener.Bind(endPoint)
    listener.Listen(int SocketOptionName.MaxConnections)
    printfn "Listening on port %d ..." port

    let rec loop() = async {
      printfn "Waiting for a request ..."
      let! socket = listener.AsyncAccept()
      let sw = Stopwatch.StartNew()
      try
        try
          let! request  = Http.receive socket baseUri
          let! response = app request
          do!  response |> Http.respond socket
        with e -> printfn "An error occurred:\r\n%s\r\n%s" e.Message e.StackTrace
      finally
        sw.Stop()
        printfn "Completed response in %d ms." sw.ElapsedMilliseconds
        socket.Shutdown(SocketShutdown.Both)
        socket.Close()
      return! loop() }

    Async.Start(loop(), cancellationToken = cts.Token)
    { new IDisposable with
        member this.Dispose() = cts.Cancel()
                                listener.Close() }

  static member Start(handler, hostname:string, ?port, ?baseUri) =
    let ipAddress = Dns.GetHostEntry(hostname).AddressList.[0]
    Server.Start(handler, ipAddress, ?port = port, ?baseUri = baseUri)