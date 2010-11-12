namespace Frank
open System
open System.Collections.Generic
open System.Net
open Microsoft.Http

[<AutoOpen>]
module Core =
  open FSharp.Monad

  /// Defines the standard Frank application type.
  type App = Func<HttpRequestMessage, HttpResponseMessage>

  /// Defines a handler for responding requests.
  type FrankHandler = IDictionary<string,string> -> State<HttpResponseMessage, HttpRequestMessage * HttpResponseMessage>

  let frank = State.StateBuilder()

  /// Gets the current state of the request.
  let getRequest = frank {
    let! (req:HttpRequestMessage, _:HttpResponseMessage) = getState
    return req }

  /// Gets the current state of the response.
  let getResponse = frank {
    let! (_:HttpRequestMessage, resp:HttpResponseMessage) = getState
    return resp }

  /// Updates the state of the request.
  let putRequest req = frank {
    let! (_:HttpRequestMessage, resp:HttpResponseMessage) = getState
    do! putState (req, resp)
    return resp }

  /// Updates the state of the response.
  let putResponse resp = frank {
    let! (req:HttpRequestMessage, _:HttpResponseMessage) = getState
    do! putState (req, resp)
    return resp }

  /// Updates the state of the response content.
  let putContent content = frank {
    let! (req:HttpRequestMessage, resp:HttpResponseMessage) = getState
    resp.StatusCode <- HttpStatusCode.OK
    resp.Content <- content
    do! putState (req, resp)
    return resp }

  /// Updates the state of the response content with plain text.
  let putPlainText (content:string) = frank {
    let! (req:HttpRequestMessage, resp:HttpResponseMessage) = getState
    resp.StatusCode <- HttpStatusCode.OK
    resp.Content <- HttpContent.Create(content, "text/plain")
    do! putState (req, resp)
    return resp }

  // TODO: Add more helper methods to mutate only portions of the request or response objects.

  /// Runs the FrankHandler 
  let run (h: FrankHandler) parms initialState = eval (h parms) initialState

[<AutoOpen>]
module Routing =
  open System.Text.RegularExpressions
  open FSharp.Monad.Maybe
  open Core

  /// A route for a Frank application.
  type Route = { Method     : string
                 Path       : string
                 Keys       : seq<string>
                 Conditions : seq<HttpRequestMessage -> bool>
                 Handler    : FrankHandler }

  /// Pulled from <see href="http://www.markhneedham.com/blog/2009/05/10/f-regular-expressionsactive-patterns/" />.
  let (|Match|_|) pattern input =
    let m = Regex.Match(input, pattern) in
    if m.Success then Some(List.tail [ for g in m.Groups -> g.Value ] |> List.toSeq) else None

  /// Matches a target HTTP method or any method.
  let matchMethod target = function m when m = "*" || m = target -> Some(target) | _ -> None 

  /// Matches a url from the path specification.
  let matchUrl path = function Match path result -> Some(result) | _ -> None

  /// Matches a route and returns an option containing the params and handler. 
  let (|Route|_|) (route:Route, request:HttpRequestMessage) = maybe {
    // Ensure the method matches the request.
    let! _ = matchMethod route.Method request.Method
    // Match the request path and retrieve the parameters from the Uri match groups.
    // The bind here is required to ensure the url path matches, as well as for retrieving the matching groups.
    let! matches = matchUrl route.Path request.Uri.AbsolutePath
    // Push the matches into a dictionary along with their keys.
    let parms = Dictionary<string, string>() in Seq.zip route.Keys matches |> Seq.iter parms.Add
    return (request, route.Handler, parms :> IDictionary<string, string>) }

  /// Returns the path along with the expected match keys.
  let private compile path =
    // Find all Regex match groups and add the group names as the keys.
    let namedGroups = "\?\<([A-Za-z0-9_]+)\>"
    let matches = Regex.Matches(path, namedGroups)
    let keys = seq {
      for m in matches do
        // Don't forget to trim the initial match from the groups.
        yield! List.tail [ for g in m.Groups -> g.Value ] |> List.toSeq }
    (path, keys)

  /// Creates a route object for a given HTTP method, path pattern, and Handler.
  let private route httpMethod path handler =
    let pattern, keys = compile path
    {Method = httpMethod; Path = pattern; Keys = keys; Conditions = Seq.empty; Handler = handler}

  let map path handler = route "*" path handler
  let get path handler = route "GET" path handler
  let head path handler = route "HEAD" path handler
  let put path handler = route "PUT" path handler
  let post path handler = route "POST" path handler
  let delete path handler = route "DELETE" path handler
  let options path handler = route "OPTIONS" path handler

[<AutoOpen>]
module FrankApp =
  open Frack
  open Core
  open Routing

  /// Initializes a Frank application with the specified sequence of routes.
  let init (routes: seq<Route>) =
    // TODO: Subscribe handlers to a request received event instead?
    // TODO: If a given route returns a not found, can you continue, or fall through, to another route?
    // Set a default not found response message.
    let notFound = new HttpResponseMessage(StatusCode = HttpStatusCode.NotFound)

    // Matches the route and request to a registered route.
    let fromRoutes = function Route res -> Some(res) | _ -> None

    // TODO: Move this into a functioning Frack helper.
    let (|/) (split:char) (input:string) =
      if input |> isNotNullOrEmpty then
        let p = input.Split(split) in (p.[0], if p.Length > 1 then p.[1] else "")
      else ("","") // This should never be reached but has to be here to satisfy the return type.
    
    // TODO: Move this into a functioning Frack helper.
    let parseQueryString qs =
      if isNotNullOrEmpty qs then
        let query = qs.Trim('?')
        // Using helpers from Frack.
        query.Split('&') |> Seq.filter isNotNullOrEmpty |> Seq.map ((|/) '=')
      else Seq.empty

    // Executes the handler on the current state of the application.
    let toResponse (request:HttpRequestMessage, handler, parms) =
      // TODO: Pull url-form-encoded values, if any, off the request and stick them in the parms' dictionary.
      let parms' =
        seq {
          yield! parms |> Dict.toSeq
          yield! request.Uri.Query |> parseQueryString
        } |> dict
      run handler parms' (request, notFound)

    // Define the delegate that defines the Frank application. 
    let app = Func<_,_>(fun request ->
      // Find the first matching response. 
      routes
      |> Seq.map (fun route -> (route, request))
      |> Seq.choose fromRoutes
      |> Seq.map toResponse
      |> Seq.head )
    app
