namespace Frack
open System
open System.Collections.Generic

module Owin =
  /// Creates an OWIN application from an Async computation.
  let create handler cancellationToken =
    Action<#IDictionary<string, #obj>, Action<string, #IDictionary<string, string>, #seq<#obj>>, Action<exn>>(
      fun request onCompleted onError ->
        Async.StartWithContinuations(handler request, onCompleted.Invoke, onError.Invoke, onError.Invoke,
          cancellationToken = cancellationToken))
    
module Request =
  open FSharp.Monad

  /// Reads the request body into a buffer and invokes the onCompleted callback with the buffer.
  let readBody onCompleted onError (requestBody: obj) =
    let requestBody = requestBody :?> Action<Action<ArraySegment<byte>>, Action<exn>>
    // We rely on the underlying implementation to return and perform the asynchronous retrieval.
    let nextSegment = Cont(fun onSeg -> requestBody.Invoke(Action<_>(onSeg), onError))
    let rec loop acc = cont {
      let! chunk = nextSegment
      if chunk.Count = 0 then
        // Invoke the continuation with an empty list.
        // This will cause the ArraySegment<byte> list to be created in order.
        let chunks: ArraySegment<_> list = acc []
        // Determine the total number of bytes read.
        let length    = chunks |> List.fold (fun len chunk -> len + chunk.Count) 0 
        // Read the contents of the body segments into a local buffer.
        let buffer, _ = chunks |> List.fold (fun (bs, offset) chunk ->
          Buffer.BlockCopy(chunk.Array, chunk.Offset, bs, offset, chunk.Count)
          (bs, offset + chunk.Count)) ((Array.create length 0uy), 0)
        return buffer
      // We append the last call as the tail of the previous call.
      else return! loop (fun chunks -> chunk::chunks) }
    runCont (loop id) onCompleted 

  /// Reads the request body as x-http-form-urlencoded.
  let readAsFormUrlEncoded onCompleted onError requestBody =
    readBody (UrlEncoded.parseForm >> onCompleted) onError requestBody
