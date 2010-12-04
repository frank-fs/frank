namespace Frank
open System
open System.IO
open System.Collections.Generic
open Microsoft.Http
open FSharp.Monad
open Owin.Extensions

/// Formats an object into the specified content type using the given format function.
type Formatter = { ContentType: seq<string>; Format: obj * Stream * HttpRequestMessage -> unit }

/// The Frank monad as a request/response handler.
type Handler = State<unit, HttpRequestMessage * HttpResponseMessage * IDictionary<string,string> * seq<Formatter>>

[<AutoOpen>]
module Core =
  open System.Net
  open System.Xml
  open System.Xml.Linq
  open Frack

  type HttpContent with
    /// Reads the content as a byte string.
    member this.ReadAsByteString() : bytestring = this.ReadAsByteArray() |> Array.toSeq

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

  /// Appends the content to the existing output.
  let appends content = frank {
    let! resp = getResponse
    let bs = resp.Content.ReadAsByteString()
    let ct = resp.Content.ContentType
    do! puts (bs |> Seq.append content) ct }

  /// Selects a formatter using the Accept header from the request and the available formatters.
  // TODO: For WCF HTTP, this should delegate to the MediaTypeProcessor.
  let getFormatter() = frank {
    let! request = getRequest
    let accepts = request.Headers.Accept
                  |> Seq.map (fun a -> ((if a.Quality.HasValue then a.Quality.Value else 0.0), a.Value))
                  |> Seq.sort
    let aset = accepts |> Seq.map snd |> Set.ofSeq
    let! formatters = getFormatters
    return seq {
      for f in formatters do
        let fset = f.ContentType |> Set.ofSeq
        let intersect = Set.intersect aset fset
        if intersect |> Set.count > 0 then
          yield (intersect |> Set.toSeq |> Seq.head, f.Format)
    } |> Seq.head } 

  /// Updates the state of the response content using the request's Accept header and available formatters.
  // TODO: For WCF HTTP, this should delegate to the MediaTypeProcessor.
  let format content = frank {
    let! request = getRequest
    let! (contentType, formatter) = getFormatter()
    use stream = new MemoryStream()
    formatter(content, stream, request)
    do! puts (stream.ToArray()) contentType }

  /// An active pattern to identify and safely type incoming content for rendering.
  let (|Str|Xml|Format|) (content:obj) =
    match content with
    | :? string   -> Str(content :?> string)
    | :? XElement -> Xml(content :?> XElement)
    | _           -> Format(content)

  /// Renders the content.
  let render = function
    | Str(v)    -> puts (ByteString.fromString v) "text/plain"
    | Xml(v)    -> use stream = new MemoryStream()
                   use writer = XmlWriter.Create(stream)
                   v.Save(writer)
                   puts (stream.ToByteString()) "application/xml"
    | Format(v) -> format v

  /// Updates the response state with a redirect, setting the status code to 303
  /// and the location header to the specified url.
  let redirectTo (url:string) = frank {
    let resp = new HttpResponseMessage(StatusCode = HttpStatusCode.RedirectMethod)
    resp.Headers.Location <- Uri(url, UriKind.RelativeOrAbsolute)
    do! putResponse resp }

  // TODO: Add more helper methods to mutate only portions of the request or response objects.
