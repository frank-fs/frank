#nowarn "77"
namespace Frack
open System
open System.Collections.Generic
open Collections

[<AutoOpen>]
module Utility =

  let isNullOrEmpty = String.IsNullOrEmpty
  let isNotNullOrEmpty = not << String.IsNullOrEmpty

  /// Dynamic indexer lookups.
  /// <see href="http://codebetter.com/blogs/matthew.podwysocki/archive/2010/02/05/using-and-abusing-the-f-dynamic-lookup-operator.aspx" />
  let inline (?) this key = ( ^a : (member get_Item : ^b -> ^c) (this,key))
  let inline (?<-) this key value = ( ^a : (member set_Item : ^b * ^c -> ^d) (this,key,value))

  /// Generic duck-typing operator.
  /// <see href="http://weblogs.asp.net/podwysocki/archive/2009/06/11/f-duck-typing-and-structural-typing.aspx" />
  let inline implicit arg = ( ^a : (static member op_Implicit : ^b -> ^a) arg)

  /// Decodes url encoded values.
  let decodeUrl input = Uri.UnescapeDataString(input)

  /// Splits a relative Uri string into the path and query string.
  let splitUri uri =
    if uri |> isNullOrEmpty then ("/", "")
    else let arr = uri.Split([|'?'|])
         let path = if arr.[0] = "/" then "/" else arr.[0].TrimEnd('/')
         let queryString = if arr.Length > 1 then arr.[1] else ""
         (path, queryString)

  /// Splits a status code into the integer status code and the string status description.
  let splitStatus status =
    if status |> isNullOrEmpty then (200, "OK")
    else let arr = status.Split([|' '|])
         let code = int arr.[0]
         let description = if arr.Length > 1 then arr.[1] else "OK"
         (code, description)

  /// Creates a tuple from the first two values returned from a string split on the specified split character.
  let private (|/) (split:char) (input:string) =
    if input |> isNullOrEmpty then ("","") // Should never occur.
    else let p = input.Split(split) in (p.[0], if p.Length > 1 then p.[1] else "")
  
  /// Parses the url encoded string into a seq<string * string>
  let private parseUrlEncodedString input =
    if input |> isNullOrEmpty then Dict.empty
    else let data = decodeUrl input
         data.Split('&')
         |> Seq.filter isNotNullOrEmpty
         |> Seq.map ((|/) '=')
         |> dict
    
  /// Parses the query string into a seq<string * string>.
  let parseQuery = parseUrlEncodedString
   
  /// Parses the input stream for x-http-form-urlencoded values into a seq<string * string>.
  let parseForm = System.Text.Encoding.UTF8.GetString >> parseUrlEncodedString

module Stream =

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

type Body =
  | Sequence of Body seq
  | Bytes of byte[]
  | Str of string
  | Segment of ArraySegment<byte>

module Response =
  open System.IO
  open System.Text
  open Stream

  let rec getBytes item =
    match item with
    | Sequence it -> seq { for i in it do yield! getBytes i } |> Seq.toArray
    | Bytes bs -> bs
    | Str str -> str |> Encoding.UTF8.GetBytes
    | Segment seg -> seg.Array |> Seq.skip seg.Offset |> Seq.take seg.Count |> Seq.toArray // Need to determine whether this is better than Array.Copy.

  let write (stream: System.IO.Stream) item =
    let bytes = getBytes item
    stream.Write(bytes, 0, bytes.Length)
