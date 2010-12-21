namespace Frack
open System

/// Creates a new response with an enumerable body.
type Response(status, headers, body:seq<_>) =

  /// Creates a new response with an enumerable body.
  static member Create(status, headers, body:seq<_>) =
    Response(status, headers, body) :> Owin.IResponse
  /// Creates a new response with a single body element.
  static member Create(status, headers, body) =
    Response(status, headers, seq { yield body }) :> Owin.IResponse

  /// The response status, e.g. "200 OK".
  member this.Status = status
  /// The response headers.
  member this.Headers = headers
  /// The response body.
  member this.Body = body

  /// <summary>Creates an Owin.IResponse.</summary>
  interface Owin.IResponse with
    /// The response status, e.g. "200 OK".
    member this.Status = status
    /// The response headers.
    member this.Headers = headers
    /// The response body.
    member this.GetBody() = body |> Seq.map (fun o -> o :> obj)
