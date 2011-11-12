(* # Frank

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2011, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)
[<AutoOpen>]
module Frank.Core

#if INTERACTIVE
#r @"..\packages\Unquote.2.1.0\lib\net40\Unquote.dll"
#r @"..\packages\FSharpx.Core.1.3.111030\lib\FSharpx.Core.dll"
#r @"..\packages\FSharpx.Core.1.3.111030\lib\FSharpx.Http.dll"
#r @"..\packages\HttpClient.0.5.0\lib\40\Microsoft.Net.Http.dll"
#r @"..\packages\HttpClient.0.5.0\lib\40\Microsoft.Net.Http.Formatting.dll"
#endif

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Formatting
open System.Net.Http.Headers
open System.Text
open FSharpx
open FSharpx.Http

#if DEBUG
open ImpromptuInterface.FSharp
open NUnit.Framework
open Swensen.Unquote.Assertions
#endif

// ## Define the web application interface

(*
One may define a web application interface using a large variety of signatures.
Indeed, if you search the web, you're likely to find a large number of approaches.
When starting with `Frank`, I wanted to try to find a way to define an HTTP application
using pure functions and function composition. The closest I found was the following:

    type HttpRequestHandler = Stream -> HttpResponseMessage
    type HttpApplication = HttpRequestMessage -> HttpRequestHandler
    
    let orElse right left = fun request -> Option.orElse (left request) (right request)
    let inline (<|>) left right = orElse right left

The last of these was a means for merging multiple applications together into a single
application. This allowed for a nice symmetry and elegance in that everything you composed
would always have the same signature. Additional functions would allow you to map
applications to specific methods or uri patterns.

Alas, this approach works only so well. HTTP is a rich communication specification.
The simplicity and elegance of a purely functional approach quickly loses the ability
to communicate back options to the client. For instance, given the above, how do you
return a meaningful `405 Method Not Allowed` response? The HTTP specification requires
that you list the allowed methods, but if you merge all the logic for selecting an
application into the functions, there is no easy way to recall all the allowed methods,
short of trying them all. You could require that the developer add the list of used
methods, but that, too, misses the point that the application should be collecting this
and helping the developer by taking care of all of the nuts and bolts items

The next approach I tried involved using a tuple of a list of allowed HTTP methods and
the application handler, which used the merged function approach described above for
actually executing the application. However, once again, there are limitations. This
structure accurately represents a resource, but it does not allow for multiple resources
to coexist side-by-side. Another tuple of uri pattern matching expressions could wrap
a list of these method * handler tuples, but at this point I realized I would be better
served by using real types and thus arrived at the signatures below.

You'll see the signatures above are still mostly present, though they have been changed
to better fit the signatures below.
*)

// An `HttpRequestHandler` takes an `HttpContent`, or request body, and returns the
// appropriate `HttpResponseMessage`. This handler is returned by an `HttpApplication`.
// Why the split? Splitting the acceptance of the request headers from the actual
// request body allows you to decide what you want to do earlier. Thus, should you
// receive a request header specifying a specific `Accept`-ed content type, you can
// select an appropriate handler that returns that type. It also allows you to return
// a 404, 405, or other message before reading the content, should you determine that
// the request is invalid based on its headers.
type HttpRequestHandler = HttpContent -> HttpResponseMessage

// `HttpApplication` defines the contract for processing any request.
// An application takes an `HttpRequestMessage` and returns an `HttpRequestHandler`.
type HttpApplication = HttpRequestMessage -> HttpRequestHandler

// ## HTTP Response Combinators

// Headers are added using the `State` monad. If F# allows mutation, why do we need the monad?
// First of all, it allows for the explicit declaration of side effects. Second, a number
// of combinators are already defined that allows you to more easily compose headers.
// Third, the next most likely candidate, the `Reader` doesn't make sense for something
// we are explicitly manipulating.
type HttpResponseHeadersBuilder = FSharpx.State.State<unit, HttpResponseMessage>
let headers = FSharpx.State.state
let addHeaders (headers: HttpResponseHeadersBuilder) response = FSharpx.State.exec headers response

