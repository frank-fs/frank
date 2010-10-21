namespace Frank
open System
open System.Collections.Generic
open System.Text.RegularExpressions
open Frack

module Dict =
  let toSeq d = d |> Seq.map (fun (KeyValue(k,v)) -> (k,v))
  let toArray (d:IDictionary<_,_>) = d |> toSeq |> Seq.toArray
  let toList (d:IDictionary<_,_>) = d |> toSeq |> Seq.toList

/// Defines available response types.
type FrankResponse =
  | Obj of obj
  | Str of string
  | Int of int
  | Bool of bool
  | ByteStr of bytestring
  | Hash of IDictionary<string, obj>
  | Response of Frack.Response
  | Unauthorized
  | Forbidden
  | NotFound
  | InternalServerError

/// A route for a Frank applicaiton.
type Route = { Pattern: Regex
               Keys: seq<string>
               Conditions: seq<IDictionary<string, string> -> bool>
               Handler: IDictionary<string, string> -> FrankResponse }

[<AutoOpen>]
module Routing =
  open Frack.Utility

  /// Pulled from <see href="http://www.markhneedham.com/blog/2009/05/10/f-regular-expressionsactive-patterns/" />.
  let (|Match|_|) pattern input =
    let m = Regex.Match(input, pattern) in
    if m.Success then Some(List.tail [ for g in m.Groups -> g.Value ]) else None

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
    // TODO: Would additional Active Patterns help here?
    match response with
    | Obj(v) -> let content = ByteString.fromString (v.ToString())
                let contentLength = content |> Seq.length
                (200, dict[("Content_Length", contentLength.ToString())], seq { yield content })
    | Str(v) -> let content = ByteString.fromString v
                let contentLength = content |> Seq.length
                (200, dict[("Content_Length", contentLength.ToString())], seq { yield content })
    | Int(v) -> let content = ByteString.fromString (v.ToString())
                let contentLength = content |> Seq.length
                (200, dict[("Content_Length", contentLength.ToString())], seq { yield content })
    | Bool(v) -> let content = ByteString.fromString (v.ToString())
                 let contentLength = content |> Seq.length
                 (200, dict[("Content_Length", contentLength.ToString())], seq { yield content })
    | ByteStr(v) -> let contentLength = v |> Seq.length
                    (200, dict[("Content_Length", contentLength.ToString())], seq { yield v })
    | Response(status, hdrs, body) -> (status, hdrs, body)
    | Unauthorized -> (401, dict[("Content_Length", "12")], seq { yield ByteString.fromString "Unauthorized" })
    | Forbidden -> (403, dict[("Content_Length", "9")], seq { yield ByteString.fromString "Forbidden" })
    | InternalServerError -> (500, dict[("Content_Length", "21")], seq { yield ByteString.fromString "Internal Server Error" })
    | _ -> (404, dict[("Content_Length", "9")], seq { yield ByteString.fromString "Not found" })

  /// Finds the appropriate handler from the router and invokes it.
  member this.Invoke(request:Request) =
    let read value = match value with | Frack.Str(v) -> v | _ -> value.ToString()
    let httpMethod = read request?HTTP_METHOD
    let response =
      if router.ContainsKey(httpMethod) then
        let path = read request?SCRIPT_NAME + "/" + read request?PATH_INFO
        // TODO: Switch to the Match Active Pattern.
        router.[httpMethod]
        |> Seq.filter (fun r -> r.Pattern.IsMatch(path))
        |> Seq.map (fun r -> 
             let values = dict [| for c in r.Pattern.Match(path).Captures do
                                    yield (path.Substring(c.Index, c.Length), c.Value) |]
             try
               r.Handler(values)
             // TODO: Add additional error handling here, e.g. 401, 403, etc.
             with _ -> InternalServerError)
        |> Seq.head
      else NotFound
    parseResponse response
