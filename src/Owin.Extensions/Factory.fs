namespace Owin

module Request =
  /// <summary>Creates an IRequest from a begin/end methods.</summary>
  let Create(methd, uri, headers, items, beginRead, endRead) =
    { new IRequest with
        member this.Method = methd
        member this.Uri = uri
        member this.Headers = headers
        member this.Items = items
        member this.BeginReadBody(buffer, offset, count, callback, state) =
          beginRead(buffer, offset, count, callback, state)
        member this.EndReadBody(result) = endRead(result) }
  /// <summary>Creates an IRequest from an Async computation.</summary>
  let fromAsync methd uri headers items asyncRead =
    let beginRead, endRead, cancelRead = Async.AsBeginEnd asyncRead
    Create(methd, uri, headers, items, (fun (b,o,c,cb,s) -> beginRead((b,o,c),cb,s)), endRead)

module Response =
  let Create(status, headers, getBody) =
    { new IResponse with
        member this.Status = status
        member this.Headers = headers
        member this.GetBody() = getBody() |> Seq.map (fun o -> o :> obj) }

module Application =
  let Create(beginInvoke, endInvoke) = 
    { new IApplication with
        member this.BeginInvoke(request, callback, state) = beginInvoke(request, callback, state)
        member this.EndInvoke(result) = endInvoke(result) }
  let fromAsync asyncInvoke =
    let beginInvoke, endInvoke, cancelInvoke = Async.AsBeginEnd asyncInvoke
    Create(beginInvoke, endInvoke)
