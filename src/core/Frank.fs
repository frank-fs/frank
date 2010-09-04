namespace Frank
open System.Collections.Generic
open Frack
open Frack.Utility

module Filters =
  /// Creates a filter that constrains by the HTTP method. If the method is a POST, it also checks for X_HTTP_METHOD_OVERRIDE.
  let methodFilter m (env: IDictionary<string, Value>) =
    let read value = match value with | Str(x) -> x | _ -> ""
    let httpMethod = read env?HTTP_METHOD
    if httpMethod = "POST" && env.ContainsKey("X_HTTP_METHOD_OVERRIDE") then m = read env?X_HTTP_METHOD_OVERRIDE
    else m = httpMethod

type FrankResponse =
  | Response of int * IDictionary<string, string> * seq<string>
  | Value of Frack.Value 
  | None

/// Interface for Frank handlers, including the application type.
type IFrankHandler =
  abstract Call : IDictionary<string, Value> -> FrankResponse

/// Maps a handler to a constraint against the incoming request and returns the response as an option when it is a match.
type Map(filter, handler) =
  interface IFrankHandler with
    member this.Call(env) = if filter(env) then Value(handler(env)) else None
      
type Get(filter, handler) =
  inherit Map((Filters.methodFilter "GET") << filter, handler)

type Head(filter, handler) =
  inherit Map((Filters.methodFilter "HEAD") << filter, handler)

type Post(filter, handler) =
  inherit Map((Filters.methodFilter "POST") << filter, handler)

type Put(filter, handler) =
  inherit Map((Filters.methodFilter "PUT") << filter, handler)

type Delete(filter, handler) =
  inherit Map((Filters.methodFilter "DELETE") << filter, handler)

type Options(filter, handler) =
  inherit Map((Filters.methodFilter "OPTIONS") << filter, handler)

/// Defines the standard Frank application type.
type FrankApp(handlers: seq<IFrankHandler>) =
  interface IFrankHandler with
    member this.Call(env) = handlers
                            |> Seq.map (fun h -> h.Call(env))
                            |> Seq.filter (fun r -> not (r = None))
                            |> Seq.head

module Frank =
  let run (app: IFrankHandler) (env: IDictionary<string, Value>) =
    match app.Call(env) with
    | Response(status, hdrs, body) -> Response(status, hdrs, body)
    | Value(obj) -> let content = match obj with
                                  | Str(value) -> value
                                  | _ -> obj.ToString() // TODO: Continue adding matchers. 
                    Response(200, dict[("Content_Length", content.Length.ToString())], seq { yield content })
    | _ -> Response(404, dict[("Content_Length", "9")], seq { yield "Not found" })
