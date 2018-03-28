﻿(* # Frank

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2011-2018, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)
module Frank.Http

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Net.Http.Formatting
open System.Net.Http.Headers
open System.Threading.Tasks

// ## Define the web application interface

(*
One may define a web application interface using a large variety of signatures. Indeed, if you search the web, you're likely to find a large number of approaches. When starting with `Frank`, I wanted to try to find a way to define an HTTP application using pure functions and function composition. The closest I found was the following:

        type HttpApplication = HttpRequestMessage -> Async<HttpResponseMessage>

Alas, this approach works only so well. HTTP is a rich communication specification. The simplicity and elegance of a purely functional approach quickly loses the ability to communicate back options to the client. For instance, given the above, how do you return a meaningful `405 Method Not Allowed` response? The HTTP specification requires that you list the allowed methods, but if you merge all the logic for selecting an application into the functions, there is no easy way to recall all the allowed methods, short of trying them all. You could require that the developer add the list of used methods, but that, too, misses the point that the application should be collecting this and helping the developer by taking care of all of the nuts and bolts items.

The next approach I tried involved using a tuple of a list of allowed HTTP methods and the application handler, which used the merged function approach described above for actually executing the application. However, once again, there are limitations. This structure accurately represents a resource, but it does not allow for multiple resources to coexist side-by-side. Another tuple of uri pattern matching expressions could wrap a list of these method * handler tuples, but at this point I realized I would be better served by using real types and thus arrived at the signatures below.

You'll see the signatures above are still mostly present, though they have been changed to better fit the signatures below.
*)

// `HttpApplication` defines the contract for processing any request.
// An application takes an `HttpRequestMessage` and returns an `HttpRequestHandler` asynchronously.
type HttpApplication = HttpRequestMessage -> Async<HttpResponseMessage>

/// An empty `HttpContent` type.
type EmptyContent() =
    inherit HttpContent()
    override x.SerializeToStreamAsync(stream, context) =
        let tcs = new TaskCompletionSource<_>(TaskCreationOptions.None)
        tcs.SetResult(())
        tcs.Task :> Task
    override x.TryComputeLength(length) =
        length <- 0L
        true
    override x.Equals(other) =
        other.GetType() = typeof<EmptyContent>
    override x.GetHashCode() = hash x

let private emptyContent = new EmptyContent() :> HttpContent

type HttpContent with
    /// Returns an `EmptyContent`.
    member x.Empty = emptyContent

// ## HTTP Response Header Combinators

// Headers are added using the `Reader` computation expression from F#x: `HttpResponseMessage -> unit`
// If F# allows mutation, why do we need the monad?
// First of all, it allows for the explicit declaration of side effects. Second, a number
// of combinators are already defined that allows you to more easily compose headers.

let respond statusCode headers content (request: HttpRequestMessage) =
    let response =
        match content with
        | Some c -> new HttpResponseMessage(statusCode, Content = c, RequestMessage = request)
        | None   -> new HttpResponseMessage(statusCode, RequestMessage = request)
    headers response
    response

// ### General Headers
let Date x (response: HttpResponseMessage) =
    response.Headers.Date <- Nullable<_> x

let Connection x (response: HttpResponseMessage) =
    response.Headers.Connection.ParseAdd x

let Trailer x (response: HttpResponseMessage) =
    response.Headers.Trailer.ParseAdd x

let ``Transfer-Encoding`` x (response: HttpResponseMessage) =
    response.Headers.TransferEncoding.ParseAdd x

let Upgrade x (response: HttpResponseMessage) =
    response.Headers.Upgrade.ParseAdd x

let Via x (response: HttpResponseMessage) =
    response.Headers.Via.ParseAdd x

let ``Cache-Control`` x (response: HttpResponseMessage) =
    response.Headers.CacheControl <- CacheControlHeaderValue.Parse x

let Pragma x (response: HttpResponseMessage) =
    response.Headers.Pragma.ParseAdd x

// ### Response Headers
let Age x (response: HttpResponseMessage) =
    response.Headers.Age <- Nullable<_> x

let ``Retry-After`` x (response: HttpResponseMessage) =
    response.Headers.RetryAfter <- RetryConditionHeaderValue.Parse x

let Server x (response: HttpResponseMessage) =
    response.Headers.Server.ParseAdd x

let Warning x (response: HttpResponseMessage) =
    response.Headers.Warning.ParseAdd x

let ``Accept-Ranges`` x (response: HttpResponseMessage) =
    response.Headers.AcceptRanges.ParseAdd x

let Vary x (response: HttpResponseMessage) =
    response.Headers.Vary.ParseAdd x

let ``Proxy-Authenticate`` x (response: HttpResponseMessage) =
    response.Headers.ProxyAuthenticate.ParseAdd x

let ``WWW-Authenticate`` x (response: HttpResponseMessage) =
    response.Headers.WwwAuthenticate.ParseAdd x

