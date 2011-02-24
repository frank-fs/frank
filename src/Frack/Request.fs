namespace Frack
open System
open System.Collections.Generic
    
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
    // This delegate reads the remaining AsyncSeq and processes the current value retrieved by the next function.
    async {
      let! res = !asyncRead
      return next res }

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
    let requestBody = req?RequestBody :?> Async<ArraySegment<byte>>
    // Accumulate the request body into a list of ArraySegments.
    listify requestBody

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