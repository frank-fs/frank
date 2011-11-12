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
open FSharpx.Http

// ## Define the web application interface

type HttpRequestHandler = Stream -> HttpResponseMessage

// `HttpApplication` defines the contract for processing a request.
// An application takes an `HttpRequestMessage` and
// returns a handler function that takes a `Stream` and
// returns an `HttpResponseMessage` that can be sent to the client.
type HttpApplication = HttpRequestMessage -> HttpRequestHandler

// ## HttpApplication helper functions

// Responding with the actual types can get a bit noisy with the long type names and required
// type cast to `HttpResponseMessage` (since most responses will include a typed body).
// The `respond` function simplifies this and also accepts a `seq<string * string>` of headers.
// While this latter is convenient, it skips the benefits of the statically typed `HttpHeaders`
// available on the `response` object.
let respond (statusCode: HttpStatusCode) headers body =
  let response = new HttpResponseMessage<_>(body, statusCode)
  headers |> Seq.iter (fun (header, values: string) -> response.Headers.Add(header, values))
  response :> HttpResponseMessage

// As most successful responses return `200 OK`, a helper to respond with this status code
// is useful for reducing keystrokes.
let respondOK headers body = respond HttpStatusCode.OK headers body

// A few responses should return allowed methods (`OPTIONS` and `405 Method Not Allowed`).
// `respondWithAllowHeader` allows both methods to share common functionality.
let private respondWithAllowHeader statusCode (allowedMethods: #seq<HttpMethod>) : HttpApplication =
  fun _ _ ->
    let response = new HttpResponseMessage(statusCode)
    allowedMethods |> Seq.map (fun m -> m.Method) |> Seq.iter response.Content.Headers.Allow.Add
    response

// `OPTIONS` responses should return the allowed methods, and this helper facilitates method calls.
let options allowedMethods =
  respondWithAllowHeader HttpStatusCode.OK allowedMethods

// In some instances, you need to respond with a `405 Message Not Allowed` response.
// The HTTP spec requires that this message include an `Allow` header with the allowed
// HTTP methods.
let ``405 Method Not Allowed`` allowedMethods =
  respondWithAllowHeader HttpStatusCode.MethodNotAllowed allowedMethods

let ``406 Not Acceptable`` =
  fun _ _ -> new HttpResponseMessage(HttpStatusCode.NotAcceptable)

// ## Content Negotiation Helpers

let inline equals y (x: ^M) = (^M : (member Equals: obj -> bool) (x, y))

[<CustomEquality;NoComparison>]
type Formatter(mediaTypes: #seq<string>, read, write) =
  member x.MediaTypes = mediaTypes
  member x.Read<'a>(stream: Stream) = read stream
  member x.Write(content) : byte[] = write content
  override x.Equals(other) =
    other <> null &&
    match other with
    | :? Formatter as formatter -> x = formatter
    | :? string as mediaType -> Seq.exists ((=) mediaType) mediaTypes
    | _ -> false
  override x.GetHashCode() = hash x

let findFormatterFor mediaType = Seq.find (equals mediaType)

// `readRequestBody` takes a collection of `HttpContent` formatters and returns a typed result from reading the content.
// This is useful if you have several options for receiving data such as JSON, XML, or form-urlencoded and want to produce
// a similar type against which to calculate a response.
let readRequestBody (formatters: #seq<Formatter>) =
  fun (request: HttpRequestMessage) stream ->
    let formatter = findFormatterFor request.Content.Headers.ContentType.MediaType formatters
    in formatter.Read(stream)

let internal accepted (request: HttpRequestMessage) = request.Headers.Accept.ToString()

let tryNegotiateMediaType f formatters =
  let servedMedia = Seq.collect (fun (f: Formatter) -> f.MediaTypes) formatters
  let bestOf = accepted >> FsConneg.bestMediaType servedMedia >> Option.map fst
  fun request -> 
    bestOf request
    |> Option.map (fun mediaType ->
        let formatter = findFormatterFor mediaType formatters
        fun stream -> respondOK ["Content-Type", mediaType] <| formatter.Write(f request stream))

// The most direct way of building an HTTP application is to focus on the actual types
// being transacted. These usually come in the form of the content of the request and response
// message bodies. The `f` function accepts the `request` and the deserialized `stream`.
// The `request` is provided to allow the function access to the request headers, which also
// allows us to merge several handler functions and select the appropriate handler using
// request header data.
let mapWithConneg f formatters =
  tryNegotiateMediaType (fun request stream -> f request <| readRequestBody formatters request stream) formatters

// ## HTTP Resource Agent

let route path handler =
  // TODO: use a better matching algorithm than `=` for matching the uri.
  function (request: HttpRequestMessage) when request.RequestUri.AbsolutePath = path -> Some(handler request)
         | _ -> None

let mapHandler(httpMethod, handler) =
  function (request: HttpRequestMessage) when request.Method.Method = httpMethod -> Some(handler request)
         | _ -> None

let get handler = mapHandler(HttpMethod.Get.Method, handler)
let post handler = mapHandler(HttpMethod.Post.Method, handler)
let put handler = mapHandler(HttpMethod.Put.Method, handler)
let delete handler = mapHandler(HttpMethod.Delete.Method, handler)

// We can use several methods to merge multiple handlers together into a single resource.
// Our chosen mechanism here is merging functions into a larger function of the same signature.
// This allows us to create resources as follows:
// 
//     let resource = get app1 <|> post app2 <|> put app3 <|> delete app4
//
// The intent here is to build a resource, with at most one handler per HTTP method. This goes
// against a lot of the "RESTful" approaches that just merge a bunch of method handlers at
// different URI addresses.
let orElse right left = fun request -> Option.orElse (left request) (right request)
let inline (<|>) left right = left |> orElse right

type ResourceMessage =
  | GetPath of AsyncReplyChannel<string>
  | ProcessRequest of HttpRequestMessage * AsyncReplyChannel<HttpResponseMessage>
  | AddHandler of HttpMethod * HttpApplication
  | RemoveHandler of HttpMethod

let getMethods handlers = List.map fst handlers
let addOptions handlers = (HttpMethod.Options, (options << getMethods) handlers)

// Creates a resource agent for a given path and set of HTTP message handlers.
let createResourceAgent path handlers =
  Agent<ResourceMessage>.Start(fun inbox ->
    // The resource agent's loop cycles through messages in its queue,
    // processing both control messages and request messages sequentially.
    let rec loop path (handlers: (HttpMethod * HttpApplication) list) = async {
      let! msg = inbox.Receive() 
      match msg with
      // TODO: Add BlockHandler and UnblockHandler messages, taking a user token. These should allow the resource to block methods for long-running operations, to be reflected in OPTIONS.
      | GetPath(reply) ->
          reply.Reply path
          return! loop path handlers
      | ProcessRequest(request, reply) ->
          let stream = request.Content.ContentReadStream
          let handler = tryFindHandlerFor request.Method handlers
          let response =
            match handler with
            | Some app -> app request stream
            | _ -> ``405 Method Not Allowed`` (List.map fst handlers) request stream
          reply.Reply response
          return! loop path handlers
      | AddHandler(httpMethod, handler) ->
          return! loop path <| (httpMethod, handler)::handlers
      | RemoveHandler(httpMethod) ->
          return! loop path <| List.filter (fst >> (<>) httpMethod) handlers }
    loop path handlers)

// The Resource wraps the routing agent in a class to provide a more intuitive
// api for those more familiar with OOP and C#. This also allows easier consumption
// in other .NET languages.
//
// In addition, unlike using the agent directly, the path is available instantly as
// a property of the resource.
type Resource(path, handlers) =
  let agent = createResourceAgent path <| List.ofSeq handlers
  member this.Path = path
  member this.AddHandler(h) = agent.Post(AddHandler h)
  member this.RemoveHandler(h) = agent.Post(RemoveHandler h)
  member this.AsyncProcessRequest(request) =
    agent.PostAndAsyncReply(fun reply -> ProcessRequest(request, reply))
  member this.ProcessRequestAsync(request, ?cancellationToken) =
    Async.StartAsTask(this.AsyncProcessRequest(request), ?cancellationToken = cancellationToken)
  member this.Extend(f:Agent<ResourceMessage> -> unit) = f agent
