#nowarn "77"
namespace Frack
open System
open System.Collections.Generic

[<AutoOpen>]
[<System.Runtime.CompilerServices.Extension>]
module Collections =

  /// Extensions to the Array module.
  module Array =
    open System.Collections.Generic
  
    /// Slices out a portion of the array from the start index up to the stop index.
    let slice start stop (source:'a[]) =
      let stop' = ref stop
      if !stop' < 0 then stop' := source.Length + !stop'
      let len = !stop' - start
      [| for i in [0..(len-1)] do yield source.[i + start] |]
    
  [<System.Runtime.CompilerServices.Extension>]
  let Slice(arr, start, stop) = Array.slice start stop arr

  /// Represents a sequence of values 'T where items 
  /// are generated asynchronously on-demand
  /// http://fssnip.net/1k
  type AsyncSeq<'a> = Async<AsyncSeqInner<'a>>
  and AsyncSeqInner<'a> =
    | Ended
    | Item of 'a * AsyncSeq<'a>
    
  module AsyncSeq =
    open System
    open System.IO
    
    /// Read stream 'stream' in blocks of size 'size'
    /// (returns on-demand asynchronous sequence)
    let readInBlocks buffer size (stream: Stream) = async {
      let rec nextBlock offset = async {
        let! count = stream.AsyncRead(buffer, offset, size)
        if count = 0 then return Ended
        else
          let res =
            if count = size then ArraySegment<_>(buffer, offset, size)
            else ArraySegment<_>(buffer, offset, count)
          return Item(res, nextBlock (offset + size)) }
      return! nextBlock 0 }
    
    /// Asynchronous function that greedily creates a Seq from an AsyncSeq.
    let toSeq aseq =
      let rec read cont aseq = async {
        let! item = aseq
        match item with
        | Ended -> return cont []
        | Item(hd, tl) -> return! read (fun rest -> hd::rest |> cont) tl }
      read List.toSeq aseq
  
    /// Asynchronous function that compares two asynchronous sequences
    /// item by item. If an item doesn't match, 'false' is returned
    /// immediately without generating the rest of the sequence. If the
    /// lengths don't match, exception is thrown.
    let rec compareAsyncSeqs seq1 seq2 = async {
      let! item1 = seq1
      let! item2 = seq2
      match item1, item2 with 
      | Item(b1, ns1), Item(b2, ns2) when b1 <> b2 -> return false
      | Item(b1, ns1), Item(b2, ns2) -> return! compareAsyncSeqs ns1 ns2
      | Ended, Ended -> return true
      | _ -> return failwith "Size doesn't match" }

  /// Extensions to dictionaries.
  module Dict =
    open System.Collections.Generic
    let empty<'a, 'b when 'a : equality> : IDictionary<'a, 'b> = dict Seq.empty
    let toSeq d = d |> Seq.map (fun (KeyValue(k,v)) -> (k,v))
    let toArray (d:IDictionary<_,_>) = d |> toSeq |> Seq.toArray
    let toList (d:IDictionary<_,_>) = d |> toSeq |> Seq.toList

  [<AutoOpen>]
  module NameValueCollectionEx =
    /// Extends NameValueCollection with methods to transform it to an enumerable, map or dictionary.
    type System.Collections.Specialized.NameValueCollection with
      member this.AsEnumerable() = seq { for key in this.Keys do yield (key, this.[key]) }
      member this.ToDictionary() = this.AsEnumerable() |> dict 
      member this.ToMap() = this.AllKeys |> Array.fold (fun h k -> Map.add k this.[k] h) Map.empty

  /// Extends NameValueCollection with methods to transform it to an enumerable.
  [<System.Runtime.CompilerServices.Extension>]
  let AsEnumerable(this:System.Collections.Specialized.NameValueCollection) = this.AsEnumerable()
    
  /// Extends NameValueCollection with methods to transform it to a dictionary.
  [<System.Runtime.CompilerServices.Extension>]
  let ToDictionary(this:System.Collections.Specialized.NameValueCollection) = this.ToDictionary()

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
