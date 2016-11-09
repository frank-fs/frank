(* # Frank

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2011-2016, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)
module Frank.Http

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Formatting
open System.Net.Http.Headers
open System.Text
open System.Threading.Tasks

/// `HttpApplication` defines the contract for processing any request.
/// An application takes an `HttpRequestMessage` and returns an `HttpRequestHandler` asynchronously.
type HttpApplication = HttpRequestMessage -> Async<HttpResponseMessage>

/// An empty `HttpContent` type.
type EmptyContent =
    inherit HttpContent
    /// Creates a new instance of `EmptyContent`
    new : unit -> EmptyContent

type HttpContent with
    /// Returns an `EmptyContent`.
    member Empty : HttpContent

val respond : statusCode: HttpStatusCode -> headers: (HttpResponseMessage -> unit) -> content: #HttpContent option -> request: HttpRequestMessage -> HttpResponseMessage

(* General Headers *)

val Date : x: DateTimeOffset -> response: HttpResponseMessage -> unit
val Connection : x: string -> response: HttpResponseMessage -> unit
val Trailer : x: string -> response: HttpResponseMessage -> unit
val ``Transfer-Encoding`` : x: string -> response: HttpResponseMessage -> unit
val Upgrade : x: string -> response: HttpResponseMessage -> unit
val Via : x: string -> response: HttpResponseMessage -> unit
val ``Cache-Control`` : x: string -> response: HttpResponseMessage -> unit
val Pragma : x: string -> response: HttpResponseMessage -> unit

(* Response Headers *)

val Age : x: TimeSpan -> response: HttpResponseMessage -> unit
val ``Retry-After`` : x: string -> response: HttpResponseMessage -> unit
val Server : x: string -> response: HttpResponseMessage -> unit
val Warning : x: string -> response: HttpResponseMessage -> unit
val ``Accept-Ranges`` : x: string -> response: HttpResponseMessage -> unit
val Vary : x: string -> response: HttpResponseMessage -> unit
val ``Proxy-Authenticate`` : x: string -> response: HttpResponseMessage -> unit
val ``WWW-Authenticate`` : x: string -> response: HttpResponseMessage -> unit

(* Entity Headers *)

val Allow : allowedMethods: #seq<HttpMethod> -> response: HttpResponseMessage -> unit
val Location : x: Uri -> response: HttpResponseMessage -> unit
val ``Content-Disposition`` : x: string -> response: HttpResponseMessage -> unit
val ``Content-Encoding`` : x: seq<string> -> response: HttpResponseMessage -> unit
val ``Content-Language`` : x: seq<string> -> response: HttpResponseMessage -> unit
val ``Content-Length`` : x: int64 -> response: HttpResponseMessage -> unit
val ``Content-Location`` : x: Uri -> response: HttpResponseMessage -> unit
val ``Content-MD5`` : x: byte[] -> response: HttpResponseMessage -> unit
val ``Content-Range`` : from: int64 -> _to: int64 -> length: int64 -> response: HttpResponseMessage -> unit
val ``Content-Type`` : x: string -> response: HttpResponseMessage -> unit
val ETag : tag: string -> isWeak: bool -> response: HttpResponseMessage -> unit
val Expires : x: DateTimeOffset -> response: HttpResponseMessage -> unit
val ``Last Modified`` : x: DateTimeOffset -> response: HttpResponseMessage -> unit

/// Returns a response message with status code `200 OK`
val OK : headers: (HttpResponseMessage -> unit) -> content: #HttpContent option -> (HttpRequestMessage -> HttpResponseMessage)

(* Allow Header Helpers *)

/// `OPTIONS` responses should return the allowed methods, and this helper facilitates method calls.
val options : allowedMethods: seq<HttpMethod> -> HttpApplication

/// In some instances, you need to respond with a `405 Message Not Allowed` response.
/// The HTTP spec requires that this message include an `Allow` header with the allowed
/// HTTP methods.
val ``405 Method Not Allowed`` : allowedMethods: seq<HttpMethod> -> HttpApplication

/// Helper function to read FormUrlEncoded content as a Map<string, string>
val readFormUrlEncoded<'T> : content:HttpContent -> Async<Map<string, string>>

(* Content Negotiation Helpers *)

val ``406 Not Acceptable`` : HttpApplication

val findFormatterFor : mediaType: string -> formatters: seq<MediaTypeFormatter> -> MediaTypeFormatter

/// `formatWith` allows you to specify a specific `formatter` with which to render a representation
/// of your content body.
/// The `Web API` tries to do this for you at this time, so this function is likely to be clobbered,
/// or rather, wrapped again in another representation. Hopefully, this will get fixed in a future release.
val formatWith : mediaType: string -> formatter: MediaTypeFormatter -> body: 'a -> HttpContent

val IO : stream: Stream -> HttpContent
val Str : s: string -> HttpContent
val Formatted : s: string * encoding: Text.Encoding * mediaType: string -> HttpContent
val Form : pairs: seq<string * string> -> HttpContent
val Bytes : bytes: byte[] -> HttpContent
val Segment : segment: ArraySegment<byte> -> HttpContent

val negotiateMediaType : formatters: seq<MediaTypeFormatter> -> HttpRequestMessage -> string option

val runConneg : formatters: seq<MediaTypeFormatter> -> f: (HttpRequestMessage -> Async<_>) -> HttpApplication

/// Adds a default response of "405 Method Not Allowed" to a handler supporting the specified methods.
val resourceHandlerOrDefault : methods: seq<HttpMethod> -> handler: (HttpRequestMessage -> Async<HttpResponseMessage> option) -> HttpApplication

/// Adapts an `HttpApplication` function into a `System.Net.Http.DelegatingHandler`.
type AsyncHandler =
    inherit DelegatingHandler
    val AsyncSend : HttpRequestMessage -> Async<HttpResponseMessage>
