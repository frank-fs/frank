namespace Frank
open System
open Microsoft.Http
open FSharp.Monad

/// Defines the standard Frank application type.
type App = Func<HttpRequestMessage, HttpResponseMessage>

/// The Frank monad as a request/response handler.
type FrankHandler = State<unit, HttpRequestMessage * HttpResponseMessage>

[<AutoOpen>]
module Core =
  open System.Collections.Generic
  open System.IO
  open System.Net
  open System.Runtime.Serialization
  open System.Xml
  open System.Xml.Linq
  open System.Xml.Serialization
  open Frack

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

  /// Updates the state of the response headers with the specified name-value pair.
  let appendHeader name (value:string) = frank {
    let! resp = getResponse
    resp.Headers.Add(name, value)
    do! putResponse resp }

  /// Updates the state of the response headers with the specified name-value pair.
  let appendHeaderValues name (values:string[]) = frank {
    let! resp = getResponse
    resp.Headers.Add(name, values)
    do! putResponse resp }

  /// Updates the state of the response content.
  let putContent content = frank {
    let! resp = getResponse
    resp.StatusCode <- HttpStatusCode.OK
    resp.Content <- content
    do! putResponse resp }

  /// Updates the state of the response content with the provided bytestring as the specified content type.
  let puts content contentType = putContent (HttpContent.Create(content |> Seq.toArray, contentType))

  /// Updates the state of the response content as text/plain.
  let putText (content:string) = puts (ByteString.fromString content) "text/plain"

  /// Updates the state of the response content as text/html.
  let putHtml (content:string) = puts (ByteString.fromString content) "text/html"

  /// Updates the state of the response content as application/json.
  let putJson (content:string) = puts (ByteString.fromString content) "application/json"

  /// Updates the state of the response content as the specified content type.
  let putXml (content:XElement) contentType =
    use stream = new MemoryStream()
    use writer = XmlWriter.Create(stream)
    content.Save(writer)
    puts (stream.ToByteString()) contentType

  /// Updates the state of the response content serialized using the serialize function as the specified content type.
  let putSerialized content contentType serialize =
    use stream = new MemoryStream()
    serialize(stream, content)
    puts (stream.ToByteString()) contentType

  /// Updates the state of the response content serialized with a data contract as application/json.
  let putAsJson content =
    let serializer = System.Runtime.Serialization.Json.DataContractJsonSerializer(content.GetType())
    putSerialized content "application/json" serializer.WriteObject

  /// Updates the state of the response content with content object serialized as XML as the specified content type.
  let putAsXml content contentType =
    let serializer = XmlSerializer(content.GetType())
    putSerialized content contentType serializer.Serialize

  /// Updates the state of the response content serialized with a data contract as the specified content type.
  let putDataContract content contentType =
    let serializer = DataContractSerializer(content.GetType())
    putSerialized content contentType serializer.WriteObject

  /// Updates the response state with a redirect, setting the status code to 303
  /// and the location header to the specified url.
  let redirectTo (url:string) = frank {
    let resp = new HttpResponseMessage(StatusCode = HttpStatusCode.RedirectMethod)
    resp.Headers.Location <- Uri(url, UriKind.RelativeOrAbsolute)
    do! putResponse resp }

  // TODO: Add more helper methods to mutate only portions of the request or response objects.
