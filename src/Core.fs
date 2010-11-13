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

  /// The Frank monad as a request/response handler.
  type FrankHandler = State<unit, HttpRequestMessage * HttpResponseMessage>

  /// Sets an instance of StateBuilder as the computation workflow for a Frank monad.
  let frank = State.StateBuilder()

  /// Runs a Frank handler with an initial state and returns an HttpResponseMessage.
  let run (h:FrankHandler) initialState = exec h initialState |> snd

  /// Gets the current state of the request.
  let getRequest = frank {
    let! (req:HttpRequestMessage, _:HttpResponseMessage) = getState
    return req }

  /// Gets the current state of the parameters.
  let getParams = frank {
    let! req = getRequest
    return req.GetPropertyOrDefault<IDictionary<string,string>>() } 

  /// Gets the current state of the response.
  let getResponse = frank {
    let! (_:HttpRequestMessage, resp:HttpResponseMessage) = getState
    return resp }

  /// Updates the state of the request.
  let putRequest req = frank {
    let! (_:HttpRequestMessage, resp:HttpResponseMessage) = getState
    do! putState (req, resp) }

  /// Updates the state of the response.
  let putResponse resp = frank {
    let! (req:HttpRequestMessage, _:HttpResponseMessage) = getState
    do! putState (req, resp) }

  /// Updates the state of the response content.
  let putContent content = frank {
    let! resp = getResponse
    resp.StatusCode <- HttpStatusCode.OK
    resp.Content <- content
    do! putResponse resp }

  /// Updates the state of the response content as text/plain.
  let putText (content:string) = putContent (HttpContent.Create(content, "text/plain"))

  /// Updates the state of the response content as text/html.
  let putHtml (content:string) = putContent (HttpContent.Create(content, "text/html"))

  /// Updates the state of the response content as text/json.
  let putJson (content:string) = putContent (HttpContent.Create(content, "text/json"))

  /// Updates the state of the response content as application/xml.
  let putXml (content:string) = putContent (HttpContent.Create(content, "application/xml"))

  /// Updates the response state with a redirect, setting the status code to 303
  /// and the location header to the specified url.
  let redirectTo (url:string) = frank {
    let resp = new HttpResponseMessage(StatusCode = HttpStatusCode.RedirectMethod)
    resp.Headers.Location <- Uri(url, UriKind.RelativeOrAbsolute)
    do! putResponse resp }

  // TODO: Add more helper methods to mutate only portions of the request or response objects.