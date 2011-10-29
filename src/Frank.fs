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
open System.Net
open System.Net.Http
open System.Text

// ## Type Aliases and Extensions

type Agent<'a> = MailboxProcessor<'a>

type HttpResponseMessage with
  static member MethodNotAllowedHandler(request) = new HttpResponseMessage(HttpStatusCode.MethodNotAllowed)

type HttpMethod with
  static member All = new HttpMethod("*")

// ## HTTP Resource Agent

type RequestHandler = {
  Method : HttpMethod
  ProcessRequest : HttpRequestMessage -> HttpResponseMessage }
  with
  member this.Match(httpMethod) =
    this.Method = HttpMethod.All || this.Method = httpMethod

let private createRequestHandler methd handler = { Method = methd; ProcessRequest = handler }
let map handler = createRequestHandler HttpMethod.All handler
let get handler = createRequestHandler HttpMethod.Get handler
let post handler = createRequestHandler HttpMethod.Post handler
let put handler = createRequestHandler HttpMethod.Put handler
let delete handler = createRequestHandler HttpMethod.Delete handler

type ResourceMessage =
  | AddRequestHandler of (RequestHandler list -> RequestHandler list)
  | GetPath of AsyncReplyChannel<string>
  | GetRoutes of AsyncReplyChannel<RequestHandler list>
  | ProcessRequest of HttpRequestMessage * AsyncReplyChannel<HttpResponseMessage>

// Creates a resource agent for a given path and set of HTTP message handlers.
let createResourceAgent path handlers = Agent<ResourceMessage>.Start(fun inbox ->
  // The resource agent's loop cycles through messages in its queue,
  // processing both control messages and request messages sequentially.
  let rec loop path (handlers:RequestHandler list) = async {
    let! msg = inbox.Receive() 
    match msg with
    | AddRequestHandler f ->
        return! loop path (f handlers)
    | GetPath(reply) ->
        reply.Reply path
        return! loop path handlers
    | GetRoutes(reply) ->
        reply.Reply handlers
        return! loop path handlers
    | ProcessRequest(request, reply) ->
        let handler = handlers |> List.filter (fun r -> r.Match request.Method)
        let response =
          match handler with
          | hd::_ -> hd.ProcessRequest request // Return the first match; there should be only one.
          | _ -> HttpResponseMessage.MethodNotAllowedHandler request
        reply.Reply response
        return! loop path handlers }
  loop path handlers)

// The Resource wraps the routing agent in a class to provide a more intuitive
// api for those more familiar with OOP and C#. This also allows easier consumption
// in other .NET languages.
//
// In addition, unlike using the agent directly, the path is available instantly as
// a property of the resource.
type Resource(path, handlers) =
  let agent = createResourceAgent path (handlers |> List.ofSeq)
  member this.Path = path
  member this.AddHandler(f) =
    agent.Post(AddRequestHandler f)
  member this.AsyncProcessRequest(request) =
    agent.PostAndAsyncReply(fun reply -> ProcessRequest(request, reply))
  member this.ProcessRequestAsync(request, ?cancellationToken) =
    Async.StartAsTask(this.AsyncProcessRequest(request), ?cancellationToken = cancellationToken)
  member this.Extend(f:Agent<ResourceMessage> -> Agent<ResourceMessage>) = f agent

// The Extend module provides the mechanisms for extending simple routing agents with
// additional functionality.
module Extend =
  let withOptions (agent:Agent<ResourceMessage>) =

    let createHandler (allowedMethods : #seq<HttpMethod>) = fun request ->
      let content = new ByteArrayContent([||])
      content.Headers.Add("Allow", allowedMethods |> Seq.map (fun m -> m.Method))
      let message = new HttpResponseMessage()
      message.Content <- content
      message

    let getMethods handlers = List.map (fun (r:RequestHandler) -> r.Method) handlers |> Array.ofList
    let options = createHandler << getMethods
    let addOptions handlers = createRequestHandler HttpMethod.Options (options handlers) :: handlers
    agent.Post(AddRequestHandler addOptions)
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

let frankWebApi (resources : seq<#Resource>) =
  // TODO: Auto-wire routes based on the passed-in resources.
  let routes = resources |> Seq.map (fun r -> (r.Path, r.ProcessRequestAsync))

  WebApiConfiguration(
    useMethodPrefixForHttpMethod = false,
    MessageHandlerFactory = (fun () -> Seq.map FrankHandler.Create resources))
