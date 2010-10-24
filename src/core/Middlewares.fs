namespace Frack
open System

module Middlewares =
  let printRequest (app: App) = fun request ->
    let status, hdrs, body = app.Invoke(request)
    let vars = seq { for key in request.Keys do yield key + " => " + read request.[key] }
               |> Seq.filter isNotNullOrEmpty
               |> Seq.map ByteString.fromString
    let bd = seq { yield! body; yield! vars }
    ( status, hdrs, bd )
