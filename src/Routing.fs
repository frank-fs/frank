namespace Frank
[<AutoOpen>]
module Routing =
  open System
  open System.Collections.Generic
  open System.Net
  open Microsoft.Http
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
