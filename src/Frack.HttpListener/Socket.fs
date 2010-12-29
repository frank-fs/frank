namespace Frack.Hosting
open System
open System.Net
open System.Net.Sockets
open System.Text

[<AutoOpen>]
module Socket =
  type Sockets.Socket with
    /// Asynchronously accepts an incoming connection using an Async computation.
    member this.AsyncAccept() = Async.FromBeginEnd(this.BeginAccept, this.EndAccept)

    /// Asynchronously receives an incoming socket request using an Async computation.
    member this.AsyncReceive(buffer, offset, size) =
      let beginReceive(b,o,s,c,st) = this.BeginReceive(b, o, s, SocketFlags.None, c, st)
      Async.FromBeginEnd(buffer, offset, size, beginReceive, this.EndReceive)

    /// Asynchronously sends a response using an Async computation.
    member this.AsyncSend(buffer, offset, size) =
      let beginSend(b,o,s,c,st) = this.BeginSend(b, o, s, SocketFlags.None, c, st)
      Async.FromBeginEnd(buffer, offset, size, beginSend, this.EndSend)

/// Creates a new Frack Server from an IP address, port number, and handler.
type Server(ipAddress:IPAddress, port, handler) =
  let ipEndPoint = IPEndPoint(ipAddress, port)
  let listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
  do listener.Bind(ipEndPoint)

  /// Creates a new Frack Server from a string representation of an IP address, port number, and handler.
  new (ipAddress, port, handler) = let ip = IPAddress.Parse(ipAddress) in new Server(ip, port, handler)

  /// Start the server with an optional backlog and buffer size.
  /// backlog
  ///     The maximum length of the queue for pending socket connections.
  /// bufferSize
  ///     The buffer size for reading bytes from an incoming socket connection.
  member this.Start(?backlog, ?bufferSize) =
    let backlog = defaultArg backlog 1000
    let bufferSize = defaultArg bufferSize 1024

    let rec loop() = async {
      printfn "Waiting for a connection ..."
      let! socket = listener.AsyncAccept()
      let buffer = Array.zeroCreate<byte> bufferSize
      let! bytesRead = socket.AsyncReceive(buffer, 0, bufferSize)
      return! socket |> read bytesRead buffer (new StringBuilder()) }
    and read bytesRead buffer sb socket = async {
      // If no more bytes were read, parse and handle the request, then write the response.
      if bytesRead = 0
        then // Parse the response and invoke the handler,
             // then return the result to the write side of the loop.
             let response = handler (sb.ToString())
             return! socket |> write response
        // otherwise, keep reading.
        else sb.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead))
             let! bytesRead = socket.AsyncReceive(buffer, 0, bufferSize)
             return! socket |> read bytesRead buffer sb }
    and write request socket = async {
      // Send the response.
      printfn "The current request:\r\n%s\r\n" request
      let bytes = Encoding.ASCII.GetBytes(request)
      let! bytesSent = socket.AsyncSend(bytes, 0, bytes.Length)
      // Close the socket.
      socket.Shutdown(SocketShutdown.Both)
      socket.Close()
      // Restart the loop.
      printfn "Sent %d bytes" bytesSent
      //return! loop() }
      return () }

    listener.Listen(backlog)
    Async.Start(loop())