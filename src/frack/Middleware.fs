namespace Frack
module Middleware =
  open System
  open System.Collections.Generic
  open Env
  open Utility

  let printEnvironment app =
    fun (env:IDictionary<string,Value>) ->
      let status, hdrs, body = app env
      let vars = seq { for key in env.Keys do
                         let value = match env.[key] with
                                     | Str(v) -> v
                                     | Int(v) -> v.ToString()
                                     | Hash(v) -> v.ToString()
                                     | Err(v) -> v.ToString()
                                     | Inp(v) -> v.ToString()
                                     | Ver(v) -> v.[0].ToString() + "." + v.[1].ToString()
                                     | Obj(v) -> v.ToString()
                         yield key + ": " + value }
      let bd = seq { yield! body; yield! vars }
               |> Seq.filter (fun v -> not(String.IsNullOrEmpty(v)))
      ( status, hdrs, bd )