// ### Entity Headers
let Allow (allowedMethods: #seq<HttpMethod>) (response: HttpResponseMessage) =
    allowedMethods
    |> Seq.map (fun (m: HttpMethod) -> m.Method)
    |> Seq.iter response.Content.Headers.Allow.Add

let Location x (response: HttpResponseMessage) =
    response.Headers.Location <- x

let ``Content-Disposition`` x (response: HttpResponseMessage) =
    response.Content.Headers.ContentDisposition <- ContentDispositionHeaderValue x

let ``Content-Encoding`` x (response: HttpResponseMessage) =
    Seq.iter response.Content.Headers.ContentEncoding.Add x

let ``Content-Language`` x (response: HttpResponseMessage) =
    Seq.iter response.Content.Headers.ContentLanguage.Add x 

let ``Content-Length`` x (response: HttpResponseMessage) =
    response.Content.Headers.ContentLength <- Nullable<_> x

let ``Content-Location`` x (response: HttpResponseMessage) =
    response.Content.Headers.ContentLocation <- x

let ``Content-MD5`` x (response: HttpResponseMessage) =
    response.Content.Headers.ContentMD5 <- x

let ``Content-Range`` from ``to`` length (response: HttpResponseMessage) =
    response.Content.Headers.ContentRange <- ContentRangeHeaderValue(from, ``to``, length)

let ``Content-Type`` x (response: HttpResponseMessage) =
    response.Content.Headers.ContentType <- MediaTypeHeaderValue x

let ETag tag isWeak (response: HttpResponseMessage) =
    response.Headers.ETag <- EntityTagHeaderValue(tag, isWeak)

let Expires x (response: HttpResponseMessage) =
    response.Content.Headers.Expires <- Nullable<_> x

let ``Last Modified`` x (response: HttpResponseMessage) =
    response.Content.Headers.LastModified <- Nullable<_> x

// Response helpers - shortcuts for common responses.
let OK headers content = respond HttpStatusCode.OK headers content

// ### Allow Header Helpers

// A few responses should return allowed methods (`OPTIONS` and `405 Method Not Allowed`).
// `respondWithAllowHeader` allows both methods to share common functionality.
let private respondWithAllowHeader statusCode allowedMethods body request =
        respond statusCode
        <| Allow allowedMethods
        <| body
        <| request
        |> async.Return

// `OPTIONS` responses should return the allowed methods, and this helper facilitates method calls.
let options allowedMethods =
    respondWithAllowHeader HttpStatusCode.OK allowedMethods <| Some emptyContent

// In some instances, you need to respond with a `405 Message Not Allowed` response.
// The HTTP spec requires that this message include an `Allow` header with the allowed
// HTTP methods.
let ``405 Method Not Allowed`` allowedMethods =
    respondWithAllowHeader HttpStatusCode.MethodNotAllowed allowedMethods
    <| Some (new StringContent("405 Method Not Allowed"))

// ## Request body helpers

let readFormUrlEncoded<'T> (content:HttpContent) =
    async {
        let! token = Async.CancellationToken
        let! body = content.ReadAsFormDataAsync(token) |> Async.AwaitTask
        return Map.ofList [ for key in body -> key, body.[key] ]
    }

// ## Content Negotiation Helpers

let ``406 Not Acceptable`` request =
    request
    |> respond HttpStatusCode.NotAcceptable ignore (Some(new StringContent("406 Not Acceptable")))
    |> async.Return

let findFormatterFor mediaType formatters =
    formatters
    |> Seq.find (fun (formatter: MediaTypeFormatter) ->
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
// `formatters` and attempts to match the best with the provided `Accept` header values.
let runConneg formatters (f: HttpRequestMessage -> Async<_>) =
    let bestOf = negotiateMediaType formatters
    fun request ->
        match bestOf request with
        | Some mediaType ->
                let formatter = findFormatterFor mediaType formatters
                async {
                    let! responseBody = f request
                    let formattedBody = responseBody |> formatWith mediaType formatter
                    let applyHeaders response =
                        ``Content-Type`` mediaType response
                        ``Vary`` "Accept" response
                    return respond HttpStatusCode.OK applyHeaders (Some formattedBody) request
                }
        | _ -> ``406 Not Acceptable`` request

/// Adds a default response of "405 Method Not Allowed" to a handler supporting the specified methods.
let resourceHandlerOrDefault methods handler (request: HttpRequestMessage) =
    match handler request with
    | Some response -> response
    | _ ->
        async {
            let response = request.CreateResponse(HttpStatusCode.MethodNotAllowed, Content = new StringContent("405 Method Not Allowed"))
            methods
            |> Seq.map (fun (m: HttpMethod) -> m.Method)
            |> Seq.iter response.Content.Headers.Allow.Add
            return response
        }

/// Adapts an `HttpApplication` function into a `System.Net.Http.DelegatingHandler`.
type AsyncHandler =
    inherit DelegatingHandler
    val AsyncSend : HttpRequestMessage -> Async<HttpResponseMessage>
    new (f, inner) = { inherit DelegatingHandler(inner); AsyncSend = f }
    new (f) = { inherit DelegatingHandler(); AsyncSend = f }
    override x.SendAsync(request, cancellationToken) =
        Async.StartAsTask(x.AsyncSend request, cancellationToken = cancellationToken)
