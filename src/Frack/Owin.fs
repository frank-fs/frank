namespace Frack
open System
open System.Collections.Generic

// Proposed: type ResponseBody = Action<Action<ArraySegment<byte>>, Action<exn>> seq
type Handler = Action<Action<string, IDictionary<string, string>, seq<obj>>, Action<exn>>
type App = Func<IDictionary<string, obj>, Handler>

[<AbstractClass>]
type Owin() =
  /// Creates an OWIN application from an Async computation.
  static member FromAsync(handler, ?cancellationToken) =
    App(fun request ->
      Action<_,_>(fun onCompleted onError ->
        Async.StartWithContinuations(handler request, onCompleted.Invoke, onError.Invoke, onError.Invoke,
          ?cancellationToken = cancellationToken)))

  /// Transforms an OWIN application into an Async computation.
  static member ToAsync(app:Func<_,Handler>) =
    fun request ->
      let handler = app.Invoke(request)
      Async.FromContinuations(fun (cont, econt, _) ->
        handler.Invoke(Action<_,_,_>(fun st hd bd -> cont(st, hd, bd)), Action<_>(econt)))
    
[<System.Runtime.CompilerServices.Extension>]
module Request =
  open Frack.Collections

  /// Converts the input stream into an AsyncSeq so that it can be read in chunks asynchronously.
  let chunk input =
    // Create the AsyncSeq and store it in a ref cell.
    // The ref cell will allow us to update the function to read only the remaining values.
    let asyncRead = input |> AsyncSeq.readInBlocks (fun bs -> ArraySegment<_>(bs)) 1024 |> ref
    // The next function allows us to pull out the ArraySegment<byte> for use in the continuation.
    let next = function
      | Ended ->
          // Return an empty array segment to indicate completion.
          ArraySegment<_>(Array.empty)
      | Item(hd, tl) ->
          // Replace the AsyncSeq with the remaining values.
          asyncRead := tl
          // Return the current ArraySegment<byte>.
          hd
    // Create an Action delegate matching the required OWIN signature.
    // This delegate reads the remaining AsyncSeq and processes the current value retrieved by the next function.
    Action<Action<_>, Action<_>>(fun cont econt ->
      try
        Async.StartWithContinuations(!asyncRead, next >> cont.Invoke, econt.Invoke, econt.Invoke)
      with e -> econt.Invoke(e))

  /// Creates a list of ArraySegment<_> from the Async.
  let listify (next: Async<ArraySegment<_>>) =
    let rec loop acc = async {
      let! chunk = next
      if chunk.Count = 0 then
        // Invoke the continuation with an empty list.
        // This will cause the ArraySegment<byte> list to be created in order.
        return acc []
      // We append the last call as the tail of the previous call.
      else return! loop (fun chunks -> chunk::chunks) }
    loop id

  /// Reads all segments from the request body into a list.
  let readBody (req: IDictionary<string, obj>) =
    // Get the request body callback.
    let requestBody = req?RequestBody :?> Action<Action<ArraySegment<byte>>, Action<exn>>
    // Convert the callback into an Async computation.
    let next = Async.FromContinuations(fun (cont, econt, _) ->
      requestBody.Invoke(Action<_>(cont), Action<_>(econt)))
    // Accumulate the request body into a list of ArraySegments.
    listify next

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
  [<Microsoft.FSharp.Core.CompiledName("ReadToEnd")>]
  let readToEndWithContinuations (req: IDictionary<string, obj>, onCompleted, onError) =
    let read = Action<Action<_>, Action<_>>(fun cont econt ->
      try
        Async.StartWithContinuations(readToEnd req, cont.Invoke, econt.Invoke, econt.Invoke)
      with e -> econt.Invoke(e))
    read.Invoke(onCompleted, onError)
