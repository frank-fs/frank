namespace Frack

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
