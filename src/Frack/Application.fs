namespace Frack
open System
open System.Collections.Generic
open Owin

/// <summary>Creates an Owin.IApplication.</summary>
type Application(beginInvoke:Func<_,_,_,_>, endInvoke:Func<_,_>) =

  /// <summary>Creates an Owin.IApplication.</summary>
  new (beginInvoke:IRequest * AsyncCallback * obj -> IAsyncResult, endInvoke:IAsyncResult -> IResponse) = 
    Application(Func<_,_,_,_>(fun r cb s -> beginInvoke(r,cb,s)), Func<_,_>(endInvoke))

  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke:Func<_,_>) = Application(invoke.BeginInvoke, invoke.EndInvoke)

  /// <summary>Creates an Owin.IApplication.</summary>
  new (asyncInvoke: IRequest -> Async<IResponse>) =
    let beginInvoke, endInvoke, cancelInvoke = Async.AsBeginEnd(asyncInvoke)
    Application(beginInvoke, endInvoke)

  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: IRequest -> IResponse) =
    let asyncInvoke req = async { return invoke req }
    Application(asyncInvoke)

  /// <summary>Creates an Owin.IApplication.</summary>
  new (asyncInvoke: IRequest -> Async<string * IDictionary<string, seq<string>> * seq<byte[]>>) =
    let asyncInvoke req = async {
      let! status, headers, body = asyncInvoke req
      return Response(status, headers, body) :> IResponse }
    Application(asyncInvoke)

  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: IRequest -> string * IDictionary<string, seq<string>> * seq<byte[]>) =
    let asyncInvoke req = async {
      let status, headers, body = invoke req
      return Response(status, headers, body) :> IResponse }
    Application(asyncInvoke)

  /// <summary>Creates an Owin.IApplication.</summary>
  new (asyncInvoke: IRequest -> Async<string * IDictionary<string, seq<string>> * byte[]>) =
    let asyncInvoke req = async {
      let! status, headers, body = asyncInvoke req
      return Response(status, headers, body) :> IResponse }
    Application(asyncInvoke)

  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: IRequest -> string * IDictionary<string, seq<string>> * byte[]) =
    let asyncInvoke req = async {
      let status, headers, body = invoke req
      return Response(status, headers, body) :> IResponse }
    Application(asyncInvoke)

  /// <summary>Creates an Owin.IApplication.</summary>
  new (asyncInvoke: IRequest -> Async<string * IDictionary<string, seq<string>> * ArraySegment<byte>>) =
    let asyncInvoke req = async {
      let! status, headers, body = asyncInvoke req
      return Response(status, headers, body) :> IResponse }
    Application(asyncInvoke)

  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: IRequest -> string * IDictionary<string, seq<string>> * ArraySegment<byte>) =
    let asyncInvoke req = async {
      let status, headers, body = invoke req
      return Response(status, headers, body) :> IResponse }
    Application(asyncInvoke)

  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: IRequest -> Async<string * IDictionary<string, seq<string>> * string>) =
    let asyncInvoke req = async {
      let! status, headers, body = invoke req
      return Response(status, headers, body) :> IResponse }
    Application(asyncInvoke)

  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: IRequest -> string * IDictionary<string, seq<string>> * string) =
    let asyncInvoke req = async {
      let status, headers, body = invoke req
      return Response(status, headers, body) :> IResponse }
    Application(asyncInvoke)

  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: IRequest -> Async<string * IDictionary<string, seq<string>> * System.IO.FileInfo>) =
    let asyncInvoke req = async {
      let! status, headers, body = invoke req
      return Response(status, headers, body) :> IResponse }
    Application(asyncInvoke)

  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: IRequest -> string * IDictionary<string, seq<string>> * System.IO.FileInfo) =
    let asyncInvoke req = async {
      let status, headers, body = invoke req
      return Response(status, headers, body) :> IResponse }
    Application(asyncInvoke)

  interface IApplication with
    member this.BeginInvoke(request, callback, state) = beginInvoke.Invoke(request, callback, state)
    member this.EndInvoke(result) = endInvoke.Invoke(result)

  member this.BeginInvoke(request, callback, state) = beginInvoke.Invoke(request, callback, state)
  member this.EndInvoke(result) = endInvoke.Invoke(result)