namespace Frack
open System
open System.Collections.Generic
open Owin

/// <summary>Creates an Owin.IResponse.</summary>
type Response(status, headers, getBody:unit -> seq<byte[]>) =

  /// <summary>Creates an Owin.IResponse.</summary>
  new (status, headers, body:seq<byte[]>) = Response(status, headers, (fun () -> body))

  /// <summary>Creates an Owin.IResponse.</summary>
  new (status, headers, body:seq<string>) =
    Response(status, headers, (fun () -> body |> Seq.map (ByteString.fromString >> Seq.toArray)))

  /// <summary>Creates an Owin.IResponse.</summary>
  new (status, headers, body:string) =
    Response(status, headers, (fun () -> seq { yield ByteString.fromString body |> Seq.toArray }))

  /// <summary>Creates an Owin.IResponse.</summary>
  new (status, headers, body:System.IO.FileInfo) =
    Response(status, headers, (fun () -> seq { yield ByteString.fromFileInfo body |> Seq.toArray })) 

  interface IResponse with
    member this.Status = status
    member this.Headers = headers
    member this.GetBody() = getBody() |> Seq.map (fun o -> o :> obj)

  /// <summary>Creates an Owin.IResponse.</summary>
  static member Create(status, headers, body:System.IO.FileInfo)=
    Response(status, headers, body) :> IResponse

  /// <summary>Creates an Owin.IResponse.</summary>
  static member Create(status, headers, body:string)=
    Response(status, headers, body) :> IResponse

  /// <summary>Creates an Owin.IResponse.</summary>
  static member Create(status, headers, body:seq<string>)=
    Response(status, headers, body) :> IResponse

  /// <summary>Creates an Owin.IResponse.</summary>
  static member Create(status, headers, body:seq<byte[]>) =
    Response(status, headers, body) :> IResponse

  /// <summary>Creates an Owin.IResponse.</summary>
  static member Create(status, headers, getBody:unit -> seq<byte[]>) =
    Response(status, headers, getBody) :> IResponse

