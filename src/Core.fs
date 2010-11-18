namespace Frank
open System
open System.IO
open System.Collections.Generic
open Microsoft.Http
open FSharp.Monad

/// Formats an object into the specified content type using the given format function.
type Formatter = { ContentType:string; Format:Stream * obj -> unit }

/// The Frank monad as a request/response handler.
type Handler = State<unit, HttpRequestMessage * HttpResponseMessage * IDictionary<string,string> * seq<Formatter>>

[<AutoOpen>]
module Core =
  open System.Net
  open System.Xml
  open System.Xml.Linq
  open Frack

  /// Sets an instance of StateBuilder as the computation workflow for a Frank monad.
  let frank = State.StateBuilder()

  /// Gets the current state of the request.
  let getRequest = frank {
    let! (req:HttpRequestMessage, _:HttpResponseMessage, _:IDictionary<string,string>, _:seq<Formatter>) = getState
    return req }

  /// Gets the current state of the response.
  let getResponse = frank {
    let! (_:HttpRequestMessage, resp:HttpResponseMessage, _:IDictionary<string,string>, _:seq<Formatter>) = getState
    return resp }

  /// Gets the current state of the parameters.
  let getParams = frank {
    let! (_:HttpRequestMessage, _:HttpResponseMessage, parms:IDictionary<string,string>, _:seq<Formatter>) = getState
    return parms } 

  /// Gets the current state of the formatters.
  let getFormatters = frank {
    let! (_:HttpRequestMessage, _:HttpResponseMessage, _:IDictionary<string,string>, formatters:seq<Formatter>) = getState
    return formatters } 

  /// Updates the state of the request.
  let putRequest req = frank {
    let! (_:HttpRequestMessage, r:HttpResponseMessage, p:IDictionary<string,string>, f:seq<Formatter>) = getState
    do! putState (req, r, p, f) }

  /// Updates the state of the response.
  let putResponse resp = frank {
    let! (r:HttpRequestMessage, _:HttpResponseMessage, p:IDictionary<string,string>, f:seq<Formatter>) = getState
    do! putState (r, resp, p, f) }

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
  let ``text/plain`` (content:string) = puts (ByteString.fromString content) "text/plain"

  /// Updates the state of the response content as text/html.
  let ``text/html`` (content:string) = puts (ByteString.fromString content) "text/html"

  /// Updates the state of the response content as application/json.
  let ``application/json`` (content:string) = puts (ByteString.fromString content) "application/json"

  /// Updates the state of the response content as the specified content type.
  let xml (content:XElement) =
    use stream = new MemoryStream()
    use writer = XmlWriter.Create(stream)
    content.Save(writer)
    puts (stream.ToByteString()) "application/xml"

  /// Selects a formatter using the Accept header from the request and the available formatters.
  let getFormatter() = frank {
    let! request = getRequest
    let accepts = request.Headers.Accept
    let! formatters = getFormatters
    let formatterMatches (f:Formatter, ct, q) =
      if f.ContentType = ct then Some(f.Format, ct, q) else None
    let (f, ct, _) =
      formatters
      |> Seq.map (fun f -> seq { for a in accepts do yield (f, a.Value, a.Quality) })
      |> Seq.concat
      |> Seq.choose formatterMatches
      // TODO: Sort the results by the quality
      |> Seq.head
    return (f, ct) }

  /// Updates the state of the response content using the request's Accept header and available formatters.
  let render content = frank {
    let! (formatter, contentType) = getFormatter()
    use stream = new MemoryStream() :> Stream
    formatter(stream, content)
    do! puts (stream.ToByteString()) contentType }

  /// Updates the response state with a redirect, setting the status code to 303
  /// and the location header to the specified url.
  let redirectTo (url:string) = frank {
    let resp = new HttpResponseMessage(StatusCode = HttpStatusCode.RedirectMethod)
    resp.Headers.Location <- Uri(url, UriKind.RelativeOrAbsolute)
    do! putResponse resp }

  // TODO: Add more helper methods to mutate only portions of the request or response objects.
