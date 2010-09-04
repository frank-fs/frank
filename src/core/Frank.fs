module Frank
open System.Collections.Generic
open Frack
open Frack.Utility

let private methodFilter m (env: IDictionary<string, Value>) =
  let read value = match value with | Str(x) -> x | _ -> ""
  let httpMethod = read env?HTTP_METHOD
  if httpMethod = "POST" && env.ContainsKey("X_HTTP_METHOD_OVERRIDE") then m = read env?X_HTTP_METHOD_OVERRIDE
  else m = httpMethod

/// Maps a handler to a constraint against the incoming request and returns the response as an option when it is a match.
let map filter handler env = if filter(env) then Some(handler(env)) else None
let get filter handler env = map (methodFilter "GET") << filter handler env
let post filter handler env = map (methodFilter "POST") << filter handler env
let put filter handler env = map (methodFilter "PUT") << filter handler env
let delete filter handler env = map (methodFilter "DELETE") << filter handler env

type FrankApp(handlers) =
  member x.Call(env: IDictionary<string, Value>) =
    let response = seq {
      for handler in handlers do
        match handler(env) with
        | Some(response) -> yield response
        | _ -> yield null } |> Seq.filter (fun o -> not (o = null)) |> Seq.head
    if response = null then (404, dict[("Content_Length", 9)], seq { yield "Not found" })
    else
      let content = response.ToString()
      (200, dict[("Content_Length", content.Length)], seq { yield content }) 