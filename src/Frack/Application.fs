namespace Frack
open System
open System.Collections.Generic
open Owin

/// <summary>Creates an Owin.IApplication.</summary>
type Application(beginInvoke, endInvoke) =
  /// <summary>Creates an Owin.IApplication.</summary>
  new (asyncInvoke: IRequest -> Async<IResponse>) =
    let beginInvoke, endInvoke, cancelInvoke = Async.AsBeginEnd(asyncInvoke)
    Application(beginInvoke, endInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: IRequest -> IResponse) =
    let asyncInvoke req = async { return invoke req }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (asyncInvoke: IRequest -> Async<string * IDictionary<string, seq<string>> * seq<seq<byte>>>) =
    let asyncInvoke req = async {
      let! status, headers, body = asyncInvoke req
      return Response.FromByteStrings(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: IRequest -> string * IDictionary<string, seq<string>> * seq<seq<byte>>) =
    let asyncInvoke req = async {
      let status, headers, body = invoke req
      return Response.FromByteStrings(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (asyncInvoke: IRequest -> Async<string * IDictionary<string, seq<string>> * seq<byte[]>>) =
    let asyncInvoke req = async {
      let! status, headers, body = asyncInvoke req
      return Response.FromByteArrays(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: IRequest -> string * IDictionary<string, seq<string>> * seq<byte[]>) =
    let asyncInvoke req = async {
      let status, headers, body = invoke req
      return Response.FromByteArrays(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (asyncInvoke: IRequest -> Async<string * IDictionary<string, seq<string>> * seq<byte>>) =
    let asyncInvoke req = async {
      let! status, headers, body = asyncInvoke req
      return Response.FromByteString(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: IRequest -> string * IDictionary<string, seq<string>> * seq<byte>) =
    let asyncInvoke req = async {
      let status, headers, body = invoke req
      return Response.FromByteString(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (asyncInvoke: IRequest -> Async<string * IDictionary<string, seq<string>> * byte[]>) =
    let asyncInvoke req = async {
      let! status, headers, body = asyncInvoke req
      return Response.FromByteArray(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: IRequest -> string * IDictionary<string, seq<string>> * byte[]) =
    let asyncInvoke req = async {
      let status, headers, body = invoke req
      return Response.FromByteArray(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: IRequest -> Async<string * IDictionary<string, seq<string>> * string>) =
    let asyncInvoke req = async {
      let! status, headers, body = invoke req
      return Response.FromString(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: IRequest -> string * IDictionary<string, seq<string>> * string) =
    let asyncInvoke req = async {
      let status, headers, body = invoke req
      return Response.FromString(status, headers, body) }
    Application(asyncInvoke)
  interface IApplication with
    member this.BeginInvoke(request, callback, state) = beginInvoke(request, callback, state)
    member this.EndInvoke(result) = endInvoke(result)