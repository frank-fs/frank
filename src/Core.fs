namespace Frank
[<AutoOpen>]
module Core =
  open System
  open System.Collections.Generic
  open System.Net
  open Microsoft.Http
  open FSharp.Monad

  /// Defines the standard Frank application type.
  type App = Func<HttpRequestMessage, HttpResponseMessage>

  /// Defines a handler for responding requests.
  type FrankHandler = IDictionary<string,string> -> State<HttpResponseMessage, HttpRequestMessage * HttpResponseMessage>

  let frank = State.StateBuilder()

  /// Gets the current state of the request.
  let getRequest = frank {
    let! (req:HttpRequestMessage, _:HttpResponseMessage) = getState
    return req }

  /// Gets the current state of the response.
  let getResponse = frank {
    let! (_:HttpRequestMessage, resp:HttpResponseMessage) = getState
    return resp }

  /// Updates the state of the request.
  let putRequest req = frank {
    let! (_:HttpRequestMessage, resp:HttpResponseMessage) = getState
    do! putState (req, resp)
    return resp }

  /// Updates the state of the response.
  let putResponse resp = frank {
    let! (req:HttpRequestMessage, _:HttpResponseMessage) = getState
    do! putState (req, resp)
    return resp }

  /// Updates the state of the response content.
  let putContent content = frank {
    let! (req:HttpRequestMessage, resp:HttpResponseMessage) = getState
    resp.StatusCode <- HttpStatusCode.OK
    resp.Content <- content
    do! putState (req, resp)
    return resp }

  /// Updates the state of the response content with plain text.
  let putPlainText (content:string) = frank {
    let! (req:HttpRequestMessage, resp:HttpResponseMessage) = getState
    resp.StatusCode <- HttpStatusCode.OK
    resp.Content <- HttpContent.Create(content, "text/plain")
    do! putState (req, resp)
    return resp }

  // TODO: Add more helper methods to mutate only portions of the request or response objects.

  /// Runs the FrankHandler 
  let run (h: FrankHandler) parms initialState = eval (h parms) initialState
