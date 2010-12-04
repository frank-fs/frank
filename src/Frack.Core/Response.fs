namespace Frack
open System
open System.Collections.Generic
open Owin
open Owin.Extensions

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
  interface IResponse with
    member this.Status = status
    member this.Headers = headers
    member this.GetBody() = getBody() |> Seq.map (fun o -> o :> obj)
  /// <summary>Creates an Owin.IResponse.</summary>
  static member FromString(status, headers, body:string)=
    Response(status, headers, body) :> IResponse
  /// <summary>Creates an Owin.IResponse.</summary>
  static member FromStringArray(status, headers, body:string[])=
    Response(status, headers, body) :> IResponse
  /// <summary>Creates an Owin.IResponse.</summary>
  static member FromStrings(status, headers, body:seq<string>)=
    Response(status, headers, body) :> IResponse
  /// <summary>Creates an Owin.IResponse.</summary>
  static member FromByteArray(status, headers, body:byte[]) =
    Response(status, headers, body) :> IResponse
  /// <summary>Creates an Owin.IResponse.</summary>
  static member FromByteString(status, headers, body:bytestring) =
    Response(status, headers, body) :> IResponse
  /// <summary>Creates an Owin.IResponse.</summary>
  static member FromByteArrays(status, headers, body:#seq<byte[]>) =
    Response(status, headers, body) :> IResponse
  /// <summary>Creates an Owin.IResponse.</summary>
  static member FromByteStrings(status, headers, body:seq<seq<byte>>) =
    Response(status, headers, body) :> IResponse
  /// <summary>Creates an Owin.IResponse.</summary>
  static member FromByteStrings(status, headers, getBody:unit -> seq<seq<byte>>) =
    Response(status, headers, getBody) :> IResponse