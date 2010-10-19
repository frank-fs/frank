namespace Frank
open System
open System.Collections.Generic
open System.Text.RegularExpressions
open Frack

module Dict =
  let toSeq (d:IDictionary<_,_>) = d |> Seq.map (fun (KeyValue(k,v)) -> (k,v))
  let toArray (d:IDictionary<_,_>) = d |> toSeq |> Seq.toArray
  let toList (d:IDictionary<_,_>) = d |> toSeq |> Seq.toList

type Agent<'T> = MailboxProcessor<'T>

/// Defines available response types.
type FrankResponse =
  | Response of Frack.Response
  | Object of Frack.Value 
  | NotFound

[<AutoOpen>]
module Utility =
  let read value = match value with | Str(v) -> v | _ -> value.ToString()

  /// Pulled from <see href="http://www.markhneedham.com/blog/2009/05/10/f-regular-expressionsactive-patterns/" />.
  let (|Match|_|) pattern input =
    let m = Regex.Match(input, pattern) in
    if m.Success then Some(List.tail [ for g in m.Groups -> g.Value ]) else None

/// A route for a Frank applicaiton.
type Route = { Pattern: Regex
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
type FrankApp(routes: seq<string * Route>) =
  // Using a mutable dictionary here since we are only creating this once at the start of a FrankApp.
  let router = routes
               |> Seq.groupBy fst
               |> Seq.map (fun (key, values) -> (key, values |> Seq.map snd))
               |> dict

  let parseResponse response =
    match response with
    | Response(status, hdrs, body) -> (status, hdrs, body)
    | Object(obj) -> let content = match obj with
                                   | Str(value) -> value
                                   | _ -> obj.ToString()
                     (200, dict[("Content_Length", content.Length.ToString())], seq { yield content })
    | _ -> (404, dict[("Content_Length", "9")], seq { yield "Not found" })

  /// Finds the appropriate handler from the router and invokes it.
  member this.Invoke(request: Frack.Request) =
    let httpMethod = read request?HTTP_METHOD
    let response =
      if router.ContainsKey(httpMethod) then
        let path = read request?SCRIPT_NAME + "/" + read request?PATH_INFO
        router.[httpMethod]
        |> Seq.filter (fun r -> r.Pattern.IsMatch(path))
        |> Seq.map (fun r -> 
             let values = dict [| for c in r.Pattern.Match(path).Captures do
                                    yield (path.Substring(c.Index, c.Length), c.Value) |]
             r.Handler(values))
        |> Seq.head  // What happens if the list is empty?
      else
        NotFound
    parseResponse response
