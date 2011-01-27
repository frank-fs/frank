namespace FHPS
open System
open System.Collections.Generic
open System.Net
open System.Net.Sockets
open FHPS.SocketEx

type Connection(maxreceives, maxsends, size, socket:Socket) as this =
  let socket = socket
  let maxreceives = maxreceives
  let maxsends = maxsends
  let sendPool = new BocketPool(maxsends, size, this.SendCompleted)
  let receivePool = new BocketPool(maxreceives, size, this.ReceiveCompleted)
  let mutable disposed = false
  let mutable anyErrors = false
 
  let cleanUp() =
    if not disposed then
      disposed <- true
      socket.Shutdown(SocketShutdown.Both)
      socket.Disconnect(false)
      socket.Close()
      (sendPool :> IDisposable).Dispose()
      (receivePool :> IDisposable).Dispose()
 
  member this.Start() =
    socket.ReceiveAsyncSafe(this.ReceiveCompleted, receivePool.CheckOut())
 
  member this.Stop() =
    socket.Close(2)
 
  member this.ReceiveCompleted (args: SocketAsyncEventArgs) =
    try
      match args.LastOperation with
      | SocketAsyncOperation.Receive ->
          match args.SocketError with
          | SocketError.Success ->
              socket.ReceiveAsyncSafe(this.ReceiveCompleted, receivePool.CheckOut())
              let data = Array.create args.BytesTransferred 0uy
              Buffer.BlockCopy(args.Buffer, args.Offset, data, 0, data.Length)
              let client = args.RemoteEndPoint
              args.RemoteEndPoint <- null
              data |> printfn "received data: %A"
          | _ -> args.SocketError.ToString() |> printfn "socket error on receive: %s"
      | _ -> failwith "unknown operation, should be receive"
    finally
      receivePool.CheckIn(args)

  member this.SendCompleted (args: SocketAsyncEventArgs) =
    try
      match args.LastOperation with
      | SocketAsyncOperation.Send ->
          match args.SocketError with
          | SocketError.Success -> ()
          | SocketError.NoBufferSpaceAvailable
          | SocketError.IOPending
          | SocketError.WouldBlock ->
              if not(anyErrors) then
                anyErrors <- true
                failwith "Buffer overflow or send buffer timeout"
          | _ -> args.SocketError.ToString() |> printfn "socket error on send: %s"
      | _ -> failwith "invalid operation, should be receive"
    finally
      sendPool.CheckIn(args)

  member this.Send (msg:byte[]) =
    let s = sendPool.CheckOut()
    s.SetBuffer(0, msg.Length)
    Buffer.BlockCopy(msg, 0, s.Buffer, 0, msg.Length)
    socket.SendAsyncSafe(this.SendCompleted, s)

  interface IDisposable with
    member this.Dispose() = cleanUp()