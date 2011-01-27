namespace FHPS
open System
open System.Net
open System.Net.Sockets

module SocketEx =
  type Socket with
    /// extension method to make async based call easier, this ensures the callback always gets
    /// called even if there is an error or the async method completed syncronously
    member s.InvokeAsyncMethod( asyncmethod, callback, args:SocketAsyncEventArgs) =
      let result = asyncmethod args
      if result <> true then callback args
    member s.AcceptAsyncSafe(callback, args) = s.InvokeAsyncMethod(s.AcceptAsync, callback, args)
    member s.ReceiveAsyncSafe(callback, args) = s.InvokeAsyncMethod(s.ReceiveAsync, callback, args)
    member s.SendAsyncSafe(callback, args) = s.InvokeAsyncMethod(s.SendAsync, callback, args)
    member s.DisconnectAsyncSafe(callback, args) = s.InvokeAsyncMethod(s.DisconnectAsync, callback, args)