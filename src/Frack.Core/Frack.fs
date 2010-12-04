namespace Frack
open System
open System.Collections.Generic

/// <summary>Creates an Owin.IRequest.</summary>
type Request(methd, uri, headers, items, beginRead, endRead) =
  /// <summary>Creates an Owin.IRequest from an <see cref="Async{T}"/> computation.</summary>
  new (methd, uri, headers, ?items, ?asyncRead) =
    let items' = defaultArg items (Dictionary<string, obj>() :> IDictionary<string, obj>)
    let asyncRead' = defaultArg asyncRead (fun (b:byte[],o:int,c:int) -> async { return 0 })
    let beginRead, endRead, cancelRead = Async.AsBeginEnd(asyncRead')
    Request(methd, uri, headers, items', (fun (b,o,c,cb,s) -> beginRead((b,o,c),cb,s)), endRead)
  interface Owin.IRequest with
    member this.Method = methd
    member this.Uri = uri
    member this.Headers = headers
    member this.Items = items
    member this.BeginReadBody(buffer, offset, count, callback, state) =
      beginRead(buffer, offset, count, callback, state)
    member this.EndReadBody(result) = endRead(result)
  /// <summary>Creates an Owin.IRequest from a begin/end methods.</summary>
  static member FromBeginEnd(methd, uri, headers, items, beginRead, endRead) =
    Request(methd, uri, headers, items, beginRead, endRead) :> Owin.IRequest
  /// <summary>Creates an Owin.IRequest from an <see cref="Async{T}"/> computation.</summary>
  static member FromAsync(methd, uri, headers, ?items, ?asyncRead) =
    let items' = defaultArg items (Dictionary<string, obj>() :> IDictionary<string, obj>)
    let asyncRead' = defaultArg asyncRead (fun (b:byte[],o:int,c:int) -> async { return 0 })
    Request(methd, uri, headers, items', asyncRead') :> Owin.IRequest

/// <summary>Creates an Owin.IResponse.</summary>
type Response(status, headers, getBody:unit -> seq<seq<byte>>) =
  /// <summary>Creates an Owin.IResponse.</summary>
  new (status, headers, body:seq<seq<byte>>) = Response(status, headers, (fun () -> body))
  /// <summary>Creates an Owin.IResponse.</summary>
  new (status, headers, body:seq<byte[]>) =
    Response(status, headers, (fun () -> body |> Seq.map (fun bs -> bs |> Array.toSeq)))
  /// <summary>Creates an Owin.IResponse.</summary>
  new (status, headers, body:bytestring) =
    Response(status, headers, (fun () -> seq { yield body }))
  /// <summary>Creates an Owin.IResponse.</summary>
  new (status, headers, body:byte[]) =
    Response(status, headers, (fun () -> seq { yield body |> Array.toSeq }))
  /// <summary>Creates an Owin.IResponse.</summary>
  new (status, headers, body:seq<string>) =
    Response(status, headers, (fun () -> body |> Seq.map (ByteString.fromString)))
  /// <summary>Creates an Owin.IResponse.</summary>
  new (status, headers, body:string[]) =
    Response(status, headers, (fun () -> body |> Seq.map (ByteString.fromString)))
  /// <summary>Creates an Owin.IResponse.</summary>
  new (status, headers, body:string) =
    Response(status, headers, (fun () -> seq { yield ByteString.fromString body }))
  interface Owin.IResponse with
    member this.Status = status
    member this.Headers = headers
    member this.GetBody() = getBody() |> Seq.map (fun o -> o :> obj)
  /// <summary>Creates an Owin.IResponse.</summary>
  static member Create(status, headers, body:string)=
    Response(status, headers, body) :> Owin.IResponse
  /// <summary>Creates an Owin.IResponse.</summary>
  static member Create(status, headers, body:string[])=
    Response(status, headers, body) :> Owin.IResponse
  /// <summary>Creates an Owin.IResponse.</summary>
  static member Create(status, headers, body:seq<string>)=
    Response(status, headers, body) :> Owin.IResponse
  /// <summary>Creates an Owin.IResponse.</summary>
  static member Create(status, headers, body:byte[]) =
    Response(status, headers, body) :> Owin.IResponse
  /// <summary>Creates an Owin.IResponse.</summary>
  static member Create(status, headers, body:bytestring) =
    Response(status, headers, body) :> Owin.IResponse
  /// <summary>Creates an Owin.IResponse.</summary>
  static member Create(status, headers, body:#seq<byte[]>) =
    Response(status, headers, body) :> Owin.IResponse
  /// <summary>Creates an Owin.IResponse.</summary>
  static member Create(status, headers, body:seq<seq<byte>>) =
    Response(status, headers, body) :> Owin.IResponse
  /// <summary>Creates an Owin.IResponse.</summary>
  static member Create(status, headers, getBody:unit -> seq<seq<byte>>) =
    Response(status, headers, getBody) :> Owin.IResponse

/// <summary>Creates an Owin.IApplication.</summary>
type Application(beginInvoke, endInvoke) =
  /// <summary>Creates an Owin.IApplication.</summary>
  new (asyncInvoke: Owin.IRequest -> Async<Owin.IResponse>) =
    let beginInvoke, endInvoke, cancelInvoke = Async.AsBeginEnd(asyncInvoke)
    Application(beginInvoke, endInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: Owin.IRequest -> Owin.IResponse) =
    let asyncInvoke req = async { return invoke req }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (asyncInvoke: Owin.IRequest -> Async<string * IDictionary<string, seq<string>> * seq<seq<byte>>>) =
    let asyncInvoke req = async {
      let! status, headers, body = asyncInvoke req
      return Response.Create(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: Owin.IRequest -> string * IDictionary<string, seq<string>> * seq<seq<byte>>) =
    let asyncInvoke req = async {
      let status, headers, body = invoke req
      return Response.Create(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (asyncInvoke: Owin.IRequest -> Async<string * IDictionary<string, seq<string>> * seq<byte[]>>) =
    let asyncInvoke req = async {
      let! status, headers, body = asyncInvoke req
      return Response.Create(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: Owin.IRequest -> string * IDictionary<string, seq<string>> * seq<byte[]>) =
    let asyncInvoke req = async {
      let status, headers, body = invoke req
      return Response.Create(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (asyncInvoke: Owin.IRequest -> Async<string * IDictionary<string, seq<string>> * seq<byte>>) =
    let asyncInvoke req = async {
      let! status, headers, body = asyncInvoke req
      return Response.Create(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: Owin.IRequest -> string * IDictionary<string, seq<string>> * seq<byte>) =
    let asyncInvoke req = async {
      let status, headers, body = invoke req
      return Response.Create(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (asyncInvoke: Owin.IRequest -> Async<string * IDictionary<string, seq<string>> * byte[]>) =
    let asyncInvoke req = async {
      let! status, headers, body = asyncInvoke req
      return Response.Create(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: Owin.IRequest -> string * IDictionary<string, seq<string>> * byte[]) =
    let asyncInvoke req = async {
      let status, headers, body = invoke req
      return Response.Create(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: Owin.IRequest -> Async<string * IDictionary<string, seq<string>> * string>) =
    let asyncInvoke req = async {
      let! status, headers, body = invoke req
      return Response.Create(status, headers, body) }
    Application(asyncInvoke)
  /// <summary>Creates an Owin.IApplication.</summary>
  new (invoke: Owin.IRequest -> string * IDictionary<string, seq<string>> * string) =
    let asyncInvoke req = async {
      let status, headers, body = invoke req
      return Response.Create(status, headers, body) }
    Application(asyncInvoke)
  interface Owin.IApplication with
    member this.BeginInvoke(request, callback, state) = beginInvoke(request, callback, state)
    member this.EndInvoke(result) = endInvoke(result)
