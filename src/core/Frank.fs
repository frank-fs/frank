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

/// Function to create a map in a more readable format.      
let map filter handler = Map(filter, handler) :> IFrankHandler

/// Defines the standard Frank application type.
type FrankApp(handlers: seq<IFrankHandler>) =
  interface IFrankHandler with
    member this.Call(env) = handlers
                            |> Seq.map (fun h -> h.Call(env))
                            |> Seq.filter (fun r -> not (r = None))
                            |> Seq.head

/// Creates a filter that constrains by the HTTP method. If the method is a POST, it also checks for X_HTTP_METHOD_OVERRIDE.
let private methodFilter m (env: IDictionary<string, Value>) =
  let read value = match value with | Str(x) -> x | _ -> ""
  let httpMethod = read env?HTTP_METHOD
  if httpMethod = "POST" && env.ContainsKey("X_HTTP_METHOD_OVERRIDE") then m = read env?X_HTTP_METHOD_OVERRIDE
  else m = httpMethod

// TODO: What's a better way to compose predicates?
let get filter handler = map (fun e -> methodFilter "GET" e && filter e) handler
let head filter handler = map (fun e -> methodFilter "HEAD" e && filter e) handler
let post filter handler = map (fun e -> methodFilter "POST" e && filter e) handler
let put filter handler = map (fun e -> methodFilter "PUT" e && filter e) handler
let delete filter handler = map (fun e -> methodFilter "DELETE" e && filter e) handler
let options filter handler = map (fun e -> methodFilter "OPTIONS" e && filter e) handler

/// Runs the app and returns a Frack response.
let run (app: IFrankHandler) (env: IDictionary<string, Value>) =
  match app.Call(env) with
  | Response(status, hdrs, body) -> (status, hdrs, body)
  | Value(obj) -> let content = match obj with
                                | Str(value) -> value
                                | _ -> obj.ToString() // TODO: Continue adding matchers. 
                  (200, dict[("Content_Length", content.Length.ToString())], seq { yield content })
  | _ -> (404, dict[("Content_Length", "9")], seq { yield "Not found" })
