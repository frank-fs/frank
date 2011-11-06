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

// ## Type Aliases and Extensions

// `HttpApplication` defines the contract for processing a request.
// An application takes an `HttpRequestMessage` and
// returns an `HttpResponseMessage` that can be sent to the client.
type HttpApplication = HttpRequestMessage -> HttpResponseMessage

type Agent<'a> = MailboxProcessor<'a>

type HttpMethod with
  static member All = new HttpMethod("*")

// ## HttpApplication helper functions

let respond (statusCode: HttpStatusCode) headers body =
  let response = new HttpResponseMessage<_>(body, statusCode)
  headers |> Seq.iter (fun (header, values: string) -> response.Headers.Add(header, values))
  response :> HttpResponseMessage

let private respondWithAllowHeader statusCode (allowedMethods: #seq<HttpMethod>) : HttpApplication =
  fun _ ->
    let response = new HttpResponseMessage(statusCode)
    allowedMethods |> Seq.map (fun m -> m.Method) |> Seq.iter response.Content.Headers.Allow.Add
    response

let options allowedMethods =
  respondWithAllowHeader HttpStatusCode.OK allowedMethods

// In some instances, you need to respond with a `405 Message Not Allowed` response.
// The HTTP spec requires that this message include an `Allow` header with the allowed
// HTTP methods.
let ``405 Method Not Allowed`` allowedMethods =
  respondWithAllowHeader HttpStatusCode.MethodNotAllowed allowedMethods

let ``406 Not Acceptable`` =
  fun _ -> new HttpResponseMessage(HttpStatusCode.NotAcceptable)

let findFormatterFor mediaType = Seq.find (fst >> Seq.exists ((=) mediaType)) >> snd

// `readRequestBody` takes a collection of `HttpContent` formatters and returns a typed result from reading the content.
// This is useful if you have several options for receiving data such as JSON, XML, or form-urlencoded and want to produce
// a similar type against which to calculate a response.
let readRequestBody mediaTypeReaders =
  fun (request: HttpRequestMessage) ->
    let reader = findFormatterFor request.Content.Headers.ContentType.MediaType mediaTypeReaders
    in reader request

let internal accepted (request: HttpRequestMessage) = request.Headers.Accept.ToString()

let negotiateMediaType f formatters : HttpApplication =
  let servedMedia = Seq.collect fst formatters
  let bestOf = accepted >> FsConneg.bestMediaType servedMedia >> Option.map fst
  fun request -> 
    match bestOf request with
    | Some mediaType ->
        let format = findFormatterFor mediaType formatters
        in format <| f request 
    | _ -> ``406 Not Acceptable`` request

// The most direct way of building an HTTP application is to focus on the actual data types
// being transacted. These usually come in the form of the content of the request and response
// message bodies. The `f` function is a standard `map` higher-order function parameter to
// transform a typed request body `'a` to a typed response body `'b`.
// 
// These may be encoded in various formats. So `mediaTypeReaders` and `formatters` are needed to handle
// the serialization and deserialization of the request and response bodies, respectively.
//
// Other mechanisms will be provided in Frank to allow a finer-grained application of both
// mapping and serialization/deserialization.
let mapWithConneg f mediaTypeReaders formatters =
  negotiateMediaType (readRequestBody mediaTypeReaders >> f) formatters

// ## HTTP Resource Agent

[<CustomEquality;NoComparison>]
type HttpMethodHandler =
  { Method : HttpMethod
    Handler : HttpApplication }
  with
  override this.Equals(other) =
    other.GetType() = typeof<HttpMethodHandler> && (other :?> HttpMethodHandler).Method = this.Method
  override this.GetHashCode() = hash this

let matchMethodHandler httpMethod handler =
  handler.Method = HttpMethod.All || handler.Method = httpMethod

let createMethodHandler httpMethod handler = { Method = httpMethod; Handler = handler }
let get handler = createMethodHandler HttpMethod.Get handler
let post handler = createMethodHandler HttpMethod.Post handler
let put handler = createMethodHandler HttpMethod.Put handler
let delete handler = createMethodHandler HttpMethod.Delete handler

type ResourceMessage =
  | GetPath of AsyncReplyChannel<string>
  | ProcessRequest of HttpRequestMessage * AsyncReplyChannel<HttpResponseMessage>
  | AddHandler of HttpMethodHandler
  | RemoveHandler of HttpMethodHandler

let getMethods = List.map (fun (r: HttpMethodHandler) -> r.Method)
let addOptions handlers = createMethodHandler HttpMethod.Options ((options << getMethods) handlers) :: handlers

// Creates a resource agent for a given path and set of HTTP message handlers.
let createResourceAgent path handlers =
  let handlers = addOptions handlers
  Agent<ResourceMessage>.Start(fun inbox ->
    // The resource agent's loop cycles through messages in its queue,
    // processing both control messages and request messages sequentially.
    let rec loop path (handlers:HttpMethodHandler list) = async {
      let! msg = inbox.Receive() 
      match msg with
      // TODO: Add BlockHandler and UnblockHandler messages, taking a user token. These should allow the resource to block methods for long-running operations, to be reflected in OPTIONS.
      | GetPath(reply) ->
          reply.Reply path
          return! loop path handlers
      | ProcessRequest(request, reply) ->
          let handler = handlers |> List.filter (fun r -> matchMethodHandler request.Method r)
          let response =
            match handler with
            | hd::_ -> hd.Handler request
            | _ -> ``405 Method Not Allowed`` [ for handler in handlers -> handler.Method ] request
          reply.Reply response
          return! loop path handlers
      | AddHandler h ->
          return! loop path <| h::handlers
      | RemoveHandler h ->
          return! loop path <| List.filter ((<>) h) handlers }
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
