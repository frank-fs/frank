namespace Frank
open System
open System.Collections.Generic
open System.Text.RegularExpressions
open Frack

type Agent<'T> = MailboxProcessor<'T>

/// Defines available response types.
type FrankResponse =
  | Response of int * IDictionary<string, string> * seq<string>
  | Object of Frack.Value 
  | NotFound

[<AutoOpen>]
module Utility =
  let read value = match value with | Str(v) -> v | _ -> value.ToString()

  /// Pulled from <see href="http://www.markhneedham.com/blog/2009/05/10/f-regular-expressionsactive-patterns/" />.
  let (|Match|_|) pattern input =
    let m = Regex.Match(input, pattern) in
    if m.Success then Some(List.tail [ for g in m.Groups -> g.Value ]) else None

type FrankRoute = { Pattern: Regex
                    Keys: seq<string>
                    Conditions: seq<IDictionary<string, string> -> bool>
                    Handler: IDictionary<string, string> -> FrankResponse }

[<AutoOpen>]
module Routing =
  open Frack.Utility

  let private compile path =
    // Need to finish this method.
    (Regex(path), Seq.empty<string>)

  let private route httpMethod path handler =
    let pattern, keys = compile path
    let conditions = Seq.empty<IDictionary<string, string> -> bool>
    (httpMethod, {Pattern = pattern; Keys = keys; Conditions = conditions; Handler = handler})

  let get path handler = route "GET" path handler
  let head path handler = route "HEAD" path handler
  let put path handler = route "PUT" path handler
  let post path handler = route "POST" path handler
  let delete path handler = route "DELETE" path handler
  let options path handler = route "OPTIONS" path handler

/// Defines the standard Frank application type.
type FrankApp(routes: seq<string * FrankRoute>) =
  let routeAdded = new Event<string * FrankRoute>()
  let routeAddedEvent = routeAdded.Publish

  // Using a mutable dictionary here since we are only creating this once at the start of a FrankApp.
  let router = Dictionary<string, seq<FrankRoute>>()
  do for httpMethod, route in routes do
       router?httpMethod <- seq { if router.ContainsKey(httpMethod) then yield! router?httpMethod
                                  yield route }
       routeAdded.Trigger(httpMethod, route)

  let parseResponse response =
    match response with
    | Response(status, hdrs, body) -> (status, hdrs, body)
    | Object(obj) -> let content = match obj with
                                   | Str(value) -> value
                                   | _ -> obj.ToString()
                     (200, dict[("Content_Length", content.Length.ToString())], seq { yield content })
    | _ -> (404, dict[("Content_Length", "9")], seq { yield "Not found" })

  /// Finds the appropriate handler from the router and invokes it.
  member this.Call(env: IDictionary<string, Value>) =
    let httpMethod = read env?HTTP_METHOD
    let response =
      if router.ContainsKey(httpMethod) then
        let path = read env?SCRIPT_NAME + "/" + read env?PATH_INFO
        router?httpMethod
        |> Seq.filter (fun r -> r.Pattern.IsMatch(path))
        |> Seq.map (fun r -> 
             let values = dict [| for c in r.Pattern.Match(path).Captures do
                                    yield (path.Substring(c.Index, c.Length), c.Value) |]
             r.Handler(values))
        |> Seq.head  // What happens if the list is empty?
      else NotFound
    parseResponse response

  /// Allows an extension point to the point at which a route has been wired up.
  member this.Subscribe(observer) = routeAddedEvent.Subscribe(observer)
  interface IObservable<string * FrankRoute> with
    member this.Subscribe(observer) = routeAddedEvent.Subscribe(observer)