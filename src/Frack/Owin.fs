namespace Frack
open System
open System.Collections.Generic

/// Type alias describing a HTTP request.
type Request = IDictionary<string, obj>

/// Type alias describing an HTTP response.
type Response = string * IDictionary<string, string> * seq<obj>

/// Type alias describing an application.
type Application = Request -> Async<Response>