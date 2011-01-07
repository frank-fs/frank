namespace Frack
open System
open System.Collections.Generic

/// Type alias describing a OWIN request.
type Request = IDictionary<string, obj>

/// Type alias describing an OWIN response.
type Response = string * IDictionary<string, string> * seq<obj>

/// Type alias describing an OWIN application.
type Application = Action<Request, Action<Response>, Action<exn>>

/// Type alias for a Frack HTTP handler.
type Handler = Request -> Async<Response>

module Owin =
  /// Creates an OWIN application from an Async computation.
  let create handler cancellationToken =
    Action<_>(fun (request:Request, onCompleted:Action<Response>, onException:Action<exn>) ->
      Async.StartWithContinuations(
        handler request, onCompleted.Invoke, onException.Invoke, onException.Invoke,
        cancellationToken = cancellationToken))