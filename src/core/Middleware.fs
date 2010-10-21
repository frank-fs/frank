namespace Frack
open System

module Middleware =
  let printRequest (app: App) = fun request ->
    let status, hdrs, body = app.Invoke(request)
    let vars = seq { for key in request.Keys do
                       let value = match request.[key] with
                                   | Str(v) -> v
                                   | Int(v) -> v.ToString()
                                   | Err(v) -> v.ToString()
                                   | Inp(v) -> v.ToString()
                                   | Ver(v) -> v.[0].ToString() + "." + v.[1].ToString()
                       yield key + " => " + value }
               |> Seq.filter (fun v -> String.IsNullOrEmpty(v))
               |> Seq.map (ByteString.fromString)
    let bd = seq { yield! body; yield! vars }
    ( status, hdrs, bd )
