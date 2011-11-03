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
open FSharpx.Iteratee

// ## Type Aliases and Extensions

// An HTTP request body transformer.
// Specifying this function will transform a stream of content
// into a type 'a. The simplest transform is the `id` function.
type RequestBodyT<'a> = HttpContent -> 'a

// HttpApplication defines the contract for processing a request.
// An application takes an HttpRequestMessage and
// returns a function that takes an HttpContent transformer and
// returns an HttpResponseMessage that can be sent to the client.
type HttpApplication<'a> = HttpRequestMessage -> RequestBodyT<'a> -> HttpResponseMessage

type Agent<'a> = MailboxProcessor<'a>

type HttpMethod with
  static member All = new HttpMethod("*")

// ## RequestBodyT helper functions

// `readBodyWithMediaTypes<'a>` takes a collection of `MediaTypeFormatter` and returns a `RequestBodyT<'a>`.
let readBodyWithMediaTypes (mediaTypeFormatters: seq<MediaTypeFormatter>) : RequestBodyT<_> =
  fun content -> content.ReadAs<_>(mediaTypeFormatters)

// ## HttpApplication helper functions

let private responseWithAllowHeader statusCode (allowedMethods: #seq<HttpMethod>) : HttpApplication<_> =
  fun _ _ ->
      let response = new HttpResponseMessage(statusCode)
      allowedMethods |> Seq.map (fun m -> m.Method) |> Seq.iter response.Content.Headers.Allow.Add
      response

let options allowedMethods =
  responseWithAllowHeader HttpStatusCode.OK allowedMethods

// In some instances, you need to respond with a `405 Message Not Allowed` response.
// The HTTP spec requires that this message include an `Allow` header with the allowed
// HTTP methods.
let ``405 Method Not Allowed`` allowedMethods =
  responseWithAllowHeader HttpStatusCode.MethodNotAllowed allowedMethods

let ``406 Not Acceptable`` =
    fun _ _ -> new HttpResponseMessage(HttpStatusCode.NotAcceptable)

// Creates an `HttpApplication` from a function that takes a transformed request body and
// returns an `HttpResponseMessage`.
let handleRequest<'a> (f:'a -> HttpResponseMessage) : HttpApplication<'a> =
  fun request requestBodyT -> f <| requestBodyT request.Content

let internal accepted (request: HttpRequestMessage) = request.Headers.Accept.ToString()

