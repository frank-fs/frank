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
  
type FrankHandler = IDictionary<string, obj> -> Async<string * IDictionary<string, string> * Body>

type Route = {
  Method : string
  Process : FrankHandler } with
  member this.MatchMethod(httpMethod) =
    String.Equals(this.Method, httpMethod, StringComparison.InvariantCultureIgnoreCase)

let route methd handler = { Method = methd; Process = handler }
let get handler = route "GET" handler
let post handler = route "POST" handler
let put handler = route "PUT" handler
let delete handler = route "DELETE" handler

type FrankMessage =
  | AddHandler of (Route list -> Route list)
  | GetPath of AsyncReplyChannel<string>
  | GetRoutes of AsyncReplyChannel<Route list>
  | Process of IDictionary<string, obj> * AsyncReplyChannel<string * IDictionary<string, string> * Body>

let H405 request = async {
  return ("405 Method not allowed", dict [], Body.Empty) }

let frank path routes = Agent<FrankMessage>.Start(fun inbox ->
  //add head and options if not specified
  let rec loop path (routes:Route list) = async {
    let! msg = inbox.Receive() 
    match msg with
    | AddHandler f ->
        return! loop path (f routes)
    | GetPath(reply) ->
        reply.Reply path
        return! loop path routes
    | GetRoutes(reply) ->
        reply.Reply routes
        return! loop path routes
    | Process(request, reply) ->
        let methd = request?RequestMethod :?> string
        let route = routes |> List.filter (fun r -> r.MatchMethod methd)
        // TODO: Should the response be string * IDictionary<string, string> * Async<Body>?
        let! response =
          match route with
          | hd::_ -> hd.Process request // Return the first match; there should be only one.
          | _ -> H405 request          // If no matches, return a 405 response.
        reply.Reply response
        return! loop path routes }
  loop path routes)

// TODO: Move this into each platform-specific library, as it can implement IHttpHandler, etc.
type FrankResource(path, routes) =
  let agent = frank path routes
  member this.AddHandler(f) = agent.Post(AddHandler f)
  member this.AsyncProcess(request) = agent.PostAndAsyncReply(fun reply -> Process(request, reply))
  member this.Process(request) = agent.PostAndReply(fun reply -> Process(request, reply))
  member this.Extend(f) = f agent

module Extend =
  let withOptions (agent:Agent<FrankMessage>) =
    let createHandler allowedMethods = fun request -> async {
      return ("200 OK", dict [("Allow", allowedMethods)], Body.Empty) }
    let getMethods routes =
      List.map (fun (r:Route) -> r.Method) routes
      |> Array.ofList
      |> (fun arr -> String.Join(", ", arr))
    let options = createHandler << getMethods
    let addOptions routes = route "OPTIONS" (options routes) :: routes
    agent.Post(AddHandler addOptions)
    agent
  
// TODO: add diagnostics and logging
// TODO: add messages to access diagnostics and logging info from the agent
