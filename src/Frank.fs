(* # Frank

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2011-2012, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)
[<AutoOpen>]
module Frank.Core

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Formatting
open System.Net.Http.Headers
open System.Text
open FSharpx
open FSharpx.Reader

// ## HTTP Response Header Combinators

// Headers are added using the `Reader` monad. If F# allows mutation, why do we need the monad?
// First of all, it allows for the explicit declaration of side effects. Second, a number
// of combinators are already defined that allows you to more easily compose headers.
type HttpResponseHeadersBuilder = Reader<HttpResponseMessage, unit>
let respond statusCode headers content =
  let response = new HttpResponseMessage(statusCode, Content = content) in
  headers response; response

// ### General Headers
let Date x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Date <- Nullable.create x

let Connection x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Connection.ParseAdd x

let Trailer x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Trailer.ParseAdd x

let ``Transfer-Encoding`` x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.TransferEncoding.ParseAdd x

let Upgrade x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Upgrade.ParseAdd x

let Via x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Via.ParseAdd x

let ``Cache-Control`` x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.CacheControl <- CacheControlHeaderValue.Parse x

let Pragma x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Pragma.ParseAdd x

// ### Response Headers
let Age x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Age <- Nullable.create x

let ``Retry-After`` x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.RetryAfter <- RetryConditionHeaderValue.Parse x

let Server x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Server.ParseAdd x

let Warning x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Warning.ParseAdd x

let ``Accept-Ranges`` x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.AcceptRanges.ParseAdd x

let Vary x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Vary.ParseAdd x

let ``Proxy-Authenticate`` x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.ProxyAuthenticate.ParseAdd x

let ``WWW-Authenticate`` x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.WwwAuthenticate.ParseAdd x

// ### Entity Headers
let Allow x : HttpResponseHeadersBuilder =
  fun response -> Seq.iter response.Content.Headers.Allow.Add x

let Location x : HttpResponseHeadersBuilder =
  fun response -> response.Headers.Location <- x

let ``Content-Disposition`` x : HttpResponseHeadersBuilder =
  fun response -> response.Content.Headers.ContentDisposition <- ContentDispositionHeaderValue x

let ``Content-Encoding`` x : HttpResponseHeadersBuilder =
  fun response -> Seq.iter response.Content.Headers.ContentEncoding.Add x

let ``Content-Language`` x : HttpResponseHeadersBuilder =
  fun response -> Seq.iter response.Content.Headers.ContentLanguage.Add x 

let ``Content-Length`` x : HttpResponseHeadersBuilder =
  fun response -> response.Content.Headers.ContentLength <- Nullable.create x

let ``Content-Location`` x : HttpResponseHeadersBuilder =
  fun response -> response.Content.Headers.ContentLocation <- x

let ``Content-MD5`` x : HttpResponseHeadersBuilder =
  fun response -> response.Content.Headers.ContentMD5 <- x

let ``Content-Range`` from _to length : HttpResponseHeadersBuilder =
  fun response -> response.Content.Headers.ContentRange <- ContentRangeHeaderValue(from, _to, length)

let ``Content-Type`` x : HttpResponseHeadersBuilder =
  fun response -> response.Content.Headers.ContentType <- MediaTypeHeaderValue x

let ETag tag isWeak : HttpResponseHeadersBuilder =
  fun response -> response.Headers.ETag <- EntityTagHeaderValue(tag, isWeak)

let Expires x : HttpResponseHeadersBuilder =
  fun response -> response.Content.Headers.Expires <- Nullable.create x

let ``Last Modified`` x : HttpResponseHeadersBuilder =
  fun response -> response.Content.Headers.LastModified <- Nullable.create x

// Response helpers - shortcuts for common responses.
let OK headers content = respond HttpStatusCode.OK headers content

// ### Allow Header Helpers

// A few responses should return allowed methods (`OPTIONS` and `405 Method Not Allowed`).
// `respondWithAllowHeader` allows both methods to share common functionality.
let private respondWithAllowHeader statusCode allowedMethods body =
  fun _ -> async {
    return respond statusCode <| Allow allowedMethods <| body }

// `OPTIONS` responses should return the allowed methods, and this helper facilitates method calls.
let options allowedMethods =
  respondWithAllowHeader HttpStatusCode.OK allowedMethods HttpContent.Empty

// In some instances, you need to respond with a `405 Message Not Allowed` response.
// The HTTP spec requires that this message include an `Allow` header with the allowed
// HTTP methods.
let ``405 Method Not Allowed`` allowedMethods =
  respondWithAllowHeader HttpStatusCode.MethodNotAllowed allowedMethods
  <| new StringContent("405 Method Not Allowed")

// ## Content Negotiation Helpers

let ``406 Not Acceptable`` =
  fun _ -> async {
    return respond HttpStatusCode.NotAcceptable ignore <| new StringContent("406 Not Acceptable") }

let findFormatterFor mediaType =
  Seq.find (fun (formatter: MediaTypeFormatter) ->
    formatter.SupportedMediaTypes
    |> Seq.map (fun value -> value.MediaType)
    |> Seq.exists ((=) mediaType))

// `formatWith` allows you to specify a specific `formatter` with which to render a representation
// of your content body.
// 
// The `Web API` tries to do this for you at this time, so this function is likely to be clobbered,
// or rather, wrapped again in another representation. Hopefully, this will get fixed in a future release.
// 
// Further note that the current solution requires creation of `ObjectContent<_>`, which is certainly
// not optimal. Hopefully this, too, will be resolved in a future release.
let formatWith (mediaType: string) formatter body =
  new ObjectContent<_>(body, formatter, mediaType) :> HttpContent

let IO stream = new StreamContent(stream) :> HttpContent
let Str s = new StringContent(s) :> HttpContent
let Formatted (s, encoding, mediaType) = new StringContent(s, encoding, mediaType) :> HttpContent
let Form pairs = new FormUrlEncodedContent(pairs |> Seq.map (fun (k,v) -> new KeyValuePair<_,_>(k,v))) :> HttpContent
let Bytes bytes = new ByteArrayContent(bytes) :> HttpContent
let Segment (segment: ArraySegment<byte>) =
  new ByteArrayContent(segment.Array, segment.Offset, segment.Count) :> HttpContent

type internal ConnegFormatter(mediaType) as x =
  inherit MediaTypeFormatter()
  do x.SupportedMediaTypes.Add(MediaTypeHeaderValue(mediaType))
  override x.CanReadType(_) = true
  override x.CanWriteType(_) = true

let negotiateMediaType formatters request =
  let negotiator = new System.Net.Http.Formatting.DefaultContentNegotiator()
  match negotiator.Negotiate(typeof<obj>, request, formatters) with
  | null -> None
  | result -> Some(result.MediaType.MediaType)

// When you want to negotiate the format of the response based on the available representations and
// the `request`'s `Accept` headers, you can `tryNegotiateMediaType`. This takes a set of available
// `formatters` and attempts to match the best with the provided `Accept` header values using
// functions from `FSharpx.Http`.
let runConneg formatters (f: HttpRequestMessage -> Async<_>) =
  let bestOf = negotiateMediaType formatters
  fun request ->
    match bestOf request with
    | Some mediaType ->
        let formatter = findFormatterFor mediaType formatters
        async {
          let! responseBody = f request
          let formattedBody = responseBody |> formatWith mediaType formatter
          return respond HttpStatusCode.OK <| ``Content-Type`` mediaType *> ``Vary`` "Accept" <| formattedBody }
    | _ -> ``406 Not Acceptable`` request
