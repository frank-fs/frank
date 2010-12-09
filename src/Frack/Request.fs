namespace Frack
open System
open System.Collections.Generic
open Owin

/// <summary>Creates an Owin.IRequest.</summary>
type Request(methd, uri, headers, items, beginRead:Func<_,_,_,_>, endRead:Func<_,_>) =

  /// <summary>Creates an Owin.IRequest from an Async computation.</summary>
  new (methd, uri, headers, ?items, ?asyncRead) =
    let items' = defaultArg items (Dictionary<string, obj>() :> IDictionary<string, obj>)
    let asyncRead' = defaultArg asyncRead (fun (b:byte[],o:int,c:int) -> async { return 0 })
    let beginRead, endRead, cancelRead = Async.AsBeginEnd(asyncRead')
    Request(methd, uri, headers, items',
            Func<_,_,_,_>(fun (b,o,c) cb s -> beginRead((b,o,c),cb,s)),
            Func<_,_>(fun iar -> endRead(iar)))

  interface IRequest with
    member this.Method = methd
    member this.Uri = uri
    member this.Headers = headers
    member this.Items = items
    member this.BeginReadBody(buffer, offset, count, callback, state) =
      beginRead.Invoke((buffer, offset, count), callback, state)
    member this.EndReadBody(result) = endRead.Invoke(result)

  member this.Method = methd
  member this.Uri = uri
  member this.Headers = headers
  member this.Items = items
  member this.BeginReadBody(buffer, offset, count, callback, state) =
    beginRead.Invoke((buffer, offset, count), callback, state)
  member this.EndReadBody(result) = endRead.Invoke(result)

  /// <summary>Creates an IRequest from begin/end Func delegates.</summary>
  static member Create(methd, uri, headers, items, beginRead:Func<_,_,_,_>, endRead:Func<_,_>) =
    Request(methd, uri, headers, items, beginRead, endRead) :> Owin.IRequest

  /// <summary>Creates an IRequest from a Func delegate.</summary>
  static member Create(methd, uri, headers, items, read:Func<_,_,_,_>) =
    Request(methd, uri, headers, items,
            (fun (b,o,c) cb s -> read.BeginInvoke(b,o,c,cb,s)),
            (fun iar -> read.EndInvoke(iar))) :> Owin.IRequest

  /// <summary>Creates an IRequest from an Async computation.</summary>
  static member Create(methd, uri, headers, items, asyncRead) =
    Request(methd, uri, headers, items, asyncRead) :> Owin.IRequest

  /// <summary>Creates an IRequest from a begin/end functions.</summary>
  static member Create(methd, uri, headers, items, beginRead, endRead) =
    Request(methd, uri, headers, items,
            Func<_,_,_,_>(fun (b,o,c) cb s -> beginRead(b,o,c,cb,s)),
            Func<_,_>(fun iar -> endRead(iar))) :> Owin.IRequest
