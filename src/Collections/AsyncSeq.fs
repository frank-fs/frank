namespace Frack.Collections

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
