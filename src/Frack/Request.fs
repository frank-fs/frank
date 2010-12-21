namespace Frack
open System
open System.Collections.Generic

module Request =
  /// Creates an Owin.IRequest from begin/end methods.
  let FromAsyncPattern(methd, uri, headers, items, beginRead:Func<_,_,_,_>, endRead:Func<_,_>) =
    { new Owin.IRequest with
        member this.Method = methd
        member this.Uri = uri
        member this.Headers = headers
        member this.Items = items
        member this.BeginReadBody(buffer, offset, count, callback, state) =
          beginRead.Invoke((buffer, offset, count), callback, state)
        member this.EndReadBody(result) = endRead.Invoke(result) }

  /// Creates an Owin.IRequest from an Async computation.
  let FromAsync(methd, uri, headers, items, asyncRead) =
    let beginRead, endRead, cancelRead = Async.AsBeginEnd(asyncRead)
    FromAsyncPattern(methd, uri, headers, items,
                     Func<_,_,_,_>(fun (b,o,c) cb s -> beginRead((b,o,c),cb,s)),
                     Func<_,_>(fun iar -> endRead(iar)))

  /// Creates an IRequest from begin/end functions.
  let FromBeginEnd(methd, uri, headers, items, beginRead:byte[] * int * int * AsyncCallback * obj -> IAsyncResult, endRead) =
    let asyncRead = fun (b, o, c) -> Async.FromBeginEnd(b, o, c, beginRead, endRead)
    FromAsync(methd, uri, headers, items, asyncRead)

  /// Creates an IRequest from a Func delegate.
  let Create(methd, uri, headers, items, read:Func<byte[],int,int,int>) =
    FromBeginEnd(methd, uri, headers, items, read.BeginInvoke, read.EndInvoke)
