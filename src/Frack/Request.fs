namespace Frack
open System
open System.Collections.Generic
open Owin

/// <summary>Creates an Owin.IRequest.</summary>
type Request(methd, uri, headers, items, beginRead, endRead) =
  /// <summary>Creates an Owin.IRequest from an Async computation.</summary>
  new (methd, uri, headers, ?items, ?asyncRead) =
    let items' = defaultArg items (Dictionary<string, obj>() :> IDictionary<string, obj>)
    let asyncRead' = defaultArg asyncRead (fun (b:byte[],o:int,c:int) -> async { return 0 })
    let beginRead, endRead, cancelRead = Async.AsBeginEnd(asyncRead')
    Request(methd, uri, headers, items', (fun (b,o,c,cb,s) -> beginRead((b,o,c),cb,s)), endRead)
  interface IRequest with
    member this.Method = methd
    member this.Uri = uri
    member this.Headers = headers
    member this.Items = items
    member this.BeginReadBody(buffer, offset, count, callback, state) =
      beginRead(buffer, offset, count, callback, state)
    member this.EndReadBody(result) = endRead(result)
  /// <summary>Creates an Owin.IRequest from a begin/end methods.</summary>
  static member FromBeginEnd(methd, uri, headers, items, beginRead, endRead) =
    Request(methd, uri, headers, items, beginRead, endRead) :> IRequest
  /// <summary>Creates an Owin.IRequest from an Async computation.</summary>
  static member FromAsync(methd, uri, headers, ?items, ?asyncRead) =
    let items' = defaultArg items (Dictionary<string, obj>() :> IDictionary<string, obj>)
    let asyncRead' = defaultArg asyncRead (fun (b:byte[],o:int,c:int) -> async { return 0 })
    Request(methd, uri, headers, items', asyncRead') :> IRequest