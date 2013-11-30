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


// ## HTTP Response Header Combinators

// Headers are added using the `Reader` monad. If F# allows mutation, why do we need the monad?
// First of all, it allows for the explicit declaration of side effects. Second, a number
// of combinators are already defined that allows you to more easily compose headers.
type HttpResponseHeadersBuilder = Reader<HttpResponseMessage, unit>
let respond statusCode headers content (request: HttpRequestMessage) =
  let response =
    match content with
    | Some c -> new HttpResponseMessage(statusCode, Content = c, RequestMessage = request)
    | None   -> request.CreateResponse(statusCode)
  headers response
  response

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
let Allow (allowedMethods: #seq<HttpMethod>) : HttpResponseHeadersBuilder =
  fun response ->
    allowedMethods
    |> Seq.map (fun (m: HttpMethod) -> m.Method)
    |> Seq.iter response.Content.Headers.Allow.Add

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
let private respondWithAllowHeader statusCode allowedMethods body request =
    respond statusCode
    <| Allow allowedMethods
    <| body
    <| request
    |> async.Return

// `OPTIONS` responses should return the allowed methods, and this helper facilitates method calls.
let options allowedMethods =
  respondWithAllowHeader HttpStatusCode.OK allowedMethods None

// In some instances, you need to respond with a `405 Message Not Allowed` response.
// The HTTP spec requires that this message include an `Allow` header with the allowed
// HTTP methods.
let ``405 Method Not Allowed`` allowedMethods =
  respondWithAllowHeader HttpStatusCode.MethodNotAllowed allowedMethods
  <| Some(new StringContent("405 Method Not Allowed"))

// ## Content Negotiation Helpers

let ``406 Not Acceptable`` request =
    request
    |> respond HttpStatusCode.NotAcceptable ignore (Some(new StringContent("406 Not Acceptable")))
    |> async.Return

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
          return respond HttpStatusCode.OK <| ``Content-Type`` mediaType *> ``Vary`` "Accept" <| Some formattedBody <| request
        }
    | _ -> ``406 Not Acceptable`` request


// ## Routing

/// Alias `MailboxProcessor<'T>` as `Agent<'T>`.
type Agent<'T> = MailboxProcessor<'T>

/// Messages used by the HTTP resource agent.
type internal ResourceMessage =
    | Request of HttpRequestMessage * Stream
    | SetHandler of HttpMethod * HttpApplication
    | Error of exn
    | Shutdown

/// An HTTP resource agent.
type Resource private (uriTemplate, allowedMethods, handlers) =
    let onError = new Event<exn>()
    let agent = Agent<ResourceMessage>.Start(fun inbox ->
        let rec loop handlers = async {
            let! msg = inbox.Receive()
            match msg with
            | Request(request, out) ->
                let! response =
                    match handlers |> List.tryFind (fun (m, _) -> m = request.Method) with
                    | Some (_, h) -> h request
                    | None -> ``405 Method Not Allowed`` allowedMethods request
                do out.WriteByte(0uy) // TODO: write the response to the out stream
                return! loop handlers
            | SetHandler(httpMethod, handler) ->
                let handlers' =
                    match allowedMethods |> List.tryFind (fun m -> m = httpMethod) with
                    | None -> handlers
                    | Some _ -> (httpMethod, handler)::(List.filter (fun (m,h) -> m <> httpMethod) handlers)
                return! loop handlers'
            | Error exn ->
                onError.Trigger(exn)
                return! loop handlers
            | Shutdown -> ()
        }
            
        loop handlers
    )

    new (uriTemplate, handlers) =
        let allowedMethods = handlers |> List.map fst
        Resource(uriTemplate, allowedMethods, handlers)

    new (uriTemplate, allowedMethods) =
        Resource(uriTemplate, allowedMethods, [])

    /// Connect the resource to the request event stream.
    /// This method applies a default filter to subscribe only to events
    /// matching the `Resource`'s `uriTemplate`.
    // NOTE: This should be internal if used in a type provider.
    abstract Connect : IObservable<HttpRequestMessage * Stream> -> IDisposable
    default x.Connect(observable) =
        (observable
         |> Observable.filter (fun (r: HttpRequestMessage, _) -> r.RequestUri.AbsolutePath = uriTemplate)
        ).Subscribe(x)

    /// Sets the handler for the specified `HttpMethod`.
    /// Ideally, we would expose methods matching the allowed methods.
    member x.SetHandler(httpMethod, handler) =
        agent.Post <| SetHandler(httpMethod, handler)

    /// Provide stream of `exn` for logging purposes.
    [<CLIEvent>]
    member x.Error = onError.Publish

    /// Implement `IObserver` to allow the `Resource` to subscribe to the request event stream.
    interface IObserver<HttpRequestMessage * Stream> with
        member x.OnNext(value) = agent.Post <| Request value
        member x.OnError(exn) = agent.Post <| Error exn
        member x.OnCompleted() = agent.Post Shutdown

// TODO: Create a ResourceManager or some form of Supervisor to serve as the App.
// Example:

type App () as x =
    // Should this also be an Agent<'T>?

    let onRequest = new Event<HttpRequestMessage * Stream>()
    let onError = new Event<exn>()

    // This shows that strings are used, but they should be hidden behind generated types.
    let rootR = Resource("/", [ HttpMethod.Get ])
    let aboutR = Resource("/about", [ HttpMethod.Get ])
    let customersR = Resource("/customers", [ HttpMethod.Get; HttpMethod.Post ])
    let customerR = Resource("/customers/{id:int}", [ HttpMethod.Get; HttpMethod.Put; HttpMethod.Delete ])

    let subscriptions = [
        rootR.Connect(x :> IObservable<_>)
        aboutR.Connect(x :> IObservable<_>)
        customersR.Connect(x :> IObservable<_>)
        customerR.Connect(x :> IObservable<_>) ]

    member x.RootR = rootR
    member x.AboutR = aboutR
    member x.CustomersR = customersR
    member x.CustomerR = customerR

    member x.Dispose() =
        for disposable in subscriptions do disposable.Dispose()

    [<CLIEvent>]
    member x.Error = onError.Publish

    interface IObservable<HttpRequestMessage * Stream> with
        member x.Subscribe(observer) = onRequest.Publish.Subscribe(observer)

    interface IObserver<HttpRequestMessage * Stream> with
        member x.OnNext(value) = onRequest.Trigger(value)
        member x.OnError(exn) = onError.Trigger(exn)
        member x.OnCompleted() = () // dispose the resources

    interface IDisposable with
        member x.Dispose() = x.Dispose()
