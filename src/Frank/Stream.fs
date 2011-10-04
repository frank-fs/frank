﻿module Frank.Stream

open System

/// Converts the input stream into an AsyncSeq so that it can be read in chunks asynchronously.
let chunk input =
  // Create the AsyncSeq and store it in a ref cell.
  // The ref cell will allow us to update the function to read only the remaining values.
  let buffer = Array.zeroCreate (2 <<< 16)
  let asyncRead = input |> AsyncSeq.readInBlocks buffer 1024 |> ref
  // The next function allows us to pull out the next byte[] for use in the continuation.
  let next = function
    | Ended -> ArraySegment<_>(Array.empty)
    | Item(hd, tl) -> asyncRead := tl; hd
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

/// Reads the entire request body into a byte[].
let readToEnd (input: Async<ArraySegment<_>>) = async {
  let! chunks = listify input 
  // Determine the total number of bytes read.
  let length = chunks |> List.fold (fun len chunk -> len + chunk.Count) 0 
  // Read the contents of the body segments into a local buffer.
  let buffer, _ = chunks |> List.fold (fun (bs, offset) chunk ->
    Buffer.BlockCopy(chunk.Array, chunk.Offset, bs, offset, chunk.Count)
    (bs, offset + chunk.Count)) (Array.create length 0uy, 0)
  return buffer }