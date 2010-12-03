namespace Frack

module Request =
  /// <summary>Creates an <see cref="Owin.IRequest"/>.</summary>
  let create methd uri headers items beginRead endRead =
    { new Owin.IRequest with
        member this.Method = methd
        member this.Uri = uri
        member this.Headers = headers
        member this.Items = items
        member this.BeginReadBody(buffer, offset, count, callback, state) =
          beginRead(buffer, offset, count, callback, state)
        member this.EndReadBody(result) = endRead(result) }

  /// <summary>Creates an <see cref="Owin.IRequest"/> from an <see cref="Async{T}"/> computation.</summary>
  let fromAsync methd uri headers items asyncRead =
    let beginRead, endRead, cancelRead = Async.AsBeginEnd(asyncRead)
    { new Owin.IRequest with
        member this.Method = methd
        member this.Uri = uri
        member this.Headers = headers
        member this.Items = items
        member this.BeginReadBody(buffer, offset, count, callback, state) =
          beginRead((buffer, offset, count), callback, state)
        member this.EndReadBody(result) = endRead(result) }

module Response =
  /// <summary>Creates an <see cref="Owin.IResponse"/>.</summary>
  let create status headers getBody =
    { new Owin.IResponse with
        member this.Status = status
        member this.Headers = headers
        member this.GetBody() = getBody() }

module Application =
  /// <summary>Creates an <see cref="Owin.IApplication"/>.</summary>
  let create beginInvoke endInvoke =
    { new Owin.IApplication with
        member this.BeginInvoke(request, callback, state) = beginInvoke(request, callback, state)
        member this.EndInvoke(result) = endInvoke(result) }

  /// <summary>Creates an <see cref="Owin.IApplication"/> from an <see cref="Async{T}"/> computation.</summary>
  let fromAsync asyncInvoke =
    let beginInvoke, endInvoke, cancelInvoke = Async.AsBeginEnd(asyncInvoke)
    { new Owin.IApplication with
        member this.BeginInvoke(request, callback, state) = beginInvoke(request, callback, state)
        member this.EndInvoke(result) = endInvoke(result) }