namespace Frack
open System
open System.Collections.Generic

[<AbstractClass>]
type Owin() =
  /// Creates an OWIN application from an Async computation.
  static member FromAsync(handler, ?cancellationToken) =
    Action<IDictionary<string, obj>, Action<string, #IDictionary<string, string>, seq<obj>>, Action<exn>>(
      fun request onCompleted onError ->
        Async.StartWithContinuations(handler request, onCompleted.Invoke, onError.Invoke, onError.Invoke,
          ?cancellationToken = cancellationToken))

  /// Transforms an OWIN application into an Async computation.
  static member ToAsync(app: Action<IDictionary<string, obj>, Action<_,_,_>, Action<_>>) = fun req ->
    Async.FromContinuations(fun (cont, econt, _) ->
      app.Invoke(req, Action<_,_,_>(fun a b c -> cont(a,b,c)), Action<_>(econt)))
    
module Request =
  /// Reads the request body into a buffer and invokes the onCompleted callback with the buffer.
  let readBody (request: IDictionary<string, obj>) =
    let requestBody = request?RequestBody :?> Action<Action<ArraySegment<byte>>, Action<exn>>
    let nextSegment = Async.FromContinuations(fun (cont, econt, _) -> requestBody.Invoke(Action<_>(cont), Action<_>(econt)))
    let rec loop acc = async {
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
    loop id
