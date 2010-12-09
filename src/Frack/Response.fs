namespace Frack
open System
open System.Collections.Generic
open Owin

/// <summary>Creates an Owin.IResponse.</summary>
type Response(status, headers, getBody:unit -> seq<'a>) =

  /// <summary>Creates an Owin.IResponse.</summary>
  new (status, headers, body:seq<'a>) = Response(status, headers, (fun () -> body))

  /// <summary>Creates an Owin.IResponse.</summary>
  new (status, headers, body:'a) = Response(status, headers, (fun () -> seq { yield body }))

  interface IResponse with
    member this.Status = status
    member this.Headers = headers
    member this.GetBody() = getBody() |> Seq.map (fun o -> o :> obj)

  member this.Status = status
  member this.Headers = headers
  member this.GetBody() = getBody()
