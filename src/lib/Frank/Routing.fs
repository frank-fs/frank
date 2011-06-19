[<AutoOpen>]
module Frank.Routing

open System
open System.Collections.Generic

type Agent<'a> = MailboxProcessor<'a>

type Body =
  | Sequence of Body seq
  | Bytes of byte[]
  | Str of string
  | Segment of ArraySegment<byte> with
  static member Empty = Bytes [||]
  
type Handler = {
  Method : string
  Process : IDictionary<string, obj> -> Async<string * IDictionary<string, string> * Body> } with
  member this.Match(httpMethod) =
    if this.Method = "*" then true
    else String.Equals(this.Method, httpMethod, StringComparison.InvariantCultureIgnoreCase)

let handle methd handler = { Method = methd; Process = handler }
let get handler = handle "GET" handler
let post handler = handle "POST" handler
let put handler = handle "PUT" handler
let delete handler = handle "DELETE" handler

type FrankMessage =
  | AddHandler of (Handler list -> Handler list)
  | GetPath of AsyncReplyChannel<string>
  | GetRoutes of AsyncReplyChannel<Handler list>
  | Process of IDictionary<string, obj> * AsyncReplyChannel<string * IDictionary<string, string> * Body>

let H405 request = async {
  return ("405 Method not allowed", dict [], Body.Empty) }

let frank path handlers = Agent<FrankMessage>.Start(fun inbox ->
  //add head and options if not specified
  let rec loop path (handlers:Handler list) = async {
    let! msg = inbox.Receive() 
    match msg with
    | AddHandler f ->
        return! loop path (f handlers)
    | GetPath(reply) ->
        reply.Reply path
        return! loop path handlers
    | GetRoutes(reply) ->
        reply.Reply handlers
        return! loop path handlers
    | Process(request, reply) ->
        let methd = request?RequestMethod :?> string
        let handler = handlers |> List.filter (fun r -> r.Match methd)
        // TODO: Should the response be string * IDictionary<string, string> * Async<Body>?
        let! response =
          match handler with
          | hd::_ -> hd.Process request // Return the first match; there should be only one.
          | _ -> H405 request          // If no matches, return a 405 response.
        reply.Reply response
        return! loop path handlers }
  loop path handlers)

// TODO: Move this into each platform-specific library, as it can implement IHttpHandler, etc.
type FrankResource(path, handlers) =
  let agent = frank path handlers
  member this.AddHandler(f) = agent.Post(AddHandler f)
  member this.AsyncProcess(request) = agent.PostAndAsyncReply(fun reply -> Process(request, reply))
  member this.Process(request) = agent.PostAndReply(fun reply -> Process(request, reply))
  member this.Extend(f) = f agent

module Extend =
  let withOptions (agent:Agent<FrankMessage>) =
    let createHandler allowedMethods = fun request -> async {
      return ("200 OK", dict [("Allow", allowedMethods)], Body.Empty) }
    let getMethods handlers =
      List.map (fun (r:Handler) -> r.Method) handlers
      |> Array.ofList
      |> (fun arr -> String.Join(", ", arr))
    let options = createHandler << getMethods
    let addOptions handlers = handle "OPTIONS" (options handlers) :: handlers
    agent.Post(AddHandler addOptions)
    agent
  
// TODO: add diagnostics and logging
// TODO: add messages to access diagnostics and logging info from the agent