let negotiateMediaType (f: HttpRequestMessage -> RequestBodyT<'a> -> 'b) (mediaTypeFormatters: (string list * ('b -> HttpResponseMessage)) list) =
    let servedMedia = List.collect fst mediaTypeFormatters
    let bestOf = accepted >> FsConneg.bestMediaType servedMedia >> Option.map fst
    let findFormatterFor mediaType = List.find (fst >> Seq.exists ((=) mediaType)) >> snd
    fun request requestBodyT -> 
        match bestOf request with
        | Some mediaType ->
            let format = findFormatterFor mediaType mediaTypeFormatters 
            f request requestBodyT |> format
        | _ -> ``406 Not Acceptable`` request requestBodyT

// ## HTTP Resource Agent

type HttpMethodHandler<'a> =
  { Method : HttpMethod
    Handler : HttpApplication<'a> }

let matchMethodHandler httpMethod handler =
  handler.Method = HttpMethod.All || handler.Method = httpMethod

let createMethodHandler httpMethod handler = { Method = httpMethod; Handler = handler }
let map handler = createMethodHandler HttpMethod.All handler
let get handler = createMethodHandler HttpMethod.Get handler
let post handler = createMethodHandler HttpMethod.Post handler
let put handler = createMethodHandler HttpMethod.Put handler
let delete handler = createMethodHandler HttpMethod.Delete handler

type ResourceMessage<'a> =
  | AddHttpMethodHandler of (HttpMethodHandler<'a> list -> HttpMethodHandler<'a> list)
  | GetPath of AsyncReplyChannel<string>
  | GetRoutes of AsyncReplyChannel<HttpMethodHandler<'a> list>
  | ProcessRequest of HttpRequestMessage * AsyncReplyChannel<HttpResponseMessage>

// Creates a resource agent for a given path and set of HTTP message handlers.
let createResourceAgent(path, handlers, mediaTypeFormatters) =
  Agent<ResourceMessage<_>>.Start(fun inbox ->
    let requestBodyT = readBodyWithMediaTypes mediaTypeFormatters
    // The resource agent's loop cycles through messages in its queue,
    // processing both control messages and request messages sequentially.
    let rec loop path (handlers:HttpMethodHandler<_> list) = async {
      let! msg = inbox.Receive() 
      match msg with
      | AddHttpMethodHandler f ->
          return! loop path (f handlers)
      | GetPath(reply) ->
          reply.Reply path
          return! loop path handlers
      | GetRoutes(reply) ->
          reply.Reply handlers
          return! loop path handlers
      | ProcessRequest(request, reply) ->
          let handler = handlers |> List.filter (fun r -> matchMethodHandler request.Method r)
          let response =
            match handler with
            | hd::_ -> hd.Handler request requestBodyT
            | _ -> ``405 Method Not Allowed`` [ for handler in handlers -> handler.Method ] request requestBodyT
          reply.Reply response
          return! loop path handlers }
    loop path handlers)

// The Resource wraps the routing agent in a class to provide a more intuitive
// api for those more familiar with OOP and C#. This also allows easier consumption
// in other .NET languages.
//
// In addition, unlike using the agent directly, the path is available instantly as
// a property of the resource.
type Resource(path, handlers, ?mediaTypeFormatters) =
  let mediaTypeFormatters = defaultArg mediaTypeFormatters Seq.empty
  let agent = createResourceAgent(path, (handlers |> List.ofSeq), mediaTypeFormatters)
  member this.Path = path
  // Should another mechanism exist to allow the removal of handlers during runtime?
  member this.AddHandler(f) =
    agent.Post(AddHttpMethodHandler f)
  member this.AsyncProcessRequest(request) =
    agent.PostAndAsyncReply(fun reply -> ProcessRequest(request, reply))
  member this.ProcessRequestAsync(request, ?cancellationToken) =
    Async.StartAsTask(this.AsyncProcessRequest(request), ?cancellationToken = cancellationToken)
  member this.Extend(f:Agent<ResourceMessage<_>> -> Agent<ResourceMessage<_>>) = f agent

// The Extend module provides the mechanisms for extending simple routing agents with
// additional functionality.
module Extend =
  let withOptions (agent:Agent<ResourceMessage<_>>) =
    // This is incredibly naive. What if the client has already submitted a request
    // that blocks other methods for a time?
    let getMethods handlers = List.map (fun (r:HttpMethodHandler<_>) -> r.Method) handlers |> Array.ofList
    let addOptions handlers = createMethodHandler HttpMethod.Options ((options << getMethods) handlers) :: handlers
    agent.Post(AddHttpMethodHandler addOptions)
    agent
  
// TODO: add diagnostics and logging
// TODO: add messages to access diagnostics and logging info from the agent

// ## Web API Hosting

// Open namespaces for Web API support.
open System.ServiceModel
open Microsoft.ApplicationServer.Http

[<ServiceContract>]
type EmptyService() =
  [<OperationContract>]
  member x.Invoke() = ()

type FrankHandler() =
  inherit DelegatingHandler()
  static member Create(resource : Resource) =
    { new FrankHandler() with
        override this.SendAsync(request, cancellationToken) =
          resource.ProcessRequestAsync(request, cancellationToken) } :> DelegatingHandler

let frankWebApi (resources : #seq<#Resource>) =
  // TODO: Auto-wire routes based on the passed-in resources.
  let routes = resources |> Seq.map (fun r -> (r.Path, r.ProcessRequestAsync))

  WebApiConfiguration(
    useMethodPrefixForHttpMethod = false,
    MessageHandlerFactory = (fun () -> Seq.map FrankHandler.Create resources))