// While you'll likely always write headers, you may find reasons to not bother. In that case,
// the `noHeaders` combinator will allow you to put a placeholder in place.
let noHeaders = fun response -> (), response

// Responding with the actual types can get a bit noisy with the long type names and required
// type cast to `HttpResponseMessage` (since most responses will include a typed body).
// The `respond` function simplifies this and also accepts an `HttpResponseHeadersBuilder`
// to allow easy composition and inclusion of headers. This function finally takes an optional
// `body`.
let respond (statusCode: HttpStatusCode) (headers: HttpResponseHeadersBuilder) body =
  match body with
  | Some(body) -> new HttpResponseMessage<_>(body, statusCode) :> HttpResponseMessage
  | _          -> new HttpResponseMessage(statusCode)
  |> addHeaders headers

#if DEBUG
[<Test>]
let ``test respond without body``() =
  let statusCode = HttpStatusCode.OK
  let response = respond statusCode noHeaders None
  test <@ response.StatusCode = statusCode @>
  test <@ response.Content = null @>

[<Test>]
let ``test respond with body``() =
  let statusCode, body = HttpStatusCode.OK, "Howdy"
  let response = respond statusCode noHeaders (Some body)
  test <@ response.StatusCode = statusCode @>
  test <@ response.Content.ReadAsString() = body @>
#endif

// ### General Headers
// TODO: Rely less upon the `Parse` and `ParseAdd` methods and pass in more of the parameters.
let Date x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.Date <- Nullable.create x
    (), response

let Connection x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.Connection.ParseAdd x
    (), response

let Trailer x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.Trailer.ParseAdd x
    (), response

let ``Transfer-Encoding`` x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.TransferEncoding.ParseAdd x
    (), response

let Upgrade x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.Upgrade.ParseAdd x
    (), response

let Via x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.Via.ParseAdd x
    (), response

let ``Cache-Control`` x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.CacheControl <- CacheControlHeaderValue.Parse x
    (), response

let Pragma x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.Pragma.ParseAdd x
    (), response

// ### Response Headers
let Age x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.Age <- Nullable.create x
    (), response

let ``Retry-After`` x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.RetryAfter <- RetryConditionHeaderValue.Parse x
    (), response

let Server x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.Server.ParseAdd x
    (), response

let Warning x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.Warning.ParseAdd x
    (), response

let ``Accept-Ranges`` x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.AcceptRanges.ParseAdd x
    (), response

let Vary x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.Vary.ParseAdd x
    (), response

let ``Proxy-Authenticate`` x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.ProxyAuthenticate.ParseAdd x
    (), response

let ``WWW-Authenticate`` x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.WwwAuthenticate.ParseAdd x
    (), response

// ### Entity Headers
let Allow x : HttpResponseHeadersBuilder =
  fun response ->
    Seq.iter response.Content.Headers.Allow.Add x
    (), response

let Location x : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.Location <- x
    (), response

let ``Content-Disposition`` x : HttpResponseHeadersBuilder =
  fun response ->
    response.Content.Headers.ContentDisposition <- ContentDispositionHeaderValue x
    (), response

let ``Content-Encoding`` x : HttpResponseHeadersBuilder =
  fun response ->
    Seq.iter response.Content.Headers.ContentEncoding.Add x
    (), response

let ``Content-Language`` x : HttpResponseHeadersBuilder =
  fun response ->
    Seq.iter response.Content.Headers.ContentLanguage.Add x 
    (), response

let ``Content-Length`` x : HttpResponseHeadersBuilder =
  fun response ->
    response.Content.Headers.ContentLength <- Nullable.create x
    (), response

let ``Content-Location`` x : HttpResponseHeadersBuilder =
  fun response ->
    response.Content.Headers.ContentLocation <- x
    (), response

