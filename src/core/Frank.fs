module Frank
open System.Collections.Generic
open Frack
open Frack.Utility

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

/// Defines the standard Frank application type.
type FrankApp(handlers: seq<IFrankHandler>) =
  interface IFrankHandler with
    member this.Call(env) = handlers
                            |> Seq.map (fun h -> h.Call(env))
                            |> Seq.filter (fun r -> not (r = None))
                            |> Seq.head

let private read value = match value with | Str(x) -> x | _ -> ""

/// Creates a path that constrains by the HTTP method. If the method is a POST, it also checks for X_HTTP_METHOD_OVERRIDE.
let private methodFilter m (env: IDictionary<string, Value>) =
  let httpMethod = read env?HTTP_METHOD
  if httpMethod = "POST" && env.ContainsKey("X_HTTP_METHOD_OVERRIDE") then m = read env?X_HTTP_METHOD_OVERRIDE
  else m = httpMethod

let private matchPath path (env: IDictionary<string, Value>) =
  path = read env?SCRIPT_NAME + "/" + read env?PATH_INFO

/// Function to create a map in a more readable format.      
let map path handler = Map((fun e -> matchPath path e), handler) :> IFrankHandler

// TODO: What's a better way to compose predicates?
// Should filters go into a route table, or is this the best approach?
let get path handler = Map((fun e -> methodFilter "GET" e && matchPath path e), handler) :> IFrankHandler
let head path handler = Map((fun e -> methodFilter "HEAD" e && path e), handler) :> IFrankHandler
let post path handler = Map((fun e -> methodFilter "POST" e && path e), handler) :> IFrankHandler
let put path handler = Map((fun e -> methodFilter "PUT" e && path e), handler) :> IFrankHandler
let delete path handler = Map((fun e -> methodFilter "DELETE" e && path e), handler) :> IFrankHandler
let options path handler = Map((fun e -> methodFilter "OPTIONS" e && path e), handler) :> IFrankHandler

/// Runs the app and returns a Frack response.
let run (app: IFrankHandler) (env: IDictionary<string, Value>) =
  match app.Call(env) with
  | Response(status, hdrs, body) -> (status, hdrs, body)
  | Value(obj) -> let content = match obj with
                                | Str(value) -> value
                                | _ -> obj.ToString() // TODO: Continue adding matchers. 
                  (200, dict[("Content_Length", content.Length.ToString())], seq { yield content })
  | _ -> (404, dict[("Content_Length", "9")], seq { yield "Not found" })
