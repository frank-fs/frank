namespace Frank
open System.Collections.Generic
open System.Text.RegularExpressions
open Frack

type Agent<'T> = MailboxProcessor<'T>

/// Defines available response types.
type FrankResponse =
  | Response of int * IDictionary<string, string> * seq<string>
  | Object of Frack.Value 
  | None

/// Defines a route.
type FrankRoute = { Pattern: System.Text.RegularExpressions.Regex
                    Keys: seq<string>
                    Conditions: seq<unit -> bool>
                    Handler: unit -> FrankResponse }

[<AutoOpen>]
module Core =
  let private read value = match value with | Str(x) -> x | _ -> ""

  /// Creates a path that constrains by the HTTP method. If the method is a POST, it also checks for X_HTTP_METHOD_OVERRIDE.
  let private methodFilter m (env: IDictionary<string, Value>) =
    let httpMethod = read env?HTTP_METHOD
    if httpMethod = "POST" && env.ContainsKey("X_HTTP_METHOD_OVERRIDE") then m = read env?X_HTTP_METHOD_OVERRIDE
    else m = httpMethod

[<AutoOpen>]
module Routing =
  open System
  open System.Collections.Generic
  open System.Text.RegularExpressions
  open Frack
  open Frack.Utility
  open Core

  type RouteAddedEventArgs(httpMethod, FrankRoute) =
    inherit EventArgs()

  /// An event to be triggered when a route is added.
  let routeAdded = new Event<RouteAddedEventArgs>()

  /// The router agent for managing consistent state of the routes.
  let router =
    Agent<string * FrankRoute>.Start(fun inbox ->
      let rec loop() = async {
        let! httpMethod, route = inbox.Receive()
        return! loop() }
      loop() )

  /// Helper for posting to an Agent.
  let inline (<--) (m:MailboxProcessor<_>) msg = m.Post(msg)

  let private compile path =
    (Regex(path), [])

  let private route httpMethod path handler =
    let pattern, keys = compile path
    (httpMethod, { Pattern = pattern; Keys = keys; Conditions = [||]; Handler = handler })

  let get path handler = route "GET" path handler
  let head path handler = route "HEAD" path handler
  let put path handler = route "PUT" path handler
  let post path handler = route "POST" path handler
  let delete path handler = route "DELETE" path handler
  let options path handler = route "OPTIONS" path handler

/// Defines the standard Frank application type.
type FrankApp(routes: seq<string * FrankRoute>) =
  let router = Dictionary<string, seq<FrankRoute>>()
  do for httpMethod, route in routes do
       router?httpMethod <- seq { if router.ContainsKey(httpMethod) then yield! router?httpMethod
                                  yield route }
       routeAdded.Trigger(RouteAddedEventArgs(httpMethod, route))

  member this.Call(env) = Object(Str("In progress")) 

[<AutoOpen>]
module Runner =
  let run (app: FrankApp) (env: IDictionary<string, Value>) =
    match app.Call(env) with
    | Response(status, hdrs, body) -> (status, hdrs, body)
    | Object(obj) -> let content = match obj with
                                   | Str(value) -> value
                                   | _ -> obj.ToString()
                     (200, dict[("Content_Length", content.Length.ToString())], seq { yield content })
    | _ -> (404, dict[("Content_Length", "9")], seq { yield "Not found" })