let ``Content-MD5`` x : HttpResponseHeadersBuilder =
  fun response ->
    response.Content.Headers.ContentMD5 <- x
    (), response

let ``Content-Range`` from _to length : HttpResponseHeadersBuilder =
  fun response ->
    response.Content.Headers.ContentRange <- ContentRangeHeaderValue(from, _to, length)
    (), response

let ``Content-Type`` x : HttpResponseHeadersBuilder =
  fun response ->
    response.Content.Headers.ContentType <- MediaTypeHeaderValue x
    (), response

let ETag tag isWeak : HttpResponseHeadersBuilder =
  fun response ->
    response.Headers.ETag <- EntityTagHeaderValue(tag, isWeak)
    (), response

let Expires x : HttpResponseHeadersBuilder =
  fun response ->
    response.Content.Headers.Expires <- Nullable.create x
    (), response

let ``Last Modified`` x : HttpResponseHeadersBuilder =
  fun response ->
    response.Content.Headers.LastModified <- Nullable.create x
    (), response

// ### Allow Header Helpers

// A few responses should return allowed methods (`OPTIONS` and `405 Method Not Allowed`).
// `respondWithAllowHeader` allows both methods to share common functionality.
let private respondWithAllowHeader statusCode (allowedMethods: #seq<string>) =
  fun _ _ -> respond statusCode (Allow allowedMethods) None

// `OPTIONS` responses should return the allowed methods, and this helper facilitates method calls.
let options allowedMethods =
  respondWithAllowHeader HttpStatusCode.OK allowedMethods

#if DEBUG
[<Test>]
let ``test options``() =
  let response = options ["GET";"POST"] (obj()) (obj())
  test <@ response.StatusCode = HttpStatusCode.OK @>
  test <@ response.Content.Headers.Allow.Contains("GET") @>
  test <@ response.Content.Headers.Allow.Contains("POST") @>
  test <@ not <| response.Content.Headers.Allow.Contains("PUT") @>
  test <@ not <| response.Content.Headers.Allow.Contains("DELETE") @>
#endif

// In some instances, you need to respond with a `405 Message Not Allowed` response.
// The HTTP spec requires that this message include an `Allow` header with the allowed
// HTTP methods.
let ``405 Method Not Allowed`` allowedMethods =
  respondWithAllowHeader HttpStatusCode.MethodNotAllowed allowedMethods

#if DEBUG
[<Test>]
let ``test 405 Method Not Allowed``() =
  let response = ``405 Method Not Allowed`` ["GET";"POST"] (obj()) (obj())
  test <@ response.StatusCode = HttpStatusCode.MethodNotAllowed @>
  test <@ response.Content.Headers.Allow.Contains("GET") @>
  test <@ response.Content.Headers.Allow.Contains("POST") @>
  test <@ not <| response.Content.Headers.Allow.Contains("PUT") @>
  test <@ not <| response.Content.Headers.Allow.Contains("DELETE") @>
#endif

// ## Content Negotiation Helpers

let ``406 Not Acceptable`` =
  fun _ _ -> respond HttpStatusCode.NotAcceptable noHeaders None

#if DEBUG
[<Test>]
let ``test 406 Not Acceptable``() =
  let response = ``406 Not Acceptable`` (obj()) (obj())
  test <@ response.StatusCode = HttpStatusCode.NotAcceptable @>
#endif

let findFormatterFor mediaType =
  Seq.find (fun (formatter: MediaTypeFormatter) ->
    formatter.SupportedMediaTypes
    |> Seq.map (fun value -> value.MediaType)
    |> Seq.exists ((=) mediaType))

// `readRequestBody` takes a collection of `HttpContent` formatters and returns a typed result from reading the content.
// This is useful if you have several options for receiving data such as JSON, XML, or form-urlencoded and want to produce
// a similar type against which to calculate a response.
let readRequestBody formatters =
  fun (request: HttpRequestMessage) (content: HttpContent) ->
    let formatter = findFormatterFor request.Content.Headers.ContentType.MediaType formatters
    in content.ReadAs<_>([| formatter |])

let internal accepted (request: HttpRequestMessage) = request.Headers.Accept.ToString()

// `formatWith` allows you to specify a specific `formatter` with which to render a representation
// of your content body.
// 
// The `WCF Web API` tries to do this for you at this time, so this function
// is likely to be clobbered, or rather, wrapped again in another representation. Hopefully, this
// will get fixed in a future release.
// 
// Further note that the current solution requires creation of `ObjectContent<_>`, which is certainly
// not optimal. Hopefully this, too, will be resolved in a future release.
let formatWith formatter body =
  let content = new ObjectContent<_>(value = body, formatters = [| formatter |])
  in content.ReadAsByteArray()

// When you want to negotiate the format of the response based on the available representations and
// the `request`'s `Accept` headers, you can `tryNegotiateMediaType`. This takes a set of available
// `formatters` and attempts to match the best with the provided `Accept` header values using
// functions from `FSharpx.Http`.
let tryNegotiateMediaType formatters f =
  let servedMedia =
    formatters
    |> Seq.collect (fun (formatter: MediaTypeFormatter) -> formatter.SupportedMediaTypes)
    |> Seq.map (fun value -> value.MediaType)
  let bestOf = accepted >> FsConneg.bestMediaType servedMedia >> Option.map fst
  fun request -> 
    bestOf request
    |> Option.map (fun mediaType ->
        let formatter = findFormatterFor mediaType formatters
        fun content ->
          respond HttpStatusCode.OK (``Content-Type`` mediaType)
          <| Some(f request content |> formatWith formatter))

// The most direct way of building an HTTP application is to focus on the actual types
// being transacted. These usually come in the form of the content of the request and response
// message bodies. The `f` function accepts the `request` and the deserialized `content`.
// The `request` is provided to allow the function access to the request headers, which also
// allows us to merge several handler functions and select the appropriate handler using
// request header data.
let mapWithConneg formatters f =
  tryNegotiateMediaType formatters <| fun request content -> f request <| readRequestBody formatters request content

// ## HTTP Resources

// HTTP resources expose an `HttpResourceHandler` at a given uri.
// In the common MVC-style frameworks, this would roughly correspond
// to a `Controller`. Resources should represent a single entity type,
// and it is important to note that a `Foo` is not the same entity
// type as a `Foo list`, which is where most MVC approaches go wrong. 
type HttpResource =
  { Uri: string
    Methods: string list
    Handler: HttpRequestMessage -> HttpRequestHandler option }
  with

  // TODO: add a method to match the Uri.

  // With the ``405 Method Not Allowed`` function, resources can correctly respond to messages.
  // Therefore, we'll extend the `HttpResourceHandler` with an `Invoke` method. Previously,
  // the `HttpResourceHandler was left without any way of actually using the `Handler` it was
  // provided in it's private record constructor. Here, we bake in not only the mechanism with
  // which to reply, but we also provide the means to do so through a built-in mechanism.
  // 
  // Also note that the methods will always be looked up using the latest set. This could
  // probably be memoized so as to save a bit of time, but it allows us to ensure that all
  // available methods are reported.
  member x.Invoke(request) =
    match x.Handler request with
    | Some(h) -> h
    | _ -> ``405 Method Not Allowed`` x.Methods request

let private makeHandler(httpMethod, handler) =
  function (request: HttpRequestMessage) when request.Method.Method = httpMethod -> Some(handler request)
                       | _ -> None

// Helpers to more easily map `HttpApplication` functions to methods to be composed into `HttpResource`s.
let mapResourceHandler(httpMethod, handler) = [httpMethod], makeHandler(httpMethod, handler)
let get handler = mapResourceHandler(HttpMethod.Get.Method, handler)
let post handler = mapResourceHandler(HttpMethod.Post.Method, handler)
let put handler = mapResourceHandler(HttpMethod.Put.Method, handler)
let delete handler = mapResourceHandler(HttpMethod.Delete.Method, handler)

// We can use several methods to merge multiple handlers together into a single resource.
// Our chosen mechanism here is merging functions into a larger function of the same signature.
// This allows us to create resources as follows:
// 
//     let resource = get app1 <|> post app2 <|> put app3 <|> delete app4
//
// The intent here is to build a resource, with at most one handler per HTTP method. This goes
// against a lot of the "RESTful" approaches that just merge a bunch of method handlers at
// different URI addresses.
let orElse right left =
  fst left @ fst right,
  fun request -> Option.orElse (snd left request) (snd right request)
let inline (<|>) left right = left |> orElse right

let route path handler =
  { Uri = path
    Methods = fst handler
    Handler = snd handler }

let routeWithMethodMapping path handlers = route path <| Seq.reduce orElse handlers

//let formatter = FormUrlEncodedMediaTypeFormatter() :> Formatting.MediaTypeFormatter
//let testBody = dict [("foo", "bar");("bar", "baz")]
//let createTestRequest() =
//  new HttpRequestMessage(
//    HttpMethod.Post, Uri("http://frankfs.net/"),
//    Version = Version(1,1),
//    Content = new FormUrlEncodedContent(testBody))
//
//// HttpRequestMessage -> HttpResponseMessage
//let echo (request : HttpRequestMessage) = 
//  let body = request.Content.ReadAs<JsonValue>(seq { yield formatter })
//  let response = new HttpResponseMessage<JsonValue>(body, HttpStatusCode.OK)
//  response.Content.Headers.ContentType <- Headers.MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded")
//  response
//
//[<Test>]
//let ``test echo should return a response of 200 OK``() =
//  let actual = echo <| createTestRequest()
//  test <@ actual.StatusCode = HttpStatusCode.OK @>
//
//[<Test>]
//let ``test echo should return a response with one header for Content_Type of application/x-www-form-urlencoded``() =
//  let expected = Headers.MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded")
//  let actual = echo <| createTestRequest()
//  test <@ actual.Content.Headers.ContentType = expected @>
//
//[<Test>]
//let ``test echo should return a response with a body of echoing the request body``() =
//  let response = echo <| createTestRequest()
//  let actual = response.Content.ReadAs()
//  test <@ actual?foo = "bar" @>
//  test <@ actual?bar = "baz" @>
//
//let formatter = FormUrlEncodedMediaTypeFormatter() :> Formatting.MediaTypeFormatter
//let testBody = dict [("foo", "bar");("bar", "baz")]
//let createTestRequest() =
//  new HttpRequestMessage(
//    HttpMethod.Post, Uri("http://frankfs.net/"),
//    Version = Version(1,1),
//    Content = new FormUrlEncodedContent(testBody))
//
//// HttpRequestMessage -> HttpResponseMessage
//let echo (request : HttpRequestMessage) = 
//  let body = request.Content.ReadAs<JsonValue>(seq { yield formatter })
//  let response = new HttpResponseMessage<JsonValue>(body, HttpStatusCode.OK)
//  response.Content.Headers.ContentType <- Headers.MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded")
//  response
//
//[<Test>]
//let ``test echo should return a response of 200 OK``() =
//  let actual = echo <| createTestRequest()
//  test <@ actual.StatusCode = HttpStatusCode.OK @>
//
//[<Test>]
//let ``test echo should return a response with one header for Content_Type of application/x-www-form-urlencoded``() =
//  let expected = Headers.MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded")
//  let actual = echo <| createTestRequest()
//  test <@ actual.Content.Headers.ContentType = expected @>
//
//[<Test>]
//let ``test echo should return a response with a body of echoing the request body``() =
//  let response = echo <| createTestRequest()
//  let actual = response.Content.ReadAs()
//  test <@ actual?foo = "bar" @>
//  test <@ actual?bar = "baz" @>
