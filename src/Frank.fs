(* # Frank

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2011, Ryan Riley.

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
open FSharpx.Http

#if DEBUG
open System.Json
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

    type HttpApplication = HttpRequestMessage -> Async<HttpResponseMessage>
    
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

// `HttpApplication` defines the contract for processing any request.
// An application takes an `HttpRequestMessage` and returns an `HttpRequestHandler`.
type HttpApplication = HttpRequestMessage -> Async<HttpResponseMessage>

// ## HTTP Response Combinators

// Headers are added using the `Reader` monad. If F# allows mutation, why do we need the monad?
// First of all, it allows for the explicit declaration of side effects. Second, a number
// of combinators are already defined that allows you to more easily compose headers.
type HttpResponseHeadersBuilder = Reader<HttpResponseMessage, unit>
let headers = Reader.reader
let addHeaders (headers: HttpResponseHeadersBuilder) response = headers response; response

// Responding with the actual types can get a bit noisy with the long type names and required
// type cast to `HttpResponseMessage` (since most responses will include a typed body).
// The `respond` function simplifies this and also accepts an `HttpResponseHeadersBuilder`
// to allow easy composition and inclusion of headers. This function finally takes an optional
// `body`.
let respond (statusCode: HttpStatusCode) (headers: HttpResponseHeadersBuilder) body =
  new HttpResponseMessage(statusCode, Content = body)
  |> addHeaders headers
  |> async.Return

#if DEBUG
[<Test>]
let ``test respond without body``() =
  let statusCode = HttpStatusCode.OK
  let response = respond statusCode ignore HttpContent.Empty |> Async.RunSynchronously
  test <@ response.StatusCode = statusCode @>
  test <@ response.Content = HttpContent.Empty @>

[<Test>]
let ``test respond with body``() =
  let statusCode, body = HttpStatusCode.OK, "Howdy"
  let response = respond statusCode ignore <| new StringContent(body) |> Async.RunSynchronously
  test <@ response.StatusCode = statusCode @>
  test <@ response.Content.ReadAsStringAsync().Result = body @>
#endif

// ### General Headers
// TODO: Rely less upon the `Parse` and `ParseAdd` methods and pass in more of the parameters.
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

// ### Allow Header Helpers

// A few responses should return allowed methods (`OPTIONS` and `405 Method Not Allowed`).
// `respondWithAllowHeader` allows both methods to share common functionality.
let private respondWithAllowHeader statusCode (allowedMethods: #seq<string>) body =
  fun _ -> respond statusCode (Allow allowedMethods) body

// `OPTIONS` responses should return the allowed methods, and this helper facilitates method calls.
let options allowedMethods =
  respondWithAllowHeader HttpStatusCode.OK allowedMethods HttpContent.Empty

#if DEBUG
[<Test>]
let ``test options``() =
  let response = options ["GET";"POST"] (obj()) |> Async.RunSynchronously
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
  respondWithAllowHeader HttpStatusCode.MethodNotAllowed allowedMethods <| new StringContent("405 Method Not Allowed")

#if DEBUG
[<Test>]
let ``test 405 Method Not Allowed``() =
  let response = ``405 Method Not Allowed`` ["GET";"POST"] (obj()) |> Async.RunSynchronously
  test <@ response.StatusCode = HttpStatusCode.MethodNotAllowed @>
  test <@ response.Content.Headers.Allow.Contains("GET") @>
  test <@ response.Content.Headers.Allow.Contains("POST") @>
  test <@ not <| response.Content.Headers.Allow.Contains("PUT") @>
  test <@ not <| response.Content.Headers.Allow.Contains("DELETE") @>
#endif

// ## Content Negotiation Helpers

let ``406 Not Acceptable`` =
  fun _ -> respond HttpStatusCode.NotAcceptable ignore <| new StringContent("406 Not Acceptable")

#if DEBUG
[<Test>]
let ``test 406 Not Acceptable``() =
  let response = ``406 Not Acceptable`` (obj()) |> Async.RunSynchronously
  test <@ response.StatusCode = HttpStatusCode.NotAcceptable @>
#endif

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
let formatWith mediaType formatter body =
  new ObjectContent<_>(body, MediaTypeHeaderValue(mediaType), [| formatter |]) :> HttpContent

#if DEBUG
[<Serializable>]
type TestType() =
  let mutable firstName = ""
  let mutable lastName = ""
  member x.FirstName
    with get() = firstName
    and set(v) = firstName <- v
  member x.LastName
    with get() = lastName
    and set(v) = lastName <- v
  override x.ToString() = firstName + " " + lastName

[<Test>]
let ``test formatWith properly format as application/json``() =
  let formatter = new System.Net.Http.Formatting.JsonMediaTypeFormatter()
  let body = TestType(FirstName = "Ryan", LastName = "Riley")
  let content = body |> formatWith "application/json" formatter
  test <@ content.Headers.ContentType.MediaType = "application/json" @>
  let result = content.AsyncReadAsString() |> Async.RunSynchronously
  test <@ result = "{\"firstName\":\"Ryan\",\"lastName\":\"Riley\"}" @>

[<Test>]
let ``test formatWith properly format as text/plain and read as string``() =
  let formatter = new Microsoft.ApplicationServer.Http.PlainTextFormatter()
  let body = TestType(FirstName = "Ryan", LastName = "Riley").ToString()
  let content = body |> formatWith "text/plain" formatter
  test <@ content.Headers.ContentType.MediaType = "text/plain" @>
  let result = content.AsyncReadAs<string>([| formatter |]) |> Async.RunSynchronously
  test <@ result = body @>

[<Test>]
let ``test formatWith properly format as application/xml and read as TestType``() =
  let formatter = new System.Net.Http.Formatting.XmlMediaTypeFormatter()
  let body = TestType(FirstName = "Ryan", LastName = "Riley")
  let content = body |> formatWith "application/xml" formatter
  test <@ content.Headers.ContentType.MediaType = "application/xml" @>
  let result = content.AsyncReadAs<TestType>([| formatter |]) |> Async.RunSynchronously
  test <@ result = body @>

[<Test;Ignore>]
let ``test formatWith properly format as application/x-www-form-urlencoded and read as JsonValue``() =
  let formatter = new System.Net.Http.Formatting.JsonValueMediaTypeFormatter()
  let body = TestType(FirstName = "Ryan", LastName = "Riley")
  let content = body |> formatWith "application/x-www-form-urlencoded" formatter
  test <@ content.Headers.ContentType.MediaType = "application/x-www-form-urlencoded" @>
  let interim = content.AsyncReadAs<JsonValue>([| formatter |]) |> Async.RunSynchronously
  let result = interim.AsDynamic()
  test <@ result?firstName = body.FirstName @>
  test <@ result?lastName = body.LastName @>
#endif

let internal accepted (request: HttpRequestMessage) = request.Headers.Accept.ToString()

// When you want to negotiate the format of the response based on the available representations and
// the `request`'s `Accept` headers, you can `tryNegotiateMediaType`. This takes a set of available
// `formatters` and attempts to match the best with the provided `Accept` header values using
// functions from `FSharpx.Http`.
let negotiateMediaType formatters (f: HttpRequestMessage -> Async<_>) =
  let servedMedia =
    formatters
    |> Seq.collect (fun (formatter: MediaTypeFormatter) -> formatter.SupportedMediaTypes)
    |> Seq.map (fun value -> value.MediaType)
  let bestOf = accepted >> FsConneg.bestMediaType servedMedia >> Option.map fst
  fun request ->
    match bestOf request with
    | Some mediaType ->
        let formatter = findFormatterFor mediaType formatters
        async {
          let! responseBody = f request
          let formattedBody = responseBody |> formatWith mediaType formatter
          return! respond HttpStatusCode.OK (``Content-Type`` mediaType *> ``Vary`` "Accept") formattedBody }
    | _ -> ``406 Not Acceptable`` request

// ## HTTP Resources

// HTTP resources expose an resource handler function at a given uri.
// In the common MVC-style frameworks, this would roughly correspond
// to a `Controller`. Resources should represent a single entity type,
// and it is important to note that a `Foo` is not the same entity
// type as a `Foo list`, which is where most MVC approaches go wrong. 
type HttpResource =
  { Uri: string
    Methods: string list
    Handler: HttpRequestMessage -> Async<HttpResponseMessage> option }
  with

  // TODO: add a method to match the Uri.

  // With the ``405 Method Not Allowed`` function, resources can correctly respond to messages.
  // Therefore, we'll extend the `HttpResource` with an `Invoke` method. Without the `Invoke` method,
  // the `HttpResource` is left without any true representation of an `HttpApplication`.
  // 
  // Also note that the methods will always be looked up using the latest set. This could
  // probably be memoized so as to save a bit of time, but it allows us to ensure that all
  // available methods are reported.
  member x.Invoke(request) =
    match x.Handler request with
    | Some h -> h
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

(* ## HTTP Applications *)

let ``404 Not Found`` : HttpApplication =
  fun _ -> respond HttpStatusCode.NotFound ignore <| new StringContent("404 Not Found")

let findApplicationFor resources (request: HttpRequestMessage) =
  let resource = Seq.tryFind (fun r -> r.Uri = request.RequestUri.AbsolutePath) resources
  let handler = resource |> Option.map (fun r -> r.Invoke)
  handler

#if DEBUG
let stub _ = respond HttpStatusCode.OK ignore HttpContent.Empty
let resource1 = route "/" (get stub <|> post stub)
let resource2 = route "/stub" <| get stub

[<Test>]
let ``test should find nothing at GET /baduri``() =
  let request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://example.org/baduri"))
  let handler = findApplicationFor [resource1; resource2] request
  test <@ handler.IsNone @>

[<Test>]
let ``test should find stub at GET /``() =
  let request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://example.org/"))
  let handler = findApplicationFor [resource1; resource2] request
  test <@ handler.IsSome @>

[<Test>]
let ``test should find stub at POST /``() =
  let request = new HttpRequestMessage(HttpMethod.Post, new Uri("http://example.org/")) 
  let handler = findApplicationFor [resource1; resource2] request
  test <@ handler.IsSome @>

[<Test>]
let ``test should find stub at GET /stub``() =
  let request = new HttpRequestMessage(HttpMethod.Post, new Uri("http://example.org/"))
  let handler = findApplicationFor [resource1; resource2] request
  test <@ handler.IsSome @>
#endif

let mergeWithNotFound notFoundHandler (resources: #seq<HttpResource>) : HttpApplication =
  fun request ->
    let handler = findApplicationFor resources request |> (flip defaultArg) notFoundHandler
    handler request

let merge resources = mergeWithNotFound ``404 Not Found`` resources

#if DEBUG
[<Test>]
let ``test should return 404 Not Found as the handler``() =
  let app = merge []
  let request = new HttpRequestMessage()
  let response = app request |> Async.RunSynchronously
  test <@ response.StatusCode = HttpStatusCode.NotFound @>

[<Test>]
let ``test should return 404 Not Found as the handler when other resources are available``() =
  let app = merge [resource1; resource2]
  let request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://example.org/baduri"))
  let response = app request |> Async.RunSynchronously
  test <@ response.StatusCode = HttpStatusCode.NotFound @>

[<Test>]
let ``test should return stub at GET /``() =
  let app = merge [resource1; resource2]
  let request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://example.org/"))
  let response = app request |> Async.RunSynchronously
  test <@ response.StatusCode = HttpStatusCode.OK @>

[<Test>]
let ``test should return stub at POST /``() =
  let app = merge [resource1; resource2]
  let request = new HttpRequestMessage(HttpMethod.Post, new Uri("http://example.org/")) 
  let response = app request |> Async.RunSynchronously
  test <@ response.StatusCode = HttpStatusCode.OK @>

[<Test>]
let ``test should return stub at GET /stub``() =
  let app = merge [resource1; resource2]
  let request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://example.org/stub"))
  let response = app request |> Async.RunSynchronously
  test <@ response.StatusCode = HttpStatusCode.OK @>
#endif

let internal startAsTask (app: HttpApplication) (request, cancelationToken) =
  Async.StartAsTask(app request, cancellationToken = cancelationToken)

type FrankHandler private () =
  inherit DelegatingHandler()
  static member Start app =
    let app = startAsTask app
    { new FrankHandler() with
        override this.SendAsync(request, cancelationToken) =
          app(request, cancelationToken) } :> DelegatingHandler

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
