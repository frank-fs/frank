namespace Frack
open System

/// Creates a new response with an enumerable body.
type Response(status, headers, body:seq<_>) =
  /// Creates a new response with a single body element.
  new (status, headers, body) = Response(status, headers, seq { yield body })

  /// <summary>Creates an Owin.IResponse.</summary>
  interface Owin.IResponse with
    member this.Status = status
    member this.Headers = headers
    member this.GetBody() = body |> Seq.map (fun o -> o :> obj)

  member this.Status = status
  member this.Headers = headers
  member this.Body = body