namespace Frack

[<AutoOpen>]
module Socket =
  open System.Net.Sockets

  type Socket with
    member socket.AsyncAccept() = Async.FromBeginEnd(socket.BeginAccept, socket.EndAccept)
    member socket.AsyncReceive(buffer:byte[], ?offset, ?count) =
      let offset = defaultArg offset 0
      let count = defaultArg count buffer.Length
      let beginReceive(b,o,c,cb,s) = socket.BeginReceive(b,o,c,SocketFlags.None,cb,s)
      Async.FromBeginEnd(buffer, offset, count, beginReceive, socket.EndReceive)
    member socket.AsyncSend(buffer:byte[], ?offset, ?count) =
      let offset = defaultArg offset 0
      let count = defaultArg count buffer.Length
      let beginSend(b,o,c,cb,s) = socket.BeginSend(b,o,c,SocketFlags.None,cb,s)
      let endSend(iar) = socket.EndSend(iar) |> ignore
      Async.FromBeginEnd(buffer, offset, count, beginSend, endSend)