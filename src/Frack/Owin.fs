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
      app.Invoke(req, Action<_,_,_>(fun st hd bd -> cont(st, hd, bd)), Action<_>(econt)))
    
[<System.Runtime.CompilerServices.Extension>]
module Request =
  /// Reads all segments from the request body into a list.
  let readBody (req: IDictionary<string, obj>) =
    let requestBody = req?RequestBody :?> Action<Action<ArraySegment<byte>>, Action<exn>>
    let nextSegment = Async.FromContinuations(fun (cont, econt, _) ->
      requestBody.Invoke(Action<_>(cont), Action<_>(econt)))
    let rec loop acc = async {
      let! chunk = nextSegment
      if chunk.Count = 0 then
        // Invoke the continuation with an empty list.
        // This will cause the ArraySegment<byte> list to be created in order.
        return acc []
      // We append the last call as the tail of the previous call.
      else return! loop (fun chunks -> chunk::chunks) }
    loop id

  /// Reads the entire request body into a byte[].
  let readToEnd (req: IDictionary<string, obj>) = async {
    let! chunks = readBody req
    // Determine the total number of bytes read.
    let length = chunks |> List.fold (fun len chunk -> len + chunk.Count) 0 
    // Read the contents of the body segments into a local buffer.
    let buffer, _ = chunks |> List.fold (fun (bs, offset) chunk ->
      Buffer.BlockCopy(chunk.Array, chunk.Offset, bs, offset, chunk.Count)
      (bs, offset + chunk.Count)) ((Array.create length 0uy), 0)
    return buffer }

  /// Reads the body of the request as a callback accepting
  /// a callback taking an ArraySegment and an error callback.
  [<System.Runtime.CompilerServices.Extension>]
  let ReadToEnd (req: IDictionary<string, obj>, onCompleted, onError) =
    let read = Action<Action<_>, Action<_>>(fun cont econt ->
      try
        Async.StartWithContinuations(readToEnd req, cont.Invoke, econt.Invoke, econt.Invoke)
      with e -> econt.Invoke(e))
    read.Invoke(onCompleted, onError)
